"""Run with python -m unittest discover -s Tests/Python (NumPy, ffmpeg/ffprobe)."""
import importlib.util
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

import numpy as np

SCRIPT = Path(__file__).resolve().parents[2] / 'extension/skills/game_audio_production/scripts/audio_finish.py'
SPEC = importlib.util.spec_from_file_location('audio_finish', SCRIPT)
af = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(af)


class AudioEventRegressionTests(unittest.TestCase):
    rate = 48000

    def decay(self):
        t = np.arange(self.rate) / self.rate
        return (.5 * np.sin(2 * np.pi * 180 * t) * np.exp(-6 * t))[:, None]

    def test_single_ring_is_not_counted_every_45_ms(self):
        self.assertEqual(1, af.transient_peak_hint(self.decay(), self.rate)['count'])

    def test_smooth_tone_is_one_region_despite_zero_crossings(self):
        t = np.arange(self.rate) / self.rate
        sound = (.3 * np.sin(2 * np.pi * 440 * t) * np.sin(np.pi * t) ** 2)[:, None]
        self.assertEqual(1, af.transient_peak_hint(sound, self.rate)['count'])

    def test_separated_attacks_are_still_reported(self):
        hit = self.decay()[:self.rate // 5]
        sound = np.concatenate([hit, np.zeros((self.rate // 5, 1)), hit])
        hint = af.transient_peak_hint(sound, self.rate)
        self.assertEqual(2, hint['count'])
        self.assertAlmostEqual(.4, hint['times_seconds'][1], delta=.015)

    def test_brief_dropout_does_not_rearm_detector(self):
        sound = np.full((self.rate // 2, 1), .3)
        sound[4800:5280] = 0  # 10 ms, not an independent onset.
        self.assertEqual(1, af.transient_peak_hint(sound, self.rate)['count'])

    def test_silence_and_quiet_noise_do_not_create_events(self):
        self.assertEqual(0, af.transient_peak_hint(np.zeros((4800, 1)), self.rate)['count'])
        self.assertEqual(0, af.transient_peak_hint(np.full((4800, 1), .001), self.rate)['count'])

    def test_stereo_uses_both_channels_without_modifying_input(self):
        sound = np.column_stack([np.zeros(self.rate), self.decay()[:, 0]])
        original = sound.copy()
        self.assertEqual(1, af.transient_peak_hint(sound, self.rate)['count'])
        np.testing.assert_array_equal(sound, original)

    @unittest.skipUnless(shutil.which('ffmpeg') and shutil.which('ffprobe'), 'ffmpeg/ffprobe required')
    def test_cli_preserves_single_event_tail_and_only_advises_review_for_multiple_regions(self):
        hit = self.decay()
        cases = [('single', hit, 1),
                 ('multiple', np.concatenate([hit, np.zeros((4800, 1)), hit]), 2)]
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            for name, sound, expected in cases:
                source = root / (name + '.wav')
                af.write_wav(source, sound, self.rate)
                run = subprocess.run([sys.executable, '-B', str(SCRIPT), 'oneshot', str(source),
                                      '--out', str(root / name)], capture_output=True, text=True)
                self.assertEqual(0, run.returncode, run.stderr)
                payload = json.loads(run.stdout)
                report = json.loads((root / name / 'report.json').read_text(encoding='utf-8'))
                self.assertEqual(expected, payload['transient_peak_count'])
                self.assertNotIn('oneshot_needs_event_window', payload)
                self.assertNotIn('oneshot_needs_event_window', report)
                self.assertEqual(expected > 1, report.get('oneshot_review_recommended', False))
                self.assertGreater(payload['duration'], .8 if expected == 1 else 1.8)


if __name__ == '__main__':
    unittest.main()
