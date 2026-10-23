using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SpatialMAS.Editor
{
    /// <summary>
    /// Multiplatform Build Helper supporting:
    /// 1. Meta Quest 3 / Android OpenXR (ARM64 APK)
    /// 2. Apple Vision Pro (visionOS Xcode Project)
    /// 3. Windows Standalone (x64)
    /// 4. macOS Standalone (Universal / Apple Silicon)
    /// 5. Linux Standalone (x64 - enabled by our zero-Csound Native C# DSP engine)
    /// </summary>
    public static class MultiPlatformBuildProfileHelper
    {
        private static void EnsureSceneBuiltFirst()
        {
            if (!File.Exists(SpatialMASEditorBuilder.MainScenePath))
            {
                SpatialMASEditorBuilder.BuildAll();
            }
        }

        [MenuItem("SpatialMAS/Configure Multiplatform Build Settings", priority = 15)]
        public static void ConfigureMultiplatformSettings()
        {
            EnsureSceneBuiltFirst();
            SpatialMASEditorBuilder.ConfigureOpenXRMultiplatformSettings();
        }

        [MenuItem("SpatialMAS/Build Target/Build Meta Quest & Android OpenXR (.apk)", priority = 20)]
        public static void BuildMetaQuestAndroidApk()
        {
            EnsureSceneBuiltFirst();
            Directory.CreateDirectory("Builds/Android_OpenXR");

            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { SpatialMASEditorBuilder.MainScenePath },
                locationPathName = "Builds/Android_OpenXR/SpatialMAS_Quest.apk",
                target = BuildTarget.Android,
                options = BuildOptions.None
            };

            ExecuteBuild(options, "Meta Quest / Android OpenXR");
        }

        [MenuItem("SpatialMAS/Build Target/Build Apple Vision Pro (visionOS Xcode)", priority = 21)]
        public static void BuildAppleVisionProXcode()
        {
            EnsureSceneBuiltFirst();
            Directory.CreateDirectory("Builds/VisionOS");

            var options = new BuildPlayerOptions
            {
                scenes = new[] { SpatialMASEditorBuilder.MainScenePath },
                locationPathName = "Builds/VisionOS/SpatialMAS_VisionPro",
                target = BuildTarget.VisionOS,
                options = BuildOptions.None
            };

            ExecuteBuild(options, "Apple Vision Pro (visionOS)");
        }

        [MenuItem("SpatialMAS/Build Target/Build Windows Standalone (x64)", priority = 22)]
        public static void BuildWindowsStandalone()
        {
            EnsureSceneBuiltFirst();
            Directory.CreateDirectory("Builds/Windows64");

            var options = new BuildPlayerOptions
            {
                scenes = new[] { SpatialMASEditorBuilder.MainScenePath },
                locationPathName = "Builds/Windows64/SpatialMAS.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };

            ExecuteBuild(options, "Windows x64 Standalone");
        }

        [MenuItem("SpatialMAS/Build Target/Build macOS Standalone (.app)", priority = 23)]
        public static void BuildMacOSStandalone()
        {
            EnsureSceneBuiltFirst();
            Directory.CreateDirectory("Builds/macOS");

            var options = new BuildPlayerOptions
            {
                scenes = new[] { SpatialMASEditorBuilder.MainScenePath },
                locationPathName = "Builds/macOS/SpatialMAS.app",
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.None
            };

            ExecuteBuild(options, "macOS Standalone");
        }

        [MenuItem("SpatialMAS/Build Target/Build Linux Standalone (x64)", priority = 24)]
        public static void BuildLinuxStandalone()
        {
            EnsureSceneBuiltFirst();
            Directory.CreateDirectory("Builds/Linux64");

            var options = new BuildPlayerOptions
            {
                scenes = new[] { SpatialMASEditorBuilder.MainScenePath },
                locationPathName = "Builds/Linux64/SpatialMAS.x86_64",
                target = BuildTarget.StandaloneLinux64,
                options = BuildOptions.None
            };

            ExecuteBuild(options, "Linux x64 Standalone");
        }

        private static void ExecuteBuild(BuildPlayerOptions options, string label)
        {
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[MultiPlatformBuildProfileHelper] {label} build succeeded ({report.summary.totalSize / 1024} KB) -> {options.locationPathName}");
            }
            else
            {
                Debug.LogError($"[MultiPlatformBuildProfileHelper] {label} build failed with {report.summary.totalErrors} errors.");
            }
        }
    }
}
