using System;

namespace Yanagisawa.ShaderHitchPipeline
{
    public readonly struct PsoBudgetAdmission
    {
        public PsoBudgetAdmission(
            int batchSize,
            int safeBatchSize,
            int deadlineBatchSize,
            double predictedBatchMilliseconds,
            double availableBudgetMilliseconds,
            bool deadlineFeasible,
            bool calibration,
            string reason)
        {
            BatchSize = Math.Max(0, batchSize);
            SafeBatchSize = Math.Max(0, safeBatchSize);
            DeadlineBatchSize = Math.Max(0, deadlineBatchSize);
            PredictedBatchMilliseconds = Math.Max(0.0, predictedBatchMilliseconds);
            AvailableBudgetMilliseconds = Math.Max(0.0, availableBudgetMilliseconds);
            DeadlineFeasible = deadlineFeasible;
            Calibration = calibration;
            Reason = reason ?? string.Empty;
        }

        public int BatchSize { get; }
        public int SafeBatchSize { get; }
        public int DeadlineBatchSize { get; }
        public double PredictedBatchMilliseconds { get; }
        public double AvailableBudgetMilliseconds { get; }
        public bool DeadlineFeasible { get; }
        public bool Calibration { get; }
        public string Reason { get; }
        public bool IsAdmitted => BatchSize > 0;
    }

    /// <summary>
    /// Strict admission control for an opaque engine warmup backend.
    ///
    /// The policy never enlarges a batch beyond its conservative frame-headroom
    /// estimate, even to recover a threatened deadline. The first cold batch and
    /// first post-cold calibration batch are deliberately limited to the bootstrap
    /// size. An observed budget violation trips a cooldown and reduces the dynamic
    /// ceiling. This bounds scheduler-created pressure; an engine or driver can
    /// still exceed the estimate inside an already admitted opaque job.
    /// </summary>
    public sealed partial class PsoAdaptiveBatchPolicy
    {
        private readonly int minimum;
        private readonly int maximum;
        private readonly int bootstrapBatchSize;
        private readonly int cooldownFrames;
        private readonly double targetFrameMilliseconds;
        private readonly double safetyMarginMilliseconds;
        private readonly double costSafetyMultiplier;
        private double priorMillisecondsPerState;

        private double frameEwma;
        private double millisecondsPerState;
        private double fixedBatchMilliseconds;
        private double residualEwmaMilliseconds;
        private double maximumResidualMilliseconds;
        private double coldStartBatchMilliseconds;
        private bool hasFrameObservation;
        private int batchObservationCount;
        private int steadyObservationCount;
        private int cooldownFramesRemaining;
        private int dynamicMaximum;
        private int budgetViolationCount;
        private int minimumBatchViolationCount;
        private int circuitBreakerTripCount;
        private int deferredRecommendationCount;
        private double maximumObservedFrameMilliseconds;
        private double maximumBudgetOverrunMilliseconds;

        private double regressionCount;
        private double regressionSumStates;
        private double regressionSumDuration;
        private double regressionSumStateSquares;
        private double regressionSumStateDuration;

        public PsoAdaptiveBatchPolicy(
            int initial,
            int minimum,
            int maximum,
            double targetFrameMilliseconds,
            double estimatedMillisecondsPerState = 0.25,
            int bootstrapBatchSize = 1,
            double safetyMarginMilliseconds = 2.0,
            double costSafetyMultiplier = 1.5,
            int cooldownFrames = 8,
            bool conservativeAdmission = false)
        {
            if (minimum < 1)
                throw new ArgumentOutOfRangeException(nameof(minimum));
            if (maximum < minimum)
                throw new ArgumentOutOfRangeException(nameof(maximum));
            if (targetFrameMilliseconds <= 0.0 || !PsoSchedulingOptions.Finite(targetFrameMilliseconds))
                throw new ArgumentOutOfRangeException(nameof(targetFrameMilliseconds));
            if (estimatedMillisecondsPerState <= 0.0 ||
                !PsoSchedulingOptions.Finite(estimatedMillisecondsPerState))
                throw new ArgumentOutOfRangeException(nameof(estimatedMillisecondsPerState));
            if (bootstrapBatchSize < 1)
                throw new ArgumentOutOfRangeException(nameof(bootstrapBatchSize));
            if (safetyMarginMilliseconds < 0.0 ||
                safetyMarginMilliseconds >= targetFrameMilliseconds || !PsoSchedulingOptions.Finite(safetyMarginMilliseconds))
                throw new ArgumentOutOfRangeException(nameof(safetyMarginMilliseconds));
            if (costSafetyMultiplier < 1.0 || !PsoSchedulingOptions.Finite(costSafetyMultiplier))
                throw new ArgumentOutOfRangeException(nameof(costSafetyMultiplier));
            if (cooldownFrames < 0)
                throw new ArgumentOutOfRangeException(nameof(cooldownFrames));

            this.minimum = minimum;
            ConservativeAdmission = conservativeAdmission;
            this.maximum = maximum;
            this.bootstrapBatchSize = Math.Max(
                minimum,
                Math.Min(maximum, bootstrapBatchSize));
            this.cooldownFrames = cooldownFrames;
            this.targetFrameMilliseconds = targetFrameMilliseconds;
            this.safetyMarginMilliseconds = safetyMarginMilliseconds;
            this.costSafetyMultiplier = costSafetyMultiplier;
            priorMillisecondsPerState = estimatedMillisecondsPerState;
            millisecondsPerState = estimatedMillisecondsPerState;
            dynamicMaximum = maximum;
            CurrentBatchSize = Math.Max(minimum, Math.Min(maximum, initial));
            LastAdmission = new PsoBudgetAdmission(
                0, 0, 0, 0.0, targetFrameMilliseconds, true, false, "not-evaluated");
        }

