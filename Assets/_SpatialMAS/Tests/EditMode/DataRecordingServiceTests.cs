using System.IO;
using NUnit.Framework;
using SpatialMAS.Logging;
using UnityEngine;

namespace SpatialMAS.Tests.EditMode
{
    public class DataRecordingServiceTests
    {
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
                        WorldScale = 0.2f,
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
