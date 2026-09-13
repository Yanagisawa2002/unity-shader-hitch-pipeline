using System;
using System.IO;

namespace Yanagisawa.ShaderHitchPipeline
{
    /// <summary>
    /// Engine-neutral trace lifecycle. The host supplies the opaque backend,
    /// environment, clock, artifact hash function, and document persistence.
    /// </summary>
    public sealed class PsoTraceCoordinator : IDisposable
    {
        private readonly string sessionId;
        private readonly string phase;
        private readonly string[] tags;
        private readonly string startedUtc;
        private readonly PsoEnvironmentSnapshot environment;
        private IPsoTraceBackend backend;
        private bool ended;

        public PsoTraceCoordinator(
            string sessionId,
            string phase,
            string[] tags,
            string startedUtc,
            PsoEnvironmentSnapshot environment,
            IPsoTraceBackend backend)
        {
            this.sessionId = sessionId ?? string.Empty;
            this.phase = phase ?? string.Empty;
            this.tags = tags ?? Array.Empty<string>();
            this.startedUtc = startedUtc ?? string.Empty;
            this.environment = environment;
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
        }

        public bool IsTracing => !ended && backend != null && backend.IsTracing;
        public bool HasEnded => ended;

        public PsoSessionManifest Finish(
            string artifactPath,
            bool sendToEditor,
            Func<string, string> computeArtifactHash,
            string endedUtc)
        {
            if (ended)
                throw new InvalidOperationException("The trace has already ended.");
            if (string.IsNullOrWhiteSpace(artifactPath))
                throw new ArgumentException("An artifact path is required.", nameof(artifactPath));
            if (computeArtifactHash == null)
                throw new ArgumentNullException(nameof(computeArtifactHash));

            ended = true;
            var manifest = new PsoSessionManifest
            {
                sessionId = sessionId,
                phase = phase,
                startedUtc = startedUtc,
                endedUtc = endedUtc ?? string.Empty,
                tags = tags,
                environment = environment,
                adapterId = backend.AdapterId,
                artifactType = backend.ArtifactType,
                error = string.Empty,
            };

            try
            {
                PsoTraceArtifactResult artifact = backend.Finish(
                    artifactPath,
                    sendToEditor);
                manifest.adapterId = artifact.adapterId;
                manifest.artifactType = artifact.artifactType;
                manifest.variantCount = artifact.variantCount;
                manifest.graphicsStateCount = artifact.stateCount;
                manifest.saved = artifact.saved;
                manifest.sentToEditor = artifact.sentToEditor;
                if (!artifact.saved)
                {
                    throw new IOException(string.IsNullOrWhiteSpace(artifact.error)
                        ? "Trace backend did not save its artifact."
                        : artifact.error);
                }

                manifest.collectionFile = Path.GetFileName(artifactPath);
                manifest.collectionSha256 = computeArtifactHash(artifactPath);
            }
            catch (Exception exception)
            {
                manifest.error = exception.ToString();
            }
            finally
            {
                backend.Dispose();
                backend = null;
            }

            return manifest;
        }

        public void Dispose()
        {
            if (backend == null)
                return;
            ended = true;
            backend.Dispose();
            backend = null;
        }
    }
}
