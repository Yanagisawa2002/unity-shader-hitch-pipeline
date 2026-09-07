using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace Yanagisawa.ShaderHitchPipeline
{
    /// <summary>Opt-in clock correlation sidecar; buffered to avoid measured-path disk I/O.</summary>
    public static class PsoSystemMarkers
    {
        [Serializable]
        private sealed class Marker
        {
            public int schemaVersion = 1;
            public int processId;
            public string sessionId;
            public string name;
            public string phase;
            public string utc;
            public long qpc;
            public long qpcFrequency;
            public long qpcBracketTicks;
            public int frame;
            public double realtimeSeconds;
        }

        private static readonly List<string> Rows = new List<string>();
        private static readonly string SessionId = Guid.NewGuid().ToString("N");
        private static string path;
        private static bool initialized;

        public static void Emit(string name, string phase = "")
        {
            if (!initialized)
            {
                initialized = true;
                path = PsoCommandLine.Current.GetString("-pso-system-markers", "");
                if (!string.IsNullOrWhiteSpace(path)) Application.quitting += Flush;
            }
            if (string.IsNullOrWhiteSpace(path)) return;
            // UTC read is bracketed by QPC samples; retain uncertainty instead of
            // presenting a sidecar anchor as an ETW provider event.
            long before = Stopwatch.GetTimestamp();
            string utc = DateTime.UtcNow.ToString("o");
            long after = Stopwatch.GetTimestamp();
            Rows.Add(JsonUtility.ToJson(new Marker {
                processId = Process.GetCurrentProcess().Id, sessionId = SessionId,
                name = name, phase = phase, utc = utc,
                qpc = before + (after - before) / 2, qpcFrequency = Stopwatch.Frequency,
                qpcBracketTicks = after - before,
                frame = Time.frameCount, realtimeSeconds = Time.realtimeSinceStartupAsDouble
            }));
        }

        public static void Flush()
        {
            if (string.IsNullOrWhiteSpace(path) || Rows.Count == 0) return;
            try
            {
                string fullPath = Path.GetFullPath(path);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                File.WriteAllLines(fullPath, Rows);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogWarning("System marker write failed: " + exception.Message);
            }
        }
    }
}
