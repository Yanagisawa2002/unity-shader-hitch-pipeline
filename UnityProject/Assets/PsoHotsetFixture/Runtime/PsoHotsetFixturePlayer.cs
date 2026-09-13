using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Profiling;
using GraphicsStateCollection = UnityEngine.Rendering.GraphicsStateCollection;
using Debug = UnityEngine.Debug;

namespace Yanagisawa.ShaderHitchPipeline.HotsetFixture
{
    [Serializable] public sealed class HotsetCatalog
    {
        public string buildGuid;
        public string shaderSha256;
        public string compatibilityNamespace;
        public PsoEnvironmentSnapshot environment;
        public PsoHotsetUnit[] units;
        public string costExecutionContext;
        public string observedDriverVersion;
        public string costScope = "independent process calibration; driver cache uncontrolled; no cold-call bound";
    }
    [Serializable] public sealed class HotsetPlayerReceipt
    {
        public string mode;
        public string renderPath = "explicit-camera-render-backbuffer";
        public string route;
        public string buildGuid;
        public PsoEnvironmentSnapshot environment;
        public string observedDriverVersion;
        public bool completed;
        public bool firstPresentAvailable;
        public double firstPresentMilliseconds = -1;
        public double firstRenderedFrameEngineMilliseconds;
        public double startupWarmupMilliseconds;
        public double deferredWarmupMilliseconds;
        public int deferredSubmittedStates;
        public int extraTotalWarmedStateEntries;
        public long engineAllocatedBytesBeforeWarmup;
        public long engineAllocatedBytesAfterWarmup;
        public long driverPsoMemoryBytes = -1;
        public long collectionFileBytes;
        public int observedPlanCacheMissStates = -1;
        public string cacheMissScope = "GSC feedback after pre-seeding complete catalog; not startup coverage or OS/driver cache telemetry";
        public PsoHotsetDecision decision;
        public PsoHotsetCoverage coverage;
        public PsoFrameStatistics laterFrames;
        public double[] frameMilliseconds;
        public string[] warmedUnitIds;
        public double[] completionMilliseconds;
        public int[] renderedGroupSequence;
        public string[] markers;
        public string limitations = "first rendered frame is engine proxy, not OS present; process-cold, driver cache uncontrolled; Development Player; tiny bounded fixture, no production benefit claim";
    }

    // Dedicated schema omits metrics this correctness-only process does not sample.
    [Serializable] public sealed class HotsetStartupSmokeReceipt
    {
        public string mode = "orchestrator-smoke";
        public string renderPath = "explicit-camera-render-backbuffer";
        public bool completed;
        public string buildGuid;
        public PsoEnvironmentSnapshot environment;
        public string observedDriverVersion;
        public PsoHotsetDecision decision;
        public double firstRenderedFrameEngineMilliseconds;
        public double startupApiElapsedMilliseconds;
        public string scope = "startup API correctness; no OS first-present, coverage, memory or later-frame measurement";
    }

    public sealed class PsoHotsetFixturePlayer : MonoBehaviour
    {
        public Shader shader;
        public string shaderSha256;
        private readonly GameObject[] groups = new GameObject[4];
        private readonly int[] renderedVisits = new int[4];
        private readonly List<PsoHotsetUse> uses = new List<PsoHotsetUse>();
        private readonly List<string> markers = new List<string>();
        private readonly Dictionary<string, double> completions = new Dictionary<string, double>();
        private string root, mode, route;
        private HotsetCatalog catalog;
        private HotsetPlayerReceipt receipt;
        private PsoUnityGraphicsStateWarmupBackend[] backends;
        private double routeEpoch;
        private int stage;
        private int activeGroup;
        private bool recordUses;
        private GraphicsStateCollection feedback;
        private Camera sceneCamera;
        private PsoWarmupOrchestrator smokeOrchestrator;

