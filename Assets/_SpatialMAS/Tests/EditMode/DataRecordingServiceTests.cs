using System.IO;
using NUnit.Framework;
using SpatialMAS.Logging;
using UnityEngine;

namespace SpatialMAS.Tests.EditMode
{
    public class DataRecordingServiceTests
    {
        [Test]
        public void RapidRestart_PreservesEverySessionAndPairsItsFiles()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "SpatialMAS_UnitTests_" + System.Guid.NewGuid().ToString("N"));
            try
            {
                using (var recorder = new DataRecordingService(null, tempDir))
                {
                    var paths = new System.Collections.Generic.HashSet<string>();
                    for (int session = 0; session < 12; session++)
                    {
                        recorder.StartRecording("same_tag");
                        string oscPath = recorder.ActiveOscCsvPath;
                        string embodimentPath = recorder.ActiveEmbodimentCsvPath;
                        Assert.IsTrue(paths.Add(oscPath), "Each restart must use a fresh file.");
                        Assert.AreEqual(Path.GetFileName(oscPath).Substring("osc_traffic_".Length),
                            Path.GetFileName(embodimentPath).Substring("xr_embodiment_".Length));
                        recorder.LogOscMessage("IN", "/session/" + session, "value");
                        recorder.LogEmbodimentRow(new EmbodimentTelemetryRow { Frame = session });
                        recorder.StopRecording();
                    }

                    Assert.AreEqual(60, Directory.GetFiles(tempDir, "*.csv").Length);
                    Assert.AreEqual(12, Directory.GetFiles(tempDir, "*.json").Length);
                    int expectedSession = 0;
                    foreach (string path in paths)
                    {
                        Assert.AreEqual(2, File.ReadAllLines(path).Length);
                        StringAssert.Contains("/session/", File.ReadAllText(path));
                        expectedSession++;
                    }
                    Assert.AreEqual(12, expectedSession);
                }
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Test]
        public void WorldSample_SharesIdentifiersAndTimestampsAndPreservesRoundTripPhysicalValues()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "SpatialMAS_UnitTests_" + System.Guid.NewGuid().ToString("N"));
            try
            {
                using (var recorder = new DataRecordingService(null, tempDir))
                {
                    recorder.StartRecording("world_sample");
                    var row = new EmbodimentTelemetryRow
                    {
                        Frame = 123, ScenarioName = "Test", Layout = "Compact", CoordinatesValid = true,
                        HeadPos = new Vector3(1.2345678f, 1.65f, -0.3456789f), HeadRot = Quaternion.identity,
                        HeadTracked = true, HeadSource = "XR", LeftHandPos = new Vector3(0.1f, 0.2f, 0.3f),
                        LeftHandRot = Quaternion.Euler(10f, 20f, 30f), LeftHandTracked = true, LeftPinch = true,
                        RightHandRot = Quaternion.Euler(30f, 20f, 10f), RightHandTracked = false,
                        TrackingOriginScale = Vector3.one, TrackingOriginRot = Quaternion.identity,
                        CalibrationId = "test_calibration", CalibrationRevision = 3, TrackingRevision = 2
                    };
                    recorder.LogWorldSample(row, new[]
                    {
                        new ObjectTelemetryRow { Kind = "Agent", Id = 7, Shape = "Cube", Position = new Vector3(2f, 3f, 4f),
                            Rotation = Quaternion.identity, Dimensions = new Vector3(0.1f, 0.2f, 0.3f), Interactable = true },
                        new ObjectTelemetryRow { Kind = "Boundary", Id = 1, Shape = "Sphere", Rotation = Quaternion.identity,
                            Dimensions = Vector3.one * 2f }
                    });
                    recorder.StopRecording();
                    string[] bodyLines = File.ReadAllLines(recorder.ActiveEmbodimentCsvPath);
                    string[] objectLines = File.ReadAllLines(recorder.ActiveObjectCsvPath);
                    Assert.AreEqual(2, bodyLines.Length);
                    Assert.AreEqual(3, objectLines.Length);
                    var body = ParseRow(bodyLines[0], bodyLines[1]);
                    for (int i = 1; i < objectLines.Length; i++)
                    {
                        var item = ParseRow(objectLines[0], objectLines[i]);
                        foreach (string key in new[] { "TimestampISO", "ElapsedMs", "SampleId", "Frame", "SessionId", "CalibrationId", "CalibrationRevision", "TrackingRevision" })
                            Assert.AreEqual(body[key], item[key], key);
                        Assert.AreEqual("1", item["CoordinatesValid"]);
                    }
                    Assert.AreEqual(row.HeadPos.x, float.Parse(body["HeadPosX_m"], System.Globalization.CultureInfo.InvariantCulture));
                    Assert.AreEqual(row.LeftHandRot.x, float.Parse(body["LHandRotX"], System.Globalization.CultureInfo.InvariantCulture));
                    Assert.AreEqual(row.RightHandRot.w, float.Parse(body["RHandRotW"], System.Globalization.CultureInfo.InvariantCulture));
                    Assert.AreEqual("1", body["LHandTracked"]);
                    Assert.AreEqual("0", body["RHandTracked"]);
                    Assert.AreEqual("1", body["LPinch"]);
                    Assert.IsFalse(body.ContainsKey("WorldScale"));
                    Assert.AreEqual(2, recorder.ObjectRowsWritten);
                    Assert.AreEqual(5, recorder.GetRecordedCsvFiles().Length);
                    Assert.AreEqual(6, recorder.GetRecordedLogFiles().Length);
                    string metadata = File.ReadAllText(recorder.ActiveMetadataPath);
                    StringAssert.Contains("\"schemaVersion\":2", metadata);
                    StringAssert.Contains("\"coordinateFrame\":\"UnityWorld\"", metadata);
                    StringAssert.Contains("\"quaternionOrder\":\"xyzw\"", metadata);
                }
            }
            finally { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); }
        }

        [Test]
        public void CalibrationChanges_AreRecordedWithoutTransformingWorldSamples()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "SpatialMAS_UnitTests_" + System.Guid.NewGuid().ToString("N"));
            var referenceObject = new GameObject("Recording Calibration Reference");
            try
            {
                var reference = referenceObject.AddComponent<SpatialMAS.Core.SpatialCalibrationReference>();
                reference.Initialize(null, Vector3.zero, Quaternion.identity, false, false);
                using (var recorder = new DataRecordingService(null, tempDir, reference))
                {
                    recorder.StartRecording("calibration");
                    var row = new EmbodimentTelemetryRow { Frame = 1, HeadPos = new Vector3(3f, 2f, 1f), HeadRot = Quaternion.identity };
                    var objects = new[] { new ObjectTelemetryRow { Kind = "Agent", Id = 0, Position = Vector3.one, Rotation = Quaternion.identity, Dimensions = Vector3.one * 0.1f } };
                    recorder.LogWorldSample(row, objects);
                    Assert.IsTrue(reference.TrySetPose(new Vector3(5f, 4f, 3f), Quaternion.Euler(0f, 90f, 0f)));
                    row.Frame = 2;
                    recorder.LogWorldSample(row, objects);
                    recorder.LogEvent("ParameterApplied", "{\"name\":\"speed\",\"value\":0.5}", 2);
                    recorder.StopRecording();
                    string[] bodyLines = File.ReadAllLines(recorder.ActiveEmbodimentCsvPath);
                    var before = ParseRow(bodyLines[0], bodyLines[1]);
                    var after = ParseRow(bodyLines[0], bodyLines[2]);
                    foreach (string key in new[] { "HeadPosX_m", "HeadPosY_m", "HeadPosZ_m", "HeadRotX", "HeadRotY", "HeadRotZ", "HeadRotW" })
                        Assert.AreEqual(before[key], after[key]);
                    Assert.AreEqual(before["CalibrationId"], after["CalibrationId"]);
                    Assert.AreEqual("0", before["CalibrationRevision"]);
                    Assert.AreEqual("1", after["CalibrationRevision"]);
                    string[] objectLines = File.ReadAllLines(recorder.ActiveObjectCsvPath);
                    var first = ParseRow(objectLines[0], objectLines[1]);
                    var second = ParseRow(objectLines[0], objectLines[2]);
                    foreach (string key in new[] { "PositionX_m", "PositionY_m", "PositionZ_m", "DimensionsX_m", "DimensionsY_m", "DimensionsZ_m" })
                        Assert.AreEqual(first[key], second[key]);
                    string[] calibrationLines = File.ReadAllLines(recorder.ActiveCalibrationCsvPath);
                    Assert.AreEqual(3, calibrationLines.Length, "Initial calibration and every accepted change must be present.");
                    Assert.AreEqual("5", ParseRow(calibrationLines[0], calibrationLines[2])["PositionX_m"]);
                    StringAssert.Contains("CalibrationChanged", File.ReadAllText(recorder.ActiveEventsCsvPath));
                    StringAssert.Contains("ParameterApplied", File.ReadAllText(recorder.ActiveEventsCsvPath));
                    StringAssert.Contains("\"revision\":0", File.ReadAllText(recorder.ActiveMetadataPath));
                }
            }
            finally
            {
                Object.DestroyImmediate(referenceObject);
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        private static System.Collections.Generic.Dictionary<string, string> ParseRow(string header, string line)
        {
            string[] keys = header.Split(',');
            var values = new System.Collections.Generic.List<string>();
            var field = new System.Text.StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '\"')
                {
                    if (quoted && i + 1 < line.Length && line[i + 1] == '\"') { field.Append(c); i++; }
                    else quoted = !quoted;
                }
                else if (c == ',' && !quoted) { values.Add(field.ToString()); field.Clear(); }
                else field.Append(c);
            }
            values.Add(field.ToString());
            Assert.AreEqual(keys.Length, values.Count, "Header and sample column counts must agree.");
            var row = new System.Collections.Generic.Dictionary<string, string>();
            for (int i = 0; i < keys.Length; i++) row[keys[i]] = values[i];
            return row;
        }

        [Test]
        public void Recording_QuotesMultilineFieldsAndKeepsTagInsideLogDirectory()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "SpatialMAS_UnitTests_" + System.Guid.NewGuid().ToString("N"));
            try
            {
                using (var recorder = new DataRecordingService(null, tempDir))
                {
                    recorder.StartRecording("../participant\\\"one\"");
                    Assert.AreEqual(Path.GetFullPath(tempDir), Path.GetDirectoryName(recorder.ActiveOscCsvPath));
                    recorder.LogOscMessage("IN", "/message", "line one\nline two");
                    recorder.StopRecording();
                    StringAssert.Contains("\"line one\nline two\"", File.ReadAllText(recorder.ActiveOscCsvPath));
                }
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Test]
        public void StartAndStopRecording_WritesValidOscAndEmbodimentCsvFiles()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "SpatialMAS_UnitTests_" + System.Guid.NewGuid().ToString("N"));
            try
            {
                using (var recorder = new DataRecordingService(null, tempDir))
                {
                    recorder.StartRecording("unit_test");
                    Assert.IsTrue(recorder.IsRecording);

                    recorder.LogOscMessage("IN", "/agents/position/id", "0,100.5,-200.0,50.0");

                    var row = new EmbodimentTelemetryRow
                    {
                        Frame = 42,
                        ScenarioName = "2. 3D Swarmalators",
                        XRMode = "MRPassthrough",
                        Layout = "Compact",
                        ActiveAgents = 24,
                        FrameTimeMs = 11.1f,
                        HeadPos = new Vector3(0.1f, 1.65f, -0.3f),
                        HeadRot = Quaternion.identity,
                        LeftHandPos = new Vector3(-0.2f, 1.3f, 0.4f),
                        LeftPinch = true,
                        RightHandPos = new Vector3(0.2f, 1.3f, 0.4f),
                        RightPinch = false
                    };
                    recorder.LogEmbodimentRow(in row);
                    recorder.StopRecording();

                    Assert.IsFalse(recorder.IsRecording);
                    Assert.IsTrue(File.Exists(recorder.ActiveOscCsvPath));
                    Assert.IsTrue(File.Exists(recorder.ActiveEmbodimentCsvPath));

                    string[] oscLines = File.ReadAllLines(recorder.ActiveOscCsvPath);
                    Assert.AreEqual(2, oscLines.Length);
                    StringAssert.Contains("/agents/position/id", oscLines[1]);

                    string[] embLines = File.ReadAllLines(recorder.ActiveEmbodimentCsvPath);
                    Assert.AreEqual(2, embLines.Length);
                    StringAssert.Contains("2. 3D Swarmalators", embLines[1]);
                }
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }
    }
}
