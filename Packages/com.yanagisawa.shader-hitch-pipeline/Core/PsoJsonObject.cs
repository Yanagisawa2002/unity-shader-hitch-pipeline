using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Yanagisawa.ShaderHitchPipeline
{
    // Presence scanner used alongside the adapter's JSON deserializer. Unity's inline
    // deserializer creates empty nested objects for missing fields; version zero cannot
    // distinguish that from an explicitly corrupt new contract. Never use a regex here.
    public sealed class PsoJsonObject
    {
        private readonly string json;
        private readonly Dictionary<string, int[]> members = new Dictionary<string, int[]>(StringComparer.Ordinal);
        public PsoJsonObject(string json)
        {
            this.json = json ?? throw new ArgumentNullException(nameof(json));
            int at = 0;
            Space(ref at);
            Expect(ref at, '{');
            Space(ref at);
            if (at < json.Length && json[at] == '}') at++;
            else while (true)
            {
                Space(ref at);
                string key = String(ref at);
                Space(ref at); Expect(ref at, ':'); Space(ref at);
                int start = at;
                Value(ref at, 0);
                if (members.ContainsKey(key)) throw new InvalidDataException("Duplicate JSON member: " + key);
                members.Add(key, new[] { start, at });
                Space(ref at);
                if (at < json.Length && json[at] == '}') { at++; break; }
                Expect(ref at, ',');
            }
            Space(ref at);
            if (at != json.Length) throw new InvalidDataException("Trailing JSON data.");
        }
        public string Get(string name) => members.TryGetValue(name, out int[] range) ? json.Substring(range[0], range[1] - range[0]) : null;
        public string Replace(string name, string rawValue)
        {
            if (!members.TryGetValue(name, out int[] range)) throw new InvalidDataException("Missing JSON member: " + name);
            return json.Substring(0, range[0]) + rawValue + json.Substring(range[1]);
        }
        private void Space(ref int at) { while (at < json.Length && char.IsWhiteSpace(json[at])) at++; }
        private void Expect(ref int at, char value)
        { if (at >= json.Length || json[at++] != value) throw new InvalidDataException("Malformed JSON object."); }
        private string String(ref int at)
        {
            Expect(ref at, '"');
            var value = new StringBuilder();
            while (at < json.Length)
            {
                char c = json[at++];
                if (c == '"') return value.ToString();
                if (c < 32) throw new InvalidDataException("Control character in JSON string.");
                if (c != '\\') { value.Append(c); continue; }
                if (at >= json.Length) break;
                c = json[at++];
                switch (c)
                {
                    case '"': case '\\': case '/': value.Append(c); break;
                    case 'b': value.Append('\b'); break;
                    case 'f': value.Append('\f'); break;
                    case 'n': value.Append('\n'); break;
                    case 'r': value.Append('\r'); break;
                    case 't': value.Append('\t'); break;
                    case 'u':
                        if (at + 4 > json.Length) throw new InvalidDataException("Incomplete JSON unicode escape.");
                        try { value.Append((char)Convert.ToInt32(json.Substring(at, 4), 16)); }
                        catch (Exception) { throw new InvalidDataException("Invalid JSON unicode escape."); }
                        at += 4; break;
                    default: throw new InvalidDataException("Invalid JSON escape.");
                }
            }
            throw new InvalidDataException("Unterminated JSON string.");
        }
        private void Value(ref int at, int depth)
        {
            if (depth > 64 || at >= json.Length) throw new InvalidDataException("Invalid JSON depth/value.");
            char c = json[at];
            if (c == '"') { String(ref at); return; }
            if (c == '{' || c == '[')
            {
                bool obj = c == '{'; char close = obj ? '}' : ']'; at++; Space(ref at);
                var keys = new HashSet<string>(StringComparer.Ordinal);
                if (at < json.Length && json[at] == close) { at++; return; }
                while (true)
                {
                    Space(ref at);
                    if (obj)
                    {
                        if (!keys.Add(String(ref at))) throw new InvalidDataException("Duplicate nested JSON member.");
                        Space(ref at); Expect(ref at, ':'); Space(ref at);
                    }
                    Value(ref at, depth + 1); Space(ref at);
                    if (at < json.Length && json[at] == close) { at++; return; }
                    Expect(ref at, ',');
                }
            }
            int start = at;
            while (at < json.Length && !char.IsWhiteSpace(json[at]) && json[at] != ',' && json[at] != '}' && json[at] != ']') at++;
            if (start == at) throw new InvalidDataException("Missing JSON value.");
            // Scalar syntax and field types are checked by the actual deserializer/schema.
        }
    }
}
