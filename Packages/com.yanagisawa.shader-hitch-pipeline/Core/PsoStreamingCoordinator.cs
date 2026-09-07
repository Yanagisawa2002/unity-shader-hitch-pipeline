using System;
using System.Collections.Generic;
using System.Threading;

namespace Yanagisawa.ShaderHitchPipeline
{
    /// <summary>A loaded resource reference. Each owner acquires a lease; the original caller releases its reference.</summary>
    public sealed class PsoRetainedAsset : IDisposable
    {
        private Action release;
        private int references = 1;
        private bool disposed;
        public PsoRetainedAsset(Action release) { this.release = release ?? throw new ArgumentNullException(nameof(release)); }
        public int ReferenceCount => references;
        internal void Acquire() { if (references == 0) throw new ObjectDisposedException(nameof(PsoRetainedAsset)); checked { references++; } }
        internal void Release() { if (--references == 0) { var action = release; release = null; action(); } }
        public void Dispose() { if (!disposed) { disposed = true; Release(); } }
    }

    public sealed class PsoStreamingState
    {
        public string Key { get; }
        public object Payload { get; }
        public PsoStreamingState(string key, object payload)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("A canonical adapter state key is required.", nameof(key));
            Key = key; Payload = payload;
        }
    }

    /// <summary>Submit must either return a fence owning ALL submitted work or throw BEFORE submission.
    /// Complete must fence before reporting failure; throwing without a fence retains resources and is retried by Drain.</summary>
    public interface IPsoStreamingBackend
    {
        IPsoWarmupBatch Submit(IReadOnlyList<PsoStreamingState> states);
    }

    /// <summary>Optional outcome after Complete has successfully fenced the operation. A failed operation
    /// with a proven fence can release resources; a throwing/unproven fence cannot.</summary>
    public interface IPsoStreamingBatchResult
    {
        string Failure { get; }
    }

    public sealed class PsoStreamOwner
    {
        internal PsoStreamingCoordinator coordinator;
        internal Dictionary<string, List<PsoStreamingCoordinator.State>> phases;
        internal PsoRetainedAsset asset;
        internal int pins;
        internal bool released;
        public string ContentId { get; internal set; }
        public string ContentRevision { get; internal set; }
        public bool IsUnloaded { get; internal set; }
    }

    public sealed class PsoStreamRequest : IDisposable
    {
        internal PsoStreamingCoordinator coordinator;
        internal PsoStreamOwner owner;
        internal List<PsoStreamingCoordinator.State> states;
        public bool IsCancelled { get; internal set; }
        public string Failure { get; internal set; }
        public bool IsComplete
        {
            get
            {
                if (IsCancelled || Failure != null) return false;
                foreach (var state in states) if (!state.complete) return false;
                return true;
            }
        }
        public void Dispose() { coordinator.Cancel(this); }
    }

    /// <summary>Main-thread, explicitly pumped streaming lifecycle. Concurrent phase requests are unioned;
    /// this is not a concurrent-call API. No driver job can be preempted. Tick admits at most one opaque job.
    /// State completion is remembered only while an owner or submitted job retains that state.</summary>
    public sealed class PsoStreamingCoordinator : IDisposable
    {
        internal sealed class State
        {
            internal string key;
            internal PsoStreamingState value;
            internal readonly HashSet<PsoStreamOwner> owners = new HashSet<PsoStreamOwner>();
            internal int demand;
            internal bool complete, submitted;
            internal string failure;
        }
        private readonly IPsoStreamingBackend backend;
        private readonly int thread = Thread.CurrentThread.ManagedThreadId;
        private readonly Dictionary<string, State> states = new Dictionary<string, State>(StringComparer.Ordinal);
        private readonly List<PsoStreamOwner> owners = new List<PsoStreamOwner>();
        private readonly List<PsoStreamRequest> requests = new List<PsoStreamRequest>();
        private readonly List<State> flight = new List<State>();
        private readonly HashSet<PsoStreamOwner> pins = new HashSet<PsoStreamOwner>();
        private IPsoWarmupBatch batch;
        private bool disposed;
        public int RetainedStateCount => states.Count;
        public int SubmittedStateCount { get; private set; }
        public int SubmittedBatchCount { get; private set; }
        public bool HasSubmittedWork => batch != null;
        public PsoStreamingCoordinator(IPsoStreamingBackend backend) { this.backend = backend ?? throw new ArgumentNullException(nameof(backend)); }
        private void Check() { if (thread != Thread.CurrentThread.ManagedThreadId) throw new InvalidOperationException("Streaming lifecycle must run on its creating thread."); }
        private void Active() { Check(); if (disposed) throw new ObjectDisposedException(nameof(PsoStreamingCoordinator)); }
        private static string Part(string value) => value.Length + ":" + value;

        /// <summary>Call only after loading all shader/material dependencies and attesting compatibility.
        /// A different revision retires existing owners of this content ID, including pending requests.
        /// compatibilityIdentity is an attested shader/build namespace, never merely an artifact hash.</summary>
        public PsoStreamOwner RegisterLoaded(string contentId, string contentRevision, string compatibilityIdentity,
            IReadOnlyDictionary<string, IReadOnlyList<PsoStreamingState>> phases, PsoRetainedAsset asset)
        {
            Active();
            if (string.IsNullOrWhiteSpace(contentId) || string.IsNullOrWhiteSpace(contentRevision) || string.IsNullOrWhiteSpace(compatibilityIdentity))
                throw new ArgumentException("Known content, revision and compatibility identities are required.");
            if (phases == null || asset == null) throw new ArgumentNullException(phases == null ? nameof(phases) : nameof(asset));
            // Validate and snapshot everything before retiring the previous revision.
            var prepared = new Dictionary<string, List<PsoStreamingState>>(StringComparer.Ordinal);
            foreach (var phase in phases)
            {
                if (string.IsNullOrWhiteSpace(phase.Key) || phase.Value == null) throw new ArgumentException("Invalid phase.");
                var copy = new List<PsoStreamingState>();
                foreach (var state in phase.Value) { if (state == null) throw new ArgumentException("Null state."); copy.Add(state); }
                prepared.Add(phase.Key, copy);
            }
            asset.Acquire();
            var owner = new PsoStreamOwner { coordinator = this, ContentId = contentId, ContentRevision = contentRevision,
                phases = new Dictionary<string, List<State>>(StringComparer.Ordinal), asset = asset };
            foreach (var phase in prepared)
            {
                var list = new List<State>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var value in phase.Value)
                {
                    string key = Part(compatibilityIdentity) + Part(contentRevision) + Part(value.Key);
                    if (!seen.Add(key)) continue;
                    if (!states.TryGetValue(key, out var state)) states.Add(key, state = new State { key = key, value = value });
                    state.owners.Add(owner); list.Add(state);
                }
                owner.phases.Add(phase.Key, list);
            }
            // Attach the replacement first: a retained shader shared across revisions cannot transiently unload.
            owners.Add(owner);
            try
            {
                foreach (var previous in owners.ToArray())
                    if (previous.ContentId == contentId && previous.ContentRevision != contentRevision) Unload(previous);
            }
            catch
            {
                // A faulty old asset release must not leave an unreachable new owner installed.
                Unload(owner); throw;
            }
            return owner;
        }

        public PsoStreamRequest RequestPhase(PsoStreamOwner owner, string phase)
        {
            Active(); ValidateOwner(owner);
            if (owner.IsUnloaded) throw new InvalidOperationException("The content handle is stale or unloaded.");
            if (!owner.phases.TryGetValue(phase, out var list)) throw new ArgumentException("Unknown phase: " + phase);
            var request = new PsoStreamRequest { coordinator = this, owner = owner, states = list };
            foreach (var state in list) { state.demand++; if (state.failure != null) request.Failure = state.failure; }
            requests.Add(request); return request;
        }
        private void ValidateOwner(PsoStreamOwner owner)
        {
            if (owner == null || owner.coordinator != this) throw new ArgumentException("Foreign content handle.");
        }
        internal void Cancel(PsoStreamRequest request)
        {
            Check(); if (request.IsCancelled) return;
            request.IsCancelled = true;
            foreach (var state in request.states) state.demand--;
            requests.Remove(request);
        }
        public void Unload(PsoStreamOwner owner)
        {
            Check(); ValidateOwner(owner); if (owner.IsUnloaded) return;
            owner.IsUnloaded = true;
            foreach (var request in requests.ToArray()) if (request.owner == owner) Cancel(request);
            foreach (var phase in owner.phases.Values) foreach (var state in phase) state.owners.Remove(owner);
            owners.Remove(owner); ReleaseOwner(owner); Prune();
        }
        private static void ReleaseOwner(PsoStreamOwner owner)
        {
            if (!owner.IsUnloaded || owner.pins != 0 || owner.released) return;
            owner.released = true; owner.phases.Clear(); owner.asset.Release();
        }
        private void Prune()
        {
            var dead = new List<string>();
            foreach (var pair in states) if (pair.Value.owners.Count == 0 && !pair.Value.submitted) dead.Add(pair.Key);
            foreach (var key in dead) states.Remove(key);
        }

        /// <summary>Pumps completion and submits a bounded subset. The count bounds work, not wall-clock time.</summary>
        public void Tick(int maximumStates = 64)
        {
            Active(); if (maximumStates < 1) throw new ArgumentOutOfRangeException(nameof(maximumStates));
            if (batch != null) { if (!batch.IsCompleted) return; FinishBatch(); }
            var values = new List<PsoStreamingState>();
            foreach (var state in states.Values)
            {
                if (state.demand == 0 || state.complete || state.failure != null) continue;
                flight.Add(state); values.Add(state.value);
                foreach (var owner in state.owners) if (pins.Add(owner)) owner.pins++;
                if (values.Count == maximumStates) break;
            }
            if (values.Count == 0) return;
            try
            {
                batch = backend.Submit(values) ?? throw new InvalidOperationException("Backend returned no completion fence.");
                foreach (var state in flight) state.submitted = true;
                SubmittedStateCount += values.Count; SubmittedBatchCount++;
            }
            catch (Exception error)
            {
                FailFlight(error.Message); ReleaseFlight();
            }
        }
        private void FailFlight(string error)
        {
            foreach (var state in flight) state.failure = error;
            foreach (var request in requests) foreach (var state in request.states)
                if (state.failure != null) request.Failure = state.failure;
        }
        private void FinishBatch()
        {
            // A throwing fence is not proof of quiescence. Preserve pins and retry on Drain/Tick.
            try { batch.Complete(); }
            catch (Exception error) { FailFlight(error.Message); throw; }
            var result = batch as IPsoStreamingBatchResult;
            if (result != null && result.Failure != null) FailFlight(result.Failure);
            foreach (var state in flight) { state.submitted = false; state.complete = state.failure == null; }
            var finished = batch; batch = null;
            try { finished.Dispose(); }
            finally { ReleaseFlight(); }
        }
        private void ReleaseFlight()
        {
            flight.Clear();
            var release = new List<PsoStreamOwner>(pins); pins.Clear();
            var errors = new List<Exception>();
            foreach (var owner in release)
            {
                owner.pins--;
                try { ReleaseOwner(owner); } catch (Exception error) { errors.Add(error); }
            }
            Prune();
            if (errors.Count != 0) throw new AggregateException("Streaming asset release failed after the job fence.", errors);
        }
        /// <summary>Blocking shutdown fence. Does not submit queued work. If the fence throws, resources remain retained.</summary>
        public void Drain() { Check(); if (batch != null) FinishBatch(); }
        public void Dispose()
        {
            Check(); if (disposed) return;
            var errors = new List<Exception>();
            foreach (var owner in owners.ToArray())
                try { Unload(owner); } catch (Exception error) { errors.Add(error); }
            try { Drain(); } catch (Exception error) { errors.Add(error); }
            if (errors.Count != 0) throw new AggregateException("Streaming shutdown did not finish cleanly.", errors);
            disposed = true;
        }
    }
}
