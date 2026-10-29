using System.Collections;
using NUnit.Framework;
using SpatialMAS.Audio;
using SpatialMAS.Core;
using SpatialMAS.XR;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

namespace SpatialMAS.Tests.PlayMode
{
    public class XRModeAndEmbodimentPlayModeTests
    {
        [UnityTest]
        public IEnumerator DelayedXRAndPassthroughInitialization_ReappliesRequestedMode()
        {
            var camGo = new GameObject("Delayed XR Camera");
            var cam = camGo.AddComponent<Camera>();
            var modeGo = new GameObject("Delayed XR Mode");
            var mode = modeGo.AddComponent<DelayedXRModeSwitcher>();
            var studio = new GameObject("Test Studio");
            var bus = new SimulationCommandBus();
            try
            {
                mode.Initialize(bus, cam, null, null, studio, XRDisplayMode.MRPassthrough);
                Assert.IsTrue(studio.activeSelf, "Keep a preview until the XR provider has started.");
                StringAssert.Contains("preview", mode.DisplayStatus);

                mode.HeadsetRunning = true;
                yield return null;
                StringAssert.Contains("unavailable", mode.DisplayStatus);
                Assert.IsTrue(studio.activeSelf);

                mode.PassthroughProviderReady = true;
                yield return null;
                Assert.IsFalse(studio.activeSelf, "Late provider initialization must hide the preview without a manual mode toggle.");
                Assert.AreEqual(XRDisplayMode.MRPassthrough, mode.CurrentMode);
                Assert.IsFalse(cam.allowHDR);

                bus.SetXRDisplayMode(XRDisplayMode.VRImmersive);
                mode.PassthroughProviderReady = false;
                yield return null;
                mode.PassthroughProviderReady = true;
                yield return null;
                Assert.AreEqual(XRDisplayMode.VRImmersive, mode.CurrentMode);
                Assert.IsTrue(studio.activeSelf, "Provider changes must preserve the user's VR choice.");
            }
            finally
            {
                Object.Destroy(modeGo);
                Object.Destroy(studio);
                Object.Destroy(camGo);
            }
        }

        [UnityTest]
        public IEnumerator XRModeSwitcher_TogglesPassthroughAlphaAndVRStudioEnvironment()
        {
            var bus = new SimulationCommandBus();
            var camGo = new GameObject("TestMainCam");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            var urpData = camGo.AddComponent<UniversalAdditionalCameraData>();

            var switcherGo = new GameObject("ModeSwitcher");
            var switcher = switcherGo.AddComponent<XRModeSwitcher>();
            switcher.Initialize(bus, cam, null, null, null, XRDisplayMode.MRPassthrough);

            var listenerGo = new GameObject("ListenerProxy");
            var listenerProxy = listenerGo.AddComponent<AudioListenerProxy>();
            listenerProxy.Initialize(bus, camGo.transform, true);

            yield return null;

            // Verify MR Passthrough state: Alpha must be 0.0 and PostProcessing must be false
            Assert.AreEqual(XRDisplayMode.MRPassthrough, switcher.CurrentMode);
            Assert.AreEqual(0f, cam.backgroundColor.a, 0.001f);
            Assert.IsFalse(urpData.renderPostProcessing);

            // Switch to VR Immersive via CommandBus (/xr/mode 1)
            bus.SetXRDisplayMode(XRDisplayMode.VRImmersive);
            yield return null;

            Assert.AreEqual(XRDisplayMode.VRImmersive, switcher.CurrentMode);
            Assert.AreEqual(1.0f, cam.backgroundColor.a, 0.001f);
            Assert.IsTrue(urpData.renderPostProcessing);

            // Verify AudioListenerProxy tracks Head Camera 6DoF
            camGo.transform.position = new Vector3(1.25f, 1.65f, -0.5f);
            yield return null;
            Assert.AreEqual(1.25f, listenerGo.transform.position.x, 0.001f);
            Assert.AreEqual(1.65f, listenerGo.transform.position.y, 0.001f);

            Object.Destroy(listenerGo);
            Object.Destroy(switcherGo);
            Object.Destroy(camGo);
        }
    }

    public class DelayedXRModeSwitcher : XRModeSwitcher
    {
        public bool HeadsetRunning;
        public bool PassthroughProviderReady;
        protected override bool IsPhysicalXRHeadsetRunning() => HeadsetRunning;
        protected override bool HasPassthroughProvider() => PassthroughProviderReady;
    }
}
