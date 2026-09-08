using System;
using System.Collections.Generic;
using System.Linq;
using Yanagisawa.ShaderHitchPipeline;

// Only functional assertions over caller-controlled numbers and mock fences. No real clock,
// Unity, profiling, workload timing, policy search or native backend can enter this executable.
if (args.Length != 0) throw new ArgumentException("This entry accepts no performance or workload arguments.");
int passed = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}
void Test(string name, Action body) { body(); passed++; Console.WriteLine("PASS " + name); }
PsoAdaptiveBatchPolicy Policy(double cost = 0.25, bool conservative = false, int minimum = 1) =>
    new PsoAdaptiveBatchPolicy(8, minimum, 64, 20, cost, minimum, 2, 1.5, 2, conservative);
PsoWarmupPhasePlan Phase(string name, double deadline = 0, int tier = 1, int priority = 0) =>
    new PsoWarmupPhasePlan { phase = name, deadlineMilliseconds = deadline, hotSetTier = tier,
        targetFrameMilliseconds = 20, priority = priority };

Test("reject nonfinite inputs without poisoning cost state", () =>
{
    Throws<ArgumentOutOfRangeException>(() => Policy(double.PositiveInfinity));
    Throws<ArgumentOutOfRangeException>(() => new PsoAdaptiveBatchPolicy(1, 1, 1, double.NaN));
    var p = Policy(); p.ObserveBatch(1, double.PositiveInfinity); p.ObserveFrame(double.NaN, true, 1);
    Check(p.BatchObservationCount == 0 && p.BudgetViolationCount == 0, "Invalid observations entered model.");
    Throws<ArgumentOutOfRangeException>(() => new PsoSchedulerCandidate("x", 1, 0, 0, 1, 0, double.NaN, 1));
});
Test("tail smaller than minimum is submitted exactly", () =>
{
    var p = Policy(minimum: 4); p.ObserveBatch(4, 1); p.ObserveBatch(4, 1);
    var a = p.Evaluate(2, double.PositiveInfinity, true);
    Check(a.BatchSize == 2 && a.BatchSize <= a.SafeBatchSize, "Tail exceeded remaining work.");
    Check(p.Evaluate(0, 1, false).BatchSize == 0, "Empty phase admitted.");
});
Test("preview never grows or commits a waiting policy", () =>
{
    var p = Policy(); int count = p.CurrentBatchSize;
    var a = p.Preview(100, 1, true);
    Check(p.CurrentBatchSize == count && p.LastAdmission.Reason == "not-evaluated", "Preview committed.");
    p.CommitAdmission(a); Check(p.CurrentBatchSize == a.BatchSize, "Selected admission not committed.");
});
Test("latest frame burst wins over smoothed idle baseline", () =>
{
    var p = Policy(conservative: true);
    p.ObserveFrame(1, false, 0); p.ObserveFrame(19, false, 0);
    Check(!p.Preview(10, 1, true).IsAdmitted, "Burst admitted using stale EWMA headroom.");
});
Test("uncertainty envelope retains underpredicted observations", () =>
{
    var p = Policy(conservative: true);
    p.ObserveBatch(2, 10); p.ObserveBatch(4, 2); p.ObserveBatch(8, 3);
    Check(p.PredictBatchMilliseconds(2) >= 15, "Fitting erased an observed underprediction.");
});
Test("cost age and identity invalidate slope and bootstrap state", () =>
{
    var p = Policy(conservative: true); p.UpdateCostContext("a", 0, 10);
    p.ObserveBatch(1, 0.2); p.ObserveBatch(2, 0.4); p.ObserveBatch(4, 0.8);
    Check(p.HasMeasuredCostSlope, "Fixture did not fit a slope.");
    p.UpdateCostContext("a", 10, 10);
    Check(!p.HasMeasuredCostSlope && p.BatchObservationCount == 0 && p.CostGeneration == 1, "Expired model reused.");
    p.UpdateCostContext("b", 11, 10);
    Check(p.CostGeneration == 2 && p.CostInvalidationReason == "cost-environment-changed", "Identity ignored.");
});
Test("minimum opaque overrun requires explicit window after expiry", () =>
{
    var p = Policy(conservative: true); p.UpdateCostContext("a", 0, 10);
    p.ObserveBatch(1, 50); p.UpdateCostContext("a", 11, 10);
    Check(p.RequiresNonInteractiveWindow && !p.Preview(5, -1, true).IsAdmitted, "Expiry erased safety hazard.");
    Check(p.Preview(5, -1, true, nonInteractiveBudgetMilliseconds: 100).IsAdmitted, "Explicit loading window unusable.");
});
Test("very distant deadline does not overflow frame count", () =>
{
    var p = Policy(); var a = p.Preview(int.MaxValue, double.MaxValue, false);
    Check(a.DeadlineBatchSize == 1, "Overflow produced urgency.");
});
Test("fixed progressive uses exact batches and does not learn", () =>
{
    var clock = new VirtualClock(); var backend = new MockBackend(7, clock);
    using var q = new PsoWarmupScheduler(clock, PsoSchedulingOptions.FixedProgressive(3));
    q.Register(Phase("x"), backend, Policy()); var run = q.Activate("x");
    for (int i = 0; i < 4; i++) { q.Tick(1); backend.Finish(); clock.Advance(1); }
    Check(run.IsComplete && q.IsComplete, "Fixed work incomplete.");
    Check(backend.Requests.SequenceEqual(new[] { 3, 3, 1 }) && backend.BulkRequests == 0, "Fixed policy changed semantic workload.");
    Check(run.Policy.BatchObservationCount == 0, "Disabled adaptive cost still learned.");
});
Test("fixed ordering is stable activation order", () =>
{
    var clock = new VirtualClock(); using var q = new PsoWarmupScheduler(clock, PsoSchedulingOptions.FixedProgressive(1));
    var z = new MockBackend(1, clock); var a = new MockBackend(1, clock);
    q.Register(Phase("z", priority: 99), z, Policy()); q.Register(Phase("a", priority: -1), a, Policy());
    q.Activate("z"); q.Activate("a"); q.Tick(1);
    Check(z.Requests.Count == 1 && a.Requests.Count == 0, "Fixed ordering was silently prioritized.");
    z.Finish(); q.Pump();
});
Test("inadmissible urgent phase does not block ready phase", () =>
{
    var clock = new VirtualClock(); using var q = new PsoWarmupScheduler(clock);
    var blocked = new MockBackend(5, clock); var ready = new MockBackend(1, clock);
    q.Register(Phase("urgent", 1, 0), blocked, Policy(100)); q.Register(Phase("ready"), ready, Policy());
    var late = q.Activate("urgent"); q.Activate("ready"); q.Tick(1);
    Check(blocked.Requests.Count == 0 && ready.Requests.Count == 1 && late.DeferredFrames == 1, "Head of line blocked ready work.");
    ready.Finish(); q.Pump();
});
Test("deadline budget conflict preserves pending work and missed status", () =>
{
    var clock = new VirtualClock(); using var q = new PsoWarmupScheduler(clock, PsoSchedulingOptions.ObservedBudget());
    var backend = new MockBackend(1, clock); q.Register(Phase("x", 1), backend, Policy(100, true));
    var run = q.Activate("x"); clock.Advance(5); q.Tick(1);
    Check(run.State == PsoPhaseState.Pending && run.DeadlineMissed && !run.DeadlineFeasible && !q.IsComplete,
        "Deadline conflict dropped work or faked completion.");
    q.Tick(1, 200); backend.Finish(); q.Tick(1);
    Check(run.IsComplete && run.DeadlineMissed, "Late admitted work could not complete honestly.");
});
Test("missed deadline remains eligible for safe progressive work", () =>
{
    var clock = new VirtualClock(); using var q = new PsoWarmupScheduler(clock, PsoSchedulingOptions.ObservedBudget());
    var b = new MockBackend(1, clock); q.Register(Phase("x", 1), b, Policy(conservative: true));
    var run = q.Activate("x"); clock.Advance(100); q.Tick(1); b.Finish(); q.Tick(1);
    Check(run.IsComplete && run.DeadlineMissed, "Expired work permanently deferred.");
});
Test("native bulk uses full cost and never core-count scaling", () =>
{
    var clock = new VirtualClock(); using var q = new PsoWarmupScheduler(clock);
    var b = new MockBackend(100, clock); q.Register(Phase("x", 1), b, Policy(1), true);
    var run = q.Activate("x"); q.Tick(1);
    Check(b.Requests.Count == 0 && run.LastAdmissionReason == "native-bulk-requires-explicit-window", "Bulk passed a one-state gate.");
    clock.Advance(10); q.Tick(1, 200); b.Finish(); q.Tick(1);
    Check(b.Requests.Single() == 100 && b.BulkRequests == 1 && run.IsComplete && run.DeadlineMissed,
        "Bulk deadline became a permanent drop/defer gate.");
});
Test("queued cancellation is distinct from completion", () =>
{
    var clock = new VirtualClock(); using var q = new PsoWarmupScheduler(clock);
    var b = new MockBackend(1, clock); q.Register(Phase("x"), b, Policy()); var run = q.Activate("x");
    Check(q.CancelPhase("x"), "Cancellation refused."); q.Tick(1);
    Check(run.State == PsoPhaseState.Cancelled && !run.IsComplete && !q.IsComplete && b.Requests.Count == 0, "Cancelled demand counted as warmed.");
});
Test("cancel then burst reactivation retains old fence and new demand", () =>
{
    var clock = new VirtualClock(); using var q = new PsoWarmupScheduler(clock);
    var b = new MockBackend(1, clock); q.Register(Phase("x"), b, Policy()); var old = q.Activate("x");
    q.Tick(1); q.CancelPhase("x"); var next = q.Activate("x"); q.Tick(1);
    Check(old != next && old.HasInFlightBatch && next.State == PsoPhaseState.Pending && b.Requests.Count == 1, "Reactivation lost or duplicated work.");
    b.Finish(); clock.Advance(2); q.Tick(1);
    Check(old.State == PsoPhaseState.Cancelled && !old.IsComplete && next.IsComplete && q.IsComplete && b.DisposeCalls == 0,
        "Cancelled history resurrected or resident prematurely released.");
    q.UnloadPhase("x"); Check(b.DisposeCalls == 1, "Last owner not released.");
});
Test("unload releases only after fence and retains incomplete history", () =>
{
    var clock = new VirtualClock(); using var q = new PsoWarmupScheduler(clock);
    var b = new MockBackend(4, clock); q.Register(Phase("x"), b, Policy()); var run = q.Activate("x");
    q.Tick(1); q.UnloadPhase("x"); Check(b.DisposeCalls == 0, "In-flight resource freed.");
    b.Finish(); q.Pump(); Check(b.DisposeCalls == 1 && !q.IsResident("x") && !run.IsComplete && !q.IsComplete, "Unload erased demand history.");
});
Test("in-flight old identity sample cannot train new model", () =>
{
    var clock = new VirtualClock(); using var q = new PsoWarmupScheduler(clock, PsoSchedulingOptions.ObservedBudget(), "a");
    var b = new MockBackend(2, clock); var p = Policy(conservative: true); q.Register(Phase("x"), b, p); q.Activate("x");
    q.Tick(1); q.InvalidateCostIdentity("b"); b.Finish(); clock.Advance(1); q.Pump();
    Check(p.CostGeneration == 1 && p.BatchObservationCount == 0, "Old environment sample leaked into new model.");
});
Test("submit failure keeps phase failed without successful work", () =>
{
    var clock = new VirtualClock(); using var q = new PsoWarmupScheduler(clock);
    var b = new MockBackend(2, clock) { FailSubmit = true }; q.Register(Phase("x"), b, Policy()); var run = q.Activate("x");
    q.Tick(1); Check(run.State == PsoPhaseState.Faulted && !q.HasInFlightBatch && !run.IsComplete, "Submission failure hidden.");
});
Test("unproven fence failure retains owner and retries without resubmit", () =>
{
    var clock = new VirtualClock(); var q = new PsoWarmupScheduler(clock);
    var b = new MockBackend(2, clock) { CompleteFailures = 1 }; q.Register(Phase("x"), b, Policy()); var run = q.Activate("x");
    q.Tick(1); b.Finish(); q.Tick(1);
    Check(run.State == PsoPhaseState.Faulted && q.HasInFlightBatch && b.DisposeCalls == 0, "Failed fence released owner.");
    q.Dispose(); Check(!q.HasInFlightBatch && b.DisposeCalls == 1 && b.Requests.Count == 1 && !run.IsComplete, "Fence retry leaked or resubmitted.");
});
Test("batch dispose retry does not duplicate cost observations", () =>
{
    var clock = new VirtualClock(); var q = new PsoWarmupScheduler(clock);
    var b = new MockBackend(2, clock) { BatchDisposeFailures = 1 }; var p = Policy(); q.Register(Phase("x"), b, p); q.Activate("x");
    q.Tick(1); b.Finish(); clock.Advance(1); q.Pump();
    Check(q.HasInFlightBatch && p.BatchObservationCount == 1, "Fixture did not retain proven fence.");
    q.Dispose(); Check(p.BatchObservationCount == 1 && b.DisposeCalls == 1, "Disposal retrained same sample.");
});
Test("no progress fails rather than loops or synthesizes completion", () =>
{
    var clock = new VirtualClock(); using var q = new PsoWarmupScheduler(clock);
    var b = new MockBackend(2, clock) { ProgressPerBatch = 0 }; q.Register(Phase("x"), b, Policy()); var run = q.Activate("x");
    q.Tick(1); b.Finish(); q.Tick(1); b.Finish(); q.Tick(1); q.Pump();
    Check(run.State == PsoPhaseState.Faulted && b.Requests.Count == 2 && !run.IsComplete, "No-progress work ran forever or completed.");
});
Test("raw progress equal to graphics count is not warmup attestation", () =>
{
    var clock = new VirtualClock(); using var q = new PsoWarmupScheduler(clock);
    var b = new MockBackend(1, clock) { ForceNotWarmed = true }; q.Register(Phase("x"), b, Policy()); var run = q.Activate("x");
    q.Tick(1); b.Finish(); q.Pump();
    Check(run.CompletedPermutations == 1 && !run.IsComplete, "Incomparable graphics/permutation counters declared completion.");
});
Test("startup gate requires entire backend completion", () =>
{
    var clock = new VirtualClock(); using var q = new PsoWarmupScheduler(clock);
    var b = new MockBackend(5, clock) { ProgressPerBatch = 1 }; q.Register(Phase("x"), b, Policy()); var run = q.Activate("x");
    q.CompleteStartupGate("x"); Check(run.State == PsoPhaseState.Faulted && !run.IsComplete, "Partial startup gate succeeded.");
});
Test("dispatch time is included in virtual batch observation", () =>
{
    var clock = new VirtualClock(); using var q = new PsoWarmupScheduler(clock, PsoSchedulingOptions.ObservedBudget());
    var b = new MockBackend(2, clock) { SubmitAdvance = 30 }; q.Register(Phase("x"), b, Policy(conservative: true)); var run = q.Activate("x");
    q.Tick(1); clock.Advance(2); b.Finish(); q.Tick(1);
    Check(run.Batches[0].ElapsedMilliseconds == 32 && run.RequiresNonInteractiveWindow && b.Requests.Count == 1,
        "Synchronous dispatch duration escaped observation/admission downgrade.");
});
Test("dispose is nonblocking; explicit drain retains all owners until fence", () =>
{
    var clock = new VirtualClock(); var q = new PsoWarmupScheduler(clock); var b = new MockBackend(2, clock);
    q.Register(Phase("x"), b, Policy()); q.Activate("x"); q.Tick(1); q.Dispose();
    Check(b.Last.CompleteCalls == 0 && b.DisposeCalls == 0 && q.HasInFlightBatch, "Dispose blocked or freed unfenced backend.");
    Check(q.Drain() && b.DisposeCalls == 1 && !q.HasInFlightBatch, "Explicit shutdown could not drain.");
});
Test("one release failure does not prevent independent owners releasing", () =>
{
    var clock = new VirtualClock(); var q = new PsoWarmupScheduler(clock);
    var a = new MockBackend(1, clock) { BackendDisposeFailures = 1 }; var b = new MockBackend(1, clock);
    q.Register(Phase("a"), a, Policy()); q.Register(Phase("b"), b, Policy());
    Throws<AggregateException>(() => q.Dispose()); Check(b.DisposeCalls == 1, "Independent release was skipped.");
    q.Dispose(); Check(a.DisposeCalls == 1, "Retained failed release could not retry.");
});
Test("hotset and deadline priorities can be independently disabled", () =>
{
    var c = new[] { new PsoSchedulerCandidate("hot", 10, 0, 0, 100, 0, 0.25, 1),
        new PsoSchedulerCandidate("urgent", 1, 0, 1, 1, 0, 0.25, 1) };
    var options = PsoSchedulingOptions.ObservedBudget();
    Check(PsoDeadlineCostScheduler.SelectNext(c, 5, options).Phase == "urgent", "Deadline switch ineffective.");
    options.EnableDeadlines = false;
    Check(PsoDeadlineCostScheduler.SelectNext(c, 5, options).Phase == "hot", "Hotset switch ineffective.");
    options.EnableHotSetPriority = false;
    Check(PsoDeadlineCostScheduler.SelectNext(c, 5, options).Phase == "urgent", "Cost density switch ineffective.");
});
Test("viable deadline outranks already missed work; bounded aging restores fairness", () =>
{
    var c = new[] { new PsoSchedulerCandidate("missed", 100, 0, 0, 1, 100, 1, 1, waitingFrames: 2),
        new PsoSchedulerCandidate("viable", 1, 0, 1, 5, 0, 1, 1) };
    var options = PsoSchedulingOptions.ObservedBudget();
    Check(PsoDeadlineCostScheduler.SelectNext(c, 5, options).Phase == "viable", "Missed slack monopolized queue.");
    options.MaximumStarvationFrames = 2;
    Check(PsoDeadlineCostScheduler.SelectNext(c, 5, options).Phase == "missed", "Aged work permanently starved.");
});
Test("clock rollback is rejected before submission", () =>
{
    var clock = new VirtualClock(); clock.Advance(5); using var q = new PsoWarmupScheduler(clock);
    clock.Advance(-1); Throws<InvalidOperationException>(() => q.Tick(1)); clock.Advance(1);
});
Test("joint deadline demand reports infeasibility without discarding either phase", () =>
{
    var clock = new VirtualClock(); var options = PsoSchedulingOptions.ObservedBudget();
    using var q = new PsoWarmupScheduler(clock, options);
    var a = new MockBackend(1, clock); var b = new MockBackend(1, clock);
    q.Register(Phase("a", 10), a, Policy(5, true)); q.Register(Phase("b", 10), b, Policy(5, true));
    var first = q.Activate("a"); var second = q.Activate("b"); q.Tick(1);
    Check(!first.DeadlineFeasible && !second.DeadlineFeasible && q.PendingCount == 2,
        "Independent cost estimates hid shared deadline contention.");
    a.Finish(); b.Finish(); q.Pump();
});
Test("fixed policy refuses a native bulk substitution", () =>
{
    var clock = new VirtualClock(); using var q = new PsoWarmupScheduler(clock, PsoSchedulingOptions.FixedProgressive(2));
    var b = new MockBackend(1, clock);
    Throws<NotSupportedException>(() => q.Register(Phase("x"), b, Policy(), true));
    Check(b.Requests.Count == 0, "Unsupported fixed policy executed.");
});
Test("invalidated imported prior cannot return through later observations", () =>
{
    var p = Policy(9, true); p.UpdateCostContext("a", 0, 10); p.UpdateCostContext("b", 1, 10);
    p.ObserveBatch(1, 1); p.ObserveBatch(1, 1);
    Check(p.EstimatedMillisecondsPerState == 0.25, "Invalidated scalar prior was restored.");
});
Test("feedback keeps activation identity and explicit policy switches", () =>
{
    var clock = new VirtualClock(); var options = PsoSchedulingOptions.ObservedBudget();
    using var q = new PsoWarmupScheduler(clock, options); var b = new MockBackend(1, clock);
    q.Register(Phase("x"), b, Policy(conservative: true)); q.Activate("x"); q.CancelPhase("x"); q.Activate("x");
    var feedback = PsoSchedulingFeedback.Capture(q, options, "plan-hash");
    string json = System.Text.Json.JsonSerializer.Serialize(feedback, new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
    Check(feedback.activations.Length == 2 && feedback.activations[0].activation != feedback.activations[1].activation &&
        feedback.activations[0].state == "Cancelled" && json.Contains("observed-budget") && !feedback.completed,
        "Feedback erased cancellation or policy identity.");
});
Test("caller mutations cannot silently change selected policy", () =>
{
    var clock = new VirtualClock(); var options = PsoSchedulingOptions.FixedProgressive(2);
    using var q = new PsoWarmupScheduler(clock, options); options.FixedBatchSize = 20;
    var b = new MockBackend(5, clock); var phase = Phase("x"); q.Register(phase, b, Policy()); phase.deadlineMilliseconds = double.NaN;
    q.Activate("x"); q.Tick(1); Check(b.Requests.Single() == 2, "Caller changed scheduler snapshot."); b.Finish(); q.Pump();
});
Test("partial dependency resolution cannot attest warmup or trace baseline", () =>
{
    Throws<InvalidOperationException>(() => PsoCollectionReadiness.RequireFullCollection("deferred", 40, 3));
    Throws<InvalidOperationException>(() => PsoCollectionReadiness.RequireFullCollection("deferred", 40, 0));
    PsoCollectionReadiness.RequireFullCollection("deferred", 40, 40);
});
Test("all cost-critical environment fields participate in invalidation", () =>
{
    foreach (string field in new[] { "driverVersion", "driverIdentitySource", "operatingSystem", "costExecutionContext",
        "graphicsDeviceVendor", "processorType", "renderingThreadingMode" })
    {
        var environment = new PsoEnvironmentSnapshot();
        string original = PsoSchedulingOptions.CostIdentity(environment, "plan");
        typeof(PsoEnvironmentSnapshot).GetField(field).SetValue(environment, "changed");
        Check(PsoSchedulingOptions.CostIdentity(environment, "plan") != original, "Identity missed " + field);
    }
});
Console.WriteLine("SCHEDULER_FUNCTIONAL_OK tests=" + passed + " virtual-clock-only; performance=Unmeasured");

sealed class VirtualClock : IPsoClock
{
    public double NowMilliseconds { get; private set; }
    public void Advance(double milliseconds) { NowMilliseconds += milliseconds; }
}
sealed class MockBackend : IPsoWarmupBackend
{
    private readonly VirtualClock clock;
    public MockBackend(int total, VirtualClock clock) { TotalStateCount = total; this.clock = clock; }
    public string AdapterId => "test.mock";
    public string RuntimePlatform => "mock";
    public string GraphicsApi => "mock";
    public int TotalStateCount { get; }
    public int CompletedStateCount { get; private set; }
    public bool IsWarmedUp => !ForceNotWarmed && CompletedStateCount >= TotalStateCount;
    public bool PreferNativeAsyncBulkForDeadline => false;
    public List<int> Requests { get; } = new List<int>();
    public int BulkRequests, DisposeCalls, CompleteFailures, BatchDisposeFailures, BackendDisposeFailures;
    public int ProgressPerBatch = -1;
    public double SubmitAdvance;
    public bool FailSubmit, ForceNotWarmed;
    public MockBatch Last;
    public IPsoWarmupBatch Schedule(int maximumStates, bool throughput)
    {
        if (FailSubmit) throw new InvalidOperationException("submit-before-work");
        if (Last != null && !Last.Fenced) throw new Exception("Duplicate in-flight submission.");
        Requests.Add(maximumStates); if (throughput) BulkRequests++;
        clock.Advance(SubmitAdvance);
        return Last = new MockBatch(this, ProgressPerBatch < 0 ? maximumStates : ProgressPerBatch);
    }
    public void Finish() { if (Last != null) Last.Ready = true; }
    public void Dispose()
    {
        if (Last != null && !Last.Fenced) throw new Exception("Backend released before fence.");
        if (BackendDisposeFailures-- > 0) throw new InvalidOperationException("release-failure");
        DisposeCalls++;
    }
    public sealed class MockBatch : IPsoWarmupBatch
    {
        private readonly MockBackend owner; private readonly int progress;
        public bool Ready, Fenced; public int CompleteCalls;
        public MockBatch(MockBackend owner, int progress) { this.owner = owner; this.progress = progress; }
        public bool IsCompleted => Ready || Fenced;
        public void Complete()
        {
            CompleteCalls++;
            if (owner.CompleteFailures-- > 0) throw new InvalidOperationException("unproven-fence");
            if (!Fenced) owner.CompletedStateCount = Math.Min(owner.TotalStateCount, owner.CompletedStateCount + progress);
            Fenced = true;
        }
        public void Dispose()
        {
            if (!Fenced) throw new Exception("Batch disposed before fence.");
            if (owner.BatchDisposeFailures-- > 0) throw new InvalidOperationException("batch-dispose-failure");
        }
    }
}
