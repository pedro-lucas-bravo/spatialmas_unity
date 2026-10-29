using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using SpatialMAS.Core;
using UnityEngine;

namespace SpatialMAS.Logging
{
    /// <summary>Buffered physical-world recordings with synchronized samples and explicit calibration provenance.</summary>
    public class DataRecordingService : IDisposable
    {
        private const string SamplePrefix = "TimestampISO,ElapsedMs,SampleId,Frame,SessionId";
        private const string EmbodimentHeader = SamplePrefix +
            ",Scenario,Layout,XRMode,ActiveAgents,FrameTimeMs,CoordinatesValid,CalibrationId,CalibrationRevision,TrackingRevision" +
            ",ScenarioCenterX_m,ScenarioCenterY_m,ScenarioCenterZ_m,HeadTracked,HeadSource,HeadPosX_m,HeadPosY_m,HeadPosZ_m" +
            ",HeadRotX,HeadRotY,HeadRotZ,HeadRotW,LHandTracked,LHandPosX_m,LHandPosY_m,LHandPosZ_m,LHandRotX,LHandRotY,LHandRotZ,LHandRotW,LPinch" +
            ",RHandTracked,RHandPosX_m,RHandPosY_m,RHandPosZ_m,RHandRotX,RHandRotY,RHandRotZ,RHandRotW,RPinch" +
            ",TrackingOriginPosX_m,TrackingOriginPosY_m,TrackingOriginPosZ_m,TrackingOriginRotX,TrackingOriginRotY,TrackingOriginRotZ,TrackingOriginRotW" +
            ",TrackingOriginScaleX,TrackingOriginScaleY,TrackingOriginScaleZ,UserRepellerEnabled,UserRepellerRadius_m,UserRepellerStrength";
        private const string ObjectHeader = SamplePrefix +
            ",Kind,Id,Shape,PositionX_m,PositionY_m,PositionZ_m,RotationX,RotationY,RotationZ,RotationW" +
            ",DimensionsX_m,DimensionsY_m,DimensionsZ_m,MovementMode,Grabbed,Interactable,CoordinatesValid,CalibrationId,CalibrationRevision,TrackingRevision";
        private const string CalibrationHeader = "TimestampISO,ElapsedMs,SessionId,CalibrationId,Revision,PositionX_m,PositionY_m,PositionZ_m,RotationX,RotationY,RotationZ,RotationW,Visible,Locked";
        private const string EventsHeader = "TimestampISO,ElapsedMs,Frame,SessionId,EventType,PayloadJson";

        private readonly string _logsDirectory;
        private readonly SimulationCommandBus _bus;
        private readonly SpatialCalibrationReference _calibrationReference;
        private readonly SpatialMASConfig _config;
        private readonly object _lock = new object();
        private readonly Stopwatch _sessionStopwatch = new Stopwatch();
        private StreamWriter[] _writers = Array.Empty<StreamWriter>();
        private StreamWriter _oscWriter, _embodimentWriter, _objectWriter, _calibrationWriter, _eventsWriter;
        private bool _isRecording;
        private long _nextSampleId;

        public string LogsDirectory => _logsDirectory;
        public string ActiveOscCsvPath { get; private set; }
        public string ActiveEmbodimentCsvPath { get; private set; }
        public string ActiveObjectCsvPath { get; private set; }
        public string ActiveCalibrationCsvPath { get; private set; }
        public string ActiveEventsCsvPath { get; private set; }
        public string ActiveMetadataPath { get; private set; }
        public string SessionId { get; private set; }
        public bool IsRecording => _isRecording;
        public long OscRowsWritten { get; private set; }
        public long EmbodimentRowsWritten { get; private set; }
        public long ObjectRowsWritten { get; private set; }

