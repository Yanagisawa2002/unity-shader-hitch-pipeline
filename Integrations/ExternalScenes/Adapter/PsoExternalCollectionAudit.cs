#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Yanagisawa.ShaderHitchPipeline;
#if UNITY_6000_5_OR_NEWER
using GraphicsStateCollection = UnityEngine.Rendering.GraphicsStateCollection;
#else
using GraphicsStateCollection = UnityEngine.Experimental.Rendering.GraphicsStateCollection;
#endif

// Offline inspection of real Unity API payloads, not a shader filter or a
// replacement workload. Only plain receipt JSON is written; no assets change.
public static class PsoExternalCollectionAudit
{
    [Serializable] sealed class Variant
    {
        public string shader, shaderPath, pass, keywords;
        public string[] states;
    }
    [Serializable] sealed class Collection
    {
        public string file, sha256;
        public int variantCount, graphicsStateCount;
        public Variant[] variants;
    }
    [Serializable] sealed class Document { public Collection[] collections; }

    public static void Dump()
    {
        string input = PsoCommandLine.Current.GetString("-pso-audit-input", "");
        string output = PsoCommandLine.Current.GetString("-pso-audit-output", "");
        if (!Directory.Exists(input) || string.IsNullOrEmpty(output) || File.Exists(output))
            throw new InvalidOperationException("Require an existing collection directory and new audit file.");
        var retained = new List<UnityEngine.Object>();
        foreach (string guid in AssetDatabase.FindAssets("t:Shader t:Material"))
            retained.Add(AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid)));
        var result = new List<Collection>();
        foreach (string path in Directory.GetFiles(input, "*.graphicsstate", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
        {
            var collection = new GraphicsStateCollection();
            try
            {
                if (!collection.LoadFromFile(path)) throw new InvalidDataException("Could not load " + path);
                var variants = new List<GraphicsStateCollection.ShaderVariant>();
                collection.GetVariants(variants);
                var records = new List<Variant>();
                foreach (var variant in variants)
                {
                    var states = new List<GraphicsStateCollection.GraphicsState>();
                    collection.GetGraphicsStatesForVariant(variant, states);
                    records.Add(new Variant { shader = variant.shader == null ? "<unresolved>" : variant.shader.name,
                        shaderPath = AssetDatabase.GetAssetPath(variant.shader), pass = PublicValue(variant.passId),
                        keywords = string.Join(" ", variant.keywords.Select(k => k.name).OrderBy(k => k, StringComparer.Ordinal)),
                        states = states.Select(s => PublicValue(s)).ToArray() });
                }
                result.Add(new Collection { file = path, sha256 = PsoFileUtility.ComputeSha256(path),
                    variantCount = collection.variantCount, graphicsStateCount = collection.totalGraphicsStateCount, variants = records.ToArray() });
            }
            finally { UnityEngine.Object.DestroyImmediate(collection); }
        }
        using (var writer = new StreamWriter(new FileStream(output, FileMode.CreateNew)))
            writer.Write(JsonUtility.ToJson(new Document { collections = result.ToArray() }, true));
        GC.KeepAlive(retained);
        Debug.Log("[PSO External] Audited " + result.Count + " native collections.");
    }

    [Serializable] sealed class Quoted { public string text; }
    static string Quote(string value)
    {
        string json = JsonUtility.ToJson(new Quoted { text = value });
        return json.Substring(8, json.Length - 9);
    }
    // JsonUtility silently drops Unity's non-Serializable GraphicsState struct.
    // Inspect its public API values in this offline Editor diagnostic, including
    // nested render-state properties; never reflect application actions/inputs.
    static string PublicValue(object value, int depth = 0)
    {
        if (depth > 16) throw new InvalidDataException("Unexpected recursive native payload.");
        if (value == null) return "null";
        Type type = value.GetType();
        if (value is UnityEngine.Rendering.RenderTargetIdentifier) return Quote(value.ToString());
        if (type == typeof(string) || type.IsEnum) return Quote(value.ToString());
        if (type == typeof(bool)) return (bool)value ? "true" : "false";
        if (type.IsPrimitive) return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        if (value is System.Collections.IEnumerable sequence)
        {
            var entries = new List<string>();
            foreach (var item in sequence) entries.Add(PublicValue(item, depth + 1));
            return "[" + string.Join(",", entries) + "]";
        }
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public;
        var indexer = type.GetProperties(flags).FirstOrDefault(p => p.CanRead && p.GetIndexParameters().Length == 1 &&
            p.GetIndexParameters()[0].ParameterType == typeof(int));
        var lengthProperty = type.GetProperty("Length", flags);
        if (indexer != null && lengthProperty != null && lengthProperty.PropertyType == typeof(int))
        {
            int count = (int)lengthProperty.GetValue(value);
            if (count < 0 || count > 4096) throw new InvalidDataException("Unexpected native array length.");
            return "[" + string.Join(",", Enumerable.Range(0, count).Select(i => PublicValue(indexer.GetValue(value, new object[] { i }), depth + 1))) + "]";
        }
        var members = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var fields = type.GetFields(flags);
        foreach (var field in fields) members.Add(field.Name, PublicValue(field.GetValue(value), depth + 1));
        if (fields.Length == 0)
            foreach (var property in type.GetProperties(flags))
                if (property.CanRead && property.GetIndexParameters().Length == 0)
                    members.Add(property.Name, PublicValue(property.GetValue(value), depth + 1));
        if (members.Count == 0) throw new InvalidDataException("Unsupported native payload: " + type);
        return "{" + string.Join(",", members.Select(item => Quote(item.Key) + ":" + item.Value)) + "}";
    }
}
#endif
