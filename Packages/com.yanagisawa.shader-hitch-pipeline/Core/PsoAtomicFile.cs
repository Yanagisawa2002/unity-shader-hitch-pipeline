using System;
using System.IO;
using System.Text;

namespace Yanagisawa.ShaderHitchPipeline
{
    public static class PsoAtomicFile
    {
        /// <summary>Publish a complete sibling file with one rename/replace. Replacement failure
        /// preserves the previous plan or receipt. Never delete the only good copy first.</summary>
        public static void WriteText(string path, string contents)
        {
            string full = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(full);
            Directory.CreateDirectory(directory);
            string temporary = Path.Combine(directory, ".pso-" + Guid.NewGuid().ToString("N").Substring(0, 12) + ".tmp");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] bytes = new UTF8Encoding(false).GetBytes(contents ?? string.Empty);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(full)) File.Replace(temporary, full, null);
                else File.Move(temporary, full);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
