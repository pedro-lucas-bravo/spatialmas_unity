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
    public struct EmbodimentTelemetryRow
    {
        public int Frame;
        public string ScenarioName;
        public string XRMode;
        public float WorldScale;
        public int ActiveAgents;
        public float FrameTimeMs;
        public Vector3 HeadPos;
        public Quaternion HeadRot;
        public Vector3 LeftHandPos;
        public bool LeftPinch;
        public Vector3 RightHandPos;
        public bool RightPinch;
    }

    /// <summary>
    /// High-performance buffered CSV Data Logger.
    /// Replaces legacy FileRecorder.cs (which opened/closed the file handle on every single message)
    /// with persistent 64 KB buffered StreamWriters for both OSC Traffic CSV and XR Embodiment Telemetry CSV.
    /// </summary>
    public class DataRecordingService : IDisposable
    {
        private readonly string _logsDirectory;
        private readonly SimulationCommandBus _bus;
        private readonly object _lock = new object();
        private readonly Stopwatch _sessionStopwatch = new Stopwatch();

        private StreamWriter _oscWriter;
        private StreamWriter _embodimentWriter;
        private bool _isRecording;

        public string LogsDirectory => _logsDirectory;
        public string ActiveOscCsvPath { get; private set; }
        public string ActiveEmbodimentCsvPath { get; private set; }
        public bool IsRecording => _isRecording;
        public long OscRowsWritten { get; private set; }
        public long EmbodimentRowsWritten { get; private set; }

        public DataRecordingService(SimulationCommandBus bus = null, string customLogsDirectory = null)
        {
            _bus = bus;
            _logsDirectory = !string.IsNullOrWhiteSpace(customLogsDirectory)
                ? customLogsDirectory
                : Path.Combine(Application.persistentDataPath, "SpatialMAS_Logs");

            if (!Directory.Exists(_logsDirectory))
            {
                Directory.CreateDirectory(_logsDirectory);
            }

            if (_bus != null)
            {
                _bus.OnOscTrafficLogged += HandleOscTrafficLogged;
                _bus.OnSetRecordingState += HandleSetRecordingState;
            }
        }

        private void HandleSetRecordingState(bool shouldRecord)
        {
            if (shouldRecord) StartRecording();
            else StopRecording();
        }

        public void StartRecording(string sessionTag = null)
        {
            lock (_lock)
            {
                if (_isRecording) return;

                string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss-fff", CultureInfo.InvariantCulture);
                string suffix = timestamp + "_" + Guid.NewGuid().ToString("N");
                string safeTag = SanitizeSessionTag(sessionTag);
                if (safeTag.Length > 0) suffix += "_" + safeTag;

                string oscPath = Path.Combine(_logsDirectory, $"osc_traffic_{suffix}.csv");
                string embodimentPath = Path.Combine(_logsDirectory, $"xr_embodiment_{suffix}.csv");
                StreamWriter oscWriter = null;
                StreamWriter embodimentWriter = null;
                try
                {
                    // Never truncate an existing session, even if a name collision occurs.
                    oscWriter = new StreamWriter(
                        new FileStream(oscPath, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite, 65536),
                        Encoding.UTF8);
                    oscWriter.WriteLine("TimestampISO,ElapsedMs,Direction,OscAddress,Arguments");
                    embodimentWriter = new StreamWriter(
                        new FileStream(embodimentPath, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite, 65536),
                        Encoding.UTF8);
                    embodimentWriter.WriteLine(
                        "TimestampISO,ElapsedMs,Frame,Scenario,XRMode,WorldScale,ActiveAgents,FrameTimeMs," +
                        "HeadPosX,HeadPosY,HeadPosZ,HeadRotX,HeadRotY,HeadRotZ,HeadRotW," +
                        "LHandPosX,LHandPosY,LHandPosZ,LPinch,RHandPosX,RHandPosY,RHandPosZ,RPinch");
                }
                catch
                {
                    // Publish a session only after both streams have opened successfully.
                    oscWriter?.Dispose();
                    embodimentWriter?.Dispose();
                    throw;
                }

                _oscWriter = oscWriter;
                _embodimentWriter = embodimentWriter;
                ActiveOscCsvPath = oscPath;
                ActiveEmbodimentCsvPath = embodimentPath;

                OscRowsWritten = 0;
                EmbodimentRowsWritten = 0;
                _sessionStopwatch.Restart();
                _isRecording = true;
            }
        }

        public void StopRecording()
        {
            lock (_lock)
            {
                if (!_isRecording) return;
                _isRecording = false;
                _sessionStopwatch.Stop();

                try
                {
                    _oscWriter?.Flush();
                    _oscWriter?.Dispose();
                }
                catch { }
                _oscWriter = null;

                try
                {
                    _embodimentWriter?.Flush();
                    _embodimentWriter?.Dispose();
                }
                catch { }
                _embodimentWriter = null;
            }
        }

        public void Flush()
        {
            lock (_lock)
            {
                if (!_isRecording) return;
                _oscWriter?.Flush();
                _embodimentWriter?.Flush();
            }
        }

        public void LogOscMessage(string direction, string oscAddress, string argumentsCsv)
        {
            lock (_lock)
            {
                if (!_isRecording || _oscWriter == null) return;
                string iso = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
                long elapsed = _sessionStopwatch.ElapsedMilliseconds;
                string escapedArgs = EscapeCsvField(argumentsCsv ?? string.Empty);
                _oscWriter.WriteLine($"{iso},{elapsed},{EscapeCsvField(direction)},{EscapeCsvField(oscAddress)},{escapedArgs}");
                OscRowsWritten++;
            }
        }

        public void LogEmbodimentRow(in EmbodimentTelemetryRow row)
        {
            lock (_lock)
            {
                if (!_isRecording || _embodimentWriter == null) return;
                var inv = CultureInfo.InvariantCulture;
                string iso = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", inv);
                long elapsed = _sessionStopwatch.ElapsedMilliseconds;

                var sb = new StringBuilder(256);
                sb.Append(iso).Append(',')
                  .Append(elapsed).Append(',')
                  .Append(row.Frame).Append(',')
                  .Append(EscapeCsvField(row.ScenarioName)).Append(',')
                  .Append(row.XRMode).Append(',')
                  .Append(row.WorldScale.ToString("F4", inv)).Append(',')
                  .Append(row.ActiveAgents).Append(',')
                  .Append(row.FrameTimeMs.ToString("F2", inv)).Append(',')
                  .Append(row.HeadPos.x.ToString("F4", inv)).Append(',')
                  .Append(row.HeadPos.y.ToString("F4", inv)).Append(',')
                  .Append(row.HeadPos.z.ToString("F4", inv)).Append(',')
                  .Append(row.HeadRot.x.ToString("F4", inv)).Append(',')
                  .Append(row.HeadRot.y.ToString("F4", inv)).Append(',')
                  .Append(row.HeadRot.z.ToString("F4", inv)).Append(',')
                  .Append(row.HeadRot.w.ToString("F4", inv)).Append(',')
                  .Append(row.LeftHandPos.x.ToString("F4", inv)).Append(',')
                  .Append(row.LeftHandPos.y.ToString("F4", inv)).Append(',')
                  .Append(row.LeftHandPos.z.ToString("F4", inv)).Append(',')
                  .Append(row.LeftPinch ? 1 : 0).Append(',')
                  .Append(row.RightHandPos.x.ToString("F4", inv)).Append(',')
                  .Append(row.RightHandPos.y.ToString("F4", inv)).Append(',')
                  .Append(row.RightHandPos.z.ToString("F4", inv)).Append(',')
                  .Append(row.RightPinch ? 1 : 0);

                _embodimentWriter.WriteLine(sb.ToString());
                EmbodimentRowsWritten++;
            }
        }

        public FileInfo[] GetRecordedCsvFiles()
        {
            Flush();
            if (!Directory.Exists(_logsDirectory)) return Array.Empty<FileInfo>();
            var dirInfo = new DirectoryInfo(_logsDirectory);
            var files = dirInfo.GetFiles("*.csv");
            Array.Sort(files, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
            return files;
        }

        private void HandleOscTrafficLogged(string direction, string address, string formattedArgs)
        {
            LogOscMessage(direction, address, formattedArgs);
        }

        private static string EscapeCsvField(string value)
        {
            if (string.IsNullOrEmpty(value)) return "\"\"";
            if (value.IndexOf(',') >= 0 || value.IndexOf('"') >= 0 || value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0)
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }
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
            StopRecording();
        }
    }
}
