using System;
using System.Collections.Generic;

namespace Yanagisawa.ShaderHitchPipeline
{
    public enum PsoPhaseState { Prepared, Pending, Running, Completed, Cancelled, Unloaded, Faulted }

    public sealed class PsoWarmupBatchRecord
    {
        public int RequestedStates { get; internal set; }
        public int CompletedPermutations { get; internal set; }
        public double ElapsedMilliseconds { get; internal set; }
        public PsoBudgetAdmission Admission { get; internal set; }
        public bool Fenced { get; internal set; }
        public bool NonInteractive { get; internal set; }
        public bool NativeBulk { get; internal set; }
    }

    /// <summary>A distinct activation. Cancelled activations never become completed when their
    /// non-preemptible work eventually finishes. A later activation has a different object/generation.</summary>
    public sealed class PsoPhaseStatus
    {
        internal PsoWarmupScheduler.Resident resident;
        internal readonly List<PsoWarmupBatchRecord> batches = new List<PsoWarmupBatchRecord>();
        internal double started, ended;
        internal int waitingFrames, noProgressBatches;
        public string Phase { get; internal set; }
        public int Activation { get; internal set; }
        public PsoPhaseState State { get; internal set; }
        public string Failure { get; internal set; } = string.Empty;
        public string LastAdmissionReason { get; internal set; } = "not-evaluated";
        public bool HasInFlightBatch { get; internal set; }
        public bool BackendReportedWarmedUp { get; internal set; }
        public int CompletedPermutations { get; internal set; }
        public int TotalGraphicsStates { get; internal set; }
        public double ElapsedMilliseconds { get; internal set; }
        public double MinimumSlackMilliseconds { get; internal set; } = double.PositiveInfinity;
        public bool DeadlineMissed { get; internal set; }
        public bool DeadlineFeasible { get; internal set; } = true;
        public int DeferredFrames { get; internal set; }
        public int DeadlineInfeasibleFrames { get; internal set; }
        public int SelectionCount { get; internal set; }
        public PsoAdaptiveBatchPolicy Policy => resident.policy;
        public bool RequiresNonInteractiveWindow => Policy.RequiresNonInteractiveWindow;
        public IReadOnlyList<PsoWarmupBatchRecord> Batches => batches;
        public bool IsComplete => State == PsoPhaseState.Completed;
        public bool IsTerminal => State == PsoPhaseState.Completed || State == PsoPhaseState.Cancelled ||
            State == PsoPhaseState.Unloaded || State == PsoPhaseState.Faulted;
    }

    /// <summary>Main-thread, clock-injected scheduler used by the Unity orchestrator and deterministic tests.
    /// Tick submits at most one job; cancellation never preempts a job, and disposal never releases its
    /// backend before a successful Complete fence. No timing source or native warmup is owned by this class.</summary>
    public sealed class PsoWarmupScheduler : IDisposable
    {
        internal sealed class Resident
        {
            internal PsoWarmupPhasePlan plan;
            internal IPsoWarmupBackend backend;
            internal PsoAdaptiveBatchPolicy policy;
            internal PsoPhaseStatus current;
            internal bool unload, nativeBulk;
        }
        private readonly IPsoClock clock;
        private readonly PsoSchedulingOptions options;
        private readonly Dictionary<string, Resident> residents = new Dictionary<string, Resident>(StringComparer.OrdinalIgnoreCase);
        // Feedback identities survive release/re-registration of a resident within this loaded plan.
        private readonly Dictionary<string, int> activationGenerations = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly List<PsoPhaseStatus> runs = new List<PsoPhaseStatus>();
        private readonly List<PsoPhaseStatus> runnable = new List<PsoPhaseStatus>();
        private readonly List<PsoPhaseStatus> pending = new List<PsoPhaseStatus>();
        private readonly List<PsoSchedulerCandidate> candidates = new List<PsoSchedulerCandidate>();
        private readonly List<PsoBudgetAdmission> admissions = new List<PsoBudgetAdmission>();
        private readonly List<DeadlineDemand> deadlineDemands = new List<DeadlineDemand>();
        private readonly struct DeadlineDemand : IComparable<DeadlineDemand>
        {
            internal readonly int index;
            internal readonly double deadline, cost;
            internal DeadlineDemand(int index, double deadline, double cost)
            { this.index = index; this.deadline = deadline; this.cost = cost; }
            public int CompareTo(DeadlineDemand other)
            {
                int result = deadline.CompareTo(other.deadline);
                return result == 0 ? index.CompareTo(other.index) : result;
            }
        }
        private PsoPhaseStatus active;
        private IPsoWarmupBatch batch;
        private PsoWarmupBatchRecord activeRecord;
        private bool fenced, closing;
        private bool hasFailures, unloadedIncompleteDemand;
        private int before, costGeneration;
        private double submittedAt, lastNow;
        private string identity;

