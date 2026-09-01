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
        private sealed class PhaseExecution
        {
            public PsoWarmupPhasePlan plan;
            public GraphicsStateCollection collection;
            public PsoAdaptiveBatchPolicy policy;
            public JobHandle job;
            public bool jobScheduled;
            public Stopwatch stopwatch;
            public PsoWarmupPhaseReceipt receipt;
        }

        private static PsoWarmupOrchestrator instance;
        private readonly Queue<PsoWarmupPhasePlan> pending =
            new Queue<PsoWarmupPhasePlan>();
        private readonly HashSet<string> activated =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, GraphicsStateCollection> completedCollections =
            new Dictionary<string, GraphicsStateCollection>(StringComparer.OrdinalIgnoreCase);
        private readonly List<PsoWarmupPhaseReceipt> phaseReceipts =
            new List<PsoWarmupPhaseReceipt>();

        private PsoWarmupPlanDocument plan;
        private PhaseExecution current;
        private string planPath;
        private string planHash;
        private string outputRoot;
        private string runId;
        private string startedUtc;
        private string receiptPath;
        private bool failed;
        private string failure;
        private bool quitting;

        public static PsoWarmupOrchestrator Instance => instance;
        public bool HasLoadedPlan => plan != null;
        public bool IsBusy => current != null || pending.Count > 0;
        public bool IsComplete => HasLoadedPlan && !IsBusy && !failed;
        public bool HasFailed => failed;
        public string Failure => failure;
        public string PlanPath => planPath;
        public string PlanHash => planHash;
        public int PendingPhaseCount => pending.Count + (current == null ? 0 : 1);

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
            receiptPath = Path.Combine(
                outputRoot,
                "Receipts",
                runId + PsoConstants.WarmupReceiptSuffix);
            failed = false;
            failure = string.Empty;
            activated.Clear();
            pending.Clear();
            phaseReceipts.Clear();
            completedCollections.Clear();

            Debug.Log("[ShaderHitchPipeline] Loaded plan '" + plan.profileId +
                      "' with " + plan.phases.Length + " phases.");
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
            foreach (KeyValuePair<string, GraphicsStateCollection> pair in completedCollections)
            {
                GraphicsStateCollection collection = pair.Value;
                if (collection == null || !collection.isTracingCacheMisses)
                    continue;

                GraphicsStateCollection misses = collection.cacheMissCollection;
                if (misses == null || misses.totalGraphicsStateCount <= 0)
                    continue;

                PsoWarmupPhaseReceipt receipt = FindReceipt(pair.Key);
                if (receipt == null)
                    continue;

                string fileName = PsoFileUtility.SanitizeFileName(pair.Key) + "-" +
                                  runId + "-cache-miss" +
                                  PsoConstants.GraphicsStateExtension;
                string path = Path.Combine(outputRoot, "CacheMisses", fileName);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (!misses.SaveToFile(path))
                    continue;

                receipt.cacheMissGraphicsStates = misses.totalGraphicsStateCount;
                receipt.cacheMissCollectionFile = path;
                receipt.cacheMissCollectionSha256 = PsoFileUtility.ComputeSha256(path);
            }

            WriteReceipt();
        }

        private bool Enqueue(PsoWarmupPhasePlan phase)
        {
            if (!activated.Add(phase.phase))
                return false;
            pending.Enqueue(phase);
            return true;
        }

        private void Update()
        {
            if (quitting || failed || plan == null)
                return;

            if (current == null)
            {
                if (pending.Count == 0)
                    return;
                BeginNextPhase();
                if (current == null)
                    return;
            }

            double frameMilliseconds = Time.unscaledDeltaTime * 1000.0;
            current.receipt.maximumObservedFrameMilliseconds = Math.Max(
                current.receipt.maximumObservedFrameMilliseconds,
                frameMilliseconds);
            if (current.jobScheduled || current.collection.completedWarmupCount > 0)
                current.policy.Observe(frameMilliseconds);

            if (current.jobScheduled)
            {
                if (!current.job.IsCompleted)
                    return;
                current.job.Complete();
                current.jobScheduled = false;
                current.receipt.completedGraphicsStates =
                    current.collection.completedWarmupCount;
            }

            if (current.collection.isWarmedUp ||
                current.collection.completedWarmupCount >=
                current.collection.totalGraphicsStateCount)
            {
                CompleteCurrentPhase();
                return;
            }

            current.job = current.collection.WarmUpProgressively(
                current.policy.CurrentBatchSize,
                default(JobHandle),
                current.plan.traceCacheMisses);
            current.jobScheduled = true;
        }

        private void BeginNextPhase()
        {
            PsoWarmupPhasePlan phase = pending.Dequeue();
            var receipt = new PsoWarmupPhaseReceipt
            {
                phase = phase.phase,
                collectionFile = phase.collectionFile,
            };
            phaseReceipts.Add(receipt);

            try
            {
                string directory = Path.GetDirectoryName(planPath);
                string collectionPath = PsoFileUtility.ResolveChildPath(
                    directory,
                    phase.collectionFile);
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

                receipt.totalGraphicsStates = collection.totalGraphicsStateCount;
                current = new PhaseExecution
                {
                    plan = phase,
                    collection = collection,
                    policy = new PsoAdaptiveBatchPolicy(
                        phase.initialBatchSize,
                        phase.minimumBatchSize,
                        phase.maximumBatchSize,
                        phase.targetFrameMilliseconds),
                    stopwatch = Stopwatch.StartNew(),
                    receipt = receipt,
                };
                Debug.Log("[ShaderHitchPipeline] Warming phase '" + phase.phase +
                          "' with " + receipt.totalGraphicsStates + " graphics states.");
            }
            catch (Exception exception)
            {
                receipt.error = exception.ToString();
                receipt.completed = false;
                Fail("Warmup phase '" + phase.phase + "' failed: " + exception.Message);
            }
        }

        private void CompleteCurrentPhase()
        {
            current.stopwatch.Stop();
            current.receipt.completedGraphicsStates =
                current.collection.completedWarmupCount;
            current.receipt.finalBatchSize = current.policy.CurrentBatchSize;
            current.receipt.elapsedMilliseconds = current.stopwatch.Elapsed.TotalMilliseconds;
            current.receipt.completed = true;
            completedCollections[current.plan.phase] = current.collection;
            Debug.Log("[ShaderHitchPipeline] Warmed phase '" + current.plan.phase +
                      "': " + current.receipt.completedGraphicsStates + "/" +
                      current.receipt.totalGraphicsStates + " states in " +
                      current.receipt.elapsedMilliseconds.ToString("F1") + " ms.");
            current = null;
            WriteReceipt();

            if (pending.Count == 0)
                WarmupCompleted?.Invoke();
        }

        private void Fail(string message)
        {
            failed = true;
            failure = message;
            current = null;
            pending.Clear();
            Debug.LogError("[ShaderHitchPipeline] " + message);
            WriteReceipt();
        }

        private void WriteReceipt()
        {
            if (string.IsNullOrWhiteSpace(receiptPath))
                return;

            var receipt = new PsoWarmupRunReceipt
            {
                runId = runId,
                startedUtc = startedUtc,
                endedUtc = PsoFileUtility.UtcNowText(),
                planFile = planPath,
                planSha256 = planHash,
                completed = !failed && current == null && pending.Count == 0,
                error = failure,
                environment = PsoEnvironmentSnapshot.Capture(),
                phases = phaseReceipts.ToArray(),
            };
            PsoFileUtility.WriteJsonAtomic(receiptPath, receipt);
        }

        private PsoWarmupPhaseReceipt FindReceipt(string phase)
        {
            for (int index = 0; index < phaseReceipts.Count; index++)
            {
                if (string.Equals(
                        phaseReceipts[index].phase,
                        phase,
                        StringComparison.OrdinalIgnoreCase))
                    return phaseReceipts[index];
            }
            return null;
        }

        private void EnsurePlan()
        {
            if (plan == null)
                throw new InvalidOperationException("No warmup plan is loaded.");
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
            if (current != null)
            {
                if (current.jobScheduled && current.job.IsCompleted)
                    current.job.Complete();
                current.stopwatch.Stop();
                current.receipt.completedGraphicsStates =
                    current.collection.completedWarmupCount;
                current.receipt.elapsedMilliseconds = current.stopwatch.Elapsed.TotalMilliseconds;
                current.receipt.error = "Application quit before phase completion.";
            }
            SaveCacheMissesNow();
            WriteReceipt();
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }
    }
}