        public int CurrentBatchSize { get; private set; }
        public double FrameEwmaMilliseconds => frameEwma;
        public double EstimatedMillisecondsPerState => ConservativeAdmission && recentCount > 0 ? recentSlope : millisecondsPerState;
        public bool HasMeasuredCostSlope { get; private set; }
        public double EstimatedFixedBatchMilliseconds => ConservativeAdmission && recentCount > 0 ? recentFixed : fixedBatchMilliseconds;
        public double TargetFrameMilliseconds => targetFrameMilliseconds;
        public double SafetyMarginMilliseconds => safetyMarginMilliseconds;
        public double CostSafetyMultiplier => costSafetyMultiplier;
        public int BootstrapBatchSize => bootstrapBatchSize;
        public int CooldownFramesRemaining => cooldownFramesRemaining;
        public int DynamicMaximumBatchSize => dynamicMaximum;
        public int BudgetViolationCount => budgetViolationCount;
        public int MinimumBatchViolationCount => minimumBatchViolationCount;
        public int CircuitBreakerTripCount => circuitBreakerTripCount;
        public int DeferredRecommendationCount => deferredRecommendationCount;
        public int BatchObservationCount => batchObservationCount;
        public double ColdStartBatchMilliseconds => coldStartBatchMilliseconds;
        public double MaximumObservedFrameMilliseconds => maximumObservedFrameMilliseconds;
        public double MaximumBudgetOverrunMilliseconds => maximumBudgetOverrunMilliseconds;
        public bool IsBudgetFeasible => minimumBatchViolationCount == 0;
        public PsoBudgetAdmission LastAdmission { get; private set; }

        public int Observe(double frameMilliseconds)
        {
            return ObserveFrame(frameMilliseconds, true, CurrentBatchSize);
        }

        public int ObserveFrame(
            double frameMilliseconds,
            bool warmupActive,
            int activeBatchSize)
        {
            if (frameMilliseconds <= 0.0 || !PsoSchedulingOptions.Finite(frameMilliseconds))
                return CurrentBatchSize;

            lastFrameMilliseconds = frameMilliseconds;

            frameEwma = hasFrameObservation
                ? (frameEwma * 0.80) + (frameMilliseconds * 0.20)
                : frameMilliseconds;
            hasFrameObservation = true;
            maximumObservedFrameMilliseconds = Math.Max(
                maximumObservedFrameMilliseconds,
                frameMilliseconds);

            if (warmupActive && frameMilliseconds > targetFrameMilliseconds)
            {
                budgetViolationCount++;
                maximumBudgetOverrunMilliseconds = Math.Max(
                    maximumBudgetOverrunMilliseconds,
                    frameMilliseconds - targetFrameMilliseconds);
                circuitBreakerTripCount++;
                cooldownFramesRemaining = Math.Max(
                    cooldownFramesRemaining,
                    cooldownFrames);
                int violatedBatch = Math.Max(minimum, activeBatchSize);
                if (violatedBatch <= minimum)
                {
                    minimumBatchViolationCount++;
                    if (ConservativeAdmission) RequiresNonInteractiveWindow = true;
                }
                dynamicMaximum = Math.Max(
                    minimum,
                    Math.Min(dynamicMaximum, violatedBatch / 2));
                CurrentBatchSize = Math.Max(
                    minimum,
                    Math.Min(CurrentBatchSize / 2, dynamicMaximum));
                return CurrentBatchSize;
            }

            if (cooldownFramesRemaining > 0)
            {
                cooldownFramesRemaining--;
                return CurrentBatchSize;
            }

            if (frameEwma > targetFrameMilliseconds * 0.95)
            {
                CurrentBatchSize = Math.Max(minimum, CurrentBatchSize / 2);
            }
            else if (frameEwma < targetFrameMilliseconds * 0.70)
            {
                if (dynamicMaximum < maximum)
                {
                    dynamicMaximum = Math.Min(
                        maximum,
                        dynamicMaximum + Math.Max(1, dynamicMaximum / 8));
                }
                int increment = Math.Max(1, CurrentBatchSize / 4);
                CurrentBatchSize = Math.Min(
                    dynamicMaximum,
                    CurrentBatchSize + increment);
            }

            return CurrentBatchSize;
        }

