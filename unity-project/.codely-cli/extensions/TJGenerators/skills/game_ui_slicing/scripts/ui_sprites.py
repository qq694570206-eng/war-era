#!/usr/bin/env python3
"""Offline, explicit-region UI slicing. No generation API and no semantic segmentation."""
import argparse
from collections import deque
import hashlib
import json
from pathlib import Path
import re
import shutil
import sys

from PIL import Image, ImageDraw, ImageFilter, PngImagePlugin

VERSION = "1.2.2"
NAME = re.compile(r"[a-z][a-z0-9_-]{0,63}\Z")
RESERVED = {"con", "prn", "aux", "nul", *[f"com{i}" for i in range(1, 10)],
            *[f"lpt{i}" for i in range(1, 10)], "contact-sheet", "coordinates"}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def digest(path):
    with Path(path).open("rb") as f:
        return hashlib.file_digest(f, "sha256").hexdigest() if hasattr(hashlib, "file_digest") else _digest(f)


def _digest(f):
    h = hashlib.sha256()
    for block in iter(lambda: f.read(1024 * 1024), b""):
        h.update(block)
    return h.hexdigest()


def read_json(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def write_json(path, value):
    Path(path).write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def fields(obj, allowed, label):
    require(isinstance(obj, dict), f"{label}: expected JSON object")
    require(not (obj.keys() - set(allowed)), f"{label}: unknown fields {sorted(obj.keys() - set(allowed))}")


def ints(values, length, label, minimum=0):
    require(isinstance(values, list) and len(values) == length and
            all(type(v) is int and v >= minimum for v in values),
            f"{label}: expected {length} integers >= {minimum}")
    return values


def safe_name(value):
    require(isinstance(value, str) and NAME.fullmatch(value) and value not in RESERVED,
            f"Unsafe name {value!r}: use lowercase ASCII letters, digits, '_' or '-', starting with a letter")
    return value


def rect(value, size, label):
    x, y, w, h = ints(value, 4, label)
    require(w > 0 and h > 0 and x + w <= size[0] and y + h <= size[1],
            f"{label}: rectangle outside image or empty (image={size})")
    return x, y, w, h


def fresh(path):
    path = Path(path)
    require(not path.exists(), f"Output already exists: {path}. Use a new version directory.")
    return path


def load_image(path):
    with Image.open(path) as im:
        require(getattr(im, "n_frames", 1) == 1, "Animated/multi-frame source unsupported; provide one still image")
        im.load()
        orientation = im.getexif().get(274, 1)
        require(orientation == 1, "EXIF rotation present: export an orientation-normalized copy before measuring")
        return im.convert("RGBA"), im.mode, dict(im.info)


def alpha_stats(im):
    alpha = im.getchannel("A")
    hist = alpha.histogram()
    box = alpha.getbbox()
    return {"size": list(im.size), "transparent_pixels": hist[0],
            "opaque_pixels": hist[255], "partial_alpha_pixels": sum(hist[1:255]),
            "alpha_min": next(i for i, n in enumerate(hist) if n),
            "alpha_max": next(i for i in range(255, -1, -1) if hist[i]),
            "alpha_bbox_xyxy": list(box) if box else None}


def background(size, color):
    if color != "checker":
        return Image.new("RGBA", size, color)
    im = Image.new("RGBA", size, "#D8D8D8")
    draw = ImageDraw.Draw(im)
    for y in range(0, size[1], 16):
        for x in range(0, size[0], 16):
            if (x // 16 + y // 16) % 2:
                draw.rectangle((x, y, x + 15, y + 15), fill="#A8A8A8")
    return im


def composite(im, color):
    return Image.alpha_composite(background(im.size, color), im).convert("RGB")


def inspect(source, out, grid=128):
    out = fresh(out)
    require(type(grid) is int and grid >= 16, "grid must be >= 16")
    im, mode, info = load_image(source)
    stats = alpha_stats(im)
    report = {"tool_version": VERSION, "source_file": Path(source).name, "source_sha256": digest(source),
              "original_mode": mode, **stats, "coordinates": "top-left, source pixels, rect=[x,y,w,h]",
              "has_icc_profile": bool(info.get("icc_profile")),
              "note": "Opaque pixels do not prove a solid background; visually inspect the source."}
    overlay = composite(im, "checker")
    draw = ImageDraw.Draw(overlay)
    for x in range(0, im.width, grid):
        draw.line((x, 0, x, im.height - 1), fill="#00BFFF")
        for y in range(0, im.height, grid):
            draw.text((x + 3, y + 3), f"{x},{y}", fill="white", stroke_width=1, stroke_fill="black")
    for y in range(0, im.height, grid):
        draw.line((0, y, im.width - 1, y), fill="#00BFFF")
    out.mkdir(parents=True)
    overlay.save(out / "coordinates.png")
    write_json(out / "report.json", report)
    write_json(out / "regions.template.json", {"schema_version": 1, "origin": "top-left",
               "source_size": list(im.size), "source_sha256": report["source_sha256"], "regions": []})
    return report


def stage_upload(source, out):
    source, out = Path(source).resolve(), Path(out).resolve()
    # Validate bytes before staging; no remote upload or credential handling here.
    load_image(source)
    sha = digest(source)
    suffix = source.suffix.lower()
    require(suffix in (".png", ".jpg", ".jpeg", ".webp"), "Upload staging expects a PNG/JPEG/WebP file")
    with Image.open(source) as probe:
        formats = {".png": "PNG", ".jpg": "JPEG", ".jpeg": "JPEG", ".webp": "WEBP"}
        require(probe.format == formats[suffix], "Actual image format does not match extension")
    out.mkdir(parents=True, exist_ok=True)
    target = out / ("ref-" + sha + suffix)
    if target.exists():
        require(digest(target) == sha, "Hash-named upload copy has been modified; choose a clean staging directory")
    else:
        # Exclusive creation prevents overwriting an existing file.
        with source.open("rb") as src, target.open("xb") as dst:
            shutil.copyfileobj(src, dst)
        require(digest(target) == sha, "Source changed while copying; inspect the input before retrying")
    return {"file_path": str(target), "sha256": sha, "next": "file_upload, PUT if needed, then use file_url"}


def remove_background(im, spec):
    fields(spec, ["mode", "key_hex", "tolerance", "softness", "scope", "edge_unmix"], "background")
    mode = spec.get("mode")
    require(mode in ("alpha", "opaque", "key"), "background.mode must be alpha, opaque or key")
    if mode != "key":
        require(set(spec) == {"mode"}, "Key parameters are only valid for mode=key")
        if mode == "opaque":
            require(im.getchannel("A").getextrema() == (255, 255),
                    "opaque mode requires an actually opaque crop; use alpha to preserve transparency")
        return im.copy()
    key_hex = spec.get("key_hex")
    require(isinstance(key_hex, str) and re.fullmatch(r"#[0-9a-fA-F]{6}", key_hex), "key_hex must be #RRGGBB")
    tolerance, softness = spec.get("tolerance"), spec.get("softness")
    require(all(type(v) is int and 0 <= v <= 255 for v in (tolerance, softness)) and
            tolerance + softness <= 255, "Key tolerance/softness must be integers, sum <= 255")
    scope = spec.get("scope")
    require(scope in ("edge", "all"), "Key scope must be edge or all")
    edge_unmix = spec.get("edge_unmix", 0)
    require(type(edge_unmix) is int and 0 <= edge_unmix <= 4, "edge_unmix must be 0..4 pixels")
    key = tuple(int(key_hex[i:i + 2], 16) for i in (1, 3, 5))
    if edge_unmix:
        require(max(key) - min(key) >= 64, "edge_unmix requires a chromatic key; gray/white/black matting needs a different method")
        midpoint = (max(key) + min(key)) / 2
        key_high = [c for c in range(3) if key[c] > midpoint]
        key_low = [c for c in range(3) if key[c] <= midpoint]
    pixels = list(im.getdata())
    distance = bytearray(max(abs(p[c] - key[c]) for c in range(3)) for p in pixels)
    seen = bytearray(len(pixels))
    limit = tolerance + softness
    if scope == "all":
        seen = bytearray(d <= limit for d in distance)
    else:
        queue = deque()

        def add(i):
            if not seen[i] and (pixels[i][3] == 0 or distance[i] <= limit):
                seen[i] = 1
                queue.append(i)

        w, h = im.size
        for x in range(w):
            add(x)
            add((h - 1) * w + x)
        for y in range(h):
            add(y * w)
            add(y * w + w - 1)
        while queue:
            i = queue.popleft()
            x, y = i % w, i // w
            if x:
                add(i - 1)
            if x + 1 < w:
                add(i + 1)
            if y:
                add(i - w)
            if y + 1 < h:
                add(i + w)
    cleaned = []
    for i, p in enumerate(pixels):
        alpha = p[3]
        if seen[i]:
            weight = (0 if distance[i] <= tolerance else
                      min(1.0, (distance[i] - tolerance) / softness) if softness else 1.0)
            alpha = round(alpha * weight)
        # RGB deliberately unchanged. Generic unmix/despill would damage same-color artwork.
        cleaned.append((*p[:3], alpha))
    result = Image.new("RGBA", im.size)
    result.putdata(cleaned)
    if edge_unmix:
        # Approximate hard-edge color-to-alpha, limited to a band beside removed background.
        # This is not a glass or soft-shadow matting algorithm.
        # Thin gaps can contain only partially keyed pixels (no fully transparent seed).
        # Include pixels actually affected by keying, but not unrelated native alpha.
        removed = Image.new("L", im.size)
        removed.putdata([255 if after[3] == 0 or after[3] < before[3] else 0
                         for before, after in zip(pixels, cleaned)])
        band = list(removed.filter(ImageFilter.MaxFilter(edge_unmix * 2 + 1)).getdata())
        def key_bias(p):
            return min(p[c] for c in key_high) - max(p[c] for c in key_low)
        references = bytearray(p[3] == 255 and key_bias(pixels[j]) <= 8 for j, p in enumerate(cleaned))
        offsets = sorted(((dx, dy) for dy in range(-8, 9) for dx in range(-8, 9) if dx or dy),
                         key=lambda xy: xy[0] * xy[0] + xy[1] * xy[1])
        for i, (original, current) in enumerate(zip(pixels, cleaned)):
            if not band[i] or current[3] == 0:
                continue
            # Only neutralize pixels biased toward the key hue. A gold/blue foreground
            # pixel beside transparency must not become translucent simply because a
            # mathematically possible color-to-alpha solution exists.
            if key_bias(original) <= 8:
                continue
            # Infer alpha from a nearby opaque foreground, not from the minimum
            # mathematically possible alpha (which can turn gray + magenta green).
            x, y = i % im.width, i // im.width
            foreground = None
            for dx, dy in offsets:
                xx, yy = x + dx, y + dy
                if 0 <= xx < im.width and 0 <= yy < im.height:
                    j = yy * im.width + xx
                    if references[j]:
                        foreground = pixels[j]
                        break
            if foreground is None:
                excess = key_bias(original)
                rgb = tuple(max(0, original[c] - excess) if c in key_high else original[c] for c in range(3))
                cleaned[i] = (*rgb, current[3])
                continue
            vector = [foreground[c] - key[c] for c in range(3)]
            denominator = sum(v * v for v in vector)
            matte = max(0, min(1, sum((original[c] - key[c]) * vector[c] for c in range(3)) / denominator)) if denominator else 1
            if matte <= 0:
                cleaned[i] = (*current[:3], 0)
                continue
            rgb = tuple(max(0, min(255, round((original[c] - (1 - matte) * key[c]) / matte))) for c in range(3))
            cleaned[i] = (*rgb, min(current[3], round(original[3] * matte)))
        result.putdata(cleaned)
    return result


def contact_sheet(items):
    width, row_h = 900, 212
    canvas = Image.new("RGB", (width, row_h * len(items)), "#34343B")
    draw = ImageDraw.Draw(canvas)
    for i, (im, record) in enumerate(items):
        y = i * row_h
        draw.text((8, y + 5), f"{record['name']}  {im.width}x{im.height}", fill="white")
        for col, color in enumerate(("#181820", "#F2F2F2", "checker")):
            tile = composite(im, color)
            tile.thumbnail((284, 180), Image.Resampling.LANCZOS)
            canvas.paste(tile, (col * 300 + 8, y + 27))
    return canvas


def slice_sheet(source, spec_path, out):
    out = fresh(out)
    plan = read_json(spec_path)
    fields(plan, ["schema_version", "origin", "source_size", "source_sha256", "regions"], "regions file")
    require(plan.get("schema_version") == 1 and plan.get("origin") == "top-left", "Use schema_version=1, origin=top-left")
    im, source_mode, info = load_image(source)
    require(plan.get("source_size") == list(im.size), "Source dimensions changed; inspect and measure again")
    source_hash = digest(source)
    require(plan.get("source_sha256") == source_hash, "Source SHA256 changed; inspect and measure again")
    regions = plan.get("regions")
    require(isinstance(regions, list) and 0 < len(regions) <= 128, "regions must contain 1..128 explicit rectangles")
    names, groups, items, warnings = set(), {}, [], []
    for region in regions:
        fields(region, ["name", "rect", "background", "trim", "padding", "allow_edge_contact", "state_group"], "region")
        name = safe_name(region.get("name"))
        require(name not in names, f"Duplicate name: {name}")
        names.add(name)
        x, y, w, h = rect(region.get("rect"), im.size, name)
        for flag in ("trim", "allow_edge_contact"):
            require(type(region.get(flag)) is bool, f"{name}: {flag} must be true or false")
        padding = region.get("padding")
        require(type(padding) is int and 0 <= padding <= 256, f"{name}: padding must be 0..256")
        group = region.get("state_group")
        require(group is None or (isinstance(group, str) and NAME.fullmatch(group)), f"{name}: invalid state_group")
        if group:
            require(not region["trim"], f"{name}: state_group requires trim=false to keep a shared canvas")
            shape = (w, h, padding)
            require(group not in groups or groups[group] == shape, f"{name}: state_group canvas/padding mismatch")
            groups[group] = shape
        cut = remove_background(im.crop((x, y, x + w, y + h)), region.get("background"))
        box = cut.getchannel("A").getbbox()
        require(box is not None, f"{name}: no visible foreground after background removal")
        touches = box[0] == 0 or box[1] == 0 or box[2] == w or box[3] == h
        require(not touches or region["allow_edge_contact"],
                f"{name}: foreground touches crop boundary; enlarge/correct rectangle. Do not hide clipping with padding.")
        if touches:
            warnings.append(f"{name}: edge contact explicitly allowed; check that it is an intentional complete rectangle")
        if region["background"]["mode"] == "key":
            warnings.append(f"{name}: approximate color key; inspect color spill, enclosed holes and lost details")
        trim_box = box if region["trim"] else (0, 0, w, h)
        cropped = cut.crop(trim_box)
        result = Image.new("RGBA", (cropped.width + 2 * padding, cropped.height + 2 * padding))
        result.paste(cropped, (padding, padding))  # No mask: preserve original straight alpha exactly.
        record = {"name": name, "file": name + ".png", "source_rect": [x, y, w, h],
                  "trim_offset_xy": list(trim_box[:2]), "trimmed": region["trim"], "padding": padding,
                  "background": region["background"], "state_group": group, "edge_contact": touches,
                  **alpha_stats(result)}
        items.append((result, record))
    # All validation and pixel processing precede output creation.
    out.mkdir(parents=True)
    metadata = PngImagePlugin.PngInfo()
    for k, v in info.items():
        if isinstance(v, str):
            metadata.add_text(k, v)
    metadata.add_text("UI-Slice-Source-SHA256", source_hash)
    for image, record in items:
        image.save(out / record["file"], pnginfo=metadata, icc_profile=info.get("icc_profile"))
        record["sha256"] = digest(out / record["file"])
    manifest = {"schema_version": 1, "tool_version": VERSION, "source_file": Path(source).name,
                "source_sha256": source_hash, "source_size": list(im.size), "source_mode": source_mode,
                "regions_spec_sha256": digest(spec_path), "coordinate_system": "top-left pixels",
                "sprites": [r for _, r in items], "warnings": warnings, "visual_review": "required"}
    write_json(out / "manifest.json", manifest)
    write_json(out / "layout.template.json", {"schema_version": 1,
               "sprites": [{"name": r["name"], "sha256": r["sha256"], "size": r["size"],
                            "border_ltrb": None, "text_expected": False, "content_rect": None,
                            "pivot_xy": [0.5, 0.5], "preview_scale": 1.0,
                            "preview_sizes": [r["size"]]} for _, r in items]})
    contact_sheet(items).save(out / "contact-sheet.png")
    return manifest


def display_borders(border, scale):
    require(type(scale) in (int, float) and 0 < scale <= 8,
            "preview_scale must be a number greater than 0 and at most 8")
    return [max(1, round(v * scale)) if v else 0 for v in border]


def map_coordinate(value, source_length, target_length, before, after, dest_before, dest_after):
    if value < before:
        return value * dest_before / before
    if value > source_length - after:
        return target_length - (source_length - value) * dest_after / after
    return dest_before + (value - before) * (target_length - dest_before - dest_after) / (source_length - before - after)


def display_content_rect(content, source_size, border, target_size, scale):
    if content is None:
        return None
    x, y, w, h = content
    l, t, r, b = border
    dl, dt, dr, db = display_borders(border, scale)
    x0 = map_coordinate(x, source_size[0], target_size[0], l, r, dl, dr)
    x1 = map_coordinate(x + w, source_size[0], target_size[0], l, r, dl, dr)
    y0 = map_coordinate(y, source_size[1], target_size[1], t, b, dt, db)
    y1 = map_coordinate(y + h, source_size[1], target_size[1], t, b, dt, db)
    return [round(x0, 3), round(y0, 3), round(x1 - x0, 3), round(y1 - y0, 3)]


def nine_slice(im, border, size, scale=1.0):
    l, t, r, b = border
    dl, dt, dr, db = display_borders(border, scale)
    width, height = size
    require(width > dl + dr and height > dt + db, "Preview target must exceed displayed fixed borders on both axes")
    sx, sy = [0, l, im.width - r, im.width], [0, t, im.height - b, im.height]
    dx, dy = [0, dl, width - dr, width], [0, dt, height - db, height]
    out = Image.new("RGBA", size)
    for yy in range(3):
        for xx in range(3):
            sw, sh = sx[xx + 1] - sx[xx], sy[yy + 1] - sy[yy]
            dw, dh = dx[xx + 1] - dx[xx], dy[yy + 1] - dy[yy]
            if min(sw, sh, dw, dh) > 0:
                patch = im.crop((sx[xx], sy[yy], sx[xx + 1], sy[yy + 1]))
                if patch.size != (dw, dh):
                    patch = patch.resize((dw, dh), Image.Resampling.LANCZOS)
                out.paste(patch, (dx[xx], dy[yy]))
    return out


def qa(slices, layout_path, out):
    out = fresh(out)
    slices = Path(slices)
    manifest = read_json(slices / "manifest.json")
    require(manifest.get("schema_version") == 1, "Unsupported manifest")
    originals = {r["name"]: r for r in manifest["sprites"]}
    layout = read_json(layout_path)
    fields(layout, ["schema_version", "sprites"], "layout")
    require(layout.get("schema_version") == 1 and isinstance(layout.get("sprites"), list), "Invalid layout schema")
    prepared, seen, groups = [], set(), {}
    for item in layout["sprites"]:
        fields(item, ["name", "sha256", "size", "border_ltrb", "text_expected", "content_rect", "pivot_xy", "preview_scale", "preview_scales", "preview_sizes"], "layout sprite")
        name = safe_name(item.get("name"))
        require(name not in seen and name in originals, f"{name}: duplicate or unknown layout name")
        seen.add(name)
        record = originals[name]
        source = slices / (name + ".png")
        require(digest(source) == item.get("sha256") == record["sha256"], f"{name}: sprite changed, remeasure layout")
        im, _, _ = load_image(source)
        require(list(im.size) == item.get("size") == record["size"], f"{name}: size mismatch")
        border = ints(item.get("border_ltrb"), 4, f"{name}.border_ltrb")
        require(border[0] + border[2] < im.width and border[1] + border[3] < im.height,
                f"{name}: borders consume the whole sprite")
        require(type(item.get("text_expected")) is bool, f"{name}: text_expected must be boolean")
        content = item.get("content_rect")
        require(content is not None or not item["text_expected"], f"{name}: text_expected requires a measured content_rect")
        if content is not None:
            rect(content, im.size, f"{name}.content_rect")
        pivot = item.get("pivot_xy")
        require(isinstance(pivot, list) and len(pivot) == 2 and
                all(type(v) in (int, float) and 0 <= v <= 1 for v in pivot), f"{name}: pivot_xy must contain two numbers in [0,1]")
        sizes = item.get("preview_sizes")
        scale = item.get("preview_scale", 1.0)
        display_borders(border, scale)
        require(isinstance(sizes, list) and 0 < len(sizes) <= 8, f"{name}: preview_sizes needs 1..8 sizes")
        scales = item.get("preview_scales", [scale] * len(sizes))
        require(isinstance(scales, list) and len(scales) == len(sizes), f"{name}: preview_scales must match preview_sizes length")
        for size, view_scale in zip(sizes, scales):
            dl, dt, dr, db = display_borders(border, view_scale)
            width, height = ints(size, 2, "preview_size", 1)
            require(max(width, height) <= 4096, "Preview dimensions must be <=4096 pixels")
            require(width > dl + dr and height > dt + db,
                    f"{name}: target too small for nine-slice borders")
        group = record.get("state_group")
        if group:
            geometry = (item["size"], border, content, pivot, scale)
            require(group not in groups or groups[group] == geometry, f"{name}: state_group layout mismatch")
            groups[group] = geometry
        prepared.append((im, item))
    require(seen == set(originals), "layout must contain every exported sprite exactly once")
    out.mkdir(parents=True)
    results = []
    for im, item in prepared:
        name, border = item["name"], item["border_ltrb"]
        for label, color in (("dark", "#181820"), ("light", "#F2F2F2"), ("checker", "checker")):
            composite(im, color).save(out / f"{name}-{label}.png")
        bounds = composite(im, "checker")
        draw = ImageDraw.Draw(bounds)
        l, t, r, b = border
        for x in (l, im.width - r - 1):
            draw.line((x, 0, x, im.height - 1), fill="#00BFFF", width=1)
        for y in (t, im.height - b - 1):
            draw.line((0, y, im.width - 1, y), fill="#00BFFF", width=1)
        if item["content_rect"] is not None:
            x, y, w, h = item["content_rect"]
            draw.rectangle((x, y, x + w - 1, y + h - 1), outline="#00FF70", width=2)
        bounds.save(out / f"{name}-bounds.png")
        previews = []
        mapped_content = []
        scale = item.get("preview_scale", 1.0)
        scales = item.get("preview_scales", [scale] * len(item["preview_sizes"]))
        for size, view_scale in zip(item["preview_sizes"], scales):
            rendered = composite(nine_slice(im, border, tuple(size), view_scale), "#34343B")
            safe = display_content_rect(item["content_rect"], im.size, border, size, view_scale)
            mapped_content.append({"size": size, "scale": view_scale, "content_rect": safe})
            if safe is not None:
                x, y, w, h = safe
                ImageDraw.Draw(rendered).rectangle((round(x), round(y), round(x + w - 1), round(y + h - 1)), outline="#00FF70")
            actual_size = rendered.size
            rendered.thumbnail((1600, 900), Image.Resampling.LANCZOS)
            previews.append((rendered, actual_size))
        board = Image.new("RGB", (max(p.width for p, _ in previews), sum(p.height + 26 for p, _ in previews)), "#34343B")
        draw = ImageDraw.Draw(board)
        yy = 0
        for preview, actual in previews:
            draw.text((2, yy + 4), f"{actual[0]}x{actual[1]} (shown {preview.width}x{preview.height})", fill="white")
            board.paste(preview, (0, yy + 24))
            yy += preview.height + 26
        board.save(out / f"{name}-sizes.png")
        results.append({**item, "preview_scale": scale, "displayed_content_rects": mapped_content, "alpha": alpha_stats(im)})
    report = {"schema_version": 1, "tool_version": VERSION, "geometry_checks": "passed",
              "visual_review": "required", "engine_review": "not_performed",
              "layout_sha256": digest(layout_path), "manifest_sha256": digest(slices / "manifest.json"),
              "warnings": manifest.get("warnings", []), "sprites": results}
    write_json(out / "report.json", report)
    return report


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    p = sub.add_parser("inspect", help="Read alpha/dimensions and create pixel-coordinate overlay")
    p.add_argument("source", type=Path)
    p.add_argument("--out", type=Path, required=True)
    p.add_argument("--grid", type=int, default=128)
    p = sub.add_parser("stage-upload", help="Create a content-hash-named copy before MCP file_upload")
    p.add_argument("source", type=Path)
    p.add_argument("--out", type=Path, required=True)
    p = sub.add_parser("slice", help="Crop explicit measured rectangles to independent RGBA PNGs")
    p.add_argument("source", type=Path)
    p.add_argument("regions", type=Path)
    p.add_argument("--out", type=Path, required=True)
    p = sub.add_parser("qa", help="Validate final geometry and create comparison previews")
    p.add_argument("slices", type=Path)
    p.add_argument("layout", type=Path)
    p.add_argument("--out", type=Path, required=True)
    args = parser.parse_args(argv)
    try:
        if args.command == "inspect":
            result = inspect(args.source, args.out, args.grid)
            summary = {"size": result["size"], "source_sha256": result["source_sha256"]}
        elif args.command == "stage-upload":
            summary = stage_upload(args.source, args.out)
        elif args.command == "slice":
            result = slice_sheet(args.source, args.regions, args.out)
            summary = {"sprites": len(result["sprites"]), "warnings": result["warnings"]}
        else:
            result = qa(args.slices, args.layout, args.out)
            summary = {"geometry_checks": result["geometry_checks"], "visual_review": "required", "engine_review": "not_performed"}
        print(json.dumps({"command": args.command, "output": str(args.out.resolve()), **summary}, ensure_ascii=False))
        return 0
    except (ValueError, OSError, KeyError, TypeError) as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
