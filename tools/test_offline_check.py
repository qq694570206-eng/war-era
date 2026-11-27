"""Regression tests with deliberately malformed and disconnected map fixtures."""
import copy
import json
import unittest
from offline_check import ROOT, validate_map


class MapValidationTests(unittest.TestCase):
    def setUp(self):
        self.data = {
            "mapId": "test", "displayName": "Test", "themeId": "ww2",
            "worldSize": [100, 100], "playerCount": 2, "mapType": "1v1",
            "playerStartPositions": [[10, 10, 0], [90, 90, 0]],
            "oreNodes": [{"position": [20, 20, 0], "radius": 5, "isInfinite": False, "maxAmount": 100}],
            "obstacles": [], "controlPoints": [], "decorations": []}

    def errors(self, data=None):
        return validate_map(self.data if data is None else data)["errors"]

    def test_open_map(self):
        self.assertEqual(self.errors(), [])

    def test_disconnected_map_and_bridge(self):
        self.data["obstacles"] = [{"position": [50, 50, 0], "size": [100, 20], "type": "Rock"}]
        self.assertIn("player starts are disconnected", self.errors())
        self.data["obstacles"] = [
            {"position": [20, 50, 0], "size": [40, 20], "type": "Rock"},
            {"position": [80, 50, 0], "size": [40, 20], "type": "Rock"}]
        self.assertEqual(self.errors(), [])

    def test_resource_inside_obstacle(self):
        self.data["obstacles"] = [{"position": [20, 20, 0], "size": [10, 10], "type": "Rock"}]
        self.assertTrue(any("oreNodes[0]: center lies inside" in error for error in self.errors()))

    def test_invalid_shapes_do_not_crash(self):
        for field, value in (("worldSize", [0, 100]), ("worldSize", [float("nan"), 100]),
                             ("oreNodes", [None]), ("playerStartPositions", [[1, 2]]),
                             ("themeId", []), ("mapType", []),
                             ("obstacles", [{"position": None, "size": "bad"}])):
            with self.subTest(field=field, value=value):
                data = copy.deepcopy(self.data)
                data[field] = value
                self.assertTrue(self.errors(data))

    def test_out_of_bounds_is_not_clamped(self):
        self.data["playerStartPositions"][0] = [-1, 10, 0]
        self.assertTrue(any("outside XY" in error for error in self.errors()))

    def test_duplicate_spawns(self):
        self.data["playerStartPositions"][1] = [10, 10, 0]
        self.assertTrue(any("duplicate spawn" in error for error in self.errors()))

    def test_invalid_enum(self):
        self.data["controlPoints"] = [{"position": [50, 50, 0], "radius": 5, "captureTime": 1, "type": "Typo"}]
        self.assertTrue(any("unknown control point type" in error for error in self.errors()))

    def test_blocked_spawn(self):
        self.data["obstacles"] = [{"position": [10, 10, 0], "size": [10, 10], "type": "Rock"}]
        self.assertTrue(any("playerStartPositions[0]: center lies inside" in error for error in self.errors()))

    def test_river_original_wall_is_rejected(self):
        data = json.loads((ROOT / "unity-project/Assets/_Project/Data/Maps/river.json").read_text())
        self.assertEqual(self.errors(data), [])
        data["obstacles"] = [{"position": [1200, 800, 0], "size": [2400, 100], "type": "Rock"}]
        self.assertIn("player starts are disconnected", self.errors(data))

    def test_crossroad_original_blocked_center_is_rejected(self):
        data = json.loads((ROOT / "unity-project/Assets/_Project/Data/Maps/crossroad.json").read_text())
        self.assertEqual(self.errors(data), [])
        data["obstacles"] = [{"position": [1200, 800, 0], "size": [50, 250], "type": "BuildingRuin"}]
        self.assertTrue(any("oreNodes[4]: center lies inside" in error for error in self.errors(data)))


if __name__ == "__main__":
    unittest.main()