        private void Awake()
        {
            Application.logMessageReceived += OnLog;
            Application.runInBackground = true;
            Application.targetFrameRate = 120;
            QualitySettings.vSyncCount = 0;
            root = Path.GetFullPath(PsoCommandLine.Current.GetString("-hotset-root", "PsoArtifacts/Hotset"));
            mode = PsoCommandLine.Current.GetString("-hotset-mode", "discover");
            route = PsoCommandLine.Current.GetString("-hotset-route", "train-a");
            Directory.CreateDirectory(root);
            ValidateArguments();
            BuildScene();
            if (mode == "discover") return;
            catalog = JsonUtility.FromJson<HotsetCatalog>(File.ReadAllText(Path.Combine(root, "catalog.json")));
            if (!CatalogMatchesCurrent())
                throw new InvalidDataException("Fixture catalog build/device/driver/Unity/cost scope changed; rediscover and recalibrate.");
            backends = new PsoUnityGraphicsStateWarmupBackend[4];
            for (int i = 0; i < 4; i++)
            {
                if (PsoFileUtility.ComputeSha256(CollectionPath(i)) != catalog.units[i].collectionSha256)
                    throw new InvalidDataException("Collection integrity mismatch");
                backends[i] = new PsoUnityGraphicsStateWarmupBackend(CollectionPath(i));
            }
            if (mode == "calibrate" || mode == "training") return;
            if (mode != "required-only" && mode != "hotset" && mode != "all-at-once" && mode != "orchestrator-smoke") throw new ArgumentException("Unknown arm");
            receipt = new HotsetPlayerReceipt { mode = mode, route = route, buildGuid = Application.buildGUID,
                environment = PsoUnityEnvironment.Capture(), observedDriverVersion = ObservedDriver() };
            // Frozen policy and actual plan are created before held-out execution.
            var frozen = JsonUtility.FromJson<PsoStartupHotsetDocument>(File.ReadAllText(Path.Combine(root, "frozen-policy.json")));
            string planPath = Path.Combine(root, "fixture-plan.json");
            var plan = PsoPlanValidation.LoadAndValidate(planPath, true, true);
            if (frozen.planSha256 != PsoFileUtility.ComputeSha256(planPath))
                throw new InvalidDataException("Frozen plan changed");
            if (mode == "required-only") frozen.startupBudgetMilliseconds = 0;
            receipt.decision = PsoStartupHotset.PrepareValidated(plan, frozen.planSha256, frozen, ValidateFixturePolicy);
            if (receipt.decision.rejectedTraces.Length != 0) throw new InvalidDataException("Fixture policy rejected: " + string.Join(";", receipt.decision.rejectedTraces));
            if (mode == "orchestrator-smoke")
            {
                PsoWarmupOrchestrator.StartupHotsetCompatibilityValidator = ValidateFixturePolicy;
                smokeOrchestrator = PsoWarmupOrchestrator.EnsureInstance();
                smokeOrchestrator.LoadPlan(planPath, root, true);
                var timer = Stopwatch.StartNew();
                smokeOrchestrator.ActivateStartupHotsetAndGate(frozen);
                timer.Stop(); receipt.startupWarmupMilliseconds = timer.Elapsed.TotalMilliseconds;
                if (smokeOrchestrator.HasFailed || !smokeOrchestrator.IsPhaseComplete("u0") ||
                    !smokeOrchestrator.StartupHotset.startupUnitIds.SequenceEqual(receipt.decision.startupUnitIds))
                    throw new Exception("Production startup gate mismatch");
                foreach (string id in receipt.decision.startupUnitIds)
                {
                    if (!smokeOrchestrator.IsPhaseComplete(id)) throw new Exception("Selected startup phase incomplete");
                    completions[id] = 0;
                }
                // Production orchestrator owns its feedback session. Do not start a
                // second native trace while it is active; this is a separate smoke.
                return;
            }
            if (mode == "all-at-once")
            {
                // Natural Unity bulk per collection, with no injected stall or forced
                // oversized deferred first batch. This arm may fairly win this fixture.
                receipt.decision.startupUnitIds = catalog.units.Select(u => u.id).ToArray();
                receipt.decision.deferredUnitIds = Array.Empty<string>();
                receipt.decision.estimatedStartupMilliseconds = catalog.units.Sum(u => u.estimatedWarmupMilliseconds);
                receipt.decision.startupStateEntries = catalog.units.Sum(u => u.graphicsStateCount);
                receipt.decision.startupMemoryKnown = false;
                receipt.decision.policy = "natural-unity-all-collections-control";
            }
            receipt.engineAllocatedBytesBeforeWarmup = Profiler.GetTotalAllocatedMemoryLong();
            Mark("STARTUP_GATE_BEGIN");
            foreach (string id in receipt.decision.startupUnitIds)
                receipt.startupWarmupMilliseconds += Warm(int.Parse(id.Substring(1)));
            Mark("STARTUP_GATE_END");
            receipt.engineAllocatedBytesAfterWarmup = Profiler.GetTotalAllocatedMemoryLong();
            feedback = new GraphicsStateCollection { runtimePlatform = Application.platform,
                graphicsDeviceType = SystemInfo.graphicsDeviceType, qualityLevelName = PsoUnityEnvironment.CurrentQualityName() };
            for (int i = 0; i < 4; i++)
            {
                var collection = new GraphicsStateCollection();
                if (!collection.LoadFromFile(CollectionPath(i)) || !PsoGraphicsStateCollectionCompatibility.Append(feedback, collection))
                    throw new InvalidDataException("Could not seed feedback");
                Destroy(collection);
                receipt.collectionFileBytes += new FileInfo(CollectionPath(i)).Length;
            }
            if (!feedback.BeginTrace()) throw new InvalidOperationException("Feedback trace unavailable");
        }

