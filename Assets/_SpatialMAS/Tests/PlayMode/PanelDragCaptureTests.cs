using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using SpatialMAS.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace SpatialMAS.Tests.PlayMode
{
    public class PanelDragCaptureTests
    {
        private GameObject _root, _systemObject;
        private EventSystem _previous, _system;
        private XRUIInputModule _module;
        private XRResearcherPanelLifecycle _lifecycle;
        private XRPanelMoveHandle _handle;
        private MutablePointer _pointer;
        private Transform _panel;
        private Pose? _head = new Pose(Vector3.zero, Quaternion.identity);

        [SetUp]
        public void SetUp()
        {
            _head = new Pose(Vector3.zero, Quaternion.identity);
            _previous = EventSystem.current;
            _systemObject = new GameObject("Captured UI Test System", typeof(EventSystem), typeof(XRUIInputModule));
            _system = _systemObject.GetComponent<EventSystem>();
            EventSystem.current = _system;
            _module = _systemObject.GetComponent<XRUIInputModule>();
            _module.enableMouseInput = false;
            _root = new GameObject("Captured Panel Test");
            var panelObject = new GameObject("Panel", typeof(RectTransform), typeof(Canvas));
            _panel = panelObject.transform;
            _panel.SetParent(_root.transform);
            _panel.position = new Vector3(0, 0, 2);
            _lifecycle = _root.AddComponent<XRResearcherPanelLifecycle>();
            _lifecycle.Initialize(_panel, () => { });
            _handle = _panel.Find("PanelBackground").GetComponent<XRPanelMoveHandle>();
            _pointer = new MutablePointer();
            _module.RegisterInteractor(_pointer);
        }

        [TearDown]
        public void TearDown()
        {
            if (_module != null) _module.UnregisterInteractor(_pointer);
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_systemObject);
            if (_previous != null && _previous.isActiveAndEnabled) EventSystem.current = _previous;
        }

        [UnityTest]
        public IEnumerator RadialCapture_LargeSideMotionKeepsRadiusAndAdjustsHeight_ReleasePersists()
        {
            yield return null;
            var follow = _root.AddComponent<XRPanelFollow>();
            follow.Initialize(_panel);
            follow.ConfigureRadius(1.25f);
            follow.Recall(_head.Value);
            _handle.ConfigureRadial(follow, () => _head);
            ProcessModule();
            Assert.IsTrue(_module.GetTrackedDeviceModel(_pointer, out var model));
            var data = new TrackedDeviceEventData(_system) { pointerId = model.pointerId, rayPoints = _pointer.Points };
            _handle.OnPointerDown(data);
            Vector3 initial = _panel.position;
            _pointer.PointAt(new Vector3(4, 2, .2f));
            Tick();
            Assert.That(Vector3.ProjectOnPlane(_panel.position, Vector3.up).magnitude, Is.EqualTo(1.25f).Within(.00001f));
            Assert.That(_panel.position.y, Is.GreaterThan(initial.y));
            Assert.That(Vector3.Dot(_panel.up, Vector3.up), Is.GreaterThan(.999f));
            _head = new Pose(new Vector3(2f, 1f, 3f), Quaternion.identity);
            _pointer.MoveOrigin(_head.Value.position);
            Tick();
            Assert.That(Vector3.ProjectOnPlane(_panel.position - _head.Value.position, Vector3.up).magnitude, Is.EqualTo(1.25f).Within(.00001f));
            _pointer.Pressed = false;
            Tick();
            follow.CommitPlacement(_head.Value);
            Vector3 released = _panel.position;
            follow.Step(_head.Value, true, false, .016f);
            Assert.That(Vector3.Distance(released, _panel.position), Is.LessThan(.00001f));
            _pointer.PointAt(Vector3.left);
            Tick();
            Assert.That(Vector3.Distance(released, _panel.position), Is.LessThan(.00001f));
        }

        [UnityTest]
        public IEnumerator RadialCapture_DepthUsesRelativeHandTranslationAndKeepsReleasedDistance()
        {
            yield return null;
            var follow = _root.AddComponent<XRPanelFollow>();
            follow.Initialize(_panel);
            follow.ConfigureDistance(.65f, 1.25f, 2.25f, .03f);
            follow.Recall(_head.Value);
            _handle.ConfigureRadial(follow, () => _head);
            _pointer.MoveOrigin(Vector3.forward * .7f);
            ProcessModule();
            Assert.IsTrue(_module.GetTrackedDeviceModel(_pointer, out var model));
            var initial = _panel.position;
            _handle.OnPointerDown(new TrackedDeviceEventData(_system) { pointerId = model.pointerId, rayPoints = _pointer.Points });
            Assert.AreEqual(initial, _panel.position);

            var sideways = Quaternion.Euler(0f, 60f, 0f) * Vector3.forward;
            _pointer.MoveOrigin(sideways * .72f + Vector3.up * .2f);
            _pointer.PointAt(sideways);
            Tick();
            Assert.That(Vector3.ProjectOnPlane(_panel.position, Vector3.up).magnitude, Is.EqualTo(1.25f).Within(.00001f),
                "Sweeping sideways at the same radius must preserve panel distance.");
            _pointer.PointAt(Vector3.forward);
            _pointer.MoveOrigin(Vector3.forward * .93f + Vector3.up * .2f);
            Tick();
            Assert.That(_panel.position.z, Is.EqualTo(1.45f).Within(.00001f));
            Assert.That(_panel.position.y, Is.EqualTo(initial.y).Within(.00001f));

            _pointer.MoveOrigin(Vector3.forward * 5f);
            Tick();
            Assert.That(_panel.position.z, Is.EqualTo(2.25f).Within(.00001f));
            _pointer.MoveOrigin(Vector3.zero);
            Tick();
            Assert.That(_panel.position.z, Is.EqualTo(.65f).Within(.00001f));

            _pointer.Pressed = false;
            Tick();
            follow.CommitPlacement(_head.Value);
            follow.Step(_head.Value, true, false, 2f);
            Assert.That(_panel.position.z, Is.EqualTo(.65f).Within(.00001f));
            follow.Recall(_head.Value);
            Assert.That(_panel.position.z, Is.EqualTo(1.25f).Within(.00001f));
        }

        [UnityTest]
        public IEnumerator RadialCapture_TrackingLossCancelsWithoutResumingOldPress()
        {
            yield return null;
            var follow = _root.AddComponent<XRPanelFollow>();
            follow.Initialize(_panel);
            follow.Recall(_head.Value);
            _handle.ConfigureRadial(follow, () => _head);
            ProcessModule();
            _module.GetTrackedDeviceModel(_pointer, out var model);
            _handle.OnPointerDown(new TrackedDeviceEventData(_system) { pointerId = model.pointerId, rayPoints = _pointer.Points });
            _head = null;
            Tick();
            Assert.IsFalse(_handle.IsDragging);
            Vector3 stopped = _panel.position;
            _head = new Pose(Vector3.zero, Quaternion.identity);
            _pointer.PointAt(Vector3.right);
            Tick();
            Assert.AreEqual(stopped, _panel.position);
        }

        [UnityTest]
        public IEnumerator CapturedModel_ContinuesAcrossLargeMotionWithoutHoverOrDragEvents_ThenReleaseStops()
        {
            yield return null;
            Capture();
            Vector3 initial = _panel.position;
            _pointer.PointAt(new Vector3(4, 2, 2));
            Tick();
            Assert.That(Vector3.Distance(_panel.position, new Vector3(4, 2, 2)), Is.LessThan(.0001f));
            Assert.AreEqual(initial.z, _panel.position.z, "Capture preserves its initial plane.");
            _pointer.Pressed = false;
            Tick();
            Vector3 released = _panel.position;
            _pointer.PointAt(new Vector3(-4, -2, 2));
            _pointer.Pressed = true;
            Tick();
            Assert.AreEqual(released, _panel.position, "A fresh press outside the handle cannot resurrect capture.");
        }

        [UnityTest]
        public IEnumerator CapturedPointer_RejectsTransferAndSurvivesParallelRayUntilValidAgain()
        {
            yield return null;
            var data = Capture();
            _handle.OnPointerDown(new TrackedDeviceEventData(_system) { pointerId = data.pointerId + 1,
                rayPoints = new List<Vector3> { Vector3.zero, new Vector3(4, 0, 2) } });
            _handle.OnPointerUp(new PointerEventData(_system) { pointerId = data.pointerId + 1 });
            _pointer.PointAt(Vector3.right);
            Tick();
            Assert.AreEqual(new Vector3(0, 0, 2), _panel.position);
            _pointer.PointAt(new Vector3(-3, 1, 2));
            Tick();
            Assert.That(Vector3.Distance(_panel.position, new Vector3(-3, 1, 2)), Is.LessThan(.0001f));
        }


        [UnityTest]
        public IEnumerator HiddenPanel_CancelsCaptureAndDoesNotResumeAfterShow()
        {
            yield return null;
            Capture();
            _lifecycle.Hide();
            _pointer.PointAt(new Vector3(4, 0, 2));
            Tick();
            Assert.AreEqual(new Vector3(0, 0, 2), _panel.position);
            _lifecycle.Show();
            Tick();
            Assert.AreEqual(new Vector3(0, 0, 2), _panel.position);
        }

        private TrackedDeviceEventData Capture()
        {
            ProcessModule();
            Assert.IsTrue(_module.GetTrackedDeviceModel(_pointer, out var model));
            var data = new TrackedDeviceEventData(_system) { pointerId = model.pointerId, rayPoints = _pointer.Points };
            Assert.AreSame(_pointer, data.interactor);
            _handle.OnPointerDown(data);
            _handle.OnBeginDrag(data); // Must not reset the captured origin/offset.
            Assert.AreEqual(new Vector3(0, 0, 2), _panel.position, "Press must not jump the panel.");
            return data;
        }

        private void Tick()
        {
            ProcessModule();
            typeof(XRPanelMoveHandle).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(_handle, null);
        }

        private void ProcessModule() => typeof(XRUIInputModule).GetMethod("DoProcess", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(_module, null);

        private sealed class MutablePointer : IUIInteractor
        {
            public bool Pressed = true;
            public readonly List<Vector3> Points = new List<Vector3> { Vector3.zero, Vector3.forward * .1f };
            public void PointAt(Vector3 point) => Points[1] = Points[0] + point.normalized * .1f;
            public void MoveOrigin(Vector3 origin)
            {
                Points[1] += origin - Points[0];
                Points[0] = origin;
            }
            public void UpdateUIModel(ref TrackedDeviceModel model)
            {
                model.position = Points[0];
                model.orientation = Quaternion.LookRotation(Points[1] - Points[0]);
                model.raycastPoints = Points;
                model.raycastLayerMask = ~0;
                model.select = Pressed;
            }
            public bool TryGetUIModel(out TrackedDeviceModel model) { model = default; return false; }
        }
    }
}
