using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_6000_5_OR_NEWER
using GraphicsStateCollection = UnityEngine.Rendering.GraphicsStateCollection;
#else
using GraphicsStateCollection = UnityEngine.Experimental.Rendering.GraphicsStateCollection;
#endif
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
            public IPsoWarmupBackend backend;
            public PsoAdaptiveBatchPolicy policy;
            public IPsoWarmupBatch job;
            public bool jobScheduled;
            public int completedBeforeBatch;
            public int scheduledBatchSize;
            public int effectiveMinimumBatchSize;
            public int effectiveMaximumBatchSize;
            public int effectiveBootstrapBatchSize;
            public bool nativeAsyncBulkDeadline;
            public double jobScheduledAt;
            public double activatedAt;
            public Stopwatch stopwatch;
            public PsoWarmupPhaseReceipt receipt;
            public readonly List<double> frameTimes = new List<double>(1024);
            public readonly List<int> batchSizes = new List<int>(512);
            public readonly List<int> safeBatchSizes = new List<int>(512);
            public readonly List<int> deadlineBatchSizes = new List<int>(512);
            public readonly List<double> predictedBatchDurations = new List<double>(512);
            public readonly List<double> batchDurations = new List<double>(512);
            public readonly List<string> admissionReasons = new List<string>(512);
            public bool schedulerAdmissionBudgetMet = true;
            public bool receiptFinalized;
        }

        private static PsoWarmupOrchestrator instance;
        private readonly List<PhaseExecution> workItems =
            new List<PhaseExecution>();
        private readonly HashSet<string> activated =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<PsoWarmupPhaseReceipt> phaseReceipts =
            new List<PsoWarmupPhaseReceipt>();
        private readonly List<PhaseExecution> activatedExecutions =
            new List<PhaseExecution>();
        private readonly Dictionary<string, PhaseExecution> preparedExecutions =
            new Dictionary<string, PhaseExecution>(StringComparer.OrdinalIgnoreCase);
        private PsoSchedulerCandidate[] schedulerCandidates =
            Array.Empty<PsoSchedulerCandidate>();
        private PsoSchedulerCandidate[][] schedulerCandidateBuffers =
            Array.Empty<PsoSchedulerCandidate[]>();

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

        public bool IsPhaseComplete(string phaseName)
        {
            if (failed || string.IsNullOrWhiteSpace(phaseName))
                return false;
            for (int index = 0; index < activatedExecutions.Count; index++)
            {
                PhaseExecution execution = activatedExecutions[index];
                if (string.Equals(
                        execution.plan.phase,
                        phaseName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return execution.receipt.completed;
                }
            }
            return false;
        }

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
            orchestrator.RunPreinteractiveBootstrap();
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

            DisposePreparedBackends();
            DisposeActivatedBackends();
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
            activatedExecutions.Clear();
            scheduled = null;
            runStopwatch = new Stopwatch();
            int phaseCount = plan.phases.Length;
            workItems.Capacity = Math.Max(workItems.Capacity, phaseCount);
            phaseReceipts.Capacity = Math.Max(phaseReceipts.Capacity, phaseCount);
            activatedExecutions.Capacity = Math.Max(
                activatedExecutions.Capacity,
                phaseCount);
            schedulerCandidateBuffers = new PsoSchedulerCandidate[phaseCount + 1][];
            schedulerCandidateBuffers[0] = Array.Empty<PsoSchedulerCandidate>();
            for (int count = 1; count <= phaseCount; count++)
                schedulerCandidateBuffers[count] = new PsoSchedulerCandidate[count];
            schedulerCandidates = schedulerCandidateBuffers[0];
            // Populate and clear once so later phase activation reuses HashSet storage.
            for (int index = 0; index < phaseCount; index++)
                activated.Add(plan.phases[index].phase);
            activated.Clear();
            try
            {
                PrepareFeedbackTrace();
                PrepareBackends();
            }
            catch
            {
                DisposePreparedBackends();
                StopFeedbackTrace();
                throw;
            }

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
                if (!PsoGraphicsStateCollectionCompatibility.Append(combined, source))
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

        private void PrepareBackends()
        {
            if (!string.Equals(
                    plan.adapterId,
                    PsoUnityGraphicsStateTraceBackend.UnityAdapterId,
                    StringComparison.Ordinal))
            {
                throw new NotSupportedException(
                    "This Unity runtime cannot execute plan adapter '" +
                    plan.adapterId + "'.");
            }

            string directory = Path.GetDirectoryName(planPath);
            for (int index = 0; index < plan.phases.Length; index++)
            {
                PsoWarmupPhasePlan phase = plan.phases[index];
                string collectionPath = PsoFileUtility.ResolveChildPath(
                    directory,
                    phase.collectionFile);
                IPsoWarmupBackend backend =
                    new PsoUnityGraphicsStateWarmupBackend(collectionPath);
                try
                {
                    preparedExecutions.Add(
                        phase.phase,
                        CreatePreparedExecution(phase, backend));
                }
                catch
                {
                    backend.Dispose();
                    throw;
                }
            }
        }

        private PhaseExecution CreatePreparedExecution(
            PsoWarmupPhasePlan phase,
            IPsoWarmupBackend backend)
        {
            PsoCommandLine commandLine = PsoCommandLine.Current;
            int minimum = commandLine.GetInt(
                PsoConstants.WarmupMinimumBatchArgument,
                phase.minimumBatchSize,
                1,
                1000000);
            int maximum = commandLine.GetInt(
                PsoConstants.WarmupMaximumBatchArgument,
                phase.maximumBatchSize,
                minimum,
                1000000);
            int initial = commandLine.GetInt(
                PsoConstants.WarmupInitialBatchArgument,
                phase.initialBatchSize,
                minimum,
                maximum);
            int bootstrap = commandLine.GetInt(
                PsoConstants.WarmupBootstrapBatchArgument,
                phase.bootstrapBatchSize,
                minimum,
                maximum);
            double safetyMargin = commandLine.GetDouble(
                PsoConstants.WarmupBudgetSafetyMarginArgument,
                phase.budgetSafetyMarginMilliseconds,
                0.0,
                Math.Max(0.0, phase.targetFrameMilliseconds - 0.001));
            double costSafetyMultiplier = commandLine.GetDouble(
                PsoConstants.WarmupBudgetCostSafetyMultiplierArgument,
                phase.budgetCostSafetyMultiplier,
                1.0,
                100.0);
            int cooldownFrames = commandLine.GetInt(
                PsoConstants.WarmupBudgetCooldownFramesArgument,
                phase.budgetCooldownFrames,
                0,
                1000000);
            bool throughput = string.Equals(
                strategy,
                ThroughputStrategy,
                StringComparison.Ordinal);
            bool nativeAsyncBulkDeadline =
                !throughput &&
                backend.PreferNativeAsyncBulkForDeadline &&
                !phase.prewarmAtStartup &&
                phase.hotSetTier > 0;
            var receipt = new PsoWarmupPhaseReceipt
            {
                phase = phase.phase,
                collectionFile = phase.collectionFile,
                strategy = strategy,
                backendSchedulingMode = throughput
                    ? "native-async-throughput"
                    : nativeAsyncBulkDeadline
                        ? "deadline-gated-native-async-bulk"
                        : "progressive-batches",
                hotSetTier = phase.hotSetTier,
                deadlineMilliseconds = phase.deadlineMilliseconds,
                expectedUseProbability = phase.expectedUseProbability,
                totalGraphicsStates = backend.TotalStateCount,
                initialEstimatedMillisecondsPerState =
                    phase.estimatedMillisecondsPerState,
                minimumSlackMilliseconds = double.MaxValue,
                initialBatchSize = initial,
                hardFrameBudgetMilliseconds = phase.targetFrameMilliseconds,
                budgetSafetyMarginMilliseconds = safetyMargin,
                budgetCostSafetyMultiplier = costSafetyMultiplier,
                bootstrapBatchSize = bootstrap,
                budgetPolicy = throughput
                    ? "throughput-control"
                    : "strict-admission",
            };
            var execution = new PhaseExecution
            {
                plan = phase,
                backend = backend,
                policy = new PsoAdaptiveBatchPolicy(
                    initial,
                    minimum,
                    maximum,
                    phase.targetFrameMilliseconds,
                    phase.estimatedMillisecondsPerState,
                    bootstrap,
                    safetyMargin,
                    costSafetyMultiplier,
                    cooldownFrames),
                stopwatch = new Stopwatch(),
                receipt = receipt,
                effectiveMinimumBatchSize = minimum,
                effectiveMaximumBatchSize = maximum,
                effectiveBootstrapBatchSize = bootstrap,
                nativeAsyncBulkDeadline = nativeAsyncBulkDeadline,
                schedulerAdmissionBudgetMet = !throughput,
            };
            receipt.preinteractiveBootstrapEnabled =
                ShouldRunPreinteractiveBootstrap(execution);
            receipt.hardBudgetGuaranteeScope = throughput
                ? "none; throughput-control"
                : nativeAsyncBulkDeadline
                    ? "strict-dispatch-admission; native-async-bulk observed " +
                      "frame enforcement; opaque-backend-admission-not-preemptible"
                : receipt.preinteractiveBootstrapEnabled
                    ? "interactive-frames-after-preinteractive-bootstrap; " +
                      "opaque-backend-admission-not-preemptible"
                    : "strict-admission; opaque-backend-observed-not-preemptible";
            return execution;
        }

        private void DisposePreparedBackends()
        {
            foreach (PhaseExecution execution in preparedExecutions.Values)
            {
                execution.backend?.Dispose();
                execution.backend = null;
            }
            preparedExecutions.Clear();
        }

        private void DisposeActivatedBackends()
        {
            for (int index = 0; index < activatedExecutions.Count; index++)
            {
                PhaseExecution execution = activatedExecutions[index];
                if (execution.job != null)
                {
                    if (!execution.job.IsCompleted)
                        continue;
                    execution.job.Complete();
                    execution.job.Dispose();
                    execution.job = null;
                    execution.jobScheduled = false;
                }
                execution.backend?.Dispose();
                execution.backend = null;
            }
        }

        private bool Enqueue(PsoWarmupPhasePlan phase)
        {
            if (activated.Contains(phase.phase))
                return false;

            if (!preparedExecutions.TryGetValue(
                    phase.phase,
                    out PhaseExecution execution))
            {
                throw new InvalidOperationException(
                    "No prepared execution exists for phase '" + phase.phase + "'.");
            }
            activated.Add(phase.phase);
            preparedExecutions.Remove(phase.phase);
            double now = Time.realtimeSinceStartupAsDouble;
            execution.activatedAt = now;
            PsoSystemMarkers.Emit("phase-start", phase.phase);
            execution.stopwatch.Restart();
            phaseReceipts.Add(execution.receipt);
            activatedExecutions.Add(execution);
            workItems.Add(execution);
            if (!runStopwatch.IsRunning)
                runStopwatch.Start();
            completionRaised = false;
            return true;
        }

        /// <summary>
        /// Completes each required startup hot set before the first scene frame can
        /// be presented. Opaque driver work cannot be preempted or bounded per state,
        /// so completing the set is the only hard first-present guarantee. This is
        /// startup latency, not silently discarded frame time; the duration is
        /// retained in the phase receipt.
        /// </summary>
        private void RunPreinteractiveBootstrap()
        {
            try
            {
                var bootstrapItems = new List<PhaseExecution>();
                for (int index = 0; index < workItems.Count; index++)
                {
                    PhaseExecution execution = workItems[index];
                    if (ShouldRunPreinteractiveBootstrap(execution))
                        bootstrapItems.Add(execution);
                }

                for (int index = 0; index < bootstrapItems.Count; index++)
                {
                    PhaseExecution execution = bootstrapItems[index];
                    EnsureLoaded(execution);
                    int remaining = Math.Max(
                        0,
                        execution.backend.TotalStateCount -
                        execution.backend.CompletedStateCount);
                    if (remaining == 0 || execution.backend.IsWarmedUp)
                    {
                        CompletePhase(execution);
                        continue;
                    }

                    // A hard first-present gate must complete the entire required
                    // hot set. Sampling one opaque driver call cannot prove that a
                    // later state will not trigger another non-preemptible compile.
                    int batchSize = remaining;
                    int completedBefore = execution.backend.CompletedStateCount;
                    double started = Time.realtimeSinceStartupAsDouble;
                    IPsoWarmupBatch batch = execution.backend.Schedule(batchSize, true);
                    try
                    {
                        batch.Complete();
                    }
                    finally
                    {
                        batch.Dispose();
                    }

                    double duration = Math.Max(
                        0.0,
                        (Time.realtimeSinceStartupAsDouble - started) * 1000.0);
                    int completed = execution.backend.CompletedStateCount;
                    int delta = Math.Max(0, completed - completedBefore);
                    execution.policy.ObserveBatch(delta, duration);
                    execution.receipt.completedGraphicsStates = completed;
                    execution.receipt.batchCount++;
                    execution.receipt.preinteractiveBootstrapBatchCount++;
                    execution.receipt.preinteractiveBootstrapMilliseconds += duration;
                    execution.batchSizes.Add(batchSize);
                    execution.safeBatchSizes.Add(0);
                    execution.deadlineBatchSizes.Add(0);
                    execution.predictedBatchDurations.Add(0.0);
                    execution.batchDurations.Add(duration);
                    execution.admissionReasons.Add(
                        "preinteractive-required-hot-set-gate");

                    if (IsPhaseComplete(execution))
                        CompletePhase(execution);
                }

                WriteReceipt();
            }
            catch (Exception exception)
            {
                Fail(
                    "Preinteractive warmup bootstrap failed: " + exception.Message,
                    exception);
            }
        }

        private bool ShouldRunPreinteractiveBootstrap(PhaseExecution execution)
        {
            return execution.plan.preinteractiveBootstrap &&
                   string.Equals(
                       strategy,
                       ScheduledStrategy,
                       StringComparison.Ordinal) &&
                   !PsoCommandLine.Current.HasFlag(
                       PsoConstants.DisablePreinteractiveBootstrapArgument);
        }

        private void Update()
        {
            if (quitting || failed || plan == null)
                return;

            try
            {
                ObserveIdleFrames();
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
            execution.policy.ObserveFrame(
                frameMilliseconds,
                true,
                execution.scheduledBatchSize);
        }

        private void ObserveIdleFrames()
        {
            double frameMilliseconds = Time.unscaledDeltaTime * 1000.0;
            for (int index = 0; index < workItems.Count; index++)
            {
                PhaseExecution execution = workItems[index];
                if (execution.policy == null || execution == scheduled)
                    continue;
                execution.policy.ObserveFrame(frameMilliseconds, false, 0);
            }
        }

        private PhaseExecution SelectNextWork()
        {
            if (workItems.Count <= 0 ||
                workItems.Count >= schedulerCandidateBuffers.Length)
            {
                throw new InvalidOperationException(
                    "No preallocated scheduler buffer exists for " +
                    workItems.Count + " active phases.");
            }
            schedulerCandidates = schedulerCandidateBuffers[workItems.Count];
            double now = Time.realtimeSinceStartupAsDouble;
            double riskWindow = 0.0;
            for (int index = 0; index < workItems.Count; index++)
            {
                PhaseExecution item = workItems[index];
                int remaining = item.backend == null
                    ? item.plan.graphicsStateCount
                    : Math.Max(
                        0,
                        item.backend.TotalStateCount -
                        item.backend.CompletedStateCount);
                double cost = item.policy == null
                    ? item.plan.estimatedMillisecondsPerState
                    : item.policy.EstimatedMillisecondsPerState;
                double elapsed = (now - item.activatedAt) * 1000.0;
                schedulerCandidates[index] = new PsoSchedulerCandidate(
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
                schedulerCandidates,
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
            if (execution.backend == null || execution.policy == null)
            {
                throw new InvalidOperationException(
                    "Phase '" + execution.plan.phase +
                    "' was activated without a resident prepared backend.");
            }
        }

        private void ScheduleNextBatch(PhaseExecution execution)
        {
            int remaining = Math.Max(
                0,
                execution.backend.TotalStateCount -
                execution.backend.CompletedStateCount);
            if (remaining == 0 || execution.backend.IsWarmedUp)
            {
                CompletePhase(execution);
                return;
            }

            execution.completedBeforeBatch = execution.backend.CompletedStateCount;
            int batchSize;
            PsoBudgetAdmission admission;
            if (string.Equals(strategy, ThroughputStrategy, StringComparison.Ordinal))
            {
                batchSize = remaining;
                admission = new PsoBudgetAdmission(
                    batchSize,
                    batchSize,
                    batchSize,
                    0.0,
                    0.0,
                    true,
                    false,
                    "throughput-control");
                execution.job = execution.backend.Schedule(batchSize, true);
            }
            else if (execution.nativeAsyncBulkDeadline)
            {
                double elapsed = (Time.realtimeSinceStartupAsDouble -
                                  execution.activatedAt) * 1000.0;
                double deadlineRemaining = execution.plan.deadlineMilliseconds <= 0.0
                    ? double.PositiveInfinity
                    : execution.plan.deadlineMilliseconds - elapsed;

                // The policy gates the tiny main-thread dispatch against current
                // frame headroom. The separately reported estimate models worker
                // completion time; it must fit the content-use deadline before the
                // opaque native job is admitted.
                PsoBudgetAdmission dispatchGate = execution.policy.Evaluate(
                    1,
                    deadlineRemaining,
                    false);
                int requestedWorkers = PsoCommandLine.Current.GetInt(
                    PsoConstants.AsyncPsoJobCountArgument,
                    -1,
                    -1,
                    1024);
                int workerCount = requestedWorkers > 0
                    ? requestedWorkers
                    : Math.Max(1, SystemInfo.processorCount);
                double predictedBackground =
                    remaining *
                    execution.policy.EstimatedMillisecondsPerState *
                    execution.policy.CostSafetyMultiplier /
                    workerCount;
                bool deadlineFeasible =
                    double.IsPositiveInfinity(deadlineRemaining) ||
                    predictedBackground <= Math.Max(0.0, deadlineRemaining);

                if (!dispatchGate.IsAdmitted || !deadlineFeasible)
                {
                    execution.receipt.deferredFrameCount++;
                    if (!deadlineFeasible)
                        execution.receipt.deadlineInfeasibleBatchCount++;
                    return;
                }

                batchSize = remaining;
                admission = new PsoBudgetAdmission(
                    batchSize,
                    dispatchGate.SafeBatchSize,
                    remaining,
                    dispatchGate.PredictedBatchMilliseconds,
                    dispatchGate.AvailableBudgetMilliseconds,
                    true,
                    dispatchGate.Calibration,
                    "native-async-bulk-deadline");
                execution.receipt.predictedBackgroundCompletionMilliseconds =
                    predictedBackground;
                execution.job = execution.backend.Schedule(batchSize, true);
            }
            else
            {
                double elapsed = (Time.realtimeSinceStartupAsDouble -
                                  execution.activatedAt) * 1000.0;
                double deadlineRemaining = execution.plan.deadlineMilliseconds <= 0.0
                    ? double.PositiveInfinity
                    : execution.plan.deadlineMilliseconds - elapsed;
                admission = execution.policy.Evaluate(
                    remaining,
                    deadlineRemaining,
                    execution.plan.hotSetTier == 0);
                batchSize = admission.BatchSize;
                if (!admission.IsAdmitted)
                {
                    execution.receipt.deferredFrameCount++;
                    return;
                }
                execution.job = execution.backend.Schedule(batchSize, false);
            }

            execution.scheduledBatchSize = batchSize;
            execution.jobScheduledAt = Time.realtimeSinceStartupAsDouble;
            execution.jobScheduled = true;
            execution.receipt.batchCount++;
            execution.safeBatchSizes.Add(admission.SafeBatchSize);
            execution.deadlineBatchSizes.Add(admission.DeadlineBatchSize);
            execution.predictedBatchDurations.Add(
                admission.PredictedBatchMilliseconds);
            execution.admissionReasons.Add(admission.Reason);
            if (!string.Equals(
                    admission.Reason,
                    "throughput-control",
                    StringComparison.Ordinal) &&
                admission.PredictedBatchMilliseconds >
                admission.AvailableBudgetMilliseconds + 0.000001)
                execution.schedulerAdmissionBudgetMet = false;
            if (!admission.DeadlineFeasible)
                execution.receipt.deadlineInfeasibleBatchCount++;
            scheduled = execution;
        }

        private void CompleteScheduledBatch(PhaseExecution execution)
        {
            execution.job.Complete();
            execution.job.Dispose();
            execution.job = null;
            execution.jobScheduled = false;
            double duration = Math.Max(
                0.0,
                (Time.realtimeSinceStartupAsDouble - execution.jobScheduledAt) * 1000.0);
            int completed = execution.backend.CompletedStateCount;
            int delta = Math.Max(0, completed - execution.completedBeforeBatch);
            execution.policy.ObserveBatch(delta, duration);
            execution.receipt.completedGraphicsStates = completed;
            execution.receipt.completedWarmupPermutations = completed;
            execution.batchSizes.Add(execution.scheduledBatchSize);
            execution.batchDurations.Add(duration);
            if (execution.nativeAsyncBulkDeadline)
                execution.receipt.observedBackgroundCompletionMilliseconds = duration;
        }

        private static bool IsPhaseComplete(PhaseExecution execution)
        {
            return execution.backend.IsWarmedUp ||
                   execution.backend.CompletedStateCount >=
                   execution.backend.TotalStateCount;
        }

        private void CompletePhase(PhaseExecution execution)
        {
            if (!workItems.Remove(execution))
                return;

            execution.stopwatch.Stop();
            execution.receipt.completedGraphicsStates =
                execution.backend.IsWarmedUp
                    ? execution.backend.TotalStateCount
                    : execution.backend.CompletedStateCount;
            execution.receipt.completedWarmupPermutations =
                execution.backend.CompletedStateCount;
            execution.receipt.backendReportedWarmedUp =
                execution.backend.IsWarmedUp;
            execution.receipt.finalBatchSize = execution.policy.CurrentBatchSize;
            execution.receipt.elapsedMilliseconds =
                execution.stopwatch.Elapsed.TotalMilliseconds;
            execution.receipt.observedMillisecondsPerState =
                execution.policy.EstimatedMillisecondsPerState;
            if (execution.receipt.minimumSlackMilliseconds == double.MaxValue)
                execution.receipt.minimumSlackMilliseconds = 0.0;
            execution.receipt.deadlineMissed =
                execution.plan.deadlineMilliseconds > 0.0 &&
                execution.receipt.elapsedMilliseconds >
                execution.plan.deadlineMilliseconds;
            execution.receipt.coldStartBatchMilliseconds =
                execution.policy.ColdStartBatchMilliseconds;
            execution.receipt.budgetViolationCount =
                execution.policy.BudgetViolationCount;
            execution.receipt.minimumBatchBudgetViolationCount =
                execution.policy.MinimumBatchViolationCount;
            execution.receipt.circuitBreakerTripCount =
                execution.policy.CircuitBreakerTripCount;
            execution.receipt.deferredFrameCount = Math.Max(
                execution.receipt.deferredFrameCount,
                execution.policy.DeferredRecommendationCount);
            execution.receipt.hardFrameBudgetMet =
                execution.policy.BudgetViolationCount == 0;
            bool strictAdmission = string.Equals(
                execution.receipt.budgetPolicy,
                "strict-admission",
                StringComparison.Ordinal);
            execution.receipt.hardFrameBudgetFeasible =
                strictAdmission && execution.policy.IsBudgetFeasible;
            execution.receipt.schedulerAdmissionBudgetMet =
                execution.schedulerAdmissionBudgetMet;
            execution.receipt.maximumBudgetOverrunMilliseconds =
                execution.policy.MaximumBudgetOverrunMilliseconds;
            execution.receipt.hardFrameBudgetOutcome = !strictAdmission
                ? "not-applicable-throughput"
                : execution.policy.BudgetViolationCount == 0
                    ? "met"
                    : execution.policy.IsBudgetFeasible
                        ? "violated-model-error"
                        : "unachievable-at-minimum-batch";
            execution.receipt.completed = true;
            PsoSystemMarkers.Emit("phase-end", execution.plan.phase);

            if (!ShouldDeferEvidenceWrite())
            {
                FinalizeExecutionReceipt(execution);
                Debug.Log("[ShaderHitchPipeline] Warmed phase '" + execution.plan.phase +
                          "': " + execution.receipt.completedGraphicsStates + "/" +
                          execution.receipt.totalGraphicsStates + " states in " +
                          execution.receipt.elapsedMilliseconds.ToString("F1") +
                          " ms across " + execution.receipt.batchCount + " batches.");
                WriteReceipt();
            }
        }

        private static void FinalizeExecutionReceipt(PhaseExecution execution)
        {
            if (execution.receiptFinalized)
                return;

            execution.receipt.warmupFrameTimes = PsoStatistics.Calculate(
                execution.frameTimes,
                execution.plan.targetFrameMilliseconds);
            execution.receipt.maximumObservedFrameMilliseconds = Math.Max(
                execution.receipt.maximumObservedFrameMilliseconds,
                execution.receipt.warmupFrameTimes.maximumMilliseconds);
            execution.receipt.batchSizes = execution.batchSizes.ToArray();
            execution.receipt.safeBatchSizes = execution.safeBatchSizes.ToArray();
            execution.receipt.deadlineBatchSizes =
                execution.deadlineBatchSizes.ToArray();
            execution.receipt.predictedBatchDurationsMilliseconds =
                execution.predictedBatchDurations.ToArray();
            execution.receipt.batchDurationsMilliseconds =
                execution.batchDurations.ToArray();
            execution.receipt.admissionReasons =
                execution.admissionReasons.ToArray();
            execution.receipt.warmupFrameTimeSamplesMilliseconds =
                execution.frameTimes.ToArray();
            if (execution.job == null)
            {
                execution.backend?.Dispose();
                execution.backend = null;
            }
            execution.receiptFinalized = true;
        }

        private void FinalizePhaseReceipts()
        {
            for (int index = 0; index < activatedExecutions.Count; index++)
            {
                PhaseExecution execution = activatedExecutions[index];
                if (execution.receipt.completed)
                    FinalizeExecutionReceipt(execution);
            }
        }

        private void RaiseCompletion()
        {
            if (completionRaised || failed)
                return;
            completionRaised = true;
            if (runStopwatch != null && runStopwatch.IsRunning)
                runStopwatch.Stop();
            if (!ShouldDeferEvidenceWrite())
                WriteReceipt();
            WarmupCompleted?.Invoke();
        }

        private bool ShouldDeferEvidenceWrite()
        {
            return !quitting &&
                   PsoCommandLine.Current.HasFlag(PsoConstants.BenchmarkArgument);
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
                PhaseExecution execution = workItems[index];
                execution.stopwatch.Stop();
                execution.receipt.elapsedMilliseconds =
                    execution.stopwatch.Elapsed.TotalMilliseconds;
                execution.receipt.error = failure;
                try
                {
                    if (execution.job != null)
                    {
                        execution.job.Complete();
                        execution.job.Dispose();
                        execution.job = null;
                    }
                    execution.backend?.Dispose();
                    execution.backend = null;
                }
                catch (Exception cleanupException)
                {
                    Debug.LogWarning(
                        "[ShaderHitchPipeline] Failure cleanup also failed: " +
                        cleanupException.Message);
                }
            }
            workItems.Clear();
            DisposePreparedBackends();
            if (runStopwatch != null && runStopwatch.IsRunning)
                runStopwatch.Stop();
            Debug.LogError("[ShaderHitchPipeline] " + message);
            WriteReceipt();
        }

        private void WriteReceipt()
        {
            FinalizePhaseReceipts();
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
                environment = PsoUnityEnvironment.Capture(),
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
            DisposePreparedBackends();
            if (scheduled != null && scheduled.jobScheduled && scheduled.job.IsCompleted)
                CompleteScheduledBatch(scheduled);
            for (int index = 0; index < workItems.Count; index++)
            {
                PhaseExecution execution = workItems[index];
                execution.stopwatch.Stop();
                execution.receipt.completedGraphicsStates = execution.backend == null
                    ? 0
                    : execution.backend.CompletedStateCount;
                execution.receipt.elapsedMilliseconds =
                    execution.stopwatch.Elapsed.TotalMilliseconds;
                execution.receipt.error = "Application quit before phase completion.";
            }
            SaveCacheMissesNow();
            WriteReceipt();
        }

        private void OnDestroy()
        {
            DisposePreparedBackends();
            DisposeActivatedBackends();
            StopFeedbackTrace();
            if (instance == this)
                instance = null;
        }
    }
}
