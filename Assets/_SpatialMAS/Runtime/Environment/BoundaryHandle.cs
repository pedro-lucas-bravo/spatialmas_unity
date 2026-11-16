using System;
using SpatialMAS.Agents;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace SpatialMAS.Environment
{
    public enum BoundaryHandleShape { Sphere = 0, Cube = 1, Diamond = 2 }

    /// <summary>A translation control owned by a boundary.</summary>
    public sealed class BoundaryHandle : MonoBehaviour
    {
        private XRGrabInteractable _grab;
        private Action<Vector3> _movedByUser;
        private Vector3 _position;

        public BoundaryHandleShape Shape { get; private set; }
        public float SizeMeters { get; private set; }
        public Color Color { get; private set; }

        public void Initialize(BoundaryHandleShape shape, float sizeMeters, Color color, Vector3 position, Action<Vector3> movedByUser)
        {
            Shape = shape;
            SizeMeters = sizeMeters;
            _movedByUser = movedByUser;
            SetPosition(position);

            var graphics = GameObject.CreatePrimitive(shape == BoundaryHandleShape.Sphere ? PrimitiveType.Sphere : PrimitiveType.Cube);
            graphics.name = "Handle Graphics";
            graphics.transform.SetParent(transform, false);
            var sourceCollider = graphics.GetComponent<Collider>();
            sourceCollider.enabled = false;
            Destroy(sourceCollider);
            if (shape == BoundaryHandleShape.Diamond)
                graphics.GetComponent<MeshFilter>().sharedMesh = ProceduralMeshFactory.GetOrCreateDiamondMesh();
            var renderer = graphics.GetComponent<Renderer>();
            renderer.sharedMaterial = Resources.Load<Material>("Mat_SpatialMAS_AgentInstanced");
            Color = color;
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);
            renderer.SetPropertyBlock(block);

            var collider = gameObject.AddComponent<SphereCollider>();
            collider.radius = sizeMeters * 0.55f;
            graphics.transform.localScale = Vector3.one * sizeMeters;
            var body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            _grab = gameObject.AddComponent<XRGrabInteractable>();
            _grab.movementType = XRGrabInteractable.MovementType.Instantaneous;
            _grab.useDynamicAttach = true;
            _grab.trackRotation = false;
            _grab.throwOnDetach = false;
            _grab.attachEaseInTime = 0f;
        }

        internal void SetPosition(Vector3 position)
        {
            _position = position;
            transform.position = position;
        }

        public void MoveByUser(Vector3 position)
        {
            if (isActiveAndEnabled && !_position.Equals(position) &&
                float.IsFinite(position.x) && float.IsFinite(position.y) && float.IsFinite(position.z))
                _movedByUser?.Invoke(position);
        }

        private void LateUpdate()
        {
            // XRI drives the transform. Sample its final position, including the release frame.
            MoveByUser(transform.position);
        }
    }
}
