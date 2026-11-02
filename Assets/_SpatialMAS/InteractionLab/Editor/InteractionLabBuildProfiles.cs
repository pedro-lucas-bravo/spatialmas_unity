using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace SpatialMAS.InteractionLab.Editor
{
    /// <summary>Persistent Unity 6 Android profiles with independent one-scene lists.</summary>
    public static class InteractionLabBuildProfiles
    {
        public const string ProfileFolder = "Assets/_SpatialMAS/InteractionLab/BuildProfiles";
        public const string LabProfilePath = ProfileFolder + "/InteractionLab_Android.asset";
        public const string MainProfilePath = ProfileFolder + "/Swarmalators_Android.asset";
        private const string AndroidPlatformGuid = "b9b35072a6f44c2e863f17467ea3dc13";

        [MenuItem("SpatialMAS/Interaction Lab/Build Profiles/Ensure Android profiles")]
        public static void EnsureProfiles()
        {
            EnsureLabProfile();
            EnsureMainProfile();
            AssetDatabase.SaveAssets();
        }

        public static BuildProfile EnsureLabProfile() => EnsureProfile(LabProfilePath,
            "InteractionLab_Android", InteractionLabEditor.ScenePath);

        public static BuildProfile EnsureMainProfile() => EnsureProfile(MainProfilePath,
            "Swarmalators_Android", InteractionLabEditor.MainScenePath);

        [MenuItem("SpatialMAS/Interaction Lab/Build Profiles/Select Interaction Lab Android")]
        public static void SelectLabProfile() => Select(EnsureLabProfile());

        [MenuItem("SpatialMAS/Interaction Lab/Build Profiles/Select Swarmalators Android")]
        public static void SelectMainProfile() => Select(EnsureMainProfile());

        private static void Select(BuildProfile profile)
        {
            BuildProfile.SetActiveBuildProfile(profile);
            Selection.activeObject = profile;
            EditorGUIUtility.PingObject(profile);
        }

        private static BuildProfile EnsureProfile(string path, string name, string scene)
        {
            var profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(path);
            if (profile == null)
            {
                Directory.CreateDirectory(ProfileFolder);
                AssetDatabase.Refresh();
                profile = BuildProfile.CreateBuildProfile(new GUID(AndroidPlatformGuid), name, null);
                string createdPath = AssetDatabase.GetAssetPath(profile);
                if (createdPath != path)
                {
                    string error = AssetDatabase.MoveAsset(createdPath, path);
                    if (!string.IsNullOrEmpty(error))
                        throw new InvalidOperationException("Cannot move Android build profile: " + error);
                }
            }
            profile.overrideGlobalScenes = true;
            profile.scenes = new[] { new EditorBuildSettingsScene(scene, true) };
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }
    }
}
