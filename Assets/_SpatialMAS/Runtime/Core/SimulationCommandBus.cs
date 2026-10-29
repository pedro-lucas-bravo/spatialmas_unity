using System;
using UnityEngine;

namespace SpatialMAS.Core
{
    /// <summary>
    /// Central event and command bus that decouples External OSC controllers, Built-in Scenarios,
    /// XR Hand/Head interactions, Spatial Audio, and CSV Telemetry Logging.
    /// Both External OSC and Internal Scenarios drive the world through this unified contract.
    /// </summary>
    public class SimulationCommandBus
    {
        // --- Agent Lifecycle & State Commands ---
        public event Action<AgentShapeType, AgentMovementMode, int, float, string, int> OnInstantiateAgents;
        public event Action<AgentShapeType, AgentMovementMode, float, string, int, int[]> OnAddAgents;
        public event Action<int[]> OnRemoveAgents; // null or empty = remove all
        public event Action<AgentShapeType, AgentMovementMode, float, Vector3[]> OnAddAgentsAutomatically;
        public event Action<int, int, int> OnRemoveLastAgents; // count, shape filter, movement filter
        public event Action<int, Vector3> OnSetAgentPositionUnity;
        public event Action<int, Quaternion> OnSetAgentRotationUnity;
        public event Action<int, float> OnSetAgentSizeMeters;
        public event Action<Vector3[]> OnSetBatchAgentPositionsUnity;
        public event Action<int, float> OnSetAgentSpeedMetersPerSec;
        public event Action<int, Vector3> OnSetAgentDirectionUnity;
        public event Action<int, Vector3> OnSetAgentVelocityUnity;
        public event Action<int, Color> OnSetAgentColor;
        public event Action<Color[]> OnSetBatchAgentColors;
        public event Action<int, float, float, Color> OnSetAgentRadiusVisual; // id, radiusMeters, alpha, color
        public event Action<int, bool> OnSetAgentInteractable;
        public event Action<bool> OnSetShowAgentIds;

        // --- Local Communication Commands ---
        public event Action<float, float, bool> OnConfigureLocalComm; // maxRangeMeters, latencySec, showVisuals
        public event Action<string, int, float, float, object[]> OnTriggerLocalComm;

        // --- Audio & DSP Synth Commands ---
        public event Action<int, bool> OnSetAgentMute; // agentId = -1 means mute/unmute all
        public event Action<int, int, int, int> OnSetAgentSynthNote; // id, noteAction (1/0), midiNote, velocity
        public event Action<int, int, float> OnSetAgentSynthParam;   // id, ccId, value
        public event Action<int> OnFlushAgentSynth;                  // id = -1 means flush all
        public event Action<int, int, int, bool> OnSetAgentSynthEnabledFiltered;
        public event Action<int, int, int, float> OnSetAgentAudioBank; // id, clipIndex, playAction, gain

        // --- Boundary & Environment Commands ---
        public event Action<int, AgentShapeType, Vector3, float> OnAddBoundaryUnity;
        public event Action<int, Vector3, Vector3> OnAddBoxBoundaryUnity;
        public event Action<int, Vector3, float> OnUpdateBoundaryUnity;
        public event Action<int, Vector3> OnSetBoundaryDimensionsUnity; // cube full extents; sphere x is radius
        public event Action<int, Color, float> OnSetBoundaryVisuals; // id, color, alpha (-1 = keep existing)
        public event Action<int> OnRemoveBoundary; // id = -1 means remove all
        public event Action<int, Vector3, Vector3, float> OnSetArrowUnity;
        public event Action<int> OnRemoveArrow; // id = -1 means remove all

        // --- Audio Listener Commands ---
        public event Action<Vector3> OnSetListenerPositionUnity;
        public event Action<Quaternion> OnSetListenerRotationUnity;
        public event Action<bool> OnSetListenerLockedToHead;

        // --- SpatialMAS v2 XR & Scenario Commands ---
        public event Action<XRDisplayMode> OnSetXRDisplayMode;
        public event Action<Vector3> OnSetScenarioCenter;
        public event Action<ScenarioLayout> OnSetScenarioLayout;
        public event Action<bool, float, float> OnConfigureUserRepeller;
        public event Action<int> OnLoadScenario;
        public event Action<string, float> OnSetScenarioParam;
        public event Action<bool> OnSetRecordingState;
        public event Action<string, int, float> OnTestAliveReceived;

        // --- Outgoing World Feedback Events (World/XR -> OSC Client & Scenarios & Logger) ---
        public event Action<int, Vector3> OnAgentMovedByUserUnity;
        public event Action<int, int, int> OnAgentTouchEvent; // agentId, touchState (1/0), otherAgentId (-1 if user)
        public event Action<int, int, int> OnAgentControlClickEvent; // agentId, controlType (0=left,1=right), controlValue (1=press,0=release)
        public event Action<string, int, int, float, object[]> OnLocalCommDeliveredUnity;
        public event Action<int, int> OnBoundaryTouchedEvent; // agentId, boundaryId
        public event Action<int, int, int> OnBoundaryTransitionEvent; // agentId, boundaryId, transitionType (0=enter, 1=exit)
        public event Action<Vector3, Quaternion> OnUserHeadPoseSampledUnity;
        public event Action<int, Vector3, bool> OnUserHandPoseSampledUnity; // hand (0=L,1=R), posUnity, isPinching

