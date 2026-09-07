using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

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
                load.IsFinished = true;
                if (load.IsCancelled) { Release(load); continue; }
                if (load.handle.Status != AsyncOperationStatus.Succeeded)
                {
                    load.Failure = load.handle.OperationException?.ToString() ?? "Addressables load failed.";
                    Release(load); continue;
                }
                try
                {
                    string identity = load.validateLoaded(load.handle.Result);
                    if (string.IsNullOrWhiteSpace(identity)) throw new InvalidOperationException("Loaded content compatibility was not attested.");
                    load.Owner = backend.RegisterLoaded(Coordinator, load.contentId, load.revision, identity, load.paths, () => Release(load));
                }
                catch (Exception error) { load.Failure = error.ToString(); Release(load); }
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
