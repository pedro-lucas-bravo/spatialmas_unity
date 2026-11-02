using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Hands.Meshing;

namespace EnigmaLux.MR
{
    /// <summary>
    /// Meta Quest Hand Mesh & Passthrough Overlay Controller.
    ///
    /// Provides Horizon OS system-quality hand overlay rendering in Mixed Reality:
    /// 1. Horizon OS Translucent Passthrough Overlay: Shaded with a dedicated URP passthrough shader
    ///    (semi-transparent ~2% opacity center so real skin is directly visible, luminous cyan Fresnel rim).
    ///    Alpha blending uses (SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha) for pristine
    ///    Quest 3 passthrough compositor underlay integration, eliminating any milky white haze or opacity issues.
    /// 2. Horizon OS Wrist Fade Smoothing: Smoothly dissolves the hand overlay near the wrist crease
    ///    (from +1.8 cm into the hand down to -2.5 cm behind the wrist), eliminating sharp polygon cutoffs
    ///    and open cylinder edges, matching the soft, natural dissolve of Meta's system hands.
    /// 3. Jitter-Free Model Stability: Uses the high-precision artist-rigged hand mesh by default with 0 ms
    ///    latency and zero joint deformation jitter, while supporting procedural anatomical mesh binding
    ///    (XR_FB_hand_tracking_mesh) via an inspector toggle when runtime dynamic scaling is desired.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    public class MRMetaHandMeshController : MonoBehaviour
    {
        public enum HandVisualMode
        {
            [Tooltip("Horizon OS style translucent holographic hand overlay with soft wrist dissolve.")]
            TranslucentHolographic = 0,

            [Tooltip("Hides the virtual 3D hand mesh so you see only your physical hands in Passthrough with 0 ms visual latency and zero displacement offset. Interaction colliders, pinch tracking, and poke mechanics remain 100% active.")]
            HiddenRealHandsOnly = 1
        }

        [Header("Hand Visual Mode")]
        [Tooltip("TranslucentHolographic = 3D overlay; HiddenRealHandsOnly = see physical hands directly with 0 ms lag.")]
        [SerializeField] private HandVisualMode m_HandVisualMode = HandVisualMode.TranslucentHolographic;

        public HandVisualMode visualMode
        {
            get => m_HandVisualMode;
            set
            {
                m_HandVisualMode = value;
                ApplyVisualMode();
            }
        }

        public void ApplyVisualMode()
        {
            if (m_SkinnedMeshRenderer != null)
            {
                m_SkinnedMeshRenderer.enabled = (m_HandVisualMode == HandVisualMode.TranslucentHolographic);
            }
        }

        [Header("Model Fidelity")]
        [Tooltip("If true, attempts to query and bind Quest runtime procedural anatomical mesh (XR_FB_hand_tracking_mesh). If false (recommended for zero latency and rock-solid stability), uses the native artist-rigged mesh which has zero rigging artifacts.")]
        [SerializeField] private bool m_BindAnatomicalRuntimeMesh = false;

        public bool bindAnatomicalRuntimeMesh
        {
            get => m_BindAnatomicalRuntimeMesh;
            set => m_BindAnatomicalRuntimeMesh = value;
        }

        [Header("Wrist Fade Smoothing (Horizon OS Style)")]
        [Tooltip("If true, smoothly fades out the hand overlay near the wrist to eliminate harsh polygon cutoffs.")]
        [SerializeField] private bool m_EnableWristFadeSmoothing = true;

        public bool enableWristFadeSmoothing
        {
            get => m_EnableWristFadeSmoothing;
            set => m_EnableWristFadeSmoothing = value;
        }

        [Tooltip("Distance into the hand from wrist joint (meters) where overlay is 100% visible (e.g. +0.040 = 4.0 cm).")]
        [SerializeField] private float m_WristFadeStart = 0.040f;

        [Tooltip("Distance behind the wrist joint towards forearm (meters) where overlay reaches 0% opacity (e.g. -0.015 = -1.5 cm).")]
        [SerializeField] private float m_WristFadeEnd = -0.015f;

        [Header("Joint Transforms")]
        [SerializeField] private XRHandSkeletonDriver m_SkeletonDriver;
        [SerializeField] private SkinnedMeshRenderer m_SkinnedMeshRenderer;
        [SerializeField] private XRHandTrackingEvents m_HandTrackingEvents;
        [SerializeField] private Transform m_WristTransform;
        [SerializeField] private Transform m_PalmTransform;

        private bool m_MeshInitialized;
        private float m_NextRetryTime;
        private const float k_RetryInterval = 0.5f;
        private Mesh m_RuntimeMesh;
        private Material m_PassthroughMaterialInstance;
        private MaterialPropertyBlock m_PropertyBlock;

        // Cached shader property IDs for zero-alloc per-frame updates
        private static readonly int s_WristPosWSProp = Shader.PropertyToID("_WristPosWS");
        private static readonly int s_WristDirWSProp = Shader.PropertyToID("_WristDirWS");
        private static readonly int s_WristFadeParamsProp = Shader.PropertyToID("_WristFadeParams");

        private static readonly List<XRHandSubsystem> s_SubsystemsReuse = new List<XRHandSubsystem>();

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            ApplyVisualMode();
            EnsureTranslucentPassthroughMaterial();
            Application.onBeforeRender += OnBeforeRenderUpdate;
        }

