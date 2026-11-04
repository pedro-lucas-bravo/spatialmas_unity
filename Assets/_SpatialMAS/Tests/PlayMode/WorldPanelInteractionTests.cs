using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using SpatialMAS.Core;
using SpatialMAS.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace SpatialMAS.Tests.PlayMode
{
    public class WorldPanelInteractionTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();
        private EventSystem _previousEventSystem;
        private EventSystem _eventSystem;
        private Camera _camera;
        private TestWorldPanelHud _hud;
        private SimulationCommandBus _bus;

        [SetUp]
        public void SetUp()
        {
            _previousEventSystem = EventSystem.current;
            _eventSystem = NewObject("Panel Test EventSystem").AddComponent<EventSystem>();
            EventSystem.current = _eventSystem;
            _camera = NewObject("Panel Test Head").AddComponent<Camera>();
            _camera.tag = "MainCamera";
            _camera.pixelRect = new Rect(0, 0, 800, 600);
            _camera.stereoTargetEye = StereoTargetEyeMask.Both;
            _bus = new SimulationCommandBus();
            _hud = NewObject("Panel Test HUD").AddComponent<TestWorldPanelHud>();
            InitializeHud();
            // Tests exercise the tracked UI path directly without requiring a physical headset.
            _hud.enabled = false;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _objects) if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
            if (_previousEventSystem != null && _previousEventSystem.isActiveAndEnabled)
                EventSystem.current = _previousEventSystem;
        }

        [Test]
        public void ExistingWorldPanel_RepairsCameraRaycasterAndExplicitStereoMaterials()
        {
            Transform panel = Panel;
            Object.DestroyImmediate(panel.GetComponent<TrackedDeviceGraphicRaycaster>());
            Canvas canvas = panel.GetComponent<Canvas>();
            canvas.worldCamera = null;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            foreach (var graphic in panel.GetComponentsInChildren<Graphic>(true)) graphic.material = null;
            InitializeHud();

            Assert.AreEqual(RenderMode.WorldSpace, canvas.renderMode);
            Assert.AreSame(_camera, canvas.worldCamera);
            Assert.AreSame(_camera, panel.GetComponent<TrackedDeviceGraphicRaycaster>().eventCamera);
            Assert.AreSame(_camera, _eventSystem.GetComponent<XRUIInputModule>().uiCamera);
            Material material = Resources.Load<Material>("Mat_SpatialMAS_WorldUI");
            Assert.NotNull(material);
            Assert.IsTrue(material.enableInstancing);
            Assert.AreEqual("SpatialMAS/World UI Stereo", material.shader.name);
            Assert.IsTrue(material.shader.isSupported);
            Assert.IsTrue(material.HasProperty("_StencilComp"));
            foreach (var graphic in panel.GetComponentsInChildren<Graphic>(true)) Assert.AreSame(material, graphic.material);
        }

        [Test]
        public void WorldPanel_WaitsForActualHeadTrackingThenRemainsAnchoredUntilManualPlacement()
        {
            Vector3 initialPosition = Panel.position;
            _camera.transform.SetPositionAndRotation(new Vector3(4f, 1.6f, -3f), Quaternion.Euler(0f, 90f, 0f));
            for (int frame = 0; frame < 8; frame++) Invoke(_hud, "UpdateWorldPanelTracking", true);
            Assert.AreEqual(initialPosition, Panel.position, "Authored or stale camera poses must not consume the pending XR placement.");
            _hud.TrackedPose = new Pose(new Vector3(2f, 1.7f, -4f), Quaternion.Euler(0f, 90f, 0f));
            _hud.TrackingAvailable = true;
            Invoke(_hud, "UpdateWorldPanelTracking", true);
            Vector3 expected = new Vector3(3.25f, 1.6f, -4f);
            Assert.That(Vector3.Distance(expected, Panel.position), Is.LessThan(0.0001f));
            _camera.transform.position += Vector3.right;
            _hud.TrackedPose = new Pose(new Vector3(-2f, 1.2f, 3f), Quaternion.identity);
            Invoke(_hud, "UpdateWorldPanelTracking", true);
            Assert.That(Vector3.Distance(expected, Panel.position), Is.LessThan(0.0001f));
            Panel.Find("Move Panel Here").GetComponent<Button>().onClick.Invoke();
            Assert.That(Vector3.Distance(new Vector3(-2f, 1.1f, 4.25f), Panel.position), Is.LessThan(0.0001f));
        }

        [UnityTest]
        public IEnumerator TrackedRayPressAndRelease_RaycastThePanelAndLoadScenario()
        {
            int requestedScenario = -1;
            _bus.OnLoadScenario += scenario => requestedScenario = scenario;
            Panel.SetPositionAndRotation(new Vector3(0f, 0f, 2f), Quaternion.identity);
            yield return null;
            Canvas.ForceUpdateCanvases();

            var button = Panel.Find("2 · Swarmalators").GetComponent<Button>();
            var rect = button.GetComponent<RectTransform>();
            Vector3 target = rect.TransformPoint(rect.rect.center);
            var pointer = new TestTrackedPointer(_camera.transform.position, target);
            XRUIInputModule module = _eventSystem.GetComponent<XRUIInputModule>();
            module.enableMouseInput = false;
            module.RegisterInteractor(pointer);
            try
            {
                Invoke(module, "DoProcess");
                Assert.IsTrue(module.GetTrackedDeviceModel(pointer, out TrackedDeviceModel model));
                Assert.AreSame(button.gameObject, model.currentRaycast.gameObject, "The tracked ray must hit the clickable graphic.");
                pointer.Pressed = true;
                Invoke(module, "DoProcess");
                pointer.Pressed = false;
                Invoke(module, "DoProcess");
                Invoke(_hud, "ProcessDeferredUiActions");
                Assert.AreEqual(2, requestedScenario, "A real XR pointer press/release must invoke the scenario action.");
            }
            finally { module.UnregisterInteractor(pointer); }
        }

        [Test]
        public void LivePanelSettings_ApplyChangedValuesWithoutResettingPoseOrMode_AndDeferDuringCapture()
        {
            var config = ScriptableObject.CreateInstance<SpatialMASConfig>();
            try
            {
                var follow = _hud.GetComponent<XRPanelFollow>();
                var handle = Panel.GetComponentInChildren<XRPanelMoveHandle>(true);
                follow.SetPinned(true, new Pose(Vector3.zero, Quaternion.identity));
                Vector3 position = Panel.position;
                Quaternion rotation = Panel.rotation;
                config.researcherPanelRadius = 1.8f;
                config.researcherPanelAngularDeadZone = 12f;
                config.researcherPanelCatchUpSeconds = .4f;
                Assert.IsTrue(_hud.ApplyPanelSettings(config));
                Assert.AreEqual(1.8f, follow.Radius);
                Assert.AreEqual(12f, follow.AngularDeadZone);
                Assert.AreEqual(.4f, follow.CatchUpSeconds);
                Assert.IsTrue(follow.Pinned);
                Assert.AreEqual(position, Panel.position);
                Assert.AreEqual(rotation, Panel.rotation);
                Assert.IsFalse(_hud.ApplyPanelSettings(config), "Unchanged asset values must not reconfigure the panel.");
                var dragging = typeof(XRPanelMoveHandle).GetField("_dragging", BindingFlags.NonPublic | BindingFlags.Instance);
                dragging.SetValue(handle, true);
                config.researcherPanelRadius = 2.1f;
                _hud.ApplyPanelSettings(config);
                Assert.IsTrue(_hud.HasPendingPanelSettings);
                Assert.AreEqual(1.8f, follow.Radius, "Live radius must not change a captured arc.");
                dragging.SetValue(handle, false);
                Invoke(_hud, "TryApplyPanelSettings");
                Assert.AreEqual(2.1f, follow.Radius);
                Assert.IsFalse(_hud.HasPendingPanelSettings);
                Assert.IsTrue(follow.Pinned);
                Assert.AreEqual(position, Panel.position);
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test]
        public void LauncherToggle_ClosesVisiblePanelThenRecallsHiddenPanel()
        {
            var lifecycle = _hud.GetComponent<XRResearcherPanelLifecycle>();
            lifecycle.ToggleFromLauncher();
            Assert.IsFalse(lifecycle.Visible);
            Assert.IsFalse(Panel.gameObject.activeSelf);
            lifecycle.ToggleFromLauncher();
            Assert.IsTrue(lifecycle.Visible);
            Assert.IsTrue(Panel.gameObject.activeSelf);
            Assert.IsFalse(Panel.GetComponent<CanvasGroup>().blocksRaycasts);
            lifecycle.ResearcherMode = false;
            lifecycle.ToggleFromLauncher();
            Assert.IsFalse(lifecycle.Visible);
        }

        [Test]
        public void BraceletHold_CancelsLostEligibilityAndRequiresReleaseBeforeRearming()
        {
            var hold = new BraceletHoldState();
            Assert.IsFalse(hold.Step(true, true, 0.6f));
            Assert.IsFalse(hold.Step(false, true, 0.6f));
            Assert.AreEqual(0f, hold.Elapsed);
            Assert.IsFalse(hold.Step(true, true, 2f));
            Assert.IsFalse(hold.Step(true, false, 0f));
            Assert.IsTrue(hold.Step(true, true, 1f));
            Assert.IsFalse(hold.Step(true, true, 2f));
            hold.Step(true, false, 0f);
            Assert.IsFalse(hold.Step(true, true, 0.5f));
            hold.Step(true, false, 0f);
            Assert.IsFalse(hold.Step(true, true, 0.5f), "Contact exit must reset elapsed time.");
        }

        [Test]
        public void ParticipantMode_HidesPanelCancelsQueuedCommandAndRecoveryOnlyRecalls()
        {
            var lifecycle = _hud.GetComponent<XRResearcherPanelLifecycle>();
            int loads = 0;
            _bus.OnLoadScenario += _ => loads++;
            Panel.Find("2 · Swarmalators").GetComponent<Button>().onClick.Invoke();
            lifecycle.ResearcherMode = false;
            Assert.IsFalse(Panel.gameObject.activeSelf);
            Assert.IsFalse(Panel.GetComponent<CanvasGroup>().blocksRaycasts);
            Invoke(_hud, "ProcessDeferredUiActions");
            Assert.AreEqual(0, loads, "Hiding must discard unfinished queued panel commands.");
            _hud.TrackingAvailable = true;
            _hud.TrackedPose = new Pose(new Vector3(2f, 1.7f, -4f), Quaternion.identity);
            lifecycle.RecoverResearcherAccess();
            Assert.IsTrue(lifecycle.ResearcherMode);
            Assert.IsTrue(lifecycle.Visible);
            Assert.IsFalse(Panel.GetComponent<CanvasGroup>().blocksRaycasts, "Recall must wait for release before controls become live.");
            Assert.AreEqual(0, loads);
        }

        [Test]
        public void CloseAndRecall_PreservePanelStateAndRejectParticipantRecall()
        {
            var lifecycle = _hud.GetComponent<XRResearcherPanelLifecycle>();
            lifecycle.SetHeadsetActive(true);
            Vector3 position = Panel.position;
            lifecycle.Hide();
            Assert.IsFalse(Panel.gameObject.activeSelf);
            Assert.AreEqual(position, Panel.position);
            lifecycle.ResearcherMode = false;
            lifecycle.Recall();
            Assert.IsFalse(lifecycle.Visible);
            Assert.AreEqual(position, Panel.position);
        }

        private Transform Panel => _hud.transform.Find("SpatialResearcherWorldCanvas");

        private GameObject NewObject(string name)
        {
            var go = new GameObject(name);
            _objects.Add(go);
            return go;
        }

        private void InitializeHud() => _hud.Initialize(_bus, new SpatialCoordinateMapper(),
            null, null, null, null, null, null, null, null);

        private static void Invoke(object target, string method, params object[] args)
        {
            var declaringType = target is SpatialResearcherHUD ? typeof(SpatialResearcherHUD) : target.GetType();
            declaringType.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
        }

        private sealed class TestTrackedPointer : IUIInteractor
        {
            private readonly Vector3 _origin;
            private readonly Quaternion _rotation;
            private readonly List<Vector3> _points;
            public bool Pressed;

            public TestTrackedPointer(Vector3 origin, Vector3 target)
            {
                _origin = origin;
                _rotation = Quaternion.LookRotation(target - origin);
                _points = new List<Vector3> { origin, target + (target - origin).normalized };
            }

            public void UpdateUIModel(ref TrackedDeviceModel model)
            {
                model.position = _origin;
                model.orientation = _rotation;
                model.raycastPoints = _points;
                model.raycastLayerMask = ~0;
                model.select = Pressed;
            }

            public bool TryGetUIModel(out TrackedDeviceModel model)
            {
                model = default;
                return false;
            }
        }
    }

    // An explicit pose source keeps these regressions deterministic even when a headset is attached.
    public class TestWorldPanelHud : SpatialResearcherHUD
    {
        public bool TrackingAvailable;
        public Pose TrackedPose;

        protected override bool TryGetWorldHeadPose(out Pose pose)
        {
            pose = TrackedPose;
            return TrackingAvailable;
        }
    }
}
