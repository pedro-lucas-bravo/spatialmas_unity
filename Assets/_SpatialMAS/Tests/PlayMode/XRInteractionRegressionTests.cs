using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using SpatialMAS.Agents;
using SpatialMAS.Audio;
using SpatialMAS.Core;
using SpatialMAS.UI;
using SpatialMAS.XR;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.XR;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace SpatialMAS.Tests.PlayMode
{
    public class XRInteractionRegressionTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();
        private Mouse _mouse;

        private GameObject NewObject(string name)
        {
            var go = new GameObject(name);
            _objects.Add(go);
            return go;
        }

        [TearDown]
        public void TearDown()
        {
            if (_mouse != null) InputSystem.RemoveDevice(_mouse);
            _mouse = null;
            foreach (var go in _objects) if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
        }

        [Test]
        public void ListenerOverride_MovesTheSoleEnabledListenerAndRebasesOnlyWhenDetached()
        {
            var bus = new SimulationCommandBus();
            var head = NewObject("Head");
            head.AddComponent<Camera>();
            var headListener = head.AddComponent<AudioListener>();
            var proxy = NewObject("Proxy").AddComponent<AudioListenerProxy>();
            proxy.Initialize(bus, head.transform, true);
            Assert.IsFalse(headListener.enabled);
            Assert.IsTrue(proxy.GetComponent<AudioListener>().enabled);
            bus.SetListenerLockedToHead(false);
            bus.SetListenerPositionUnity(new Vector3(2, 3, 4));
            Assert.AreEqual(new Vector3(2, 3, 4), proxy.GetComponent<AudioListener>().transform.position);
            var oldMapper = new SpatialCoordinateMapper(0.2f, Vector3.zero);
            var change = new SpatialFrameChange(oldMapper.OriginOffset, Quaternion.identity, oldMapper.WorldScale,
                Vector3.one, Quaternion.identity, 0.4f);
            bus.ApplySpatialFrameChange(change);
            Assert.AreEqual(change.TransformPosition(new Vector3(2, 3, 4)), proxy.transform.position);
            bus.SetListenerLockedToHead(true);
            var position = proxy.transform.position;
            bus.ApplySpatialFrameChange(change);
            Assert.AreEqual(position, proxy.transform.position);
        }

        [Test]
        public void TrackingPose_UsesMovedRotatedAndScaledCameraOffset()
        {
            var offset = NewObject("Tracking Origin").transform;
            offset.SetPositionAndRotation(new Vector3(4f, 1f, -2f), Quaternion.Euler(0f, 90f, 0f));
            offset.localScale = Vector3.one * 2f;
            Pose pose = XRInteractionRigSetup.TrackingPoseToWorld(new Pose(Vector3.forward, Quaternion.Euler(0f, 30f, 0f)), offset);
            Assert.That(Vector3.Distance(pose.position, new Vector3(6f, 1f, -2f)), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(pose.rotation, Quaternion.Euler(0f, 120f, 0f)), Is.LessThan(0.001f));
        }

        [Test]
        public void ExistingFallbackRig_GainsBoundHeadTrackingAndBothNearFarPinchInteractors()
        {
            var rigGo = NewObject("XR Origin");
            rigGo.SetActive(false);
            var origin = rigGo.AddComponent<XROrigin>();
            var offset = new GameObject("Camera Offset");
            offset.transform.SetParent(rigGo.transform, false);
            origin.CameraFloorOffsetObject = offset;
            var cameraObject = new GameObject("Head Camera");
            cameraObject.transform.SetParent(offset.transform, false);
            var camera = cameraObject.AddComponent<Camera>();
            origin.Camera = camera;
            cameraObject.AddComponent<TrackedPoseDriver>();
            XRInteractionRigSetup.ConfigureHeadTracking(camera);
            XRInteractionRigSetup.EnsureHandInteractors(camera);
            XRInteractionRigSetup.EnsureHandInteractors(camera);
            var driver = camera.GetComponent<TrackedPoseDriver>();
            Assert.AreEqual("<XRHMD>/centerEyePosition", driver.positionInput.action.bindings[0].path);
            Assert.AreEqual("<XRHMD>/centerEyeRotation", driver.rotationInput.action.bindings[0].path);
            Assert.AreEqual("<XRHMD>/trackingState", driver.trackingStateInput.action.bindings[0].path);
            var interactors = rigGo.GetComponentsInChildren<NearFarInteractor>(true);
            Assert.AreEqual(2, interactors.Length);
            foreach (var interactor in interactors)
            {
                Assert.AreEqual(XRInputButtonReader.InputSourceMode.ObjectReference, interactor.selectInput.inputSourceMode);
                Assert.IsInstanceOf<XRHandPinchHysteresis>(interactor.selectInput.GetObjectReference());
                Assert.AreSame(interactor.selectInput.GetObjectReference(), interactor.uiPressInput.GetObjectReference());
                Assert.IsTrue(interactor.enableUIInteraction);
                Assert.AreEqual(QueryTriggerInteraction.Collide, ((SphereInteractionCaster)interactor.nearInteractionCaster).physicsTriggerInteraction);
                Assert.AreEqual(QueryTriggerInteraction.Collide, ((CurveInteractionCaster)interactor.farInteractionCaster).raycastTriggerInteraction);
            }
        }

        [Test]
        public void PrefabWithExistingGrabInteractable_RegistersGrabLifecycle()
        {
            var go = NewObject("Prefab Agent");
            go.AddComponent<SphereCollider>();
            go.AddComponent<Rigidbody>().isKinematic = true;
            var grab = go.AddComponent<XRGrabInteractable>();
            var agent = go.AddComponent<AgentEntity>();
            agent.Configure(7, AgentShapeType.Cube, AgentMovementMode.Teleport, 0.1f, new SimulationCommandBus());
            grab.selectEntered.Invoke(new SelectEnterEventArgs());
            Assert.IsTrue(agent.IsGrabbedByUser);
            grab.selectExited.Invoke(new SelectExitEventArgs());
            Assert.IsFalse(agent.IsGrabbedByUser);
        }

        [Test]
        public void WorldPanel_HasTrackedUIControlsAndScenarioButtonRoutesThroughBus()
        {
            var bus = new SimulationCommandBus();
            int requestedScenario = -1;
            bus.OnLoadScenario += scenario => requestedScenario = scenario;
            var hud = NewObject("HUD").AddComponent<SpatialResearcherHUD>();
            hud.Initialize(bus, new SpatialCoordinateMapper(0.2f, Vector3.zero), null, null, null, null, null, null, null, null);
            var panel = hud.transform.Find("SpatialResearcherWorldCanvas");
            Assert.NotNull(panel.GetComponent<TrackedDeviceGraphicRaycaster>());
            Assert.NotNull(EventSystem.current.GetComponent<XRUIInputModule>());
            Assert.NotNull(panel.GetComponentInChildren<Slider>(true));
            var button = panel.Find("5 · Self-Assembly").GetComponent<Button>();
            button.onClick.Invoke();
            Invoke(hud, "ProcessDeferredUiActions");
            Assert.AreEqual(5, requestedScenario);
        }

        [Test]
        public void MousePresses_ReleaseForNonGrabbableAgentsAndRightButtonDrags()
        {
            var bus = new SimulationCommandBus();
            var events = new List<string>();
            bus.OnAgentControlClickEvent += (id, button, state) => events.Add($"{id}:{button}:{state}");
            var rig = CreateDesktopRig(bus, out var camera);
            var agent = CreateAgent(bus, AgentShapeType.Sphere, new Vector3(0f, 0f, 3f));
            var boundary = NewObject("Transparent Arena Boundary");
            boundary.transform.position = agent.transform.position;
            var collider = boundary.AddComponent<SphereCollider>();
            collider.isTrigger = true;
            collider.radius = 1.5f;
            Physics.SyncTransforms();
            Vector2 pointer = camera.WorldToScreenPoint(agent.transform.position);
            ProcessMouse(rig, new MouseState { position = pointer }.WithButton(MouseButton.Left));
            ProcessMouse(rig, new MouseState { position = pointer });
            ProcessMouse(rig, new MouseState { position = pointer }.WithButton(MouseButton.Right));
            ProcessMouse(rig, new MouseState { position = pointer + Vector2.right * 40f });
            CollectionAssert.AreEqual(new[] { "7:0:1", "7:0:0", "7:1:1", "7:1:0" }, events);
            ProcessMouse(rig, new MouseState { position = pointer }.WithButton(MouseButton.Right));
            rig.enabled = false;
            Assert.AreEqual("7:1:0", events[events.Count - 1]);
        }

        [Test]
        public void MouseGrab_OffCenterSurfaceHitDoesNotMoveUntilPointerMoves()
        {
            var bus = new SimulationCommandBus();
            var rig = CreateDesktopRig(bus, out var camera);
            var agent = CreateAgent(bus, AgentShapeType.Cube, new Vector3(1f, 0f, 3f));
            Vector3 original = agent.transform.position;
            Vector2 pointer = camera.WorldToScreenPoint(original);
            ProcessMouse(rig, new MouseState { position = pointer }.WithButton(MouseButton.Left));
            Assert.IsTrue(agent.IsGrabbedByUser);
            Assert.That(Vector3.Distance(original, agent.transform.position), Is.LessThan(0.0001f));
            ProcessMouse(rig, new MouseState { position = pointer });
            Assert.IsFalse(agent.IsGrabbedByUser);
        }

        private DesktopInteractionRig CreateDesktopRig(SimulationCommandBus bus, out Camera camera)
        {
            camera = NewObject("Desktop Camera").AddComponent<Camera>();
            camera.pixelRect = new Rect(0, 0, 800, 600);
            var rig = NewObject("Desktop Rig").AddComponent<DesktopInteractionRig>();
            rig.Initialize(camera, bus);
            rig.IgnoredScreenGuiRect = Rect.zero;
            _mouse = InputSystem.AddDevice<Mouse>();
            return rig;
        }

        private AgentEntity CreateAgent(SimulationCommandBus bus, AgentShapeType shape, Vector3 position)
        {
            var agent = NewObject("Agent").AddComponent<AgentEntity>();
            agent.Configure(7, shape, AgentMovementMode.Teleport, 0.3f, bus);
            agent.transform.position = position;
            Physics.SyncTransforms();
            return agent;
        }

        private void ProcessMouse(DesktopInteractionRig rig, MouseState state)
        {
            InputSystem.QueueStateEvent(_mouse, state);
            InputSystem.Update();
            Invoke(rig, "HandleMouseAgentInteraction", _mouse, null);
        }

        private static void Invoke(object target, string method, params object[] args)
        {
            target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
        }
    }
}
