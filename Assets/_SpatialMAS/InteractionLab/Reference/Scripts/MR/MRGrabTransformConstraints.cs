using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Transformers;

namespace EnigmaLux.MR
{
    /// <summary>
    /// Optional, modular grab transform constraint component inspired by Meta Interaction SDK's
    /// OneGrabFreeTransformer, TwoGrabFreeTransformer, and DiscreteGrabRotation in human_swarm_ims.
    ///
    /// By default, objects in MR_HandsInteraction (such as MR_Cube_10cm) use full unconstrained
    /// 6DoF rotation and translation. Add and enable this component on any XRGrabInteractable
    /// only when you need axis locks, min/max angle limits, discrete rotation detents, or position bounds.
    /// </summary>
    [DisallowMultipleComponent]
    public class MRGrabTransformConstraints : XRBaseGrabTransformer
    {
        [Header("Position Constraints")]
        [SerializeField] private bool m_LockPositionX = false;
        [SerializeField] private bool m_LockPositionY = false;
        [SerializeField] private bool m_LockPositionZ = false;

        [Header("Rotation Axis Locks (e.g., Lock X & Z for Tabletop Yaw-Only)")]
        [SerializeField] private bool m_LockRotationX = false;
        [SerializeField] private bool m_LockRotationY = false;
        [SerializeField] private bool m_LockRotationZ = false;

        [Header("Discrete Rotation Detents (Optional - Matches DiscreteGrabRotation.cs)")]
        [Tooltip("When enabled, snaps rotation around the free axes to discrete degree increments (e.g. 15 or 45 degrees).")]
        [SerializeField] private bool m_UseDiscreteRotationSnap = false;

        [Range(1f, 90f)]
        [SerializeField] private float m_DiscreteAngleStepDegrees = 15f;

        private Vector3 m_InitialPosition;
        private Vector3 m_InitialEulerAngles;
        private Vector3 m_GrabbedLocalAttach;
        private bool m_HasGrabbedAttach = false;

        public bool LockPositionX { get => m_LockPositionX; set => m_LockPositionX = value; }
        public bool LockPositionY { get => m_LockPositionY; set => m_LockPositionY = value; }
        public bool LockPositionZ { get => m_LockPositionZ; set => m_LockPositionZ = value; }

        public bool LockRotationX { get => m_LockRotationX; set => m_LockRotationX = value; }
        public bool LockRotationY { get => m_LockRotationY; set => m_LockRotationY = value; }
        public bool LockRotationZ { get => m_LockRotationZ; set => m_LockRotationZ = value; }

        public bool UseDiscreteRotationSnap { get => m_UseDiscreteRotationSnap; set => m_UseDiscreteRotationSnap = value; }
        public float DiscreteAngleStepDegrees { get => m_DiscreteAngleStepDegrees; set => m_DiscreteAngleStepDegrees = value; }

        protected override RegistrationMode registrationMode => RegistrationMode.SingleAndMultiple;

        public override void OnLink(XRGrabInteractable grabInteractable)
        {
            base.OnLink(grabInteractable);
            m_InitialPosition = grabInteractable.transform.position;
            m_InitialEulerAngles = grabInteractable.transform.eulerAngles;
        }

        public override void OnGrab(XRGrabInteractable grabInteractable)
        {
            base.OnGrab(grabInteractable);
            m_InitialPosition = grabInteractable.transform.position;
            m_InitialEulerAngles = grabInteractable.transform.eulerAngles;

            Transform attachTrans = null;
            if (grabInteractable.interactorsSelecting.Count > 0)
            {
                attachTrans = grabInteractable.GetAttachTransform(grabInteractable.interactorsSelecting[0]);
            }
            if (attachTrans == null)
            {
                attachTrans = grabInteractable.attachTransform;
            }

            if (attachTrans != null && attachTrans != grabInteractable.transform)
            {
                m_GrabbedLocalAttach = grabInteractable.transform.InverseTransformPoint(attachTrans.position);
                m_HasGrabbedAttach = true;
            }
            else
            {
                m_GrabbedLocalAttach = Vector3.zero;
                m_HasGrabbedAttach = false;
            }
        }

