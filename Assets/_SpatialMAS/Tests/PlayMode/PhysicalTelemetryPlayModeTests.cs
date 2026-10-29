using System.Collections;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using SpatialMAS.Agents;
using SpatialMAS.Core;
using SpatialMAS.Logging;
using SpatialMAS.Scenarios;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.TestTools;

namespace SpatialMAS.Tests.PlayMode
{
    public class PhysicalTelemetryPlayModeTests
    {
        [UnityTest]
        public IEnumerator CalibrationMoves_DoNotChangeRecordedWorldPosesOrSampleSynchronization()
        {
            string directory = Path.Combine(Path.GetTempPath(), "SpatialMAS_Physical_" + System.Guid.NewGuid().ToString("N"));
            var root = new GameObject("PhysicalRecordingTest");
            DataRecordingService recorder = null;
            try
            {
                Camera camera = CreateCameraRig(root, out _);
                var bus = new SimulationCommandBus();
                var agents = root.AddComponent<AgentManager>();
                agents.Initialize(bus);
                bus.InstantiateAgents(AgentShapeType.Cube, AgentMovementMode.Teleport, 1, 0.1f, "127.0.0.1", 6012);
                bus.SetAgentPositionUnity(0, new Vector3(1f, 2f, 3f));
                var reference = new GameObject("Reference").AddComponent<SpatialCalibrationReference>();
                reference.transform.SetParent(root.transform);
                reference.Initialize(bus, Vector3.zero, Quaternion.identity, true, false);
                recorder = new DataRecordingService(bus, directory, reference);
                var sampler = root.AddComponent<EmbodimentTelemetrySampler>();
                sampler.Initialize(recorder, null, null, null, agents, reference, 50f, camera: camera);
                recorder.StartRecording();
                yield return new WaitForSecondsRealtime(0.05f);
                Assert.IsTrue(reference.TrySetPose(new Vector3(10f, 20f, 30f), Quaternion.Euler(0f, 90f, 0f)));
                yield return new WaitForSecondsRealtime(0.05f);
                recorder.StopRecording();
                string[] body = File.ReadAllLines(recorder.ActiveEmbodimentCsvPath);
                string[] objects = File.ReadAllLines(recorder.ActiveObjectCsvPath);
                Assert.GreaterOrEqual(body.Length, 3);
                Assert.AreEqual(body.Length, objects.Length, "One object row must join each participant sample.");
                for (int i = 1; i < body.Length; i++)
                {
                    Assert.AreEqual(Cell(body, i, "SampleId"), Cell(objects, i, "SampleId"));
                    Assert.AreEqual(Cell(body, i, "TimestampISO"), Cell(objects, i, "TimestampISO"));
                    Assert.AreEqual(Cell(body, i, "ElapsedMs"), Cell(objects, i, "ElapsedMs"));
                    Assert.AreEqual(3f, Number(body, i, "HeadPosX_m"));
                    Assert.AreEqual(1.6f, Number(body, i, "HeadPosY_m"), 0.00001f);
                    Assert.AreEqual(5f, Number(body, i, "HeadPosZ_m"));
                    Assert.AreEqual("DesktopCamera", Cell(body, i, "HeadSource"));
                    Assert.AreEqual("0", Cell(body, i, "HeadTracked"));
                    Assert.AreEqual(1f, Number(objects, i, "PositionX_m"));
                    Assert.AreEqual(2f, Number(objects, i, "PositionY_m"));
                    Assert.AreEqual(3f, Number(objects, i, "PositionZ_m"));
                    Assert.AreEqual(0.1f, Number(objects, i, "DimensionsX_m"), 0.00001f);
                }
                Assert.AreNotEqual(Cell(body, 1, "CalibrationRevision"), Cell(body, body.Length - 1, "CalibrationRevision"));
            }
            finally
            {
                recorder?.Dispose();
                Object.Destroy(root);
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [UnityTest]
        public IEnumerator NonUnitTrackingRig_IsFlaggedAndTrackingChangesAreRecorded()
        {
            string directory = Path.Combine(Path.GetTempPath(), "SpatialMAS_Tracking_" + System.Guid.NewGuid().ToString("N"));
            var root = new GameObject("TrackingFrameRecordingTest");
            DataRecordingService recorder = null;
            try
            {
                Camera camera = CreateCameraRig(root, out Transform offset);
                recorder = new DataRecordingService(null, directory);
                var sampler = root.AddComponent<EmbodimentTelemetrySampler>();
                sampler.Initialize(recorder, null, null, null, null, null, 50f, camera: camera);
                recorder.StartRecording();
                yield return new WaitForSecondsRealtime(0.05f);
                offset.localScale = Vector3.one * 2f;
                yield return new WaitForSecondsRealtime(0.05f);
                recorder.StopRecording();
                string[] body = File.ReadAllLines(recorder.ActiveEmbodimentCsvPath);
                Assert.AreEqual("1", Cell(body, 1, "CoordinatesValid"));
                Assert.AreEqual("0", Cell(body, body.Length - 1, "CoordinatesValid"));
                Assert.AreNotEqual(Cell(body, 1, "TrackingRevision"), Cell(body, body.Length - 1, "TrackingRevision"));
                Assert.AreEqual(2f, Number(body, body.Length - 1, "TrackingOriginScaleX"));
                StringAssert.Contains("rig_transform_changed", File.ReadAllText(recorder.ActiveEventsCsvPath));
            }
            finally
            {
                recorder?.Dispose();
                Object.Destroy(root);
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [UnityTest]
        public IEnumerator SameFrameRecordingRestart_RecordsInitialScenarioAndTrackingEvents()
        {
            string directory = Path.Combine(Path.GetTempPath(), "SpatialMAS_Restart_" + System.Guid.NewGuid().ToString("N"));
            var root = new GameObject("RecordingRestartTest");
            DataRecordingService recorder = null;
            try
            {
                Camera camera = CreateCameraRig(root, out _);
                var bus = new SimulationCommandBus();
                var agents = root.AddComponent<AgentManager>();
                agents.Initialize(bus);
                var scenarios = root.AddComponent<ScenarioManager>();
                scenarios.Initialize(bus, Vector3.zero, agents, null);
                recorder = new DataRecordingService(bus, directory);
                var sampler = root.AddComponent<EmbodimentTelemetrySampler>();
                sampler.Initialize(recorder, null, null, scenarios, agents, null, 50f, camera: camera);
                recorder.StartRecording();
                yield return new WaitForSecondsRealtime(0.05f);

                string firstSessionId = recorder.SessionId;
                recorder.StopRecording();
                recorder.StartRecording(); // No intervening LateUpdate to observe IsRecording == false.
                Assert.AreNotEqual(firstSessionId, recorder.SessionId);
                yield return new WaitForSecondsRealtime(0.05f);
                recorder.StopRecording();

                string events = File.ReadAllText(recorder.ActiveEventsCsvPath);
                StringAssert.Contains(",scenario_state,", events);
                StringAssert.Contains(",tracking_frame,", events);
                StringAssert.Contains("\"\"reason\"\":\"\"initial\"\"", events);
                Assert.Greater(File.ReadAllLines(recorder.ActiveEmbodimentCsvPath).Length, 1);
            }
            finally
            {
                recorder?.Dispose();
                Object.Destroy(root);
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        private static Camera CreateCameraRig(GameObject root, out Transform offset)
        {
            var rig = new GameObject("Unit XR Origin");
            rig.transform.SetParent(root.transform);
            var origin = rig.AddComponent<XROrigin>();
            offset = new GameObject("Camera Offset").transform;
            offset.SetParent(rig.transform);
            origin.CameraFloorOffsetObject = offset.gameObject;
            var camera = new GameObject("Physical Camera").AddComponent<Camera>();
            camera.transform.SetParent(offset);
            camera.transform.position = new Vector3(3f, 1.6f, 5f);
            origin.Camera = camera;
            return camera;
        }

        private static string Cell(string[] rows, int row, string column)
        {
            int index = System.Array.IndexOf(rows[0].Split(','), column);
            Assert.GreaterOrEqual(index, 0, column);
            return rows[row].Split(',')[index];
        }
        private static float Number(string[] rows, int row, string column)
            => float.Parse(Cell(rows, row, column), CultureInfo.InvariantCulture);
    }
}
