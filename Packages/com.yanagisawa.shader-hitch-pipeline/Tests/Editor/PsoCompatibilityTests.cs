using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Yanagisawa.ShaderHitchPipeline.Editor;

namespace Yanagisawa.ShaderHitchPipeline.Tests
{
    public sealed class PsoCompatibilityTests
    {
        [Serializable]
        private sealed class HistoricalV3Plan
        {
            public int schemaVersion = 3;
            public string packageVersion = "0.3.0";
            public string profileId = "legacy";
            public string generatedUtc = "2026-09-01";
            public string runtimePlatform = "WindowsPlayer";
            public string graphicsDeviceType = "Direct3D12";
            public string qualityLevelName = "High";
            public string adapterId = "unity.graphics-state-collection";
            public string adapterVersion = "1";
            public int sourceSessionCount = 1;
            public string[] sourceSessionHashes = { "historical" };
            public PsoWarmupPhasePlan[] phases = { new PsoWarmupPhasePlan { phase = "startup", collectionFile = "startup.graphicsstate" } };
            public string planSha256 = "";
        }

        [Test]
        public void LegacyV3HashIsByteCompatibleAndDoesNotInventIdentity()
        {
            string original = JsonUtility.ToJson(new HistoricalV3Plan(), true);
            var plan = PsoDocumentJson.Parse<PsoWarmupPlanDocument>(original);
            Assert.IsNull(plan.compatibility);
            Assert.AreEqual(PsoFileUtility.ComputeTextSha256(original), PsoPlanValidation.ComputeContentHash(plan));
            Assert.IsTrue(PsoCompatibility.Evaluate(plan.compatibility, null).legacy);
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
            try
            {
                PsoFileUtility.WriteJsonAtomic(path, plan);
                Assert.AreEqual(original, File.ReadAllText(path));
                Assert.IsNull(PsoFileUtility.ReadJson<PsoWarmupPlanDocument>(path).compatibility);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Test]
        public void VersionedPlanHashCoversIdentityAndRestoresHashField()
        {
            var plan = new PsoWarmupPlanDocument { planSha256 = "preserved", compatibility = new PsoCompatibilityContract { version = 1 } };
            string first = PsoPlanValidation.ComputeContentHash(plan);
            Assert.AreEqual("preserved", plan.planSha256);
            plan.compatibility.version = 2;
            Assert.AreNotEqual(first, PsoPlanValidation.ComputeContentHash(plan));
        }

        [Test]
        public void EmbeddedIdentityDetectsCorruptionMissingAndUnknown()
        {
            PsoContentIdentity identity = PsoBuildIdentityCapture.Capture(EditorUserBuildSettings.activeBuildTarget);
            var doc = new PsoBuildIdentityDocument { version = 1, unityVersion = Application.unityVersion, identity = identity,
                identitySha256 = PsoFileUtility.ComputeTextSha256(JsonUtility.ToJson(identity)) };
            Assert.AreEqual(identity.buildInputSha256, PsoUnityBuildIdentity.Parse(JsonUtility.ToJson(doc)).identity.buildInputSha256);
            doc.identity.contentRevision = "tampered";
            Assert.Throws<InvalidDataException>(() => PsoUnityBuildIdentity.Parse(JsonUtility.ToJson(doc)));
            Assert.Throws<InvalidDataException>(() => PsoUnityBuildIdentity.Parse("{}"));
            doc.version = 2;
            Assert.Throws<InvalidDataException>(() => PsoUnityBuildIdentity.Parse(JsonUtility.ToJson(doc)));
        }

        [Test]
        public void MissingAndCorruptCostFilesLeavePriorsUnmodified()
        {
            string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
            var plan = new PsoWarmupPlanDocument { phases = new[] { new PsoWarmupPhasePlan { estimatedMillisecondsPerState = 7 } } };
            try
            {
                Assert.IsFalse(PsoCostCacheStorage.TryApply(file, plan, out string[] reasons));
                Assert.IsNotEmpty(reasons);
                File.WriteAllText(file, "{corrupt");
                Assert.IsFalse(PsoCostCacheStorage.TryApply(file, plan, out reasons));
                Assert.AreEqual(7, plan.phases[0].estimatedMillisecondsPerState);
            }
            finally { if (File.Exists(file)) File.Delete(file); }
        }

        [Test]
        public void MissingLegacyAndExplicitCorruptContractsCannotBeConfused()
        {
            Assert.IsNull(PsoDocumentJson.Parse<PsoWarmupPlanDocument>("{\"profileId\":\"old\"}").compatibility);
            var corrupt = PsoDocumentJson.Parse<PsoWarmupPlanDocument>("{\"profileId\":\"new\",\"compatibility\":{}}");
            Assert.IsNotNull(corrupt.compatibility);
            Assert.IsFalse(PsoCompatibility.Evaluate(corrupt.compatibility, null).legacy);
            Assert.IsFalse(PsoCompatibility.Evaluate(corrupt.compatibility, null).collectionCompatible);
            Assert.Throws<InvalidDataException>(() => PsoDocumentJson.Parse<PsoWarmupPlanDocument>("{\"compatibility\":null,\"compatibility\":{}}"));
        }

        [Test]
        public void MissingLegacyTraceIdentityAndExplicitUnknownIdentityStayDistinct()
        {
            var legacy = PsoDocumentJson.Parse<PsoSessionManifest>("{\"environment\":{\"engineName\":\"Unity\"}}");
            Assert.IsNull(legacy.environment.identity);
            var unknown = PsoDocumentJson.Parse<PsoSessionManifest>("{\"environment\":{\"identity\":{}}}");
            Assert.IsNotNull(unknown.environment.identity);
            Assert.IsNotEmpty(PsoCompatibility.ValidateIdentity(unknown.environment.identity));
        }

        [Test]
        public void StrictSerializationPreservesNullCostAndHashRoundtrip()
        {
            var plan = new PsoWarmupPlanDocument { compatibility = new PsoCompatibilityContract
                { version = 1, collectionEnvironment = PsoUnityEnvironment.Capture(), costEnvironment = null } };
            plan.planSha256 = PsoPlanValidation.ComputeContentHash(plan);
            string json = PsoDocumentJson.Serialize(plan);
            var roundtrip = PsoDocumentJson.Parse<PsoWarmupPlanDocument>(json);
            Assert.IsNull(roundtrip.compatibility.costEnvironment);
            Assert.AreEqual(plan.planSha256, PsoPlanValidation.ComputeContentHash(roundtrip));
        }

        [Test]
        public void BuildOptionsAreActualInputs()
        {
            var release = PsoBuildIdentityCapture.Capture(BuildTarget.StandaloneWindows64, BuildOptions.None);
            var development = PsoBuildIdentityCapture.Capture(BuildTarget.StandaloneWindows64, BuildOptions.Development);
            Assert.AreNotEqual(release.buildInputSha256, development.buildInputSha256);
            Assert.AreEqual(release.contentSha256, development.contentSha256);
            var firstScene = PsoBuildIdentityCapture.Capture(BuildTarget.StandaloneWindows64, BuildOptions.None, new[] { "Assets/First.unity" });
            var otherScene = PsoBuildIdentityCapture.Capture(BuildTarget.StandaloneWindows64, BuildOptions.None, new[] { "Assets/Other.unity" });
            Assert.AreNotEqual(firstScene.buildInputSha256, otherScene.buildInputSha256);
            var define = PsoBuildIdentityCapture.Capture(BuildTarget.StandaloneWindows64, BuildOptions.None, new[] { "Assets/First.unity" }, new[] { "EXTRA_BUILD_DEFINE" });
            Assert.AreNotEqual(firstScene.buildInputSha256, define.buildInputSha256);
        }

        [Test]
        public void BuildCaptureChangesForActualContentAndShaderAndIgnoresGeneratedIdentity()
        {
            const string folder = "Assets/PsoCompatibilityTestFixture";
            Assert.IsFalse(AssetDatabase.IsValidFolder(folder));
            string originalResource = File.Exists(PsoBuildIdentityCapture.ResourcePath) ? File.ReadAllText(PsoBuildIdentityCapture.ResourcePath) : null;
            try
            {
                Directory.CreateDirectory(folder);
                string content = folder + "/revision.txt";
                string shader = folder + "/identity.shader";
                File.WriteAllText(content, "revision1");
                File.WriteAllText(shader, "Shader \"Tests/PsoIdentity\" { SubShader { Pass { Color (1,0,0,1) } } }");
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                var first = PsoBuildIdentityCapture.Capture(EditorUserBuildSettings.activeBuildTarget);
                File.WriteAllText(content, "revision2");
                AssetDatabase.ImportAsset(content, ImportAssetOptions.ForceSynchronousImport);
                var second = PsoBuildIdentityCapture.Capture(EditorUserBuildSettings.activeBuildTarget);
                Assert.AreNotEqual(first.contentSha256, second.contentSha256);
                Assert.AreNotEqual(first.buildInputSha256, second.buildInputSha256);
                File.WriteAllText(shader, "Shader \"Tests/PsoIdentity\" { SubShader { Pass { Color (0,1,0,1) } } }");
                AssetDatabase.ImportAsset(shader, ImportAssetOptions.ForceSynchronousImport);
                var third = PsoBuildIdentityCapture.Capture(EditorUserBuildSettings.activeBuildTarget);
                Assert.AreNotEqual(second.shaderSha256, third.shaderSha256);
                Directory.CreateDirectory(Path.GetDirectoryName(PsoBuildIdentityCapture.ResourcePath));
                File.WriteAllText(PsoBuildIdentityCapture.ResourcePath, "{}");
                AssetDatabase.ImportAsset(PsoBuildIdentityCapture.ResourcePath, ImportAssetOptions.ForceSynchronousImport);
                var fourth = PsoBuildIdentityCapture.Capture(EditorUserBuildSettings.activeBuildTarget);
                Assert.AreEqual(third.buildInputSha256, fourth.buildInputSha256, "Generated identity must not self-invalidate.");
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
                if (originalResource == null) AssetDatabase.DeleteAsset(PsoBuildIdentityCapture.ResourcePath);
                else { File.WriteAllText(PsoBuildIdentityCapture.ResourcePath, originalResource); AssetDatabase.ImportAsset(PsoBuildIdentityCapture.ResourcePath); }
            }
        }
    }
}
