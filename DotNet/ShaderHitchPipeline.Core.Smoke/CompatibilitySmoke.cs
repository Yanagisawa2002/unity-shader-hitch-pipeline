using System;
using System.Text.Json;
using Yanagisawa.ShaderHitchPipeline;

static class CompatibilitySmoke
{
    private static int checks;
    private static void Check(bool value, string name)
    { if (!value) throw new Exception("COMPATIBILITY_FAILED " + name); checks++; }
    private static PsoEnvironmentSnapshot Environment() => new PsoEnvironmentSnapshot
    {
        engineName = "Unity", engineVersion = "6000.5.2f1", unityVersion = "6000.5.2f1",
        runtimePlatform = "WindowsPlayer", graphicsDeviceType = "Direct3D12", qualityLevelName = "High",
        graphicsDeviceName = "R9700", graphicsDeviceVendor = "AMD", graphicsDeviceId = 123,
        graphicsDeviceVendorId = 4098, graphicsDeviceVersion = "driver1", operatingSystem = "Windows11",
        processorType = "9950X", processorCount = 32, renderingThreadingMode = "MultiThreaded",
        driverIdentity = new string('9',64), driverIdentitySource = PsoCompatibility.DriverIdentitySource, driverVersion = "1.0",
        costExecutionContext = "exact-launch-context", buildGuid = "trace-build-guid",
        identity = new PsoContentIdentity { version = 1, source = PsoCompatibility.BuildIdentitySource,
            buildInputSha256 = new string('a',64), shaderSha256 = new string('b',64),
            contentSha256 = new string('c',64), contentId = "player-content", contentRevision = "rev1" }
    };
    private static PsoCompatibilityContract Contract() => new PsoCompatibilityContract
    { version = 1, collectionEnvironment = Environment(), costEnvironment = Environment(), costModelVersion = PsoCompatibility.CostModelVersion };
    public static void Run()
    {
        var original = Contract();
        Check(PsoCompatibility.Evaluate(original, Environment()).reuseCostModel, "exact match");
        Check(PsoCompatibility.Evaluate(null, null).legacy, "explicit legacy result");
        var current = Environment(); current.buildGuid = "final-build-guid";
        Check(PsoCompatibility.Evaluate(original, current).collectionCompatible, "build GUID provenance vs identical inputs");
        foreach (string field in new[] { "engineName", "engineVersion", "unityVersion", "runtimePlatform", "graphicsDeviceType", "qualityLevelName", "graphicsDeviceName", "graphicsDeviceVendor" })
        {
            current = Environment(); typeof(PsoEnvironmentSnapshot).GetField(field).SetValue(current, "changed");
            Check(!PsoCompatibility.Evaluate(original, current).collectionCompatible, field + " changed");
            typeof(PsoEnvironmentSnapshot).GetField(field).SetValue(current, "Unknown");
            Check(!PsoCompatibility.Evaluate(original, current).collectionCompatible, field + " unknown");
        }
        foreach (string field in new[] { "buildInputSha256", "shaderSha256", "contentSha256", "contentId", "contentRevision", "source" })
        {
            current = Environment(); typeof(PsoContentIdentity).GetField(field).SetValue(current.identity, new string('d',64));
            Check(!PsoCompatibility.Evaluate(original, current).collectionCompatible, field + " changed");
            typeof(PsoContentIdentity).GetField(field).SetValue(current.identity, null);
            Check(!PsoCompatibility.Evaluate(original, current).collectionCompatible, field + " missing");
        }
        foreach (string field in new[] { "graphicsDeviceVersion", "driverIdentity", "driverIdentitySource", "driverVersion", "operatingSystem", "processorType", "renderingThreadingMode", "costExecutionContext" })
        {
            current = Environment(); typeof(PsoEnvironmentSnapshot).GetField(field).SetValue(current, "changed");
            var result = PsoCompatibility.Evaluate(original, current);
            Check(result.collectionCompatible && !result.reuseCostModel, field + " cost-only invalidation");
            typeof(PsoEnvironmentSnapshot).GetField(field).SetValue(current, null);
            result = PsoCompatibility.Evaluate(original, current);
            Check(result.collectionCompatible && !result.reuseCostModel, field + " unavailable cost");
        }
        current = Environment(); current.identity = null;
        Check(!PsoCompatibility.Evaluate(original, current).collectionCompatible, "missing identity");
        current = Environment(); current.identity.version = 2;
        Check(!PsoCompatibility.Evaluate(original, current).collectionCompatible, "future identity");
        current = Environment(); current.identity.shaderSha256 = new string('0',64);
        Check(!PsoCompatibility.Evaluate(original, current).collectionCompatible, "zero hash unavailable");
        current = Environment(); current.graphicsDeviceId = 0;
        Check(!PsoCompatibility.Evaluate(original, current).collectionCompatible, "unknown device");
        var future = Contract(); future.version = 2;
        Check(!PsoCompatibility.Evaluate(future, Environment()).collectionCompatible, "future contract");
        Check(PsoCompatibility.CollectionKey(Environment()) == PsoCompatibility.CollectionKey(Environment()), "stable namespace");
        current = Environment(); current.identity.contentRevision = "rev2";
        Check(PsoCompatibility.CollectionKey(current) != PsoCompatibility.CollectionKey(Environment()), "revision namespace");
        current.identity = null;
        bool threw = false; try { PsoCompatibility.CollectionKey(current); } catch (ArgumentException) { threw = true; }
        Check(threw, "unknown namespace throws");
        var plan = new PsoWarmupPlanDocument { planSha256 = new string('e',64), compatibility = Contract(), phases = new[] {
            new PsoWarmupPhasePlan { phase = "startup", collectionSha256 = new string('f',64), estimatedMillisecondsPerState = 99, initialBatchSize = 64, bootstrapBatchSize = 32 }
        }};
        var cold = Contract(); cold.costEnvironment = null;
        PsoCompatibility.ResetCostPriors(plan, PsoCompatibility.Evaluate(cold, Environment()));
        Check(plan.phases[0].estimatedMillisecondsPerState == 0.25 && plan.phases[0].initialBatchSize == 1 && plan.phases[0].bootstrapBatchSize == 1, "actual cost seed reset");
        var cache = new PsoCostCacheDocument { version = 1, modelVersion = PsoCompatibility.CostModelVersion,
            planSha256 = plan.planSha256, environment = Environment(), entries = new[] { new PsoCostCacheEntry {
                phase = "startup", collectionSha256 = plan.phases[0].collectionSha256, millisecondsPerState = 0.8, observedBatches = 4 } } };
        Check(PsoCostCache.Validate(cache, plan, Environment()).Count == 0, "valid cache");
        current = Environment(); current.graphicsDeviceVersion = "driver2";
        Check(PsoCostCache.Validate(cache, plan, current).Count > 0, "cache driver invalidation");
        cache.entries[0].millisecondsPerState = double.NaN;
        Check(PsoCostCache.Validate(cache, plan, Environment()).Count > 0, "corrupt cost NaN");
        cache.entries[0].millisecondsPerState = 1; cache.entries[0].observedBatches = 0;
        Check(PsoCostCache.Validate(cache, plan, Environment()).Count > 0, "unmeasured cost");
        cache.entries[0].observedBatches = 1; cache.planSha256 = "other";
        Check(PsoCostCache.Validate(cache, plan, Environment()).Count > 0, "cache wrong plan");
        cache.planSha256 = plan.planSha256; cache.entries[0].collectionSha256 = "other";
        Check(PsoCostCache.Validate(cache, plan, Environment()).Count > 0, "cache wrong collection");
        string json = JsonSerializer.Serialize(original, new JsonSerializerOptions { IncludeFields = true });
        var roundtrip = JsonSerializer.Deserialize<PsoCompatibilityContract>(json, new JsonSerializerOptions { IncludeFields = true });
        Check(PsoCompatibility.Evaluate(roundtrip, Environment()).reuseCostModel, "versioned contract roundtrip");
        string scope1 = PsoCostExecutionScope.FromArguments("fixture", "scope-v1", new[] {"player", "-pso-output", "a", "-workers", "4"});
        string scope2 = PsoCostExecutionScope.FromArguments("fixture", "scope-v1", new[] {"player", "-pso-output", "b", "-workers", "4"});
        Check(scope1 == scope2, "evidence destination normalization");
        Check(scope1 != PsoCostExecutionScope.FromArguments("fixture", "scope-v1", new[] {"player", "-pso-output", "b", "-workers", "8"}), "worker context changes");
        Check(scope1 != PsoCostExecutionScope.FromArguments("fixture", "scope-v1", new[] {"player", "-pso-output", "b", "-workers", "4", "-unknown"}), "unknown argument retained");
        var members = new PsoJsonObject("{\"phases\":[{\"compatibility\":{}}],\"compatibility\":null}");
        Check(members.Get("compatibility") == "null", "top-level null member");
        Check(new PsoJsonObject("{\"phases\":[{\"compatibility\":{}}]}").Get("compatibility") == null, "nested name is not top-level presence");
        Check(new PsoJsonObject("{\"compatibility\":{}}").Get("compatibility") == "{}", "explicit empty contract retained");
        Check(new PsoJsonObject("{\"compatibility\":{},\"text\":\"escaped \\\" braces {} \"}").Get("compatibility") == "{}", "escaped strings scanned");
        threw = false; try { new PsoJsonObject("{\"compatibility\":null,\"compatibility\":{}}"); } catch (System.IO.InvalidDataException) { threw = true; }
        Check(threw, "duplicate contract rejected");
        Console.WriteLine("COMPATIBILITY_SMOKE_OK checks=" + checks);
    }
}
