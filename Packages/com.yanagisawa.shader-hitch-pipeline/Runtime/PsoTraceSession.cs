using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace Yanagisawa.ShaderHitchPipeline
{
    public sealed class PsoTraceSession : IDisposable
    {
        private readonly string sessionId;
        private readonly string phase;
        private readonly string outputRoot;
        private readonly bool sendToEditor;
        private readonly string[] tags;
        private readonly string startedUtc;
        private GraphicsStateCollection collection;
        private bool ended;

        public PsoTraceSession(
            string sessionId,
            string phase,
            string outputRoot,
            bool sendToEditor,
            string[] tags = null)
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
            this.tags = tags ?? Array.Empty<string>();
            startedUtc = PsoFileUtility.UtcNowText();

            collection = new GraphicsStateCollection
            {
                runtimePlatform = Application.platform,
                graphicsDeviceType = SystemInfo.graphicsDeviceType,
                qualityLevelName = CurrentQualityName(),
            };
            if (!collection.BeginTrace())
                throw new InvalidOperationException(
                    "Unity refused to begin GraphicsStateCollection tracing. " +
                    "Tracing is supported only on compatible runtime graphics backends.");
        }

        public bool IsTracing => !ended && collection != null && collection.isTracing;
        public string SessionId => sessionId;
        public string Phase => phase;

        public PsoSessionManifest End()
        {
            if (ended)
                throw new InvalidOperationException("The trace session has already ended.");
            ended = true;

            var manifest = new PsoSessionManifest
            {
                sessionId = sessionId,
                phase = phase,
                startedUtc = startedUtc,
                endedUtc = PsoFileUtility.UtcNowText(),
                tags = tags,
                environment = PsoEnvironmentSnapshot.Capture(),
            };

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

            try
            {
                if (collection.isTracing)
                    collection.EndTrace();
                Directory.CreateDirectory(sessionDirectory);
                manifest.variantCount = collection.variantCount;
                manifest.graphicsStateCount = collection.totalGraphicsStateCount;
                manifest.saved = collection.SaveToFile(collectionPath);
                if (!manifest.saved)
                    throw new IOException("GraphicsStateCollection.SaveToFile returned false.");

                manifest.collectionFile = Path.GetFileName(collectionPath);
                manifest.collectionSha256 = PsoFileUtility.ComputeSha256(collectionPath);
                if (sendToEditor)
                    manifest.sentToEditor = collection.SendToEditor(Path.GetFileName(collectionPath));
            }
            catch (Exception exception)
            {
                manifest.error = exception.ToString();
                Debug.LogError("[ShaderHitchPipeline] Trace finalization failed: " + exception);
            }
            finally
            {
                PsoFileUtility.WriteJsonAtomic(manifestPath, manifest);
                collection = null;
            }

            return manifest;
        }

        public void Dispose()
        {
            if (!ended)
                End();
        }

        private static string CurrentQualityName()
        {
            int index = QualitySettings.GetQualityLevel();
            string[] names = QualitySettings.names;
            return index >= 0 && index < names.Length ? names[index] : "Unknown";
        }
    }
}
