using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace Yanagisawa.ShaderHitchPipeline
{
    [DefaultExecutionOrder(-31000)]
    [DisallowMultipleComponent]
    public sealed class PsoWarmupOrchestrator : MonoBehaviour
    {
        private const string ScheduledStrategy = "scheduled";
        private const string ThroughputStrategy = "throughput";

        private sealed class PhaseExecution
        {
            public PsoWarmupPhasePlan plan;
            public GraphicsStateCollection collection;
            public PsoAdaptiveBatchPolicy policy;
            public JobHandle job;
            public bool jobScheduled;
            public int completedBeforeBatch;
            public int scheduledBatchSize;
            public double jobScheduledAt;
            public double activatedAt;
            public Stopwatch stopwatch;
            public PsoWarmupPhaseReceipt receipt;
            public readonly List<double> frameTimes = new List<double>();
            public readonly List<int> batchSizes = new List<int>();
            public readonly List<double> batchDurations = new List<double>();
        }

        private static PsoWarmupOrchestrator instance;
        private readonly List<PhaseExecution> workItems =
            new List<PhaseExecution>();
        private readonly HashSet<string> activated =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<PsoWarmupPhaseReceipt> phaseReceipts =
            new List<PsoWarmupPhaseReceipt>();

        private PsoWarmupPlanDocument plan;
        private PhaseExecution scheduled;
        private Stopwatch runStopwatch;
        private GraphicsStateCollection feedbackTraceCollection;
        private PsoCacheMissTraceReceipt feedbackTraceReceipt;
        private string strategy;
        private string planPath;
        private string planHash;
        private string outputRoot;
        private string runId;
        private string startedUtc;
        private string receiptPath;
        private bool failed;
        private string failure;
        private bool quitting;
        private bool completionRaised;

        public static PsoWarmupOrchestrator Instance => instance;
        public bool HasLoadedPlan => plan != null;
        public bool IsBusy => scheduled != null || workItems.Count > 0;
        public bool IsComplete => HasLoadedPlan && !IsBusy && !failed;
        public bool HasFailed => failed;
        public string Failure => failure;
        public string PlanPath => planPath;
        public string PlanHash => planHash;
        public string Strategy => strategy;
        public int PendingPhaseCount => workItems.Count;

        public event Action WarmupCompleted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void BootstrapFromCommandLine()
        {
            PsoCommandLine commandLine = PsoCommandLine.Current;
            if (commandLine.HasFlag(PsoConstants.DisableWarmupArgument))
                return;

            string defaultPlan = Path.Combine(
                Application.streamingAssetsPath,
                PsoConstants.DefaultStreamingSubdirectory,
                PsoConstants.DefaultPlanFileName);
            string requestedPlan = commandLine.GetString(
                PsoConstants.WarmupPlanArgument,
                defaultPlan);
            if (!File.Exists(requestedPlan))
                return;

            PsoWarmupOrchestrator orchestrator = EnsureInstance();
            string requestedOutput = commandLine.GetString(
                PsoConstants.OutputArgument,
                PsoFileUtility.DefaultRuntimeOutputRoot());
            orchestrator.LoadPlan(requestedPlan, requestedOutput, true);

            string explicitPhase = commandLine.GetString(
                PsoConstants.WarmupPhaseArgument,
                string.Empty);
            if (!string.IsNullOrWhiteSpace(explicitPhase))
                orchestrator.ActivatePhase(explicitPhase);
            else
                orchestrator.ActivateStartupPhases();
        }

        public static PsoWarmupOrchestrator EnsureInstance()
        {
            if (instance != null)
                return instance;

            var host = new GameObject("Shader Hitch Pipeline Warmup");
            DontDestroyOnLoad(host);
            return host.AddComponent<PsoWarmupOrchestrator>();
        }

        public void LoadPlan(
            string requestedPlanPath,
            string requestedOutputRoot = null,
            bool validateEnvironment = true)
        {
            if (IsBusy)
                throw new InvalidOperationException("Cannot replace a plan while warmup is active.");

            StopFeedbackTrace();

            planPath = Path.GetFullPath(requestedPlanPath);
            plan = PsoPlanValidation.LoadAndValidate(
                planPath,
                validateEnvironment,
                true);
            planHash = PsoFileUtility.ComputeSha256(planPath);
            outputRoot = Path.GetFullPath(
                string.IsNullOrWhiteSpace(requestedOutputRoot)
                    ? PsoFileUtility.DefaultRuntimeOutputRoot()
                    : requestedOutputRoot);
            runId = PsoFileUtility.CreateRunId("warmup");
            startedUtc = PsoFileUtility.UtcNowText();

            PsoCommandLine commandLine = PsoCommandLine.Current;
            strategy = NormalizeStrategy(commandLine.GetString(
                PsoConstants.WarmupStrategyArgument,
                ScheduledStrategy));
            string explicitReceipt = commandLine.GetString(
                PsoConstants.WarmupReceiptArgument,
                string.Empty);
            receiptPath = string.IsNullOrWhiteSpace(explicitReceipt)
                ? Path.Combine(
                    outputRoot,
                    "Receipts",
                    runId + PsoConstants.WarmupReceiptSuffix)
                : Path.GetFullPath(explicitReceipt);

            failed = false;
            failure = string.Empty;
            completionRaised = false;
            activated.Clear();
            workItems.Clear();
            phaseReceipts.Clear();
            scheduled = null;
            runStopwatch = null;
            PrepareFeedbackTrace();

            Debug.Log("[ShaderHitchPipeline] Loaded plan '" + plan.profileId +
                      "' with " + plan.phases.Length + " phases; strategy=" +
                      strategy + ".");
        }

        public void ActivateStartupPhases()
        {
            EnsurePlan();
            var startup = new List<PsoWarmupPhasePlan>();
            for (int index = 0; index < plan.phases.Length; index++)
            {
                if (plan.phases[index].prewarmAtStartup)
                    startup.Add(plan.phases[index]);
            }
            startup.Sort(ComparePlans);
            for (int index = 0; index < startup.Count; index++)
                Enqueue(startup[index]);
        }

        public bool ActivatePhase(string phase)
        {
            EnsurePlan();
            for (int index = 0; index < plan.phases.Length; index++)
            {
                if (!string.Equals(
                        plan.phases[index].phase,
                        phase,
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                return Enqueue(plan.phases[index]);
            }

            Debug.LogWarning("[ShaderHitchPipeline] Plan has no phase named '" + phase + "'.");
            return false;
        }

        public void SaveCacheMissesNow()
        {
            if (feedbackTraceReceipt == null || !feedbackTraceReceipt.requested)
            {
                WriteReceipt();
                return;
            }

            if (feedbackTraceCollection == null)
            {
                feedbackTraceReceipt.error =
                    "Feedback trace was requested but no collection is available.";
                WriteReceipt();
                return;
            }

            if (feedbackTraceCollection.isTracing)
                feedbackTraceCollection.EndTrace();

            feedbackTraceReceipt.observedGraphicsStates =
                feedbackTraceCollection.totalGraphicsStateCount;
            feedbackTraceReceipt.cacheMissGraphicsStates = Math.Max(
                0,
                feedbackTraceReceipt.observedGraphicsStates -
                feedbackTraceReceipt.baselineGraphicsStates);

            if (feedbackTraceReceipt.cacheMissGraphicsStates > 0)
            {
                string fileName = "plan-" + runId + "-feedback" +
                                  PsoConstants.GraphicsStateExtension;
                string path = Path.Combine(outputRoot, "CacheMisses", fileName);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (feedbackTraceCollection.SaveToFile(path))
                {
                    feedbackTraceReceipt.collectionContainsBaseline = true;
                    feedbackTraceReceipt.collectionFile = path;
                    feedbackTraceReceipt.collectionSha256 =
                        PsoFileUtility.ComputeSha256(path);
                }
                else
                {
                    feedbackTraceReceipt.error =
                        "GraphicsStateCollection.SaveToFile returned false.";
                }
            }

            if (!quitting && !feedbackTraceCollection.BeginTrace())
                feedbackTraceReceipt.error =
                    "Could not resume the plan-scoped feedback trace.";

            WriteReceipt();
        }

        private void PrepareFeedbackTrace()
        {
            bool requested = false;
            for (int index = 0; index < plan.phases.Length; index++)
                requested |= plan.phases[index].traceCacheMisses;

            feedbackTraceReceipt = new PsoCacheMissTraceReceipt
            {
                requested = requested,
                scope = "plan",
            };
            if (!requested)
                return;

            var combined = new GraphicsStateCollection();
            string directory = Path.GetDirectoryName(planPath);
            for (int index = 0; index < plan.phases.Length; index++)
            {
                string collectionPath = PsoFileUtility.ResolveChildPath(
                    directory,
                    plan.phases[index].collectionFile);
                var source = new GraphicsStateCollection();
                if (!source.LoadFromFile(collectionPath))
                    throw new IOException(
                        "Could not load feedback baseline collection '" +
                        collectionPath + "'.");
                if (!combined.Append(source))
                    throw new InvalidDataException(
                        "Could not append feedback baseline collection '" +
                        collectionPath + "'.");
                Destroy(source);
            }

            feedbackTraceCollection = combined;
            feedbackTraceReceipt.baselineGraphicsStates =
                combined.totalGraphicsStateCount;
            feedbackTraceReceipt.observedGraphicsStates =
                combined.totalGraphicsStateCount;
            feedbackTraceReceipt.armed = combined.BeginTrace();
            if (!feedbackTraceReceipt.armed)
                throw new InvalidOperationException(
                    "Could not start the plan-scoped feedback trace.");
        }

        private void StopFeedbackTrace()
        {
            if (feedbackTraceCollection == null)
                return;
            if (feedbackTraceCollection.isTracing)
                feedbackTraceCollection.EndTrace();
            Destroy(feedbackTraceCollection);
            feedbackTraceCollection = null;
        }

        private bool Enqueue(PsoWarmupPhasePlan phase)
        {
            if (!activated.Add(phase.phase))
                return false;

            double now = Time.realtimeSinceStartupAsDouble;
            var receipt = new PsoWarmupPhaseReceipt
            {
                phase = phase.phase,
                collectionFile = phase.collectionFile,
                strategy = strategy,
                hotSetTier = phase.hotSetTier,
                deadlineMilliseconds = phase.deadlineMilliseconds,
                expectedUseProbability = phase.expectedUseProbability,
                totalGraphicsStates = phase.graphicsStateCount,
                initialEstimatedMillisecondsPerState =
                    phase.estimatedMillisecondsPerState,
                minimumSlackMilliseconds = double.MaxValue,
            };
            phaseReceipts.Add(receipt);
            workItems.Add(new PhaseExecution
            {
                plan = phase,
                activatedAt = now,
                stopwatch = Stopwatch.StartNew(),
                receipt = receipt,
            });
            if (runStopwatch == null)
                runStopwatch = Stopwatch.StartNew();
            else if (!runStopwatch.IsRunning)
                runStopwatch.Start();
            completionRaised = false;
            return true;
        }

        private void Update()
        {
            if (quitting || failed || plan == null)
                return;

            try
            {
                if (scheduled != null && scheduled.jobScheduled)
                {
                    ObserveScheduledFrame(scheduled);
                    if (!scheduled.job.IsCompleted)
                        return;

                    PhaseExecution completedBatch = scheduled;
                    CompleteScheduledBatch(completedBatch);
                    scheduled = null;
                    if (IsPhaseComplete(completedBatch))
                        CompletePhase(completedBatch);
                }

                if (workItems.Count == 0)
                {
                    RaiseCompletion();
                    return;
                }

                PhaseExecution next = SelectNextWork();
                EnsureLoaded(next);
                ScheduleNextBatch(next);
            }
            catch (Exception exception)
            {
                Fail("Warmup failed: " + exception.Message, exception);
            }
        }

        private void ObserveScheduledFrame(PhaseExecution execution)
        {
            double frameMilliseconds = Time.unscaledDeltaTime * 1000.0;
            execution.frameTimes.Add(frameMilliseconds);
            execution.receipt.maximumObservedFrameMilliseconds = Math.Max(
                execution.receipt.maximumObservedFrameMilliseconds,
                frameMilliseconds);
            execution.policy.Observe(frameMilliseconds);
        }

        private PhaseExecution SelectNextWork()
        {
            var candidates = new PsoSchedulerCandidate[workItems.Count];
            double now = Time.realtimeSinceStartupAsDouble;
            double riskWindow = 0.0;
            for (int index = 0; index < workItems.Count; index++)
            {
                PhaseExecution item = workItems[index];
                int remaining = item.collection == null
                    ? item.plan.graphicsStateCount
                    : Math.Max(
                        0,
                        item.collection.totalGraphicsStateCount -
                        item.collection.completedWarmupCount);
                double cost = item.policy == null
                    ? item.plan.estimatedMillisecondsPerState
                    : item.policy.EstimatedMillisecondsPerState;
                double elapsed = (now - item.activatedAt) * 1000.0;
                candidates[index] = new PsoSchedulerCandidate(
                    item.plan.phase,
                    remaining,
                    item.plan.priority,
                    item.plan.hotSetTier,
                    item.plan.deadlineMilliseconds,
                    elapsed,
                    cost,
                    item.plan.expectedUseProbability);
                riskWindow = Math.Max(
                    riskWindow,
                    item.plan.targetFrameMilliseconds * 2.0);
            }

            PsoSchedulingDecision decision = PsoDeadlineCostScheduler.SelectNext(
                candidates,
                riskWindow);
            if (!decision.IsValid)
                throw new InvalidOperationException("Scheduler found no warmup work.");

            PhaseExecution selected = workItems[decision.CandidateIndex];
            selected.receipt.schedulerSelectionCount++;
            if (selected.plan.deadlineMilliseconds > 0.0)
            {
                selected.receipt.minimumSlackMilliseconds = Math.Min(
                    selected.receipt.minimumSlackMilliseconds,
                    decision.SlackMilliseconds);
            }
            return selected;
        }

        private void EnsureLoaded(PhaseExecution execution)
        {
            if (execution.collection != null)
                return;

            string directory = Path.GetDirectoryName(planPath);
            string collectionPath = PsoFileUtility.ResolveChildPath(
                directory,
                execution.plan.collectionFile);
            var collection = new GraphicsStateCollection();
            if (!collection.LoadFromFile(collectionPath))
                throw new IOException("GraphicsStateCollection.LoadFromFile returned false.");
            if (collection.runtimePlatform != Application.platform)
                throw new InvalidDataException(
                    "Collection platform is " + collection.runtimePlatform +
                    ", current platform is " + Application.platform + ".");
            if (collection.graphicsDeviceType != SystemInfo.graphicsDeviceType)
                throw new InvalidDataException(
                    "Collection graphics API is " + collection.graphicsDeviceType +
                    ", current API is " + SystemInfo.graphicsDeviceType + ".");

            PsoCommandLine commandLine = PsoCommandLine.Current;
            int minimum = commandLine.GetInt(
                PsoConstants.WarmupMinimumBatchArgument,
                execution.plan.minimumBatchSize,
                1,
                1000000);
            int maximum = commandLine.GetInt(
                PsoConstants.WarmupMaximumBatchArgument,
                execution.plan.maximumBatchSize,
                minimum,
                1000000);
            int initial = commandLine.GetInt(
                PsoConstants.WarmupInitialBatchArgument,
                execution.plan.initialBatchSize,
                minimum,
                maximum);

            execution.collection = collection;
            execution.policy = new PsoAdaptiveBatchPolicy(
                initial,
                minimum,
                maximum,
                execution.plan.targetFrameMilliseconds,
                execution.plan.estimatedMillisecondsPerState);
            execution.receipt.totalGraphicsStates = collection.totalGraphicsStateCount;
            execution.receipt.initialBatchSize = initial;
            Debug.Log("[ShaderHitchPipeline] Prepared phase '" + execution.plan.phase +
                      "' with " + collection.totalGraphicsStateCount +
                      " graphics states; deadline=" +
                      execution.plan.deadlineMilliseconds.ToString("F1") +
                      " ms, hot-set tier=" + execution.plan.hotSetTier + ".");
        }

        private void ScheduleNextBatch(PhaseExecution execution)
        {
            int remaining = Math.Max(
                0,
                execution.collection.totalGraphicsStateCount -
                execution.collection.completedWarmupCount);
            if (remaining == 0 || execution.collection.isWarmedUp)
            {
                CompletePhase(execution);
                return;
            }

            execution.completedBeforeBatch = execution.collection.completedWarmupCount;
            int batchSize;
            if (string.Equals(strategy, ThroughputStrategy, StringComparison.Ordinal))
            {
                batchSize = remaining;
                execution.job = execution.collection.WarmUp(
                    default(JobHandle),
                    false);
            }
            else
            {
                double elapsed = (Time.realtimeSinceStartupAsDouble -
                                  execution.activatedAt) * 1000.0;
                double deadlineRemaining = execution.plan.deadlineMilliseconds <= 0.0
                    ? double.PositiveInfinity
                    : execution.plan.deadlineMilliseconds - elapsed;
                batchSize = execution.policy.RecommendBatchSize(
                    remaining,
                    deadlineRemaining,
                    execution.plan.hotSetTier == 0);
                execution.job = execution.collection.WarmUpProgressively(
                    batchSize,
                    default(JobHandle),
                    false);
            }

            execution.scheduledBatchSize = batchSize;
            execution.jobScheduledAt = Time.realtimeSinceStartupAsDouble;
            execution.jobScheduled = true;
            execution.receipt.batchCount++;
            scheduled = execution;
        }

        private void CompleteScheduledBatch(PhaseExecution execution)
        {
            execution.job.Complete();
            execution.jobScheduled = false;
            double duration = Math.Max(
                0.0,
                (Time.realtimeSinceStartupAsDouble - execution.jobScheduledAt) * 1000.0);
            int completed = execution.collection.completedWarmupCount;
            int delta = Math.Max(0, completed - execution.completedBeforeBatch);
            execution.policy.ObserveBatch(delta, duration);
            execution.receipt.completedGraphicsStates = completed;
            execution.batchSizes.Add(execution.scheduledBatchSize);
            execution.batchDurations.Add(duration);
        }

        private static bool IsPhaseComplete(PhaseExecution execution)
        {
            return execution.collection.isWarmedUp ||
                   execution.collection.completedWarmupCount >=
                   execution.collection.totalGraphicsStateCount;
        }

        private void CompletePhase(PhaseExecution execution)
        {
            if (!workItems.Remove(execution))
                return;

            execution.stopwatch.Stop();
            execution.receipt.completedGraphicsStates =
                execution.collection.completedWarmupCount;
            execution.receipt.finalBatchSize = execution.policy.CurrentBatchSize;
            execution.receipt.elapsedMilliseconds =
                execution.stopwatch.Elapsed.TotalMilliseconds;
            execution.receipt.observedMillisecondsPerState =
                execution.policy.EstimatedMillisecondsPerState;
            execution.receipt.warmupFrameTimes = PsoStatistics.Calculate(
                execution.frameTimes,
                execution.plan.targetFrameMilliseconds);
            execution.receipt.maximumObservedFrameMilliseconds =
                execution.receipt.warmupFrameTimes.maximumMilliseconds;
            execution.receipt.batchSizes = execution.batchSizes.ToArray();
            execution.receipt.batchDurationsMilliseconds =
                execution.batchDurations.ToArray();
            if (execution.receipt.minimumSlackMilliseconds == double.MaxValue)
                execution.receipt.minimumSlackMilliseconds = 0.0;
            execution.receipt.deadlineMissed =
                execution.plan.deadlineMilliseconds > 0.0 &&
                execution.receipt.elapsedMilliseconds >
                execution.plan.deadlineMilliseconds;
            execution.receipt.completed = true;

            Debug.Log("[ShaderHitchPipeline] Warmed phase '" + execution.plan.phase +
                      "': " + execution.receipt.completedGraphicsStates + "/" +
                      execution.receipt.totalGraphicsStates + " states in " +
                      execution.receipt.elapsedMilliseconds.ToString("F1") +
                      " ms across " + execution.receipt.batchCount + " batches.");
            WriteReceipt();
        }

        private void RaiseCompletion()
        {
            if (completionRaised || failed)
                return;
            completionRaised = true;
            if (runStopwatch != null && runStopwatch.IsRunning)
                runStopwatch.Stop();
            WriteReceipt();
            WarmupCompleted?.Invoke();
        }

        private void Fail(string message, Exception exception = null)
        {
            failed = true;
            failure = exception == null ? message : message + Environment.NewLine + exception;
            if (scheduled != null)
                scheduled.jobScheduled = false;
            scheduled = null;
            for (int index = 0; index < workItems.Count; index++)
            {
                workItems[index].stopwatch.Stop();
                workItems[index].receipt.elapsedMilliseconds =
                    workItems[index].stopwatch.Elapsed.TotalMilliseconds;
                workItems[index].receipt.error = failure;
            }
            workItems.Clear();
            if (runStopwatch != null && runStopwatch.IsRunning)
                runStopwatch.Stop();
            Debug.LogError("[ShaderHitchPipeline] " + message);
            WriteReceipt();
        }

        private void WriteReceipt()
        {
            if (string.IsNullOrWhiteSpace(receiptPath))
                return;

            PsoCommandLine commandLine = PsoCommandLine.Current;
            var receipt = new PsoWarmupRunReceipt
            {
                runId = runId,
                startedUtc = startedUtc,
                endedUtc = PsoFileUtility.UtcNowText(),
                planFile = planPath,
                planSha256 = planHash,
                strategy = strategy,
                asyncPsoJobCount = commandLine.GetInt(
                    PsoConstants.AsyncPsoJobCountArgument,
                    -1,
                    -1,
                    1024),
                processorCount = SystemInfo.processorCount,
                elapsedMilliseconds = runStopwatch == null
                    ? 0.0
                    : runStopwatch.Elapsed.TotalMilliseconds,
                completed = !failed && scheduled == null && workItems.Count == 0,
                error = failure,
                environment = PsoEnvironmentSnapshot.Capture(),
                phases = phaseReceipts.ToArray(),
                cacheMissTrace = feedbackTraceReceipt,
            };
            PsoFileUtility.WriteJsonAtomic(receiptPath, receipt);
        }

        private void EnsurePlan()
        {
            if (plan == null)
                throw new InvalidOperationException("No warmup plan is loaded.");
        }

        private static string NormalizeStrategy(string value)
        {
            if (string.Equals(value, ThroughputStrategy, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "naive", StringComparison.OrdinalIgnoreCase))
                return ThroughputStrategy;
            if (string.IsNullOrWhiteSpace(value) ||
                string.Equals(value, ScheduledStrategy, StringComparison.OrdinalIgnoreCase))
                return ScheduledStrategy;
            throw new ArgumentException(
                "Unsupported warmup strategy '" + value +
                "'. Expected scheduled or throughput.");
        }

        private static int ComparePlans(PsoWarmupPhasePlan left, PsoWarmupPhasePlan right)
        {
            int priority = left.priority.CompareTo(right.priority);
            return priority != 0
                ? priority
                : string.Compare(left.phase, right.phase, StringComparison.OrdinalIgnoreCase);
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnApplicationQuit()
        {
            quitting = true;
            if (scheduled != null && scheduled.jobScheduled && scheduled.job.IsCompleted)
                CompleteScheduledBatch(scheduled);
            for (int index = 0; index < workItems.Count; index++)
            {
                PhaseExecution execution = workItems[index];
                execution.stopwatch.Stop();
                execution.receipt.completedGraphicsStates = execution.collection == null
                    ? 0
                    : execution.collection.completedWarmupCount;
                execution.receipt.elapsedMilliseconds =
                    execution.stopwatch.Elapsed.TotalMilliseconds;
                execution.receipt.error = "Application quit before phase completion.";
            }
            SaveCacheMissesNow();
            WriteReceipt();
        }

        private void OnDestroy()
        {
            StopFeedbackTrace();
            if (instance == this)
                instance = null;
        }
    }
}
