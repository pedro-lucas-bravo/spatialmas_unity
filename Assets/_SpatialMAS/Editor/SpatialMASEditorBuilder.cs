using System;
using System.IO;
using SpatialMAS.Agents;
using SpatialMAS.Audio;
using SpatialMAS.Core;
using SpatialMAS.XR;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

namespace SpatialMAS.Editor
{
    /// <summary>
    /// Deterministic C# Editor Builder that generates all SpatialMAS materials, agent prefabs
    /// (Sphere, Cube, Pyramid), configuration asset, multiplatform XR Origin rig, OpenXR feature settings,
    /// and the main scene (Assets/_SpatialMAS/Scenes/SpatialMAS_Main.unity) without manual Inspector clicking.
    /// Automatically executes on domain reload if SpatialMAS_Main.unity has not been generated yet.
    /// </summary>
    [InitializeOnLoad]
    public static class SpatialMASEditorBuilder
    {
        public const string RootFolder = "Assets/_SpatialMAS";
        public const string MaterialsFolder = "Assets/_SpatialMAS/Materials";
        public const string PrefabsFolder = "Assets/_SpatialMAS/Prefabs";
        public const string ScenesFolder = "Assets/_SpatialMAS/Scenes";
        public const string MainScenePath = "Assets/_SpatialMAS/Scenes/SpatialMAS_Main.unity";
        public const string ConfigAssetPath = "Assets/_SpatialMAS/SpatialMASConfig.asset";

        private const string XriHandsRigSamplePath =
            "Assets/Samples/XR Interaction Toolkit/3.6.1/Hands Interaction Demo/Prefabs/XR Origin Hands (XR Rig).prefab";

        static SpatialMASEditorBuilder()
        {
            EditorApplication.delayCall += AutoBuildIfSceneMissing;
        }

        private static void AutoBuildIfSceneMissing()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            if (!File.Exists(MainScenePath))
            {
                Debug.Log("[SpatialMASEditorBuilder] Auto-initializing SpatialMAS prefabs, materials, OpenXR settings, and Main Scene...");
                BuildAll();
            }
        }

        [MenuItem("SpatialMAS/1. Build Complete Project (Materials, Prefabs, OpenXR & Main Scene)", priority = 1)]
        public static void BuildAll()
        {
            EnsureDirectoriesExist();
            ConfigureOpenXRMultiplatformSettings();

            SpatialMASConfig config = CreateOrLoadConfigAsset();
            Material agentLitMat = CreateOrLoadAgentMaterial();
            Material boundaryMat = CreateOrLoadBoundaryMaterial();

            CreateAgentPrefab(AgentShapeType.Sphere, "Agent_Sphere.prefab", agentLitMat);
            CreateAgentPrefab(AgentShapeType.Cube, "Agent_Cube.prefab", agentLitMat);
            CreateAgentPrefab(AgentShapeType.Pyramid, "Agent_Pyramid.prefab", agentLitMat);

            BuildMainScene(config, agentLitMat, boundaryMat);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            try
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllText("Logs/SpatialMAS_BuildStatus.txt",
                    $"SUCCESS: Built SpatialMAS_Main.unity, Prefabs, Materials, and OpenXR settings at {DateTime.UtcNow:O}");
            }
            catch { }

