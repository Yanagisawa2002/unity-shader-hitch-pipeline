"""Compare a declared off/on pair of complete diagnostic audits; no runner."""
import argparse
import json
from pathlib import Path
from pso_external_capture import load, sha
from pso_whole_task_evidence import profiler_pair


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('off', type=Path)
    parser.add_argument('on', type=Path)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    result = profiler_pair(load(args.off), load(args.on))
    result['analysisInputs'] = {str(p): sha(p) for p in (args.off, args.on)}
    with args.output.open('x', encoding='utf-8') as stream:
        json.dump(result, stream, indent=2, allow_nan=False); stream.write('\n')
    print(json.dumps({k: result[k] for k in ('pairComparable', 'failures', 'lowOverheadCertified')}))
    return 0 if result['pairComparable'] else 1


if __name__ == '__main__': raise SystemExit(main())
