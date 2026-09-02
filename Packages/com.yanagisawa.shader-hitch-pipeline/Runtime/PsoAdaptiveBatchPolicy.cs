using System;

namespace Yanagisawa.ShaderHitchPipeline
{
    public sealed class PsoAdaptiveBatchPolicy
    {
        private readonly int minimum;
        private readonly int maximum;
        private readonly double targetFrameMilliseconds;
        private double frameEwma;
        private double millisecondsPerState;
        private bool hasObservation;
        private bool hasBatchObservation;

        public PsoAdaptiveBatchPolicy(
            int initial,
            int minimum,
            int maximum,
            double targetFrameMilliseconds,
            double estimatedMillisecondsPerState = 0.25)
        {
            if (minimum < 1)
                throw new ArgumentOutOfRangeException(nameof(minimum));
            if (maximum < minimum)
                throw new ArgumentOutOfRangeException(nameof(maximum));
            if (targetFrameMilliseconds <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(targetFrameMilliseconds));
            if (estimatedMillisecondsPerState <= 0.0 ||
                double.IsNaN(estimatedMillisecondsPerState))
                throw new ArgumentOutOfRangeException(nameof(estimatedMillisecondsPerState));

            this.minimum = minimum;
            this.maximum = maximum;
            this.targetFrameMilliseconds = targetFrameMilliseconds;
            millisecondsPerState = estimatedMillisecondsPerState;
            CurrentBatchSize = Math.Max(minimum, Math.Min(maximum, initial));
        }

        public int CurrentBatchSize { get; private set; }
        public double FrameEwmaMilliseconds => frameEwma;
        public double EstimatedMillisecondsPerState => millisecondsPerState;
        public double TargetFrameMilliseconds => targetFrameMilliseconds;

        public int Observe(double frameMilliseconds)
        {
            if (frameMilliseconds <= 0.0 || double.IsNaN(frameMilliseconds))
                return CurrentBatchSize;

            frameEwma = hasObservation
                ? (frameEwma * 0.80) + (frameMilliseconds * 0.20)
                : frameMilliseconds;
            hasObservation = true;

            if (frameEwma > targetFrameMilliseconds * 1.05)
            {
                CurrentBatchSize = Math.Max(minimum, CurrentBatchSize / 2);
            }
            else if (frameEwma < targetFrameMilliseconds * 0.80)
            {
                int increment = Math.Max(1, CurrentBatchSize / 4);
                CurrentBatchSize = Math.Min(maximum, CurrentBatchSize + increment);
            }

            return CurrentBatchSize;
        }

        public double ObserveBatch(int completedStates, double elapsedMilliseconds)
        {
            if (completedStates <= 0 || elapsedMilliseconds <= 0.0 ||
                double.IsNaN(elapsedMilliseconds))
                return millisecondsPerState;

            double sample = elapsedMilliseconds / completedStates;
            millisecondsPerState = hasBatchObservation
                ? (millisecondsPerState * 0.75) + (sample * 0.25)
                : sample;
            hasBatchObservation = true;
            return millisecondsPerState;
        }

        public int RecommendBatchSize(
            int remainingStates,
            double deadlineRemainingMilliseconds,
            bool hotSet)
        {
            if (remainingStates <= 0)
                return 0;

            int recommendation = CurrentBatchSize;
            if (!double.IsInfinity(deadlineRemainingMilliseconds))
            {
                int remainingFrames = deadlineRemainingMilliseconds <= 0.0
                    ? 1
                    : Math.Max(
                        1,
                        (int)Math.Floor(
                            deadlineRemainingMilliseconds / targetFrameMilliseconds));
                int deadlineBatch = (int)Math.Ceiling(
                    remainingStates / (double)remainingFrames);
                recommendation = Math.Max(recommendation, deadlineBatch);
            }

            if (hotSet && (!hasObservation || frameEwma < targetFrameMilliseconds * 0.90))
                recommendation = Math.Max(recommendation, CurrentBatchSize + 1);

            CurrentBatchSize = Math.Max(
                minimum,
                Math.Min(maximum, Math.Min(remainingStates, recommendation)));
            return CurrentBatchSize;
        }
    }
}
