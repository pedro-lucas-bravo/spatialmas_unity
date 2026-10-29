using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;
using UnityEngine.XR.Interaction.Toolkit.Attachment;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

namespace SpatialMAS.XR
{
    /// <summary>Shared scene-builder and runtime setup, including repair of older generated fallback rigs.</summary>
    public static class XRInteractionRigSetup
    {
        public static Transform GetTrackingOrigin(Camera camera)
        {
            var origin = camera != null ? camera.GetComponentInParent<XROrigin>(true) : null;
            return origin != null && origin.CameraFloorOffsetObject != null
                ? origin.CameraFloorOffsetObject.transform
                : origin != null ? origin.transform : null;
        }

        public static Pose TrackingPoseToWorld(Pose pose, Transform trackingOrigin)
        {
            return trackingOrigin == null ? pose : new Pose(
                trackingOrigin.TransformPoint(pose.position), trackingOrigin.rotation * pose.rotation);
        }

        public static void ConfigureHeadTracking(Camera camera)
        {
            if (camera == null) return;
            var driver = camera.GetComponent<TrackedPoseDriver>();
            if (driver == null) driver = camera.gameObject.AddComponent<TrackedPoseDriver>();
            // Preserve user-authored actions and repair only the unbound actions from older scenes.
            if (!HasBindings(driver.positionInput))
                driver.positionInput = new InputActionProperty(new InputAction("Head Position", InputActionType.Value,
                    "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));
            if (!HasBindings(driver.rotationInput))
                driver.rotationInput = new InputActionProperty(new InputAction("Head Rotation", InputActionType.Value,
                    "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));
            if (!HasBindings(driver.trackingStateInput))
                driver.trackingStateInput = new InputActionProperty(new InputAction("Head Tracking State", InputActionType.Value,
                    "<XRHMD>/trackingState", expectedControlType: "Integer"));
            driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
        }

        private static bool HasBindings(InputActionProperty input) => input.action != null && input.action.bindings.Count > 0;

        public static void EnsureHandInteractors(Camera camera)
        {
            Transform trackingOrigin = GetTrackingOrigin(camera);
            if (trackingOrigin == null) return;
            var rig = camera.GetComponentInParent<XROrigin>(true);
            // Repair already-generated fallback rigs as well as newly built scenes.
            foreach (var driver in rig.GetComponentsInChildren<XRTrackedHandInteractor>(true))
                driver.EnsureHandSupport();
            // Imported/custom rigs retain their own tracking drivers and interactors.
            if (rig.GetComponentInChildren<XRBaseInputInteractor>(true) != null) return;

            CreateHand(trackingOrigin, Handedness.Left);
            CreateHand(trackingOrigin, Handedness.Right);
        }

        private static void CreateHand(Transform trackingOrigin, Handedness handedness)
        {
            XRHandPinchHysteresis pinch = null;
            foreach (var reader in trackingOrigin.GetComponentsInChildren<XRHandPinchHysteresis>(true))
                if (reader.Handedness == handedness) { pinch = reader; break; }
            if (pinch == null)
            {
                var readerObject = new GameObject(handedness + " Hand Tracking");
                readerObject.transform.SetParent(trackingOrigin, false);
                pinch = readerObject.AddComponent<XRHandPinchHysteresis>();
                pinch.Handedness = handedness;
            }
            pinch.ConfigureTrackingOrigin(trackingOrigin);
            pinch.EnsureControllerFallbackBindings();

            // Configure before activation so XRI Awake/OnEnable sees complete references and input sources.
            var handObject = new GameObject(handedness + " Hand Interactor");
            handObject.SetActive(false);
            handObject.transform.SetParent(trackingOrigin, false);
            var near = handObject.AddComponent<SphereInteractionCaster>();
            near.castRadius = 0.045f;
            near.physicsTriggerInteraction = QueryTriggerInteraction.Collide;
            var far = handObject.AddComponent<CurveInteractionCaster>();
            far.castDistance = 5f;
            far.raycastTriggerInteraction = QueryTriggerInteraction.Collide;
            var attach = handObject.AddComponent<InteractionAttachController>();
            attach.useMomentum = false;
            var interactor = handObject.AddComponent<NearFarInteractor>();
            interactor.nearInteractionCaster = near;
            interactor.farInteractionCaster = far;
            interactor.interactionAttachController = attach;
            interactor.enableUIInteraction = true;
            interactor.selectInput = ObjectReader("Pinch Select", pinch);
            interactor.uiPressInput = ObjectReader("Pinch UI Press", pinch);

            var line = handObject.AddComponent<LineRenderer>();
            line.startWidth = line.endWidth = 0.003f;
            line.sharedMaterial = Resources.Load<Material>("Mat_SpatialMAS_AgentInstanced");
            var visual = handObject.AddComponent<CurveVisualController>();
            visual.lineRenderer = line;
            visual.curveInteractionDataProvider = interactor;

            var poseDriver = handObject.AddComponent<XRTrackedHandInteractor>();
            poseDriver.Configure(pinch, trackingOrigin, interactor, line, visual);
            handObject.SetActive(true);
        }

        public static XRInputButtonReader ObjectReader(string name, IXRInputButtonReader source)
        {
            var reader = new XRInputButtonReader(name, inputSourceMode: XRInputButtonReader.InputSourceMode.ObjectReference);
            reader.SetObjectReference(source);
            return reader;
        }
    }
}
