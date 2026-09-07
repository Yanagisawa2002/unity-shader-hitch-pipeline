using System;
using System.Linq;
using System.Collections.Generic;
using Yanagisawa.ShaderHitchPipeline;

internal static class HotsetTests
{
    public static void Run()
    {
        int assertions = 0;
        void Check(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
        void Reject(Action action, string message) { try { action(); } catch (ArgumentException) { assertions++; return; } throw new Exception(message); }
        PsoHotsetUnit Unit(string id, bool required = false) => new PsoHotsetUnit {
            id = id, phase = id, contentId = "fixture", contentRevision = "r1", compatibilityNamespace = "build-a",
            collectionSha256 = "sha-" + id, graphicsStateCount = 4, requiredStartup = required,
            estimatedWarmupMilliseconds = 2, residentBytes = 100 };
        var units = new[] { Unit("required", true), Unit("hot"), Unit("late"), Unit("unseen") };
        var trace = new PsoHotsetTrace { routeId = "train", captureId = "capture-a", split = "training",
            compatibilityNamespace = "build-a", units = units, uses = new[] {
                new PsoHotsetUse { unitId = "hot", milliseconds = 10, frequency = 20 },
                new PsoHotsetUse { unitId = "late", milliseconds = 2000, phaseOrdinal = 2, frequency = 2 } } };
        var selected = PsoHotsetPolicy.Select(units, new[] { trace }, 4);
        Check(selected.startupUnitIds.SequenceEqual(new[] { "required", "hot" }), "Budgeted hotset selection");
        Check(selected.estimatedStartupMilliseconds == 4 && selected.startupResidentBytes == 200, "Accounting");
        Check(selected.deferredUnitIds.SequenceEqual(new[] { "late", "unseen" }), "Unseen deferred");
        Check(PsoHotsetPolicy.Select(units.AsEnumerable().Reverse().ToArray(), new[] { trace }, 4).startupUnitIds.SequenceEqual(selected.startupUnitIds), "Determinism");
        var under = PsoHotsetPolicy.Select(units, new[] { trace }, 0);
        Check(under.requiredOverBudget && under.startupUnitIds.SequenceEqual(new[] { "required" }), "Required preserved over budget");
        units[0].estimatedWarmupMilliseconds = double.NaN;
        var unknown = PsoHotsetPolicy.Select(units, new[] { trace }, 100);
        Check(!unknown.startupCostKnown && unknown.estimatedStartupMilliseconds == -1 && unknown.startupUnitIds.Length == 1, "Unknown required cost preserves and defers");
        units[0].estimatedWarmupMilliseconds = 2;
        units[1].residentBytes = -1;
        Check(PsoHotsetPolicy.Select(units, new[] { trace }, 4).startupResidentBytes == -1, "Unknown memory not zero");
        units[1].residentBytes = 100;
        units[1].estimatedWarmupMilliseconds = -1;
        Check(!PsoHotsetPolicy.Select(units, new[] { trace }, 100).startupUnitIds.Contains("hot"), "Unknown optional cost defers");
        units[1].estimatedWarmupMilliseconds = 2;
        Check(PsoHotsetPolicy.Select(units, null, 100).startupUnitIds.Length == 1, "No evidence fallback");
        trace.uses[0].frequency = -1;
        Check(PsoHotsetPolicy.Select(units, new[] { trace }, 100).rejectedTraces.Length == 1, "Negative frequency");
        trace.uses[0].frequency = 20;
        trace.uses[0].milliseconds = double.NaN;
        Check(PsoHotsetPolicy.Select(units, new[] { trace }, 100).startupUnitIds.Length == 1, "NaN evidence fallback");
        trace.uses[0].milliseconds = 10;
        trace.units = units.Select(u => Unit(u.id, u.requiredStartup)).ToArray();
        trace.units[1].contentRevision = "stale";
        Check(PsoHotsetPolicy.Select(units, new[] { trace }, 100).startupUnitIds.Length == 1, "Stale content fallback");
        trace.units = units;
        trace.split = "held-out";
        Check(PsoHotsetPolicy.Select(units, new[] { trace }, 100).startupUnitIds.Length == 1, "No held-out selection leakage");
        trace.split = "training";
        Check(PsoHotsetPolicy.Select(units, new[] { trace, trace }, 4).rejectedTraces.Length == 2, "Duplicate captures rejected");
        var held = new PsoHotsetTrace { routeId = "held", captureId = "capture-h", split = "held-out",
            compatibilityNamespace = "build-a", units = units, uses = new[] {
                new PsoHotsetUse { unitId = "required", milliseconds = 0 },
                new PsoHotsetUse { unitId = "late", milliseconds = 10, frequency = 2 },
                new PsoHotsetUse { unitId = "late", milliseconds = 30 } } };
        var replay = PsoHotsetPolicy.Replay(units, selected, held);
        Check(replay.missedUnits == 1 && replay.missedStateEntries == 4, "Unique first-use miss");
        Check(replay.extraWarmedUnits == 1 && replay.extraWarmedStateEntries == 4, "Unused startup cost");
        Check(replay.coveredUseEvents == 1 && replay.useEvents == 4, "Frequency coverage");
        replay = PsoHotsetPolicy.Replay(units, selected, held, new Dictionary<string, double> { ["late"] = 10 });
        Check(replay.missedUnits == 0 && replay.coveredUseEvents == 4, "Exact deferred completion boundary");
        held.routeId = "train";
        Reject(() => PsoHotsetPolicy.Replay(units, selected, held), "Route leakage");
        held.routeId = "held";
        held.captureId = "capture-a";
        Reject(() => PsoHotsetPolicy.Replay(units, selected, held), "Capture leakage");
        Reject(() => PsoHotsetPolicy.Select(units, null, double.PositiveInfinity), "Infinite budget");
        Reject(() => PsoHotsetPolicy.Select(new[] { units[0], units[0] }, null, 0), "Duplicate units");
        var plan = new PsoWarmupPlanDocument { phases = units.Select(u => new PsoWarmupPhasePlan {
            phase = u.phase, required = u.requiredStartup, prewarmAtStartup = true,
            collectionSha256 = u.collectionSha256, graphicsStateCount = u.graphicsStateCount }).ToArray() };
        Check(PsoStartupHotset.Prepare(plan, "plan", null).startupUnitIds.SequenceEqual(new[] { "required" }), "Malformed runtime policy preserves required");
        var document = new PsoStartupHotsetDocument { planSha256 = "plan", units = units, training = new[] { trace }, startupBudgetMilliseconds = 4 };
        units[0].requiredStartup = false; // hostile policy cannot change caller declaration.
        Check(PsoStartupHotset.Prepare(plan, "plan", document).startupUnitIds.Contains("required"), "Caller required authoritative");
        document.planSha256 = "wrong";
        Check(PsoStartupHotset.Prepare(plan, "plan", document).startupUnitIds.Length == 1, "Wrong plan fallback");
        document.planSha256 = "plan";
        Check(PsoStartupHotset.PrepareValidated(plan, "plan", document, null).startupUnitIds.Length == 1, "Absent compatibility bridge fallback");
        Check(PsoStartupHotset.PrepareValidated(plan, "plan", document, (p,d) => false).startupUnitIds.Length == 1, "Rejected compatibility fallback");
        Check(PsoStartupHotset.PrepareValidated(plan, "plan", document, (p,d) => throw new Exception("missing identity")).startupUnitIds.Length == 1, "Unknown compatibility fallback");
        Check(PsoStartupHotset.PrepareValidated(plan, "plan", document, (p,d) => true).startupUnitIds.Contains("hot"), "Validated runtime selection");
        trace.uses[0].milliseconds = -1;
        Check(PsoHotsetPolicy.Select(units, new[] { trace }, 100).rejectedTraces.Length == 1, "Negative timestamp");
        trace.uses[0].milliseconds = 10;
        held.captureId = "capture-h";
        var extra = Unit("unknown");
        held.units = units.Concat(new[] { extra }).ToArray();
        held.uses = new[] { new PsoHotsetUse { unitId = "unknown", milliseconds = 10, frequency = 3 } };
        Check(PsoHotsetPolicy.Replay(units, selected, held).unknownUseEvents == 3, "Unknown heldout coverage explicit");
        units[0].requiredStartup = true;
        trace.uses = new[] {
            new PsoHotsetUse { unitId = "hot", frequency = 10, milliseconds = 10 },
            new PsoHotsetUse { unitId = "late", frequency = 10, milliseconds = 2000 } };
        Check(PsoHotsetPolicy.Select(units, new[] { trace }, 4).startupUnitIds.Contains("hot"), "First use affects rank");
        trace.uses[1].milliseconds = 10;
        trace.uses[0].phaseOrdinal = 3;
        Check(PsoHotsetPolicy.Select(units, new[] { trace }, 4).startupUnitIds.Contains("late"), "Phase affects rank");
        trace.uses[0].phaseOrdinal = 0;
        trace.uses[0].frequency = 100;
        Check(PsoHotsetPolicy.Select(units, new[] { trace }, 4).startupUnitIds.Contains("hot"), "Frequency affects rank");
        trace.uses[0].frequency = 10;
        Check(PsoHotsetPolicy.Select(units, new[] { trace }, 4).startupUnitIds.Contains("hot"), "Ordinal tie break");
        trace.compatibilityNamespace = "";
        Check(PsoHotsetPolicy.Select(units, new[] { trace }, 100).startupUnitIds.Length == 1, "Unknown namespace fallback");
        Console.WriteLine("HOTSET_TESTS_OK assertions=" + assertions);
    }
}
