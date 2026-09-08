using System;
using System.IO;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_6000_5_OR_NEWER
using GraphicsStateCollection = UnityEngine.Rendering.GraphicsStateCollection;
#else
using GraphicsStateCollection = UnityEngine.Experimental.Rendering.GraphicsStateCollection;
#endif

namespace Yanagisawa.ShaderHitchPipeline
{
    public sealed class PsoUnityGraphicsStateWarmupBackend : IPsoWarmupBackend
    {
        private GraphicsStateCollection collection;

        public PsoUnityGraphicsStateWarmupBackend(string collectionPath)
        {
            if (string.IsNullOrWhiteSpace(collectionPath))
                throw new ArgumentException("A collection path is required.", nameof(collectionPath));

            collection = new GraphicsStateCollection();
            if (!collection.LoadFromFile(Path.GetFullPath(collectionPath)))
            {
                UnityEngine.Object.Destroy(collection);
                collection = null;
                throw new IOException("GraphicsStateCollection.LoadFromFile returned false.");
            }
            try
            {
                if (collection.runtimePlatform != Application.platform)
                    throw new InvalidDataException(
                        "Collection platform is " + collection.runtimePlatform +
                        ", current platform is " + Application.platform + ".");
                if (collection.graphicsDeviceType != SystemInfo.graphicsDeviceType)
                    throw new InvalidDataException(
                        "Collection graphics API is " + collection.graphicsDeviceType +
                        ", current API is " + SystemInfo.graphicsDeviceType + ".");
            }
            catch
            {
                UnityEngine.Object.Destroy(collection);
                collection = null;
                throw;
            }
        }

        public string AdapterId => PsoUnityGraphicsStateTraceBackend.UnityAdapterId;
        public string RuntimePlatform => collection == null
            ? string.Empty
            : collection.runtimePlatform.ToString();
        public string GraphicsApi => collection == null
            ? string.Empty
            : collection.graphicsDeviceType.ToString();
        public int TotalStateCount => collection == null
            ? 0
            : collection.totalGraphicsStateCount;
        public int CompletedStateCount => collection == null
            ? 0
            : collection.completedWarmupCount;
        public bool IsWarmedUp => collection != null && collection.isWarmedUp;
        public bool PreferNativeAsyncBulkForDeadline
        {
            get
            {
                string requested = PsoCommandLine.Current.GetString(
                    PsoConstants.DeadlineBackendModeArgument,
                    "auto");
                if (string.Equals(
                        requested,
                        "progressive",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                if (string.Equals(
                        requested,
                        "native-async-bulk",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                if (!string.Equals(
                        requested,
                        "auto",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException(
                        "Unsupported deadline backend mode '" + requested +
                        "'. Expected auto, progressive, or native-async-bulk.");
                }
#if UNITY_6000_5_OR_NEWER
                return false;
#else
                // Unity 6000.1's experimental WarmUpProgressively job has
                // pathological completion latency for small repeated jobs.
                // WarmUp itself remains asynchronous and completes the same
                // collection efficiently on the worker pool.
                return true;
#endif
            }
        }

        public IPsoWarmupBatch Schedule(int maximumStates, bool throughput)
        {
            if (collection == null)
                throw new ObjectDisposedException(nameof(PsoUnityGraphicsStateWarmupBackend));
            if (maximumStates < 1)
                throw new ArgumentOutOfRangeException(nameof(maximumStates));

            JobHandle job;
#if UNITY_6000_5_OR_NEWER
            job = throughput
                ? collection.WarmUp(default(JobHandle), false)
                : collection.WarmUpProgressively(
                    maximumStates,
                    default(JobHandle),
                    false);
#else
            job = throughput
                ? collection.WarmUp(default(JobHandle))
                : collection.WarmUpProgressively(
                    maximumStates,
                    default(JobHandle));
#endif
            return new UnityWarmupBatch(job);
        }

        public void Dispose()
        {
            if (collection == null)
                return;
            UnityEngine.Object.Destroy(collection);
            collection = null;
        }

        private sealed class UnityWarmupBatch : IPsoWarmupBatch
        {
            private JobHandle job;
            private bool completed;

            public UnityWarmupBatch(JobHandle job)
            {
                this.job = job;
            }

            public bool IsCompleted => completed || job.IsCompleted;

            public void Complete()
            {
                if (completed)
                    return;
                job.Complete();
                completed = true;
            }

            public void Dispose()
            {
                // The orchestrator calls Complete before disposal. Do not turn an
                // application-quit path into an unexpected blocking wait.
            }
        }
    }
}
