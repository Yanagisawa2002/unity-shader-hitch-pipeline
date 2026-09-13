using System;
using System.Text;

namespace Yanagisawa.ShaderHitchPipeline
{
    public interface IPsoClock
    {
        double NowMilliseconds { get; }
    }

    /// <summary>Runtime switches, deliberately separate from the versioned plan schema.
    /// New policies are opt-in and unmeasured. Counts and predicted budgets are not driver time bounds.</summary>
    public sealed class PsoSchedulingOptions
    {
        public bool ConservativeAdmission { get; set; }
        public bool EnableDeadlines { get; set; } = true;
        public bool EnableAdaptiveCost { get; set; } = true;
        public bool EnableHotSetPriority { get; set; } = true;
        public bool EnableCostPriority { get; set; } = true;
        public int FixedBatchSize { get; set; }
        public int MaximumStarvationFrames { get; set; } = 120;
        public double MaximumCostAgeMilliseconds { get; set; } = 30000;
        public int MaximumNoProgressBatches { get; set; } = 2;
        public string PolicyId => FixedBatchSize > 0 ? "fixed-progressive" :
            ConservativeAdmission ? "observed-budget" : "scheduled";

        public static PsoSchedulingOptions ObservedBudget() => new PsoSchedulingOptions { ConservativeAdmission = true };
        public static PsoSchedulingOptions FixedProgressive(int batchSize) => new PsoSchedulingOptions
        {
            FixedBatchSize = batchSize > 0 ? batchSize : throw new ArgumentOutOfRangeException(nameof(batchSize)),
            EnableDeadlines = false, EnableAdaptiveCost = false,
            EnableHotSetPriority = false, EnableCostPriority = false
        };

        internal PsoSchedulingOptions Snapshot()
        {
            if (FixedBatchSize < 0 || MaximumStarvationFrames < 1 || MaximumNoProgressBatches < 1 ||
                !Finite(MaximumCostAgeMilliseconds) || MaximumCostAgeMilliseconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(PsoSchedulingOptions));
            return (PsoSchedulingOptions)MemberwiseClone();
        }
        public PsoSchedulingOptions Copy() => Snapshot();

        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        /// <summary>The caller supplies the current attested environment at load and at environment changes.
        /// This is an invalidation key, not a replacement for collection compatibility validation.</summary>
        public static string CostIdentity(PsoEnvironmentSnapshot environment, string planHash)
        {
            if (environment == null) throw new ArgumentNullException(nameof(environment));
            var key = new StringBuilder();
            foreach (string value in new[] { planHash, environment.engineVersion, environment.unityVersion,
                environment.runtimePlatform, environment.graphicsDeviceType, environment.graphicsDeviceName,
                environment.graphicsDeviceVersion, environment.qualityLevelName, environment.driverIdentity,
                environment.driverIdentitySource, environment.driverVersion, environment.operatingSystem,
                environment.graphicsDeviceVendor,
                environment.graphicsDeviceId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                environment.graphicsDeviceVendorId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                environment.processorType, environment.processorCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                environment.renderingThreadingMode, environment.costExecutionContext,
                environment.identity?.buildInputSha256, environment.identity?.shaderSha256,
                environment.identity?.contentSha256, environment.identity?.contentId, environment.identity?.contentRevision })
            {
                string part = value ?? string.Empty;
                key.Append(part.Length).Append(':').Append(part);
            }
            return key.ToString();
        }
    }
}
