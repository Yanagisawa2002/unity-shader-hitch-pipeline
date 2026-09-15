"""Synthetic analysis-contract tests, not scene or performance evidence."""
import sys
from pathlib import Path
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from pso_whole_task_analysis import interval_summary, recorder_summary
from pso_boat_comparison import identity_failures


def frames():
    return [dict(frame=1,seconds=3.5,updateIntervalMilliseconds=0,scene='loading'),
            dict(frame=2,seconds=3.72,updateIntervalMilliseconds=220,scene='loading'),
            dict(frame=3,seconds=3.73,updateIntervalMilliseconds=10,scene='scene')]


class WholeTaskContracts(unittest.TestCase):
    def test_initial_stall_and_blind_boundaries_are_retained(self):
        result = interval_summary(frames(), 3.8)
        self.assertAlmostEqual(result['maximumMilliseconds'], 220)
        self.assertEqual(result['countsAboveMilliseconds']['200'], 1)
        self.assertAlmostEqual(result['observerToFirstUpdateSeconds'], 3.5)
        self.assertAlmostEqual(result['lastUpdateToQuitCallbackSeconds'], .07)
        self.assertAlmostEqual(result['excessAbove33333Milliseconds'], 186.667)
        self.assertEqual(len(result['longIntervals']), 1)

    def test_inconsistent_stored_interval_fails(self):
        data = frames(); data[1]['updateIntervalMilliseconds'] = 20
        with self.assertRaises(ValueError): interval_summary(data, 3.8)

    def test_frame_gap_stays_visible(self):
        data = frames(); data[-1]['frame'] = 5
        self.assertEqual(interval_summary(data, 3.8)['frameGaps'], [[2, 5]])

    def test_unavailable_is_not_zero_and_unknown_unit_is_not_ns(self):
        document = dict(diagnosticOnly=True, normalQuit=True, metrics=[
            dict(name='missing', available=False, unit=None),
            dict(name='unknown-unit', available=True, unit='Count'),
            dict(name='shader', available=True, unit='TimeNanoseconds')], samples=[
                dict(observedAtUnityFrame=2, values=[-1, 12, 4000000], counts=[-1, 1, 2])])
        result = recorder_summary(document)['metrics']
        self.assertIsNone(result[0]['maximumRecordedWorkMilliseconds'])
        self.assertIsNone(result[1]['maximumRecordedWorkMilliseconds'])
        self.assertEqual(result[2]['maximumRecordedWorkMilliseconds'], 4)
        self.assertEqual(result[2]['positiveSamples'][0]['markerSampleCount'], 2)

    def test_incomplete_or_ragged_recorder_fails(self):
        with self.assertRaises(ValueError): recorder_summary(dict(normalQuit=False))
        with self.assertRaises(ValueError):
            recorder_summary(dict(diagnosticOnly=True, normalQuit=True, metrics=[], samples=[dict(values=[1], counts=[1])]))

    def test_formal_gate_rejects_discovery_and_profiler_modes(self):
        for flag in ('diagnosticOnly', 'observerOnly'):
            result = dict(accepted=True, contentAccepted=True, nativePolicyValidated=True,
                          policy='disabled', buildGuid='build', screenshotsEnabled=False, traceEnabled=False,
                          **{flag: True})
            failures = identity_failures(result, dict(policy='disabled'), dict(buildGuid='build'))
            self.assertIn('Whole-task discovery/profiler mode is not a formal policy comparison arm', failures)


if __name__ == '__main__': unittest.main()