        public DataRecordingService(SimulationCommandBus bus = null, string customLogsDirectory = null,
            SpatialCalibrationReference calibrationReference = null, SpatialMASConfig config = null)
        {
            _bus = bus;
            _calibrationReference = calibrationReference;
            _config = config;
            _logsDirectory = !string.IsNullOrWhiteSpace(customLogsDirectory)
                ? Path.GetFullPath(customLogsDirectory)
                : Path.Combine(Application.persistentDataPath, "SpatialMAS_Logs");
            Directory.CreateDirectory(_logsDirectory);
            if (_bus != null)
            {
                _bus.OnOscTrafficLogged += HandleOscTrafficLogged;
                _bus.OnSetRecordingState += HandleSetRecordingState;
            }
            if (_calibrationReference != null) _calibrationReference.Changed += HandleCalibrationChanged;
        }

        private void HandleSetRecordingState(bool shouldRecord)
        {
            if (shouldRecord) StartRecording(); else StopRecording();
        }

        public void StartRecording(string sessionTag = null)
        {
            lock (_lock)
            {
                if (_isRecording) return;
                string sessionId = Guid.NewGuid().ToString("N");
                string startedUtc = UtcNow();
                string suffix = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss-fff", CultureInfo.InvariantCulture) + "_" + sessionId;
                string safeTag = SanitizeSessionTag(sessionTag);
                if (safeTag.Length > 0) suffix += "_" + safeTag;
                string[] paths =
                {
                    Path.Combine(_logsDirectory, "osc_traffic_" + suffix + ".csv"),
                    Path.Combine(_logsDirectory, "xr_embodiment_" + suffix + ".csv"),
                    Path.Combine(_logsDirectory, "object_state_" + suffix + ".csv"),
                    Path.Combine(_logsDirectory, "calibration_" + suffix + ".csv"),
                    Path.Combine(_logsDirectory, "session_events_" + suffix + ".csv"),
                    Path.Combine(_logsDirectory, "session_metadata_" + suffix + ".json")
                };
                string[] headers = { "TimestampISO,ElapsedMs,Direction,OscAddress,Arguments", EmbodimentHeader, ObjectHeader, CalibrationHeader, EventsHeader };
                var opened = new List<StreamWriter>(5);
                var createdPaths = new List<string>(6);
                CalibrationSnapshot initialCalibration = CurrentCalibration();
                try
                {
                    for (int i = 0; i < headers.Length; i++)
                    {
                        StreamWriter writer = CreateNewWriter(paths[i], createdPaths);
                        opened.Add(writer);
                        writer.WriteLine(headers[i]);
                        writer.Flush();
                    }
                    using (StreamWriter metadata = CreateNewWriter(paths[5], createdPaths))
                        metadata.Write(BuildMetadata(sessionId, startedUtc, initialCalibration, paths));
                }
                catch
                {
                    foreach (StreamWriter writer in opened) { try { writer.Dispose(); } catch { } }
                    // These paths are recorded only after CreateNew succeeds; existing sessions are never deleted.
                    foreach (string path in createdPaths) { try { File.Delete(path); } catch { } }
                    throw;
                }

                _writers = opened.ToArray();
                _oscWriter = _writers[0]; _embodimentWriter = _writers[1]; _objectWriter = _writers[2];
                _calibrationWriter = _writers[3]; _eventsWriter = _writers[4];
                ActiveOscCsvPath = paths[0]; ActiveEmbodimentCsvPath = paths[1]; ActiveObjectCsvPath = paths[2];
                ActiveCalibrationCsvPath = paths[3]; ActiveEventsCsvPath = paths[4]; ActiveMetadataPath = paths[5];
                SessionId = sessionId;
                OscRowsWritten = EmbodimentRowsWritten = ObjectRowsWritten = 0;
                _nextSampleId = 0;
                _sessionStopwatch.Restart();
                _isRecording = true;
                WriteCalibrationLocked(initialCalibration);
                WriteEventLocked("RecordingStarted", "{}", -1);
            }
        }

        private static StreamWriter CreateNewWriter(string path, List<string> createdPaths)
        {
            var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite, 65536);
            createdPaths.Add(path);
            try { return new StreamWriter(stream, new UTF8Encoding(false)); }
            catch { stream.Dispose(); throw; }
        }

