"""Offline consistency audit; never derives selection from held-out outcomes."""
import argparse
import hashlib
import json
import math
from pathlib import Path


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def verify(root):
    summary = read(root / "summary.json")
    policy = read(root / "frozen-policy.json")
    catalog = read(root / "catalog.json")
    assert digest(root / "fixture-plan.json") == policy["planSha256"], "Frozen plan integrity"
    assert not list(root.glob("failure-*.txt")), "Player failure artifacts present"
    for item in summary["hashes"]:
        assert digest(root / item["file"]) == item["sha256"], item["file"]
    for i in range(4):
        discovery = read(root / f"unit-{i}.discovery.json")
        assert discovery["saved"] and discovery["stateCount"] >= 4, "Native state discovery"
        assert digest(root / f"unit-{i}.graphicsstate") == catalog["units"][i]["collectionSha256"]
    train_ids = {trace["routeId"] for trace in policy["training"]}
    capture_ids = {trace["captureId"] for trace in policy["training"]}
    assert train_ids == {"train-a", "train-b"} and len(capture_ids) == 2
    smoke = read(root / "orchestrator-smoke.json")
    assert smoke["completed"] and "u0" in smoke["decision"]["startupUnitIds"]
    observations = []
    for row in summary["runs"]:
        trace = read(root / (row["id"] + ".trace.json"))
        receipt = read(root / (row["id"] + ".receipt.json"))
        assert receipt["completed"] and receipt["buildGuid"] == catalog["buildGuid"]
        assert receipt["observedDriverVersion"] == catalog["observedDriverVersion"]
        assert trace["split"] == "held-out" and trace["routeId"] not in train_ids
        assert trace["captureId"] not in capture_ids and "u0" in receipt["decision"]["startupUnitIds"]
        assert receipt["decision"]["rejectedTraces"] == [], "Rejected selection evidence"
        if row["arm"] == "hotset":
            assert receipt["decision"]["startupUnitIds"] == smoke["decision"]["startupUnitIds"]
        sequence = summary["declaration"]["heldOutRoutes"][row["route"]]
        assert receipt["renderedGroupSequence"] == sequence, "Actual route identity"
        assert len(trace["uses"]) == 480 and all(use["frequency"] == 1 for use in trace["uses"])
        for stage, group in enumerate(sequence):
            stage_uses = [use for use in trace["uses"] if use["phaseOrdinal"] == stage]
            assert len(stage_uses) == 96 and all(use["unitId"] == f"u{group}" for use in stage_uses)
        frames = receipt["frameMilliseconds"]
        assert len(frames) == 119 and all(math.isfinite(value) and value > 0 for value in frames)
        assert math.isclose(max(frames), receipt["laterFrames"]["maximumMilliseconds"], rel_tol=1e-12)
        assert sum(value >= 16.67 for value in frames) == receipt["laterFrames"]["hitchFrameCount"]
        assert not receipt["firstPresentAvailable"] and receipt["firstPresentMilliseconds"] == -1
        assert receipt["driverPsoMemoryBytes"] == -1 and not receipt["decision"]["startupMemoryKnown"]
        assert receipt["coverage"]["useEvents"] == len(trace["uses"])
        startup = set(receipt["decision"]["startupUnitIds"])
        completed = dict(zip(receipt["warmedUnitIds"], receipt["completionMilliseconds"]))
        seen, missed, covered = set(), set(), 0
        for use in sorted(trace["uses"], key=lambda item: (item["milliseconds"], item["unitId"])):
            unit = use["unitId"]
            ready = unit in startup or (unit in completed and completed[unit] <= use["milliseconds"])
            if ready:
                covered += use["frequency"]
            elif unit not in seen:
                missed.add(unit)
            seen.add(unit)
        counts = {unit["id"]: unit["graphicsStateCount"] for unit in catalog["units"]}
        assert receipt["coverage"]["coveredUseEvents"] == covered
        assert receipt["coverage"]["missedUnits"] == len(missed)
        assert receipt["coverage"]["missedStateEntries"] == sum(counts[unit] for unit in missed)
        assert receipt["coverage"]["extraWarmedStateEntries"] == sum(counts[unit] for unit in startup - seen)
        observations.append({
            "id": row["id"], "startupUnits": receipt["decision"]["startupUnitIds"],
            "firstRenderedFrameEngineMilliseconds": receipt["firstRenderedFrameEngineMilliseconds"],
            "startupBlockingCallMilliseconds": receipt["startupWarmupMilliseconds"],
            "deferredObservedElapsedMilliseconds": receipt["deferredWarmupMilliseconds"],
            "deferredSubmittedStates": receipt["deferredSubmittedStates"],
            "maximumLaterFrameMilliseconds": max(frames),
            "hitchFramesAt16_67": receipt["laterFrames"]["hitchFrameCount"],
            "coverage": receipt["coverage"], "nativePlanMissStates": receipt["observedPlanCacheMissStates"],
            "engineAllocatedByteDelta": receipt["engineAllocatedBytesAfterWarmup"] - receipt["engineAllocatedBytesBeforeWarmup"],
            "driverPsoMemoryBytes": None, "osFirstPresentMilliseconds": None,
            "extraTotalWarmedStateEntries": receipt["extraTotalWarmedStateEntries"],
        })
    expected = summary["declaration"]["repetitions"] * 6
    assert len(observations) == expected
    return {"status": "passed", "verifiedCells": expected, "observations": observations,
            "interpretation": "Correctness smoke; no zero-hitch acceptance requirement and no performance promotion. All noisy frames retained."}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("root", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    result = verify(args.root)
    if args.output:
        args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(f'HOTSET_EVIDENCE_AUDIT_OK cells={result["verifiedCells"]}')
