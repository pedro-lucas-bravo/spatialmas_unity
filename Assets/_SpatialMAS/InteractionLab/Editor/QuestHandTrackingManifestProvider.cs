using System;
using System.Collections.Generic;
using Unity.XR.Management.AndroidManifest.Editor;
using UnityEngine.XR.OpenXR;

namespace SpatialMAS.InteractionLab.Editor
{
    /// <summary>Requests hand tracking support and HIGH frequency; hardware controls the actual cadence.</summary>
    public sealed class QuestHandTrackingManifestProvider : IAndroidManifestRequirementProvider
    {
        public ManifestRequirement ProvideManifestRequirement()
        {
            return new ManifestRequirement
            {
                SupportedXRLoaders = new HashSet<Type> { typeof(OpenXRLoader) },
                NewElements = new List<ManifestElement>
                {
                    Element(new[] { "manifest", "uses-permission" }, "com.oculus.permission.HAND_TRACKING"),
                    Element(new[] { "manifest", "uses-feature" }, "oculus.software.handtracking", "required", "false"),
                    Element(new[] { "manifest", "application", "meta-data" }, "com.oculus.handtracking.frequency", "value", "HIGH"),
                    Element(new[] { "manifest", "application", "meta-data" }, "com.oculus.handtracking.version", "value", "V2.0")
                }
            };
        }

        private static ManifestElement Element(string[] path, string name, string extraKey = null, string extraValue = null)
        {
            var attributes = new Dictionary<string, string> { { "name", name } };
            if (extraKey != null) attributes.Add(extraKey, extraValue);
            return new ManifestElement { ElementPath = new List<string>(path), Attributes = attributes };
        }
    }
}
