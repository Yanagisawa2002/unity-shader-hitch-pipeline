using System;

namespace Yanagisawa.ShaderHitchPipeline
{
    public sealed partial class PsoAdaptiveBatchPolicy
    {
        private const int WindowSize = 16;
        private readonly int[] recentSizes = new int[WindowSize];
        private readonly double[] recentDurations = new double[WindowSize];
        private int recentCount, recentCursor;
        private bool recentHasSlope;
        private double recentSlope, recentFixed, lastFrameMilliseconds, explicitWindowBudget;
        private double contextTime, lastCostObservationTime;
        private string costIdentity;
        public bool ConservativeAdmission { get; }
        public bool RequiresNonInteractiveWindow { get; private set; }
        public int CostGeneration { get; private set; }
        public string CostInvalidationReason { get; private set; } = string.Empty;

        /// <summary>Expire observations without clearing completed work or recorded budget violations.
        /// An in-flight operation must not train a different generation's cost model.</summary>
        public void UpdateCostContext(string identity, double nowMilliseconds, double maximumAgeMilliseconds)
        {
            if (!PsoSchedulingOptions.Finite(nowMilliseconds) || nowMilliseconds < contextTime ||
                !PsoSchedulingOptions.Finite(maximumAgeMilliseconds) || maximumAgeMilliseconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(nowMilliseconds));
            identity = identity ?? string.Empty;
            bool changed = costIdentity != null && !string.Equals(identity, costIdentity, StringComparison.Ordinal);
            bool expired = batchObservationCount > 0 && nowMilliseconds - lastCostObservationTime >= maximumAgeMilliseconds;
            contextTime = nowMilliseconds;
            costIdentity = identity;
            if (!changed && !expired) return;
            CostGeneration++;
            CostInvalidationReason = changed ? "cost-environment-changed" : "cost-observations-expired";
            batchObservationCount = steadyObservationCount = recentCount = recentCursor = 0;
            regressionCount = regressionSumStates = regressionSumDuration = regressionSumStateSquares = regressionSumStateDuration = 0;
            fixedBatchMilliseconds = residualEwmaMilliseconds = maximumResidualMilliseconds = recentSlope = recentFixed = 0;
            // An imported scalar prior has the same stale identity/age as its fitted observations.
            millisecondsPerState = priorMillisecondsPerState = 0.25;
            recentHasSlope = false;
            HasMeasuredCostSlope = false;
            CurrentBatchSize = bootstrapBatchSize;
            dynamicMaximum = bootstrapBatchSize;
            // A previous minimum-batch overrun remains a hazard even when its cost estimate expires.
        }

        private void ObserveConservativeBatch(int size, double duration)
        {
            lastCostObservationTime = contextTime;
            recentSizes[recentCursor] = size;
            recentDurations[recentCursor] = duration;
            recentCursor = (recentCursor + 1) % WindowSize;
            recentCount = Math.Min(WindowSize, recentCount + 1);
            if (size <= minimum && duration > targetFrameMilliseconds - safetyMarginMilliseconds)
                RequiresNonInteractiveWindow = true;

            double sumX = 0, sumY = 0, sumXX = 0, sumXY = 0;
            for (int i = 0; i < recentCount; i++)
            {
                double x = recentSizes[i], y = recentDurations[i];
                sumX += x; sumY += y; sumXX += x * x; sumXY += x * y;
            }
            double denominator = recentCount * sumXX - sumX * sumX;
            recentHasSlope = denominator > 0.000001;
            recentSlope = recentHasSlope
                ? Math.Max(0.000001, (recentCount * sumXY - sumX * sumY) / denominator)
                : Math.Max(0.000001, priorMillisecondsPerState);
            recentFixed = Math.Max(0, (sumY - recentSlope * sumX) / recentCount);
        }

        private double PredictConservativeBatch(int size)
        {
            double slope = recentCount == 0 ? millisecondsPerState : recentSlope;
            double fixedCost = recentCount == 0 ? 0 : recentFixed;
            double positiveResidual = 0;
            for (int i = 0; i < recentCount; i++)
                positiveResidual = Math.Max(positiveResidual,
                    recentDurations[i] - (fixedCost + slope * recentSizes[i]));
            // The fitted line plus its largest underprediction envelopes the retained samples.
            // This is a conservative admission estimate, never a confidence or driver latency bound.
            double prediction = (fixedCost + slope * size + Math.Max(0, positiveResidual)) * costSafetyMultiplier;
            return PsoSchedulingOptions.Finite(prediction) ? prediction : double.MaxValue;
        }

        public PsoBudgetAdmission Preview(int remaining, double deadline, bool hotSet,
            int fixedBatchSize = 0, double nonInteractiveBudgetMilliseconds = 0)
        {
            if (!PsoSchedulingOptions.Finite(nonInteractiveBudgetMilliseconds) || nonInteractiveBudgetMilliseconds < 0)
                throw new ArgumentOutOfRangeException(nameof(nonInteractiveBudgetMilliseconds));
            int savedSize = CurrentBatchSize, savedDeferred = deferredRecommendationCount;
            var savedAdmission = LastAdmission;
            explicitWindowBudget = nonInteractiveBudgetMilliseconds;
            try
            {
                var admission = Evaluate(remaining, deadline, hotSet);
                if (fixedBatchSize <= 0 || remaining <= 0) return admission;
                int count = Math.Min(remaining, Math.Min(maximum, fixedBatchSize));
                double predicted = PredictBatchMilliseconds(count);
                // A fixed policy has no bootstrap search or adaptive batch growth. It still refuses
                // known frame pressure; its constant count can be independently held in a future run.
                bool blocked = explicitWindowBudget <= 0 && (cooldownFramesRemaining > 0 || RequiresNonInteractiveWindow);
                bool fits = !blocked && predicted <= admission.AvailableBudgetMilliseconds;
                return new PsoBudgetAdmission(fits ? count : 0, fits ? count : 0,
                    admission.DeadlineBatchSize, predicted, admission.AvailableBudgetMilliseconds,
                    fits && admission.DeadlineBatchSize <= count, false,
                    fits ? "fixed-progressive" : "fixed-batch-exceeds-budget");
            }
            finally
            {
                explicitWindowBudget = 0;
                CurrentBatchSize = savedSize; deferredRecommendationCount = savedDeferred; LastAdmission = savedAdmission;
            }
        }

        public void CommitAdmission(PsoBudgetAdmission admission)
        {
            LastAdmission = admission;
            if (admission.IsAdmitted) CurrentBatchSize = admission.BatchSize;
            else deferredRecommendationCount++;
        }
    }
}