        public PsoWarmupScheduler(IPsoClock clock, PsoSchedulingOptions options = null, string costIdentity = "")
        {
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.options = (options ?? new PsoSchedulingOptions()).Snapshot();
            identity = costIdentity ?? string.Empty;
            Now();
        }
        public IReadOnlyList<PsoPhaseStatus> Activations => runs;
        public PsoPhaseStatus Active => active;
        public bool HasInFlightBatch => batch != null;
        public bool HasPendingRetirement
        {
            get { foreach (var resident in residents.Values) if (resident.unload && resident.backend != null) return true; return false; }
        }
        public bool HasFailed => hasFailures;
        public int PendingCount { get { int count = 0; foreach (var r in residents.Values) if (r.current != null && !r.current.IsTerminal) count++; return count; } }
        public bool IsBusy => HasInFlightBatch || PendingCount > 0;
        public bool IsComplete
        {
            get
            {
                if (IsBusy || runs.Count == 0) return false;
                // Cancellation/unload/failure is not successful warmup. A later successful
                // activation of the same phase can satisfy current demand; history is retained.
                foreach (var r in residents.Values) if (r.current != null && !r.current.IsComplete) return false;
                return !HasFailed && !unloadedIncompleteDemand;
            }
        }

        private double Now()
        {
            double value = clock.NowMilliseconds;
            if (!PsoSchedulingOptions.Finite(value) || value < lastNow)
                throw new InvalidOperationException("The scheduler requires a finite monotonic clock.");
            return lastNow = value;
        }
        private void CheckOpen() { if (closing) throw new ObjectDisposedException(nameof(PsoWarmupScheduler)); }

        public void Register(PsoWarmupPhasePlan phase, IPsoWarmupBackend backend, PsoAdaptiveBatchPolicy policy,
            bool nativeAsyncBulk = false)
        {
            CheckOpen();
            if (phase == null || backend == null || policy == null) throw new ArgumentNullException(nameof(phase));
            if (string.IsNullOrWhiteSpace(phase.phase)) throw new ArgumentException("A phase name is required.");
            if (options.FixedBatchSize > 0 && (nativeAsyncBulk || backend.PreferNativeAsyncBulkForDeadline))
                throw new NotSupportedException("fixed-progressive cannot be replaced by native bulk.");
            if (!PsoSchedulingOptions.Finite(phase.deadlineMilliseconds) || phase.deadlineMilliseconds < 0 ||
                !PsoSchedulingOptions.Finite(phase.targetFrameMilliseconds) || phase.targetFrameMilliseconds <= 0 ||
                !PsoSchedulingOptions.Finite(phase.expectedUseProbability) || phase.expectedUseProbability < 0 || phase.expectedUseProbability > 1 ||
                backend.TotalStateCount < 0 || backend.CompletedStateCount < 0)
                throw new ArgumentException("Invalid scheduling fields or backend progress.");
            if (residents.ContainsKey(phase.phase)) throw new InvalidOperationException("Phase is still resident: " + phase.phase);
            // Snapshot only scheduling fields; loading, compatibility and trace coverage belong to the adapter.
            var copy = new PsoWarmupPhasePlan { phase = phase.phase, priority = phase.priority, hotSetTier = phase.hotSetTier,
                deadlineMilliseconds = phase.deadlineMilliseconds, targetFrameMilliseconds = phase.targetFrameMilliseconds,
                expectedUseProbability = phase.expectedUseProbability };
            policy.UpdateCostContext(identity, Now(), options.MaximumCostAgeMilliseconds);
            residents.Add(copy.phase, new Resident { plan = copy, backend = backend, policy = policy, nativeBulk = nativeAsyncBulk });
            int count = residents.Count;
            pending.Capacity = Math.Max(pending.Capacity, count);
            candidates.Capacity = Math.Max(candidates.Capacity, count);
            admissions.Capacity = Math.Max(admissions.Capacity, count);
            deadlineDemands.Capacity = Math.Max(deadlineDemands.Capacity, count);
        }

