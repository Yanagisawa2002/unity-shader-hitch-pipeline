using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
#if UNITY_6000_5_OR_NEWER
using GraphicsStateCollection = UnityEngine.Rendering.GraphicsStateCollection;
#else
using GraphicsStateCollection = UnityEngine.Experimental.Rendering.GraphicsStateCollection;
#endif

namespace Yanagisawa.ShaderHitchPipeline.Addressables
{
    public sealed class PsoAddressablesLoad
    {
        internal PsoAddressablesLoader loader;
        internal AsyncOperationHandle<GameObject> handle;
        internal string contentId, revision;
        internal Dictionary<string, PsoStreamingCollectionAsset> paths;
        internal Func<GameObject, string> validateLoaded;
        internal bool released;
        internal Dictionary<string, AsyncOperationHandle<GraphicsStateCollection>> collections = new Dictionary<string, AsyncOperationHandle<GraphicsStateCollection>>();
        internal string identity;
        public bool IsCancelled { get; internal set; }
        public bool IsFinished { get; internal set; }
        public string Failure { get; internal set; }
        public PsoStreamOwner Owner { get; internal set; }
        public GameObject Asset => !released && handle.IsValid() && handle.Status == AsyncOperationStatus.Succeeded ? handle.Result : null;
        public void Unload() { loader.Unload(this); }
    }

    /// <summary>Main-thread, explicitly pumped optional adapter. Tick also observes cancelled loads until their
    /// real operations finish. Call BeginShutdown and keep ticking, then DrainAndDispose to fence submitted warmup.</summary>
    public sealed class PsoAddressablesLoader
    {
        private readonly PsoUnityStreamingBackend backend = new PsoUnityStreamingBackend();
        private readonly List<PsoAddressablesLoad> loads = new List<PsoAddressablesLoad>();
        private bool shuttingDown, disposed;
        public PsoStreamingCoordinator Coordinator { get; }
        public int RetainedHandleCount { get; private set; }
        public bool PendingLoads { get { foreach (var load in loads) if (!load.IsFinished) return true; return false; } }
        public PsoAddressablesLoader() { Coordinator = new PsoStreamingCoordinator(backend); }

        /// <param name="validateLoaded">Required compatibility/attestation hook, called AFTER the prefab and its shader
        /// dependencies load but BEFORE any collection is opened. Must verify the catalog/bundle contentId/revision and
        /// digest, build/shader identity and collection artifact hashes, and return the compatible collection namespace.
        /// Throw on unknown/stale/mismatch. A collection hash alone is not current-build attestation.</param>
        public PsoAddressablesLoad Load(object key, string contentId, string contentRevision,
            IReadOnlyDictionary<string, PsoStreamingCollectionAsset> collections, Func<GameObject, string> validateLoaded)
        {
            if (disposed || shuttingDown) throw new ObjectDisposedException(nameof(PsoAddressablesLoader));
            if (key == null || validateLoaded == null || collections == null) throw new ArgumentNullException("Load arguments");
            if (string.IsNullOrWhiteSpace(contentId) || string.IsNullOrWhiteSpace(contentRevision)) throw new ArgumentException("Known content identity required.");
            var paths = new Dictionary<string, PsoStreamingCollectionAsset>(StringComparer.Ordinal);
            foreach (var path in collections)
            {
                if (string.IsNullOrWhiteSpace(path.Key) || path.Value == null) throw new ArgumentException("Invalid phase collection.");
                if (string.IsNullOrWhiteSpace(path.Value.AddressableKey)) throw new ArgumentException("Addressables content requires a native .graphicsstate AddressableKey; raw-file GUID resolution does not bind bundle shaders.");
                paths.Add(path.Key, path.Value);
            }
            // Supersede older in-flight revisions now: their late completion must never revive old content.
            foreach (var previous in loads.ToArray())
                if (previous.contentId == contentId && previous.revision != contentRevision) Unload(previous);
            var load = new PsoAddressablesLoad { loader = this, contentId = contentId, revision = contentRevision,
                paths = paths, validateLoaded = validateLoaded, handle = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<GameObject>(key) };
            loads.Add(load); RetainedHandleCount++; return load;
        }

