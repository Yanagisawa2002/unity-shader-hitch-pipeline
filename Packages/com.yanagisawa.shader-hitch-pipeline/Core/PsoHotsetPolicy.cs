using System;
using System.Collections.Generic;
using System.Linq;

namespace Yanagisawa.ShaderHitchPipeline
{
    // A unit is an atomic independently warmable collection, not an arbitrary PSO.
    // Id is owner-independent; content revision and compatibility namespace must be
    // supplied by the content/build authority, never inferred from an integrity hash.
    [Serializable]
    public sealed class PsoHotsetUnit
    {
        public string id;
        public string phase;
        public string contentId;
        public string contentRevision;
        public string compatibilityNamespace;
        public string collectionSha256;
        public bool requiredStartup;
        public int graphicsStateCount;
        public double estimatedWarmupMilliseconds = -1;
        public long residentBytes = -1;
    }

    [Serializable]
    public sealed class PsoHotsetUse
    {
        public string unitId;
        public int phaseOrdinal;
        public double milliseconds;
        public int frequency = 1;
    }

    [Serializable]
    public sealed class PsoHotsetTrace
    {
        public string routeId;
        public string captureId;
        public string split; // "training" or "held-out"; route IDs may not cross splits.
        public string compatibilityNamespace;
        public PsoHotsetUnit[] units = Array.Empty<PsoHotsetUnit>();
        public PsoHotsetUse[] uses = Array.Empty<PsoHotsetUse>();
    }

    [Serializable]
    public sealed class PsoHotsetDecision
    {
        public int schemaVersion = 1;
        public string policy = "route-frequency-first-use-per-cost-v1";
        public string[] startupUnitIds = Array.Empty<string>();
        public string[] deferredUnitIds = Array.Empty<string>();
        public string[] trainingRouteIds = Array.Empty<string>();
        public string[] trainingCaptureIds = Array.Empty<string>();
        public string[] rejectedTraces = Array.Empty<string>();
        public double startupBudgetMilliseconds;
        public double estimatedStartupMilliseconds;
        public bool startupCostKnown = true;
        public bool requiredOverBudget;
        public long startupResidentBytes;
        public bool startupMemoryKnown = true;
        public int startupStateEntries;
        public string accounting = "collection entries; overlap may double-count; bytes exclude opaque driver allocations";
        public string guarantee = "estimate-only admission; opaque driver calls are not preemptible or frame-time bounded";
    }

    [Serializable]
    public sealed class PsoHotsetCoverage
    {
        public string routeId;
        public int useEvents;
        public int coveredUseEvents;
        public int missedUnits;
        public int missedStateEntries;
        public int extraWarmedUnits;
        public int extraWarmedStateEntries;
        public int unknownUseEvents;
        public string interpretation = "replay coverage at first use, not measured driver cache misses or hitches";
    }

    public static class PsoHotsetPolicy
    {
        public static bool SameIdentity(PsoHotsetUnit a, PsoHotsetUnit b)
        {
            return a != null && b != null && a.id == b.id && a.contentId == b.contentId &&
                a.contentRevision == b.contentRevision &&
                a.compatibilityNamespace == b.compatibilityNamespace &&
                a.collectionSha256 == b.collectionSha256 && a.graphicsStateCount == b.graphicsStateCount;
        }