        public PsoPhaseStatus GetPhaseStatus(string phase)
        {
            if (phase == null) return null;
            if (residents.TryGetValue(phase, out var resident)) return resident.current;
            for (int i = runs.Count - 1; i >= 0; i--)
                if (string.Equals(runs[i].Phase, phase, StringComparison.OrdinalIgnoreCase)) return runs[i];
            return null;
        }
        public bool IsResident(string phase) => residents.ContainsKey(phase);
        public bool IsUnloading(string phase) => residents.TryGetValue(phase, out var resident) && resident.unload;

        public PsoPhaseStatus Activate(string phase)
        {
            CheckOpen();
            if (!residents.TryGetValue(phase, out var resident) || resident.unload)
                throw new InvalidOperationException("Phase must be loaded and its previous unload fenced before activation: " + phase);
            if (resident.current != null && (!resident.current.IsTerminal || resident.current.IsComplete)) return resident.current;
            if (resident.current?.State == PsoPhaseState.Faulted) throw new InvalidOperationException("Unload and reload the failed phase before retrying.");
            activationGenerations.TryGetValue(phase, out int generation);
            generation = checked(generation + 1);
            var run = new PsoPhaseStatus { resident = resident, Phase = phase, Activation = generation,
                State = PsoPhaseState.Pending, started = Now(), TotalGraphicsStates = resident.backend.TotalStateCount };
            activationGenerations[phase] = generation;
            resident.current = run;
            runs.Add(run);
            runnable.Add(run);
            Refresh(run, lastNow);
            return run;
        }

        public bool CancelPhase(string phase)
        {
            if (string.IsNullOrWhiteSpace(phase)) return false;
            if (!residents.TryGetValue(phase, out var resident) || resident.current == null || resident.current.IsTerminal) return false;
            Stop(resident.current, PsoPhaseState.Cancelled, "cancelled-by-caller", Now());
            return true;
        }
        public bool UnloadPhase(string phase)
        {
            if (string.IsNullOrWhiteSpace(phase)) return false;
            if (!residents.TryGetValue(phase, out var resident)) return false;
            resident.unload = true;
            unloadedIncompleteDemand |= resident.current != null && !resident.current.IsComplete;
            if (resident.current != null && !resident.current.IsTerminal)
                Stop(resident.current, PsoPhaseState.Unloaded, "unloaded-by-caller", Now());
            ReleaseIfRetired(resident);
            return true;
        }

        public void InvalidateCostIdentity(string currentIdentity)
        {
            identity = currentIdentity ?? string.Empty;
            double now = Now();
            foreach (var resident in residents.Values)
                resident.policy.UpdateCostContext(identity, now, options.MaximumCostAgeMilliseconds);
        }

