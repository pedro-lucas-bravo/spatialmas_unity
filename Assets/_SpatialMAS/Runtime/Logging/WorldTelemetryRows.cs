using UnityEngine;

namespace SpatialMAS.Logging
{
    /// <summary>A synchronized participant sample, expressed only in Unity-world meters and xyzw quaternions.</summary>
    public struct EmbodimentTelemetryRow
    {
        public int Frame;
        public string ScenarioName;
        public string Layout;
        public string XRMode;
        public Vector3 ScenarioCenter;
        public int ActiveAgents;
        public float FrameTimeMs;
        public Vector3 HeadPos;
        public Quaternion HeadRot;
        public bool HeadTracked;
        public string HeadSource;
        public Vector3 LeftHandPos;
        public Quaternion LeftHandRot;
        public bool LeftHandTracked;
        public bool LeftPinch;
        public Vector3 RightHandPos;
        public Quaternion RightHandRot;
        public bool RightHandTracked;
        public bool RightPinch;
        public string CalibrationId;
        public int CalibrationRevision;
        public int TrackingRevision;
        public Vector3 TrackingOriginPos;
        public Quaternion TrackingOriginRot;
        public Vector3 TrackingOriginScale;
        public bool CoordinatesValid;
        public bool UserRepellerEnabled;
        public float UserRepellerRadiusMeters;
        public float UserRepellerStrength;
    }

    /// <summary>Actual object state at the same sample instant as an embodiment row; dimensions follow object-local axes.</summary>
    public struct ObjectTelemetryRow
    {
        public string Kind;
        public int Id;
        public string Shape;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Dimensions;
        public string MovementMode;
        public bool Grabbed;
        public bool Interactable;
    }
}
