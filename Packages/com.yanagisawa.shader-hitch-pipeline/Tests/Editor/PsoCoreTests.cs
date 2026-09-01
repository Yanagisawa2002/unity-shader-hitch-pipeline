using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace Yanagisawa.ShaderHitchPipeline.Tests
{
    public sealed class PsoCoreTests
    {
        private string temporaryRoot;

        [SetUp]
        public void SetUp()
        {
            temporaryRoot = Path.Combine(
                Path.GetTempPath(),
                "ShaderHitchPipelineTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryRoot);
        }

        [TearDown]
        public void TearDown()
        {
            string root = Path.GetFullPath(temporaryRoot);
            string temp = Path.GetFullPath(Path.GetTempPath());
            Assert.That(root.StartsWith(temp, StringComparison.OrdinalIgnoreCase));
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        [Test]
        public void CommandLine_ParsesFlagsValuesEqualsAndNegativeNumbers()
        {
            PsoCommandLine line = PsoCommandLine.Parse(new[]
            {
                "player.exe",
                "-flag",
                "-name", "phase one",
                "-count=17",
                "-negative", "-3.5",
            });

            Assert.That(line.HasFlag("-flag"), Is.True);
            Assert.That(line.GetString("-name"), Is.EqualTo("phase one"));
            Assert.That(line.GetInt("-count", 0, 0, 20), Is.EqualTo(17));
            Assert.That(line.GetDouble("-negative", 0, -10, 10), Is.EqualTo(-3.5));
        }

        [Test]
        public void Statistics_CalculatesInterpolatedPercentilesAndHitches()
        {
            PsoFrameStatistics result = PsoStatistics.Calculate(
                new[] { 10.0, 20.0, 30.0, 40.0, 50.0 },
                35.0);

            Assert.That(result.sampleCount, Is.EqualTo(5));
            Assert.That(result.meanMilliseconds, Is.EqualTo(30.0).Within(0.0001));
            Assert.That(result.p50Milliseconds, Is.EqualTo(30.0).Within(0.0001));
            Assert.That(result.p95Milliseconds, Is.EqualTo(48.0).Within(0.0001));
            Assert.That(result.maximumMilliseconds, Is.EqualTo(50.0));
            Assert.That(result.hitchFrameCount, Is.EqualTo(2));
            Assert.That(result.hitchFramePercent, Is.EqualTo(40.0).Within(0.0001));
        }

        [Test]
        public void AdaptiveBatchPolicy_ShrinksUnderPressureAndGrowsWithHeadroom()
        {
            var policy = new PsoAdaptiveBatchPolicy(16, 2, 64, 16.0);
            Assert.That(policy.Observe(30.0), Is.EqualTo(8));

            for (int index = 0; index < 20; index++)
                policy.Observe(1.0);

            Assert.That(policy.CurrentBatchSize, Is.GreaterThan(8));
            Assert.That(policy.CurrentBatchSize, Is.LessThanOrEqualTo(64));
        }

        [Test]
        public void ResolveChildPath_RejectsAbsoluteAndTraversalPaths()
        {
            Assert.Throws<InvalidOperationException>(() =>
                PsoFileUtility.ResolveChildPath(temporaryRoot, "../escape.graphicsstate"));
            Assert.Throws<InvalidOperationException>(() =>
                PsoFileUtility.ResolveChildPath(temporaryRoot, Path.GetFullPath("absolute.bin")));

            string valid = PsoFileUtility.ResolveChildPath(
                temporaryRoot,
                "collections/startup.graphicsstate");
            Assert.That(valid.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase));
        }

        [Test]
        public void PlanValidation_VerifiesContentAndCollectionHashes()
        {
            string collections = Path.Combine(temporaryRoot, "collections");
            Directory.CreateDirectory(collections);
            string collection = Path.Combine(collections, "startup.graphicsstate");
            File.WriteAllText(collection, "deterministic-test-collection");

            var plan = new PsoWarmupPlanDocument
            {
                profileId = "unit-test",
                generatedUtc = "2026-09-01T00:00:00.0000000Z",
                runtimePlatform = "WindowsPlayer",
                graphicsDeviceType = "Direct3D12",
                qualityLevelName = "High",
                sourceSessionCount = 1,
                sourceSessionHashes = new[] { "source-hash" },
                phases = new[]
                {
                    new PsoWarmupPhasePlan
                    {
                        phase = "startup",
                        collectionFile = "collections/startup.graphicsstate",
                        collectionSha256 = PsoFileUtility.ComputeSha256(collection),
                        variantCount = 4,
                        graphicsStateCount = 8,
                    },
                },
            };
            plan.planSha256 = PsoPlanValidation.ComputeContentHash(plan);
            string planPath = Path.Combine(temporaryRoot, "plan.json");
            PsoFileUtility.WriteJsonAtomic(planPath, plan);

            List<string> issues = PsoPlanValidation.Validate(plan, planPath, false, true);
            Assert.That(issues, Is.Empty);

            plan.phases[0].graphicsStateCount++;
            issues = PsoPlanValidation.Validate(plan, planPath, false, true);
            Assert.That(issues, Has.Some.Contains("planSha256"));
        }

        [Test]
        public void AtomicJsonWrite_ReplacesFileWithoutLeavingTemporaryState()
        {
            string path = Path.Combine(temporaryRoot, "receipt.json");
            var first = new PsoSessionManifest { sessionId = "first" };
            var second = new PsoSessionManifest { sessionId = "second" };
            PsoFileUtility.WriteJsonAtomic(path, first);
            PsoFileUtility.WriteJsonAtomic(path, second);

            Assert.That(PsoFileUtility.ReadJson<PsoSessionManifest>(path).sessionId,
                Is.EqualTo("second"));
            Assert.That(Directory.GetFiles(temporaryRoot, "*.tmp-*"), Is.Empty);
        }
    }
}
