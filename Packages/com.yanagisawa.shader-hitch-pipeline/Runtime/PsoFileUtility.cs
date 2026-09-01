using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Yanagisawa.ShaderHitchPipeline
{
    public static class PsoFileUtility
    {
        public static string UtcNowText()
        {
            return DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        }

        public static string CreateRunId(string prefix)
        {
            return SanitizeFileName(prefix) + "-" +
                   DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) +
                   "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        public static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "unnamed";

            var builder = new StringBuilder(value.Length);
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                bool rejected = Array.IndexOf(invalid, character) >= 0 ||
                                char.IsWhiteSpace(character);
                builder.Append(rejected ? '-' : char.ToLowerInvariant(character));
            }

            string result = builder.ToString().Trim('-', '.');
            return string.IsNullOrEmpty(result) ? "unnamed" : result;
        }

        public static string DefaultRuntimeOutputRoot()
        {
            return Path.Combine(
                Application.persistentDataPath,
                PsoConstants.DefaultRuntimeDirectoryName);
        }

        public static string ResolveChildPath(string root, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(root))
                throw new ArgumentException("A root directory is required.", nameof(root));
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new ArgumentException("A relative path is required.", nameof(relativePath));
            if (Path.IsPathRooted(relativePath))
                throw new InvalidOperationException("Expected a relative path: " + relativePath);

            string fullRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string candidate = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
            string requiredPrefix = fullRoot + Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Path escapes its declared root: " + relativePath);

            return candidate;
        }

        public static string ComputeSha256(string filePath)
        {
            using (FileStream stream = File.OpenRead(filePath))
            using (SHA256 sha = SHA256.Create())
                return ToHex(sha.ComputeHash(stream));
        }

        public static string ComputeTextSha256(string value)
        {
            using (SHA256 sha = SHA256.Create())
                return ToHex(sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty)));
        }

        public static void WriteJsonAtomic(string path, object document, bool pretty = true)
        {
            WriteTextAtomic(path, JsonUtility.ToJson(document, pretty));
        }

        public static void WriteTextAtomic(string path, string contents)
        {
            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(directory))
                throw new InvalidOperationException("Could not resolve output directory for " + path);

            Directory.CreateDirectory(directory);
            string temporary = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(temporary, contents, new UTF8Encoding(false));
            if (File.Exists(fullPath))
                File.Delete(fullPath);
            File.Move(temporary, fullPath);
        }

        public static T ReadJson<T>(string path) where T : class
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("JSON file was not found.", path);
            T result = JsonUtility.FromJson<T>(File.ReadAllText(path));
            if (result == null)
                throw new InvalidDataException("Could not parse JSON document: " + path);
            return result;
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            for (int index = 0; index < bytes.Length; index++)
                builder.Append(bytes[index].ToString("x2"));
            return builder.ToString();
        }
    }
}
