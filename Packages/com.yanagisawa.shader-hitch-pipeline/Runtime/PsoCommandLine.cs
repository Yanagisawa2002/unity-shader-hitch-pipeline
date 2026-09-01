using System;
using System.Collections.Generic;
using System.Globalization;

namespace Yanagisawa.ShaderHitchPipeline
{
    public sealed class PsoCommandLine
    {
        private readonly Dictionary<string, string> values =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> flags =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static PsoCommandLine Current => Parse(Environment.GetCommandLineArgs());

        public static PsoCommandLine Parse(IEnumerable<string> arguments)
        {
            var parsed = new PsoCommandLine();
            if (arguments == null)
                return parsed;

            string[] args = arguments as string[] ?? new List<string>(arguments).ToArray();
            for (int index = 0; index < args.Length; index++)
            {
                string token = args[index];
                if (string.IsNullOrWhiteSpace(token) || token[0] != '-')
                    continue;

                int equals = token.IndexOf('=');
                if (equals > 0)
                {
                    string key = token.Substring(0, equals);
                    parsed.values[key] = token.Substring(equals + 1);
                    parsed.flags.Add(key);
                    continue;
                }

                parsed.flags.Add(token);
                if (index + 1 >= args.Length)
                    continue;

                string next = args[index + 1];
                if (!LooksLikeOption(next) || IsNumeric(next))
                {
                    parsed.values[token] = next;
                    index++;
                }
            }

            return parsed;
        }

        public bool HasFlag(string name) => flags.Contains(name);

        public string GetString(string name, string fallback = "")
        {
            return values.TryGetValue(name, out string value) ? value : fallback;
        }

        public int GetInt(string name, int fallback, int minimum, int maximum)
        {
            if (!values.TryGetValue(name, out string text) ||
                !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                return fallback;
            }

            return Math.Max(minimum, Math.Min(maximum, value));
        }

        public double GetDouble(
            string name,
            double fallback,
            double minimum,
            double maximum)
        {
            if (!values.TryGetValue(name, out string text) ||
                !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                return fallback;
            }

            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private static bool LooksLikeOption(string value)
        {
            return !string.IsNullOrEmpty(value) && value[0] == '-';
        }

        private static bool IsNumeric(string value)
        {
            return double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out _);
        }
    }
}
