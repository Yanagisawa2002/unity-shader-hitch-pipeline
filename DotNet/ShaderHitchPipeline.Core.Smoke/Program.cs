using System;
using System.Text.Json;
using Yanagisawa.ShaderHitchPipeline;

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

var policy = new PsoAdaptiveBatchPolicy(
    initial: 64,
    minimum: 1,
    maximum: 64,
    targetFrameMilliseconds: 16.67,
    estimatedMillisecondsPerState: 0.25);
PsoBudgetAdmission probe = policy.Evaluate(389, 1000.0, true);
Require(probe.BatchSize == 1, "Cold start was not constrained to one state.");

var candidates = new[]
{
    new PsoSchedulerCandidate("hot", 100, 0, 0, 1000.0, 100.0, 0.2, 1.0),
    new PsoSchedulerCandidate("deadline", 20, 1, 1, 150.0, 100.0, 1.0, 0.5),
};
PsoSchedulingDecision decision = PsoDeadlineCostScheduler.SelectNext(candidates, 40.0);
Require(decision.Phase == "deadline", "Deadline-critical phase was not selected.");

var plan = new PsoWarmupPlanDocument
{
    profileId = "engine-neutral-smoke",
    runtimePlatform = "Windows",
    graphicsDeviceType = "D3D12",
    adapterId = "example.native-d3d12",
    adapterVersion = "1",
    phases = new[]
    {
        new PsoWarmupPhasePlan
        {
            phase = "startup",
            collectionFile = "startup.pso-trace",
            collectionSha256 = "example",
            graphicsStateCount = 389,
            deadlineMilliseconds = 1000.0,
            hotSetTier = 0,
        },
    },
};
string json = JsonSerializer.Serialize(
    plan,
    new JsonSerializerOptions { IncludeFields = true, WriteIndented = true });
Require(json.Contains("example.native-d3d12", StringComparison.Ordinal),
    "Engine-neutral adapter id did not survive serialization.");
Require(PsoPlanRules.Validate(plan).Count == 0,
    "Engine-neutral plan validation rejected a valid plan.");

Console.WriteLine("CORE_SMOKE_OK phase={0} coldBatch={1} schema={2}",
    decision.Phase,
    probe.BatchSize,
    plan.schemaVersion);

HotsetTests.Run();
