using System;
using System.IO;
using UnityEngine;

namespace Yanagisawa.ShaderHitchPipeline
{
    [Serializable]
    public sealed class PsoBuildIdentityDocument
    {
        public int version;
        public string unityVersion;
        public string buildTarget;
        public string buildOptions;
        public string capturedUtc;
        public PsoContentIdentity identity;
        public string identitySha256;
    }

    public static class PsoUnityBuildIdentity
    {
        public const string ResourceName = "PsoBuildIdentity";
#if UNITY_EDITOR
        public static Func<PsoContentIdentity> EditorCapture;
#endif
        public static PsoContentIdentity Capture()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying && EditorCapture != null) return EditorCapture();
#endif
            TextAsset asset = Resources.Load<TextAsset>(ResourceName);
            if (asset == null) return Unavailable();
            try { return Parse(asset.text).identity; }
            catch (Exception exception)
            {
                Debug.LogWarning("[ShaderHitchPipeline] Build identity unavailable: " + exception.Message);
                return Unavailable();
            }
        }

        private static PsoContentIdentity Unavailable() => new PsoContentIdentity { version = 1, source = "unavailable" };

        public static PsoBuildIdentityDocument Parse(string json)
        {
            PsoBuildIdentityDocument document = JsonUtility.FromJson<PsoBuildIdentityDocument>(json);
            if (document == null || document.version != 1 || document.unityVersion != Application.unityVersion)
                throw new InvalidDataException("Build identity version/Unity mismatch.");
            var issues = PsoCompatibility.ValidateIdentity(document.identity);
            if (issues.Count != 0) throw new InvalidDataException(string.Join("\n", issues));
            string hash = PsoFileUtility.ComputeTextSha256(JsonUtility.ToJson(document.identity));
            if (!string.Equals(hash, document.identitySha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Embedded build identity checksum mismatch.");
            return document;
        }
    }
}
