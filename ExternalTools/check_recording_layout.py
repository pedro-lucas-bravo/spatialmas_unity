"""Check recorded geometry against docs/SCENARIOS_AND_AUDIO.md, using Python only."""

import argparse
import csv
import math
from pathlib import Path
import re
import sys


TABLE = Path(__file__).resolve().parents[1] / "docs" / "SCENARIOS_AND_AUDIO.md"
SCENARIOS = {1: "Crowd", 2: "Swarmalators", 3: "Attractors",
             4: "Musical Encounters", 5: "Self-Assembly"}
AXES = ("X", "Y", "Z")


def expected_dimensions(table=TABLE):
    """Read the documented mesh dimensions and rendered boundary diameters."""
    result = {}
    text = table.read_text(encoding="utf-8")
    for scenario, label in SCENARIOS.items():
        line = next(line for line in text.splitlines()
                    if line.startswith(f"| {label} |"))
        compact, room = line.split("|")[2:4]
        numbers = []
        for cell in (compact, room):
            numbers.append([float(value) for value in
                            re.findall(r"(?<![\w.])(?:\d+)?\.\d+|(?<![\w.])\d+(?=\s*m\b)", cell)])
        for layout, values in zip(("Compact", "RoomScale"), numbers):
            if scenario == 1:
                dimensions = {("Agent", "Pyramid"): values[0],
                              ("Boundary", "Sphere"): 2 * values[1]}
            elif scenario in (2, 3):
                dimensions = {("Agent", "Sphere"): values[0],
                              ("Agent", "Cube"): values[1]}
            elif scenario == 4:
                dimensions = {("Agent", "Sphere"): values[0],
                              ("Agent", "Cube"): values[1],
                              ("Boundary", "Sphere"): 2 * values[2]}
            else:
                dimensions = {("Agent", "Cube"): values[0]}
            result[scenario, layout] = dimensions
    return result


def find_csv(folder, stem):
    candidates = sorted(folder.glob(f"{stem}*.csv"))
    if len(candidates) != 1:
        raise ValueError(f"Expected one {stem}*.csv in {folder}, found {len(candidates)}.")
    return candidates[0]


def rows(path):
    with path.open(encoding="utf-8-sig", newline="") as stream:
        yield from csv.DictReader(stream)


def sample_key(row):
    return row["SessionId"], row["SampleId"]


def check(folder, tolerance):
    expectations = expected_dimensions()
    samples = {}
    tracking_failures = 0
    unknown_layouts = set()
    for row in rows(find_csv(folder, "xr_embodiment")):
        match = re.match(r"(\d+)", row["Scenario"])
        scenario = int(match.group(1)) if match else -1
        samples[sample_key(row)] = scenario, row["Layout"]
        if scenario in SCENARIOS:
            if (scenario, row["Layout"]) not in expectations:
                unknown_layouts.add(row["Layout"])
            scale = [float(row[f"TrackingOriginScale{axis}"]) for axis in AXES]
            if row["CoordinatesValid"] != "1" or any(
                    not math.isfinite(value) or abs(value - 1) > 0.00001
                    for value in scale):
                tracking_failures += 1

    groups = {}
    unmatched = skipped = 0
    for row in rows(find_csv(folder, "object_state")):
        context = samples.get(sample_key(row))
        if context is None:
            unmatched += 1
            continue
        expected = expectations.get(context, {}).get((row["Kind"], row["Shape"]))
        if expected is None:
            skipped += 1
            continue
        key = (*context, row["Kind"], row["Shape"])
        group = groups.setdefault(key, {"count": 0, "failed": 0,
                                        "min": math.inf, "max": -math.inf,
                                        "expected": expected, "example": None})
        values = [float(row[f"Dimensions{axis}_m"]) for axis in AXES]
        failed = any(not math.isfinite(value) or abs(value - expected) > tolerance
                     for value in values)
        group["count"] += 1
        group["failed"] += failed
        if failed and group["example"] is None:
            group["example"] = f"sample {row['SampleId']}, object {row['Id']}"
        for value in values:
            if math.isfinite(value):
                group["min"] = min(group["min"], value)
                group["max"] = max(group["max"], value)

    print(f"Recording: {folder}")
    print(f"Table: {TABLE}")
    print(f"Absolute tolerance: {tolerance:g} m")
    for key, group in sorted(groups.items()):
        scenario, layout, kind, shape = key
        status = "FAIL" if group["failed"] else "PASS"
        print(f"{status} {SCENARIOS[scenario]} / {layout} / {kind} {shape}: "
              f"expected {group['expected']:g} m on each axis; "
              f"observed {group['min']:.6g} to {group['max']:.6g} m "
              f"({group['count']} rows, {group['failed']} mismatches)")
        if group["example"]:
            print(f"  First mismatch: {group['example']}")
    print(f"Tracking scale/coordinate validity: {tracking_failures} invalid samples")
    print(f"Unmatched object rows: {unmatched}; rows outside table geometry: {skipped}")
    if unknown_layouts:
        print(f"INCOMPLETE: Unrecognized layouts: {sorted(unknown_layouts)}")
        return 2
    if not groups:
        print("INCOMPLETE: No comparable built-in geometry found.")
        return 2
    if unmatched:
        print("INCOMPLETE: Some object samples could not be joined to scenario/layout.")
        return 2
    if tracking_failures or any(group["failed"] for group in groups.values()):
        return 1
    print("PASS: Recorded geometry matches the layout table.")
    print("Check hand alignment and physical grab reach separately in the headset.")
    return 0


def check_path(path, tolerance):
    if not path.is_dir():
        raise ValueError(f"Recording path is not a folder: {path}")

    def has_recording_csv(folder):
        return any(folder.glob("object_state*.csv")) or any(folder.glob("xr_embodiment*.csv"))

    folders = [path] if has_recording_csv(path) else [
        child for child in sorted(path.iterdir())
        if child.is_dir() and has_recording_csv(child)
    ]
    if not folders:
        raise ValueError(f"No recording folders found in {path}.")
    results = []
    for folder in folders:
        print()
        try:
            result = check(folder, tolerance)
        except (OSError, ValueError, KeyError, StopIteration) as error:
            print(f"ERROR: {folder}: {error}", file=sys.stderr)
            result = 2
        results.append(result)
    print(f"\nSummary: {len(results)} recording(s); "
          f"{results.count(0)} passed, {results.count(1)} failed, "
          f"{results.count(2)} incomplete.")
    return max(results)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("recording", type=Path,
                        help="Recording folder or parent containing recording folders")
    parser.add_argument("--tolerance-mm", type=float, default=1,
                        help="Absolute dimension tolerance in millimeters (default 1)")
    args = parser.parse_args()
    if not math.isfinite(args.tolerance_mm) or args.tolerance_mm < 0:
        parser.error("--tolerance-mm must be finite and nonnegative")
    try:
        return check_path(args.recording, args.tolerance_mm / 1000)
    except (OSError, ValueError, KeyError, StopIteration) as error:
        print(f"ERROR: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
