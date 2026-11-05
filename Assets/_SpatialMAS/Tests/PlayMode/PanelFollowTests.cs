using NUnit.Framework;
using SpatialMAS.UI;
using UnityEngine;

namespace SpatialMAS.Tests.PlayMode
{
    public class PanelFollowTests
    {
        private GameObject _root, _panel;
        private XRPanelFollow _follow;
        private static Pose Head(Vector3 p, float yaw = 0f, float pitch = 0f, float roll = 0f) => new Pose(p, Quaternion.Euler(pitch, yaw, roll));
        private static Pose Home => Head(new Vector3(0, 1.7f, 0));

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Follow test");
            _panel = new GameObject("Panel");
            _follow = _root.AddComponent<XRPanelFollow>();
            _follow.Initialize(_panel.transform);
            _follow.Recall(Home);
        }

        [TearDown]
        public void TearDown() { Object.DestroyImmediate(_panel); Object.DestroyImmediate(_root); }
        private void Settle(Pose head, int frames = 300) { for (int i = 0; i < frames; i++) _follow.Step(head, true, false, .016f); }

        [Test]
        public void PitchRollAndNearVerticalHeading_DoNotFollowLookHeight()
        {
            Vector3 initial = _panel.transform.position;
            _follow.Step(Head(Home.position, 0, 50, 70), true, false, 1f);
            Assert.That(Vector3.Distance(initial, _panel.transform.position), Is.LessThan(.00001f));
            _follow.Recall(Head(Home.position, 65));
            initial = _panel.transform.position;
            _follow.Step(Head(Home.position, -90, 90), true, false, 1f);
            Assert.That(Vector3.Distance(initial, _panel.transform.position), Is.LessThan(.00001f));
            Assert.That(Vector3.Dot(_panel.transform.up, Vector3.up), Is.GreaterThan(.9999f));
        }

        [Test]
        public void AngularDeadZone_HoldsInsideAndRespondsContinuouslyAtBoundary()
        {
            Vector3 initial = _panel.transform.position;
            _follow.Step(Head(Home.position, 7.9f), true, false, .016f);
            Assert.That(Vector3.Distance(initial, _panel.transform.position), Is.LessThan(.00001f));
            _follow.Step(Head(Home.position, 8.1f), true, false, .016f);
            Assert.That(Vector3.Distance(initial, _panel.transform.position), Is.LessThan(.001f));
            _follow.Step(Head(Home.position, 90), true, false, .016f);
            Assert.That(_panel.transform.position.x, Is.GreaterThan(0f).And.LessThan(.2f));
        }

        [Test]
        public void HorizontalDeadZone_IsTwoDimensionalAndYawDoesNotMoveItsCenter()
        {
            Vector3 initial = _panel.transform.position;
            var lean = Head(Home.position + new Vector3(.05f, 0, .05f));
            _follow.Step(lean, true, false, 1f);
            Assert.AreEqual(initial, _panel.transform.position);
            Settle(Head(lean.position, 90));
            Vector3 center = _panel.transform.position - _panel.transform.forward * _follow.Radius;
            Assert.That(new Vector2(center.x, center.z).magnitude, Is.LessThan(.00001f), "Yaw must orbit the held follow center, not the leaned head.");
        }

        [Test]
        public void HorizontalHysteresis_StartsOutsideOuterContinuesBelowOuterAndStopsAtInner()
        {
            Pose moved = Head(Home.position + Vector3.right * .2f);
            Vector3 initial = _panel.transform.position;
            _follow.Step(moved, true, false, .016f);
            Assert.That(_panel.transform.position.x, Is.GreaterThan(0f).And.LessThan(.02f));
            Settle(moved);
            Assert.That(_panel.transform.position.x, Is.EqualTo(.15f).Within(.0002f));
            Vector3 settled = _panel.transform.position;
            Settle(Head(Home.position + Vector3.right * .22f));
            Assert.That(Vector3.Distance(settled, _panel.transform.position), Is.LessThan(.00001f));
            Assert.AreEqual(initial.y, _panel.transform.position.y);
        }

        [Test]
        public void VerticalChannel_UsesHeadPositionAndItsOwnThresholdAndTime()
        {
            Vector3 initial = _panel.transform.position;
            _follow.Step(Head(Home.position + Vector3.up * .07f, 0, 70), true, false, 1f);
            Assert.AreEqual(initial, _panel.transform.position);
            _follow.ConfigurePositionTuning(.1f, .05f, .01f, .08f, .04f, 1f);
            Pose moved = Head(Home.position + new Vector3(.2f, .2f, 0));
            _follow.Step(moved, true, false, .1f);
            Assert.That(_panel.transform.position.x, Is.GreaterThan(.14f));
            Assert.That(_panel.transform.position.y - initial.y, Is.GreaterThan(0f).And.LessThan(.02f));
            Settle(moved, 1000);
            Assert.That(_panel.transform.position.y, Is.EqualTo(initial.y + .16f).Within(.0002f));
        }

        [Test]
        public void PressDragAndTrackingLoss_FreezeBothPoseThenResumeGently()
        {
            Vector3 initial = _panel.transform.position;
            Quaternion rotation = _panel.transform.rotation;
            Pose moved = Head(Home.position + new Vector3(2, .5f, 3), 90);
            _follow.Step(moved, true, true, 1f);
            _follow.Step(moved, true, false, 1f, true);
            _follow.Step(moved, false, false, 1f);
            Assert.AreEqual(initial, _panel.transform.position);
            Assert.AreEqual(rotation, _panel.transform.rotation);
            _follow.Step(moved, true, true, .016f);
            Assert.AreEqual(initial, _panel.transform.position);
            _follow.Step(moved, true, false, .016f);
            Assert.That(Vector3.Distance(initial, _panel.transform.position), Is.GreaterThan(0f).And.LessThan(.4f));
        }

