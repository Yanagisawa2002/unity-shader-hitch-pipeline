using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Yanagisawa.ShaderHitchPipeline.Editor
{
    // Captures imported dependencies and build settings, not the plan's own integrity hash.
    [InitializeOnLoad]
    public sealed class PsoBuildIdentityCapture : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public const string ResourcePath = "Assets/Resources/PsoBuildIdentity.json";
        private static BuildPlayerOptions? pendingBuild;
        private static BuildPlayerOptions? activeBuild;
        public int callbackOrder => -2000;
        static PsoBuildIdentityCapture() { PsoUnityBuildIdentity.EditorCapture = () => Capture(EditorUserBuildSettings.activeBuildTarget); }

        // Call immediately before BuildPipeline.BuildPlayer with that exact options value.
        public static void DeclareBuildInputs(BuildPlayerOptions options)
        {
            if (options.scenes == null || options.scenes.Length == 0)
                throw new ArgumentException("Declare the actual nonempty BuildPlayerOptions.scenes list.");
            // Builders may have just changed PlayerSettings. Persist those actual
            // inputs before the preprocess callback hashes ProjectSettings bytes.
            AssetDatabase.SaveAssets();
            options.scenes = (string[])options.scenes.Clone();
            options.extraScriptingDefines = options.extraScriptingDefines == null ? Array.Empty<string>() : (string[])options.extraScriptingDefines.Clone();
            pendingBuild = options;
        }

        public void OnPostprocessBuild(BuildReport report) { activeBuild = null; }

        public static PsoContentIdentity CaptureCurrentBuild(BuildTarget target, BuildOptions options)
        {
            if (!activeBuild.HasValue || activeBuild.Value.target != target || activeBuild.Value.options != options)
                throw new InvalidDataException("Actual BuildPlayerOptions were not declared; call DeclareBuildInputs immediately before BuildPlayer.");
            BuildPlayerOptions build = activeBuild.Value;
            return Capture(target, options, build.scenes, build.extraScriptingDefines, build.subtarget, build.assetBundleManifestPath);
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            activeBuild = pendingBuild;
            pendingBuild = null;
            PsoContentIdentity identity;
            if (activeBuild.HasValue) identity = CaptureCurrentBuild(report.summary.platform, report.summary.options);
            else
            {
                identity = new PsoContentIdentity { version = 1, source = "unavailable-no-build-declaration" };
                Debug.LogWarning("[ShaderHitchPipeline] Undeclared BuildPlayerOptions: legacy build only; current-build identity unavailable.");
            }
            var document = new PsoBuildIdentityDocument
            {
                version = 1, unityVersion = Application.unityVersion,
                buildTarget = report.summary.platform.ToString(), buildOptions = report.summary.options.ToString(), capturedUtc = PsoFileUtility.UtcNowText(),
                identity = identity,
                identitySha256 = PsoFileUtility.ComputeTextSha256(JsonUtility.ToJson(identity))
            };
            Directory.CreateDirectory(Path.GetDirectoryName(ResourcePath));
            PsoFileUtility.WriteJsonAtomic(ResourcePath, document);
            AssetDatabase.ImportAsset(ResourcePath, ImportAssetOptions.ForceSynchronousImport);
        }

        public static PsoContentIdentity Capture(BuildTarget target, BuildOptions options = BuildOptions.None,
            string[] scenes = null, string[] extraScriptingDefines = null, int subtarget = 0, string assetBundleManifestPath = null)
        {
            string installed = PsoProjectConfiguration.Load().installedPlanDirectory.Replace('\\', '/').TrimEnd('/');
            var content = new StringBuilder();
            var shaders = new StringBuilder();
            var generatedMetadata = new StringBuilder();
            var packages = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
            bool hasAddressables = packages.Any(p => p.name == "com.unity.addressables");
            // Other versions retain ordinary content attestation until their
            // producer schema has been checked, rather than assuming its fields.
            bool hasPerformanceTesting = packages.Any(p => p.name == "com.unity.test-framework.performance" && p.version == "3.1.0");
            // Includes registry and local package imported assets, shader includes and dependencies.
            foreach (string path in AssetDatabase.GetAllAssetPaths().OrderBy(p => p, StringComparer.Ordinal))
            {
                if (!(path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith("Packages/", StringComparison.Ordinal)) ||
                    AssetDatabase.IsValidFolder(path) || IsGenerated(path, installed)) continue;
                string entry = path + "|" + AssetDatabase.AssetPathToGUID(path) + "|" +
                    AssetDatabase.GetAssetDependencyHash(path) + "\n";
                if (PsoBuildGeneratedMetadata.RequiresByteIdentity(path, hasAddressables))
                    entry = path + "|generated-guid-content-sha256|" + PsoFileUtility.ComputeSha256(path) + "\n";
                if (PsoBuildGeneratedMetadata.IsGenerated(path, hasAddressables, hasPerformanceTesting))
                {
                    // Content-update cache and performance-test run metadata are
                    // regenerated/deleted by Unity's build callbacks. Their GUIDs
                    // and dependency hashes are evidence, not runtime content.
                    generatedMetadata.Append(entry);
                    continue;
                }
                content.Append(entry);
                string extension = Path.GetExtension(path).ToLowerInvariant();
                if (extension == ".shader" || extension == ".compute" || extension == ".hlsl" ||
                    extension == ".cginc" || extension == ".shadergraph" || extension == ".shadersubgraph") shaders.Append(entry);
            }
            var settings = new StringBuilder();
            if (hasPerformanceTesting)
                settings.Append(PsoBuildGeneratedMetadata.PerformanceSettingsFromArguments(Environment.GetCommandLineArgs())).Append('\n');
            foreach (string path in Directory.GetFiles("ProjectSettings").OrderBy(p => p, StringComparer.Ordinal))
            {
                // Scheduler policy and Editor window state do not change shader/content inputs.
                if (Path.GetFileName(path) == "ShaderHitchPipeline.json" || Path.GetFileName(path) == "EditorUserSettings.asset") continue;
                settings.Append(Path.GetFileName(path)).Append('|').Append(PsoFileUtility.ComputeSha256(path)).Append('\n');
            }
            foreach (string path in new[] { "Packages/manifest.json", "Packages/packages-lock.json" })
                if (File.Exists(path)) settings.Append(path).Append('|').Append(PsoFileUtility.ComputeSha256(path)).Append('\n');
            settings.Append("target=").Append(target).Append("\nunity=").Append(Application.unityVersion);
            settings.Append("\nbuildOptions=").Append(options);
            settings.Append("\nsubtarget=").Append(subtarget);
            // Offline inspection defaults to EditorBuildSettings; actual builds always pass
            // the declared BuildPlayerOptions list through CaptureCurrentBuild.
            string[] actualScenes = scenes ?? EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            foreach (string scene in actualScenes)
                settings.Append("\nscene=").Append(scene.Length).Append(':').Append(scene);
            foreach (string define in extraScriptingDefines ?? Array.Empty<string>())
                settings.Append("\ndefine=").Append(define.Length).Append(':').Append(define);
            if (!string.IsNullOrWhiteSpace(assetBundleManifestPath))
                settings.Append("\nassetBundleManifest=").Append(PsoFileUtility.ComputeSha256(assetBundleManifestPath));
            string contentHash = PsoFileUtility.ComputeTextSha256(content.ToString());
            string shaderHash = PsoFileUtility.ComputeTextSha256(shaders.ToString());
            var identity = new PsoContentIdentity
            {
                version = 1, source = PsoCompatibility.BuildIdentitySource,
                buildInputSha256 = PsoFileUtility.ComputeTextSha256(settings + "\n" + contentHash + "\n" + shaderHash),
                shaderSha256 = shaderHash, contentSha256 = contentHash,
                contentId = "player-content", contentRevision = contentHash
            };
            if (PsoCommandLine.Current.HasFlag("-pso-build-input-evidence"))
            {
                // Optional immutable diagnostics outside Assets. Preserve the
                // actual hashed inputs so a failed gate can name the difference.
                string file = Path.GetFullPath("PsoArtifacts/BuildInputEvidence/" + identity.buildInputSha256 + ".json");
                if (!File.Exists(file)) PsoFileUtility.WriteJsonAtomic(file, new InputEvidence {
                    identity = identity, assets = content.ToString(), shaders = shaders.ToString(), settings = settings.ToString(),
                    generatedMetadataExcludedFromIdentity = generatedMetadata.ToString()
                });
            }
            return identity;
        }

        [Serializable] private sealed class InputEvidence
        {
            public PsoContentIdentity identity;
            public string assets, shaders, settings, generatedMetadataExcludedFromIdentity;
        }

        private static bool IsGenerated(string path, string installed) =>
            path == ResourcePath || path == ResourcePath + ".meta" ||
            path == installed || path.StartsWith(installed + "/", StringComparison.Ordinal) ||
            path.StartsWith(installed + ".staging-", StringComparison.Ordinal) ||
            path.StartsWith(installed + ".backup-", StringComparison.Ordinal);
    }
}
