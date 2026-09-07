using UnityEngine;

namespace Yanagisawa.ShaderHitchPipeline
{
    public static class PsoDocumentJson
    {
        public static T Parse<T>(string json) where T : class
        {
            var fields = new PsoJsonObject(json);
            T document = JsonUtility.FromJson<T>(json);
            if (document is PsoWarmupPlanDocument plan)
            {
                string contractJson = fields.Get("compatibility");
                if (Absent(contractJson)) plan.compatibility = null;
                else
                {
                    var contract = new PsoJsonObject(contractJson);
                    plan.compatibility.collectionEnvironment = ReadEnvironment(contract.Get("collectionEnvironment"));
                    plan.compatibility.costEnvironment = ReadEnvironment(contract.Get("costEnvironment"));
                }
            }
            else if (document is PsoSessionManifest session) session.environment = ReadEnvironment(fields.Get("environment"));
            else if (document is PsoCostCacheDocument cache) cache.environment = ReadEnvironment(fields.Get("environment"));
            else if (document is PsoWarmupRunReceipt warmup) warmup.environment = ReadEnvironment(fields.Get("environment"));
            else if (document is PsoBenchmarkReceipt benchmark) benchmark.environment = ReadEnvironment(fields.Get("environment"));
            return document;
        }

        public static string Serialize(object document, bool pretty = true)
        {
            if (document is PsoWarmupPlanDocument legacy && legacy.compatibility == null)
                return JsonUtility.ToJson(JsonUtility.FromJson<PsoLegacyPlanHashDocument>(JsonUtility.ToJson(legacy)), pretty);
            string json = JsonUtility.ToJson(document, pretty);
            if (document is PsoWarmupPlanDocument plan)
            {
                var fields = new PsoJsonObject(json);
                string contract = fields.Get("compatibility");
                contract = new PsoJsonObject(contract).Replace("collectionEnvironment", EnvironmentJson(plan.compatibility.collectionEnvironment, pretty));
                contract = new PsoJsonObject(contract).Replace("costEnvironment", EnvironmentJson(plan.compatibility.costEnvironment, pretty));
                return fields.Replace("compatibility", contract);
            }
            PsoEnvironmentSnapshot environment = null;
            bool hasEnvironment = true;
            if (document is PsoSessionManifest session) environment = session.environment;
            else if (document is PsoCostCacheDocument cache) environment = cache.environment;
            else if (document is PsoWarmupRunReceipt warmup) environment = warmup.environment;
            else if (document is PsoBenchmarkReceipt benchmark) environment = benchmark.environment;
            else hasEnvironment = false;
            return hasEnvironment ? new PsoJsonObject(json).Replace("environment", EnvironmentJson(environment, pretty)) : json;
        }

        private static bool Absent(string json) => json == null || json == "null";
        private static PsoEnvironmentSnapshot ReadEnvironment(string json)
        {
            if (Absent(json)) return null;
            var fields = new PsoJsonObject(json);
            var environment = JsonUtility.FromJson<PsoEnvironmentSnapshot>(json);
            if (Absent(fields.Get("identity"))) environment.identity = null;
            return environment;
        }
        private static string EnvironmentJson(PsoEnvironmentSnapshot environment, bool pretty)
        {
            if (environment == null) return "null";
            string json = JsonUtility.ToJson(environment, pretty);
            return environment.identity == null ? new PsoJsonObject(json).Replace("identity", "null") : json;
        }
    }
}
