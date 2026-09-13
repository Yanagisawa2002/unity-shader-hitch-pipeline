using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Yanagisawa.ShaderHitchPipeline
{
    [Serializable]
    public sealed class PsoContentIdentity
    {
        public int version;
        public string source;
        public string buildInputSha256;
        public string shaderSha256;
        public string contentSha256;
        public string contentId;
        public string contentRevision;
    }

    [Serializable]
    public sealed class PsoCompatibilityContract
    {
        public int version;
        public PsoEnvironmentSnapshot collectionEnvironment;
        // Null means no measured cost cache. Configured estimates are only priors.
        public PsoEnvironmentSnapshot costEnvironment;
        public string costModelVersion;
    }

    [Serializable]
    public sealed class PsoCompatibilityResult
    {
        public bool collectionCompatible;
        public bool reuseCostModel;
        public bool legacy;
        public string action;
        public string[] collectionReasons = Array.Empty<string>();
        public string[] costReasons = Array.Empty<string>();
    }

    public static class PsoCompatibility
    {
        public const int Version = 1;
        public const string CostModelVersion = "adaptive-online-v1";
        public const string DriverIdentitySource = "windows-display-registry-loaded-driverstore-sha256-v1";
        public const string BuildIdentitySource = "unity-build-inputs-v1";

        public static PsoCompatibilityResult Evaluate(
            PsoCompatibilityContract contract, PsoEnvironmentSnapshot current)
        {
            if (contract == null)
                return new PsoCompatibilityResult { legacy = true, action = "legacy-platform-api-quality-validation" };
            var collection = new List<string>();
            var cost = new List<string>();
            if (contract.version != Version)
                collection.Add("contract.version: unsupported or missing; retrace with the current adapter.");
            CompareCollection(contract.collectionEnvironment, current, collection);
            if (contract.costModelVersion != CostModelVersion)
                cost.Add("costModelVersion: missing or changed; recalibrate.");
            if (contract.costEnvironment == null)
                cost.Add("costEnvironment: no measured cache; bootstrap and fit online.");
            else
            {
                CompareCollection(contract.costEnvironment, current, cost);
                if (current != null)
                {
                    Match("costExecutionContext", contract.costEnvironment.costExecutionContext, current.costExecutionContext, cost);
                    Match("graphicsApiVersion", contract.costEnvironment.graphicsDeviceVersion, current.graphicsDeviceVersion, cost);
                    Match("driverIdentity", contract.costEnvironment.driverIdentity, current.driverIdentity, cost);
                    Match("driverIdentitySource", contract.costEnvironment.driverIdentitySource, current.driverIdentitySource, cost);
                    if (contract.costEnvironment.driverIdentitySource != DriverIdentitySource || current.driverIdentitySource != DriverIdentitySource ||
                        !IsSha256(contract.costEnvironment.driverIdentity) || !IsSha256(current.driverIdentity))
                        cost.Add("driverIdentity: unsupported source or missing/corrupt loaded-driver digest; recalibrate.");
                    Match("driverVersion", contract.costEnvironment.driverVersion, current.driverVersion, cost);
                    Match("operatingSystem", contract.costEnvironment.operatingSystem, current.operatingSystem, cost);
                    Match("processorType", contract.costEnvironment.processorType, current.processorType, cost);
                    Match("processorCount", contract.costEnvironment.processorCount.ToString(), current.processorCount.ToString(), cost);
                    Match("renderingThreadingMode", contract.costEnvironment.renderingThreadingMode, current.renderingThreadingMode, cost);
                    if (current.processorCount <= 0 || contract.costEnvironment.processorCount <= 0)
                        cost.Add("processorCount: unavailable; recalibrate.");
                }
            }
            return new PsoCompatibilityResult
            {
                collectionCompatible = collection.Count == 0,
                reuseCostModel = collection.Count == 0 && cost.Count == 0,
                action = collection.Count != 0 ? "reject-collection-retrace-or-render-cold" :
                    cost.Count != 0 ? "reuse-collection-bootstrap-recalibrate" : "reuse-collection-and-cost",
                collectionReasons = collection.ToArray(), costReasons = cost.ToArray()
            };
        }

        public static List<string> ValidateIdentity(PsoContentIdentity identity)
        {
            var issues = new List<string>();
            if (identity == null) { issues.Add("identity: missing; current-build compatibility is unknown."); return issues; }
            if (identity.version != Version) issues.Add("identity.version: unsupported or missing.");
            if (identity.source != BuildIdentitySource) issues.Add("identity.source: unsupported or missing capture source.");
            if (!IsSha256(identity.buildInputSha256)) issues.Add("identity.buildInputSha256: missing or malformed.");
            if (!IsSha256(identity.shaderSha256)) issues.Add("identity.shaderSha256: missing or malformed.");
            if (!IsSha256(identity.contentSha256)) issues.Add("identity.contentSha256: missing or malformed.");
            if (!Known(identity.contentId)) issues.Add("identity.contentId: missing or unknown.");
            if (!Known(identity.contentRevision)) issues.Add("identity.contentRevision: missing or unknown.");
            return issues;
        }

        // A grouping key, never proof that a loaded asset is the stated revision.
        public static string CollectionKey(PsoEnvironmentSnapshot environment)
        {
            var issues = new List<string>();
            CompareCollection(environment, environment, issues);
            if (issues.Count != 0) throw new ArgumentException(string.Join("\n", issues));
            PsoContentIdentity id = environment.identity;
            var values = new[] { environment.engineName, environment.engineVersion, environment.unityVersion,
                environment.runtimePlatform, environment.graphicsDeviceType, environment.qualityLevelName,
                environment.graphicsDeviceName, environment.graphicsDeviceVendor,
                environment.graphicsDeviceId.ToString(), environment.graphicsDeviceVendorId.ToString(),
                id.source, id.buildInputSha256.ToLowerInvariant(), id.shaderSha256.ToLowerInvariant(),
                id.contentSha256.ToLowerInvariant(), id.contentId, id.contentRevision };
            var canonical = new StringBuilder();
            foreach (string value in values) canonical.Append(value.Length).Append(':').Append(value);
            using (SHA256 hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString())))
                    .Replace("-", "").ToLowerInvariant();
        }

        public static void ResetCostPriors(PsoWarmupPlanDocument plan, PsoCompatibilityResult result)
        {
            if (result.legacy || result.reuseCostModel || plan.phases == null) return;
            foreach (PsoWarmupPhasePlan phase in plan.phases)
            {
                if (phase == null) continue;
                phase.estimatedMillisecondsPerState = 0.25;
                phase.initialBatchSize = phase.minimumBatchSize;
                phase.bootstrapBatchSize = phase.minimumBatchSize;
            }
        }

        private static void CompareCollection(PsoEnvironmentSnapshot expected, PsoEnvironmentSnapshot actual, List<string> issues)
        {
            if (expected == null || actual == null) { issues.Add("environment: missing; retrace or use cold rendering."); return; }
            issues.AddRange(ValidateIdentity(expected.identity));
            issues.AddRange(ValidateIdentity(actual.identity));
            Match("engineName", expected.engineName, actual.engineName, issues);
            Match("engineVersion", expected.engineVersion, actual.engineVersion, issues);
            Match("unityVersion", expected.unityVersion, actual.unityVersion, issues);
            Match("runtimePlatform", expected.runtimePlatform, actual.runtimePlatform, issues);
            Match("graphicsDeviceType", expected.graphicsDeviceType, actual.graphicsDeviceType, issues);
            Match("qualityLevelName", expected.qualityLevelName, actual.qualityLevelName, issues);
            // No cross-device compatible-match claim without device validation.
            Match("graphicsDeviceName", expected.graphicsDeviceName, actual.graphicsDeviceName, issues);
            Match("graphicsDeviceVendor", expected.graphicsDeviceVendor, actual.graphicsDeviceVendor, issues);
            if (expected.graphicsDeviceId <= 0 || actual.graphicsDeviceId <= 0 || expected.graphicsDeviceId != actual.graphicsDeviceId)
                issues.Add("graphicsDeviceId: changed or unknown; cross-device collection reuse is not validated.");
            if (expected.graphicsDeviceVendorId <= 0 || actual.graphicsDeviceVendorId <= 0 || expected.graphicsDeviceVendorId != actual.graphicsDeviceVendorId)
                issues.Add("graphicsDeviceVendorId: changed or unknown; cross-device collection reuse is not validated.");
            if (expected.identity == null || actual.identity == null) return;
            Match("buildInputSha256", expected.identity.buildInputSha256, actual.identity.buildInputSha256, issues, true);
            Match("shaderSha256", expected.identity.shaderSha256, actual.identity.shaderSha256, issues, true);
            Match("contentSha256", expected.identity.contentSha256, actual.identity.contentSha256, issues, true);
            Match("contentId", expected.identity.contentId, actual.identity.contentId, issues);
            Match("contentRevision", expected.identity.contentRevision, actual.identity.contentRevision, issues);
        }

        private static void Match(string field, string a, string b, List<string> issues, bool hash = false)
        {
            if (!Known(a) || !Known(b)) issues.Add(field + ": missing or unknown identity.");
            else if (!string.Equals(a, b, hash ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                issues.Add(field + ": changed (recorded='" + a + "', current='" + b + "').");
        }
        private static bool Known(string value) => !string.IsNullOrWhiteSpace(value) &&
            !string.Equals(value, "unknown", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(value, "n/a", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(value, "unavailable", StringComparison.OrdinalIgnoreCase);
        private static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64 || value == new string('0', 64)) return false;
            foreach (char c in value) if (!Uri.IsHexDigit(c)) return false;
            return true;
        }
    }
}