        public static PsoHotsetDecision Select(PsoHotsetUnit[] units,
            PsoHotsetTrace[] training, double startupBudgetMilliseconds)
        {
            if (!Finite(startupBudgetMilliseconds) || startupBudgetMilliseconds < 0)
                throw new ArgumentOutOfRangeException(nameof(startupBudgetMilliseconds));
            var map = ValidateUnits(units);
            var accepted = new List<PsoHotsetTrace>();
            var rejected = new List<string>();
            var sources = training ?? Array.Empty<PsoHotsetTrace>();
            var duplicates = new HashSet<string>(sources.Where(t => t != null && t.captureId != null)
                .GroupBy(t => t.captureId, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key), StringComparer.Ordinal);
            foreach (var trace in (training ?? Array.Empty<PsoHotsetTrace>())
                .OrderBy(t => t == null ? "" : t.captureId, StringComparer.Ordinal))
            {
                string reason = ValidateTrace(trace, map, "training");
                if (reason == null && duplicates.Contains(trace.captureId)) reason = "duplicate-capture";
                if (reason != null) rejected.Add((trace?.captureId ?? "<null>") + ":" + reason);
                else accepted.Add(trace);
            }
            var result = new PsoHotsetDecision
            {
                startupBudgetMilliseconds = startupBudgetMilliseconds,
                rejectedTraces = rejected.ToArray(),
                trainingRouteIds = accepted.Select(t => t.routeId).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToArray(),
                trainingCaptureIds = accepted.Select(t => t.captureId).ToArray()
            };
            var selected = new List<PsoHotsetUnit>();
            foreach (var unit in units.Where(u => u.requiredStartup).OrderBy(u => u.id, StringComparer.Ordinal))
                Add(result, selected, unit);
            result.requiredOverBudget = !result.startupCostKnown || result.estimatedStartupMilliseconds > startupBudgetMilliseconds;

            // Aggregate once per route, averaging repeat captures so repeated training
            // sessions do not silently turn into extra route popularity weight.
            var scores = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var unit in units.Where(u => !u.requiredStartup))
            {
                if (!Finite(unit.estimatedWarmupMilliseconds) || unit.estimatedWarmupMilliseconds < 0) continue;
                double score = 0;
                foreach (var route in accepted.GroupBy(t => t.routeId))
                {
                    double routeScore = 0;
                    foreach (var trace in route)
                    {
                        var uses = trace.uses.Where(e => e.unitId == unit.id).ToArray();
                        if (uses.Length == 0) continue;
                        double frequency = uses.Sum(e => (double)e.frequency);
                        double first = uses.Min(e => e.milliseconds);
                        int phase = uses.Min(e => e.phaseOrdinal);
                        routeScore += Math.Log(1 + frequency) / ((1 + first / 1000.0) * (1.0 + phase));
                    }
                    score += routeScore / route.Count();
                }
                if (score > 0) scores[unit.id] = score / Math.Max(0.001, unit.estimatedWarmupMilliseconds);
            }
            foreach (var unit in units.Where(u => scores.ContainsKey(u.id))
                .OrderByDescending(u => scores[u.id]).ThenBy(u => u.id, StringComparer.Ordinal))
            {
                if (result.startupCostKnown && result.estimatedStartupMilliseconds + unit.estimatedWarmupMilliseconds <= startupBudgetMilliseconds)
                    Add(result, selected, unit);
            }
            result.startupUnitIds = selected.Select(u => u.id).ToArray();
            var ids = new HashSet<string>(result.startupUnitIds, StringComparer.Ordinal);
            result.deferredUnitIds = units.Where(u => !ids.Contains(u.id)).Select(u => u.id).OrderBy(s => s, StringComparer.Ordinal).ToArray();
            return result;
        }

        // completionMilliseconds may include actual deferred completion times. A miss
        // is counted once per unit at first use; subsequent renders still count toward
        // use coverage. No replay result is promoted into a measured hitch count.
        public static PsoHotsetCoverage Replay(PsoHotsetUnit[] units, PsoHotsetDecision decision,
            PsoHotsetTrace heldOut, IDictionary<string, double> completionMilliseconds = null)
        {
            var map = ValidateUnits(units);
            string reason = ValidateTrace(heldOut, map, "held-out", true);
            if (reason != null) throw new ArgumentException("Invalid held-out trace: " + reason);
            if (decision.trainingRouteIds.Contains(heldOut.routeId) || decision.trainingCaptureIds.Contains(heldOut.captureId))
                throw new ArgumentException("Training and held-out routes/captures must be disjoint.");
            var startup = new HashSet<string>(decision.startupUnitIds, StringComparer.Ordinal);
            var used = new HashSet<string>(StringComparer.Ordinal);
            var result = new PsoHotsetCoverage { routeId = heldOut.routeId };
            foreach (var use in heldOut.uses.OrderBy(e => e.milliseconds).ThenBy(e => e.unitId, StringComparer.Ordinal))
            {
                result.useEvents = checked(result.useEvents + use.frequency);
                if (!map.TryGetValue(use.unitId, out var unit))
                {
                    result.unknownUseEvents = checked(result.unknownUseEvents + use.frequency);
                    continue;
                }
                bool covered = startup.Contains(use.unitId);
                if (!covered && completionMilliseconds != null && completionMilliseconds.TryGetValue(use.unitId, out double completed))
                    covered = Finite(completed) && completed >= 0 && completed <= use.milliseconds;
                if (covered) result.coveredUseEvents = checked(result.coveredUseEvents + use.frequency);
                if (used.Add(use.unitId) && !covered)
                {
                    result.missedUnits++;
                    result.missedStateEntries = checked(result.missedStateEntries + unit.graphicsStateCount);
                }
            }
            foreach (string id in startup)
            {
                if (!map.ContainsKey(id)) throw new ArgumentException("Decision contains unknown unit.");
                if (used.Contains(id)) continue;
                result.extraWarmedUnits++;
                result.extraWarmedStateEntries = checked(result.extraWarmedStateEntries + map[id].graphicsStateCount);
            }
            return result;
        }

