"""Verify reference motion and world-frame stability in a paused scene recording."""

import argparse
import math
from pathlib import Path

from check_recording_layout import find_csv, rows, sample_key


def vector(row, prefix, axes, suffix=""):
    values = tuple(float(row[f"{prefix}{axis}{suffix}"]) for axis in axes)
    if not all(map(math.isfinite, values)):
        raise ValueError(f"Nonfinite {prefix} in recording")
    return values


def pose(row, prefix=""):
    return (vector(row, prefix + "Position", "XYZ", "_m"),
            vector(row, prefix + "Rotation", "XYZW"))


def angle(a, b):
    a = tuple(value / math.hypot(*a) for value in a)
    b = tuple(value / math.hypot(*b) for value in b)
    chord = min(math.dist(a, b), math.dist(a, tuple(-value for value in b)))
    return math.degrees(4 * math.asin(min(1, chord / 2)))


def maximum_drift(poses):
    position, rotation = poses[0]
    return (max(math.dist(position, p) for p, _ in poses),
            max(angle(rotation, q) for _, q in poses))


def check(folder):
    calibration = list(rows(find_csv(folder, "calibration")))
    if not calibration:
        raise ValueError("Calibration recording is empty")
    reference = {(row["SessionId"], row["CalibrationId"], row["Revision"]): pose(row)
                 for row in calibration}
    samples = list(rows(find_csv(folder, "xr_embodiment")))
    if not samples or len({row["SessionId"] for row in samples}) != 1:
        raise ValueError("Expected telemetry for one session")
    sample_refs = {sample_key(row): (row["SessionId"], row["CalibrationId"], row["CalibrationRevision"])
                   for row in samples}
    used_refs = list(dict.fromkeys(sample_refs.values()))
    if any(key not in reference for key in used_refs):
        raise ValueError("Some sampled calibration revisions have no matching reference pose")
    if len({key[1] for key in used_refs}) != 1:
        raise ValueError("Expected one calibration reference throughout the recording")
    marker_m, marker_deg = maximum_drift([reference[key] for key in used_refs])
    tracking = [(vector(row, "TrackingOriginPos", "XYZ", "_m"),
                 vector(row, "TrackingOriginRot", "XYZW")) for row in samples]
    tracking_m, tracking_deg = maximum_drift(tracking)
    valid_frame = all(row["CoordinatesValid"] == "1"
                      and row["TrackingRevision"] == samples[0]["TrackingRevision"]
                      and math.dist(vector(row, "TrackingOriginScale", "XYZ"), (1, 1, 1)) <= 1e-5
                      for row in samples)

    objects = {}
    for row in rows(find_csv(folder, "object_state")):
        if row["Kind"] != "Agent":
            continue
        key = sample_key(row)
        if key not in sample_refs:
            raise ValueError("Agent sample has no matching XR telemetry")
        objects.setdefault(row["Id"], []).append((sample_refs[key], pose(row)))
    if not objects or any(
            not {used_refs[0], used_refs[-1]}.issubset({key for key, _ in observations})
            for observations in objects.values()):
        raise ValueError("Expected the same agents before and after moving the reference")
    drifts = [maximum_drift([value for _, value in observations])
              for observations in objects.values()]
    object_m = max(value[0] for value in drifts)
    object_deg = max(value[1] for value in drifts)

    print(f"{folder.name}: {len(samples)} samples, {len(objects)} agents, {len(used_refs)} sampled calibration revisions")
    print(f"Reference change: {marker_m * 1000:.3f} mm, {marker_deg:.4f} deg")
    print(f"Maximum agent drift: {object_m * 1000:.3f} mm, {object_deg:.4f} deg")
    print(f"Tracking-origin drift: {tracking_m * 1000:.3f} mm, {tracking_deg:.4f} deg")
    passed = (len(used_refs) > 1 and (marker_m > 0.001 or marker_deg > 0.1)
              and valid_frame and max(object_m, tracking_m) <= 0.001
              and max(object_deg, tracking_deg) <= 0.1)
    print("PASS" if passed else "FAIL")
    return passed


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("folder", type=Path, help="Recording folder or parent of recording folders")
    args = parser.parse_args()
    try:
        folders = [args.folder] if any(args.folder.glob("calibration*.csv")) else [
            child for child in sorted(args.folder.iterdir()) if child.is_dir()
        ]
        if not folders:
            raise ValueError("No recording folders found")
        return 0 if all([check(folder) for folder in folders]) else 1
    except (OSError, ValueError, KeyError, ZeroDivisionError) as error:
        print(f"INCOMPLETE: {error}")
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
