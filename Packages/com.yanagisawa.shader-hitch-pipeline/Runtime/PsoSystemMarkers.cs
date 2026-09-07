using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
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
            public string clockSource;
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

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [DllImport("kernel32.dll")]
        private static extern bool QueryPerformanceCounter(out long value);
        [DllImport("kernel32.dll")]
        private static extern bool QueryPerformanceFrequency(out long value);
        private const string ClockSource = "windows-query-performance-counter-v1";
        private static long ReadCounter()
        {
            if (!QueryPerformanceCounter(out long value)) throw new InvalidOperationException("Native QPC unavailable.");
            return value;
        }
        private static long ReadFrequency()
        {
            if (!QueryPerformanceFrequency(out long value) || value <= 0) throw new InvalidOperationException("Native QPC frequency unavailable.");
            return value;
        }
#else
        private const string ClockSource = "managed-stopwatch-process-clock-v1";
        private static long ReadCounter() => Stopwatch.GetTimestamp();
        private static long ReadFrequency() => Stopwatch.Frequency;
#endif

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
            // Unity's managed Stopwatch can use a process-relative epoch. ETW
            // uses native absolute QPC, so Windows markers must call that API.
            long frequency = ReadFrequency();
            long before = ReadCounter();
            string utc = DateTime.UtcNow.ToString("o");
            long after = ReadCounter();
            Rows.Add(JsonUtility.ToJson(new Marker {
                processId = Process.GetCurrentProcess().Id, sessionId = SessionId,
                name = name, phase = phase, utc = utc,
                clockSource = ClockSource,
                qpc = before + (after - before) / 2, qpcFrequency = frequency,
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
