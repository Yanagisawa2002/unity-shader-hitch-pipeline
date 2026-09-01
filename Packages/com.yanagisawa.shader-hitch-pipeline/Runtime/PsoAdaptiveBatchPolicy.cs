using System;

namespace Yanagisawa.ShaderHitchPipeline
{
    public sealed class PsoAdaptiveBatchPolicy
    {
        private readonly int minimum;
        private readonly int maximum;
        private readonly double targetFrameMilliseconds;
        private double frameEwma;
        private bool hasObservation;

        public PsoAdaptiveBatchPolicy(
            int initial,
            int minimum,
            int maximum,
            double targetFrameMilliseconds)
        {
            if (minimum < 1)
                throw new ArgumentOutOfRangeException(nameof(minimum));
            if (maximum < minimum)
                throw new ArgumentOutOfRangeException(nameof(maximum));
            if (targetFrameMilliseconds <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(targetFrameMilliseconds));

            this.minimum = minimum;
            this.maximum = maximum;
            this.targetFrameMilliseconds = targetFrameMilliseconds;
            CurrentBatchSize = Math.Max(minimum, Math.Min(maximum, initial));
        }

        public int CurrentBatchSize { get; private set; }
        public double FrameEwmaMilliseconds => frameEwma;

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
    }
}
