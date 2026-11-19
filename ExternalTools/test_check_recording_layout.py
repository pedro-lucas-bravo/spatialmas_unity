import contextlib
import csv
import io
from pathlib import Path
import tempfile
import unittest

from check_recording_layout import check, check_path, expected_dimensions
from unittest.mock import patch


class RecordingLayoutTests(unittest.TestCase):
    def test_documented_boundary_radii_are_converted_to_diameters(self):
        expected = expected_dimensions()
        self.assertEqual(expected[4, "Compact"]["Boundary", "Sphere"], 2)
        self.assertEqual(expected[4, "RoomScale"]["Boundary", "Sphere"], 5)
        self.assertEqual(expected[3, "Compact"]["Agent", "Cube"], 0.0812)
        self.assertEqual(expected[5, "RoomScale"]["Agent", "Cube"], 0.15)

    def test_checks_layout_per_sample_and_reports_wrong_size(self):
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory)
            telemetry = [
                dict(SessionId="test", SampleId=str(i), Scenario="4. Musical Encounters",
                     Layout=layout, CoordinatesValid="1",
                     TrackingOriginScaleX="1", TrackingOriginScaleY="1",
                     TrackingOriginScaleZ="1")
                for i, layout in enumerate(("Compact", "RoomScale"))
            ]
            objects = [
                dict(SessionId="test", SampleId=str(i), Kind="Boundary", Shape="Sphere",
                     Id="1", DimensionsX_m=size, DimensionsY_m=size, DimensionsZ_m=size)
                for i, size in enumerate((2, 5))
            ]
            self.write(folder / "xr_embodiment_oldsession.csv", telemetry)
            self.write(folder / "object_state_oldsession.csv", objects)
            with contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(check(folder, 0.001), 0)
                objects[1]["DimensionsY_m"] = 2.5
                self.write(folder / "object_state_oldsession.csv", objects)
                self.assertEqual(check(folder, 0.001), 1)
                objects[1]["SampleId"] = "missing"
                self.write(folder / "object_state_oldsession.csv", objects)
                self.assertEqual(check(folder, 0.001), 2)

    def test_parent_continues_after_an_incomplete_recording(self):
        with tempfile.TemporaryDirectory() as directory:
            parent = Path(directory)
            folders = [parent / name for name in ("compact", "room", "broken")]
            for folder in folders:
                folder.mkdir()
                (folder / "object_state.csv").touch()
            (parent / "unrelated").mkdir()
            with patch("check_recording_layout.check", side_effect=[ValueError("missing CSV"), 0, 1]) as checker:
                with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
                    self.assertEqual(check_path(parent, 0.001), 2)
                self.assertEqual([call.args[0].name for call in checker.call_args_list],
                                 ["broken", "compact", "room"])
            with patch("check_recording_layout.check", return_value=0) as checker:
                with contextlib.redirect_stdout(io.StringIO()):
                    self.assertEqual(check_path(folders[0], 0.001), 0)
                checker.assert_called_once_with(folders[0], 0.001)

    @staticmethod
    def write(path, records):
        with path.open("w", newline="", encoding="utf-8") as stream:
            writer = csv.DictWriter(stream, fieldnames=list(records[0]))
            writer.writeheader()
            writer.writerows(records)


if __name__ == "__main__":
    unittest.main()
