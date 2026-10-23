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
}