        public void StopRecording()
        {
            lock (_lock)
            {
                if (!_isRecording) return;
                try { WriteEventLocked("RecordingStopped", "{}", -1); }
                finally
                {
                    _isRecording = false;
                    _sessionStopwatch.Stop();
                    foreach (StreamWriter writer in _writers)
                    {
                        try { writer.Dispose(); }
                        catch (Exception error) { UnityEngine.Debug.LogWarning("[DataRecordingService] Could not flush/close a session file: " + error.Message); }
                    }
                    _writers = Array.Empty<StreamWriter>();
                    _oscWriter = _embodimentWriter = _objectWriter = _calibrationWriter = _eventsWriter = null;
                }
            }
        }

        public void Flush()
        {
            lock (_lock)
            {
                if (!_isRecording) return;
                foreach (StreamWriter writer in _writers) writer.Flush();
            }
        }

        public void LogOscMessage(string direction, string oscAddress, string argumentsCsv)
        {
            lock (_lock)
            {
                if (!_isRecording) return;
                _oscWriter.WriteLine(UtcNow() + "," + Number(_sessionStopwatch.Elapsed.TotalMilliseconds) + "," +
                    EscapeCsvField(direction) + "," + EscapeCsvField(oscAddress) + "," + EscapeCsvField(argumentsCsv));
                OscRowsWritten++;
            }
        }

        public void LogEmbodimentRow(in EmbodimentTelemetryRow row)
            => LogWorldSample(in row, Array.Empty<ObjectTelemetryRow>());

        public void LogWorldSample(in EmbodimentTelemetryRow row, IReadOnlyList<ObjectTelemetryRow> objects)
        {
            lock (_lock)
            {
                if (!_isRecording) return;
                // Stamp once: participant and every object in a sample share the same time and sample identifier.
                string iso = UtcNow();
                double elapsed = _sessionStopwatch.Elapsed.TotalMilliseconds;
                long sampleId = ++_nextSampleId;
                CalibrationSnapshot calibration = CurrentCalibration();
                string calibrationId = string.IsNullOrEmpty(row.CalibrationId) ? calibration.CalibrationId : row.CalibrationId;
                int calibrationRevision = string.IsNullOrEmpty(row.CalibrationId) ? calibration.Revision : row.CalibrationRevision;
                var sb = new StringBuilder(1024);
                AppendSamplePrefix(sb, iso, elapsed, sampleId, row.Frame);
                Text(sb, row.ScenarioName); Text(sb, row.Layout); Text(sb, row.XRMode);
                Integer(sb, row.ActiveAgents); Float(sb, row.FrameTimeMs); Flag(sb, row.CoordinatesValid);
                Text(sb, calibrationId); Integer(sb, calibrationRevision); Integer(sb, row.TrackingRevision);
                Vector(sb, row.ScenarioCenter); Flag(sb, row.HeadTracked); Text(sb, row.HeadSource); Vector(sb, row.HeadPos); Rotation(sb, row.HeadRot);
                Flag(sb, row.LeftHandTracked); Vector(sb, row.LeftHandPos); Rotation(sb, row.LeftHandRot); Flag(sb, row.LeftPinch);
                Flag(sb, row.RightHandTracked); Vector(sb, row.RightHandPos); Rotation(sb, row.RightHandRot); Flag(sb, row.RightPinch);
                Vector(sb, row.TrackingOriginPos); Rotation(sb, row.TrackingOriginRot); Vector(sb, row.TrackingOriginScale);
                Flag(sb, row.UserRepellerEnabled); Float(sb, row.UserRepellerRadiusMeters); Float(sb, row.UserRepellerStrength);
                _embodimentWriter.WriteLine(sb.ToString());
                EmbodimentRowsWritten++;

                if (objects == null) return;
                for (int i = 0; i < objects.Count; i++)
                {
                    ObjectTelemetryRow item = objects[i];
                    sb.Clear();
                    AppendSamplePrefix(sb, iso, elapsed, sampleId, row.Frame);
                    Text(sb, item.Kind); Integer(sb, item.Id); Text(sb, item.Shape);
                    Vector(sb, item.Position); Rotation(sb, item.Rotation); Vector(sb, item.Dimensions);
                    Text(sb, item.MovementMode); Flag(sb, item.Grabbed); Flag(sb, item.Interactable);
                    Flag(sb, row.CoordinatesValid); Text(sb, calibrationId); Integer(sb, calibrationRevision); Integer(sb, row.TrackingRevision);
                    _objectWriter.WriteLine(sb.ToString());
                    ObjectRowsWritten++;
                }
            }
        }

