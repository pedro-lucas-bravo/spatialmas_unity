using NUnit.Framework;
using SpatialMAS.UI;
using UnityEngine;

namespace SpatialMAS.Tests.PlayMode
{
    public class DorsalLauncherGeometryTests
    {
        private static Pose Joint(Vector3 position) => new Pose(position, Quaternion.identity);

        [Test]
        public void LeftHandSurface_FacesDorsumAndPlacesReadableTopTowardFingers()
        {
            Assert.IsTrue(XRBraceletLauncher.TryBuildSurface(Joint(Vector3.zero),
                Joint(new Vector3(0, 0, .08f)), Joint(new Vector3(.025f, 0, .08f)),
                Joint(new Vector3(-.025f, 0, .08f)), .022f, out Pose surface));
            Assert.That(surface.position.y, Is.EqualTo(.022f).Within(.00001f));
            Assert.That(surface.position.z, Is.EqualTo(.048f).Within(.00001f));
            Assert.That(Vector3.Dot(-(surface.rotation * Vector3.forward), Vector3.up), Is.GreaterThan(.999f));
            Assert.That(Vector3.Dot(surface.rotation * Vector3.up, Vector3.forward), Is.GreaterThan(.999f));
            Assert.IsTrue(XRBraceletLauncher.IsSurfaceContact(surface, surface.position + Vector3.up * .008f));
            Assert.IsFalse(XRBraceletLauncher.IsSurfaceContact(surface, surface.position - Vector3.up * .008f));
            Assert.IsFalse(XRBraceletLauncher.IsSurfaceContact(surface, surface.position + Vector3.right * .032f));
            Assert.IsFalse(XRBraceletLauncher.IsSurfaceContact(surface, surface.position + Vector3.up * .02f));
        }

        [Test]
        public void RotatedTranslatedHand_PreservesSurfaceAndContactWithoutWristPose()
        {
            var rotation = Quaternion.Euler(47f, 123f, -25f);
            Vector3 offset = new Vector3(2f, 1f, -3f);
            Pose Transform(Vector3 point) => Joint(offset + rotation * point);
            Assert.IsTrue(XRBraceletLauncher.TryBuildSurface(Transform(Vector3.zero),
                Transform(new Vector3(0, 0, .08f)), Transform(new Vector3(.025f, 0, .08f)),
                Transform(new Vector3(-.025f, 0, .08f)), .022f, out Pose surface));
            Assert.That(Vector3.Distance(surface.position, offset + rotation * new Vector3(0, .022f, .048f)), Is.LessThan(.00001f));
            Assert.IsTrue(XRBraceletLauncher.IsSurfaceContact(surface, surface.position + rotation * Vector3.up * .008f));
            Assert.IsFalse(XRBraceletLauncher.IsSurfaceContact(surface, surface.position - rotation * Vector3.up * .01f));
        }

        [Test]
        public void Reveal_AllowsSideAndObliqueApproachButRejectsPalmAtAnyRotation()
        {
            foreach (var rotation in new[] { Quaternion.identity, Quaternion.Euler(90, 0, 0), Quaternion.Euler(30, 80, 130) })
            {
                var surface = new Pose(new Vector3(2, 1, -3), rotation);
                Vector3 Point(Vector3 local) => surface.position + rotation * local;
                Assert.IsTrue(XRBraceletLauncher.CanReveal(surface, Point(new Vector3(.06f, 0, .008f))));
                Assert.IsTrue(XRBraceletLauncher.CanReveal(surface, Point(new Vector3(.04f, .02f, -.03f))));
                Assert.IsFalse(XRBraceletLauncher.CanReveal(surface, Point(new Vector3(0, 0, .025f))));
                Assert.IsFalse(XRBraceletLauncher.CanReveal(surface, Point(new Vector3(.12f, 0, 0))));
            }
        }

        [Test]
        public void Contact_PadRadiusAndExitHysteresisCoverEdgeWithoutAcceptingPalm()
        {
            var surface = new Pose(Vector3.zero, Quaternion.identity);
            Assert.IsTrue(XRBraceletLauncher.IsSurfaceContact(surface, new Vector3(.024f, 0, 0)));
            Assert.IsFalse(XRBraceletLauncher.IsSurfaceContact(surface, new Vector3(.028f, 0, 0)));
            Assert.IsTrue(XRBraceletLauncher.IsSurfaceContact(surface, new Vector3(.028f, 0, 0), true));
            Assert.IsFalse(XRBraceletLauncher.IsSurfaceContact(surface, new Vector3(.032f, 0, 0), true));
            Assert.IsFalse(XRBraceletLauncher.IsSurfaceContact(surface, new Vector3(0, 0, .022f), true));
        }

        [Test]
        public void DegenerateHandFrame_IsRejected()
        {
            Assert.IsFalse(XRBraceletLauncher.TryBuildSurface(Joint(Vector3.zero), Joint(Vector3.zero),
                Joint(Vector3.right), Joint(Vector3.left), .022f, out _));
            Assert.IsFalse(XRBraceletLauncher.TryBuildSurface(Joint(Vector3.zero), Joint(Vector3.forward),
                Joint(Vector3.forward * 2), Joint(Vector3.zero), .022f, out _));
        }
    }
}
