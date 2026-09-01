using System;
using UnityEngine;

namespace Yanagisawa.ShaderHitchPipeline
{
    [DefaultExecutionOrder(-32000)]
    [DisallowMultipleComponent]
    public sealed class PsoTraceController : MonoBehaviour
    {
        private static PsoTraceController instance;
        private PsoTraceSession activeSession;
        private string sessionId;
        private string outputRoot;
        private bool sendToEditor;
        private double autoStopAt;

        public static PsoTraceController Instance => instance;
        public bool IsTracing => activeSession != null && activeSession.IsTracing;
        public string ActivePhase => activeSession == null ? string.Empty : activeSession.Phase;

        public event Action<PsoSessionManifest> SessionCompleted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void BootstrapFromCommandLine()
        {
            PsoCommandLine commandLine = PsoCommandLine.Current;
            if (!commandLine.HasFlag(PsoConstants.TraceArgument))
                return;

            PsoTraceController controller = EnsureInstance();
            controller.Configure(
                commandLine.GetString(
                    PsoConstants.SessionArgument,
                    PsoFileUtility.CreateRunId("trace")),
                commandLine.GetString(
                    PsoConstants.OutputArgument,
                    PsoFileUtility.DefaultRuntimeOutputRoot()),
                commandLine.HasFlag(PsoConstants.SendToEditorArgument));
            controller.BeginPhase(
                commandLine.GetString(PsoConstants.TracePhaseArgument, "startup"));

            double autoStopSeconds = commandLine.GetDouble(
                PsoConstants.TraceAutoStopArgument,
                0.0,
                0.0,
                86400.0);
            if (autoStopSeconds > 0.0)
                controller.autoStopAt = Time.realtimeSinceStartupAsDouble + autoStopSeconds;
        }

        public static PsoTraceController EnsureInstance()
        {
            if (instance != null)
                return instance;

            var host = new GameObject("Shader Hitch Pipeline Trace");
            DontDestroyOnLoad(host);
            return host.AddComponent<PsoTraceController>();
        }

        public void Configure(string newSessionId, string newOutputRoot, bool shouldSendToEditor)
        {
            if (IsTracing)
                throw new InvalidOperationException("Stop the active trace before reconfiguration.");
            sessionId = string.IsNullOrWhiteSpace(newSessionId)
                ? PsoFileUtility.CreateRunId("trace")
                : newSessionId;
            outputRoot = string.IsNullOrWhiteSpace(newOutputRoot)
                ? PsoFileUtility.DefaultRuntimeOutputRoot()
                : newOutputRoot;
            sendToEditor = shouldSendToEditor;
        }

        public void BeginPhase(string phase, params string[] tags)
        {
            if (IsTracing)
                EndPhase();
            if (string.IsNullOrWhiteSpace(sessionId))
                Configure(PsoFileUtility.CreateRunId("trace"), null, false);

            activeSession = new PsoTraceSession(
                sessionId,
                phase,
                outputRoot,
                sendToEditor,
                tags);
            Debug.Log("[ShaderHitchPipeline] Tracing phase '" + phase +
                      "' in session '" + activeSession.SessionId + "'.");
        }

        public PsoSessionManifest EndPhase()
        {
            if (activeSession == null)
                return null;

            PsoTraceSession session = activeSession;
            activeSession = null;
            PsoSessionManifest manifest = session.End();
            Debug.Log("[ShaderHitchPipeline] Trace phase '" + manifest.phase +
                      "' saved " + manifest.variantCount + " variants / " +
                      manifest.graphicsStateCount + " graphics states.");
            SessionCompleted?.Invoke(manifest);
            return manifest;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            if (autoStopAt > 0.0 &&
                Time.realtimeSinceStartupAsDouble >= autoStopAt)
            {
                autoStopAt = 0.0;
                EndPhase();
            }
        }

        private void OnApplicationQuit()
        {
            if (activeSession != null)
                EndPhase();
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }
    }
}
