import json
import tempfile
import unittest
from pathlib import Path

from analyze_pso_capture_windows import audit
from pso_windows_evidence import sha256


class FullWindowAuditTests(unittest.TestCase):
    def make(self, root):
        qpc = 2**54
        rows = ['ProcessID,CPUStartQPC,SwapChainAddress,FrameTime,DisplayedTime,GPUBusy',
                f'42,{qpc-1},A,40,NA,1', f'42,{qpc+1},A,2,16.67,1',
                f'42,{qpc+100001},B,3,17,2', f'99,{qpc+1},C,999,999,999']
        (root/'frames.csv').write_text('\n'.join(rows))
        markers = [dict(processId=42,sessionId='test',name=name,phase='',
                        utc=utc,qpc=ticks,qpcFrequency=10000000,qpcBracketTicks=0,
                        clockSource='windows-query-performance-counter-v1')
                   for name,utc,ticks in [('start','2026-09-07T00:00:00Z',qpc),
                                          ('end','2026-09-07T00:00:00.010Z',qpc+100000)]]
        (root/'markers.jsonl').write_text('\n'.join(json.dumps(m) for m in markers))
        (root/'warmup.json').write_text(json.dumps(dict(startedUtc='2026-09-07T00:00:00Z',endedUtc='2026-09-07T00:00:00.005Z')))
        (root/'benchmark.json').write_text('{}')
        artifacts = {key:dict(path=str(root/name),sha256=sha256(root/name)) for key,name in
                     [('presentMonCsv','frames.csv'),('markers','markers.jsonl'),('warmupReceipt','warmup.json'),('benchmarkReceipt','benchmark.json')]}
        manifest=root/'manifest.json'
        manifest.write_text(json.dumps(dict(artifacts=artifacts,target=dict(processId=42,startedUtc='2026-09-06T23:59:59Z',endedUtc='2026-09-07T00:00:01Z'))))
        return manifest

    def test_every_owned_row_and_chain_retained_with_integer_boundaries(self):
        with tempfile.TemporaryDirectory() as directory:
            result=audit(self.make(Path(directory)))
            self.assertEqual([s['rowCount'] for s in result['stages']],[1,1,1])
            self.assertEqual(result['completeness']['foreignPidRows'],1)
            self.assertEqual(result['completeness']['swapChains'],dict(A=2,B=1))
            self.assertEqual(result['full']['metrics']['presentedFrameMilliseconds']['maximumMilliseconds'],40)
            self.assertEqual(result['full']['metrics']['displayedFrameMilliseconds']['missingCount'],1)
            self.assertEqual(result['completeness']['cpuIntervalsCrossingMarkerBoundary'],1)

    def test_mutated_raw_input_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory)
            manifest=self.make(root)
            (root/'frames.csv').write_text('tampered')
            with self.assertRaisesRegex(ValueError,'hash mismatch'):
                audit(manifest)


if __name__ == '__main__':
    unittest.main()
