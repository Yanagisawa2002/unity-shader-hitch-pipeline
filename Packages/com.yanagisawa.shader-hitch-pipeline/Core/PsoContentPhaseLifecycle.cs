using System;
using System.Collections.Generic;

namespace Yanagisawa.ShaderHitchPipeline
{
    public interface IPsoContentPhaseSink
    {
        // Deferred retains the pending request so dependency-ready hosts can retry after retirement.
        PsoContentPhaseActivation Activate(string phase);
        void Cancel(string phase);
        void Unload(string phase);
        bool IsComplete(string phase);
    }

    public enum PsoContentPhaseActivation { Accepted, Deferred, Unavailable }
    public enum PsoContentPhaseStatus { WaitingForDependencies, Active, Complete, Cancelled, Unloaded, Failed, RenderingCold }

    public sealed class PsoContentPhaseRequest
    {
        internal PsoContentPhaseLifecycle owner;
        internal bool demanded;
        public string ContentId { get; internal set; }
        public string Revision { get; internal set; }
        public string Phase { get; internal set; }
        public long Generation { get; internal set; }
        public PsoContentPhaseStatus Status { get; internal set; }
        public string Failure { get; internal set; }
    }

    /// <summary>Main-thread lifecycle bridge, with no clocks, tracing or GPU calls of its own.
    /// Hosts retain their material/shader assets until their backend has fenced cancellation.
    /// A load-ready event means dependencies exist; it does not prove first-render coverage.</summary>
    public sealed class PsoContentPhaseLifecycle : IDisposable
    {
        private readonly IPsoContentPhaseSink sink;
        private readonly List<PsoContentPhaseRequest> requests = new List<PsoContentPhaseRequest>();
        private readonly Dictionary<string, int> demand = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private long generation;
        private bool disposed;

        public PsoContentPhaseLifecycle(IPsoContentPhaseSink sink)
        { this.sink = sink ?? throw new ArgumentNullException(nameof(sink)); }

        public PsoContentPhaseRequest Request(string contentId, string revision, string phase)
        {
            if (disposed) throw new ObjectDisposedException(nameof(PsoContentPhaseLifecycle));
            if (string.IsNullOrWhiteSpace(contentId) || string.IsNullOrWhiteSpace(revision) || string.IsNullOrWhiteSpace(phase))
                throw new ArgumentException("Content identity, revision and phase are required.");
            // Validate before retiring an old load. Its delayed async completion cannot revive it.
            foreach (var previous in requests.ToArray())
                if (previous.ContentId == contentId && previous.Revision != revision) Unload(previous);
            var request = new PsoContentPhaseRequest { owner = this, ContentId = contentId, Revision = revision,
                Phase = phase, Generation = ++generation, Status = PsoContentPhaseStatus.WaitingForDependencies };
            requests.Add(request);
            return request;
        }

        public bool DependenciesReady(PsoContentPhaseRequest request, bool renderCold = false)
        {
            Validate(request);
            if (request.Status != PsoContentPhaseStatus.WaitingForDependencies) return false;
            if (renderCold) { request.Status = PsoContentPhaseStatus.RenderingCold; return true; }
            demand.TryGetValue(request.Phase, out int count);
            try
            {
                if (count == 0)
                {
                    var activation = sink.Activate(request.Phase);
                    if (activation == PsoContentPhaseActivation.Deferred) return false;
                    if (activation != PsoContentPhaseActivation.Accepted)
                        throw new InvalidOperationException("No available plan phase: " + request.Phase);
                }
                demand[request.Phase] = count + 1;
                request.demanded = true;
                request.Status = PsoContentPhaseStatus.Active;
                return true;
            }
            catch (Exception error)
            {
                request.Failure = error.Message;
                request.Status = PsoContentPhaseStatus.Failed;
                return false;
            }
        }

        public void Refresh()
        {
            if (disposed) throw new ObjectDisposedException(nameof(PsoContentPhaseLifecycle));
            foreach (var request in requests)
            {
                if (request.Status != PsoContentPhaseStatus.Active) continue;
                try { if (sink.IsComplete(request.Phase)) request.Status = PsoContentPhaseStatus.Complete; }
                catch (Exception error) { request.Failure = error.Message; request.Status = PsoContentPhaseStatus.Failed; }
            }
        }

        public void Cancel(PsoContentPhaseRequest request) => Retire(request, PsoContentPhaseStatus.Cancelled);
        public void Unload(PsoContentPhaseRequest request) => Retire(request, PsoContentPhaseStatus.Unloaded);

        private void Retire(PsoContentPhaseRequest request, PsoContentPhaseStatus status)
        {
            Validate(request);
            if (request.Status == PsoContentPhaseStatus.Cancelled || request.Status == PsoContentPhaseStatus.Unloaded) return;
            // Call cancellation before dropping ownership. A throwing/unproven fence remains retryable.
            if (request.demanded)
            {
                int count = demand[request.Phase];
                if (count == 1)
                {
                    if (status == PsoContentPhaseStatus.Unloaded) sink.Unload(request.Phase);
                    else sink.Cancel(request.Phase);
                    demand.Remove(request.Phase);
                }
                else demand[request.Phase] = count - 1;
                request.demanded = false;
            }
            request.Status = status;
            requests.Remove(request);
        }

        private void Validate(PsoContentPhaseRequest request)
        { if (request == null || request.owner != this) throw new ArgumentException("Foreign load request."); }

        public void Dispose()
        {
            if (disposed) return;
            foreach (var request in requests.ToArray()) Unload(request);
            disposed = true;
        }
    }
}