        // --- Raw Traffic Logging Hook ---
        public event Action<string, string, string> OnOscTrafficLogged; // direction ("IN"/"OUT"), address, formattedArgs

        // Dispatch Helpers
        public void InstantiateAgents(AgentShapeType shape, AgentMovementMode movMode, int count, float scaleMeters, string remoteIp, int remotePort)
            => OnInstantiateAgents?.Invoke(shape, movMode, count, scaleMeters, remoteIp, remotePort);

        public void AddAgents(AgentShapeType shape, AgentMovementMode movMode, float scaleMeters, string remoteIp, int remotePort, int[] ids)
            => OnAddAgents?.Invoke(shape, movMode, scaleMeters, remoteIp, remotePort, ids);

        public void RemoveAgents(int[] ids = null)
            => OnRemoveAgents?.Invoke(ids);

        public void AddAgentsAutomatically(AgentShapeType shape, AgentMovementMode mode, float scaleMeters, Vector3[] initialPositions)
            => OnAddAgentsAutomatically?.Invoke(shape, mode, scaleMeters, initialPositions);

        public void RemoveLastAgents(int count, int shape = -1, int movementMode = -1)
            => OnRemoveLastAgents?.Invoke(count, shape, movementMode);

        public void SetAgentPositionUnity(int id, Vector3 posUnity)
            => OnSetAgentPositionUnity?.Invoke(id, posUnity);

        public void SetAgentRotationUnity(int id, Quaternion rotation) => OnSetAgentRotationUnity?.Invoke(id, rotation);
        public void SetAgentSizeMeters(int id, float fullDimensionMeters) => OnSetAgentSizeMeters?.Invoke(id, fullDimensionMeters);

        public void SetBatchAgentPositionsUnity(Vector3[] positionsUnity)
            => OnSetBatchAgentPositionsUnity?.Invoke(positionsUnity);

        public void SetAgentSpeedMetersPerSec(int id, float speedMetersPerSec)
            => OnSetAgentSpeedMetersPerSec?.Invoke(id, speedMetersPerSec);

        public void SetAgentDirectionUnity(int id, Vector3 dirUnity)
            => OnSetAgentDirectionUnity?.Invoke(id, dirUnity);

        public void SetAgentVelocityUnity(int id, Vector3 velUnity)
            => OnSetAgentVelocityUnity?.Invoke(id, velUnity);

        public void SetAgentColor(int id, Color color)
            => OnSetAgentColor?.Invoke(id, color);

        public void SetBatchAgentColors(Color[] colors)
            => OnSetBatchAgentColors?.Invoke(colors);

        public void SetAgentRadiusVisual(int id, float radiusMeters, float alpha, Color color)
            => OnSetAgentRadiusVisual?.Invoke(id, radiusMeters, alpha, color);

        public void SetAgentInteractable(int id, bool interactable)
            => OnSetAgentInteractable?.Invoke(id, interactable);

        public void SetShowAgentIds(bool show)
            => OnSetShowAgentIds?.Invoke(show);

        public void ConfigureLocalComm(float maxRangeMeters, float latencySec, bool showVisuals)
            => OnConfigureLocalComm?.Invoke(maxRangeMeters, latencySec, showVisuals);

        public void TriggerLocalComm(string msgType, int senderId, float rangeMeters, float latencySec, object[] extraArgs)
            => OnTriggerLocalComm?.Invoke(msgType, senderId, rangeMeters, latencySec, extraArgs);

        public void SetAgentMute(int id, bool mute)
            => OnSetAgentMute?.Invoke(id, mute);

        public void SetAgentSynthNote(int id, int noteAction, int midiNote, int velocity)
            => OnSetAgentSynthNote?.Invoke(id, noteAction, midiNote, velocity);

        public void SetAgentSynthParam(int id, int ccId, float value)
            => OnSetAgentSynthParam?.Invoke(id, ccId, value);

        public void FlushAgentSynth(int id = -1)
            => OnFlushAgentSynth?.Invoke(id);

        public void SetAgentSynthEnabledFiltered(int count, int shape, int movementMode, bool enabled)
            => OnSetAgentSynthEnabledFiltered?.Invoke(count, shape, movementMode, enabled);

        public void SetAgentAudioBank(int id, int clipIndex, int playAction, float gain)
            => OnSetAgentAudioBank?.Invoke(id, clipIndex, playAction, gain);

        public void AddBoundaryUnity(int id, AgentShapeType shape, Vector3 posUnity, float radiusOrHalfExtentMeters)
            => OnAddBoundaryUnity?.Invoke(id, shape, posUnity, radiusOrHalfExtentMeters);

        public void AddBoxBoundaryUnity(int id, Vector3 posUnity, Vector3 fullDimensionsMeters)
            => OnAddBoxBoundaryUnity?.Invoke(id, posUnity, fullDimensionsMeters);

