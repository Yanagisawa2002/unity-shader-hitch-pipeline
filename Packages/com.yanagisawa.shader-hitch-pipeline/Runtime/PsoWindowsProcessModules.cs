using System;
using System.Collections.Generic;
using System.IO;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System.Runtime.InteropServices;
using System.Text;
#endif

namespace Yanagisawa.ShaderHitchPipeline
{
    // System.Diagnostics.Process.Modules is not implemented by the pinned
    // IL2CPP runtime. Query this process directly without loading/unloading DLLs.
    internal static class PsoWindowsProcessModules
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        [DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll", EntryPoint = "K32EnumProcessModules", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumProcessModules(IntPtr process, [Out] IntPtr[] modules, uint bytes, out uint needed);
        [DllImport("kernel32.dll", EntryPoint = "GetModuleFileNameW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern uint GetModuleFileName(IntPtr module, StringBuilder path, uint capacity);

        internal static string[] CaptureFileNames()
        {
            int capacity = 128;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                var modules = new IntPtr[capacity];
                uint bytes = checked((uint)(capacity * IntPtr.Size));
                if (!EnumProcessModules(GetCurrentProcess(), modules, bytes, out uint needed))
                    throw new IOException("Current-process module enumeration failed, Win32=" + Marshal.GetLastWin32Error());
                if (needed == 0 || needed % IntPtr.Size != 0)
                    throw new IOException("Current-process module enumeration returned an invalid size.");
                if (needed > bytes)
                {
                    long required = needed / IntPtr.Size;
                    if (required > 16384) throw new IOException("Current-process module inventory exceeds the declared limit.");
                    capacity = (int)required;
                    continue;
                }
                var paths = new List<string>((int)(needed / IntPtr.Size));
                for (int i = 0; i < needed / IntPtr.Size; i++)
                {
                    if (modules[i] == IntPtr.Zero) throw new IOException("Current-process module inventory contains a null handle.");
                    paths.Add(ReadFileName(modules[i]));
                }
                return paths.ToArray();
            }
            throw new IOException("Current-process module inventory kept changing during capture.");
        }

        private static string ReadFileName(IntPtr module)
        {
            for (int capacity = 512; capacity <= 32768; capacity *= 2)
            {
                var path = new StringBuilder(capacity);
                uint length = GetModuleFileName(module, path, (uint)capacity);
                if (length == 0) throw new IOException("Loaded module filename unavailable, Win32=" + Marshal.GetLastWin32Error());
                if (length < capacity) return path.ToString();
            }
            throw new IOException("Loaded module filename exceeds the declared Windows path limit.");
        }
#endif
    }
}