        [Test]
        public void SidePlacement_RemainsOnReleaseThenRecentersWhenLookingTowardIt_WithoutPermanentBias()
        {
            _panel.transform.position = Home.position + Vector3.right * _follow.Radius;
            _follow.CommitPlacement(Home);
            Vector3 released = _panel.transform.position;
            Settle(Home);
            Assert.That(Vector3.Distance(released, _panel.transform.position), Is.LessThan(.0001f));
            Settle(Head(Home.position, 90));
            Vector3 relative = Vector3.ProjectOnPlane(_panel.transform.position - Home.position, Vector3.up);
            Assert.That(Vector3.Angle(relative, Vector3.right), Is.LessThan(.01f), "Looking toward the panel must not add the old 90-degree drag offset again.");
            Settle(Head(Home.position, 150));
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(_panel.transform.eulerAngles.y, 150)), Is.LessThanOrEqualTo(8.01f));
        }

        [Test]
        public void DragCommit_PreservesReleasedPoseAndPreferredHeadHeightOffset()
        {
            Pose head = Head(new Vector3(1, 1.7f, 2), 90);
            Vector3 placed = head.position + new Vector3(2, 0, -1).normalized * _follow.Radius;
            placed.y = 1.2f;
            _panel.transform.position = placed;
            _follow.CommitPlacement(head);
            _follow.Step(head, true, false, .016f);
            Assert.That(Vector3.Distance(placed, _panel.transform.position), Is.LessThan(.00001f));
            Settle(Head(head.position + Vector3.up * .5f, 90));
            Assert.That(_panel.transform.position.y, Is.EqualTo(placed.y + .46f).Within(.0002f));
        }

        [Test]
        public void PinUnpinAndRecall_KeepModeAndResumeWithoutTeleport()
        {
            Pose moved = Head(new Vector3(2, 1.8f, 3), 100);
            _follow.SetPinned(true, moved);
            Vector3 initial = _panel.transform.position;
            Quaternion rotation = _panel.transform.rotation;
            _follow.Step(moved, true, false, 10f);
            Assert.AreEqual(initial, _panel.transform.position);
            Assert.AreEqual(rotation, _panel.transform.rotation);
            _follow.SetPinned(false, moved);
            _follow.Step(moved, true, false, .016f);
            Assert.That(Vector3.Distance(initial, _panel.transform.position), Is.LessThan(.3f));
            _follow.SetPinned(true, moved);
            _follow.ConfigureRadius(1.8f);
            _follow.Recall(moved);
            Assert.IsTrue(_follow.Pinned);
            Assert.That(Vector3.ProjectOnPlane(_panel.transform.position - moved.position, Vector3.up).magnitude, Is.EqualTo(1.8f).Within(.00001f));
        }

        [Test]
        public void ConfiguredDistance_ClampsInvalidOrderingAndKeepsPinnedPose()
        {
            _follow.ConfigureDistance(1.1f, .5f, .8f, -.1f);
            Assert.AreEqual(1.1f, _follow.MinimumDistance);
            Assert.AreEqual(1.1f, _follow.Radius);
            Assert.AreEqual(1.1f, _follow.MaximumDistance);
            Assert.AreEqual(0f, _follow.DepthDragDeadZone);
            _follow.SetPinned(true, Home);
            var pinned = _panel.transform.position;
            _follow.Step(Head(Home.position + Vector3.forward), true, false, 2f);
            Assert.AreEqual(pinned, _panel.transform.position);
            _follow.Recall(Home);
            Assert.That(Vector3.ProjectOnPlane(_panel.transform.position - Home.position, Vector3.up).magnitude,
                Is.EqualTo(1.1f).Within(.00001f));
        }

        [Test]
        public void LiveConfiguration_ChangesTargetsWithoutResettingPosePlacementOrMode()
        {
            _panel.transform.position = new Vector3(.6f, 1.2f, 1.1f);
            _follow.CommitPlacement(Home);
            Vector3 initial = _panel.transform.position;
            Quaternion rotation = _panel.transform.rotation;
            _follow.ConfigureRadius(1.8f);
            _follow.ConfigureTuning(12f, .5f);
            _follow.ConfigurePositionTuning(.2f, .1f, .5f, .12f, .06f, .4f);
            Assert.AreEqual(initial, _panel.transform.position);
            Assert.AreEqual(rotation, _panel.transform.rotation);
            _follow.Step(Home, true, true, 1f);
            Assert.AreEqual(initial, _panel.transform.position);
            _follow.Step(Home, true, false, .016f);
            Assert.That(Vector3.Distance(initial, _panel.transform.position), Is.LessThan(.05f));
            Assert.That(_panel.transform.position.y, Is.EqualTo(1.2f).Within(.00001f));
            _follow.SetPinned(true, Home);
            initial = _panel.transform.position;
            _follow.ConfigureTuning(0f, .01f);
            _follow.Step(Head(new Vector3(3, 2, 0), 90), true, false, 1f);
            Assert.IsTrue(_follow.Pinned);
            Assert.AreEqual(initial, _panel.transform.position);
        }
    }
}
