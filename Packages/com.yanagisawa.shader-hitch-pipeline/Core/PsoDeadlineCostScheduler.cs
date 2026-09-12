using System;
using System.Collections.Generic;

namespace Yanagisawa.ShaderHitchPipeline
{
    public readonly struct PsoSchedulerCandidate
    {
        public PsoSchedulerCandidate(
            string phase,
            int remainingStates,
            int priority,
            int hotSetTier,
            double deadlineMilliseconds,
            double elapsedMilliseconds,
            double estimatedMillisecondsPerState,
            double expectedUseProbability,
            bool admissible = true,
            int waitingFrames = 0,
            double predictedRemainingMilliseconds = -1)
        {
            if (!PsoSchedulingOptions.Finite(deadlineMilliseconds) || !PsoSchedulingOptions.Finite(elapsedMilliseconds) ||
                !PsoSchedulingOptions.Finite(estimatedMillisecondsPerState) || !PsoSchedulingOptions.Finite(expectedUseProbability) ||
                !PsoSchedulingOptions.Finite(predictedRemainingMilliseconds))
                throw new ArgumentOutOfRangeException(nameof(estimatedMillisecondsPerState));
            Phase = phase ?? string.Empty;
            RemainingStates = Math.Max(0, remainingStates);
            Priority = priority;
            HotSetTier = Math.Max(0, hotSetTier);
            DeadlineMilliseconds = Math.Max(0.0, deadlineMilliseconds);
            ElapsedMilliseconds = Math.Max(0.0, elapsedMilliseconds);
            EstimatedMillisecondsPerState = Math.Max(
                0.000001,
                estimatedMillisecondsPerState);
            ExpectedUseProbability = Math.Max(
                0.0,
                Math.Min(1.0, expectedUseProbability));
            Admissible = admissible;
            WaitingFrames = Math.Max(0, waitingFrames);
            PredictedRemainingMilliseconds = predictedRemainingMilliseconds < 0
                ? RemainingStates * EstimatedMillisecondsPerState : predictedRemainingMilliseconds;
        }

        public string Phase { get; }
        public int RemainingStates { get; }
        public int Priority { get; }
        public int HotSetTier { get; }
        public double DeadlineMilliseconds { get; }
        public double ElapsedMilliseconds { get; }
        public double EstimatedMillisecondsPerState { get; }
        public double ExpectedUseProbability { get; }
        public bool Admissible { get; }
        public int WaitingFrames { get; }
        public double PredictedRemainingMilliseconds { get; }

        public double EstimatedRemainingMilliseconds =>
            PredictedRemainingMilliseconds;

        public double SlackMilliseconds => DeadlineMilliseconds <= 0.0
            ? double.PositiveInfinity
            : DeadlineMilliseconds - ElapsedMilliseconds -
              EstimatedRemainingMilliseconds;

        public double ValueDensity => ExpectedUseProbability /
                                      Math.Max(0.000001, EstimatedRemainingMilliseconds);
    }

    public readonly struct PsoSchedulingDecision
    {
        public PsoSchedulingDecision(
            int candidateIndex,
            string phase,
            double slackMilliseconds,
            bool deadlineCritical,
            double valueDensity)
        {
            CandidateIndex = candidateIndex;
            Phase = phase;
            SlackMilliseconds = slackMilliseconds;
            DeadlineCritical = deadlineCritical;
            ValueDensity = valueDensity;
        }

        public int CandidateIndex { get; }
        public string Phase { get; }
        public double SlackMilliseconds { get; }
        public bool DeadlineCritical { get; }
        public double ValueDensity { get; }
        public bool IsValid => CandidateIndex >= 0;
    }

