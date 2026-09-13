using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System.Runtime.InteropServices;
#endif

namespace Yanagisawa.ShaderHitchPipeline
{
    public static class PsoWindowsDriverIdentity
    {
        private static readonly PsoDriverAttestationCache Cache = new PsoDriverAttestationCache(PsoFileUtility.ComputeTextSha256);

        /// <summary>Discard process-local attestation after an application-known device reset.</summary>
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Invalidate() => Cache.Invalidate();
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        [DllImport("advapi32.dll", EntryPoint = "RegOpenKeyExW", CharSet = CharSet.Unicode)]
        private static extern int RegOpenKeyEx(IntPtr key, string subkey, uint options, uint access, out IntPtr result);
        [DllImport("advapi32.dll", EntryPoint = "RegEnumKeyExW", CharSet = CharSet.Unicode)]
        private static extern int RegEnumKeyEx(IntPtr key, uint index, StringBuilder name, ref uint length,
            IntPtr reserved, IntPtr className, IntPtr classLength, IntPtr lastWriteTime);
        [DllImport("advapi32.dll", EntryPoint = "RegGetValueW", CharSet = CharSet.Unicode)]
        private static extern int RegGetValue(IntPtr key, string subkey, string value, uint flags,
            out uint type, StringBuilder data, ref uint size);
        [DllImport("advapi32.dll")]
        private static extern int RegCloseKey(IntPtr key);

        private static string ReadString(IntPtr key, string subkey, string value)
        {
            var data = new StringBuilder(4096);
            uint bytes = 8192;
            int status = RegGetValue(key, subkey, value, 2, out _, data, ref bytes);
            if (status == 2) return null;
            if (status != 0) throw new IOException("Registry value " + value + " unavailable, Win32=" + status);
            return data.ToString();
        }
#endif
        // Unity's graphicsDeviceVersion on D3D12 can be just an API feature-level string.
        // Bind installed display-driver metadata AND loaded driver-package DLL bytes.
        public static void Capture(PsoEnvironmentSnapshot environment)
        {
            using var diagnostic = PsoRuntimeDiagnostics.Begin("driver-attestation");
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            try
            {
                Cache.CaptureInto(environment, () => Probe(environment), PsoFileUtility.ComputeSha256,
                    PsoFileUtility.UtcNowText);
            }
            catch (Exception exception)
            {
                environment.driverIdentityError = exception.GetType().Name + ": " + exception.Message;
            }
#else
            environment.driverIdentityError = "Driver identity provider unavailable on this platform.";
#endif
        }
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        private static PsoDriverIdentityProbe Probe(PsoEnvironmentSnapshot environment)
        {
                string vendor = "VEN_" + environment.graphicsDeviceVendorId.ToString("X4");
                string device = "DEV_" + environment.graphicsDeviceId.ToString("X4");
                string version = null;
                string registryIdentity = null;
                int matches = 0;
                IntPtr displayClass;
                int open = RegOpenKeyEx(new IntPtr(unchecked((int)0x80000002)),
                    @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}",
                    0, 0x20119, out displayClass);
                if (open != 0) throw new IOException("Display driver registry unavailable, Win32=" + open);
                try
                {
                    for (uint index = 0; ; index++)
                    {
                        var nameBuffer = new StringBuilder(256);
                        uint length = 256;
                        int status = RegEnumKeyEx(displayClass, index, nameBuffer, ref length,
                            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                        if (status == 259) break;
                        if (status != 0) throw new IOException("Registry enumeration failed, Win32=" + status);
                        string name = nameBuffer.ToString();
                        if (!int.TryParse(name, out _)) continue;
                        string hardware = ReadString(displayClass, name, "MatchingDeviceId");
                        if (hardware == null || hardware.IndexOf(vendor, StringComparison.OrdinalIgnoreCase) < 0 ||
                            hardware.IndexOf(device, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        matches++;
                        version = ReadString(displayClass, name, "DriverVersion");
                        registryIdentity = hardware + "|" + version + "|" + ReadString(displayClass, name, "DriverDate") + "|" + ReadString(displayClass, name, "InfPath");
                    }
                }
                finally { RegCloseKey(displayClass); }
                if (matches != 1 || string.IsNullOrWhiteSpace(version))
                    throw new IOException("Selected display-driver registry identity is missing or ambiguous (matches=" + matches + ").");
                var modules = new List<PsoDriverFileStamp>();
                foreach (var module in PsoWindowsProcessModules.CaptureModules())
                {
                    string path = module.FileName;
                    if (path.IndexOf(@"\DriverStore\FileRepository\", StringComparison.OrdinalIgnoreCase) < 0 ||
                        !string.Equals(Path.GetExtension(path), ".dll", StringComparison.OrdinalIgnoreCase)) continue;
                    var file = new FileInfo(path);
                    if (!file.Exists) throw new IOException("Loaded driver module file unavailable.");
                    modules.Add(new PsoDriverFileStamp { File = path, LoadedModule = module.BaseAddress,
                        Bytes = file.Length, LastWriteUtcTicks = file.LastWriteTimeUtc.Ticks,
                        CreationUtcTicks = file.CreationTimeUtc.Ticks });
                }
                if (modules.Count == 0) throw new IOException("No loaded DriverStore DLLs; actual user-mode driver identity unavailable.");
                return new PsoDriverIdentityProbe { Device = vendor + "|" + device + "|" +
                    environment.graphicsDeviceType + "|" + environment.graphicsDeviceVersion,
                    RegistryIdentity = registryIdentity, Version = version, Modules = modules.ToArray() };
        }
#endif
    }
}
