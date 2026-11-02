using NUnit.Framework;
using SpatialMAS.Editor;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.CompositionLayers;

namespace SpatialMAS.Tests.EditMode
{
    public class XRBuilderSettingsTests
    {
        [Test]
        public void ImportedSimulatorGuard_DisablesSimulationWhenLoaderOwnsAnUnwornHeadset()
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/_SpatialMAS/InteractionLab/Reference/Scripts/MR/MRSimulatorAutoHand.cs");
            var manager = XRGeneralSettings.Instance.Manager;
            var loaderProperty = typeof(XRManagerSettings).GetProperty("activeLoader");
            var previousLoader = manager.activeLoader;
            var loader = UnityEngine.ScriptableObject.CreateInstance<OpenXRLoader>();
            var go = new UnityEngine.GameObject("Hardware loader simulator regression");
            go.SetActive(false);
            try
            {
                // No device or session is initialized: an unworn headset must still exclude simulation.
                loaderProperty.SetValue(manager, loader);
                var component = go.AddComponent(script.GetClass());
                go.SetActive(true);
                script.GetClass().GetMethod("Awake", System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic).Invoke(component, null);
                Assert.IsFalse(go.activeSelf,
                    "A hardware XR loader must disable the simulator before its lifecycle runs, even without headset presence.");
            }
            finally
            {
                loaderProperty.SetValue(manager, previousLoader);
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(loader);
            }
        }

        [Test]
        public void ImportedSimulatorGuard_PreservesPhysicalHeadsetsBeforeSimulatorEnable()
        {
            var guard = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/_SpatialMAS/InteractionLab/Reference/Scripts/MR/MRSimulatorAutoHand.cs");
            var lifecycle = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Packages/com.unity.xr.interaction.toolkit/Runtime/Inputs/Simulation/SimulatedDeviceLifecycleManager.cs");
            Assert.NotNull(guard);
            Assert.NotNull(lifecycle);
            Assert.Less(EffectiveExecutionOrder(guard), EffectiveExecutionOrder(lifecycle),
                "The simulator must not remove the real HMD before the hardware guard runs.");
            var go = new UnityEngine.GameObject("Inactive simulator regression");
            go.SetActive(false);
            try
            {
                var manager = go.AddComponent(lifecycle.GetClass());
                var component = go.AddComponent(guard.GetClass());
                guard.GetClass().GetMethod("Awake", System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic).Invoke(component, null);
                Assert.IsFalse((bool)lifecycle.GetClass().GetProperty("removeOtherHMDDevices").GetValue(manager),
                    "Delayed Link initialization must also preserve physical HMD devices.");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        private static int EffectiveExecutionOrder(MonoScript script)
        {
            int customOrder = MonoImporter.GetExecutionOrder(script);
            if (customOrder != 0) return customOrder;
            var attribute = System.Attribute.GetCustomAttribute(script.GetClass(),
                typeof(UnityEngine.DefaultExecutionOrder)) as UnityEngine.DefaultExecutionOrder;
            return attribute != null ? attribute.order : 0;
        }

        [Test]
        public void ConfigureOpenXR_CreatesRegisteredManagersAndIsIdempotent()
        {
            SpatialMASEditorBuilder.ConfigureOpenXRMultiplatformSettings();
            Assert.IsTrue(EditorBuildSettings.TryGetConfigObject<XRGeneralSettingsPerBuildTarget>(XRGeneralSettings.settingsKey, out var targets));
            foreach (var target in new[] { BuildTargetGroup.Standalone, BuildTargetGroup.Android })
            {
                XRGeneralSettings settings = targets.SettingsForBuildTarget(target);
                Assert.NotNull(settings);
                Assert.IsTrue(settings.InitManagerOnStart);
                Assert.NotNull(settings.Manager);
                Assert.IsTrue(settings.Manager.automaticLoading);
                Assert.IsTrue(settings.Manager.automaticRunning);
                Assert.That(settings.Manager.activeLoaders, Has.Some.InstanceOf<OpenXRLoader>());
                Assert.IsTrue(OpenXRSettings.GetSettingsForBuildTargetGroup(target)
                    .GetFeature<OpenXRCompositionLayersFeature>().enabled,
                    "Meta passthrough needs the composition layer provider on both Link and Android.");
                var manager = settings.Manager;
                int loaderCount = manager.activeLoaders.Count;
                SpatialMASEditorBuilder.ConfigureOpenXRMultiplatformSettings();
                Assert.AreSame(manager, targets.SettingsForBuildTarget(target).Manager);
                Assert.AreEqual(loaderCount, manager.activeLoaders.Count);
            }
        }
    }
}
