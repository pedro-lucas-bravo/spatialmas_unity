using System.Reflection;
using NUnit.Framework;
using SpatialMAS.Core;
using UnityEngine;

namespace SpatialMAS.Tests.PlayMode
{
    public class CalibrationReferenceTests
    {
        private GameObject _root;
        private SpatialCalibrationReference _reference;
        private SimulationCommandBus _bus;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Calibration Regression");
            _bus = new SimulationCommandBus();
            _reference = _root.AddComponent<SpatialCalibrationReference>();
            _reference.Initialize(_bus, Vector3.zero, Quaternion.identity, true, false);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        [Test]
        public void ReferencePose_TransformsCoordinatesWithoutMovingOtherObjects()
        {
            var observed = new GameObject("Independent Object");
            try
            {
                observed.transform.SetPositionAndRotation(new Vector3(3f, 2f, 4f), Quaternion.Euler(10, 20, 30));
                var position = observed.transform.position;
                var rotation = observed.transform.rotation;
                string id = _reference.Snapshot.CalibrationId;
                _bus.SetCalibrationPose(new Vector3(2f, 2f, 4f), Quaternion.Euler(0, 90, 0));
                Assert.AreEqual(position, observed.transform.position);
                Assert.AreEqual(rotation, observed.transform.rotation);
                Assert.That(Vector3.Distance(_reference.WorldToReferencePosition(position), Vector3.forward), Is.LessThan(0.0001f));
                Quaternion expected = Quaternion.Inverse(_reference.Snapshot.Rotation) * rotation;
                Assert.That(Quaternion.Angle(expected, _reference.WorldToReferenceRotation(rotation)), Is.LessThan(0.001f));
                Assert.AreEqual(id, _reference.Snapshot.CalibrationId);
                Assert.AreEqual(1, _reference.Snapshot.Revision);
            }
            finally { Object.DestroyImmediate(observed); }
        }

        [Test]
        public void HiddenReference_RemainsAvailableAndChangesAreVersioned()
        {
            CalibrationSnapshot reported = default;
            _bus.OnCalibrationState += state => reported = state;
            _bus.SetCalibrationVisible(false);
            _bus.SetCalibrationPose(Vector3.one, Quaternion.identity);
            _bus.GetCalibration();
            Assert.IsTrue(_reference.gameObject.activeInHierarchy);
            Assert.AreEqual(Vector3.one, reported.Position);
            Assert.IsFalse(reported.Visible);
            Assert.AreEqual(2, reported.Revision);
            Assert.IsFalse(_reference.CanManipulate);
            Assert.IsFalse(_root.GetComponent<Collider>().enabled);
        }

        [Test]
        public void LockedReference_RejectsChangesAndRestoresUnitScale()
        {
            _bus.SetCalibrationLocked(true);
            _bus.SetCalibrationPose(Vector3.one, Quaternion.identity);
            _root.transform.position = Vector3.one * 4f;
            _root.transform.localScale = Vector3.one * 3f;
            typeof(SpatialCalibrationReference).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_reference, null);
            Assert.AreEqual(Vector3.zero, _reference.Snapshot.Position);
            Assert.AreEqual(Vector3.zero, _root.transform.position);
            Assert.AreEqual(Vector3.one, _root.transform.localScale);
            Assert.IsFalse(_reference.CanManipulate);
            _bus.SetCalibrationLocked(false);
            Assert.IsTrue(_reference.TrySetPose(Vector3.one, Quaternion.identity));
        }

        [Test]
        public void FineRotationAdjustment_IsRecordedAndQuaternionSignDoesNotChangeRevision()
        {
            Quaternion fine = Quaternion.Euler(0, 0.01f, 0);
            Assert.IsTrue(_reference.TrySetPose(Vector3.zero, fine));
            Assert.AreEqual(1, _reference.Snapshot.Revision);
            Assert.That(_reference.Snapshot.Rotation.y, Is.EqualTo(fine.y).Within(0.0000001f));
            Assert.IsTrue(_reference.TrySetPose(Vector3.zero, new Quaternion(-fine.x, -fine.y, -fine.z, -fine.w)));
            Assert.AreEqual(1, _reference.Snapshot.Revision);
        }

        [Test]
        public void InvalidPose_IsRejectedWithoutChangingRevision()
        {
            Assert.IsFalse(_reference.TrySetPose(new Vector3(float.NaN, 0, 0), Quaternion.identity));
            Assert.IsFalse(_reference.TrySetPose(Vector3.one, new Quaternion(0, 0, 0, 0)));
            Assert.IsFalse(_reference.TrySetPose(Vector3.one, new Quaternion(float.PositiveInfinity, 0, 0, 1)));
            Assert.AreEqual(0, _reference.Snapshot.Revision);
            Assert.IsTrue(_reference.TrySetPose(Vector3.one, new Quaternion(0, 0, 0, 2)));
            Assert.That(Quaternion.Dot(_reference.Snapshot.Rotation, _reference.Snapshot.Rotation), Is.EqualTo(1f).Within(0.0001f));
        }
    }
}