        public void Tick(double frameMilliseconds, double nonInteractiveBudgetMilliseconds = 0, bool throughput = false)
        {
            CheckOpen();
            if (!PsoSchedulingOptions.Finite(frameMilliseconds) || frameMilliseconds < 0 ||
                !PsoSchedulingOptions.Finite(nonInteractiveBudgetMilliseconds) || nonInteractiveBudgetMilliseconds < 0)
                throw new ArgumentOutOfRangeException(nameof(frameMilliseconds));
            double now = Now();
            foreach (var resident in residents.Values)
            {
                if (resident.current != null) Refresh(resident.current, now);
                if (resident.current?.State == PsoPhaseState.Pending)
                    resident.current.waitingFrames = Math.Min(int.MaxValue - 1, resident.current.waitingFrames + 1);
                resident.policy.UpdateCostContext(identity, now, options.MaximumCostAgeMilliseconds);
                // Even fixed counts observe pressure for safety; adaptive cost fitting is separately switchable.
                if ((resident.current != null && !resident.current.IsTerminal) || active?.resident == resident)
                    resident.policy.ObserveFrame(frameMilliseconds, active?.resident == resident, activeRecord?.RequestedStates ?? 0);
            }
            if (batch != null && !Poll()) return;
            now = Now();
            pending.Clear(); candidates.Clear(); admissions.Clear();
            double riskWindow = 0;
            // Activation order is stable, independent of dictionary iteration order and alphabetical names.
            for (int index = 0; index < runnable.Count;)
            {
                var run = runnable[index];
                if (run.State != PsoPhaseState.Pending) { index++; continue; }
                var resident = run.resident;
                Refresh(run, now);
                if (run.BackendReportedWarmedUp) { Stop(run, PsoPhaseState.Completed, string.Empty, now); continue; }
                index++;
                int remaining = Math.Max(1, resident.backend.TotalStateCount - resident.backend.CompletedStateCount);
                double deadline = options.EnableDeadlines && resident.plan.deadlineMilliseconds > 0
                    ? resident.plan.deadlineMilliseconds - run.ElapsedMilliseconds : double.PositiveInfinity;
                var admission = resident.policy.Preview(remaining, deadline,
                    options.EnableHotSetPriority && resident.plan.hotSetTier == 0, options.FixedBatchSize, nonInteractiveBudgetMilliseconds);
                bool bulk = throughput || resident.nativeBulk;
                if (throughput)
                    admission = new PsoBudgetAdmission(remaining, remaining, remaining, 0, 0, true, false, "throughput-control");
                else if (bulk)
                {
                    // No division by worker count: an opaque backend offers no linear scaling contract.
                    double predicted = resident.policy.PredictBatchMilliseconds(remaining);
                    bool fits = admission.IsAdmitted && predicted <= admission.AvailableBudgetMilliseconds;
                    admission = new PsoBudgetAdmission(fits ? remaining : 0, fits ? remaining : 0,
                        admission.DeadlineBatchSize, predicted, admission.AvailableBudgetMilliseconds,
                        predicted <= deadline && fits, false, fits ? "native-bulk-within-estimated-budget" : "native-bulk-requires-explicit-window");
                }
                run.LastAdmissionReason = admission.Reason;
                run.DeadlineFeasible = admission.DeadlineFeasible;
                if (!admission.IsAdmitted) { run.DeferredFrames++; resident.policy.CommitAdmission(admission); }
                double predictedRemaining = options.ConservativeAdmission
                    ? PredictRemaining(resident.policy, remaining, Math.Max(1, admission.BatchSize), resident.plan.targetFrameMilliseconds)
                    : remaining * resident.policy.EstimatedMillisecondsPerState;
                pending.Add(run); admissions.Add(admission);
                candidates.Add(new PsoSchedulerCandidate(run.Phase, remaining, resident.plan.priority, resident.plan.hotSetTier,
                    resident.plan.deadlineMilliseconds, run.ElapsedMilliseconds, resident.policy.EstimatedMillisecondsPerState,
                    resident.plan.expectedUseProbability, admission.IsAdmitted, run.waitingFrames, predictedRemaining));
                riskWindow = Math.Max(riskWindow, resident.plan.targetFrameMilliseconds * 2);
            }
            if (options.ConservativeAdmission && options.EnableDeadlines) AnnotateDeadlineContention();
            foreach (var run in pending) if (!run.DeadlineFeasible) run.DeadlineInfeasibleFrames++;
            var decision = PsoDeadlineCostScheduler.SelectNext(candidates, riskWindow, options);
            if (!decision.IsValid) return;
            var selected = pending[decision.CandidateIndex];
            selected.SelectionCount++;
            selected.MinimumSlackMilliseconds = Math.Min(selected.MinimumSlackMilliseconds, decision.SlackMilliseconds);
            Submit(selected, admissions[decision.CandidateIndex], throughput || selected.resident.nativeBulk,
                nonInteractiveBudgetMilliseconds > 0);
        }

