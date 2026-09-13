using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Yanagisawa.ShaderHitchPipeline
{
    /// <summary>
    /// Process-local synchronization between a real-time scenario and the generic
    /// frame sampler. A scenario owns elapsed time; the sampler must not substitute
    /// a fixed frame count for that wall-clock interval.
    /// </summary>
    public static class PsoBenchmarkMeasurementGate
    {
        private static bool required;
        private static bool open;
        private static bool closed;
        private static bool samplerFinalized;
        private static double openedAt = -1.0;
        private static double closedAt = -1.0;

        public static bool IsRequired => required;
        public static bool IsOpen => open && !closed;
        public static bool IsClosed => required && closed;
        public static bool IsSamplerFinalized => samplerFinalized;
        public static double OpenedAt => openedAt;
        public static double ClosedAt => closedAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            required = false;
            open = false;
            closed = false;
            samplerFinalized = false;
            openedAt = -1.0;
            closedAt = -1.0;
        }

        internal static void Require()
        {
            if (required)
                return;
            required = true;
            open = false;
            closed = false;
            samplerFinalized = false;
            openedAt = -1.0;
            closedAt = -1.0;
        }

        internal static void Open(double realtimeSeconds)
        {
            if (!required)
                Require();
            if (closed)
                throw new InvalidOperationException(
                    "A completed benchmark measurement gate cannot be reopened.");
            if (open)
                return;
            open = true;
            openedAt = realtimeSeconds;
        }

        internal static void Close(double realtimeSeconds)
        {
            if (!required || !open || closed)
                return;
            closed = true;
            closedAt = Math.Max(realtimeSeconds, openedAt);
        }

        internal static void MarkSamplerFinalized()
        {
            samplerFinalized = true;
        }
    }

    public enum PsoDeadlineScenarioMode
    {
        Baseline,
        Throughput,
        Scheduled,
    }

    /// <summary>
    /// Reusable event contract for a predicted content reveal. Scene code owns visuals;
    /// this class owns trace phase transition, request/deadline timing, warmup activation,
    /// and machine-readable markers. Time is supplied by the caller so a presented-frame
    /// freeze naturally holds and then jumps the scene without a synthetic stall.
    /// </summary>
    public sealed class PsoDeadlineScenario
    {
        private readonly string phase;
        private readonly string scenarioTag;
        private readonly double requestDelaySeconds;
        private readonly double deadlineSeconds;
        private readonly int startupTraceFrameCount;
        private readonly Action<string, double> markerSink;

        private int startupTraceFrames;
        private bool tracePhaseStarted;
        private bool armed;
        private bool contentRequested;
        private bool contentRevealed;
        private bool contentCompleted;
        private bool deferredActivated;
        private bool deferredReady;
        private bool deadlineMissed;
        private double workloadStartedAt = -1.0;
        private double contentRequestAt = -1.0;
        private double contentRevealAt = -1.0;
        private double deferredReadyAt = -1.0;

        public PsoDeadlineScenario(
            string phaseName,
            string tag,
            double requestDelay,
            double deadline,
            int traceStartupFrames,
            Action<string, double> emitMarker)
        {
            if (string.IsNullOrWhiteSpace(phaseName))
                throw new ArgumentException("A deferred phase name is required.", nameof(phaseName));
            if (requestDelay < 0.0)
                throw new ArgumentOutOfRangeException(nameof(requestDelay));
            if (deadline <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(deadline));
            if (traceStartupFrames < 1)
                throw new ArgumentOutOfRangeException(nameof(traceStartupFrames));

            phase = phaseName;
            scenarioTag = string.IsNullOrWhiteSpace(tag) ? "scenario" : tag;
            requestDelaySeconds = requestDelay;
            deadlineSeconds = deadline;
            startupTraceFrameCount = traceStartupFrames;
            markerSink = emitMarker;
            Mode = ResolveMode(PsoCommandLine.Current);
            PsoBenchmarkMeasurementGate.Require();
        }

        public PsoDeadlineScenarioMode Mode { get; }
        public string Phase => phase;
        public bool IsArmed => armed;
        public bool IsContentRequested => contentRequested;
        public bool IsContentRevealed => contentRevealed;
        public bool IsContentCompleted => contentCompleted;
        public bool IsDeferredReady => deferredReady;
        public bool DeadlineMissed => deadlineMissed;
        public double WorkloadStartedAt => workloadStartedAt;
        public double ContentRequestAt => contentRequestAt;
        public double ContentRevealAt => contentRevealAt;
        public double DeferredReadyAt => deferredReadyAt;
        public double DeadlineSeconds => deadlineSeconds;

        public bool IsStartupReady
        {
            get
            {
                if (Mode == PsoDeadlineScenarioMode.Baseline)
                    return true;
                PsoWarmupOrchestrator orchestrator = PsoWarmupOrchestrator.Instance;
                return orchestrator == null || orchestrator.IsComplete;
            }
        }

        public void AdvanceTrainingTrace(double realtimeSeconds)
        {
            if (tracePhaseStarted)
                return;

            PsoTraceController trace = PsoTraceController.Instance;
            if (trace == null || !trace.IsTracing ||
                !string.Equals(trace.ActivePhase, "startup", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            startupTraceFrames++;
            if (startupTraceFrames < startupTraceFrameCount)
                return;

            tracePhaseStarted = true;
            trace.BeginPhase(phase, scenarioTag, "deferred", "deadline");
            Emit("TRACE_DEFERRED_BEGIN", realtimeSeconds);
        }

        public void Arm(double realtimeSeconds)
        {
            if (armed)
                return;

            armed = true;
            workloadStartedAt = realtimeSeconds;
            contentRequestAt = realtimeSeconds + requestDelaySeconds;
            contentRevealAt = contentRequestAt + deadlineSeconds;
            PsoBenchmarkMeasurementGate.Open(realtimeSeconds);
            Emit("WORKLOAD_START", realtimeSeconds);
        }

        public void CompleteMeasurement(double realtimeSeconds)
        {
            if (!armed)
                throw new InvalidOperationException(
                    "A deadline scenario cannot finish before it is armed.");
            PsoBenchmarkMeasurementGate.Close(realtimeSeconds);
        }

        public void Tick(double realtimeSeconds)
        {
            if (!armed)
                return;

            if (!contentRequested && realtimeSeconds >= contentRequestAt)
                RequestContent(realtimeSeconds);
            if (deferredActivated && !deferredReady)
                CheckDeferredReady(realtimeSeconds);
            if (!contentRevealed && realtimeSeconds >= contentRevealAt)
                RevealContent(realtimeSeconds);
        }

        public void MarkContentComplete(double realtimeSeconds)
        {
            if (contentCompleted)
                return;
            if (!contentRevealed)
                throw new InvalidOperationException(
                    "Content cannot complete before the reveal deadline.");

            contentCompleted = true;
            Emit("CONTENT_COMPLETE", realtimeSeconds);
        }

        public double SecondsUntilRequest(double realtimeSeconds)
        {
            return Math.Max(0.0, contentRequestAt - realtimeSeconds);
        }

        public double SecondsUntilReveal(double realtimeSeconds)
        {
            return Math.Max(0.0, contentRevealAt - realtimeSeconds);
        }

        private void RequestContent(double realtimeSeconds)
        {
            contentRequested = true;
            Emit("CONTENT_REQUEST", realtimeSeconds);

            if (Mode == PsoDeadlineScenarioMode.Baseline)
                return;

            PsoWarmupOrchestrator orchestrator = PsoWarmupOrchestrator.Instance;
            if (orchestrator == null)
                throw new InvalidOperationException(
                    "A deadline scenario requires a warmup orchestrator outside baseline mode.");
            if (orchestrator.HasFailed)
                throw new InvalidOperationException(
                    "Warmup failed before phase activation: " + orchestrator.Failure);

            deferredActivated = orchestrator.ActivatePhase(phase);
            if (!deferredActivated)
                throw new InvalidOperationException(
                    "The installed plan has no deferred '" + phase + "' phase.");
        }

        private void CheckDeferredReady(double realtimeSeconds)
        {
            PsoWarmupOrchestrator orchestrator = PsoWarmupOrchestrator.Instance;
            if (orchestrator == null ||
                orchestrator.HasFailed ||
                !orchestrator.IsPhaseComplete(phase))
                return;

            deferredReady = true;
            deferredReadyAt = realtimeSeconds;
            Emit("DEFERRED_READY", realtimeSeconds);
        }

        private void RevealContent(double realtimeSeconds)
        {
            contentRevealed = true;
            deadlineMissed = deferredActivated && !deferredReady;
            Emit("CONTENT_REVEAL", realtimeSeconds);
        }

        private void Emit(string marker, double realtimeSeconds)
        {
            markerSink?.Invoke(marker, realtimeSeconds);
        }

        private static PsoDeadlineScenarioMode ResolveMode(PsoCommandLine commandLine)
        {
            string requested = commandLine.GetString(
                PsoConstants.BenchmarkModeArgument,
                commandLine.HasFlag(PsoConstants.DisableWarmupArgument)
                    ? "baseline"
                    : "scheduled");
            if (commandLine.HasFlag(PsoConstants.DisableWarmupArgument) ||
                string.Equals(requested, "baseline", StringComparison.OrdinalIgnoreCase))
            {
                return PsoDeadlineScenarioMode.Baseline;
            }
            if (string.Equals(requested, "naive", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(requested, "throughput", StringComparison.OrdinalIgnoreCase))
            {
                return PsoDeadlineScenarioMode.Throughput;
            }
            return PsoDeadlineScenarioMode.Scheduled;
        }
    }

    public sealed class PsoScenarioMarkerWriter
    {
        private readonly string path;
        private readonly string logScope;
        private readonly StringBuilder pending = new StringBuilder(512);

        public PsoScenarioMarkerWriter(string scope)
        {
            logScope = string.IsNullOrWhiteSpace(scope) ? "Scenario" : scope;
            path = PsoCommandLine.Current.GetString(
                PsoConstants.ScenarioMarkerArgument,
                string.Empty);
        }

        public void Write(string marker, double realtimeSeconds)
        {
            PsoSystemMarkers.Emit(marker, logScope);
            string line = marker + " realtime=" +
                          realtimeSeconds.ToString("F6", CultureInfo.InvariantCulture);
            pending.Append(line).AppendLine();

            // The recorder needs CAPTURE_READY before it starts. All measured
            // markers remain memory-only until shutdown so evidence I/O cannot
            // manufacture a hitch at request or reveal.
            if (string.Equals(marker, "CAPTURE_READY", StringComparison.Ordinal))
                Flush();
        }

        public void Flush()
        {
            if (pending.Length == 0)
                return;

            string payload = pending.ToString();
            Debug.Log(
                "[ShaderHitchPipeline." + logScope + "] " +
                payload.TrimEnd('\r', '\n').Replace(
                    Environment.NewLine,
                    Environment.NewLine + "[ShaderHitchPipeline." + logScope + "] "));
            if (string.IsNullOrWhiteSpace(path))
            {
                pending.Length = 0;
                return;
            }

            try
            {
                string fullPath = Path.GetFullPath(path);
                string directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                File.AppendAllText(
                    fullPath,
                    payload,
                    new UTF8Encoding(false));
                pending.Length = 0;
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "[ShaderHitchPipeline." + logScope +
                    "] Could not write scenario marker: " + exception.Message);
            }
        }
    }
}
