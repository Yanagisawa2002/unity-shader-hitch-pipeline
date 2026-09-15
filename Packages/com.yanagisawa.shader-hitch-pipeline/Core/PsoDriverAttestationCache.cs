using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Yanagisawa.ShaderHitchPipeline
{
    /// <summary>Platform-provided observations, not file-byte attestations.</summary>
    public sealed class PsoDriverIdentityProbe
    {
        public string Device, RegistryIdentity, Version;
        public PsoDriverFileStamp[] Modules;
    }

    public sealed class PsoDriverFileStamp
    {
        public string File;
        public long LoadedModule, Bytes, LastWriteUtcTicks, CreationUtcTicks;
    }

    /// <summary>
    /// Process-local byte attestation with metadata-checked reuse. A changed
    /// probe rehashes; failed/unstable probes discard all cached evidence.
    /// Metadata reuse is explicitly NOT a fresh byte hash. The caller must
    /// invalidate on an application-known device reset and refresh scheduling
    /// compatibility after environment changes. No persistent cache is used.
    /// </summary>
    public sealed class PsoDriverAttestationCache
    {
        private readonly object gate = new object();
        private readonly Func<string, string> hashText;
        private string fingerprint;
        private PsoEnvironmentSnapshot attested;

        public PsoDriverAttestationCache(Func<string, string> hashText)
        {
            this.hashText = hashText ?? throw new ArgumentNullException(nameof(hashText));
        }

        public void Invalidate()
        {
            lock (gate) { fingerprint = null; attested = null; }
        }

        public void CaptureInto(PsoEnvironmentSnapshot output, Func<PsoDriverIdentityProbe> probe,
            Func<string, string> hashFile, Func<string> utcNow)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            lock (gate)
            {
                Clear(output);
                try
                {
                    PsoDriverIdentityProbe before = probe();
                    string key = Fingerprint(before);
                    bool reused = attested != null && key == fingerprint;
                    if (!reused)
                    {
                        // A failed replacement must never expose the old identity.
                        fingerprint = null; attested = null;
                        var canonical = new StringBuilder(before.RegistryIdentity);
                        var modules = new List<PsoDriverModuleIdentity>();
                        foreach (PsoDriverFileStamp file in Sorted(before.Modules))
                        {
                            string hash = hashFile(file.File);
                            if (!PsoCompatibility.IsSha256(hash)) throw new IOException("Invalid driver module byte hash.");
                            canonical.Append('\n').Append(file.File).Append('|').Append(hash);
                            modules.Add(new PsoDriverModuleIdentity { file = file.File, sha256 = hash,
                                bytes = file.Bytes, lastWriteUtcTicks = file.LastWriteUtcTicks });
                        }
                        if (Fingerprint(probe()) != key)
                            throw new IOException("Driver identity changed during byte attestation; no snapshot accepted.");
                        string identity = hashText(canonical.ToString());
                        if (!PsoCompatibility.IsSha256(identity)) throw new IOException("Invalid driver identity hash.");
                        attested = new PsoEnvironmentSnapshot {
                            driverVersion = before.Version, driverRegistryIdentity = before.RegistryIdentity,
                            driverIdentity = identity,
                            driverIdentitySource = PsoCompatibility.DriverIdentitySource,
                            driverModules = modules.ToArray(), driverBytesAttestedUtc = utcNow() };
                        fingerprint = key;
                    }
                    Copy(attested, output);
                    output.driverMetadataCheckedUtc = utcNow();
                    output.driverByteAttestationReused = reused;
                }
                catch
                {
                    fingerprint = null; attested = null;
                    Clear(output);
                    throw;
                }
            }
        }

        private static PsoDriverFileStamp[] Sorted(PsoDriverFileStamp[] source)
        {
            if (source == null || source.Length == 0) throw new IOException("Missing loaded driver modules.");
            var items = (PsoDriverFileStamp[])source.Clone();
            foreach (var item in items)
                if (item == null || string.IsNullOrWhiteSpace(item.File) || item.LoadedModule == 0 ||
                    item.Bytes <= 0 || item.LastWriteUtcTicks <= 0 || item.CreationUtcTicks <= 0)
                    throw new IOException("Invalid loaded driver module metadata.");
            Array.Sort(items, (a,b) => StringComparer.OrdinalIgnoreCase.Compare(a.File,b.File));
            for (int i = 1; i < items.Length; ++i)
                if (string.Equals(items[i-1].File, items[i].File, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Ambiguous duplicate loaded driver module.");
            return items;
        }

        private static string Fingerprint(PsoDriverIdentityProbe probe)
        {
            if (probe == null || string.IsNullOrWhiteSpace(probe.Device) ||
                string.IsNullOrWhiteSpace(probe.RegistryIdentity) || string.IsNullOrWhiteSpace(probe.Version))
                throw new IOException("Missing current driver identity metadata.");
            var value = new StringBuilder();
            Add(value, probe.Device); Add(value, probe.RegistryIdentity); Add(value, probe.Version);
            foreach (var file in Sorted(probe.Modules))
            {
                Add(value, file.File); Add(value, file.LoadedModule.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Add(value, file.Bytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Add(value, file.LastWriteUtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Add(value, file.CreationUtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            return value.ToString();
        }
        private static void Add(StringBuilder result, string value) => result.Append(value.Length).Append(':').Append(value);
        private static void Clear(PsoEnvironmentSnapshot value)
        {
            value.driverVersion = value.driverRegistryIdentity = value.driverIdentity = value.driverIdentitySource = null;
            value.driverBytesAttestedUtc = value.driverMetadataCheckedUtc = value.driverIdentityError = null;
            value.driverByteAttestationReused = false;
            value.driverModules = Array.Empty<PsoDriverModuleIdentity>();
        }
        private static void Copy(PsoEnvironmentSnapshot source, PsoEnvironmentSnapshot target)
        {
            target.driverVersion = source.driverVersion; target.driverRegistryIdentity = source.driverRegistryIdentity;
            target.driverIdentity = source.driverIdentity; target.driverIdentitySource = source.driverIdentitySource;
            target.driverBytesAttestedUtc = source.driverBytesAttestedUtc;
            target.driverModules = new PsoDriverModuleIdentity[source.driverModules.Length];
            for (int i = 0; i < target.driverModules.Length; ++i)
            {
                var item = source.driverModules[i];
                target.driverModules[i] = new PsoDriverModuleIdentity { file = item.file, sha256 = item.sha256,
                    bytes = item.bytes, lastWriteUtcTicks = item.lastWriteUtcTicks };
            }
        }
    }
}
