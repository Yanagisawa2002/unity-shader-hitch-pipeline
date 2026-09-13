using System;
using System.IO;
using UnityEngine;

namespace Yanagisawa.ShaderHitchPipeline.HotsetFixture
{
    public static class PsoHotsetJsonRoundtripProbe
    {
        [Serializable] private sealed class Receipt
        {
            public string unityVersion;
            public string originalStoredHash;
            public string firstReadContentHash;
            public string secondReadContentHash;
            public bool originalHashSurvivedRead;
        }

        // A diagnostic, not an alternate plan loader. Retains the unmodified source
        // and parsed output so the shared serializer can acquire a regression case.
        public static void Run()
        {
            string input = PsoCommandLine.Current.GetString("-hotset-json-input", "");
            string output = PsoCommandLine.Current.GetString("-hotset-json-output", "");
            if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(output))
                throw new ArgumentException("Specify input plan and output directory.");
            Directory.CreateDirectory(output);
            string raw = File.ReadAllText(input);
            var first = JsonUtility.FromJson<PsoWarmupPlanDocument>(raw);
            string parsed = JsonUtility.ToJson(first, true);
            var second = JsonUtility.FromJson<PsoWarmupPlanDocument>(parsed);
            File.WriteAllText(Path.Combine(output, "original.json"), raw);
            File.WriteAllText(Path.Combine(output, "first-read.json"), parsed);
            File.WriteAllText(Path.Combine(output, "second-read.json"), JsonUtility.ToJson(second, true));
            PsoFileUtility.WriteJsonAtomic(Path.Combine(output, "receipt.json"), new Receipt {
                unityVersion = Application.unityVersion,
                originalStoredHash = first.planSha256,
                firstReadContentHash = PsoPlanValidation.ComputeContentHash(first),
                secondReadContentHash = PsoPlanValidation.ComputeContentHash(second),
                originalHashSurvivedRead = first.planSha256 == PsoPlanValidation.ComputeContentHash(first)
            });
        }
    }
}
