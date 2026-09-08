using System;

namespace Yanagisawa.ShaderHitchPipeline
{
    public static class PsoCollectionReadiness
    {
        public static void RequireFullCollection(string phase, int expectedGraphicsStates, int resolvedGraphicsStates)
        {
            if (expectedGraphicsStates < 0 || resolvedGraphicsStates != expectedGraphicsStates)
                throw new InvalidOperationException("Phase '" + phase + "' resolved " + resolvedGraphicsStates +
                    " graphics states, expected " + expectedGraphicsStates + ". Load all shader dependencies before activation or tracing.");
        }
    }

    /// <summary>Separate scheduling contract; it does not change the warmup-plan or historical receipt schemas.</summary>
    [Serializable]
    public sealed class PsoSchedulingFeedback
    {
        public int schemaVersion = 1;
        public string planSha256;
        public string policy;
        public string implementation = "scheduler-lifecycle-v1";
        public string performanceClaim = "Unmeasured; no comparative result";
        public bool deadlinesEnabled, adaptiveCostEnabled, hotSetPriorityEnabled, costPriorityEnabled;
        public bool conservativeAdmission;
        public int fixedBatchSize;
        public bool completed, hasInFlightBatch, hasPendingRetirement;
        public string latencyGuarantee = "None. An admitted opaque engine/driver batch cannot be preempted or time-bounded.";
        public PsoPhaseFeedback[] activations = Array.Empty<PsoPhaseFeedback>();

        public static PsoSchedulingFeedback Capture(PsoWarmupScheduler scheduler, PsoSchedulingOptions options, string planSha256)
        {
            if (scheduler == null || options == null) throw new ArgumentNullException(nameof(scheduler));
            var result = new PsoSchedulingFeedback { planSha256 = planSha256, policy = options.PolicyId,
                deadlinesEnabled = options.EnableDeadlines, adaptiveCostEnabled = options.EnableAdaptiveCost,
                hotSetPriorityEnabled = options.EnableHotSetPriority, costPriorityEnabled = options.EnableCostPriority,
                conservativeAdmission = options.ConservativeAdmission, fixedBatchSize = options.FixedBatchSize,
                completed = scheduler.IsComplete, hasInFlightBatch = scheduler.HasInFlightBatch,
                hasPendingRetirement = scheduler.HasPendingRetirement,
                activations = new PsoPhaseFeedback[scheduler.Activations.Count] };
            for (int i = 0; i < result.activations.Length; i++)
            {
                var run = scheduler.Activations[i];
                result.activations[i] = new PsoPhaseFeedback { phase = run.Phase, activation = run.Activation,
                    state = run.State.ToString(), completedPermutations = run.CompletedPermutations,
                    totalGraphicsStates = run.TotalGraphicsStates, backendReportedWarmedUp = run.BackendReportedWarmedUp,
                    hasInFlightBatch = run.HasInFlightBatch, ownerReleased = run.resident.backend == null,
                    elapsedMilliseconds = run.ElapsedMilliseconds, deadlineMissed = run.DeadlineMissed,
                    deadlineFeasible = run.DeadlineFeasible, deferredFrames = run.DeferredFrames,
                    lastAdmissionReason = run.LastAdmissionReason, failure = run.Failure,
                    costGeneration = run.Policy.CostGeneration, costInvalidationReason = run.Policy.CostInvalidationReason,
                    requiresNonInteractiveWindow = run.RequiresNonInteractiveWindow };
            }
            return result;
        }
    }

    [Serializable]
    public sealed class PsoPhaseFeedback
    {
        public string phase, state, failure, lastAdmissionReason, costInvalidationReason;
        public int activation, completedPermutations, totalGraphicsStates, costGeneration, deferredFrames;
        public bool backendReportedWarmedUp, hasInFlightBatch, ownerReleased, deadlineMissed, deadlineFeasible, requiresNonInteractiveWindow;
        public double elapsedMilliseconds;
    }
}
