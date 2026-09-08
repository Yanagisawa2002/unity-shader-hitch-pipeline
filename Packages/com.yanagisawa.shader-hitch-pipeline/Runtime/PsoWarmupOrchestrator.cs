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
            public PsoPhaseStatus status;
            public PsoWarmupPhasePlan plan;
            public IPsoWarmupBackend backend;
            public PsoAdaptiveBatchPolicy policy;
            public bool nativeAsyncBulkDeadline;
            public PsoWarmupPhaseReceipt receipt;
            public readonly List<double> frameTimes = new List<double>(1024);
            public readonly List<int> batchSizes = new List<int>(512);
            public readonly List<int> safeBatchSizes = new List<int>(512);
            public readonly List<int> deadlineBatchSizes = new List<int>(512);
            public readonly List<double> predictedBatchDurations = new List<double>(512);
            public readonly List<double> batchDurations = new List<double>(512);
            public readonly List<string> admissionReasons = new List<string>(512);
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

        private PsoWarmupPlanDocument plan;
        public PsoCompatibilityResult Compatibility { get; private set; }
        private PsoWarmupScheduler scheduler;
        private PsoSchedulingOptions schedulingOptions = new PsoSchedulingOptions();
        private PsoEnvironmentSnapshot schedulingEnvironment;
        private double nonInteractiveBudget;
        private static readonly List<PsoWarmupScheduler> retiredSchedulers = new List<PsoWarmupScheduler>();
        private sealed class UnityClock : IPsoClock
        {
            public double NowMilliseconds => Time.realtimeSinceStartupAsDouble * 1000.0;
        }
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
        private PsoHotsetDecision startupHotset;
        public PsoHotsetDecision StartupHotset => startupHotset;
        // Install a current-build collection AND cost compatibility proof. An absent
        // bridge conservatively allows only caller-required startup work.
        public static Func<PsoWarmupPlanDocument, PsoStartupHotsetDocument, bool> StartupHotsetCompatibilityValidator;

        public static PsoWarmupOrchestrator Instance => instance;
        public bool HasLoadedPlan => plan != null;
        public bool IsBusy => scheduler != null && scheduler.IsBusy;
        public bool IsComplete => HasLoadedPlan && !failed && scheduler != null && scheduler.IsComplete;
        public bool HasFailed => failed;
        public string Failure => failure;
        public string PlanPath => planPath;
        public string PlanHash => planHash;
        public string Strategy => strategy;
        public bool FeedbackTraceArmed => feedbackTraceReceipt != null && feedbackTraceReceipt.armed;
        public string FeedbackTraceError => feedbackTraceReceipt == null ? "No loaded plan." : feedbackTraceReceipt.error;
        public int PendingPhaseCount => scheduler == null ? 0 : scheduler.PendingCount;

        public bool IsPhaseComplete(string phaseName) => !failed && GetPhaseStatus(phaseName)?.IsComplete == true;
        public PsoPhaseStatus GetPhaseStatus(string phaseName) => scheduler?.GetPhaseStatus(phaseName);
        public bool IsPhaseUnloading(string phaseName) => !string.IsNullOrWhiteSpace(phaseName) && scheduler?.IsUnloading(phaseName) == true;

        public bool CancelPhase(string phaseName)
        {
            bool changed = scheduler != null && scheduler.CancelPhase(phaseName);
            SyncExecutions();
            return changed;
        }

        public bool UnloadPhase(string phaseName)
        {
            bool changed = scheduler != null && scheduler.UnloadPhase(phaseName);
            SyncExecutions();
            return changed;
        }

        /// <summary>Opt-in policy configuration after LoadPlan and before the first activation.</summary>
        public void ConfigureScheduling(PsoSchedulingOptions options)
        {
            EnsurePlan();
            if (activatedExecutions.Count != 0) throw new InvalidOperationException("Configure scheduling before activation.");
            if (options == null) throw new ArgumentNullException(nameof(options));
            var validated = options.Copy();
            scheduler.Dispose();
            schedulingOptions = validated;
            strategy = validated.PolicyId;
            scheduler = new PsoWarmupScheduler(new UnityClock(), validated,
                PsoSchedulingOptions.CostIdentity(schedulingEnvironment, planHash));
            try { ValidateBackendAdapter(); }
            catch { scheduler.Dispose(); throw; }
        }

        /// <summary>Explicit caller-owned loading window with a per-admission estimate cap, not a hard latency limit.</summary>
        public void SetNonInteractiveWindow(bool active, double budgetMilliseconds = 0)
        {
            if (active && (double.IsNaN(budgetMilliseconds) || double.IsInfinity(budgetMilliseconds) || budgetMilliseconds <= 0))
                throw new ArgumentOutOfRangeException(nameof(budgetMilliseconds));
            nonInteractiveBudget = active ? budgetMilliseconds : 0;
        }

        /// <summary>Call after device, quality, content/build, driver or execution-context changes.
        /// Compatibility failure retires work; a cost-only change invalidates observations, including in-flight samples.</summary>
        public bool RefreshSchedulingEnvironment(PsoEnvironmentSnapshot current)
        {
            EnsurePlan();
            if (current == null) throw new ArgumentNullException(nameof(current));
            var result = PsoCompatibility.Evaluate(plan.compatibility, current);
            if ((!result.legacy && !result.collectionCompatible) ||
                current.qualityLevelName != schedulingEnvironment.qualityLevelName ||
                current.graphicsDeviceType != schedulingEnvironment.graphicsDeviceType)
            {
                Fail("Scheduling environment is incompatible with the loaded collection: " + string.Join("; ", result.collectionReasons));
                return false;
            }
            scheduler.InvalidateCostIdentity(PsoSchedulingOptions.CostIdentity(current, planHash));
            schedulingEnvironment = current;
            Compatibility = result;
            return true;
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
            string hotsetFile = commandLine.GetString("-pso-startup-hotset", string.Empty);
            if (!string.IsNullOrWhiteSpace(hotsetFile))
            {
                PsoStartupHotsetDocument policy = null;
                try { policy = JsonUtility.FromJson<PsoStartupHotsetDocument>(File.ReadAllText(hotsetFile)); }
                catch (Exception exception) { Debug.LogWarning("[ShaderHitchPipeline] Hotset policy fallback: " + exception.Message); }
                orchestrator.ConfigureStartupHotset(policy);
            }

            string explicitPhase = commandLine.GetString(
                PsoConstants.WarmupPhaseArgument,
                string.Empty);
            if (orchestrator.startupHotset != null || string.IsNullOrWhiteSpace(explicitPhase))
                orchestrator.ActivateStartupPhases();
            if (!string.IsNullOrWhiteSpace(explicitPhase))
                orchestrator.ActivatePhase(explicitPhase);
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

            scheduler?.Dispose();
            StopFeedbackTrace();

            plan = null;
            planPath = Path.GetFullPath(requestedPlanPath);
            plan = PsoPlanValidation.LoadAndValidate(
                planPath,
                validateEnvironment,
                true);
            planHash = PsoFileUtility.ComputeSha256(planPath);
            schedulingEnvironment = PsoUnityEnvironment.Capture();
            Compatibility = PsoCompatibility.Evaluate(plan.compatibility, schedulingEnvironment);
            // Strict plans cannot bypass current-build identity through validateEnvironment=false.
            if (!Compatibility.legacy && !Compatibility.collectionCompatible)
            {
                plan = null;
                throw new InvalidDataException(string.Join(Environment.NewLine, Compatibility.collectionReasons));
            }
            PsoCompatibility.ResetCostPriors(plan, Compatibility);
            Debug.Log("[ShaderHitchPipeline] Compatibility: " + JsonUtility.ToJson(Compatibility));
            string costCachePath = PsoCommandLine.Current.GetString("-pso-cost-cache", string.Empty);
            CostCacheRequested = !string.IsNullOrWhiteSpace(costCachePath);
            CostCacheApplied = false;
            CostCacheReasons = Array.Empty<string>();
            if (CostCacheRequested)
            {
                CostCacheApplied = PsoCostCacheStorage.TryApply(costCachePath, plan, out string[] cacheReasons);
                CostCacheReasons = cacheReasons;
                if (!CostCacheApplied)
                    Debug.LogWarning("[ShaderHitchPipeline] Cost cache ignored: " + string.Join("; ", cacheReasons));
            }
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
            startupHotset = null;
            activated.Clear();
            workItems.Clear();
            phaseReceipts.Clear();
            activatedExecutions.Clear();
            schedulingOptions = ReadSchedulingOptions(strategy, commandLine);
            scheduler = new PsoWarmupScheduler(new UnityClock(), schedulingOptions,
                PsoSchedulingOptions.CostIdentity(schedulingEnvironment, planHash));
            nonInteractiveBudget = 0;
            runStopwatch = new Stopwatch();
            int phaseCount = plan.phases.Length;
            workItems.Capacity = Math.Max(workItems.Capacity, phaseCount);
            phaseReceipts.Capacity = Math.Max(phaseReceipts.Capacity, phaseCount);
            activatedExecutions.Capacity = Math.Max(
                activatedExecutions.Capacity,
                phaseCount);
            // Populate and clear once so later phase activation reuses HashSet storage.
            for (int index = 0; index < phaseCount; index++)
                activated.Add(plan.phases[index].phase);
            activated.Clear();
            try
            {
                PrepareFeedbackTrace();
                ValidateBackendAdapter();
            }
            catch
            {
                scheduler.Dispose();
                StopFeedbackTrace();
                plan = null;
                throw;
            }

            Debug.Log("[ShaderHitchPipeline] Loaded plan '" + plan.profileId +
                      "' with " + plan.phases.Length + " phases; strategy=" +
                      strategy + ".");
        }

        public bool CostCacheRequested { get; private set; }
        public bool CostCacheApplied { get; private set; }
        public string[] CostCacheReasons { get; private set; } = Array.Empty<string>();

        public void SaveCostCache(string path)
        {
            if (!IsComplete) throw new InvalidOperationException("Cost export requires completed warmup.");
            var entries = new List<PsoCostCacheEntry>();
            foreach (PhaseExecution item in activatedExecutions)
                if (!schedulingOptions.ConservativeAdmission && schedulingOptions.EnableAdaptiveCost &&
                    item.policy != null && item.policy.HasMeasuredCostSlope && !item.nativeAsyncBulkDeadline && item.receipt.completed)
                    entries.Add(new PsoCostCacheEntry
                    {
                        phase = item.plan.phase, collectionSha256 = item.plan.collectionSha256,
                        millisecondsPerState = item.policy.EstimatedMillisecondsPerState,
                        observedBatches = item.policy.BatchObservationCount
                    });
            PsoCostCacheStorage.Save(path, plan, entries.ToArray());
        }

        /// <summary>Opt-in selection after validated LoadPlan, before any activation.</summary>
        public void ConfigureStartupHotset(PsoStartupHotsetDocument policy)
        {
            EnsurePlan();
            if (activated.Count != 0) throw new InvalidOperationException("Configure hotset before activation.");
            startupHotset = PsoStartupHotset.PrepareValidated(plan, planHash, policy,
                StartupHotsetCompatibilityValidator ?? PsoUnityIntegratedCompatibility.ValidateConfiguredHotset);
            PsoFileUtility.WriteJsonAtomic(Path.Combine(outputRoot, "Receipts", runId + ".hotset.json"), startupHotset);
        }

        public void ActivateStartupHotsetAndGate(PsoStartupHotsetDocument policy)
        {
            ConfigureStartupHotset(policy);
            ActivateStartupPhases();
            RunPreinteractiveBootstrap();
        }

        public void ActivateStartupPhases()
        {
            EnsurePlan();
            var startup = new List<PsoWarmupPhasePlan>();
            for (int index = 0; index < plan.phases.Length; index++)
            {
                if (startupHotset == null ? plan.phases[index].prewarmAtStartup :
                    Array.IndexOf(startupHotset.startupUnitIds, plan.phases[index].phase) >= 0)
                    startup.Add(plan.phases[index]);
            }
            startup.Sort(ComparePlans);
            for (int index = 0; index < startup.Count; index++)
                Enqueue(startup[index]);
            TryArmFeedbackTrace();
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

            if (feedbackTraceCollection == null || !feedbackTraceReceipt.armed)
            {
                feedbackTraceReceipt.error =
                    "Feedback is unavailable: a complete dependency-resolved plan baseline has not been armed. Zero counts are not coverage evidence.";
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

            if (!quitting)
            {
                feedbackTraceReceipt.armed = feedbackTraceCollection.BeginTrace();
                if (!feedbackTraceReceipt.armed) feedbackTraceReceipt.error = "Could not resume the plan-scoped feedback trace.";
            }

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
                error = requested ? "Feedback unavailable until the full plan baseline resolves; call TryArmFeedbackTrace after dependencies are ready." : string.Empty,
            };
        }

        /// <summary>Arms only a complete, count-validated plan baseline. Failure leaves feedback explicitly
        /// unavailable and never prevents dependency-ready phases from warming. Earlier draws are outside this trace window.</summary>
        public bool TryArmFeedbackTrace()
        {
            EnsurePlan();
            if (feedbackTraceReceipt == null || !feedbackTraceReceipt.requested) return false;
            if (feedbackTraceReceipt.armed) return true;
            try
            {
                LoadFeedbackBaseline();
                feedbackTraceReceipt.error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                StopFeedbackTrace();
                feedbackTraceReceipt.error = "Feedback unavailable; no complete plan baseline: " + exception.Message;
                return false;
            }
        }

        private void LoadFeedbackBaseline()
        {
            var combined = new GraphicsStateCollection();
            feedbackTraceCollection = combined; // Cleanup owns it even if any source load/append fails.
            string directory = Path.GetDirectoryName(planPath);
            for (int index = 0; index < plan.phases.Length; index++)
            {
                string collectionPath = PsoFileUtility.ResolveChildPath(
                    directory,
                    plan.phases[index].collectionFile);
                var source = new GraphicsStateCollection();
                try
                {
                    if (!source.LoadFromFile(collectionPath))
                        throw new IOException("Could not load feedback baseline collection '" + collectionPath + "'.");
                    if (source.runtimePlatform != Application.platform || source.graphicsDeviceType != SystemInfo.graphicsDeviceType)
                        throw new InvalidDataException("Feedback baseline platform/API does not match the running environment.");
                    PsoCollectionReadiness.RequireFullCollection(plan.phases[index].phase,
                        plan.phases[index].graphicsStateCount, source.totalGraphicsStateCount);
                    if (!PsoGraphicsStateCollectionCompatibility.Append(combined, source))
                        throw new InvalidDataException("Could not append feedback baseline collection '" + collectionPath + "'.");
                }
                finally { Destroy(source); }
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
            if (feedbackTraceReceipt != null) feedbackTraceReceipt.armed = false;
            if (feedbackTraceCollection == null)
                return;
            try
            {
                if (feedbackTraceCollection.isTracing) feedbackTraceCollection.EndTrace();
            }
            finally
            {
                Destroy(feedbackTraceCollection);
                feedbackTraceCollection = null;
            }
        }

        private void ValidateBackendAdapter()
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

            // File hashes were validated by LoadPlan. Native collections must be resolved
            // only when the host has loaded the phase's shader dependencies and activates it.
        }

        private PhaseExecution CreateExecution(
            PsoWarmupPhasePlan phase,
            IPsoWarmupBackend backend, bool register = true)
        {
            PsoCollectionReadiness.RequireFullCollection(phase.phase, phase.graphicsStateCount, backend.TotalStateCount);
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
            if (schedulingOptions.FixedBatchSize > 0 && backend.PreferNativeAsyncBulkForDeadline)
                throw new NotSupportedException("fixed-progressive is unsupported by this backend mode; native bulk is not an equivalent policy.");
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
                    cooldownFrames, schedulingOptions.ConservativeAdmission),
                receipt = receipt,
                nativeAsyncBulkDeadline = nativeAsyncBulkDeadline,
            };
            receipt.preinteractiveBootstrapEnabled =
                ShouldRunPreinteractiveBootstrap(execution);
            receipt.hardBudgetGuaranteeScope = throughput
                ? "none; throughput-control"
                : nativeAsyncBulkDeadline
                    ? "estimated-full-batch-admission; native-async-bulk-not-preemptible; no-hard-latency-bound"
                : receipt.preinteractiveBootstrapEnabled
                    ? "interactive-frames-after-preinteractive-bootstrap; " +
                      "opaque-backend-admission-not-preemptible"
                    : "strict-admission; opaque-backend-observed-not-preemptible";
            if (register) scheduler.Register(phase, backend, execution.policy, nativeAsyncBulkDeadline);
            return execution;
        }

        private bool Enqueue(PsoWarmupPhasePlan phase)
        {
            if (failed) return false;
            if (scheduler.IsUnloading(phase.phase)) return false;
            var previousStatus = scheduler.GetPhaseStatus(phase.phase);
            if (previousStatus != null && (!previousStatus.IsTerminal || previousStatus.IsComplete) && scheduler.IsResident(phase.phase))
                return false;
            PhaseExecution execution;
            if (!scheduler.IsResident(phase.phase))
            {
                string path = PsoFileUtility.ResolveChildPath(Path.GetDirectoryName(planPath), phase.collectionFile);
                var backend = new PsoUnityGraphicsStateWarmupBackend(path);
                try { execution = CreateExecution(phase, backend); }
                catch { backend.Dispose(); throw; }
            }
            else
            {
                // A new activation queues behind any cancelled activation's fence.
                // Its predecessor keeps a distinct cancelled receipt and the resident cost model.
                PhaseExecution previous = activatedExecutions.FindLast(item =>
                    string.Equals(item.plan.phase, phase.phase, StringComparison.OrdinalIgnoreCase));
                execution = CreateExecution(phase, previous.backend, false);
                execution.policy = previous.policy;
            }
            execution.status = scheduler.Activate(phase.phase);
            activated.Add(phase.phase);
            PsoSystemMarkers.Emit("phase-start", phase.phase);
            phaseReceipts.Add(execution.receipt);
            activatedExecutions.Add(execution);
            workItems.Add(execution);
            if (!runStopwatch.IsRunning) runStopwatch.Start();
            completionRaised = false;
            return true;
        }

        /// <summary>
        /// Completes each required startup hot set before the first scene frame can
        /// be presented. Opaque driver work cannot be preempted or bounded per state,
        /// so this gate waits for completion. It cannot guarantee a latency bound. This is
        /// startup latency; the duration is
        /// retained in the phase receipt.
        /// </summary>
        private void RunPreinteractiveBootstrap()
        {
            try
            {
                foreach (var execution in new List<PhaseExecution>(workItems))
                    if (ShouldRunPreinteractiveBootstrap(execution)) scheduler.CompleteStartupGate(execution.plan.phase);
                SyncExecutions();
                if (scheduler.HasFailed) { Fail("Preinteractive warmup gate failed; inspect the phase status."); return; }
                WriteReceipt();
            }
            catch (Exception exception) { Fail("Preinteractive warmup bootstrap failed: " + exception.Message, exception); }
        }

        private bool ShouldRunPreinteractiveBootstrap(PhaseExecution execution)
        {
            if (schedulingOptions.FixedBatchSize > 0) return false;
            if (startupHotset != null && Array.IndexOf(startupHotset.startupUnitIds, execution.plan.phase) >= 0)
                return true; // Explicit policy promises a gate for selected and caller-required startup units.
            return execution.plan.preinteractiveBootstrap &&
                   string.Equals(
                       strategy,
                       ThroughputStrategy,
                       StringComparison.Ordinal) == false &&
                   !PsoCommandLine.Current.HasFlag(
                       PsoConstants.DisablePreinteractiveBootstrapArgument);
        }

        private void Update()
        {
            DrainRetiredSchedulers();
            if (quitting || plan == null || scheduler == null) return;
            try
            {
                if (failed) { scheduler.Pump(); SyncExecutions(); return; }
                if (PsoUnityEnvironment.CurrentQualityName() != schedulingEnvironment.qualityLevelName ||
                    SystemInfo.graphicsDeviceType.ToString() != schedulingEnvironment.graphicsDeviceType)
                {
                    RefreshSchedulingEnvironment(PsoUnityEnvironment.Capture());
                    return;
                }
                var activeStatus = scheduler.Active;
                if (activeStatus != null)
                {
                    PhaseExecution execution = activatedExecutions.Find(item => item.status == activeStatus);
                    execution?.frameTimes.Add(Time.unscaledDeltaTime * 1000.0);
                }
                scheduler.Tick(Time.unscaledDeltaTime * 1000.0, nonInteractiveBudget,
                    string.Equals(strategy, ThroughputStrategy, StringComparison.Ordinal));
                SyncExecutions();
                if (scheduler.HasFailed) { Fail("Warmup backend fault; inspect phase status/receipts."); return; }
                if (IsComplete) RaiseCompletion();
            }
            catch (Exception exception) { Fail("Warmup failed: " + exception.Message, exception); }
        }

        private void SyncExecutions()
        {
            if (scheduler == null) return;
            for (int index = workItems.Count - 1; index >= 0; index--)
            {
                var execution = workItems[index];
                var status = execution.status;
                if (status == null || !status.IsTerminal) continue;
                FinalizeExecutionReceipt(execution);
                if (status.HasInFlightBatch) continue;
                workItems.RemoveAt(index);
                PsoSystemMarkers.Emit(status.IsComplete ? "phase-end" : "phase-retired", execution.plan.phase);
            }
        }

        public static void DrainRetiredSchedulers(bool block = false)
        {
            for (int i = retiredSchedulers.Count - 1; i >= 0; i--)
            {
                var retired = retiredSchedulers[i];
                bool done = block ? retired.Drain() : retired.Pump();
                if (done && !retired.HasInFlightBatch && !retired.HasPendingRetirement) retiredSchedulers.RemoveAt(i);
            }
        }

        private static void FinalizeExecutionReceipt(PhaseExecution execution)
        {
            if (execution.receiptFinalized) return;
            var status = execution.status;
            if (status == null) return;
            var receipt = execution.receipt;
            var policy = execution.policy;
            receipt.completed = status.IsComplete;
            receipt.error = status.Failure;
            receipt.completedWarmupPermutations = status.CompletedPermutations;
            // Unity reports warmed permutations, which cannot be converted into a partial graphics-state coverage count.
            receipt.completedGraphicsStates = status.BackendReportedWarmedUp ? receipt.totalGraphicsStates : 0;
            receipt.backendReportedWarmedUp = status.BackendReportedWarmedUp;
            receipt.elapsedMilliseconds = status.ElapsedMilliseconds;
            receipt.deadlineMissed = status.DeadlineMissed;
            receipt.minimumSlackMilliseconds = double.IsInfinity(status.MinimumSlackMilliseconds) ? 0 : status.MinimumSlackMilliseconds;
            receipt.schedulerSelectionCount = status.SelectionCount;
            receipt.deferredFrameCount = status.DeferredFrames;
            receipt.deadlineInfeasibleBatchCount = status.DeadlineInfeasibleFrames;
            receipt.finalBatchSize = policy.CurrentBatchSize;
            receipt.observedMillisecondsPerState = policy.EstimatedMillisecondsPerState;
            receipt.coldStartBatchMilliseconds = policy.ColdStartBatchMilliseconds;
            receipt.budgetViolationCount = policy.BudgetViolationCount;
            receipt.minimumBatchBudgetViolationCount = policy.MinimumBatchViolationCount;
            receipt.circuitBreakerTripCount = policy.CircuitBreakerTripCount;
            receipt.maximumBudgetOverrunMilliseconds = policy.MaximumBudgetOverrunMilliseconds;
            receipt.hardFrameBudgetMet = policy.BudgetViolationCount == 0 && status.IsComplete;
            bool strict = receipt.budgetPolicy == "strict-admission";
            receipt.hardFrameBudgetFeasible = strict && policy.IsBudgetFeasible;
            receipt.hardFrameBudgetOutcome = !strict ? "not-applicable-throughput" : !status.IsComplete ? "incomplete" :
                policy.BudgetViolationCount == 0 ? "met" : policy.IsBudgetFeasible ? "violated-model-error" : "unachievable-at-minimum-batch";
            execution.batchSizes.Clear(); execution.safeBatchSizes.Clear(); execution.deadlineBatchSizes.Clear();
            execution.predictedBatchDurations.Clear(); execution.batchDurations.Clear(); execution.admissionReasons.Clear();
            receipt.preinteractiveBootstrapBatchCount = 0; receipt.preinteractiveBootstrapMilliseconds = 0;
            receipt.schedulerAdmissionBudgetMet = strict;
            foreach (var record in status.Batches)
            {
                execution.batchSizes.Add(record.RequestedStates);
                execution.safeBatchSizes.Add(record.Admission.SafeBatchSize);
                execution.deadlineBatchSizes.Add(record.Admission.DeadlineBatchSize);
                execution.predictedBatchDurations.Add(record.Admission.PredictedBatchMilliseconds);
                execution.batchDurations.Add(record.ElapsedMilliseconds);
                execution.admissionReasons.Add(record.Admission.Reason);
                if (record.Admission.Reason == "preinteractive-required-hot-set-gate")
                {
                    receipt.preinteractiveBootstrapBatchCount++;
                    receipt.preinteractiveBootstrapMilliseconds += record.ElapsedMilliseconds;
                }
                else if (record.Admission.PredictedBatchMilliseconds > record.Admission.AvailableBudgetMilliseconds)
                    receipt.schedulerAdmissionBudgetMet = false;
                if (record.NativeBulk)
                {
                    receipt.predictedBackgroundCompletionMilliseconds = record.Admission.PredictedBatchMilliseconds;
                    receipt.observedBackgroundCompletionMilliseconds = record.ElapsedMilliseconds;
                }
            }
            receipt.batchCount = status.Batches.Count;
            receipt.batchSizes = execution.batchSizes.ToArray(); receipt.safeBatchSizes = execution.safeBatchSizes.ToArray();
            receipt.deadlineBatchSizes = execution.deadlineBatchSizes.ToArray();
            receipt.predictedBatchDurationsMilliseconds = execution.predictedBatchDurations.ToArray();
            receipt.batchDurationsMilliseconds = execution.batchDurations.ToArray();
            receipt.admissionReasons = execution.admissionReasons.ToArray();
            receipt.warmupFrameTimes = PsoStatistics.Calculate(execution.frameTimes, execution.plan.targetFrameMilliseconds);
            receipt.maximumObservedFrameMilliseconds = receipt.warmupFrameTimes.maximumMilliseconds;
            receipt.warmupFrameTimeSamplesMilliseconds = execution.frameTimes.ToArray();
            // Resident collections remain owned by the scheduler until explicit unload/plan replacement.
            // This preserves progress across cancellation and prevents destroying in-flight shader owners.
            execution.receiptFinalized = status.IsTerminal && !status.HasInFlightBatch;
        }

        private void FinalizePhaseReceipts()
        {
            for (int index = 0; index < activatedExecutions.Count; index++)
            {
                PhaseExecution execution = activatedExecutions[index];
                FinalizeExecutionReceipt(execution);
            }
        }

        private void RaiseCompletion()
        {
            if (completionRaised || failed || !IsComplete)
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
            // Dispose retires demand but does not block on or forget an unfenced operation.
            try { scheduler?.Dispose(); }
            catch (Exception cleanup) { Debug.LogWarning("[ShaderHitchPipeline] Cleanup retained an owner: " + cleanup.Message); }
            SyncExecutions();
            if (runStopwatch != null && runStopwatch.IsRunning) runStopwatch.Stop();
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
                completed = IsComplete,
                error = failure,
                environment = PsoUnityEnvironment.Capture(),
                phases = phaseReceipts.ToArray(),
                cacheMissTrace = feedbackTraceReceipt,
            };
            PsoFileUtility.WriteJsonAtomic(receiptPath, receipt);
            if (scheduler != null)
            {
                var feedback = PsoSchedulingFeedback.Capture(scheduler, schedulingOptions, planHash);
                feedback.policy = strategy;
                PsoFileUtility.WriteJsonAtomic(receiptPath + ".scheduling.json", feedback);
            }
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
            if (string.Equals(value, "fixed-progressive", StringComparison.OrdinalIgnoreCase)) return "fixed-progressive";
            if (string.Equals(value, "observed-budget", StringComparison.OrdinalIgnoreCase)) return "observed-budget";
            if (string.IsNullOrWhiteSpace(value) ||
                string.Equals(value, ScheduledStrategy, StringComparison.OrdinalIgnoreCase))
                return ScheduledStrategy;
            throw new ArgumentException(
                "Unsupported warmup strategy '" + value +
                "'. Expected scheduled, throughput, fixed-progressive or observed-budget.");
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

        private static PsoSchedulingOptions ReadSchedulingOptions(string mode, PsoCommandLine commandLine)
        {
            var options = mode == "fixed-progressive" ? PsoSchedulingOptions.FixedProgressive(
                commandLine.GetInt("-pso-fixed-batch-size", 4, 1, 1000000)) :
                mode == "observed-budget" ? PsoSchedulingOptions.ObservedBudget() : new PsoSchedulingOptions();
            if (commandLine.HasFlag("-pso-disable-deadlines")) options.EnableDeadlines = false;
            if (commandLine.HasFlag("-pso-disable-adaptive-cost")) options.EnableAdaptiveCost = false;
            if (commandLine.HasFlag("-pso-disable-hotset-priority")) options.EnableHotSetPriority = false;
            if (commandLine.HasFlag("-pso-disable-cost-priority")) options.EnableCostPriority = false;
            return options;
        }

        private void OnApplicationQuit()
        {
            quitting = true;
            try { scheduler?.Dispose(); }
            catch (Exception exception) { Debug.LogWarning("[ShaderHitchPipeline] Shutdown retained an owner: " + exception.Message); }
            SyncExecutions();
            SaveCacheMissesNow();
            WriteReceipt();
        }

        private void OnDestroy()
        {
            if (scheduler != null)
            {
                try { scheduler.Dispose(); }
                catch (Exception exception) { Debug.LogWarning("[ShaderHitchPipeline] Retained shutdown owner: " + exception.Message); }
                if ((scheduler.HasInFlightBatch || scheduler.HasPendingRetirement) && !retiredSchedulers.Contains(scheduler))
                    retiredSchedulers.Add(scheduler);
                scheduler = null;
            }
            StopFeedbackTrace();
            if (instance == this) instance = null;
        }

    }
}
