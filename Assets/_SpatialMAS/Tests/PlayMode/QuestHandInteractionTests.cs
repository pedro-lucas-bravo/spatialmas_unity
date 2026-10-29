using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using NUnit.Framework;
using SpatialMAS.XR;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace SpatialMAS.Tests.PlayMode
{
    public class QuestHandInteractionTests
    {
        // Matches the installed XR Hands MetaAimHandDeviceState layout. Write the complete
        // device state because isTracked/indexPressed are bit controls, not byte-aligned floats.
        [StructLayout(LayoutKind.Explicit, Size = 57)]
        private struct AimDeviceState
        {
            [FieldOffset(0)] public byte indexPressed;
            [FieldOffset(4)] public int aimFlags;
            [FieldOffset(8)] public float pinchStrengthIndex;
            [FieldOffset(24)] public int trackingState;
            [FieldOffset(28)] public byte isTracked;
            [FieldOffset(29)] public Vector3 devicePosition;
            [FieldOffset(41)] public Quaternion deviceRotation;
        }

        private readonly List<GameObject> _objects = new List<GameObject>();
        private MetaAimHand _testAim;
        private MetaAimHand _previousLeftAim;

        private GameObject NewObject(string name)
        {
            var go = new GameObject(name);
            _objects.Add(go);
            return go;
        }

        [TearDown]
        public void TearDown()
        {
            if (_testAim != null)
            {
                InputSystem.RemoveDevice(_testAim);
                MetaAimHand.left = _previousLeftAim;
                _testAim = null;
            }
            foreach (var go in _objects) if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
        }

        [Test]
        public void MetaAimPinch_DrivesUIReaderAndReleasesForSystemGesture()
        {
            _previousLeftAim = MetaAimHand.left;
            _testAim = InputSystem.AddDevice<MetaAimHand>();
            MetaAimHand.left = _testAim;
            var state = new AimDeviceState
            {
                isTracked = 1,
                trackingState = 3,
                aimFlags = (int)MetaAimFlags.Valid,
                indexPressed = 1,
                pinchStrengthIndex = 1f,
                deviceRotation = Quaternion.identity
            };
            InputState.Change(_testAim, state);
            var reader = NewObject("Left Pinch").AddComponent<XRHandPinchHysteresis>();
            reader.Handedness = Handedness.Left;
            var uiInput = XRInteractionRigSetup.ObjectReader("UI Press", reader);
            Invoke(reader, "Update");
            Assert.IsTrue(uiInput.ReadIsPerformed());
            Assert.IsTrue(uiInput.ReadWasPerformedThisFrame());
            Assert.AreEqual(1f, uiInput.ReadValue());

            state.aimFlags = (int)(MetaAimFlags.Valid | MetaAimFlags.SystemGesture);
            InputState.Change(_testAim, state);
            Invoke(reader, "Update");
            Assert.IsFalse(uiInput.ReadIsPerformed());
            Assert.IsTrue(uiInput.ReadWasCompletedThisFrame());
            Assert.AreEqual(0f, uiInput.ReadValue());
        }

        [Test]
        public void MetaAimPose_IsTransformedThroughTrackingOrigin()
        {
            _previousLeftAim = MetaAimHand.left;
            _testAim = InputSystem.AddDevice<MetaAimHand>();
            MetaAimHand.left = _testAim;
            InputState.Change(_testAim, new AimDeviceState
            {
                isTracked = 1,
                trackingState = 3,
                aimFlags = (int)MetaAimFlags.Valid,
                devicePosition = Vector3.forward,
                deviceRotation = Quaternion.Euler(0f, 20f, 0f)
            });
            var origin = NewObject("Origin").transform;
            origin.SetPositionAndRotation(new Vector3(2f, 1f, 3f), Quaternion.Euler(0f, 90f, 0f));
            var reader = NewObject("Left Pinch").AddComponent<XRHandPinchHysteresis>();
            reader.Handedness = Handedness.Left;
            reader.ConfigureTrackingOrigin(origin);
            Assert.IsTrue(reader.TryGetAimPoseWorld(out Pose pose));
            Assert.That(Vector3.Distance(new Vector3(3f, 1f, 3f), pose.position), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(Quaternion.Euler(0f, 110f, 0f), pose.rotation), Is.LessThan(0.001f));
        }

        [Test]
        public void ExistingGeneratedRig_GainsHandMeshesAndSeparateAimOrigins()
        {
            var rig = NewObject("XR Origin");
            rig.SetActive(false);
            var origin = rig.AddComponent<XROrigin>();
            var offset = new GameObject("Camera Offset");
            offset.transform.SetParent(rig.transform, false);
            origin.CameraFloorOffsetObject = offset;
            var camGo = new GameObject("Camera");
            camGo.transform.SetParent(offset.transform, false);
            var camera = camGo.AddComponent<Camera>();
            origin.Camera = camera;
            XRInteractionRigSetup.EnsureHandInteractors(camera);
            var visuals = rig.GetComponentsInChildren<XRHandSkeletonVisual>(true);
            Assert.AreEqual(2, visuals.Length);
            Object.DestroyImmediate(visuals[0].gameObject);
            // This call must repair rigs that already contain their interactors, rather than return early.
            XRInteractionRigSetup.EnsureHandInteractors(camera);
            Assert.AreEqual(2, rig.GetComponentsInChildren<XRHandSkeletonVisual>(true).Length);
            foreach (var interactor in rig.GetComponentsInChildren<NearFarInteractor>(true))
            {
                Assert.AreNotSame(interactor.transform, interactor.farInteractionCaster.castOrigin);
                Assert.AreEqual("Aim Pose", interactor.farInteractionCaster.castOrigin.name);
            }
        }

        [Test]
        public void HandMeshVisual_UsesReferenceMaterialsAndOfficialJointDrivers()
        {
            foreach (var handedness in new[] { Handedness.Left, Handedness.Right })
            {
                var reader = NewObject(handedness + " Reader").AddComponent<XRHandPinchHysteresis>();
                reader.Handedness = handedness;
                var visual = NewObject(handedness + " Mesh Visual").AddComponent<XRHandSkeletonVisual>();
                visual.Configure(reader);
                Assert.NotNull(visual.HandRenderer.sharedMesh);
                Assert.That(visual.HandRenderer.sharedMesh.vertexCount, Is.GreaterThan(100));
                Assert.AreEqual(26, visual.SkeletonDriver.jointTransformReferences.Count);
                Assert.AreEqual(handedness, visual.TrackingEvents.handedness);
                Assert.AreEqual(XRHandTrackingEvents.UpdateTypes.Dynamic | XRHandTrackingEvents.UpdateTypes.BeforeRender, visual.TrackingEvents.updateType);
                Assert.AreSame(visual.TrackingEvents, visual.SkeletonDriver.handTrackingEvents);
                Assert.AreSame(visual.TrackingEvents, visual.MeshController.handTrackingEvents);
                Assert.IsTrue(visual.MeshController.hideMeshWhenTrackingIsLost);
                Assert.AreEqual(Vector3.one, visual.HandModel.transform.localScale);
                Material[] materials = visual.HandRenderer.sharedMaterials;
                Assert.AreEqual(2, materials.Length);
                Assert.AreEqual("DepthOnly", materials[0].name);
                Assert.AreEqual("Unity_Hand_Medium", materials[1].name);
                Assert.IsTrue(materials[1].HasProperty("_MainColor"));
                Assert.That(materials[1].GetColor("_MainColor").a, Is.EqualTo(0.65882355f).Within(0.001f));
                Assert.AreEqual("Transparent", materials[1].GetTag("RenderType", false));
                Assert.That(materials[1].renderQueue, Is.GreaterThanOrEqualTo(3000));
                Vector3 physicalSize = Vector3.Scale(visual.HandRenderer.sharedMesh.bounds.size, visual.HandRenderer.transform.lossyScale);
                float largestSide = Mathf.Max(Mathf.Abs(physicalSize.x), Mathf.Abs(physicalSize.y), Mathf.Abs(physicalSize.z));
                Assert.That(largestSide, Is.InRange(0.05f, 0.5f), "Imported hand dimensions must remain in physical meters.");
            }
        }

        [Test]
        public void HandMeshVisual_WristAlignsUnderMovedTrackingOriginAndHidesOnTrackingLoss()
        {
            var origin = NewObject("Tracking Origin").transform;
            origin.SetPositionAndRotation(new Vector3(4f, 1f, -2f), Quaternion.Euler(0f, 90f, 0f));
            var reader = NewObject("Left Reader").AddComponent<XRHandPinchHysteresis>();
            reader.Handedness = Handedness.Left;
            var visual = NewObject("Hand Mesh Visual").AddComponent<XRHandSkeletonVisual>();
            visual.transform.SetParent(origin, false);
            visual.Configure(reader);
            var localPose = new Pose(new Vector3(0.2f, 1.1f, 0.4f), Quaternion.Euler(10f, 25f, 5f));
            visual.TrackingEvents.poseUpdated.Invoke(localPose);
            Assert.That(Vector3.Distance(origin.TransformPoint(localPose.position), visual.SkeletonDriver.rootTransform.position), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(origin.rotation * localPose.rotation, visual.SkeletonDriver.rootTransform.rotation), Is.LessThan(0.001f));
            var tracked = Field<Unity.XR.CoreUtils.Bindings.Variables.BindableVariable<bool>>(visual.TrackingEvents, "m_HandIsTracked");
            tracked.Value = true;
            Assert.IsTrue(visual.HandRenderer.enabled);
            visual.enabled = false;
            Assert.IsFalse(visual.HandRenderer.gameObject.activeInHierarchy);
            tracked.Value = false;
            tracked.Value = true;
            Assert.IsFalse(visual.HandRenderer.enabled, "Disabled host must not become visible on tracking acquisition.");
            visual.enabled = true;
            Assert.IsTrue(visual.HandRenderer.gameObject.activeInHierarchy);
            Assert.IsTrue(visual.HandRenderer.enabled, "Re-enabled host must restore the currently tracked mesh.");
            tracked.Value = false;
            Assert.IsFalse(visual.HandRenderer.enabled);
        }

        private static T Field<T>(object target, string name) =>
            (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

        private static void Invoke(object target, string method) =>
            target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    }
}