        private void AnnotateDeadlineContention()
        {
            deadlineDemands.Clear();
            for (int i = 0; i < pending.Count; i++)
            {
                var candidate = candidates[i];
                if (candidate.DeadlineMilliseconds <= 0) continue;
                deadlineDemands.Add(new DeadlineDemand(i, candidate.DeadlineMilliseconds - candidate.ElapsedMilliseconds,
                    candidate.PredictedRemainingMilliseconds));
            }
            deadlineDemands.Sort();
            double demand = 0;
            for (int start = 0; start < deadlineDemands.Count;)
            {
                double deadline = deadlineDemands[start].deadline;
                int end = start;
                do { demand += deadlineDemands[end++].cost; }
                while (end < deadlineDemands.Count && deadlineDemands[end].deadline == deadline);
                // One fence owns the queue: per-phase feasibility ignores competing demand.
                // Equal-deadline phases see the same inclusive prefix, independent of activation order.
                if (demand > deadline)
                {
                    for (int j = start; j < end; j++)
                    {
                        int i = deadlineDemands[j].index;
                        var run = pending[i];
                        run.DeadlineFeasible = false;
                        var admission = admissions[i];
                        if (!admission.IsAdmitted) continue;
                        admissions[i] = new PsoBudgetAdmission(admission.BatchSize, admission.SafeBatchSize,
                            admission.DeadlineBatchSize, admission.PredictedBatchMilliseconds, admission.AvailableBudgetMilliseconds,
                            false, admission.Calibration, "deadline-contention-within-budget");
                        run.LastAdmissionReason = admissions[i].Reason;
                    }
                }
                start = end;
            }
        }

        private static double PredictRemaining(PsoAdaptiveBatchPolicy policy, int remaining, int size, double frame)
        {
            int count = (int)Math.Ceiling(remaining / (double)size);
            int tail = remaining - (count - 1) * size;
            return Math.Min(double.MaxValue, (count - 1) * Math.Max(frame, policy.PredictBatchMilliseconds(size)) +
                policy.PredictBatchMilliseconds(tail));
        }

        private void Submit(PsoPhaseStatus run, PsoBudgetAdmission admission, bool bulk, bool nonInteractive)
        {
            // Allocate bookkeeping before handing ownership to an opaque submit call.
            var record = new PsoWarmupBatchRecord { RequestedStates = admission.BatchSize, Admission = admission,
                NonInteractive = nonInteractive, NativeBulk = bulk };
            run.batches.Add(record);
            before = run.resident.backend.CompletedStateCount;
            submittedAt = Now(); // Includes synchronous dispatch work, not only the subsequent polling interval.
            costGeneration = run.Policy.CostGeneration;
            active = run; activeRecord = record; fenced = false;
            run.Policy.CommitAdmission(admission);
            try
            {
                batch = run.resident.backend.Schedule(admission.BatchSize, bulk);
                if (batch == null) throw new InvalidOperationException("Warmup submit returned no owning fence.");
                run.HasInFlightBatch = true;
                run.State = PsoPhaseState.Running;
                run.waitingFrames = 0;
            }
            catch (Exception exception)
            {
                Fault(run, exception);
                active = null; activeRecord = null;
            }
        }

        /// <summary>Only this explicit startup gate may block on Complete before IsCompleted.
        /// Its latency is recorded and has no hard time bound.</summary>
        public void CompleteStartupGate(string phase)
        {
            CheckOpen();
            if (batch != null) throw new InvalidOperationException("Another opaque job still owns the scheduler.");
            var run = GetPhaseStatus(phase) ?? throw new ArgumentException("Phase is not active.");
            if (run.State != PsoPhaseState.Pending) return;
            Refresh(run, Now());
            if (run.BackendReportedWarmedUp) { Stop(run, PsoPhaseState.Completed, string.Empty, lastNow); return; }
            int remaining = Math.Max(1, run.resident.backend.TotalStateCount - run.resident.backend.CompletedStateCount);
            Submit(run, new PsoBudgetAdmission(remaining, 0, 0, 0, 0, false, false,
                "preinteractive-required-hot-set-gate"), true, true);
            if (batch != null && Poll(true) && !run.IsComplete && !run.IsTerminal)
                Fault(run, new InvalidOperationException("Startup gate fenced without warming the complete collection."));
        }

