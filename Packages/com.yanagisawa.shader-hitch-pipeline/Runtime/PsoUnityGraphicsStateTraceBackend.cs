using System;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_6000_5_OR_NEWER
using GraphicsStateCollection = UnityEngine.Rendering.GraphicsStateCollection;
#else
using GraphicsStateCollection = UnityEngine.Experimental.Rendering.GraphicsStateCollection;
#endif

namespace Yanagisawa.ShaderHitchPipeline
{
    public sealed class PsoUnityGraphicsStateTraceBackend : IPsoTraceBackend
    {
        public const string UnityAdapterId = "unity.graphics-state-collection";
        public const string UnityArtifactType = "graphics-state-collection";

        private GraphicsStateCollection collection;
        private bool finished;

        public PsoUnityGraphicsStateTraceBackend()
        {
            collection = new GraphicsStateCollection
            {
                runtimePlatform = Application.platform,
                graphicsDeviceType = SystemInfo.graphicsDeviceType,
                qualityLevelName = PsoUnityEnvironment.CurrentQualityName(),
            };
            if (!collection.BeginTrace())
            {
                UnityEngine.Object.Destroy(collection);
                collection = null;
                throw new InvalidOperationException(
                    "Unity refused to begin GraphicsStateCollection tracing. " +
                    "Tracing is supported only on compatible runtime graphics backends.");
            }
        }

        public string AdapterId => UnityAdapterId;
        public string ArtifactType => UnityArtifactType;
        public bool IsTracing => !finished && collection != null && collection.isTracing;

        public PsoTraceArtifactResult Finish(string outputPath, bool sendToEditor)
        {
            if (finished)
                throw new InvalidOperationException("The trace backend has already finished.");
            if (collection == null)
                throw new ObjectDisposedException(nameof(PsoUnityGraphicsStateTraceBackend));

            finished = true;
            if (collection.isTracing)
                collection.EndTrace();

            var result = new PsoTraceArtifactResult
            {
                adapterId = AdapterId,
                artifactType = ArtifactType,
                variantCount = collection.variantCount,
                stateCount = collection.totalGraphicsStateCount,
            };
            result.saved = collection.SaveToFile(outputPath);
            if (!result.saved)
            {
                result.error = "GraphicsStateCollection.SaveToFile returned false.";
                return result;
            }

            if (sendToEditor)
                result.sentToEditor = collection.SendToEditor(
                    System.IO.Path.GetFileName(outputPath));
            return result;
        }

        public void Dispose()
        {
            if (collection == null)
                return;
            if (collection.isTracing)
                collection.EndTrace();
            UnityEngine.Object.Destroy(collection);
            collection = null;
        }
    }
}