        public double ObserveBatch(int completedStates, double elapsedMilliseconds)
        {
            if (completedStates <= 0 || elapsedMilliseconds <= 0.0 ||
                !PsoSchedulingOptions.Finite(elapsedMilliseconds))
                return millisecondsPerState;

            if (ConservativeAdmission) ObserveConservativeBatch(completedStates, elapsedMilliseconds);
            lastCostObservationTime = contextTime;

            batchObservationCount++;
            if (batchObservationCount == 1)
            {
                coldStartBatchMilliseconds = elapsedMilliseconds;
                return millisecondsPerState;
            }

            steadyObservationCount++;
            regressionCount += 1.0;
            regressionSumStates += completedStates;
            regressionSumDuration += elapsedMilliseconds;
            regressionSumStateSquares += completedStates * (double)completedStates;
            regressionSumStateDuration += completedStates * elapsedMilliseconds;

            if (steadyObservationCount == 1)
            {
                millisecondsPerState = priorMillisecondsPerState;
                fixedBatchMilliseconds = Math.Max(
                    0.0,
                    elapsedMilliseconds -
                    (priorMillisecondsPerState * completedStates));
            }
            else
            {
                double denominator =
                    (regressionCount * regressionSumStateSquares) -
                    (regressionSumStates * regressionSumStates);
                if (Math.Abs(denominator) > 0.000001)
                {
                    double slope =
                        ((regressionCount * regressionSumStateDuration) -
                         (regressionSumStates * regressionSumDuration)) /
                        denominator;
                    millisecondsPerState = Math.Max(0.000001, slope);
                    HasMeasuredCostSlope = true;
                    fixedBatchMilliseconds = Math.Max(
                        0.0,
                        (regressionSumDuration -
                         (millisecondsPerState * regressionSumStates)) /
                        regressionCount);
                }
            }

            double predicted = fixedBatchMilliseconds +
                               (millisecondsPerState * completedStates);
            double residual = Math.Abs(elapsedMilliseconds - predicted);
            residualEwmaMilliseconds = steadyObservationCount > 1
                ? (residualEwmaMilliseconds * 0.75) + (residual * 0.25)
                : residual;
            maximumResidualMilliseconds = Math.Max(
                maximumResidualMilliseconds,
                residual);
            if (ConservativeAdmission) HasMeasuredCostSlope = recentHasSlope;
            return millisecondsPerState;
        }

