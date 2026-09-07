using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Yanagisawa.ShaderHitchPipeline
{
    public static class PsoCostExecutionScope
    {
        // Only these package-owned output destinations are known not to select work.
        private static readonly HashSet<string> EvidenceOptions = new HashSet<string>(StringComparer.Ordinal)
        { "-pso-output", "-pso-warmup-receipt", "-logFile" };

        public static string FromArguments(string adapterId, string declaredScope, string[] actualArguments)
        {
            if (string.IsNullOrWhiteSpace(adapterId) || string.IsNullOrWhiteSpace(declaredScope) || actualArguments == null)
                throw new ArgumentException("Cost scope requires adapter, declared workload scope and actual execution arguments.");
            var values = new List<string> { "cost-execution-scope-v1", adapterId, declaredScope };
            for (int i = 0; i < actualArguments.Length; i++)
            {
                string value = actualArguments[i];
                if (value == null) throw new ArgumentException("Null execution argument.");
                if (EvidenceOptions.Contains(value))
                {
                    // A missing value is not silently consumed. Unknown flags and all route,
                    // worker, strategy and batch inputs remain part of the key.
                    if (i + 1 >= actualArguments.Length || actualArguments[i + 1] == null || actualArguments[i + 1].StartsWith("-", StringComparison.Ordinal))
                        throw new ArgumentException("Missing evidence-path argument for " + value);
                    i++;
                    continue;
                }
                values.Add(value);
            }
            var canonical = new StringBuilder();
            foreach (string value in values) canonical.Append(value.Length).Append(':').Append(value);
            using (SHA256 hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString())))
                    .Replace("-", "").ToLowerInvariant();
        }
    }
}