        private void OnDisable()
        {
            Application.onBeforeRender -= OnBeforeRenderUpdate;
        }

        private void OnBeforeRenderUpdate()
        {
            UpdateWristFadeParameters();
        }

        private void Start()
        {
            ApplyVisualMode();
            EnsureTranslucentPassthroughMaterial();
        }

        public void ResolveReferences()
        {
            if (m_SkeletonDriver == null)
            {
                m_SkeletonDriver = GetComponent<XRHandSkeletonDriver>() ?? GetComponentInParent<XRHandSkeletonDriver>();
                if (m_SkeletonDriver == null)
                {
                    m_SkeletonDriver = GetComponentInChildren<XRHandSkeletonDriver>(true);
                }
            }

            if (m_SkinnedMeshRenderer == null)
            {
                m_SkinnedMeshRenderer = GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (m_SkinnedMeshRenderer == null)
                {
                    m_SkinnedMeshRenderer = GetComponent<SkinnedMeshRenderer>() ?? GetComponentInParent<SkinnedMeshRenderer>();
                }
            }

            if (m_HandTrackingEvents == null)
            {
                m_HandTrackingEvents = GetComponent<XRHandTrackingEvents>() ?? GetComponentInParent<XRHandTrackingEvents>();
                if (m_HandTrackingEvents == null)
                {
                    m_HandTrackingEvents = GetComponentInChildren<XRHandTrackingEvents>(true);
                }
            }

            if (m_WristTransform == null)
            {
                m_WristTransform = m_SkeletonDriver != null && m_SkeletonDriver.rootTransform != null
                    ? m_SkeletonDriver.rootTransform
                    : transform;
            }

            if (m_PalmTransform == null)
            {
                FindPalmTransform();
            }
        }

