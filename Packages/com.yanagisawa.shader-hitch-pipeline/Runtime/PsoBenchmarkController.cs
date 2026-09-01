using System;
using System.Collections.Generic;
using System.IO;
using Unity.Profiling;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Yanagisawa.ShaderHitchPipeline
{
    [DefaultExecutionOrder(31000)]
    [DisallowMultipleComponent]
    public sealed class PsoBenchmarkController : MonoBehaviour
    {
        private sealed class MarkerAccumulator : IDisposable
        {
            public readonly string name;
            public ProfilerRecorder recorder;
            public long sampledFrames;
            public double totalMilliseconds;
            public double maximumFrameMilliseconds;

            public MarkerAccumulator(string markerName)
            {
                name = markerName;
                try
                {
                    recorder = ProfilerRecorder.StartNew(
                        ProfilerCategory.Render,
                        markerName,
                        1);
                }
                catch (Exception)
                {
                    recorder = default(ProfilerRecorder);
                }
            }

            public void Sample()
            {
                if (!recorder.Valid)
                    return;

                double milliseconds = recorder.LastValue * 1e-6;
                totalMilliseconds += milliseconds;
                maximumFrameMilliseconds = Math.Max(maximumFrameMilliseconds, milliseconds);
                sampledFrames++;
            }

            public PsoMarkerStatistics ToStatistics()
            {
                return new PsoMarkerStatistics
                {
                    markerName = name,
                    available = recorder.Valid,
                    sampleCount = sampledFrames,
                    totalMilliseconds = totalMilliseconds,
                    maximumFrameMilliseconds = maximumFrameMilliseconds,
                };
            }

            public void Dispose()
            {
                if (recorder.Valid)
                    recorder.Dispose();
            }
        }

        private static readonly string[] MarkerNames =
        {
            "Shader.CreateGPUProgram",
            "CreateGraphicsGraphicsPipelineImpl",
        };

        private readonly List<double> frameTimes = new List<double>();
        private readonly List<MarkerAccumulator> markers = new List<MarkerAccumulator>();

        private string mode;
        private string runId;
        private string startedUtc;
        private string reportPath;
        private int requestedFrames;
        private int discardFrames;
        private int discarded;
        private double delaySeconds;
        private double waitingSince;
        private bool noQuit;
        private bool sampling;
        private bool finished;
        private double hitchThresholdMilliseconds;
        private double optimizedReadyAt = -1.0;
        private double warmupTimeoutSeconds;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void BootstrapFromCommandLine()
        {
            PsoCommandLine commandLine = PsoCommandLine.Current;
            if (!commandLine.HasFlag(PsoConstants.BenchmarkArgument))
                return;

            var host = new GameObject("Shader Hitch Pipeline Benchmark");
            DontDestroyOnLoad(host);
            host.AddComponent<PsoBenchmarkController>();
        }

        private void Awake()
        {
            PsoCommandLine commandLine = PsoCommandLine.Current;
            mode = PsoFileUtility.SanitizeFileName(commandLine.GetString(
                PsoConstants.BenchmarkModeArgument,
                commandLine.HasFlag(PsoConstants.DisableWarmupArgument)
                    ? "baseline"
                    : "optimized"));
            requestedFrames = commandLine.GetInt(
                PsoConstants.BenchmarkFramesArgument,
                600,
                30,
                1000000);
            discardFrames = commandLine.GetInt(
                PsoConstants.BenchmarkDiscardFramesArgument,
                60,
                0,
                1000000);
            delaySeconds = commandLine.GetDouble(
                PsoConstants.BenchmarkDelayArgument,
                1.0,
                0.0,
                3600.0);
            hitchThresholdMilliseconds = commandLine.GetDouble(
                PsoConstants.BenchmarkHitchThresholdArgument,
                33.33,
                0.01,
                10000.0);
            warmupTimeoutSeconds = commandLine.GetDouble(
                PsoConstants.BenchmarkWarmupTimeoutArgument,
                300.0,
                1.0,
                86400.0);
            noQuit = commandLine.HasFlag(PsoConstants.BenchmarkNoQuitArgument);
            runId = PsoFileUtility.CreateRunId(mode);
            startedUtc = PsoFileUtility.UtcNowText();

            string defaultReport = Path.Combine(
                PsoFileUtility.DefaultRuntimeOutputRoot(),
                "Benchmarks",
                runId + PsoConstants.BenchmarkReceiptSuffix);
            reportPath = Path.GetFullPath(commandLine.GetString(
                PsoConstants.BenchmarkReportArgument,
                defaultReport));
            waitingSince = Time.realtimeSinceStartupAsDouble;

            for (int index = 0; index < MarkerNames.Length; index++)
                markers.Add(new MarkerAccumulator(MarkerNames[index]));

            Debug.Log("[ShaderHitchPipeline] Benchmark '" + mode +
                      "' waiting to collect " + requestedFrames + " frames.");
        }

        private void Update()
        {
            if (finished)
                return;

            try
            {
                if (!sampling)
                {
                    if (!ReadyToSample())
                        return;
                    sampling = true;
                    Debug.Log("[ShaderHitchPipeline] Benchmark sampling started.");
                }

                if (discarded < discardFrames)
                {
                    discarded++;
                    return;
                }

                frameTimes.Add(Time.unscaledDeltaTime * 1000.0);
                for (int index = 0; index < markers.Count; index++)
                    markers[index].Sample();

                if (frameTimes.Count >= requestedFrames)
                    Finish(string.Empty);
            }
            catch (Exception exception)
            {
                Finish(exception.ToString());
            }
        }

        private bool ReadyToSample()
        {
            if (!string.Equals(mode, "optimized", StringComparison.OrdinalIgnoreCase))
                return Time.realtimeSinceStartupAsDouble - waitingSince >= delaySeconds;

            PsoWarmupOrchestrator orchestrator = PsoWarmupOrchestrator.Instance;
            if (orchestrator == null || !orchestrator.HasLoadedPlan)
                throw new InvalidOperationException(
                    "Optimized benchmark requires a valid warmup plan.");
            if (orchestrator.HasFailed)
                throw new InvalidOperationException(
                    "Warmup failed before benchmark: " + orchestrator.Failure);
            if (Time.realtimeSinceStartupAsDouble - waitingSince > warmupTimeoutSeconds)
                throw new TimeoutException(
                    "Warmup did not complete within " + warmupTimeoutSeconds + " seconds.");
            if (!orchestrator.IsComplete)
                return false;

            if (optimizedReadyAt < 0.0)
                optimizedReadyAt = Time.realtimeSinceStartupAsDouble + delaySeconds;
            return Time.realtimeSinceStartupAsDouble >= optimizedReadyAt;
        }

        private void Finish(string error)
        {
            if (finished)
                return;
            finished = true;

            PsoTraceController trace = PsoTraceController.Instance;
            if (trace != null && trace.IsTracing)
                trace.EndPhase();

            PsoWarmupOrchestrator warmup = PsoWarmupOrchestrator.Instance;
            if (warmup != null)
                warmup.SaveCacheMissesNow();

            var markerStatistics = new PsoMarkerStatistics[markers.Count];
            for (int index = 0; index < markers.Count; index++)
                markerStatistics[index] = markers[index].ToStatistics();

            string planPath = warmup == null ? string.Empty : warmup.PlanPath;
            var receipt = new PsoBenchmarkReceipt
            {
                runId = runId,
                mode = mode,
                startedUtc = startedUtc,
                endedUtc = PsoFileUtility.UtcNowText(),
                planFile = planPath,
                planSha256 = warmup == null ? string.Empty : warmup.PlanHash,
                discardFrames = discardFrames,
                requestedSampleFrames = requestedFrames,
                completed = string.IsNullOrEmpty(error) && frameTimes.Count == requestedFrames,
                error = error,
                environment = PsoEnvironmentSnapshot.Capture(),
                frameTimes = PsoStatistics.Calculate(
                    frameTimes,
                    hitchThresholdMilliseconds),
                frameTimeSamplesMilliseconds = frameTimes.ToArray(),
                profilerMarkers = markerStatistics,
            };
            PsoFileUtility.WriteJsonAtomic(reportPath, receipt);
            Debug.Log("[ShaderHitchPipeline] Benchmark report: " + reportPath);

            if (!noQuit)
                Application.Quit(receipt.completed ? 0 : 2);
        }

        private void OnDestroy()
        {
            for (int index = 0; index < markers.Count; index++)
                markers[index].Dispose();
            markers.Clear();
        }
    }
}