    public static class PsoDeadlineCostScheduler
    {
        public static PsoSchedulingDecision SelectNext(
            IReadOnlyList<PsoSchedulerCandidate> candidates,
            double deadlineRiskWindowMilliseconds,
            PsoSchedulingOptions options = null)
        {
            if (candidates == null)
                throw new ArgumentNullException(nameof(candidates));

            int selected = -1;
            for (int index = 0; index < candidates.Count; index++)
            {
                if (candidates[index].RemainingStates <= 0 || !candidates[index].Admissible)
                    continue;
                if (selected < 0 || IsPreferred(
                        candidates[index],
                        candidates[selected],
                        deadlineRiskWindowMilliseconds, options))
                    selected = index;
            }

            if (selected < 0)
                return new PsoSchedulingDecision(-1, string.Empty, 0.0, false, 0.0);

            PsoSchedulerCandidate candidate = candidates[selected];
            double slack = candidate.SlackMilliseconds;
            return new PsoSchedulingDecision(
                selected,
                candidate.Phase,
                double.IsInfinity(slack) ? 0.0 : slack,
                (options == null || options.EnableDeadlines) && IsDeadlineCritical(candidate, deadlineRiskWindowMilliseconds),
                candidate.ValueDensity);
        }

        private static bool IsPreferred(
            PsoSchedulerCandidate candidate,
            PsoSchedulerCandidate incumbent,
            double riskWindow,
            PsoSchedulingOptions options)
        {
            if (options != null && options.FixedBatchSize > 0 && !options.EnableDeadlines &&
                !options.EnableHotSetPriority && !options.EnableCostPriority) return false;
            if (options != null && options.ConservativeAdmission)
            {
                bool aged = candidate.WaitingFrames >= options.MaximumStarvationFrames;
                bool incumbentAged = incumbent.WaitingFrames >= options.MaximumStarvationFrames;
                if (aged != incumbentAged) return aged;
                if (aged && candidate.WaitingFrames != incumbent.WaitingFrames)
                    return candidate.WaitingFrames > incumbent.WaitingFrames;
            }
            bool deadlines = options == null || options.EnableDeadlines;
            bool candidateCritical = deadlines && IsDeadlineCritical(candidate, riskWindow);
            bool incumbentCritical = deadlines && IsDeadlineCritical(incumbent, riskWindow);
            // Once a deadline has already been missed, preserve the work but do not let
            // its ever more negative slack monopolize every remaining viable deadline.
            if (options != null && options.ConservativeAdmission && deadlines)
            {
                bool missed = candidate.DeadlineMilliseconds > 0 && candidate.ElapsedMilliseconds > candidate.DeadlineMilliseconds;
                bool incumbentMissed = incumbent.DeadlineMilliseconds > 0 && incumbent.ElapsedMilliseconds > incumbent.DeadlineMilliseconds;
                if (missed != incumbentMissed)
                {
                    if (!missed && candidateCritical) return true;
                    if (!incumbentMissed && incumbentCritical) return false;
                }
                candidateCritical &= !missed;
                incumbentCritical &= !incumbentMissed;
            }
            if (candidateCritical != incumbentCritical)
                return candidateCritical;

            if (candidateCritical)
            {
                int slack = candidate.SlackMilliseconds.CompareTo(
                    incumbent.SlackMilliseconds);
                if (slack != 0)
                    return slack < 0;
            }

            int hotSet = candidate.HotSetTier.CompareTo(incumbent.HotSetTier);
            if ((options == null || options.EnableHotSetPriority) && hotSet != 0)
                return hotSet < 0;

            int density = candidate.ValueDensity.CompareTo(incumbent.ValueDensity);
            if ((options == null || options.EnableCostPriority) && density != 0)
                return density > 0;

            int priority = candidate.Priority.CompareTo(incumbent.Priority);
            if (priority != 0)
                return priority < 0;

            int deadline = EffectiveDeadline(candidate).CompareTo(
                EffectiveDeadline(incumbent));
            if (deadlines && deadline != 0)
                return deadline < 0;

            if (options != null && options.FixedBatchSize > 0) return false; // Stable activation order.

            return string.Compare(
                       candidate.Phase,
                       incumbent.Phase,
                       StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static bool IsDeadlineCritical(
            PsoSchedulerCandidate candidate,
            double riskWindow)
        {
            return candidate.DeadlineMilliseconds > 0.0 &&
                   candidate.SlackMilliseconds <= Math.Max(0.0, riskWindow);
        }

        private static double EffectiveDeadline(PsoSchedulerCandidate candidate)
        {
            return candidate.DeadlineMilliseconds <= 0.0
                ? double.PositiveInfinity
                : candidate.DeadlineMilliseconds;
        }
    }
}