        public void Tick(int maximumStates = 64)
        {
            if (disposed) throw new ObjectDisposedException(nameof(PsoAddressablesLoader));
            foreach (var load in loads.ToArray())
            {
                if (load.IsFinished || !load.handle.IsDone) continue;
                bool collectionsDone = true;
                foreach (var collection in load.collections.Values) if (!collection.IsDone) collectionsDone = false;
                if (!collectionsDone) continue;
                if (load.IsCancelled) { load.IsFinished = true; Release(load); continue; }
                if (load.handle.Status != AsyncOperationStatus.Succeeded)
                {
                    load.IsFinished = true;
                    load.Failure = load.handle.OperationException?.ToString() ?? "Addressables load failed.";
                    Release(load); continue;
                }
                try
                {
                    if (load.identity == null)
                    {
                        load.identity = load.validateLoaded(load.handle.Result);
                        if (string.IsNullOrWhiteSpace(load.identity)) throw new InvalidOperationException("Loaded content compatibility was not attested.");
                        // Native collection assets must travel through the same bundle dependency graph as their
                        // shaders. LoadFromFile cannot resolve a bundle shader's GUID in a separate process.
                        foreach (var path in load.paths)
                        {
                            load.collections.Add(path.Key, UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<GraphicsStateCollection>(path.Value.AddressableKey));
                            RetainedHandleCount++;
                        }
                        continue;
                    }
                    var resolved = new Dictionary<string, GraphicsStateCollection>(StringComparer.Ordinal);
                    foreach (var pair in load.collections)
                    {
                        if (pair.Value.Status != AsyncOperationStatus.Succeeded) throw new InvalidOperationException("Addressables collection load failed: " + pair.Value.OperationException);
                        resolved.Add(pair.Key, pair.Value.Result);
                    }
                    load.IsFinished = true;
                    load.Owner = backend.RegisterLoaded(Coordinator, load.contentId, load.revision, load.identity, load.paths, () => Release(load), resolved);
                }
                catch (Exception error)
                {
                    load.Failure = error.ToString(); load.IsCancelled = true;
                    // A partial launch failure still owns earlier collection operations until their completion.
                    bool done = true; foreach (var collection in load.collections.Values) if (!collection.IsDone) done = false;
                    if (done) { load.IsFinished = true; Release(load); }
                }
            }
            if (!shuttingDown) Coordinator.Tick(maximumStates);
            // Prune bookkeeping once the native handle has actually released. Public load handles remain safe to inspect.
            loads.RemoveAll(load => load.released);
        }
        internal void Unload(PsoAddressablesLoad load)
        {
            if (load.loader != this) throw new ArgumentException("Foreign load handle.");
            if (load.IsCancelled) return;
            load.IsCancelled = true;
            if (load.Owner != null) Coordinator.Unload(load.Owner);
            else if (load.IsFinished) Release(load);
        }
        private void Release(PsoAddressablesLoad load)
        {
            if (load.released) return;
            load.released = true;
            foreach (var collection in load.collections.Values)
            {
                UnityEngine.AddressableAssets.Addressables.Release(collection); RetainedHandleCount--;
            }
            load.collections.Clear();
            UnityEngine.AddressableAssets.Addressables.Release(load.handle);
            RetainedHandleCount--;
        }
        public void BeginShutdown()
        {
            if (disposed) return;
            shuttingDown = true;
            foreach (var load in loads.ToArray()) Unload(load);
        }
        /// <summary>No blocking Addressables WaitForCompletion, which can deadlock remote/web loads. First pump
        /// cancellation to completion. Submitted opaque warmup is synchronously fenced here and may block.</summary>
        public void DrainAndDispose()
        {
            if (disposed) return;
            BeginShutdown();
            if (PendingLoads) throw new InvalidOperationException("Keep pumping Tick until pending Addressables operations finish before disposal.");
            Coordinator.Dispose(); disposed = true;
        }
    }
}
