using System;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace Yanagisawa.ShaderHitchPipeline
{
    public static class PsoDocumentJson
    {
        public static T Parse<T>(string json) where T : class
        {
            var fields = new PsoJsonObject(json);
            T document = ReadExact<T>(json);
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
                return JsonUtility.ToJson(ReadExact<PsoLegacyPlanHashDocument>(JsonUtility.ToJson(legacy)), pretty);
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
            var environment = ReadExact<PsoEnvironmentSnapshot>(json);
            if (Absent(fields.Get("identity"))) environment.identity = null;
            return environment;
        }
        private static string EnvironmentJson(PsoEnvironmentSnapshot environment, bool pretty)
        {
            if (environment == null) return "null";
            string json = JsonUtility.ToJson(environment, pretty);
            return environment.identity == null ? new PsoJsonObject(json).Replace("identity", "null") : json;
        }

        // Unity 6000.5's native reader can round a double by one ULP (for example
        // 0.10332000000000001 becomes 0.10332). Recover scalar values from their
        // original JSON tokens, preserving the existing writer/hash byte contract.
        private static T ReadExact<T>(string json)
        {
            T value = JsonUtility.FromJson<T>(json);
            RestoreNumbers(value, json);
            return value;
        }

        private static void RestoreNumbers(object value, string json)
        {
            if (value == null || json == null || json == "null") return;
            var fields = new PsoJsonObject(json);
            foreach (FieldInfo field in value.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                string token = fields.Get(field.Name);
                if (token == null || token == "null") continue;
                if (field.FieldType == typeof(double))
                    field.SetValue(value, double.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture));
                else if (field.FieldType == typeof(float))
                    field.SetValue(value, float.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture));
                else if (field.FieldType.IsArray && field.GetValue(value) is Array array)
                {
                    string[] tokens = PsoJsonObject.ArrayValues(token);
                    Type element = field.FieldType.GetElementType();
                    for (int i = 0; i < array.Length; i++)
                    {
                        if (element == typeof(double)) array.SetValue(double.Parse(tokens[i], NumberStyles.Float, CultureInfo.InvariantCulture), i);
                        else if (element == typeof(float)) array.SetValue(float.Parse(tokens[i], NumberStyles.Float, CultureInfo.InvariantCulture), i);
                        else if (!element.IsPrimitive && element != typeof(string)) RestoreNumbers(array.GetValue(i), tokens[i]);
                    }
                }
                else if (!field.FieldType.IsPrimitive && !field.FieldType.IsEnum && field.FieldType != typeof(string))
                    RestoreNumbers(field.GetValue(value), token);
            }
        }
    }
}
