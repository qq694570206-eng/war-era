#!/usr/bin/env python3
"""Offline map/assembly checks; no Unity, network or third-party modules required.

Navigation checks approximate a point agent on a 10-unit raster. They do not
prove clearance for vehicle footprints or correctness of runtime pathfinding.
"""
import argparse
from collections import deque
import json
import math
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OBSTACLE_TYPES = {"BuildingRuin", "Rock", "Forest", "BarbedWire", "Sandbag", "Wreckage", "Roadblock"}
CONTROL_TYPES = {"Economy", "Tech", "Vision", "Highland"}
THEMES = {"ancient", "ww1", "ww2", "postwar", "modern"}


def number(value):
    return type(value) in (int, float) and math.isfinite(value)


def vector(value, length):
    return isinstance(value, list) and len(value) == length and all(number(x) for x in value)


def validate_map(data, cell_size=10):
    errors, warnings = [], []
    if not isinstance(data, dict):
        return {"errors": ["map must be an object"], "warnings": []}
    for field in ("mapId", "displayName"):
        if not isinstance(data.get(field), str) or not data[field].strip():
            errors.append(f"{field}: required nonempty string")
    if not isinstance(data.get("themeId"), str) or data["themeId"] not in THEMES:
        errors.append("themeId: unknown theme")
    world = data.get("worldSize")
    if not vector(world, 2) or any(x <= 0 for x in world):
        errors.append("worldSize: expected two positive finite numbers")
    count = data.get("playerCount")
    if type(count) is not int or count not in (2, 4):
        errors.append("playerCount: expected 2 or 4")
    starts = data.get("playerStartPositions")
    if not isinstance(starts, list) or len(starts) != count:
        errors.append("playerStartPositions: count must equal playerCount")
    map_type = data.get("mapType")
    expected_count = {"1v1": 2, "2v2": 4, "4人": 4}.get(map_type) if isinstance(map_type, str) else None
    if expected_count is None or expected_count != count:
        errors.append("mapType: unsupported or inconsistent with playerCount")
    for field in ("oreNodes", "obstacles", "controlPoints", "decorations"):
        if not isinstance(data.get(field), list):
            errors.append(f"{field}: expected array")
    if errors:
        return {"errors": errors, "warnings": warnings}

    def position(pos, label):
        if not vector(pos, 3):
            errors.append(f"{label}: expected three finite numbers")
        elif not (0 <= pos[0] < world[0] and 0 <= pos[1] < world[1]) or pos[2] != 0:
            errors.append(f"{label}: outside XY map bounds or nonzero Z")

    for i, pos in enumerate(starts):
        position(pos, f"playerStartPositions[{i}]")
    if all(vector(p, 3) for p in starts) and len({tuple(p) for p in starts}) != len(starts):
        errors.append("playerStartPositions: duplicate spawn")
    for field in ("oreNodes", "obstacles", "controlPoints", "decorations"):
        for i, item in enumerate(data[field]):
            label = f"{field}[{i}]"
            if not isinstance(item, dict):
                errors.append(f"{label}: expected object")
                continue
            position(item.get("position"), label + ".position")
            if field in ("oreNodes", "controlPoints"):
                if not number(item.get("radius")) or item["radius"] <= 0:
                    errors.append(f"{label}.radius: expected positive number")
            if field == "oreNodes":
                if type(item.get("maxAmount")) is not int or item["maxAmount"] <= 0:
                    errors.append(f"{label}.maxAmount: expected positive integer")
                if type(item.get("isInfinite")) is not bool:
                    errors.append(f"{label}.isInfinite: expected boolean")
            if field == "controlPoints":
                if not isinstance(item.get("type"), str) or item["type"] not in CONTROL_TYPES:
                    errors.append(f"{label}.type: unknown control point type")
                if not number(item.get("captureTime")) or item["captureTime"] <= 0:
                    errors.append(f"{label}.captureTime: expected positive number")
            if field == "obstacles":
                size = item.get("size")
                if not vector(size, 2) or any(x <= 0 for x in size):
                    errors.append(f"{label}.size: expected two positive numbers")
                elif vector(item.get("position"), 3):
                    x, y, _ = item["position"]
                    if x-size[0]/2 < 0 or y-size[1]/2 < 0 or x+size[0]/2 > world[0] or y+size[1]/2 > world[1]:
                        errors.append(f"{label}: obstacle footprint exceeds map bounds")
                if not isinstance(item.get("type"), str) or item["type"] not in OBSTACLE_TYPES:
                    errors.append(f"{label}.type: unknown obstacle type")
    if errors:
        return {"errors": errors, "warnings": warnings}
    if not data["oreNodes"]:
        errors.append("oreNodes: at least one resource node required")
    if any(node["isInfinite"] for node in data["oreNodes"]):
        warnings.append("Infinite ore remains from prototype; finite-economy design is not applied.")
    if count > 2:
        warnings.append("Map data supports 4 starts; current GameManager only initializes two sides.")
    if not number(cell_size) or cell_size <= 0:
        return {"errors": errors + ["cell size must be positive and finite"], "warnings": warnings}
    width, height = (math.ceil(x/cell_size) for x in world)
    if width * height > 1_000_000:
        return {"errors": errors + ["navigation raster exceeds 1,000,000 cells"], "warnings": warnings}
    rectangles = []
    for obstacle in data["obstacles"]:
        x, y, _ = obstacle["position"]
        w, h = obstacle["size"]
        rectangles.append((x-w/2, y-h/2, x+w/2, y+h/2))

    def inside(pos):
        return any(a <= pos[0] <= c and b <= pos[1] <= d for a, b, c, d in rectangles)

    for field, items in (("playerStartPositions", starts), ("oreNodes", data["oreNodes"]), ("controlPoints", data["controlPoints"])):
        for i, item in enumerate(items):
            pos = item if field == "playerStartPositions" else item["position"]
            if inside(pos):
                errors.append(f"{field}[{i}]: center lies inside obstacle")

    # Conservative raster: any positive-area obstacle overlap blocks a cell.
    labels = [-1] * (width * height)
    for y in range(height):
        for x in range(width):
            left, bottom = x*cell_size, y*cell_size
            if any(left < c and left+cell_size > a and bottom < d and bottom+cell_size > b for a, b, c, d in rectangles):
                labels[y*width+x] = -2
    components = 0
    for initial in range(len(labels)):
        if labels[initial] != -1:
            continue
        labels[initial] = components
        queue = deque([initial])
        while queue:
            index = queue.popleft()
            x, y = index % width, index // width
            for nx, ny in ((x-1,y), (x+1,y), (x,y-1), (x,y+1)):
                if 0 <= nx < width and 0 <= ny < height:
                    other = ny*width+nx
                    if labels[other] == -1:
                        labels[other] = components
                        queue.append(other)
        components += 1

    def component(pos):
        return labels[int(pos[1]/cell_size)*width+int(pos[0]/cell_size)]

    spawn_components = [component(pos) for pos in starts]
    if any(c < 0 for c in spawn_components):
        errors.append("spawn blocked at navigation raster resolution")
    if len(set(spawn_components)) != 1:
        errors.append("player starts are disconnected")
    for field in ("oreNodes", "controlPoints"):
        for i, item in enumerate(data[field]):
            c = component(item["position"])
            if c < 0 or any(c != start for start in spawn_components):
                errors.append(f"{field}[{i}]: center unreachable from one or more starts")
    return {"errors": errors, "warnings": warnings, "navigationComponents": components}