        private static void Add(PsoHotsetDecision result, List<PsoHotsetUnit> selected, PsoHotsetUnit unit)
        {
            selected.Add(unit);
            if (Finite(unit.estimatedWarmupMilliseconds) && unit.estimatedWarmupMilliseconds >= 0)
            {
                if (result.startupCostKnown) result.estimatedStartupMilliseconds += unit.estimatedWarmupMilliseconds;
            }
            else { result.startupCostKnown = false; result.estimatedStartupMilliseconds = -1; }
            if (unit.residentBytes >= 0)
            {
                if (result.startupMemoryKnown) result.startupResidentBytes = checked(result.startupResidentBytes + unit.residentBytes);
            }
            else { result.startupMemoryKnown = false; result.startupResidentBytes = -1; }
            result.startupStateEntries = checked(result.startupStateEntries + unit.graphicsStateCount);
        }

        private static Dictionary<string, PsoHotsetUnit> ValidateUnits(PsoHotsetUnit[] units)
        {
            if (units == null) throw new ArgumentNullException(nameof(units));
            var map = new Dictionary<string, PsoHotsetUnit>(StringComparer.Ordinal);
            foreach (var unit in units)
            {
                if (unit == null || string.IsNullOrWhiteSpace(unit.id) || unit.graphicsStateCount < 0 || map.ContainsKey(unit.id))
                    throw new ArgumentException("Units require unique IDs and nonnegative state counts.");
                map.Add(unit.id, unit);
            }
            return map;
        }

        private static string ValidateTrace(PsoHotsetTrace trace, Dictionary<string, PsoHotsetUnit> map,
            string split, bool allowUnknown = false)
        {
            if (trace == null || trace.split != split || string.IsNullOrWhiteSpace(trace.routeId) ||
                string.IsNullOrWhiteSpace(trace.captureId) || string.IsNullOrWhiteSpace(trace.compatibilityNamespace) ||
                trace.units == null || trace.uses == null) return "missing-identity-or-wrong-split";
            var identities = new Dictionary<string, PsoHotsetUnit>(StringComparer.Ordinal);
            foreach (var unit in trace.units)
            {
                if (unit == null || string.IsNullOrWhiteSpace(unit.id) || identities.ContainsKey(unit.id)) return "duplicate-or-null-unit";
                if (string.IsNullOrWhiteSpace(unit.contentId) || string.IsNullOrWhiteSpace(unit.contentRevision) ||
                    string.IsNullOrWhiteSpace(unit.collectionSha256) || unit.compatibilityNamespace != trace.compatibilityNamespace)
                    return "unknown-content-identity";
                if (map.TryGetValue(unit.id, out var current))
                {
                    if (!SameIdentity(unit, current)) return "stale-content-or-collection";
                }
                else if (!allowUnknown) return "unknown-unit";
                identities.Add(unit.id, unit);
            }
            foreach (var use in trace.uses)
                if (use == null || string.IsNullOrWhiteSpace(use.unitId) || !identities.ContainsKey(use.unitId) ||
                    !Finite(use.milliseconds) || use.milliseconds < 0 || use.phaseOrdinal < 0 || use.frequency <= 0)
                    return "invalid-use-evidence";
            return null;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
