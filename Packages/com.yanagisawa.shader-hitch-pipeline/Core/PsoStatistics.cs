using System;
using System.Collections.Generic;

namespace Yanagisawa.ShaderHitchPipeline
{
    public static class PsoStatistics
    {
        public static PsoFrameStatistics Calculate(
            IReadOnlyList<double> samples,
            double hitchThresholdMilliseconds)
        {
            if (samples == null || samples.Count == 0)
            {
                return new PsoFrameStatistics
                {
                    hitchThresholdMilliseconds = hitchThresholdMilliseconds,
                };
            }

            var sorted = new double[samples.Count];
            double sum = 0.0;
            double minimum = double.PositiveInfinity;
            double maximum = double.NegativeInfinity;
            int hitchCount = 0;
            for (int index = 0; index < samples.Count; index++)
            {
                double value = samples[index];
                sorted[index] = value;
                sum += value;
                minimum = Math.Min(minimum, value);
                maximum = Math.Max(maximum, value);
                if (value >= hitchThresholdMilliseconds)
                    hitchCount++;
            }

            Array.Sort(sorted);
            double mean = sum / sorted.Length;
            double variance = 0.0;
            for (int index = 0; index < sorted.Length; index++)
            {
                double delta = sorted[index] - mean;
                variance += delta * delta;
            }

            return new PsoFrameStatistics
            {
                sampleCount = sorted.Length,
                minimumMilliseconds = minimum,
                meanMilliseconds = mean,
                p50Milliseconds = Percentile(sorted, 0.50),
                p95Milliseconds = Percentile(sorted, 0.95),
                p99Milliseconds = Percentile(sorted, 0.99),
                maximumMilliseconds = maximum,
                standardDeviationMilliseconds = Math.Sqrt(variance / sorted.Length),
                hitchThresholdMilliseconds = hitchThresholdMilliseconds,
                hitchFrameCount = hitchCount,
                hitchFramePercent = 100.0 * hitchCount / sorted.Length,
            };
        }

        public static double Percentile(double[] sortedValues, double percentile)
        {
            if (sortedValues == null || sortedValues.Length == 0)
                return 0.0;
            if (sortedValues.Length == 1)
                return sortedValues[0];

            percentile = Math.Max(0.0, Math.Min(1.0, percentile));
            double position = percentile * (sortedValues.Length - 1);
            int lower = (int)Math.Floor(position);
            int upper = (int)Math.Ceiling(position);
            if (lower == upper)
                return sortedValues[lower];

            double fraction = position - lower;
            return sortedValues[lower] +
                   ((sortedValues[upper] - sortedValues[lower]) * fraction);
        }
    }
}
