using System;

namespace Yanagisawa.ShaderHitchPipeline
{
    public sealed class PsoTraceArtifactResult
    {
        public string adapterId = string.Empty;
        public string artifactType = string.Empty;
        public int variantCount;
        public int stateCount;
        public bool saved;
        public bool sentToEditor;
        public string error = string.Empty;
    }

    /// <summary>
    /// Engine adapter boundary for recording an opaque pipeline-state artifact.
    /// The core owns manifests and plans; the adapter owns engine-specific capture.
    /// </summary>
    public interface IPsoTraceBackend : IDisposable
    {
        string AdapterId { get; }
        string ArtifactType { get; }
        bool IsTracing { get; }
        PsoTraceArtifactResult Finish(string outputPath, bool sendToEditor);
    }

    /// <summary>
    /// A single opaque warmup operation. Implementations may wrap an engine job,
    /// a native API fence, a process RPC, or a synchronous operation.
    /// </summary>
    public interface IPsoWarmupBatch : IDisposable
    {
        bool IsCompleted { get; }
        void Complete();
    }

    /// <summary>
    /// Engine-neutral scheduler boundary. The scheduler reasons only about state
    /// counts, deadlines, cost, and completion; adapter-specific collections stay
    /// behind this interface.
    /// </summary>
    public interface IPsoWarmupBackend : IDisposable
    {
        string AdapterId { get; }
        string RuntimePlatform { get; }
        string GraphicsApi { get; }
        int TotalStateCount { get; }
        int CompletedStateCount { get; }
        bool IsWarmedUp { get; }
        /// <summary>
        /// True when the engine's progressive API is not a viable interactive
        /// scheduler primitive and scheduled deferred phases should instead use
        /// one deadline-gated native asynchronous warmup operation.
        /// </summary>
        bool PreferNativeAsyncBulkForDeadline { get; }
        IPsoWarmupBatch Schedule(int maximumStates, bool throughput);
    }
}
