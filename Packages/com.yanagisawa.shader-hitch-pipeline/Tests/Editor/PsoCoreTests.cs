using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace Yanagisawa.ShaderHitchPipeline.Tests
{
    public sealed class PsoCoreTests
    {
        private sealed class FakeTraceBackend : IPsoTraceBackend
        {
            public bool disposed;
            public string AdapterId => "test.adapter";
            public string ArtifactType => "test-artifact";
            public bool IsTracing => !disposed;

            public PsoTraceArtifactResult Finish(string outputPath, bool sendToEditor)
            {
                File.WriteAllText(outputPath, "trace-artifact");
                return new PsoTraceArtifactResult
                {
                    adapterId = AdapterId,
                    artifactType = ArtifactType,
                    stateCount = 7,
                    variantCount = 5,
                    saved = true,
                    sentToEditor = sendToEditor,
                };
            }

            public void Dispose()
            {
                disposed = true;
            }
        }

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
        public void AdaptiveBatchPolicy_ColdStartProbeOverridesUnsafeInitialBatch()
        {
            var policy = new PsoAdaptiveBatchPolicy(64, 1, 64, 16.67, 0.25);

            PsoBudgetAdmission admission = policy.Evaluate(
                remainingStates: 389,
                deadlineRemainingMilliseconds: 1000.0,
                hotSet: true);

            Assert.That(admission.BatchSize, Is.EqualTo(1));
            Assert.That(admission.Calibration, Is.True);
            Assert.That(admission.Reason, Is.EqualTo("cold-start-probe"));
        }

        [Test]
        public void AdaptiveBatchPolicy_DeadlineNeverOverridesHardBudget()
        {
            var policy = new PsoAdaptiveBatchPolicy(
                64,
                1,
                64,
                16.67,
                1.0,
                bootstrapBatchSize: 1,
                safetyMarginMilliseconds: 2.0,
                costSafetyMultiplier: 1.5,
                cooldownFrames: 2);

            Assert.That(policy.Evaluate(100, 1000.0, true).BatchSize, Is.EqualTo(1));
            policy.ObserveFrame(8.0, true, 1);
            policy.ObserveBatch(1, 8.0);
            Assert.That(policy.Evaluate(99, 1000.0, true).BatchSize, Is.EqualTo(1));
            policy.ObserveFrame(8.0, true, 1);
            policy.ObserveBatch(1, 4.0);

            for (int index = 0; index < 8; index++)
                policy.ObserveFrame(4.0, false, 0);

            PsoBudgetAdmission admission = policy.Evaluate(
                remainingStates: 98,
                deadlineRemainingMilliseconds: 1.0,
                hotSet: true);

            Assert.That(admission.IsAdmitted, Is.True);
            Assert.That(admission.BatchSize, Is.LessThan(admission.DeadlineBatchSize));
            Assert.That(admission.BatchSize, Is.LessThanOrEqualTo(admission.SafeBatchSize));
            Assert.That(admission.PredictedBatchMilliseconds,
                Is.LessThanOrEqualTo(admission.AvailableBudgetMilliseconds));
            Assert.That(admission.DeadlineFeasible, Is.False);
        }

        [Test]
        public void AdaptiveBatchPolicy_ViolationTripsCooldownAndRecovers()
        {
            var policy = new PsoAdaptiveBatchPolicy(
                64, 1, 64, 16.67, 0.25, 1, 2.0, 1.5, 3);
            Assert.That(policy.Evaluate(389, 1000.0, true).BatchSize, Is.EqualTo(1));

            policy.ObserveFrame(178.9, true, 1);
            policy.ObserveBatch(1, 165.4);
            Assert.That(policy.BudgetViolationCount, Is.EqualTo(1));
            Assert.That(policy.MinimumBatchViolationCount, Is.EqualTo(1));
            Assert.That(policy.IsBudgetFeasible, Is.False);
            Assert.That(policy.CircuitBreakerTripCount, Is.EqualTo(1));
            Assert.That(policy.ColdStartBatchMilliseconds, Is.EqualTo(165.4).Within(0.001));
            Assert.That(policy.Evaluate(388, 800.0, true).IsAdmitted, Is.False);

            for (int index = 0; index < 20; index++)
                policy.ObserveFrame(4.17, false, 0);

            PsoBudgetAdmission recovered = policy.Evaluate(388, 500.0, true);
            Assert.That(recovered.BatchSize, Is.EqualTo(1));
            Assert.That(recovered.Reason, Is.EqualTo("steady-state-probe"));
        }

        [Test]
        public void AdaptiveBatchPolicy_NeverRunsAProbePredictedOverBudget()
        {
            var policy = new PsoAdaptiveBatchPolicy(
                1, 1, 64, 16.67, 20.0, 1, 2.0, 1.5, 0);

            PsoBudgetAdmission admission = policy.Evaluate(100, 1000.0, true);

            Assert.That(admission.IsAdmitted, Is.False);
            Assert.That(admission.Calibration, Is.True);
            Assert.That(admission.Reason, Is.EqualTo("minimum-probe-exceeds-headroom"));
            Assert.That(admission.PredictedBatchMilliseconds,
                Is.GreaterThan(admission.AvailableBudgetMilliseconds));
        }

        [Test]
        public void DeadlineScheduler_PrioritizesAtRiskWorkBeforeHotSet()
        {
            var candidates = new[]
            {
                new PsoSchedulerCandidate(
                    "hot",
                    20,
                    0,
                    0,
                    1000.0,
                    100.0,
                    1.0,
                    1.0),
                new PsoSchedulerCandidate(
                    "deadline",
                    20,
                    1,
                    2,
                    150.0,
                    100.0,
                    1.0,
                    0.5),
            };

            PsoSchedulingDecision decision = PsoDeadlineCostScheduler.SelectNext(
                candidates,
                40.0);

            Assert.That(decision.Phase, Is.EqualTo("deadline"));
            Assert.That(decision.DeadlineCritical, Is.True);
        }

        [Test]
        public void DeadlineScheduler_UsesHotSetThenValueDensityWithoutDeadlineRisk()
        {
            var candidates = new[]
            {
                new PsoSchedulerCandidate("tail", 10, 0, 2, 0.0, 0.0, 1.0, 1.0),
                new PsoSchedulerCandidate("hot", 100, 5, 0, 0.0, 0.0, 1.0, 0.5),
            };
            Assert.That(
                PsoDeadlineCostScheduler.SelectNext(candidates, 40.0).Phase,
                Is.EqualTo("hot"));

            candidates = new[]
            {
                new PsoSchedulerCandidate("expensive", 100, 0, 1, 0.0, 0.0, 2.0, 1.0),
                new PsoSchedulerCandidate("cheap", 10, 5, 1, 0.0, 0.0, 1.0, 0.8),
            };
            Assert.That(
                PsoDeadlineCostScheduler.SelectNext(candidates, 40.0).Phase,
                Is.EqualTo("cheap"));
        }

        [Test]
        public void PlanRules_RunWithoutAnEngineAndRejectUnsafeBudgetSettings()
        {
            var plan = new PsoWarmupPlanDocument
            {
                profileId = "portable",
                adapterId = "example.native",
                adapterVersion = "1",
                phases = new[]
                {
                    new PsoWarmupPhasePlan
                    {
                        phase = "startup",
                        collectionFile = "startup.bin",
                        budgetSafetyMarginMilliseconds = 16.67,
                        targetFrameMilliseconds = 16.67,
                    },
                },
            };

            List<string> issues = PsoPlanRules.Validate(plan);

            Assert.That(issues, Has.Some.Contains("budgetSafetyMarginMilliseconds"));
        }

        [Test]
        public void TraceCoordinator_ProducesPortableManifestAndDisposesAdapter()
        {
            string artifact = Path.Combine(temporaryRoot, "trace.bin");
            var backend = new FakeTraceBackend();
            var coordinator = new PsoTraceCoordinator(
                "session",
                "startup",
                new[] { "rain" },
                "2026-09-02T00:00:00Z",
                new PsoEnvironmentSnapshot { engineName = "test-engine" },
                backend);

            PsoSessionManifest manifest = coordinator.Finish(
                artifact,
                true,
                PsoFileUtility.ComputeSha256,
                "2026-09-02T00:00:01Z");

            Assert.That(manifest.error, Is.Empty);
            Assert.That(manifest.adapterId, Is.EqualTo("test.adapter"));
            Assert.That(manifest.collectionFile, Is.EqualTo("trace.bin"));
            Assert.That(manifest.collectionSha256, Has.Length.EqualTo(64));
            Assert.That(manifest.environment.engineName, Is.EqualTo("test-engine"));
            Assert.That(manifest.sentToEditor, Is.True);
            Assert.That(backend.disposed, Is.True);
            Assert.That(coordinator.IsTracing, Is.False);
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
                        deadlineMilliseconds = 500.0,
                        estimatedMillisecondsPerState = 0.5,
                        expectedUseProbability = 1.0,
                        hotSetTier = 0,
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