        private IEnumerator Start()
        {
            var endFrame = new WaitForEndOfFrame();
            if (mode == "discover")
            {
                catalog = new HotsetCatalog { buildGuid = Application.buildGUID, environment = PsoUnityEnvironment.Capture(),
                    shaderSha256 = shaderSha256, observedDriverVersion = ObservedDriver(), costExecutionContext = CostContext(),
                    compatibilityNamespace = PsoCompatibility.CollectionKey(PsoUnityEnvironment.Capture()),
                    units = new PsoHotsetUnit[4] };
                for (int i = 0; i < 4; i++)
                {
                    Show(i);
                    using (var trace = new PsoUnityGraphicsStateTraceBackend())
                    {
                        int before = renderedVisits[i];
                        double timeout = Time.realtimeSinceStartupAsDouble + 30;
                        while (renderedVisits[i] - before < 32)
                        {
                            yield return endFrame;
                            if (Time.realtimeSinceStartupAsDouble > timeout) throw new Exception("Discovery received no complete renderer visits: group=" + i + ", visits=" + renderedVisits[i]);
                        }
                        var artifact = trace.Finish(CollectionPath(i), false);
                        Write("unit-" + i + ".discovery.json", artifact);
                        if (!artifact.saved || artifact.stateCount < 4) throw new Exception("Incomplete discovery trace: states=" + artifact.stateCount + ", variants=" + artifact.variantCount);
                        catalog.units[i] = new PsoHotsetUnit { id = "u" + i, phase = "u" + i,
                            contentId = "hotset-fixture", contentRevision = Application.buildGUID,
                            compatibilityNamespace = catalog.compatibilityNamespace,
                            collectionSha256 = PsoFileUtility.ComputeSha256(CollectionPath(i)),
                            requiredStartup = i == 0, graphicsStateCount = artifact.stateCount };
                    }
                }
                Write("catalog.json", catalog);
                Application.Quit(0); yield break;
            }
            if (mode == "calibrate")
            {
                for (int i = 0; i < 4; i++) catalog.units[i].estimatedWarmupMilliseconds = Warm(i);
                Write("catalog.json", catalog);
                var plan = new PsoWarmupPlanDocument {
                    profileId = "hotset-fixture", runtimePlatform = Application.platform.ToString(),
                    graphicsDeviceType = SystemInfo.graphicsDeviceType.ToString(), qualityLevelName = PsoUnityEnvironment.CurrentQualityName(),
                    compatibility = new PsoCompatibilityContract { version = PsoCompatibility.Version,
                        collectionEnvironment = catalog.environment, costEnvironment = null,
                        costModelVersion = PsoCompatibility.CostModelVersion },
                    phases = catalog.units.Select((unit, i) => new PsoWarmupPhasePlan {
                        phase = unit.id, collectionFile = Path.GetFileName(CollectionPath(i)), collectionSha256 = unit.collectionSha256,
                        graphicsStateCount = unit.graphicsStateCount, required = unit.requiredStartup, prewarmAtStartup = true,
                        estimatedMillisecondsPerState = Math.Max(0.000001, unit.estimatedWarmupMilliseconds / unit.graphicsStateCount)
                    }).ToArray() };
                // Normalize measured floating-point values through Unity's serializer
                // before hashing, then verify the actual file using the production reader.
                for (int pass = 0; pass < 3; pass++)
                    plan = PsoDocumentJson.Parse<PsoWarmupPlanDocument>(PsoDocumentJson.Serialize(plan, true));
                plan.planSha256 = PsoPlanValidation.ComputeContentHash(plan);
                Write("fixture-plan.json", plan);
                PsoPlanValidation.LoadAndValidate(Path.Combine(root, "fixture-plan.json"), true, true);
                Application.Quit(0); yield break;
            }
            if (mode == "orchestrator-smoke")
            {
                Show(0); yield return endFrame;
                Write("orchestrator-smoke.json", new HotsetStartupSmokeReceipt { completed = !smokeOrchestrator.HasFailed,
                    environment = PsoUnityEnvironment.Capture(), buildGuid = Application.buildGUID, observedDriverVersion = ObservedDriver(),
                    decision = smokeOrchestrator.StartupHotset, firstRenderedFrameEngineMilliseconds = Time.realtimeSinceStartupAsDouble * 1000,
                    startupApiElapsedMilliseconds = receipt.startupWarmupMilliseconds });
                Application.Quit(0); yield break;
            }
            int[] sequence = Sequence(route);
            if ((mode == "training") != route.StartsWith("train-", StringComparison.Ordinal))
                throw new ArgumentException("Arm/split mismatch");
            Show(sequence[0]);
            routeEpoch = Time.realtimeSinceStartupAsDouble;
            recordUses = true;
            var frames = new List<double>();
            var pending = receipt == null ? new Queue<int>() : new Queue<int>(receipt.decision.deferredUnitIds.Select(id => int.Parse(id.Substring(1))));
            IPsoWarmupBatch batch = null;
            int warming = -1;
            double batchAt = 0;
            int baselineStates = feedback == null ? 0 : feedback.totalGraphicsStateCount;
            double previous = Time.realtimeSinceStartupAsDouble;
            for (int frame = 0; frame < sequence.Length * 24; frame++)
            {
                stage = frame / 24;
                Show(sequence[stage]);
                int visitsBefore = renderedVisits[activeGroup];
                double renderTimeout = Time.realtimeSinceStartupAsDouble + 30;
                do
                {
                    yield return endFrame;
                    if (Time.realtimeSinceStartupAsDouble > renderTimeout) throw new Exception("Route has no actual renderer visits");
                } while (renderedVisits[activeGroup] == visitsBefore);
                double now = Time.realtimeSinceStartupAsDouble;
                if (frame == 0)
                {
                    if (receipt != null) receipt.firstRenderedFrameEngineMilliseconds = now * 1000;
                    Mark("FIRST_RENDERED_FRAME_PROXY");
                }
                else frames.Add((now - previous) * 1000);
                previous = now;
                // Both required-only and hotset use identical deferred submission.
                // One state per job, nonblocking polling; this is a fixture scheduling
                // control, not a claim that driver work stays inside a frame budget.
                if (batch != null && batch.IsCompleted)
                {
                    batch.Complete(); batch.Dispose(); batch = null;
                    receipt.deferredWarmupMilliseconds += (Time.realtimeSinceStartupAsDouble - batchAt) * 1000;
                    if (backends[warming].IsWarmedUp)
                    {
                        completions["u" + warming] = (Time.realtimeSinceStartupAsDouble - routeEpoch) * 1000;
                        pending.Dequeue();
                    }
                }
                if (frame >= 4 && batch == null && pending.Count > 0)
                {
                    warming = pending.Peek(); batchAt = Time.realtimeSinceStartupAsDouble;
                    batch = backends[warming].Schedule(1, false);
                    receipt.deferredSubmittedStates++;
                }
            }
            recordUses = false;
            // Drain a submitted opaque job before freeing any retained collections.
            if (batch != null)
            {
                batch.Complete(); batch.Dispose();
                receipt.deferredWarmupMilliseconds += (Time.realtimeSinceStartupAsDouble - batchAt) * 1000;
                if (backends[warming].IsWarmedUp) completions["u" + warming] = (Time.realtimeSinceStartupAsDouble - routeEpoch) * 1000;
            }
            var routeTrace = new PsoHotsetTrace { routeId = route, captureId = PsoCommandLine.Current.GetString("-hotset-run-id", Guid.NewGuid().ToString("N")),
                split = mode == "training" ? "training" : "held-out", compatibilityNamespace = catalog.compatibilityNamespace,
                units = catalog.units, uses = uses.ToArray() };
            if (mode == "training") Write(route + ".trace.json", routeTrace);
            else
            {
                feedback.EndTrace();
                receipt.observedPlanCacheMissStates = Math.Max(0, feedback.totalGraphicsStateCount - baselineStates);
                string stem = PsoCommandLine.Current.GetString("-hotset-run-id", mode + "-" + route);
                if (!feedback.SaveToFile(Path.Combine(root, stem + ".feedback.graphicsstate"))) throw new IOException("Feedback save failed");
                receipt.coverage = PsoHotsetPolicy.Replay(catalog.units, receipt.decision, routeTrace, completions);
                receipt.frameMilliseconds = frames.ToArray();
                receipt.laterFrames = PsoStatistics.Calculate(receipt.frameMilliseconds, 16.67);
                receipt.warmedUnitIds = completions.Keys.OrderBy(s => s, StringComparer.Ordinal).ToArray();
                receipt.completionMilliseconds = receipt.warmedUnitIds.Select(id => completions[id]).ToArray();
                receipt.extraTotalWarmedStateEntries = catalog.units.Where(u => completions.ContainsKey(u.id) &&
                    !routeTrace.uses.Any(e => e.unitId == u.id)).Sum(u => u.graphicsStateCount);
                receipt.renderedGroupSequence = sequence;
                Mark("ROUTE_COMPLETE"); receipt.markers = markers.ToArray();
                receipt.completed = true;
                Write(stem + ".trace.json", routeTrace);
                Write(stem + ".receipt.json", receipt);
            }
            foreach (var backend in backends) backend.Dispose();
            Application.Quit(0);
        }

