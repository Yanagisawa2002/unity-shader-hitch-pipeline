using System;
using System.Text.Json;
using Yanagisawa.ShaderHitchPipeline;

// Normal Core APIs with an explicitly simulated content sink.
// This example runs no Unity, native warmup, clock, GPU command or benchmark.
if (args.Length != 0) throw new ArgumentException("This CPU illustration accepts no runtime or workload arguments.");
foreach (var options in new[] {
    new PsoSchedulingOptions(),
    PsoSchedulingOptions.FixedProgressive(8),
    PsoSchedulingOptions.ObservedBudget()
})
{
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        example = "policy-configuration", execution = "CPU API illustration",
        policy = options.PolicyId, fixedBatchSize = options.FixedBatchSize,
        conservativeAdmission = options.ConservativeAdmission,
        deadlines = options.EnableDeadlines, adaptiveCost = options.EnableAdaptiveCost,
        hotSetPriority = options.EnableHotSetPriority, costPriority = options.EnableCostPriority,
        performance = "Unmeasured", hardDriverLatencyBound = false
    }));
}

var sink = new IllustrativeSink();
using var loads = new PsoContentPhaseLifecycle(sink);
var prior = loads.Request("city-sector", "content-revision-A", "city");
Require(loads.DependenciesReady(prior), "Ready dependencies should activate the initial request.");
sink.SubmitSimulatedWork();
loads.Unload(prior);
Require(prior.Status == PsoContentPhaseStatus.Unloaded && sink.ResourcesRetained && sink.RetirementPending,
    "Unloaded demand must not release resources still owned by a pending fence.");
Print("unloaded-demand-retains-resources", prior, sink);

var oldRequest = loads.Request("city-sector", "content-revision-B", "city");
Require(!loads.DependenciesReady(oldRequest) && oldRequest.Status == PsoContentPhaseStatus.WaitingForDependencies && oldRequest.Failure == null,
    "A deferred activation must remain retryable, not accepted or failed.");
Print("waiting-for-retirement", oldRequest, sink);
loads.Cancel(oldRequest);
int attemptsBeforeCallback = sink.ActivationAttempts;
Require(!loads.DependenciesReady(oldRequest) && oldRequest.Status == PsoContentPhaseStatus.Cancelled &&
    sink.ActivationAttempts == attemptsBeforeCallback && sink.CancelCalls == 0,
    "A cancelled, unaccepted request cannot reach the sink again or cancel another owner's work.");
Print("cancelled-callback-ignored", oldRequest, sink);

var replacement = loads.Request("city-sector", "content-revision-C", "city");
Require(replacement.Generation > oldRequest.Generation && !loads.DependenciesReady(replacement),
    "A fresh generation still waits for the prior owner's fence.");
sink.SignalSimulatedFence();
Require(!sink.ResourcesRetained && !sink.RetirementPending && prior.Status == PsoContentPhaseStatus.Unloaded,
    "Only the explicit simulated fence retires the old owner; it does not complete unloaded demand.");
Require(loads.DependenciesReady(replacement) && sink.ResourcesRetained,
    "Retry must acquire a new owner after retirement.");
Print("replacement-active", replacement, sink);
sink.SubmitSimulatedWork();
sink.SignalSimulatedFence();
loads.Refresh();
Require(replacement.Status == PsoContentPhaseStatus.Active,
    "A finished batch alone is not complete warmup coverage.");
sink.ReportSimulatedWarmupComplete();
loads.Refresh();
Require(replacement.Status == PsoContentPhaseStatus.Complete, "Completion must come from the sink.");
Print("simulated-sink-complete", replacement, sink);
loads.Unload(replacement);
Require(replacement.Status == PsoContentPhaseStatus.Unloaded && !sink.ResourcesRetained,
    "A completed owner with no pending fence should release on unload.");
Print("replacement-unloaded", replacement, sink);
Console.WriteLine("POLICY_EXAMPLE_OK simulated-sink-only; performance=Unmeasured; first-draw-coverage=Unverified");

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Print(string example, PsoContentPhaseRequest request, IllustrativeSink sink) =>
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        example, execution = "simulated content sink only",
        content = request.ContentId, revision = request.Revision,
        generation = request.Generation, status = request.Status.ToString(),
        simulatedFencePending = sink.FencePending, resourcesRetained = sink.ResourcesRetained,
        retirementPending = sink.RetirementPending,
        nativeWarmupExecuted = false, realFirstDrawCoverageEstablished = false
    }));

// One-phase teaching model of an external owner, not a scheduler or Unity adapter.
// Methods below set simulated events explicitly. No native fence or asset is created.
sealed class IllustrativeSink : IPsoContentPhaseSink
{
    public bool ResourcesRetained { get; private set; }
    public bool RetirementPending { get; private set; }
    public bool FencePending { get; private set; }
    public int ActivationAttempts { get; private set; }
    public int CancelCalls { get; private set; }
    private bool demandActive, reportedComplete;

    public PsoContentPhaseActivation Activate(string phase)
    {
        ActivationAttempts++;
        if (phase != "city") return PsoContentPhaseActivation.Unavailable;
        if (RetirementPending) return PsoContentPhaseActivation.Deferred;
        ResourcesRetained = demandActive = true;
        return PsoContentPhaseActivation.Accepted;
    }
    public void Cancel(string phase)
    {
        RequireCity(phase);
        CancelCalls++;
        demandActive = false; // Cancellation preserves the resident owner, including any pending fence.
    }
    public void Unload(string phase)
    {
        RequireCity(phase);
        demandActive = reportedComplete = false;
        RetirementPending = true;
        ReleaseIfRetired();
    }
    public bool IsComplete(string phase) => phase == "city" && demandActive && reportedComplete;
    public void SubmitSimulatedWork()
    {
        if (!demandActive || RetirementPending || FencePending) throw new InvalidOperationException("No ready simulated owner.");
        reportedComplete = false;
        FencePending = true;
    }
    public void SignalSimulatedFence()
    {
        if (!FencePending) throw new InvalidOperationException("No pending simulated fence.");
        FencePending = false;
        ReleaseIfRetired();
    }
    public void ReportSimulatedWarmupComplete()
    {
        if (!demandActive || !ResourcesRetained || FencePending) throw new InvalidOperationException("Warmup is not ready to complete.");
        reportedComplete = true;
    }
    private void ReleaseIfRetired()
    {
        if (RetirementPending && !FencePending) ResourcesRetained = RetirementPending = false;
    }
    private static void RequireCity(string phase)
    {
        if (phase != "city") throw new ArgumentException("This simulated owner handles only city.");
    }
}
