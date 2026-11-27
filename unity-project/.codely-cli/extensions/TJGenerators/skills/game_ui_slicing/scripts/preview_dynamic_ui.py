#!/usr/bin/env python3
"""Offline HUD QA: independent frame/track/fill, clipped values and live text samples."""
import argparse
from decimal import Decimal
import hashlib
import math
from pathlib import Path
import sys

from PIL import Image, ImageChops, ImageDraw, ImageFont
import ui_sprites as ui


def color(value, label):
    ui.require(isinstance(value, list) and len(value) == 3 and
               all(type(v) is int and 0 <= v <= 255 for v in value), f"{label}: expected three RGB integers 0..255")
    return tuple(value)


def ratio(current, maximum):
    ui.require(all(type(v) in (int, float) and math.isfinite(v) for v in (current, maximum)), "Gauge values must be finite numbers")
    maximum = max(0, maximum)
    current = min(maximum, max(0, current))
    return current, maximum, current / maximum if maximum else 0.0


def number_text(value):
    return str(int(value)) if value == int(value) else format(Decimal(str(value)), 'f')


def font_at(path, size):
    ui.require(type(size) is int and 1 <= size <= 256, "font_size must be an integer 1..256")
    return ImageFont.truetype(str(path), size)


def wrapped_text(text, font, width, wrap):
    ui.require(isinstance(text, str) and len(text) <= 300, "Text sample must have at most 300 characters")
    if not wrap:
        return text
    lines = []
    measure = ImageDraw.Draw(Image.new("RGB", (1, 1)))
    for paragraph in text.split("\n"):
        line = ""
        for ch in paragraph:
            if line and measure.textlength(line + ch, font=font) > width:
                lines.append(line)
                line = ch
            else:
                line += ch
        lines.append(line)
    return "\n".join(lines)


def draw_text(im, text, font, box, fill, wrap=False, stroke_width=0, stroke_fill=None):
    x, y, width, height = box
    text = wrapped_text(text, font, width, wrap)
    draw = ImageDraw.Draw(im)
    bounds = draw.multiline_textbbox((0, 0), text, font=font, spacing=4, align="center", stroke_width=stroke_width)
    actual = [bounds[2] - bounds[0], bounds[3] - bounds[1]]
    fits = actual[0] <= width and actual[1] <= height
    # Do not silently shrink, truncate or clip overflowing text to make QA pass.
    draw.multiline_text((x + (width - actual[0]) / 2 - bounds[0],
                         y + (height - actual[1]) / 2 - bounds[1]), text,
                        font=font, fill=fill, spacing=4, align="center", stroke_width=stroke_width, stroke_fill=stroke_fill)
    return {"rendered_text": text, "bounds_size": actual, "container": list(box), "fits": fits}


def render_bar(frame, track, fill, fill_box, radius, current, maximum, tint, font, text_box, label_mode, text_color,
               text_stroke_width=1, text_outline=(0, 0, 0)):
    current, maximum, fraction = ratio(current, maximum)
    x, y, width, height = fill_box
    mask = Image.new("L", (width, height), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, width - 1, height - 1), radius=radius, fill=255)
    result = Image.new("RGBA", frame.size)
    # Stretch the fill to its FULL range once; crop that fixed appearance for every value.
    track = track.resize((width, height), Image.Resampling.LANCZOS)
    track.putalpha(ImageChops.multiply(track.getchannel("A"), mask))
    result.alpha_composite(track, (x, y))
    fill = fill.resize((width, height), Image.Resampling.LANCZOS)
    alpha = ImageChops.multiply(fill.getchannel("A"), mask)
    rgb = ImageChops.multiply(fill.convert("RGB"), Image.new("RGB", fill.size, tint)).convert("RGBA")
    rgb.putalpha(alpha)
    # A positive value gets at least one raster pixel; zero is always completely empty.
    filled_width = min(width, max(1, round(width * fraction))) if fraction > 0 else 0
    if filled_width:
        result.alpha_composite(rgb.crop((0, 0, filled_width, height)), (x, y))
    # Frame is a separate static foreground layer and never changes with the value.
    result.alpha_composite(frame)
    text = f"{number_text(current)}/{number_text(maximum)}" if label_mode == "current_max" else f"{fraction:.0%}"
    text_report = draw_text(result, text, font, text_box, (*text_color, 255),
                            stroke_width=text_stroke_width, stroke_fill=(*text_outline, 255))
    text_report.update(stroke_width=text_stroke_width, outline=list(text_outline))
    return result, {"current": current, "maximum": maximum, "fraction": fraction,
                    "fill_width": filled_width, "fill_box": list(fill_box), "tint": list(tint),
                    "text": text_report, "invalid_maximum_shown_as_zero": maximum == 0}


