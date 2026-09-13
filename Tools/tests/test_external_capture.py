"""CPU-only evidence-gate fixtures; never Player evidence or a timing workload."""
import copy
import unittest
from collections import Counter
from pso_external_capture import complete_render_indices, native_policy_failures, statistics
from pso_boat_comparison import ORDER


class ExternalCaptureTests(unittest.TestCase):
    def test_original_render_frame_coverage(self):
        self.assertTrue(complete_render_indices(range(500), 500))
        self.assertFalse(complete_render_indices(range(0, 500, 2), 500))
        self.assertFalse(complete_render_indices([*range(499), 498], 500))
        self.assertFalse(complete_render_indices([*range(500), 499], 500))

    def test_complete_native_work_required_independently_of_content(self):
        run = dict(error='', cacheMissTrace=dict(armed=True, error=''), phases=[dict(
            phase='p', error='', totalGraphicsStates=6, completedGraphicsStates=6,
            backendReportedWarmedUp=True, batchCount=1)])
        scheduler = dict(hasInFlightBatch=False, hasPendingRetirement=False, activations=[dict(
            phase='p', state='Completed', failure='', ownerReleased=True, hasInFlightBatch=False,
            backendReportedWarmedUp=True, completedPermutations=5)])
        def validate(r=run, s=scheduler):
            return native_policy_failures(dict(policy='scheduled'), [dict(data=r)], [dict(data=s)], ['p'])
        self.assertEqual(validate(), [])
        for field, value in [('activations', []), ('hasPendingRetirement', True)]:
            bad = copy.deepcopy(scheduler); bad[field] = value
            self.assertTrue(validate(s=bad))
        for field, value in [('completedPermutations', 0), ('ownerReleased', False), ('state', 'Unavailable')]:
            bad = copy.deepcopy(scheduler); bad['activations'][0][field] = value
            self.assertTrue(validate(s=bad))
        bad = copy.deepcopy(run); bad['phases'][0]['completedGraphicsStates'] = 0
        self.assertTrue(validate(r=bad))
        self.assertTrue(native_policy_failures(dict(policy='scheduled'), [], [], ['p']))
        run['phases'] = []; scheduler['activations'] = []
        self.assertEqual(native_policy_failures(dict(policy='disabled', planBaselineForDisabled=True),
            [dict(data=run)], [dict(data=scheduler)], ['p']), [])
        self.assertTrue(native_policy_failures(dict(policy='disabled'), [dict(data=run)], [dict(data=scheduler)], ['p']))

    def test_tail_and_strict_hitch_thresholds_keep_extreme_samples(self):
        d = statistics([1]*98+[50, 600])
        self.assertEqual(d['p99Milliseconds'], 50)
        self.assertEqual(d['maximumMilliseconds'], 600)
        self.assertEqual(d['hitchCounts']['50'], 1)
        for bad in ([], [-1], [float('nan')]):
            with self.assertRaises(ValueError): statistics(bad)

    def test_declared_order_balances_positions_and_directed_carryover(self):
        self.assertTrue(all(set(row) == set(range(4)) for row in ORDER))
        self.assertTrue(all(set(column) == set(range(4)) for column in zip(*ORDER)))
        self.assertEqual(set(Counter((a,b) for row in ORDER for a,b in zip(row,row[1:])).values()), {1})


if __name__ == '__main__':
    unittest.main()