        public void LogEvent(string eventType, string payloadJson, int frame = -1)
        {
            lock (_lock)
            {
                if (_isRecording) WriteEventLocked(eventType, payloadJson, frame);
            }
        }

        private void WriteEventLocked(string eventType, string payloadJson, int frame)
        {
            _eventsWriter.WriteLine(UtcNow() + "," + Number(_sessionStopwatch.Elapsed.TotalMilliseconds) + "," + frame + "," +
                SessionId + "," + EscapeCsvField(eventType) + "," + EscapeCsvField(payloadJson ?? "{}"));
        }

        private void HandleCalibrationChanged(CalibrationSnapshot snapshot)
        {
            lock (_lock)
            {
                if (!_isRecording) return;
                WriteCalibrationLocked(snapshot);
                WriteEventLocked("CalibrationChanged", CalibrationJson(snapshot), -1);
            }
        }

        private void WriteCalibrationLocked(CalibrationSnapshot snapshot)
        {
            var sb = new StringBuilder(256);
            sb.Append(UtcNow()).Append(',').Append(Number(_sessionStopwatch.Elapsed.TotalMilliseconds)).Append(',').Append(SessionId);
            Text(sb, snapshot.CalibrationId); Integer(sb, snapshot.Revision); Vector(sb, snapshot.Position); Rotation(sb, snapshot.Rotation);
            Flag(sb, snapshot.Visible); Flag(sb, snapshot.Locked);
            _calibrationWriter.WriteLine(sb.ToString());
        }

        private CalibrationSnapshot CurrentCalibration() => _calibrationReference != null ? _calibrationReference.Snapshot :
            new CalibrationSnapshot("none", 0, Vector3.zero, Quaternion.identity, false, true);

        private void AppendSamplePrefix(StringBuilder sb, string iso, double elapsed, long sampleId, int frame)
            => sb.Append(iso).Append(',').Append(Number(elapsed)).Append(',').Append(sampleId).Append(',').Append(frame).Append(',').Append(SessionId);
        private static void Text(StringBuilder sb, string value) => sb.Append(',').Append(EscapeCsvField(value));
        private static void Integer(StringBuilder sb, int value) => sb.Append(',').Append(value);
        private static void Float(StringBuilder sb, float value) => sb.Append(',').Append(value.ToString("R", CultureInfo.InvariantCulture));
        private static void Flag(StringBuilder sb, bool value) => sb.Append(value ? ",1" : ",0");
        private static void Vector(StringBuilder sb, Vector3 value) { Float(sb, value.x); Float(sb, value.y); Float(sb, value.z); }
        private static void Rotation(StringBuilder sb, Quaternion value) { Float(sb, value.x); Float(sb, value.y); Float(sb, value.z); Float(sb, value.w); }
        private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        private static string UtcNow() => DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        private string BuildMetadata(string sessionId, string startedUtc, CalibrationSnapshot calibration, string[] paths)
        {
            var sb = new StringBuilder(2048).Append('{');
            JsonProperty(sb, "schemaVersion", "2", true);
            JsonProperty(sb, "sessionId", sessionId);
            JsonProperty(sb, "startedUtc", startedUtc);
            JsonProperty(sb, "coordinateFrame", "UnityWorld");
            JsonProperty(sb, "positionUnits", "meters");
            JsonProperty(sb, "velocityUnits", "meters/second");
            JsonProperty(sb, "axes", "X=right,Y=up,Z=forward");
            JsonProperty(sb, "handedness", "left");
            JsonProperty(sb, "quaternionOrder", "xyzw");
            JsonProperty(sb, "timeUnits", "ElapsedMs=milliseconds; TimestampISO=UTC");
            JsonProperty(sb, "sampling", "frame-limited; use recorded timestamps and SampleId");
            JsonProperty(sb, "objectDimensions", "mesh bounds along object-local axes, multiplied by world transform scale, in meters");
            JsonProperty(sb, "calibrationSemantics", "reference pose only; moving reference never transforms saved world poses");
            JsonProperty(sb, "legacyOsc", "physical millimeters, wire axes X=right,Y=forward,Z=up");
            JsonProperty(sb, "worldOsc", "physical millimeters, Unity XYZ axes; /world prefix");
            JsonProperty(sb, "initialCalibration", CalibrationJson(calibration), true);
            JsonProperty(sb, "configuration", _config != null ? JsonUtility.ToJson(_config) : "null", true);
            var files = new StringBuilder().Append('[');
            for (int i = 0; i < paths.Length; i++)
            {
                if (i > 0) files.Append(',');
                files.Append(JsonString(Path.GetFileName(paths[i])));
            }
            JsonProperty(sb, "files", files.Append(']').ToString(), true);
            return sb.Append('}').ToString();
        }

