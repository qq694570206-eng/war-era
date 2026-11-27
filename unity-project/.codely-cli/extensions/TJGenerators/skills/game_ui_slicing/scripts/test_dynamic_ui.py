#!/usr/bin/env python3
"""Behavior checks for dynamic UI composition, without fonts or network installs."""
import unittest
from PIL import Image, ImageDraw, ImageFont
import preview_dynamic_ui as hud


class DynamicTests(unittest.TestCase):
    def setUp(self):
        self.frame = Image.new('RGBA', (100, 60))
        ImageDraw.Draw(self.frame).rectangle((0, 0, 99, 59), outline=(240, 200, 100, 255), width=4)
        self.track = Image.new('RGBA', (84, 20), (8, 16, 24, 255))
        self.fill = Image.new('RGBA', (84, 20))
        for x in range(84):
            for y in range(20): self.fill.putpixel((x, y), (128 + x, 128 + x, 128 + x, 255))
        self.font = ImageFont.load_default()

    def render(self, value, maximum=100):
        return hud.render_bar(self.frame, self.track, self.fill, [8, 10, 84, 20], 0,
                              value, maximum, (255, 0, 0), self.font, [10, 36, 80, 20],
                              'current_max', (255, 255, 255))

    def test_zero_one_mid_full_and_frame_invariance(self):
        widths = []
        for value in [0, 1, 25, 50, 99, 100]:
            im, report = self.render(value)
            widths.append(report['fill_width'])
            self.assertEqual(im.getpixel((1, 1)), (240, 200, 100, 255))
            self.assertEqual(im.getpixel((6, 15))[3], 0)  # No fill outside the inner opening.
        self.assertEqual(widths, [0, 1, 21, 42, 83, 84])
        zero, _ = self.render(0)
        self.assertEqual(zero.getpixel((8, 15)), (8, 16, 24, 255))
        full, _ = self.render(100)
        self.assertNotEqual(full.getpixel((91, 15)), (8, 16, 24, 255))

    def test_fill_is_clipped_not_rescaled_as_value_changes(self):
        half, _ = self.render(50)
        full, _ = self.render(100)
        self.assertEqual(half.crop((8, 10, 50, 30)).tobytes(), full.crop((8, 10, 50, 30)).tobytes())
        self.assertEqual(half.getpixel((50, 15)), (8, 16, 24, 255))

    def test_invalid_values_clamp_and_never_divide_by_zero(self):
        self.assertEqual(hud.ratio(-5, 100), (0, 100, 0))
        self.assertEqual(hud.ratio(120, 100), (100, 100, 1))
        self.assertEqual(hud.ratio(5, 0), (0, 0, 0))
        self.assertEqual(hud.ratio(5, -2), (0, 0, 0))
        with self.assertRaises(ValueError): hud.ratio(float('nan'), 100)
        with self.assertRaises(ValueError): hud.ratio(1, float('inf'))

    def test_long_text_is_reported_instead_of_silently_clipped(self):
        im = Image.new('RGBA', (80, 40))
        report = hud.draw_text(im, 'A long player name ' * 3, self.font, [0, 0, 30, 10], 'white')
        self.assertFalse(report['fits'])
        wrapped = hud.draw_text(im, 'Alpha Beta', self.font, [0, 0, 32, 40], 'white', wrap=True)
        self.assertIn('\n', wrapped['rendered_text'])

    def test_large_values_never_silently_switch_to_scientific_notation(self):
        _, report = self.render(1000000, 1000000)
        self.assertEqual(report['text']['rendered_text'], '1000000/1000000')
        self.assertEqual(hud.number_text(12345.5), '12345.5')

    def test_outline_keeps_white_value_visible_on_bright_fill(self):
        im = Image.new('RGB', (80, 30), 'white')
        report = hud.draw_text(im, 'HP', self.font, [0, 0, 80, 30], 'white', stroke_width=1, stroke_fill='black')
        self.assertTrue(report['fits'])
        self.assertTrue(any(max(pixel) < 32 for pixel in im.getdata()))


if __name__ == '__main__':
    unittest.main(verbosity=2)