def asset(base, path):
    ui.require(isinstance(path, str) and bool(path), "Asset paths must be nonempty strings")
    path = (base / path).resolve()
    image, _, _ = ui.load_image(path)
    ui.require(image.getchannel("A").getbbox() is not None, "Asset is fully transparent")
    return image, {"file": path.name, "sha256": ui.digest(path), "size": list(image.size)}


def run(config_path, out):
    out = ui.fresh(out)
    config_path = Path(config_path)
    base = config_path.resolve().parent
    spec = ui.read_json(config_path)
    ui.fields(spec, ["schema_version", "font_path", "canvas_scales", "bar", "text_samples"], "dynamic spec")
    ui.require(spec.get("schema_version") == 1, "Use schema_version=1")
    font_path = (base / spec["font_path"]).resolve()
    ui.require(font_path.is_file(), "A real local font file is required; choose a font covering your language")
    scales = spec.get("canvas_scales", [1.0, 0.5])
    ui.require(isinstance(scales, list) and 1 <= len(scales) <= 4 and
               all(type(s) in (int, float) and 0 < s <= 2 for s in scales), "canvas_scales needs 1..4 numbers in (0,2]")
    outputs, records, text_records = [], [], []
    sources = {}
    frame_hash = None
    if spec.get("bar") is not None:
        bar = spec["bar"]
        ui.fields(bar, ["frame", "track", "fill", "size", "frame_border_ltrb", "frame_scale", "fill_rect",
                   "fill_radius", "text_rect", "font_size", "text_color", "text_stroke_width", "text_outline", "label_mode", "colors", "states"], "bar")
        size = ui.ints(bar["size"], 2, "bar.size", 1)
        ui.require(max(size) <= 2048, "bar.size maximum edge is 2048")
        frame, frame_info = asset(base, bar["frame"])
        track, track_info = asset(base, bar["track"])
        fill, fill_info = asset(base, bar["fill"])
        borders = ui.ints(bar["frame_border_ltrb"], 4, "frame_border_ltrb")
        ui.require(borders[0] + borders[2] < frame.width and borders[1] + borders[3] < frame.height,
                   "Frame borders consume source image")
        frame = ui.nine_slice(frame, borders, tuple(size), bar.get("frame_scale", 1.0))
        fill_box = ui.rect(bar["fill_rect"], size, "fill_rect (display pixels)")
        fx, fy, fw, fh = fill_box
        opening = frame.getchannel("A").crop((fx, fy, fx + fw, fy + fh))
        ui.require(opening.getextrema()[0] < 255, "The foreground frame completely covers fill_rect; provide a transparent frame opening")
        text_box = ui.rect(bar["text_rect"], size, "text_rect (display pixels)")
        radius = bar.get("fill_radius", 0)
        ui.require(type(radius) is int and 0 <= radius <= min(fill_box[2:]) / 2, "fill_radius outside allowed range")
        font = font_at(font_path, bar["font_size"])
        text_color = color(bar["text_color"], "text_color")
        stroke_width = bar.get("text_stroke_width", 1)
        ui.require(type(stroke_width) is int and 0 <= stroke_width <= 4, "text_stroke_width must be 0..4 display pixels")
        outline = color(bar.get("text_outline", [0, 0, 0]), "text_outline")
        mode = bar.get("label_mode", "current_max")
        ui.require(mode in ("current_max", "percentage"), "label_mode must be current_max or percentage")
        colors = bar.get("colors", [{"up_to": 1, "rgb": [255, 255, 255]}])
        ui.require(isinstance(colors, list) and 1 <= len(colors) <= 8, "colors needs 1..8 threshold entries")
        last = -1
        for entry in colors:
            ui.fields(entry, ["up_to", "rgb"], "colors entry")
            threshold = entry.get("up_to")
            ui.require(type(threshold) in (int, float) and last < threshold <= 1 and threshold >= 0,
                       "colors.up_to must increase from 0 to 1")
            color(entry["rgb"], "colors.rgb")
            last = threshold
        ui.require(last == 1, "Last colors.up_to must be 1")
        states = bar["states"]
        ui.require(isinstance(states, list) and 1 <= len(states) <= 30, "states needs 1..30 [current,maximum] pairs")
        outputs, records, text_records = [], [], []
        for index, pair in enumerate(states):
            ui.require(isinstance(pair, list) and len(pair) == 2, "Each state must be [current, maximum]")
            _, _, fraction = ratio(*pair)
            tint = next(color(c["rgb"], "tint") for c in colors if fraction <= c["up_to"])
            im, record = render_bar(frame, track.copy(), fill, fill_box, radius, *pair, tint,
                                font, text_box, mode, text_color, stroke_width, outline)
            record["input"] = pair
            record["images"] = []
            for si, scale in enumerate(scales):
                shown = im.resize((max(1, round(im.width * scale)), max(1, round(im.height * scale))), Image.Resampling.LANCZOS)
                for label, bg in (("dark", "#18212C"), ("light", "#E9EEF2")):
                    name = f"hp-{index:02d}-scale-{si}-{label}.png"
                    outputs.append((name, ui.composite(shown, bg)))
                    record["images"].append({"file": name, "size": list(shown.size), "canvas_scale": scale})
            records.append(record)
        sources = {"frame": frame_info, "track": track_info, "fill": fill_info}
        frame_hash = hashlib.sha256(frame.tobytes()).hexdigest()
    samples = spec.get("text_samples", [])
    ui.require(isinstance(samples, list) and len(samples) <= 20, "text_samples maximum is 20")
    ui.require(records or samples, "Provide a bar or at least one text sample")
    for index, sample in enumerate(samples):
        ui.fields(sample, ["name", "text", "size", "font_size", "wrap"], "text sample")
        ui.safe_name(sample["name"])
        width, height = ui.ints(sample["size"], 2, "text sample size", 1)
        ui.require(max(width, height) <= 2048 and type(sample.get("wrap", False)) is bool, "Invalid text sample size or wrap flag")
        im = Image.new("RGBA", (width, height), "#203044")
        f = font_at(font_path, sample["font_size"])
        report = draw_text(im, sample["text"], f, [0, 0, width, height], "white", sample.get("wrap", False))
        report.update(name=sample["name"], font_size=sample["font_size"], wrap=sample.get("wrap", False))
        text_records.append(report)
        for si, scale in enumerate(scales):
            scaled = im.resize((max(1, round(width * scale)), max(1, round(height * scale))), Image.Resampling.LANCZOS)
            outputs.append((f"text-{index:02d}-scale-{si}.png", scaled.convert("RGB")))
    all_fit = all(r["text"]["fits"] for r in records) and all(r["fits"] for r in text_records)
    out.mkdir(parents=True)
    for filename, image in outputs:
        image.save(out / filename)
    # Compact, real-size contact sheet of the first six normal gauge states.
    chosen = [(name, im) for name, im in outputs if name.startswith("hp-") and "scale-0-dark" in name][:6]
    if chosen:
        sheet = Image.new("RGB", (size[0], sum(im.height + 24 for _, im in chosen)), "#18212C")
        draw = ImageDraw.Draw(sheet)
        y = 0
        for name, im in chosen:
            index = int(name.split('-')[1]);r = records[index]
            draw.text((4, y + 4), f"{number_text(r['current'])}/{number_text(r['maximum'])} ({r['fraction']:.0%})", fill="white")
            sheet.paste(im, (0, y + 24));y += im.height + 24
        sheet.save(out / "states-contact.png")
    report = {"schema_version": 1, "tool_version": ui.VERSION, "rendering": "offline_Pillow_not_game_engine",
              "config_sha256": ui.digest(config_path), "font_file": font_path.name, "font_sha256": ui.digest(font_path),
              "sources": sources,
              "frame_rendered_sha256": frame_hash,
              "fill_quantization": "round_to_pixel_positive_min_one; zero_is_empty",
              "states": records, "text_samples": text_records, "text_geometry": "passed" if all_fit else "failed",
              "visual_review": "required", "glyph_coverage_review": "required", "engine_review": "not_performed"}
    ui.write_json(out / "report.json", report)
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("config", type=Path)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    try:
        report = run(args.config, args.out)
        print(f"states={len(report['states'])}; text_geometry={report['text_geometry']}; visual_review=required; engine=not_performed")
        return 0 if report["text_geometry"] == "passed" else 2
    except (ValueError, OSError, KeyError, TypeError) as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