        private static void JsonProperty(StringBuilder sb, string key, string value, bool raw = false)
        {
            if (sb.Length > 1) sb.Append(',');
            sb.Append(JsonString(key)).Append(':').Append(raw ? value : JsonString(value));
        }

        [Serializable]
        private class CalibrationMetadata
        {
            public string calibrationId;
            public int revision;
            public Vector3 positionMeters;
            public Quaternion rotationXyzw;
            public bool visible;
            public bool locked;
            public CalibrationMetadata(CalibrationSnapshot snapshot)
            {
                calibrationId = snapshot.CalibrationId; revision = snapshot.Revision; positionMeters = snapshot.Position;
                rotationXyzw = snapshot.Rotation; visible = snapshot.Visible; locked = snapshot.Locked;
            }
        }
        private static string CalibrationJson(CalibrationSnapshot snapshot) => JsonUtility.ToJson(new CalibrationMetadata(snapshot));

        private static string JsonString(string value)
        {
            if (value == null) return "null";
            const char quote = (char)34;
            const char escape = (char)92;
            var sb = new StringBuilder(value.Length + 2).Append(quote);
            foreach (char c in value)
            {
                if (c == quote || c == escape) sb.Append(escape).Append(c);
                else if (c < 32) sb.Append(escape).Append('u').Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else sb.Append(c);
            }
            return sb.Append(quote).ToString();
        }

        public FileInfo[] GetRecordedCsvFiles() => GetRecordedFiles(false);
        public FileInfo[] GetRecordedLogFiles() => GetRecordedFiles(true);
        private FileInfo[] GetRecordedFiles(bool includeMetadata)
        {
            Flush();
            if (!Directory.Exists(_logsDirectory)) return Array.Empty<FileInfo>();
            var files = new List<FileInfo>();
            foreach (FileInfo file in new DirectoryInfo(_logsDirectory).GetFiles())
                if (file.Extension.Equals(".csv", StringComparison.OrdinalIgnoreCase) ||
                    (includeMetadata && file.Extension.Equals(".json", StringComparison.OrdinalIgnoreCase))) files.Add(file);
            files.Sort((a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
            return files.ToArray();
        }

        private void HandleOscTrafficLogged(string direction, string address, string formattedArgs) => LogOscMessage(direction, address, formattedArgs);
        private static string EscapeCsvField(string value)
        {
            const char quote = (char)34;
            if (string.IsNullOrEmpty(value)) return new string(quote, 2);
            if (value.IndexOf(',') >= 0 || value.IndexOf(quote) >= 0 || value.IndexOf((char)13) >= 0 || value.IndexOf((char)10) >= 0)
                return quote + value.Replace(quote.ToString(), new string(quote, 2)) + quote;
            return value;
        }
        private static string SanitizeSessionTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return string.Empty;
            var safe = new StringBuilder(Math.Min(tag.Length, 64));
            foreach (char c in tag)
            {
                if (safe.Length >= 64) break;
                safe.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            }
            return safe.ToString();
        }

        public void Dispose()
        {
            if (_bus != null)
            {
                _bus.OnOscTrafficLogged -= HandleOscTrafficLogged;
                _bus.OnSetRecordingState -= HandleSetRecordingState;
            }
            if (_calibrationReference != null) _calibrationReference.Changed -= HandleCalibrationChanged;
            StopRecording();
        }
    }
}
