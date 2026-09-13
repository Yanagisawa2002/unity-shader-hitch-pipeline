using System;
using System.IO;
using UnityEngine;

namespace Yanagisawa.ShaderHitchPipeline
{
    public sealed class PsoTraceSession : IDisposable
    {
        private readonly string sessionId;
        private readonly string phase;
        private readonly string outputRoot;
        private readonly bool sendToEditor;
        private readonly PsoTraceCoordinator coordinator;
        private bool ended;

        public PsoTraceSession(
            string sessionId,
            string phase,
            string outputRoot,
            bool sendToEditor,
            string[] tags = null,
            IPsoTraceBackend traceBackend = null)
        {
            this.sessionId = string.IsNullOrWhiteSpace(sessionId)
                ? PsoFileUtility.CreateRunId("trace")
                : PsoFileUtility.SanitizeFileName(sessionId);
            this.phase = PsoFileUtility.SanitizeFileName(phase);
            this.outputRoot = Path.GetFullPath(
                string.IsNullOrWhiteSpace(outputRoot)
                    ? PsoFileUtility.DefaultRuntimeOutputRoot()
                    : outputRoot);
            this.sendToEditor = sendToEditor;
            coordinator = new PsoTraceCoordinator(
                this.sessionId,
                this.phase,
                tags ?? Array.Empty<string>(),
                PsoFileUtility.UtcNowText(),
                PsoUnityEnvironment.Capture(),
                traceBackend ?? new PsoUnityGraphicsStateTraceBackend());
        }

        public bool IsTracing => !ended && coordinator.IsTracing;
        public string SessionId => sessionId;
        public string Phase => phase;

        public PsoSessionManifest End()
        {
            if (ended)
                throw new InvalidOperationException("The trace session has already ended.");
            ended = true;

            string fileStem = phase + "-" +
                              DateTime.UtcNow.ToString(
                                  "yyyyMMdd-HHmmss-fff",
                                  System.Globalization.CultureInfo.InvariantCulture);
            string sessionDirectory = Path.Combine(outputRoot, "Inbox", sessionId);
            string collectionPath = Path.Combine(
                sessionDirectory,
                fileStem + PsoConstants.GraphicsStateExtension);
            string manifestPath = Path.Combine(
                sessionDirectory,
                fileStem + PsoConstants.SessionManifestSuffix);

            Directory.CreateDirectory(sessionDirectory);
            PsoSessionManifest manifest = coordinator.Finish(
                collectionPath,
                sendToEditor,
                PsoFileUtility.ComputeSha256,
                PsoFileUtility.UtcNowText());
            PsoFileUtility.WriteJsonAtomic(manifestPath, manifest);
            if (!string.IsNullOrWhiteSpace(manifest.error))
                Debug.LogError(
                    "[ShaderHitchPipeline] Trace finalization failed: " + manifest.error);

            return manifest;
        }

        public void Dispose()
        {
            if (!ended)
                End();
            coordinator.Dispose();
        }
    }
}