        private void LateUpdate()
        {
            // Explicit real camera submission also works when the OS window is
            // occluded. All controls use this same rendering path; no synthetic uses.
            if (sceneCamera != null) sceneCamera.Render();
        }

        public void RecordRendered(int group)
        {
            renderedVisits[group]++;
            if (recordUses && group == activeGroup)
                uses.Add(new PsoHotsetUse { unitId = "u" + group, phaseOrdinal = stage,
                    milliseconds = (Time.realtimeSinceStartupAsDouble - routeEpoch) * 1000 });
        }
        private double Warm(int index)
        {
            var clock = Stopwatch.StartNew();
            using (var batch = backends[index].Schedule(Math.Max(1, backends[index].TotalStateCount), true)) batch.Complete();
            clock.Stop();
            if (!backends[index].IsWarmedUp) throw new Exception("Startup warmup incomplete");
            completions["u" + index] = 0;
            return clock.Elapsed.TotalMilliseconds;
        }
        private void BuildScene()
        {
            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.gameObject.tag = "MainCamera";
            sceneCamera = camera;
            camera.enabled = false;
            camera.cullingMask = -1;
            camera.transform.position = new Vector3(0, 0, -10); camera.orthographic = true; camera.orthographicSize = 3;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            string[] keywords = { "HOTSET_A", "HOTSET_B", "HOTSET_C", "HOTSET_D" };
            for (int group = 0; group < 4; group++)
            {
                groups[group] = new GameObject("Group " + group);
                for (int j = 0; j < 4; j++)
                {
                    var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    Destroy(quad.GetComponent<Collider>());
                    quad.transform.SetParent(groups[group].transform);
                    quad.transform.position = new Vector3((j % 2) * 2 - 1, (j / 2) * 2 - 1, 0);
                    var material = new Material(shader);
                    int mask = group * 4 + j;
                    for (int k = 0; k < 4; k++) if ((mask & (1 << k)) != 0) material.EnableKeyword(keywords[k]);
                    material.SetColor("_Tint", Color.HSVToRGB(group / 4f, 0.7f, 1));
                    quad.GetComponent<MeshRenderer>().sharedMaterial = material;
                    var probe = quad.AddComponent<PsoHotsetRenderProbe>(); probe.fixture = this; probe.group = group;
                }
                groups[group].SetActive(false);
            }
        }
        private void Show(int group) { activeGroup = group; for (int i = 0; i < 4; i++) groups[i].SetActive(i == group); }
        private static int[] Sequence(string id)
        {
            switch (id)
            {
                case "train-a": return new[] { 0, 1, 1, 2, 1 };
                case "train-b": return new[] { 0, 1, 2, 1, 1 };
                case "held-a": return new[] { 0, 2, 1, 3, 1 };
                case "held-b": return new[] { 0, 3, 2, 3, 2 };
                case "next-held-a": return new[] { 0, 3, 1, 2, 3 };
                case "next-held-b": return new[] { 0, 2, 3, 1, 2 };
                default: throw new ArgumentException("Unknown fixed route " + id);
            }
        }
        private bool CatalogMatchesCurrent()
        {
            if (catalog == null || catalog.environment == null) return false;
            var current = PsoUnityEnvironment.Capture();
            var compatibility = PsoCompatibility.Evaluate(new PsoCompatibilityContract {
                version = PsoCompatibility.Version, collectionEnvironment = catalog.environment }, current);
            if (!compatibility.collectionCompatible || catalog.compatibilityNamespace != PsoCompatibility.CollectionKey(current) ||
                string.IsNullOrWhiteSpace(current.driverIdentity) || current.driverIdentitySource != PsoCompatibility.DriverIdentitySource ||
                catalog.environment.driverIdentity != current.driverIdentity) return false;
            return catalog != null && catalog.environment != null && catalog.buildGuid == Application.buildGUID &&
                catalog.shaderSha256 == shaderSha256 && catalog.observedDriverVersion == ObservedDriver() && catalog.costExecutionContext == CostContext() &&
                catalog.environment.graphicsDeviceVersion == SystemInfo.graphicsDeviceVersion &&
                catalog.environment.graphicsDeviceName == SystemInfo.graphicsDeviceName &&
                catalog.environment.graphicsDeviceType == SystemInfo.graphicsDeviceType.ToString() &&
                catalog.environment.qualityLevelName == PsoUnityEnvironment.CurrentQualityName() &&
                catalog.environment.unityVersion == Application.unityVersion;
        }
        private bool ValidateFixturePolicy(PsoWarmupPlanDocument plan, PsoStartupHotsetDocument policy)
        {
            return CatalogMatchesCurrent() && policy.units != null && policy.units.Length == catalog.units.Length &&
                policy.units.All(unit => catalog.units.Any(current => PsoHotsetPolicy.SameIdentity(unit, current) &&
                    Math.Abs(unit.estimatedWarmupMilliseconds - current.estimatedWarmupMilliseconds) <=
                        1e-12 * Math.Max(1, Math.Abs(current.estimatedWarmupMilliseconds))));
        }
        private string CostContext()
        {
            // This explicit experiment permits route/mode/output axes; it does not
            // relax the production exact-context measured-cost cache contract.
            return "hotset-fixture-v1|" + Application.buildGUID + "|" + shaderSha256 + "|" + Application.unityVersion +
                "|" + SystemInfo.graphicsDeviceName + "|apiVersion=" + SystemInfo.graphicsDeviceVersion + "|observedDriver=" + ObservedDriver() +
                "|" + SystemInfo.graphicsDeviceType + "|" + PsoUnityEnvironment.CurrentQualityName() +
                "|explicit-camera-render|bulk-startup,progressive-one-deferred|maxAsyncPsoJobs=" + PsoCommandLine.Current.GetString("-max-async-pso-job-count", "engine-default") +
                "|jobWorkers=" + Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount +
                "|cpuThreads=" + SystemInfo.processorCount + "|asyncPsoWorkers=engine-default-unavailable|640x360|120Hz|vsync0";
        }
        private static string ObservedDriver()
        {
            string driver = PsoCommandLine.Current.GetString("-hotset-observed-driver", "");
            if (string.IsNullOrWhiteSpace(driver)) throw new InvalidDataException("Runner Win32_VideoController driver evidence is required");
            return driver;
        }
        private static void ValidateArguments()
        {
            var args = Environment.GetCommandLineArgs();
            var flags = new HashSet<string>(StringComparer.Ordinal) { "-force-d3d12", "-pso-disable-warmup" };
            var valued = new HashSet<string>(StringComparer.Ordinal) { "-screen-fullscreen", "-screen-width", "-screen-height",
                "-hotset-mode", "-hotset-route", "-hotset-run-id", "-hotset-root", "-hotset-observed-driver", "-logFile", "-max-async-pso-job-count" };
            for (int i = 1; i < args.Length; i++)
            {
                if (flags.Contains(args[i])) continue;
                if (!valued.Contains(args[i]) || ++i >= args.Length) throw new ArgumentException("Unknown fixture cost-context argument");
            }
            string psoWorkers = PsoCommandLine.Current.GetString("-max-async-pso-job-count", "engine-default");
            if (psoWorkers != "engine-default" && psoWorkers != "4")
                throw new ArgumentException("Fixture permits only the predeclared four async PSO workers");
            if (PsoCommandLine.Current.GetString("-screen-width", "640") != "640" ||
                PsoCommandLine.Current.GetString("-screen-height", "360") != "360" ||
                PsoCommandLine.Current.GetString("-screen-fullscreen", "0") != "0") throw new ArgumentException("Fixture screen context changed");
        }
        private void Mark(string name) { markers.Add(name + "|utc=" + DateTime.UtcNow.ToString("O") + "|qpc=" + Stopwatch.GetTimestamp() + "|qpcFrequency=" + Stopwatch.Frequency + "|frame=" + Time.frameCount); }
        private string CollectionPath(int index) => Path.Combine(root, "unit-" + index + ".graphicsstate");
        private PsoHotsetTrace ReadTrace(string id) => JsonUtility.FromJson<PsoHotsetTrace>(File.ReadAllText(Path.Combine(root, id + ".trace.json")));
        private void Write(string name, object value) => PsoFileUtility.WriteJsonAtomic(Path.Combine(root, name), value);
        private void OnLog(string message, string stack, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error) return;
            if (root != null) File.WriteAllText(Path.Combine(root, "failure-" + mode + ".txt"), message + "\n" + stack);
            Application.Quit(1);
        }
    }

    public sealed class PsoHotsetRenderProbe : MonoBehaviour
    {
        public PsoHotsetFixturePlayer fixture;
        public int group;
        private void OnWillRenderObject() { fixture.RecordRendered(group); }
    }
}