        public PsoBudgetAdmission Evaluate(
            int remainingStates,
            double deadlineRemainingMilliseconds,
            bool hotSet)
        {
            if (remainingStates <= 0)
            {
                LastAdmission = new PsoBudgetAdmission(
                    0, 0, 0, 0.0, 0.0, true, false, "complete");
                return LastAdmission;
            }

            if (double.IsNaN(deadlineRemainingMilliseconds))
                throw new ArgumentOutOfRangeException(nameof(deadlineRemainingMilliseconds));
            double baseline = hasFrameObservation
                ? (ConservativeAdmission ? Math.Max(frameEwma, lastFrameMilliseconds) : frameEwma) : 0.0;
            double available = Math.Max(
                0.0,
                targetFrameMilliseconds - safetyMarginMilliseconds - baseline);
            if (explicitWindowBudget > 0) available = explicitWindowBudget;
            int deadlineBatch = CalculateDeadlineBatch(
                remainingStates,
                deadlineRemainingMilliseconds);

            if (explicitWindowBudget <= 0 && (RequiresNonInteractiveWindow || cooldownFramesRemaining > 0 ||
                (hasFrameObservation &&
                 baseline >= targetFrameMilliseconds - safetyMarginMilliseconds)))
            {
                deferredRecommendationCount++;
                LastAdmission = new PsoBudgetAdmission(
                    0,
                    0,
                    deadlineBatch,
                    0.0,
                    available,
                    false,
                    false,
                    RequiresNonInteractiveWindow ? "nonpreemptible-minimum-requires-explicit-window" : cooldownFramesRemaining > 0
                        ? "circuit-breaker-cooldown"
                        : "no-frame-headroom");
                return LastAdmission;
            }

            if (batchObservationCount < 2 && explicitWindowBudget <= 0)
            {
                int calibrationBatch = Math.Min(
                    remainingStates,
                    Math.Min(dynamicMaximum, bootstrapBatchSize));
                double predictedCalibration = PredictBatchMilliseconds(
                    calibrationBatch);
                if (predictedCalibration > available)
                {
                    deferredRecommendationCount++;
                    LastAdmission = new PsoBudgetAdmission(
                        0,
                        0,
                        deadlineBatch,
                        predictedCalibration,
                        available,
                        false,
                        true,
                        "minimum-probe-exceeds-headroom");
                    return LastAdmission;
                }
                LastAdmission = new PsoBudgetAdmission(
                    calibrationBatch,
                    calibrationBatch,
                    deadlineBatch,
                    predictedCalibration,
                    available,
                    deadlineBatch <= calibrationBatch,
                    true,
                    batchObservationCount == 0
                        ? "cold-start-probe"
                        : "steady-state-probe");
                CurrentBatchSize = calibrationBatch;
                return LastAdmission;
            }

            int safeBatch = CalculateSafeBatch(available, remainingStates);
            if (safeBatch < Math.Min(minimum, remainingStates))
            {
                deferredRecommendationCount++;
                LastAdmission = new PsoBudgetAdmission(
                    0,
                    0,
                    deadlineBatch,
                    0.0,
                    available,
                    false,
                    false,
                    "predicted-cost-exceeds-headroom");
                return LastAdmission;
            }

            int adaptive = Math.Max(
                minimum,
                Math.Min(safeBatch, Math.Min(dynamicMaximum, CurrentBatchSize)));
            int recommendation = Math.Max(
                adaptive,
                Math.Min(deadlineBatch, safeBatch));
            if (hotSet && recommendation < safeBatch)
                recommendation++;
            recommendation = Math.Min(remainingStates,
                Math.Min(safeBatch, Math.Max(minimum, recommendation)));

            CurrentBatchSize = recommendation;
            LastAdmission = new PsoBudgetAdmission(
                recommendation,
                safeBatch,
                deadlineBatch,
                PredictBatchMilliseconds(recommendation),
                available,
                deadlineBatch <= safeBatch,
                false,
                deadlineBatch <= safeBatch
                    ? "admitted"
                    : "deadline-infeasible-within-budget");
            return LastAdmission;
        }

        public int RecommendBatchSize(
            int remainingStates,
            double deadlineRemainingMilliseconds,
            bool hotSet)
        {
            return Evaluate(
                remainingStates,
                deadlineRemainingMilliseconds,
                hotSet).BatchSize;
        }

        public double PredictBatchMilliseconds(int batchSize)
        {
            if (batchSize <= 0)
                return 0.0;

            if (ConservativeAdmission)
                return PredictConservativeBatch(batchSize);
            double residual = Math.Max(
                maximumResidualMilliseconds,
                residualEwmaMilliseconds * 2.0);
            double prediction = Math.Max(
                0.0,
                (fixedBatchMilliseconds +
                 (millisecondsPerState * batchSize) +
                 residual) * costSafetyMultiplier);
            return PsoSchedulingOptions.Finite(prediction) ? prediction : double.MaxValue;
        }

        private int CalculateSafeBatch(double available, int remainingStates)
        {
            if (available <= 0.0)
                return 0;

            int upper = Math.Min(
                remainingStates,
                Math.Min(dynamicMaximum, maximum));
            int lower = 0;
            while (lower < upper)
            {
                int candidate = lower + ((upper - lower + 1) / 2);
                if (PredictBatchMilliseconds(candidate) <= available)
                    lower = candidate;
                else
                    upper = candidate - 1;
            }
            return lower;
        }

        private int CalculateDeadlineBatch(
            int remainingStates,
            double deadlineRemainingMilliseconds)
        {
            if (double.IsPositiveInfinity(deadlineRemainingMilliseconds))
                return Math.Min(minimum, remainingStates);

            int remainingFrames = deadlineRemainingMilliseconds <= 0.0
                ? 1
                : Math.Max(
                    1,
                        (int)Math.Min(int.MaxValue, Math.Floor(
                            deadlineRemainingMilliseconds / targetFrameMilliseconds)));
            return Math.Min(remainingStates, Math.Max(
                minimum,
                (int)Math.Ceiling(remainingStates / (double)remainingFrames)));
        }
    }
}
