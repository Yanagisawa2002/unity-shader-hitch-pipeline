using System;
using System.IO;

namespace Yanagisawa.ShaderHitchPipeline
{
    public static class PsoRelativePath
    {
        public static string Resolve(string root, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("A root directory is required.", nameof(root));
            if (string.IsNullOrWhiteSpace(relativePath)) throw new ArgumentException("A relative path is required.", nameof(relativePath));
            // Serialized paths are portable. Reject Windows drives/ADS and traversal on every OS.
            string portable = relativePath.Replace('\\', '/');
            if (portable.StartsWith("/", StringComparison.Ordinal) || portable.IndexOf(':') >= 0 || Path.IsPathRooted(relativePath))
                throw new InvalidOperationException("Expected a relative path: " + relativePath);
            string fullRoot = Path.GetFullPath(root);
            string candidate = Path.GetFullPath(Path.Combine(fullRoot, portable.Replace('/', Path.DirectorySeparatorChar)));
            StringComparison comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            string prefix = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(prefix, comparison) || string.Equals(candidate.TrimEnd(Path.DirectorySeparatorChar), fullRoot.TrimEnd(Path.DirectorySeparatorChar), comparison))
                throw new InvalidOperationException("Path escapes its declared root: " + relativePath);
            return candidate;
        }
    }
}
