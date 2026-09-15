using System;

namespace Yanagisawa.ShaderHitchPipeline
{
    /// <summary>Known Unity tool outputs, not runtime scene/shader inputs. Custom paths fail closed.</summary>
    public static class PsoBuildGeneratedMetadata
    {
        public static string PerformanceSettingsFromArguments(string[] arguments)
        {
            for (int i = 0; arguments != null && i + 1 < arguments.Length; i++)
                if (arguments[i] == "-performance-measurement-count") return PerformanceSettingsIdentity(arguments[i + 1]);
            return PerformanceSettingsIdentity(null);
        }
        // Mirrors the official performance package's effective RunSettings
        // constructor value, including the -1 default for absent/invalid input.
        public static string PerformanceSettingsIdentity(string measurementCountArgument)
        {
            int count = int.TryParse(measurementCountArgument, out int parsed) ? parsed : -1;
            return "performanceTesting.MeasurementCount=" + count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        // Addressables deletes/recreates this file and its meta between builds.
        // The XML controls IL2CPP stripping: hash its bytes, never exclude it.
        public static bool RequiresByteIdentity(string path, bool hasAddressables) =>
            hasAddressables && path == "Assets/AddressableAssetsData/link.xml";

        public static bool IsGenerated(string path, bool hasAddressables, bool hasPerformanceTesting)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (hasPerformanceTesting && (path == "Assets/Resources/PerformanceTestRunInfo.json" ||
                path == "Assets/Resources/PerformanceTestRunSettings.json")) return true;
            const string prefix = "Assets/AddressableAssetsData/";
            const string suffix = "/addressables_content_state.bin";
            if (!hasAddressables || path.Length <= prefix.Length + suffix.Length || !path.StartsWith(prefix, StringComparison.Ordinal) ||
                !path.EndsWith(suffix, StringComparison.Ordinal)) return false;
            string platform = path.Substring(prefix.Length, path.Length - prefix.Length - suffix.Length);
            return platform.Length > 0 && platform.IndexOfAny(new[] { '/', '\\', '.' }) < 0;
        }
    }
}