            Debug.Log("[SpatialMASEditorBuilder] Successfully built all SpatialMAS materials, prefabs, OpenXR settings, and SpatialMAS_Main.unity!");
        }

        [MenuItem("SpatialMAS/2. Configure OpenXR & Multiplatform XR Settings", priority = 2)]
        public static void ConfigureOpenXRMultiplatformSettings()
        {
            try
            {
                ConfigureBuildTargetOpenXR(BuildTargetGroup.Standalone);
                ConfigureBuildTargetOpenXR(BuildTargetGroup.Android);
                AssetDatabase.SaveAssets();
                Debug.Log("[SpatialMASEditorBuilder] Configured OpenXR Loader & Hand/MR Features for Standalone and Android.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SpatialMASEditorBuilder] OpenXR auto-configuration warning: {ex.Message}");
            }
        }

        private static void ConfigureBuildTargetOpenXR(BuildTargetGroup targetGroup)
        {
            var generalSettings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(targetGroup);
            if (generalSettings != null && generalSettings.Manager != null)
            {
                XRPackageMetadataStore.AssignLoader(generalSettings.Manager, typeof(OpenXRLoader).FullName, targetGroup);
            }

            FeatureHelpers.RefreshFeatures(targetGroup);
            OpenXRSettings openXrSettings = OpenXRSettings.GetSettingsForBuildTargetGroup(targetGroup);
            if (openXrSettings == null) return;

            var features = openXrSettings.GetFeatures();
            if (features == null) return;

            foreach (var feature in features)
            {
                if (feature == null) continue;
                string typeName = feature.GetType().Name;
                string featName = feature.name ?? string.Empty;

                // Enable cross-platform Hand Tracking, Hand Interaction Profile, Meta Quest MR/Camera, and Controller profiles
                if (typeName.IndexOf("Hand", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    typeName.IndexOf("MetaQuest", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    typeName.IndexOf("OculusTouch", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    typeName.IndexOf("EyeGaze", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    typeName.IndexOf("Camera", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    typeName.IndexOf("ARSession", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    featName.IndexOf("Hand", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    featName.IndexOf("Meta", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    feature.enabled = true;
                }
            }

            EditorUtility.SetDirty(openXrSettings);
        }

        private static void EnsureDirectoriesExist()
        {
            string[] dirs = { RootFolder, MaterialsFolder, PrefabsFolder, ScenesFolder };
            for (int i = 0; i < dirs.Length; i++)
            {
                if (!Directory.Exists(dirs[i]))
                {
                    Directory.CreateDirectory(dirs[i]);
                }
            }
        }

        private static SpatialMASConfig CreateOrLoadConfigAsset()
        {
            var config = AssetDatabase.LoadAssetAtPath<SpatialMASConfig>(ConfigAssetPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<SpatialMASConfig>();
                AssetDatabase.CreateAsset(config, ConfigAssetPath);
            }
            EditorUtility.SetDirty(config);
            return config;
        }

        private static Material CreateOrLoadAgentMaterial()
        {
            string path = $"{MaterialsFolder}/Mat_SpatialMAS_AgentInstanced.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                mat = new Material(shader)
                {
                    name = "Mat_SpatialMAS_AgentInstanced",
                    enableInstancing = true,
                    color = new Color(0.15f, 0.85f, 1.0f, 1.0f)
                };
                mat.SetColor("_BaseColor", new Color(0.15f, 0.85f, 1.0f, 1.0f));
                mat.SetFloat("_Smoothness", 0.82f);
                mat.SetFloat("_Metallic", 0.25f);
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", new Color(0.05f, 0.35f, 0.45f, 1.0f));
                AssetDatabase.CreateAsset(mat, path);
            }
            return mat;
        }

        private static Material CreateOrLoadBoundaryMaterial()
        {
            string path = $"{MaterialsFolder}/Mat_SpatialMAS_BoundaryTranslucent.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                mat = new Material(shader)
                {
                    name = "Mat_SpatialMAS_BoundaryTranslucent"
                };
                mat.SetFloat("_Surface", 1f);
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.renderQueue = 3000;
                mat.color = new Color(0.2f, 0.75f, 1.0f, 0.14f);
                mat.SetColor("_BaseColor", new Color(0.2f, 0.75f, 1.0f, 0.14f));
                AssetDatabase.CreateAsset(mat, path);
            }
            return mat;
        }

        private static GameObject CreateAgentPrefab(AgentShapeType shape, string fileName, Material mat)
        {
            string path = $"{PrefabsFolder}/{fileName}";
            GameObject tempGo = new GameObject(Path.GetFileNameWithoutExtension(fileName));

            var mf = tempGo.AddComponent<MeshFilter>();
            if (shape == AgentShapeType.Pyramid)
            {
                mf.sharedMesh = ProceduralMeshFactory.GetOrCreatePyramidMesh();
            }
            else
            {
                PrimitiveType prim = shape == AgentShapeType.Cube ? PrimitiveType.Cube : PrimitiveType.Sphere;
                GameObject primGo = GameObject.CreatePrimitive(prim);
                mf.sharedMesh = primGo.GetComponent<MeshFilter>().sharedMesh;
                UnityEngine.Object.DestroyImmediate(primGo);
            }

            var mr = tempGo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;

            var col = tempGo.AddComponent<SphereCollider>();
            col.radius = 0.5f;
            col.isTrigger = true;

            var rb = tempGo.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.isKinematic = true;

            tempGo.AddComponent<AudioSource>();
            tempGo.AddComponent<NativeDspSynthesizer>();
            tempGo.AddComponent<SpatialAudioBankPlayer>();
            tempGo.AddComponent<AgentVisualController>();
            tempGo.AddComponent<XRGrabInteractable>();
            tempGo.AddComponent<AgentEntity>();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(tempGo, path);
            UnityEngine.Object.DestroyImmediate(tempGo);
            return prefab;
        }

        public static void BuildMainScene(SpatialMASConfig config, Material agentMat, Material boundaryMat)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 1. Directional Light
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(0.96f, 0.97f, 1.0f, 1.0f);
            light.intensity = 1.1f;
            lightGo.transform.rotation = Quaternion.Euler(48f, -32f, 0f);

            // 2. AR Session (OpenXR / Quest 3 / Vision Pro / Android XR Passthrough lifecycle)
            var arSessionGo = new GameObject("AR Session");
            var arSession = arSessionGo.AddComponent<ARSession>();
            arSessionGo.AddComponent<ARInputManager>();

            // 3. XR Interaction Manager
            var xriMgrGo = new GameObject("XR Interaction Manager");
            xriMgrGo.AddComponent<XRInteractionManager>();

            // 4. Multiplatform XR Origin (uses XRI Hands Rig sample if imported, otherwise builds clean XR Origin)
            Camera mainCam = SetupMultiplatformXROrigin(out ARCameraManager arCamMgr);

            // 5. SpatialMAS Bootstrapper Root
            var bootstrapGo = new GameObject("SpatialMAS_System");
            var bootstrapper = bootstrapGo.AddComponent<SpatialMASBootstrapper>();

            var so = new SerializedObject(bootstrapper);
            so.FindProperty("_config").objectReferenceValue = config;
            so.FindProperty("_mainCamera").objectReferenceValue = mainCam;
            so.FindProperty("_arSession").objectReferenceValue = arSession;
            so.FindProperty("_arCameraManager").objectReferenceValue = arCamMgr;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, MainScenePath);

            // Register as default scene in Build Settings
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(MainScenePath, true)
            };
        }

        private static Camera SetupMultiplatformXROrigin(out ARCameraManager arCamMgr)
        {
            GameObject rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(XriHandsRigSamplePath);
            Camera mainCam = null;

            if (rigPrefab != null)
            {
                GameObject rigInstance = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab);
                rigInstance.name = "XR Origin Hands (Multiplatform)";
                PrefabUtility.UnpackPrefabInstance(rigInstance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

                var xrOrigin = rigInstance.GetComponent<XROrigin>();
                if (xrOrigin != null)
                {
                    xrOrigin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
                    mainCam = xrOrigin.Camera;
                }

                var interactors = rigInstance.GetComponentsInChildren<NearFarInteractor>(true);
                foreach (var nfi in interactors)
                {
                    if (nfi == null || nfi.transform.parent == null) continue;
                    string parentName = nfi.transform.parent.name;
                    if (parentName.Contains("Left") || parentName.Contains("Right"))
                    {
                        var pinch = nfi.gameObject.AddComponent<XRHandPinchHysteresis>();
                        pinch.Handedness = parentName.Contains("Left") ? Handedness.Left : Handedness.Right;
                        nfi.selectInput.SetObjectReference(pinch);
                        nfi.uiPressInput.SetObjectReference(pinch);
                    }
                }
            }

            if (mainCam == null)
            {
                var xrOriginGo = new GameObject("XR Origin (Multiplatform)");
                var xrOrigin = xrOriginGo.AddComponent<XROrigin>();
                xrOrigin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;

                var camOffsetGo = new GameObject("Camera Offset");
                camOffsetGo.transform.SetParent(xrOriginGo.transform, false);
                xrOrigin.CameraFloorOffsetObject = camOffsetGo;

                var camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                camGo.transform.SetParent(camOffsetGo.transform, false);
                camGo.transform.localPosition = new Vector3(0f, 1.35f, -0.6f);

                mainCam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
                xrOrigin.Camera = mainCam;

                var leftHandGo = new GameObject("Left Hand Tracking");
                leftHandGo.transform.SetParent(camOffsetGo.transform, false);
                var leftPinch = leftHandGo.AddComponent<XRHandPinchHysteresis>();
                leftPinch.Handedness = Handedness.Left;

                var rightHandGo = new GameObject("Right Hand Tracking");
                rightHandGo.transform.SetParent(camOffsetGo.transform, false);
                var rightPinch = rightHandGo.AddComponent<XRHandPinchHysteresis>();
                rightPinch.Handedness = Handedness.Right;
            }

            // Configure Passthrough defaults on Main Camera (Alpha = 0, PostProcessing = false, ARCameraManager active)
            mainCam.clearFlags = CameraClearFlags.SolidColor;
            mainCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            mainCam.nearClipPlane = 0.05f;

            var urpCam = mainCam.GetComponent<UniversalAdditionalCameraData>();
            if (urpCam == null) urpCam = mainCam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            urpCam.renderPostProcessing = false;

            arCamMgr = mainCam.GetComponent<ARCameraManager>();
            if (arCamMgr == null) arCamMgr = mainCam.gameObject.AddComponent<ARCameraManager>();

            return mainCam;
        }
    }
}