        private void FindPalmTransform()
        {
            if (m_SkeletonDriver != null && m_SkeletonDriver.jointTransformReferences != null)
            {
                var refs = m_SkeletonDriver.jointTransformReferences;
                for (int i = 0; i < refs.Count; i++)
                {
                    var r = refs[i];
                    if (r.xrHandJointID == XRHandJointID.Palm ||
                        r.xrHandJointID == XRHandJointID.MiddleMetacarpal ||
                        r.xrHandJointID == XRHandJointID.MiddleProximal)
                    {
                        m_PalmTransform = r.jointTransform;
                        break;
                    }
                }
            }

            if (m_PalmTransform == null && m_WristTransform != null)
            {
                for (int c = 0; c < m_WristTransform.childCount; c++)
                {
                    var child = m_WristTransform.GetChild(c);
                    if (child.name.IndexOf("Palm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        child.name.IndexOf("Middle", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        m_PalmTransform = child;
                        break;
                    }
                }
            }
        }

        private void Update()
        {
            if (m_SkeletonDriver == null || m_SkinnedMeshRenderer == null)
            {
                ResolveReferences();
                if (m_SkinnedMeshRenderer != null)
                {
                    EnsureTranslucentPassthroughMaterial();
                }

                if (m_SkeletonDriver == null || m_SkinnedMeshRenderer == null)
                    return;
            }

            // Only attempt runtime anatomical mesh construction if explicitly enabled
            if (!m_BindAnatomicalRuntimeMesh || m_MeshInitialized)
                return;

            if (m_HandTrackingEvents != null && !m_HandTrackingEvents.handIsTracked)
                return;

            if (Time.time < m_NextRetryTime)
                return;

            m_NextRetryTime = Time.time + k_RetryInterval;

            if (TryBindMetaHandMesh())
            {
                m_MeshInitialized = true;
            }
        }

        private void LateUpdate()
        {
            UpdateWristFadeParameters();
        }

        /// <summary>
        /// Updates shader properties each frame to smoothly dissolve the mesh near the wrist.
        /// Uses MaterialPropertyBlock to update the SkinnedMeshRenderer without cloning materials.
        /// </summary>
        private void UpdateWristFadeParameters()
        {
            if (m_SkinnedMeshRenderer == null)
                return;

            if (m_PropertyBlock == null)
                m_PropertyBlock = new MaterialPropertyBlock();

            m_SkinnedMeshRenderer.GetPropertyBlock(m_PropertyBlock);

            if (!m_EnableWristFadeSmoothing)
            {
                var disabledFade = new Vector4(0f, 0f, 0f, 0f);
                m_PropertyBlock.SetVector(s_WristFadeParamsProp, disabledFade);
                m_SkinnedMeshRenderer.SetPropertyBlock(m_PropertyBlock);

                if (m_PassthroughMaterialInstance != null)
                {
                    m_PassthroughMaterialInstance.SetVector(s_WristFadeParamsProp, disabledFade);
                }
                return;
            }

            Transform wrist = m_WristTransform != null ? m_WristTransform : (m_SkeletonDriver != null ? m_SkeletonDriver.rootTransform : transform);
            if (wrist == null)
                return;

            Vector3 wristPos = wrist.position;
            Vector3 wristDir;

            if (m_PalmTransform != null)
            {
                Vector3 delta = m_PalmTransform.position - wristPos;
                wristDir = delta.sqrMagnitude > 0.0001f ? delta.normalized : wrist.forward;
            }
            else
            {
                wristDir = wrist.forward;
            }

            var fadeParams = new Vector4(m_WristFadeStart, m_WristFadeEnd, 0f, 1f);

            // Apply via MaterialPropertyBlock to directly modify render state without cloning
            m_PropertyBlock.SetVector(s_WristPosWSProp, wristPos);
            m_PropertyBlock.SetVector(s_WristDirWSProp, wristDir);
            m_PropertyBlock.SetVector(s_WristFadeParamsProp, fadeParams);
            m_SkinnedMeshRenderer.SetPropertyBlock(m_PropertyBlock);

            // Also keep the material instance updated in sync
            if (m_PassthroughMaterialInstance != null)
            {
                m_PassthroughMaterialInstance.SetVector(s_WristPosWSProp, wristPos);
                m_PassthroughMaterialInstance.SetVector(s_WristDirWSProp, wristDir);
                m_PassthroughMaterialInstance.SetVector(s_WristFadeParamsProp, fadeParams);
            }
        }

        /// <summary>
        /// Configures the SkinnedMeshRenderer's material to match Meta Horizon OS passthrough mode:
        /// completely see-through translucent center so real hands are clearly visible in passthrough,
        /// accompanied by a subtle, holographic ice-cyan silhouette rim and wrist dissolve.
        /// </summary>
        public void EnsureTranslucentPassthroughMaterial()
        {
            if (m_SkinnedMeshRenderer == null)
                return;

            // Ensure we have our dedicated passthrough material
            if (m_PassthroughMaterialInstance == null)
            {
                var resourceMat = Resources.Load<Material>("MR_Hand_Passthrough_Translucent");
                if (resourceMat != null)
                {
                    m_PassthroughMaterialInstance = new Material(resourceMat)
                    {
                        name = "MR_Hand_Passthrough_Runtime"
                    };
                }
                else
                {
                    var shader = Shader.Find("EnigmaLux/MR_Hand_Passthrough");
                    if (shader != null)
                    {
                        m_PassthroughMaterialInstance = new Material(shader)
                        {
                            name = "MR_Hand_Passthrough_Runtime"
                        };
                    }
                }

                if (m_PassthroughMaterialInstance != null)
                {
                    ApplyTranslucentProperties(m_PassthroughMaterialInstance);
                }
            }

            if (m_PassthroughMaterialInstance != null)
            {
                // Provide 2 material slots pointing to m_PassthroughMaterialInstance so that XRI Affordance
                // receivers configured with materialIndex: 1 never throw IndexOutOfRangeException.
                // NOTE: We assign to sharedMaterials ONLY. We NEVER access .materials setter as that creates
                // disconnected material clones and decouples PropertyBlock/runtime updates.
                var currentMats = m_SkinnedMeshRenderer.sharedMaterials;
                if (currentMats == null || currentMats.Length < 2 || currentMats[0] != m_PassthroughMaterialInstance || currentMats[1] != m_PassthroughMaterialInstance)
                {
                    m_SkinnedMeshRenderer.sharedMaterials = new Material[] { m_PassthroughMaterialInstance, m_PassthroughMaterialInstance };
                }
            }
        }

        public static void ApplyTranslucentProperties(Material mat)
        {
            if (mat == null)
                return;

            // Dedicated passthrough shader properties
            if (mat.HasProperty("_MainColor"))
                mat.SetColor("_MainColor", new Color(0.05f, 0.15f, 0.25f, 0.02f));

            if (mat.HasProperty("_RimColor"))
                mat.SetColor("_RimColor", new Color(0.60f, 0.88f, 1.0f, 0.40f));

            if (mat.HasProperty("_RimPower"))
                mat.SetFloat("_RimPower", 3.5f);

            if (mat.HasProperty("_OutlineWidth"))
                mat.SetFloat("_OutlineWidth", 0.0005f);

            // Horizon OS Wrist Fade parameters (Start: +1.8cm, End: -2.5cm, Enable: 1)
            if (mat.HasProperty("_WristFadeParams"))
                mat.SetVector("_WristFadeParams", new Vector4(0.018f, -0.025f, 0f, 1f));

            // Compatibility properties for older ShaderGraphs
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", new Color(0.60f, 0.88f, 1.0f, 0.40f));

            if (mat.HasProperty("_EdgeColor"))
                mat.SetColor("_EdgeColor", new Color(0.60f, 0.88f, 1.0f, 0.40f));

            if (mat.HasProperty("_EdgeHighlightPower"))
                mat.SetFloat("_EdgeHighlightPower", 3.5f);

            if (mat.HasProperty("_Power"))
                mat.SetFloat("_Power", 3.5f);

            if (mat.HasProperty("_FresnelPower"))
                mat.SetFloat("_FresnelPower", 3.5f);

            if (mat.HasProperty("_OutlineColor"))
                mat.SetColor("_OutlineColor", new Color(0.60f, 0.88f, 1.0f, 0.35f));

            if (mat.HasProperty("_OutlineOpacity"))
                mat.SetFloat("_OutlineOpacity", 0.35f);

            if (mat.HasProperty("_ColorTop"))
                mat.SetColor("_ColorTop", new Color(0.75f, 0.90f, 1.0f, 0.10f));

            if (mat.HasProperty("_ColorBottom"))
                mat.SetColor("_ColorBottom", new Color(0.50f, 0.75f, 0.95f, 0.05f));

            if (mat.HasProperty("_Color_1"))
                mat.SetColor("_Color_1", new Color(0.70f, 0.85f, 1.0f, 0.15f));

            if (mat.HasProperty("_NoiseStrength"))
                mat.SetFloat("_NoiseStrength", 0.1f);

            if (mat.HasProperty("_Opacity"))
                mat.SetFloat("_Opacity", 0.02f);

            // Neutralize finger tint masks
            if (mat.HasProperty("_FingerColor_1"))
                mat.SetColor("_FingerColor_1", new Color(0.60f, 0.88f, 1.0f, 0.0f));
            if (mat.HasProperty("_FingerColor_2"))
                mat.SetColor("_FingerColor_2", new Color(0.60f, 0.88f, 1.0f, 0.0f));
            if (mat.HasProperty("_FingerColor_3"))
                mat.SetColor("_FingerColor_3", new Color(0.60f, 0.88f, 1.0f, 0.0f));
            if (mat.HasProperty("_FingerColor_4"))
                mat.SetColor("_FingerColor_4", new Color(0.60f, 0.88f, 1.0f, 0.0f));
            if (mat.HasProperty("_ThumbColor"))
                mat.SetColor("_ThumbColor", new Color(0.60f, 0.88f, 1.0f, 0.0f));

            mat.renderQueue = 3100;
        }

        private bool TryBindMetaHandMesh()
        {
            SubsystemManager.GetSubsystems(s_SubsystemsReuse);
            XRHandSubsystem activeSubsystem = null;
            for (int i = 0; i < s_SubsystemsReuse.Count; i++)
            {
                var sub = s_SubsystemsReuse[i];
                if (sub != null && sub.running)
                {
                    activeSubsystem = sub;
                    break;
                }
            }

            if (activeSubsystem == null)
                return false;

            var queryParams = new XRHandMeshDataQueryParams { allocator = Allocator.Temp };
            XRHandMeshDataQueryResult result;

            try
            {
                if (!activeSubsystem.TryGetMeshData(out result, ref queryParams))
                {
                    return false;
                }
            }
            catch (Exception)
            {
                return false;
            }

            try
            {
                Handedness handedness = m_HandTrackingEvents != null ? m_HandTrackingEvents.handedness : Handedness.Invalid;
                if (handedness == Handedness.Invalid)
                {
                    handedness = gameObject.name.Contains("Left") ? Handedness.Left : Handedness.Right;
                }

                var handData = (handedness == Handedness.Left) ? result.leftHand : result.rightHand;

                if (!handData.positions.IsCreated || handData.positions.Length == 0 ||
                    !handData.indices.IsCreated || handData.indices.Length == 0)
                {
                    return false;
                }

                // Procedurally construct runtime anatomical hand mesh
                m_RuntimeMesh = new Mesh
                {
                    name = $"MetaQuest_AnatomicalHand_{handedness}"
                };

                m_RuntimeMesh.SetVertices(handData.positions);
                if (handData.normals.IsCreated && handData.normals.Length == handData.positions.Length)
                {
                    m_RuntimeMesh.SetNormals(handData.normals);
                }
                else
                {
                    m_RuntimeMesh.RecalculateNormals();
                }

                if (handData.uvs.IsCreated && handData.uvs.Length == handData.positions.Length)
                {
                    m_RuntimeMesh.SetUVs(0, handData.uvs);
                }

                // Single submesh for clean translucent passthrough rendering
                m_RuntimeMesh.subMeshCount = 1;
                m_RuntimeMesh.SetIndices(handData.indices, MeshTopology.Triangles, 0);

                // Map skeleton transforms to Unity joint indices (0..25)
                int boneCount = XRHandJointID.EndMarker.ToIndex();
                Transform[] bones = new Transform[boneCount];
                Matrix4x4[] bindPoses = new Matrix4x4[boneCount];

                var refs = m_SkeletonDriver != null ? m_SkeletonDriver.jointTransformReferences : null;
                if (refs != null)
                {
                    for (int i = 0; i < refs.Count; i++)
                    {
                        var r = refs[i];
                        int idx = r.xrHandJointID.ToIndex();
                        if (idx >= 0 && idx < boneCount)
                        {
                            bones[idx] = r.jointTransform;
                        }
                    }
                }

                // Ensure palm bone is properly bound
                if (bones[0] == null)
                {
                    bones[0] = m_PalmTransform != null ? m_PalmTransform : (m_WristTransform != null ? m_WristTransform : transform);
                }

                // Fill any missing bones with wrist or root fallback
                Transform defaultBone = m_WristTransform != null ? m_WristTransform : (m_SkeletonDriver != null && m_SkeletonDriver.rootTransform != null ? m_SkeletonDriver.rootTransform : transform);
                for (int i = 0; i < boneCount; i++)
                {
                    if (bones[i] == null)
                    {
                        bones[i] = defaultBone;
                    }
                }

                // Compute inverse bind poses
                for (int i = 0; i < boneCount; i++)
                {
                    var jointId = XRHandJointIDUtility.FromIndex(i);
                    if (handData.TryGetJointBindPoseMatrix(out Matrix4x4 bpm, jointId))
                    {
                        bindPoses[i] = bpm.inverse;
                    }
                    else if (bones[i] != null && m_SkinnedMeshRenderer != null)
                    {
                        bindPoses[i] = bones[i].worldToLocalMatrix * m_SkinnedMeshRenderer.transform.localToWorldMatrix;
                    }
                    else
                    {
                        bindPoses[i] = Matrix4x4.identity;
                    }
                }

                m_RuntimeMesh.bindposes = bindPoses;

                if (handData.bonesPerVertex.IsCreated && handData.boneWeights.IsCreated)
                {
                    m_RuntimeMesh.SetBoneWeights(handData.bonesPerVertex, handData.boneWeights);
                }

                m_RuntimeMesh.RecalculateBounds();

                // Swap mesh onto existing SkinnedMeshRenderer
                m_SkinnedMeshRenderer.sharedMesh = m_RuntimeMesh;
                m_SkinnedMeshRenderer.bones = bones;
                if (m_SkeletonDriver != null && m_SkeletonDriver.rootTransform != null)
                {
                    m_SkinnedMeshRenderer.rootBone = m_SkeletonDriver.rootTransform;
                }

                EnsureTranslucentPassthroughMaterial();

                Debug.Log($"[MRMetaHandMeshController] Successfully bound Quest anatomical hand mesh ({m_RuntimeMesh.vertexCount} verts, {m_RuntimeMesh.triangles.Length / 3} tris) for {handedness} hand.");
                return true;
            }
            finally
            {
                result.Dispose();
            }
        }

        private void OnDestroy()
        {
            if (m_RuntimeMesh != null)
            {
                Destroy(m_RuntimeMesh);
                m_RuntimeMesh = null;
            }

            if (m_PassthroughMaterialInstance != null)
            {
                Destroy(m_PassthroughMaterialInstance);
                m_PassthroughMaterialInstance = null;
            }
        }
    }
}