def check_project(root):
    report = {"maps": {}, "errors": [], "warnings": []}
    paths = sorted((root / "unity-project/Assets/_Project/Data/Maps").glob("*.json"))
    if not paths:
        report["errors"].append("No map JSON files found")
    ids = set()
    for path in paths:
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
            result = validate_map(data)
            if isinstance(data, dict) and isinstance(data.get("mapId"), str):
                if data["mapId"] in ids:
                    result["errors"].append("duplicate mapId")
                ids.add(data["mapId"])
                if data["mapId"] != path.stem:
                    result["errors"].append("mapId does not match filename")
        except (OSError, ValueError) as error:
            result = {"errors": [str(error)], "warnings": []}
        report["maps"][path.name] = result
    scripts = root / "unity-project/Assets/_Project/Scripts"
    definitions = list(scripts.rglob("*.asmdef"))
    # Temporary single-runtime boundary: prevents reintroducing the original split.
    expected = scripts / "WarEra.Runtime.asmdef"
    if definitions != [expected]:
        report["errors"].append("Expected a single Scripts/WarEra.Runtime.asmdef boundary")
    else:
        try:
            definition = json.loads(expected.read_text())
            if definition.get("name") != "WarEra.Runtime" or not {"UnityEngine.UI", "Unity.TextMeshPro"} <= set(definition.get("references", [])):
                report["errors"].append("Runtime assembly name or UI/TMP references invalid")
        except (OSError, ValueError, TypeError) as error:
            report["errors"].append(f"Invalid runtime assembly: {error}")
    for relative in ("Packages/manifest.json", "ProjectSettings/ProjectVersion.txt"):
        if not (root / "unity-project" / relative).exists():
            report["warnings"].append(f"Missing {relative}; Unity build baseline remains incomplete")
    report["warnings"].append("No serialized UnitDefinition assets: unit IDs, prerequisites and costs cannot yet be content-validated.")
    report["errorCount"] = len(report["errors"]) + sum(len(m["errors"]) for m in report["maps"].values())
    report["warningCount"] = len(report["warnings"]) + sum(len(m["warnings"]) for m in report["maps"].values())
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=ROOT)
    parser.add_argument("--json", type=Path, help="Optional output report")
    args = parser.parse_args()
    report = check_project(args.root)
    if args.json:
        args.json.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    for name, result in report["maps"].items():
        print(f"{name}: {len(result['errors'])} errors, {len(result['warnings'])} warnings")
        for issue in result["errors"]:
            print(f"  ERROR: {issue}")
    for issue in report["errors"]:
        print(f"ERROR: {issue}")
    for issue in report["warnings"]:
        print(f"WARNING: {issue}")
    print(f"Total: {report['errorCount']} errors, {report['warningCount']} warnings (not Unity compilation)")
    return 1 if report["errorCount"] else 0


if __name__ == "__main__":
    raise SystemExit(main())
