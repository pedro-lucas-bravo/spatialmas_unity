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