        public void UpdateBoundaryUnity(int id, Vector3 posUnity, float radiusOrHalfExtentMeters)
            => OnUpdateBoundaryUnity?.Invoke(id, posUnity, radiusOrHalfExtentMeters);

        public void SetBoundaryDimensionsUnity(int id, Vector3 dimensionsMeters)
            => OnSetBoundaryDimensionsUnity?.Invoke(id, dimensionsMeters);

        public void SetBoundaryVisuals(int id, Color color, float alpha = -1f)
            => OnSetBoundaryVisuals?.Invoke(id, color, alpha);

        public void RemoveBoundary(int id = -1)
            => OnRemoveBoundary?.Invoke(id);

        public void SetArrowUnity(int id, Vector3 startUnity, Vector3 endUnity, float widthMeters)
            => OnSetArrowUnity?.Invoke(id, startUnity, endUnity, widthMeters);

        public void RemoveArrow(int id = -1)
            => OnRemoveArrow?.Invoke(id);

        public void SetListenerPositionUnity(Vector3 posUnity)
            => OnSetListenerPositionUnity?.Invoke(posUnity);

        public void SetListenerRotationUnity(Quaternion rotUnity)
            => OnSetListenerRotationUnity?.Invoke(rotUnity);

        public void SetListenerLockedToHead(bool locked)
            => OnSetListenerLockedToHead?.Invoke(locked);

        public void SetXRDisplayMode(XRDisplayMode mode)
            => OnSetXRDisplayMode?.Invoke(mode);

        /// <summary>Placement for subsequent built-in scenario spawns, in Unity world meters.</summary>
        public void SetScenarioCenter(Vector3 centerMeters) => OnSetScenarioCenter?.Invoke(centerMeters);
        public void SetScenarioLayout(ScenarioLayout layout) => OnSetScenarioLayout?.Invoke(layout);

        public event Action<Vector3, Quaternion> OnSetCalibrationPose;
        public event Action<bool> OnSetCalibrationVisible;
        public event Action<bool> OnSetCalibrationLocked;
        public event Action OnGetCalibration;
        public event Action<CalibrationSnapshot> OnCalibrationState;
        public void SetCalibrationPose(Vector3 position, Quaternion rotation) => OnSetCalibrationPose?.Invoke(position, rotation);
        public void SetCalibrationVisible(bool visible) => OnSetCalibrationVisible?.Invoke(visible);
        public void SetCalibrationLocked(bool locked) => OnSetCalibrationLocked?.Invoke(locked);
        public void GetCalibration() => OnGetCalibration?.Invoke();
        public void EmitCalibrationState(CalibrationSnapshot snapshot) => OnCalibrationState?.Invoke(snapshot);

        public void ConfigureUserRepeller(bool enabled, float radiusMeters, float force)
            => OnConfigureUserRepeller?.Invoke(enabled, radiusMeters, force);

        public void LoadScenario(int scenarioIndex)
            => OnLoadScenario?.Invoke(scenarioIndex);

        public void SetScenarioParam(string paramName, float value)
            => OnSetScenarioParam?.Invoke(paramName, value);

        public void SetRecordingState(bool recording)
            => OnSetRecordingState?.Invoke(recording);

        public void NotifyTestAlive(string ip, int port, float testNum)
            => OnTestAliveReceived?.Invoke(ip, port, testNum);

        // Feedback Emitters
        public void EmitAgentMovedByUserUnity(int id, Vector3 posUnity)
            => OnAgentMovedByUserUnity?.Invoke(id, posUnity);

        public void EmitAgentTouch(int id, int touchState, int otherAgentId = -1)
            => OnAgentTouchEvent?.Invoke(id, touchState, otherAgentId);

        public void EmitAgentControlClick(int id, int controlType, int controlValue)
            => OnAgentControlClickEvent?.Invoke(id, controlType, controlValue);

        public void EmitLocalCommDeliveredUnity(string msgType, int senderId, int receiverId, float distanceMeters, object[] extraArgs)
            => OnLocalCommDeliveredUnity?.Invoke(msgType, senderId, receiverId, distanceMeters, extraArgs);

        public void EmitBoundaryTouched(int agentId, int boundaryId)
            => OnBoundaryTouchedEvent?.Invoke(agentId, boundaryId);

        public void EmitBoundaryTransition(int agentId, int boundaryId, int transitionType)
            => OnBoundaryTransitionEvent?.Invoke(agentId, boundaryId, transitionType);

        public void EmitUserHeadPoseUnity(Vector3 posUnity, Quaternion rotUnity)
            => OnUserHeadPoseSampledUnity?.Invoke(posUnity, rotUnity);

        public void EmitUserHandPoseUnity(int handIndex, Vector3 posUnity, bool isPinching)
            => OnUserHandPoseSampledUnity?.Invoke(handIndex, posUnity, isPinching);

        public void LogOscTraffic(string direction, string address, string formattedArgs)
            => OnOscTrafficLogged?.Invoke(direction, address, formattedArgs);
    }
}
