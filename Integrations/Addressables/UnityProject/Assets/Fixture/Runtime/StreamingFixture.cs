using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Yanagisawa.ShaderHitchPipeline;
using Yanagisawa.ShaderHitchPipeline.Addressables;
using GraphicsStateCollection = UnityEngine.Rendering.GraphicsStateCollection;

public sealed class StreamingFixture : MonoBehaviour
{
    [Serializable] private sealed class Receipt
    {
        public string mode, buildGuid, unityVersion, gpu, driver, contentDigest, collectionHash, error;
        public string contentId, contentRevision;
        public PsoEnvironmentSnapshot environment;
        public bool passed;
        public bool developmentBuild;
        public bool nativeCollectionValidated;
        public string importedCollectionHash;
        public int checks, traceStates, submittedStates, submittedBatches;
    }
    private string root, mode;
    private Receipt receipt;
    private PsoAddressablesLoader loader;
    private float started;
    private IEnumerator Start()
    {
        Application.runInBackground = true;
        root = Path.GetFullPath(PsoCommandLine.Current.GetString("-stream-evidence", "StreamingEvidence"));
        Directory.CreateDirectory(root); started = Time.realtimeSinceStartup;
        mode = PsoCommandLine.Current.GetString("-stream-mode", "smoke");
        receipt = new Receipt { mode = mode, buildGuid = Application.buildGUID, unityVersion = Application.unityVersion,
            gpu = SystemInfo.graphicsDeviceName, driver = SystemInfo.graphicsDeviceVersion, environment = PsoUnityEnvironment.Capture(), developmentBuild = Debug.isDebugBuild };
        IEnumerator test = Run();
        while (true)
        {
            object current;
            try { if (!test.MoveNext()) break; current = test.Current; }
            catch (Exception error) { receipt.error = error.ToString(); Debug.LogException(error); break; }
            yield return current;
        }
        receipt.passed = receipt.error == null;
        File.WriteAllText(Path.Combine(root, mode + ".json"), JsonUtility.ToJson(receipt, true));
        Debug.Log("STREAMING_FIXTURE_" + (receipt.passed ? "OK" : "FAILED") + " mode=" + mode + " checks=" + receipt.checks);
        Application.Quit(receipt.passed ? 0 : 1);
    }
    private void Check(bool condition, string message) { receipt.checks++; if (!condition) throw new Exception(message); }
    private void Timeout() { if (Time.realtimeSinceStartup - started > 120) throw new TimeoutException("Streaming fixture exceeded 120 seconds."); }
    private static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    private string ContentDigest()
    {
        string aa = Path.Combine(Application.streamingAssetsPath, "aa");
        string[] files = Directory.GetFiles(aa, "*", SearchOption.AllDirectories); Array.Sort(files, StringComparer.Ordinal);
        Check(files.Any(file => file.EndsWith(".bundle", StringComparison.Ordinal)), "Real Addressables bundles must ship in Player.");
        return Hash(Encoding.UTF8.GetBytes(string.Join("\n", files.Select(file => file.Substring(aa.Length).Replace('\\', '/') + ":" + Hash(File.ReadAllBytes(file))))));
    }
    private IEnumerator Run()
    {
        receipt.contentDigest = ContentDigest();
        string tracePath = Path.Combine(root, "fixture-r1.graphicsstate");
        if (mode == "trace")
        {
            for (int revision = 1; revision <= 2; revision++)
            {
            tracePath = Path.Combine(root, "fixture-r" + revision + ".graphicsstate");
            var handle = Addressables.LoadAssetAsync<GameObject>("stream-r" + revision);
            while (!handle.IsDone) { Timeout(); yield return null; }
            Check(handle.Status == AsyncOperationStatus.Succeeded, "Real Addressables trace load failed.");
            var camera = FindFirstObjectByType<Camera>(); camera.enabled = false;
            var target = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGB32); target.Create();
            camera.targetTexture = target;
            var live = new GraphicsStateCollection { runtimePlatform = Application.platform, graphicsDeviceType = SystemInfo.graphicsDeviceType,
                qualityLevelName = PsoUnityEnvironment.CurrentQualityName() };
            try
            {
                Check(live.BeginTrace(), "Native tracing must start.");
                var instance = Instantiate(handle.Result);
                // A hidden batchmode window need not present. Explicit offscreen rendering and readback
                // verify real GPU work without making the fixture depend on desktop visibility.
                for (int frame = 0; frame < 24; frame++) { camera.Render(); yield return null; }
                var previousTarget = RenderTexture.active; RenderTexture.active = target;
                var pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                pixel.ReadPixels(new Rect(32, 32, 1, 1), 0, 0); pixel.Apply();
                Color actual = pixel.GetPixel(0, 0);
                Check(revision == 1 ? actual.b > 0.8f && actual.r < 0.2f : actual.r > 0.8f && actual.b < 0.2f,
                    "GPU readback must show the real revision material, not the background or error shader.");
                RenderTexture.active = previousTarget; Destroy(pixel);
                live.EndTrace();
                Check(live.SaveToFile(tracePath) && live.totalGraphicsStateCount > 0, "Trace must capture actual rendered PSOs."); receipt.traceStates = live.totalGraphicsStateCount;
                receipt.nativeCollectionValidated = false;
                if (PsoCommandLine.Current.HasFlag("-stream-verify-native"))
                {
                    var imported = JsonUtility.FromJson<Receipt>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "fixture-import-r" + revision + ".json")));
                    Check(imported.collectionHash == Hash(File.ReadAllBytes(Path.Combine(Application.streamingAssetsPath, "fixture-origin-r" + revision + ".bytes"))), "Imported source artifact must retain its original hash.");
                    var native = Addressables.LoadAssetAsync<GraphicsStateCollection>("stream-pso-r" + revision);
                    while (!native.IsDone) { Timeout(); yield return null; }
                    Check(native.Status == AsyncOperationStatus.Succeeded, "Bundled native graphics-state collection must load.");
                    var union = new GraphicsStateCollection();
                    try
                    {
                        Check(PsoGraphicsStateCollectionCompatibility.Append(union, live) && PsoGraphicsStateCollectionCompatibility.Append(union, native.Result), "Native state union must succeed.");
                        Check(native.Result.totalGraphicsStateCount == live.totalGraphicsStateCount && union.totalGraphicsStateCount == live.totalGraphicsStateCount &&
                            native.Result.variantCount == live.variantCount && union.variantCount == live.variantCount,
                            "Bundled collection and final frozen Player's independent live trace must contain exactly the same native variants and graphics states.");
                        receipt.nativeCollectionValidated = true; receipt.importedCollectionHash = imported.collectionHash;
                    }
                    finally { Destroy(union); Addressables.Release(native); }
                }
                Destroy(instance); yield return null;
            }
            finally { if (live.isTracing) live.EndTrace(); Destroy(live); }
            camera.targetTexture = null; target.Release(); Destroy(target);
            Addressables.Release(handle); receipt.collectionHash = Hash(File.ReadAllBytes(tracePath));
            receipt.contentId = "room"; receipt.contentRevision = "r" + revision; receipt.passed = true;
            File.WriteAllText(Path.Combine(root, "trace-r" + revision + ".json"), JsonUtility.ToJson(receipt, true));
            yield return Resources.UnloadUnusedAssets();
            }
            yield break;
        }
        var prior = JsonUtility.FromJson<Receipt>(File.ReadAllText(Path.Combine(root, "trace-r1.json")));
        receipt.collectionHash = Hash(File.ReadAllBytes(tracePath)); receipt.traceStates = prior.traceStates;
        Check(prior.passed && prior.buildGuid == receipt.buildGuid && prior.contentDigest == receipt.contentDigest && prior.collectionHash == receipt.collectionHash,
            "Collection must be from this exact Player build and shipped bundle/catalog bytes.");
        var origin1 = JsonUtility.FromJson<Receipt>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "fixture-import-r1.json")));
        var paths = new Dictionary<string, PsoStreamingCollectionAsset> { ["main"] = new PsoStreamingCollectionAsset(Path.Combine(Application.streamingAssetsPath, "fixture-origin-r1.bytes"), origin1.collectionHash, origin1.traceStates, "stream-pso-r1") };
        var priorRevision = JsonUtility.FromJson<Receipt>(File.ReadAllText(Path.Combine(root, "trace-r2.json")));
        var origin2 = JsonUtility.FromJson<Receipt>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "fixture-import-r2.json")));
        var revisedPaths = new Dictionary<string, PsoStreamingCollectionAsset> { ["main"] = new PsoStreamingCollectionAsset(Path.Combine(Application.streamingAssetsPath, "fixture-origin-r2.bytes"), origin2.collectionHash, origin2.traceStates, "stream-pso-r2") };
        loader = new PsoAddressablesLoader();
        Func<int, Func<GameObject, string>> attest = revision => asset =>
        {
            var captured = JsonUtility.FromJson<Receipt>(File.ReadAllText(Path.Combine(root, "trace-r" + revision + ".json")));
            var imported = JsonUtility.FromJson<Receipt>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "fixture-import-r" + revision + ".json")));
            Check(captured.nativeCollectionValidated && imported.collectionHash == captured.importedCollectionHash && imported.traceStates == captured.traceStates,
                "The bundled collection must have an exact native-state validation against this final Player's independent capture.");
            Check(captured.passed && captured.contentId == "room" && captured.contentRevision == "r" + revision &&
                captured.buildGuid == receipt.buildGuid && captured.contentDigest == receipt.contentDigest &&
                captured.gpu == receipt.gpu && captured.unityVersion == receipt.unityVersion &&
                captured.collectionHash == Hash(File.ReadAllBytes(Path.Combine(root, "fixture-r" + revision + ".graphicsstate"))),
                "Unknown/changed content requires its own matching trace and build provenance.");
            Check(asset != null && asset.name == "r" + revision, "Loaded key/revision mapping mismatch: " + (asset == null ? "null" : asset.name));
            var material = asset.GetComponent<Renderer>().sharedMaterial;
            Check(material.shader != null && material.shader.name == "ShaderHitchPipeline/StreamingFixture" && material.shader.isSupported,
                "Actual bundle shader must be loaded before collection registration.");
            Check(material.GetColor("_Tint") == (revision == 1 ? Color.blue : Color.red), "Revision must load different real material content.");
            // Validate the shared actual-build contract as well as the native collection,
            // loaded material revision and complete shipped bundle/catalog tree above.
            string contentNamespace = PsoIntegratedCompatibility.RequireCollectionNamespace(
                new PsoCompatibilityContract { version = 1, collectionEnvironment = captured.environment },
                PsoUnityEnvironment.Capture());
            return contentNamespace + ":" + receipt.contentDigest;
        };
        var a = loader.Load("stream-r1", "room", "r1", paths, attest(1));
        var b = loader.Load("stream-r1", "room", "r1", paths, attest(1));
        while (!a.IsFinished || !b.IsFinished) { Timeout(); loader.Tick(); yield return null; }
        Check(a.Owner != null && b.Owner != null, "Both owners must register after load: " + a.Failure + b.Failure);
        Check(loader.Coordinator.RetainedStateCount == receipt.traceStates, "Native interning must merge duplicate states across real loaded owners.");
        var requestA = loader.Coordinator.RequestPhase(a.Owner, "main");
        var requestB = loader.Coordinator.RequestPhase(b.Owner, "main");
        loader.Tick(1); Check(loader.Coordinator.HasSubmittedWork, "Submit must expose a native fence.");
        Check(loader.Coordinator.SubmittedStateCount == 1, "Overlap must deduplicate and subset admission must hold.");
        a.Unload(); b.Unload();
        Check(loader.RetainedHandleCount == 4, "Unloaded submitted owners must retain both prefab and native collection handles until fence.");
        loader.Coordinator.Drain(); Check(loader.RetainedHandleCount == 0, "Fence must release retained handles.");
        Check(requestA.IsCancelled && requestB.IsCancelled, "Unload must cancel all outstanding requests.");
        int submitted = loader.Coordinator.SubmittedStateCount;
        var reload = loader.Load("stream-r1", "room", "r1", paths, attest(1));
        while (!reload.IsFinished) { Timeout(); loader.Tick(); yield return null; }
        Check(reload.Owner != null, "Reload registration failed: " + reload.Failure);
        var reloadRequest = loader.Coordinator.RequestPhase(reload.Owner, "main");
        while (!reloadRequest.IsComplete) { Timeout(); loader.Tick(); Check(reloadRequest.Failure == null, "Native warmup failed."); yield return null; }
        Check(loader.Coordinator.SubmittedStateCount > submitted, "Unretained reload must warm again.");
        var revised = loader.Load("stream-r2", "room", "r2", revisedPaths, attest(2));
        Check(reload.Owner.IsUnloaded, "A revision request retires the old owner.");
        while (!revised.IsFinished) { Timeout(); loader.Tick(); yield return null; }
        Check(revised.Owner != null, "Changed real content must register: " + revised.Failure);
        var revisedRequest = loader.Coordinator.RequestPhase(revised.Owner, "main");
        while (!revisedRequest.IsComplete) { Timeout(); loader.Tick(); Check(revisedRequest.Failure == null, "Revised native warmup failed."); yield return null; }
        revised.Unload();
        var cancelled = loader.Load("stream-r1", "cancel", "r1", paths, attest(1)); cancelled.Unload();
        while (!cancelled.IsFinished) { Timeout(); loader.Tick(); yield return null; }
        Check(cancelled.Owner == null && cancelled.Asset == null, "Load cancellation must not register late resources.");
        var rejected = loader.Load("stream-r2", "reject", "r1", paths, attest(1));
        while (!rejected.IsFinished) { Timeout(); loader.Tick(); yield return null; }
        Check(rejected.Failure != null && rejected.Owner == null && rejected.Asset == null, "A real r2 asset paired with r1 metadata must reject before opening the collection and release its load.");
        var wrongCount = new Dictionary<string, PsoStreamingCollectionAsset> { ["main"] = new PsoStreamingCollectionAsset(tracePath, prior.collectionHash, prior.traceStates + 1, "stream-pso-r1") };
        var countFailure = loader.Load("stream-r1", "wrong-count", "r1", wrongCount, attest(1));
        while (!countFailure.IsFinished) { Timeout(); loader.Tick(); yield return null; }
        Check(countFailure.Failure != null && countFailure.Owner == null && countFailure.Asset == null, "Partial native resolution/count mismatch must reject and release.");
        var wrongHash = new Dictionary<string, PsoStreamingCollectionAsset> { ["main"] = new PsoStreamingCollectionAsset(tracePath, new string('0', 64), prior.traceStates, "stream-pso-r1") };
        var hashFailure = loader.Load("stream-r1", "wrong-hash", "r1", wrongHash, attest(1));
        while (!hashFailure.IsFinished) { Timeout(); loader.Tick(); yield return null; }
        Check(hashFailure.Failure != null && hashFailure.Owner == null && hashFailure.Asset == null, "Artifact hash mismatch must reject and release.");
        var stale = loader.Load("stream-r1", "late-revision", "r1", paths, attest(1));
        var latest = loader.Load("stream-r2", "late-revision", "r2", revisedPaths, attest(2));
        while (!stale.IsFinished || !latest.IsFinished) { Timeout(); loader.Tick(); yield return null; }
        Check(stale.IsCancelled && stale.Owner == null && latest.Owner != null, "Late load completion must not resurrect a superseded content revision."); latest.Unload();
        var missing = loader.Load("missing-addressable-key", "missing", "r1", paths, attest(1));
        while (!missing.IsFinished) { Timeout(); loader.Tick(); yield return null; }
        Check(missing.Failure != null && missing.Owner == null, "Real Addressables failed load must be observable.");
        loader.BeginShutdown(); while (loader.PendingLoads) { Timeout(); loader.Tick(); yield return null; }
        loader.DrainAndDispose(); Check(loader.RetainedHandleCount == 0, "Shutdown must release all Addressables handles.");
        receipt.submittedStates = loader.Coordinator.SubmittedStateCount; receipt.submittedBatches = loader.Coordinator.SubmittedBatchCount;
    }
}
