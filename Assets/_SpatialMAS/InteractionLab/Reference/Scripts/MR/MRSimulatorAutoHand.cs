using System.Collections;
using System.Reflection;
using UnityEngine;
using Unity.XR.CoreUtils;

#if UNITY_EDITOR
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
#endif

namespace EnigmaLux.MR
{
    /// <summary>
    /// Ensures the in-editor simulator starts directly in Hand tracking mode,
    /// bridges modern XIT 3.6 ([ / ]) with classic shortcuts (Space / Shift),
    /// and provides seamless Room Navigation (Option/Alt + WASDQE or Right Mouse + WASDQE)
    /// to walk through MR room space while keeping head and hands unified.
    /// </summary>
    [DefaultExecutionOrder(-31000)]
    public class MRSimulatorAutoHand : MonoBehaviour
    {
#if UNITY_EDITOR
        [Header("Room Navigation Settings")]
        [Tooltip("Walking speed through room space in meters per second.")]
        [SerializeField] private float m_WalkSpeed = 2.0f;

        [Tooltip("Sprint multiplier when holding Shift during room navigation.")]
        [SerializeField] private float m_SprintMultiplier = 2.0f;

        [Tooltip("Show small helper HUD overlay in the top-left of Game View.")]
        [SerializeField] private bool m_ShowHelpOverlay = true;

        private XRInteractionSimulator m_Simulator;
        private MethodInfo m_SwitchDeviceModeMethod;
        private XROrigin m_XROrigin;
        private Camera m_Camera;
        private TargetedDevices m_ActiveHandTarget = TargetedDevices.RightDevice;
        private bool m_IsNavigatingRoom = false;

        private static bool HasHardwareXRLoader()
        {
            // Presence can be false while Link connects or the headset is unworn.
            // The initialized hardware loader still owns the hand provider.
            var settings = UnityEngine.XR.Management.XRGeneralSettings.Instance;
            return UnityEngine.XR.XRSettings.isDeviceActive ||
                (settings != null && settings.Manager != null && settings.Manager.activeLoader != null);
        }

        private void Awake()
        {
            // Preserve physical devices even if Link finishes initializing after scene Awake.
            var lifecycle = GetComponent<SimulatedDeviceLifecycleManager>();
            if (lifecycle != null) lifecycle.removeOtherHMDDevices = false;
            if (HasHardwareXRLoader())
            {
                gameObject.SetActive(false);
                return;
            }
            Application.runInBackground = true;
            m_Simulator = GetComponent<XRInteractionSimulator>();
            m_XROrigin = GetComponentInParent<XROrigin>();
            if (m_XROrigin == null)
            {
                m_XROrigin = FindAnyObjectByType<XROrigin>();
            }
            if (m_XROrigin != null)
            {
                m_Camera = m_XROrigin.Camera;
            }
            if (m_Camera == null)
            {
                m_Camera = Camera.main;
            }
        }

        private IEnumerator Start()
        {
            // Give simulator and XR subsystem 1 frame to initialize
            yield return null;

            EnsureHandMode();
        }

        private void Update()
        {
            if (HasHardwareXRLoader())
            {
                gameObject.SetActive(false);
                return;
            }
            EnsureHandMode();
            HandleRoomNavigationAndShortcuts();
        }

        private bool m_LoggedHandModeSwitch = false;

        private void EnsureHandMode()
        {
            var lifecycleMgr = SimulatedDeviceLifecycleManager.instance;
            if (lifecycleMgr != null && lifecycleMgr.deviceMode != SimulatedDeviceLifecycleManager.DeviceMode.Hand)
            {
                if (m_SwitchDeviceModeMethod == null)
                {
                    m_SwitchDeviceModeMethod = typeof(SimulatedDeviceLifecycleManager).GetMethod(
                        "SwitchDeviceMode",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
                    );
                }
                m_SwitchDeviceModeMethod?.Invoke(lifecycleMgr, null);
                if (!m_LoggedHandModeSwitch)
                {
                    m_LoggedHandModeSwitch = true;
                    Debug.Log("[MRSimulatorAutoHand] Automatically switched XR Interaction Simulator to Hand Tracking mode.");
                }
            }

#pragma warning disable 0618 // XRInteractionSimulator.targetedDeviceInput deprecation in XRI 3.6
            if (m_Simulator != null && (m_Simulator.currentState.targetedDeviceInput & TargetedDevices.FPS) != 0)
            {
                // Clear FPS mode so mouse point-and-click immediately targets the hand
                m_Simulator.targetedDeviceInput = m_ActiveHandTarget;
            }
#pragma warning restore 0618
        }

        private void HandleRoomNavigationAndShortcuts()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || m_Simulator == null) return;