        /// <summary>Nonblocking completion pump. A throwing Complete is not a proven fence;
        /// retain its owner and allow a later Pump/Drain to retry without submitting duplicate work.</summary>
        public bool Pump()
        {
            Now();
            bool done = Poll();
            foreach (var resident in new List<Resident>(residents.Values))
                try { ReleaseIfRetired(resident); } catch { done = false; }
            return done && !HasPendingRetirement;
        }
        private bool Poll(bool block = false)
        {
            if (batch == null) return true;
            var run = active;
            try
            {
                if (!fenced)
                {
                    if (!block && !batch.IsCompleted) return false;
                    batch.Complete();
                    fenced = true;
                    activeRecord.Fenced = true;
                    activeRecord.ElapsedMilliseconds = Math.Max(0, Now() - submittedAt);
                    Refresh(run, lastNow);
                    int delta = run.CompletedPermutations - before;
                    activeRecord.CompletedPermutations = Math.Max(0, delta);
                    if (delta < 0) throw new InvalidOperationException("Backend progress regressed within a resident collection.");
                    if (options.EnableAdaptiveCost && run.Policy.CostGeneration == costGeneration && !activeRecord.NativeBulk)
                        run.Policy.ObserveBatch(delta, activeRecord.ElapsedMilliseconds);
                    if (delta == 0 && !run.BackendReportedWarmedUp && !run.IsTerminal)
                    {
                        run.noProgressBatches++;
                        if (run.noProgressBatches >= options.MaximumNoProgressBatches)
                            throw new InvalidOperationException("Warmup fenced without progress; dependencies may be missing.");
                    }
                    else run.noProgressBatches = 0;
                }
                batch.Dispose();
                batch = null;
                run.HasInFlightBatch = false;
                active = null; activeRecord = null;
                if (!run.IsTerminal)
                {
                    if (run.BackendReportedWarmedUp) Stop(run, PsoPhaseState.Completed, string.Empty, Now());
                    else run.State = PsoPhaseState.Pending;
                }
                ReleaseIfRetired(run.resident);
                return true;
            }
            catch (Exception exception)
            {
                Fault(run, exception);
                return false;
            }
        }

        private void Fault(PsoPhaseStatus run, Exception exception)
        {
            hasFailures = true;
            Stop(run, PsoPhaseState.Faulted, exception.Message, Now());
            if (run.resident.current != run && !run.resident.current.IsTerminal)
                Stop(run.resident.current, PsoPhaseState.Faulted, exception.Message, lastNow);
        }
        private void Refresh(PsoPhaseStatus run, double now)
        {
            if (run.resident.backend != null)
            {
                run.CompletedPermutations = run.resident.backend.CompletedStateCount;
                run.BackendReportedWarmedUp = run.resident.backend.IsWarmedUp;
            }
            run.ElapsedMilliseconds = Math.Max(0, (run.IsTerminal ? run.ended : now) - run.started);
            run.DeadlineMissed = run.resident.plan.deadlineMilliseconds > 0 &&
                run.ElapsedMilliseconds > run.resident.plan.deadlineMilliseconds;
        }
        private void Stop(PsoPhaseStatus run, PsoPhaseState state, string reason, double now)
        {
            runnable.Remove(run);
            run.State = state; run.ended = now;
            if (reason.Length > 0) run.Failure = reason;
            Refresh(run, now);
        }
        private void ReleaseIfRetired(Resident resident)
        {
            if (!resident.unload || active?.resident == resident) return;
            resident.backend?.Dispose();
            resident.backend = null;
            residents.Remove(resident.plan.phase);
        }

        public void Dispose()
        {
            closing = true;
            // Continue independent releases even if one adapter throws. An unfenced owner remains reachable.
            var errors = new List<Exception>();
            foreach (var resident in new List<Resident>(residents.Values))
                try { UnloadPhase(resident.plan.phase); } catch (Exception exception) { errors.Add(exception); }
            try { Pump(); } catch (Exception exception) { errors.Add(exception); }
            if (errors.Count > 0) throw new AggregateException(errors);
        }
        /// <summary>Explicit blocking shutdown. If a fence fails, Dispose/Drain can be retried;
        /// the backend remains retained. Interactive cancellation uses Pump instead.</summary>
        public bool Drain()
        {
            Now();
            return Poll(true) && Pump();
        }
    }
}
