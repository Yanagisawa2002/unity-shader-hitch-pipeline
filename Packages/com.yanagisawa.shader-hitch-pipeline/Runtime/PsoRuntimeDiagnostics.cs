using System;
using System.Diagnostics;
using UnityEngine;

namespace Yanagisawa.ShaderHitchPipeline
{
    // Opt-in diagnosis of our own synchronous evidence overhead. These intervals
    // are CPU elapsed time, not native job, GPU or presentation measurements.
    internal sealed class PsoRuntimeDiagnostics : IDisposable
    {
        [Serializable] private sealed class Sample
        {
            public string operation;
            public int frame;
            public double startRealtimeSeconds;
            public double milliseconds;
        }
        private readonly Sample sample;
        private readonly Stopwatch stopwatch;
        private PsoRuntimeDiagnostics(string operation)
        {
            sample = new Sample { operation = operation, frame = Time.frameCount,
                startRealtimeSeconds = Time.realtimeSinceStartupAsDouble };
            stopwatch = Stopwatch.StartNew();
        }
        public static PsoRuntimeDiagnostics Begin(string operation) =>
            PsoCommandLine.Current.HasFlag("-pso-diagnostic-attestation")
                ? new PsoRuntimeDiagnostics(operation) : null;
        public void Dispose()
        {
            stopwatch.Stop();
            sample.milliseconds = stopwatch.Elapsed.TotalMilliseconds;
            UnityEngine.Debug.Log("[PSO Runtime Diagnostic] " + JsonUtility.ToJson(sample));
        }
    }
}