            // 1. Hand Selection Shortcuts (Space / Shift / Bracket keys)
            bool shiftPressed = kb[Key.LeftShift].isPressed;
            bool spacePressed = kb[Key.Space].isPressed;

#pragma warning disable 0618
            if (shiftPressed && spacePressed)
            {
                m_ActiveHandTarget = TargetedDevices.LeftDevice | TargetedDevices.RightDevice;
                if (!m_IsNavigatingRoom) m_Simulator.targetedDeviceInput = m_ActiveHandTarget;
            }
            else if (spacePressed)
            {
                m_ActiveHandTarget = TargetedDevices.RightDevice;
                if (!m_IsNavigatingRoom) m_Simulator.targetedDeviceInput = m_ActiveHandTarget;
            }
            else if (shiftPressed && !kb[Key.W].isPressed && !kb[Key.A].isPressed && !kb[Key.S].isPressed && !kb[Key.D].isPressed)
            {
                // Only select left hand if not using Shift as sprint during movement
                m_ActiveHandTarget = TargetedDevices.LeftDevice;
                if (!m_IsNavigatingRoom) m_Simulator.targetedDeviceInput = m_ActiveHandTarget;
            }

            // Sync with XIT's own brackets [ / ] targeting if changed externally
            var currentTarget = m_Simulator.currentState.targetedDeviceInput;
            if (currentTarget != TargetedDevices.None && currentTarget != TargetedDevices.HMD)
            {
                m_ActiveHandTarget = currentTarget;
            }

            // 2. Room Navigation (Walk through MR space with Alt/Option or Right Mouse Button)
            bool altPressed = kb[Key.LeftAlt].isPressed || kb[Key.RightAlt].isPressed;
            bool rmbPressed = mouse != null && mouse.rightButton.isPressed;
            bool wantsRoomNav = altPressed || rmbPressed;

            if (wantsRoomNav && m_XROrigin != null)
            {
                Vector3 forward = m_Camera != null ? m_Camera.transform.forward : Vector3.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude > 0.001f) forward.Normalize();

                Vector3 right = m_Camera != null ? m_Camera.transform.right : Vector3.right;
                right.y = 0f;
                if (right.sqrMagnitude > 0.001f) right.Normalize();

                Vector3 moveDir = Vector3.zero;
                if (kb[Key.W].isPressed) moveDir += forward;
                if (kb[Key.S].isPressed) moveDir -= forward;
                if (kb[Key.D].isPressed) moveDir += right;
                if (kb[Key.A].isPressed) moveDir -= right;
                if (kb[Key.E].isPressed) moveDir += Vector3.up;
                if (kb[Key.Q].isPressed) moveDir -= Vector3.up;

                if (moveDir.sqrMagnitude > 0.001f)
                {
                    // While actively translating the player body/room, suspend hand translation
                    if (!m_IsNavigatingRoom)
                    {
                        m_Simulator.targetedDeviceInput = TargetedDevices.None;
                        m_IsNavigatingRoom = true;
                    }

                    float speed = m_WalkSpeed;
                    if (kb[Key.LeftShift].isPressed || kb[Key.RightShift].isPressed)
                    {
                        speed *= m_SprintMultiplier;
                    }

                    m_XROrigin.transform.position += moveDir.normalized * (speed * Time.unscaledDeltaTime);
                    return;
                }
            }

            // When Room Navigation finishes, restore active hand target
            if (m_IsNavigatingRoom)
            {
                m_Simulator.targetedDeviceInput = m_ActiveHandTarget;
                m_IsNavigatingRoom = false;
            }
#pragma warning restore 0618
        }

        private void OnGUI()
        {
            if (!m_ShowHelpOverlay || !Application.isPlaying) return;

            float boxWidth = 340f;
            float boxHeight = 135f;
            float margin = 10f;
            Rect boxRect = new Rect(Screen.width - boxWidth - margin, margin, boxWidth, boxHeight);

            GUI.color = new Color(0.08f, 0.08f, 0.1f, 0.92f);
            GUI.Box(boxRect, GUIContent.none);
            GUI.color = Color.white;

            GUILayout.BeginArea(new Rect(boxRect.x + 10, boxRect.y + 8, boxRect.width - 20, boxRect.height - 16));
            GUILayout.Label("<b>EnigmaLux MR Simulator Controls</b>");
            GUILayout.Label("<color=#00F0FF>• Move Hand:</color> WASDQE | Depth: Scroll Wheel");
            GUILayout.Label("<color=#FFD700>• Room Walk:</color> Alt + WASDQE (or RMB + WASD)");
            GUILayout.Label("<color=#A0FFA0>• Head Look:</color> Arrow Keys or Hold RMB + Drag");
            GUILayout.Label("<color=#FFA0FF>• Target Hand:</color> Space (Right) | Shift (Left) | R (Reset)");
            GUILayout.EndArea();
        }
#else
        private void Awake()
        {
            var lifecycle = GetComponent<UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation.SimulatedDeviceLifecycleManager>();
            if (lifecycle != null) lifecycle.removeOtherHMDDevices = false;
            gameObject.SetActive(false);
        }
#endif
    }
}
