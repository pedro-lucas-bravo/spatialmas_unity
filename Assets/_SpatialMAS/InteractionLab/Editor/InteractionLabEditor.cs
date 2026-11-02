using System;
using System.IO;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpatialMAS.InteractionLab.Editor
{
    public static class InteractionLabEditor
    {
        public const string ScenePath = "Assets/_SpatialMAS/InteractionLab/Reference/Scenes/MR_HandsInteraction.unity";
        public const string MainScenePath = "Assets/_SpatialMAS/Scenes/SpatialMAS_Main.unity";

        [MenuItem("SpatialMAS/Interaction Lab/Open ported MR Hands Interaction")]
        public static void OpenScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            ValidateDependencies();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        [MenuItem("SpatialMAS/Interaction Lab/Validate dependency closure")]
        public static void ValidateDependencies() => ValidateScene(ScenePath);

        private static void ValidateScene(string scene)
        {
            AssetDatabase.Refresh();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scene) == null)
                throw new InvalidOperationException("Missing scene: " + scene);
            foreach (string dependency in AssetDatabase.GetDependencies(scene, true))
                if (dependency.StartsWith("helping_sources/", StringComparison.Ordinal))
                    throw new InvalidOperationException("Unimported source dependency: " + dependency);
            Debug.Log("[InteractionLab] Scene dependency paths validated: " + scene);
        }

        [MenuItem("SpatialMAS/Interaction Lab/Build isolated Android APK")]
        public static void BuildAndroidApk() => BuildSceneAndroidApk(ScenePath,
            "com.spatialmas.interactionlab", "Builds/InteractionLab/SpatialMAS_InteractionLab.apk", "InteractionLab_AndroidBuild.json");

        [MenuItem("SpatialMAS/Interaction Lab/Build Swarmalators Android test APK")]
        public static void BuildMainAndroidApk() => BuildSceneAndroidApk(MainScenePath,
            "com.spatialmas.pickingtest", "Builds/InteractionLab/SpatialMAS_Swarmalators.apk", "Swarmalators_AndroidBuild.json");

        [Serializable]
        private sealed class Result
        {
            public string result, scene, output, applicationIdentifier, utc, error;
            public double durationSeconds;
            public ulong totalBytes;
        }

        private static void BuildSceneAndroidApk(string scene, string identifier, string output, string resultFile)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; ++i)
                if (args[i] == "-interactionLabApk") output = args[i + 1];
            var result = new Result { result = "Running", scene = scene, output = Path.GetFullPath(output),
                applicationIdentifier = identifier, utc = DateTime.UtcNow.ToString("O") };
            string resultPath = Path.Combine("Logs/PickingIteration", resultFile);
            Directory.CreateDirectory(Path.GetDirectoryName(resultPath));
            File.WriteAllText(resultPath, JsonUtility.ToJson(result, true));
            bool previousBundle = EditorUserBuildSettings.buildAppBundle;
            string previousGradleHome = AndroidExternalToolsSettings.Gradle.userHomePath;
            BuildProfile previousProfile = BuildProfile.GetActiveBuildProfile();
            string previousIdentifier = PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android);
            try
            {
                string jdk = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines/AndroidPlayer/OpenJDK");
                if (!Directory.Exists(jdk)) throw new InvalidOperationException("Unity's Android JDK is missing: " + jdk);
                // Use Unity's launcher-supported Gradle setting, not a process environment override.
                // This bypasses stale ~/.gradle/gradle.properties without editing global configuration.
                // Keep dependency paths below Ninja's Windows MAX_PATH limit.
                string gradleHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".smg");
                Directory.CreateDirectory(gradleHome);
                AndroidExternalToolsSettings.Gradle.userHomePath = gradleHome;
                ValidateScene(scene);
                BuildProfile profile = scene == ScenePath
                    ? InteractionLabBuildProfiles.EnsureLabProfile()
                    : InteractionLabBuildProfiles.EnsureMainProfile();
                Directory.CreateDirectory(Path.GetDirectoryName(result.output));
                EditorUserBuildSettings.buildAppBundle = false;
                PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, identifier);
                BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerWithProfileOptions
                {
                    buildProfile = profile,
                    locationPathName = result.output,
                    options = BuildOptions.None
                });
                result.result = report.summary.result.ToString();
                result.durationSeconds = report.summary.totalTime.TotalSeconds;
                result.totalBytes = report.summary.totalSize;
                if (report.summary.result != BuildResult.Succeeded)
                    throw new InvalidOperationException("Android build failed: " + report.summary.result);
                Debug.Log("[InteractionLab] Built " + scene + " to " + result.output);
            }
            catch (Exception exception)
            {
                result.result = "Failed";
                result.error = exception.ToString();
                throw;
            }
            finally
            {
                AndroidExternalToolsSettings.Gradle.userHomePath = previousGradleHome;
                if (BuildProfile.GetActiveBuildProfile() != previousProfile)
                    BuildProfile.SetActiveBuildProfile(previousProfile);
                EditorUserBuildSettings.buildAppBundle = previousBundle;
                PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, previousIdentifier);
                result.utc = DateTime.UtcNow.ToString("O");
                File.WriteAllText(resultPath, JsonUtility.ToJson(result, true));
            }
        }
    }
}
