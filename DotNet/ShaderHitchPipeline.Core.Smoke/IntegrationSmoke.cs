using System;
using System.IO;
using System.Linq;
using Yanagisawa.ShaderHitchPipeline;

static class IntegrationSmoke
{
    private static int checks;
    private static void Check(bool condition, string name)
    { if (!condition) throw new Exception("INTEGRATION_FAILED " + name); checks++; }
    private static PsoEnvironmentSnapshot Environment() => new PsoEnvironmentSnapshot
    {
        engineName = "Unity", engineVersion = "6000.5.2f1", unityVersion = "6000.5.2f1",
        runtimePlatform = "WindowsPlayer", graphicsDeviceType = "Direct3D12", qualityLevelName = "High",
        graphicsDeviceName = "R9700", graphicsDeviceVendor = "AMD", graphicsDeviceId = 123, graphicsDeviceVendorId = 4098,
        graphicsDeviceVersion = "D3D12 level 12.2", operatingSystem = "Windows11", processorType = "9950X",
        processorCount = 32, renderingThreadingMode = "MultiThreaded", driverIdentity = new string('9', 64),
        driverIdentitySource = PsoCompatibility.DriverIdentitySource, driverVersion = "test-driver-1", costExecutionContext = "test-fixed-scope",
        identity = new PsoContentIdentity { version = 1, source = PsoCompatibility.BuildIdentitySource,
            buildInputSha256 = new string('a', 64), shaderSha256 = new string('b', 64), contentSha256 = new string('c', 64),
            contentId = "player-content", contentRevision = "rev1" }
    };

    public static void Run()
    {
        var recorded = Environment();
        var plan = new PsoWarmupPlanDocument
        {
            planSha256 = new string('d', 64),
            compatibility = new PsoCompatibilityContract { version = 1, collectionEnvironment = recorded,
                costEnvironment = recorded, costModelVersion = PsoCompatibility.CostModelVersion },
            phases = new[] {
                new PsoWarmupPhasePlan { phase = "required", collectionSha256 = new string('e',64), graphicsStateCount = 1 },
                new PsoWarmupPhasePlan { phase = "optional", required = false, collectionSha256 = new string('f',64), graphicsStateCount = 2 }
            }
        };
        var cache = new PsoCostCacheDocument { version = 1, modelVersion = PsoCompatibility.CostModelVersion,
            planSha256 = plan.planSha256, environment = recorded,
            entries = plan.phases.Select(p => new PsoCostCacheEntry { phase = p.phase,
                collectionSha256 = p.collectionSha256, millisecondsPerState = 1, observedBatches = 3 }).ToArray() };
        string fileHash = new string('1', 64);
        var policy = PsoIntegratedCompatibility.CreateCostBackedPolicy(plan, fileHash, cache, null, 3);
        policy.training = new[] { new PsoHotsetTrace { routeId = "training", captureId = "training-capture", split = "training",
            compatibilityNamespace = policy.units[0].compatibilityNamespace, units = policy.units,
            uses = new[] { new PsoHotsetUse { unitId = "optional", phaseOrdinal = 0, milliseconds = 1 } } } };
        Func<PsoWarmupPlanDocument, PsoStartupHotsetDocument, bool> proof = (p, d) =>
            PsoIntegratedCompatibility.ValidateHotsetCostProof(p, d, cache, Environment()).Count == 0;
        Check(proof(plan, policy), "matching measured costs and current content");
        Check(PsoStartupHotset.PrepareValidated(plan, fileHash, policy, proof).startupUnitIds.Length == 2,
            "measured optional unit admitted after cross-module validation");
        policy.units[1].estimatedWarmupMilliseconds = 0.001;
        Check(!proof(plan, policy), "unproven cheaper estimate rejected");
        var fallback = PsoStartupHotset.PrepareValidated(plan, fileHash, policy, proof);
        Check(fallback.startupUnitIds.SequenceEqual(new[] { "required" }), "invalid cost still preserves required startup");
        policy.units[1].estimatedWarmupMilliseconds = 2;
        var changed = Environment(); changed.driverIdentity = new string('8', 64);
        Check(PsoIntegratedCompatibility.RequireCollectionNamespace(plan.compatibility, changed) == policy.units[0].compatibilityNamespace,
            "driver change preserves collection namespace");
        Check(PsoIntegratedCompatibility.ValidateHotsetCostProof(plan, policy, cache, changed).Count > 0,
            "driver change invalidates hotset measured cost");
        changed = Environment(); changed.identity.contentRevision = "rev2";
        bool rejected = false;
        try { PsoIntegratedCompatibility.RequireCollectionNamespace(plan.compatibility, changed); }
        catch (InvalidDataException) { rejected = true; }
        Check(rejected, "streaming namespace rejected for changed content");
        policy.units[1].compatibilityNamespace = "unattested";
        Check(!proof(plan, policy), "policy namespace cannot authorize stale content");
        policy.units[1].compatibilityNamespace = policy.units[0].compatibilityNamespace;
        Check(PsoIntegratedCompatibility.ValidateHotsetCostProof(plan, policy, null, Environment()).Count > 0,
            "missing measured cache does not authorize selection");
        cache.entries = new[] { cache.entries[0] };
        var partial = PsoIntegratedCompatibility.CreateCostBackedPolicy(plan, fileHash, cache, policy.training, 3);
        Check(partial.units[1].estimatedWarmupMilliseconds == -1, "missing phase cost remains unknown");
        Check(PsoIntegratedCompatibility.ValidateHotsetCostProof(plan, partial, cache, Environment()).Count == 0,
            "unknown unmeasured optional cost remains a valid conservative policy");
        partial.units[1].estimatedWarmupMilliseconds = 1;
        Check(PsoIntegratedCompatibility.ValidateHotsetCostProof(plan, partial, cache, Environment()).Count > 0,
            "uncached phase cannot acquire an invented estimate");
        Console.WriteLine("INTEGRATION_SMOKE_OK checks=" + checks);
    }
}