        public override void Process(
            XRGrabInteractable grabInteractable,
            XRInteractionUpdateOrder.UpdatePhase updatePhase,
            ref Pose targetPose,
            ref Vector3 localScale)
        {
            if (!enabled)
                return;

            // 1. Apply Position Axis Locks if enabled
            if (m_LockPositionX || m_LockPositionY || m_LockPositionZ)
            {
                Vector3 pos = targetPose.position;
                if (m_LockPositionX) pos.x = m_InitialPosition.x;
                if (m_LockPositionY) pos.y = m_InitialPosition.y;
                if (m_LockPositionZ) pos.z = m_InitialPosition.z;
                targetPose.position = pos;
            }

            // 2. Apply Rotation Axis Locks or Discrete Detents if enabled
            if (m_LockRotationX || m_LockRotationY || m_LockRotationZ || m_UseDiscreteRotationSnap)
            {
                // Tabletop planar case: strictly lock pitch (X) and roll (Z) to gravity plane
                if (m_LockRotationX && m_LockRotationZ)
                {
                    Vector3 localAttach = m_HasGrabbedAttach ? m_GrabbedLocalAttach : Vector3.zero;
                    if (!m_HasGrabbedAttach && grabInteractable.attachTransform != null && grabInteractable.attachTransform != grabInteractable.transform)
                    {
                        localAttach = grabInteractable.transform.InverseTransformPoint(grabInteractable.attachTransform.position);
                    }

                    Vector3 pivotBefore = targetPose.position + targetPose.rotation * localAttach;

                    if (m_LockRotationY)
                    {
                        // Locked in all 3 rotation axes
                        Quaternion lockedRot = Quaternion.Euler(m_InitialEulerAngles.x, m_InitialEulerAngles.y, m_InitialEulerAngles.z);
                        targetPose.rotation = lockedRot;
                        if (localAttach.sqrMagnitude > 0.0001f)
                        {
                            targetPose.position = pivotBefore - lockedRot * localAttach;
                        }
                    }
                    else
                    {
                        // Free horizontal Yaw only: Project forward onto XZ plane to prevent any gimbal flips or tilt
                        Vector3 forward = targetPose.rotation * Vector3.forward;
                        forward.y = 0f;
                        float yaw;
                        if (forward.sqrMagnitude > 0.0001f)
                        {
                            yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
                        }
                        else
                        {
                            yaw = targetPose.rotation.eulerAngles.y;
                        }

                        if (m_UseDiscreteRotationSnap && m_DiscreteAngleStepDegrees > 0.1f)
                        {
                            yaw = Mathf.Round(yaw / m_DiscreteAngleStepDegrees) * m_DiscreteAngleStepDegrees;
                        }

                        Quaternion levelRot = Quaternion.Euler(0f, yaw, 0f);
                        targetPose.rotation = levelRot;

                        // CRITICAL: Compensate targetPose.position so the handle/grab pivot stays
                        // 100% stuck to the player's hand with zero vertical jumping or drift!
                        if (localAttach.sqrMagnitude > 0.0001f)
                        {
                            targetPose.position = pivotBefore - levelRot * localAttach;
                        }
                    }
                }
                else
                {
                    Vector3 euler = targetPose.rotation.eulerAngles;

                    if (m_LockRotationX) euler.x = m_InitialEulerAngles.x;
                    if (m_LockRotationY) euler.y = m_InitialEulerAngles.y;
                    if (m_LockRotationZ) euler.z = m_InitialEulerAngles.z;

                    if (m_UseDiscreteRotationSnap && m_DiscreteAngleStepDegrees > 0.1f)
                    {
                        if (!m_LockRotationX)
                            euler.x = Mathf.Round(euler.x / m_DiscreteAngleStepDegrees) * m_DiscreteAngleStepDegrees;
                        if (!m_LockRotationY)
                            euler.y = Mathf.Round(euler.y / m_DiscreteAngleStepDegrees) * m_DiscreteAngleStepDegrees;
                        if (!m_LockRotationZ)
                            euler.z = Mathf.Round(euler.z / m_DiscreteAngleStepDegrees) * m_DiscreteAngleStepDegrees;
                    }

                    targetPose.rotation = Quaternion.Euler(euler);
                }
            }
        }
    }
}

