"""CPU-only evidence-gate fixtures; never Player evidence or a timing workload."""
import copy
import unittest
from collections import Counter
from pso_external_capture import complete_render_indices, native_policy_failures, statistics
from pso_boat_comparison import ENVIRONMENT_KEYS, ORDER, PROCESS_CACHE, identity_failures, index, raw_validation_failures, save, verify_index
from pso_external_capture import sha
from pathlib import Path
import tempfile


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

    def test_summary_binds_native_validation_and_frozen_run_identity(self):
        environment = {key:'fixture-'+key for key in ENVIRONMENT_KEYS}
        protocol = dict(buildGuid='build',planFileSha256='plan',environment=environment)
        result = dict(accepted=True,contentAccepted=True,nativePolicyValidated=True,policy='scheduled',buildGuid='build',
            screenshotsEnabled=False,traceEnabled=False,cacheCondition=PROCESS_CACHE,persistentAllocationWarnings=[],
            warmupReceipts=[dict(data=dict(planSha256='plan',environment=environment))])
        run = dict(policy='scheduled')
        self.assertFalse(identity_failures(result,run,protocol))
        for key,value in [('accepted',False),('nativePolicyValidated',False),('policy','disabled'),('buildGuid','another'),
                          ('screenshotsEnabled',True),('traceEnabled',True),('cacheCondition','driver-cold')]:
            bad=copy.deepcopy(result);bad[key]=value
            self.assertTrue(identity_failures(bad,run,protocol),key)
        bad=copy.deepcopy(result);bad['warmupReceipts'][0]['data']['environment']['driverIdentity']='different'
        self.assertTrue(identity_failures(bad,run,protocol))

    def test_summary_file_index_detects_modified_bytes_extra_files_and_changed_index(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp);player=root/'player';player.mkdir();file=player/'fixture';file.write_bytes(b'first')
            receipt=root/'index.json';save(receipt,index(player));digest=sha(receipt)
            self.assertFalse(verify_index(player,receipt,digest))
            file.write_bytes(b'other')
            self.assertTrue(verify_index(player,receipt,digest))
            file.write_bytes(b'first');(player/'extra').write_bytes(b'extra')
            self.assertTrue(verify_index(player,receipt,digest))
            receipt.write_text('[]')
            self.assertTrue(verify_index(player,receipt,digest))

    def test_summary_does_not_trust_stale_total_time_or_memory(self):
        actual={key:[] for key in ('routes','nativeResults','schedulingFeedback','warmupReceipts','phaseEvents')}
        actual.update(captureSha256='raw',playerLogSha256='log',allUpdateIntervals={'hitchCounts':{'50':1,'200':1,'500':0}},
                      observerElapsedSeconds=30,observerToFirstUpdateSeconds=2,peakAllocatedBytes=1000,peakReservedBytes=2000)
        self.assertFalse(raw_validation_failures(actual,actual,[50,200,500]))
        for key in ('observerElapsedSeconds','observerToFirstUpdateSeconds','peakAllocatedBytes','peakReservedBytes'):
            stale=copy.deepcopy(actual);stale[key]+=1
            self.assertEqual(raw_validation_failures(stale,actual,[50,200,500]),
                             ['Saved validation differs from actual raw evidence: '+key])
        older=copy.deepcopy(actual);older['allUpdateIntervals']['hitchCounts']={'50':1}
        self.assertFalse(raw_validation_failures(older,actual,[50]))
        self.assertTrue(raw_validation_failures(older,actual,[50,200,500]))


if __name__ == '__main__':
    unittest.main()
