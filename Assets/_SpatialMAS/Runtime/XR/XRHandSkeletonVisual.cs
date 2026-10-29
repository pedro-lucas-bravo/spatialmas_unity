using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Hands;

namespace SpatialMAS.XR
{
    /// <summary>
    /// Hosts the XR Hands meshes and translucent XRI materials used by the Laser XR reference.
    /// The component name is retained so previously generated scene references remain valid.
    /// </summary>
    public class XRHandSkeletonVisual : MonoBehaviour
    {
        [SerializeField] private XRHandPinchHysteresis _pinch;
        [SerializeField] private GameObject _handModel;
        [SerializeField] private GameObject _modelContainer;
        [SerializeField] private Handedness _modelHandedness;
        [SerializeField] private XRHandTrackingEvents _trackingEvents;
        [SerializeField] private XRHandSkeletonDriver _skeletonDriver;
        [SerializeField] private XRHandMeshController _meshController;
        [SerializeField] private SkinnedMeshRenderer _handRenderer;

        public GameObject HandModel => _handModel;
        public XRHandTrackingEvents TrackingEvents => _trackingEvents;
        public XRHandSkeletonDriver SkeletonDriver => _skeletonDriver;
        public XRHandMeshController MeshController => _meshController;
        public SkinnedMeshRenderer HandRenderer => _handRenderer;

        public void Configure(XRHandPinchHysteresis pinch)
        {
            _pinch = pinch;
            EnsureHandModel();
        }

        private void Awake() => EnsureHandModel();

        private void OnEnable()
        {
            EnsureHandModel();
            if (_modelContainer != null) _modelContainer.SetActive(true);
        }

        private void EnsureHandModel()
        {
            // Older generated scenes included a procedural MeshRenderer on this host.
            // Only the child SkinnedMeshRenderer should render now.
            if (TryGetComponent<MeshRenderer>(out var legacyRenderer)) legacyRenderer.enabled = false;
            if (_pinch == null) return;
            if (_handModel != null && _modelHandedness == _pinch.Handedness)
            {
                if (_modelContainer == null)
                    _modelContainer = _handModel.transform.parent == transform ? _handModel : _handModel.transform.parent.gameObject;
                return;
            }
            if (_handModel != null)
            {
                _handModel.SetActive(false);
                GameObject oldContainer = _handModel.transform.parent != transform ? _handModel.transform.parent.gameObject : _handModel;
                if (Application.isPlaying) Destroy(oldContainer);
                else DestroyImmediate(oldContainer);
            }

            string modelName = _pinch.Handedness == Handedness.Left ? "LeftHand" : "RightHand";
            GameObject model = Resources.Load<GameObject>("Hands/Models/" + modelName);
            Material depth = Resources.Load<Material>("Hands/Materials/DepthOnly");
            Material translucent = Resources.Load<Material>("Hands/Materials/Unity_Hand_Medium");
            if (model == null || depth == null || translucent == null)
            {
                Debug.LogError("[SpatialMAS] Laser XR hand mesh/material assets are missing from Resources/Hands.", this);
                return;
            }

            // Keep every container at identity below Camera Offset. The package skeleton driver
            // applies the tracking-origin wrist pose once, directly to the wrist bone.
            var container = new GameObject("Hand Mesh Rig");
            _modelContainer = container;
            container.SetActive(false);
            container.transform.SetParent(transform, false);
            _handModel = Instantiate(model, container.transform, false);
            _handModel.name = modelName + " Mesh";
            _handModel.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            _handModel.transform.localScale = Vector3.one;
            _modelHandedness = _pinch.Handedness;
            _handRenderer = _handModel.GetComponentInChildren<SkinnedMeshRenderer>(true);
            _handRenderer.sharedMaterials = new[] { depth, translucent };
            _handRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _handRenderer.receiveShadows = false;
            _handRenderer.enabled = false;

            _trackingEvents = _handModel.AddComponent<XRHandTrackingEvents>();
            _trackingEvents.handedness = _modelHandedness;
            _trackingEvents.updateType = XRHandTrackingEvents.UpdateTypes.Dynamic | XRHandTrackingEvents.UpdateTypes.BeforeRender;

            _skeletonDriver = _handModel.AddComponent<XRHandSkeletonDriver>();
            _skeletonDriver.jointTransformReferences = new List<JointToTransformReference>();
            foreach (Transform candidate in _handModel.GetComponentsInChildren<Transform>(true))
                if (candidate.name.EndsWith(XRHandJointID.Wrist.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    _skeletonDriver.rootTransform = candidate;
                    break;
                }
            if (_skeletonDriver.rootTransform == null)
            {
                Debug.LogError("[SpatialMAS] Hand mesh is missing its wrist bone.", this);
                container.SetActive(false);
                return;
            }
            XRHandSkeletonDriverUtility.FindJointsFromRoot(_skeletonDriver);
            _skeletonDriver.InitializeFromSerializedReferences();
            _skeletonDriver.handTrackingEvents = _trackingEvents;

            _meshController = _handModel.AddComponent<XRHandMeshController>();
            _meshController.handTrackingEvents = _trackingEvents;
            _meshController.handMeshRenderer = _handRenderer;
            _meshController.showMeshWhenTrackingIsAcquired = true;
            _meshController.hideMeshWhenTrackingIsLost = true;
            container.SetActive(isActiveAndEnabled);
        }

        private void OnDisable()
        {
            if (_modelContainer != null) _modelContainer.SetActive(false);
            if (_handRenderer != null) _handRenderer.enabled = false;
        }
    }
}
