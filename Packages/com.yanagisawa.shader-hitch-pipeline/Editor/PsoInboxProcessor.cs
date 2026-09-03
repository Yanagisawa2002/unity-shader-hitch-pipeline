using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_6000_5_OR_NEWER
using GraphicsStateCollection = UnityEngine.Rendering.GraphicsStateCollection;
#else
using GraphicsStateCollection = UnityEngine.Experimental.Rendering.GraphicsStateCollection;
#endif

namespace Yanagisawa.ShaderHitchPipeline.Editor
{
    public sealed class PsoInboxProcessingResult
    {
        public string planPath;
        public string receiptPath;
        public PsoWarmupPlanDocument plan;
        public PsoMergeReceipt receipt;
    }

    public static class PsoInboxProcessor
    {
        private sealed class Candidate
        {
            public string manifestPath;
            public string collectionPath;
            public PsoSessionManifest manifest;
            public RuntimePlatform platform;
            public GraphicsDeviceType graphicsApi;
            public string quality;
            public string groupKey;
            public PsoMergeInputReceipt receipt;
        }

        public static PsoInboxProcessingResult Process(PsoProjectConfiguration configuration)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));
            configuration.ValidateSettings();

            string inbox = configuration.ResolveProjectPath(configuration.inboxDirectory);
            string outputRoot = configuration.ResolveProjectPath(
                configuration.profileOutputDirectory);
            if (!Directory.Exists(inbox))
                throw new DirectoryNotFoundException("PSO inbox does not exist: " + inbox);

            string[] manifests = Directory.GetFiles(
                inbox,
                "*" + PsoConstants.SessionManifestSuffix,
                SearchOption.AllDirectories);
            Array.Sort(manifests, StringComparer.OrdinalIgnoreCase);
            if (manifests.Length == 0)
                throw new InvalidOperationException("No trace session manifests were found in " + inbox);

            var allReceipts = new List<PsoMergeInputReceipt>();
            var candidates = new List<Candidate>();
            for (int index = 0; index < manifests.Length; index++)
                InspectManifest(manifests[index], candidates, allReceipts);

            List<Candidate> selected = SelectProfileGroup(configuration, candidates);
            if (selected.Count == 0)
                throw new InvalidOperationException(
                    "No valid trace sessions match the configured platform/API/quality profile.");

            string selectedKey = selected[0].groupKey;
            for (int index = 0; index < candidates.Count; index++)
            {
                Candidate candidate = candidates[index];
                if (candidate.groupKey == selectedKey)
                    continue;
                candidate.receipt.reason = "Valid session belongs to a different environment profile.";
            }

            var excludedPhases = new HashSet<string>(
                configuration.excludedPhases ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            var planCandidates = new List<Candidate>();
            for (int index = 0; index < selected.Count; index++)
            {
                Candidate candidate = selected[index];
                if (excludedPhases.Contains(candidate.manifest.phase))
                {
                    candidate.receipt.reason =
                        "Excluded by the configured plan phase scope.";
                    continue;
                }
                planCandidates.Add(candidate);
            }
            if (planCandidates.Count == 0)
                throw new InvalidOperationException(
                    "Every valid trace session was excluded by the configured plan phase scope.");

            string profileDirectory = Path.Combine(
                outputRoot,
                PsoFileUtility.SanitizeFileName(configuration.profileId));
            string collectionsDirectory = Path.Combine(profileDirectory, "collections");
            Directory.CreateDirectory(collectionsDirectory);

            var phases = new List<PsoWarmupPhasePlan>();
            Candidate first = planCandidates[0];
            IEnumerable<IGrouping<string, Candidate>> phaseGroups = planCandidates
                .GroupBy(candidate => candidate.manifest.phase, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => PhasePriority(group.Key))
                .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

            int priority = 0;
            foreach (IGrouping<string, Candidate> phaseGroup in phaseGroups)
            {
                string phaseName = PsoFileUtility.SanitizeFileName(phaseGroup.Key);
                bool startupPhase = PhasePriority(phaseName) == 0;
                var merged = new GraphicsStateCollection
                {
                    runtimePlatform = first.platform,
                    graphicsDeviceType = first.graphicsApi,
                    qualityLevelName = first.quality,
                };

                foreach (Candidate candidate in phaseGroup.OrderBy(
                             item => item.manifestPath,
                             StringComparer.OrdinalIgnoreCase))
                {
                    var source = new GraphicsStateCollection();
                    if (!source.LoadFromFile(candidate.collectionPath))
                        throw new IOException("Could not load " + candidate.collectionPath);

                    source = ApplyPhaseShaderFilter(
                        configuration,
                        phaseName,
                        source,
                        candidate.receipt);

                    int before = merged.totalGraphicsStateCount;
                    if (!PsoGraphicsStateCollectionCompatibility.Append(merged, source))
                        throw new InvalidDataException(
                            "Unity rejected compatible collection " + candidate.collectionPath);
                    int after = merged.totalGraphicsStateCount;
                    candidate.receipt.statesBefore = before;
                    candidate.receipt.statesAfter = after;
                    candidate.receipt.statesAdded = after - before;
                    candidate.receipt.accepted = true;
                    candidate.receipt.reason = after == before
                        ? "Accepted; all graphics states were duplicates."
                        : "Accepted and merged.";
                }

                string outputFile = Path.Combine(
                    collectionsDirectory,
                    phaseName + PsoConstants.GraphicsStateExtension);
                if (!merged.SaveToFile(outputFile))
                    throw new IOException("Could not save merged collection " + outputFile);

                phases.Add(new PsoWarmupPhasePlan
                {
                    phase = phaseName,
                    collectionFile = "collections/" + Path.GetFileName(outputFile),
                    collectionSha256 = PsoFileUtility.ComputeSha256(outputFile),
                    variantCount = merged.variantCount,
                    graphicsStateCount = merged.totalGraphicsStateCount,
                    required = true,
                    prewarmAtStartup = startupPhase,
                    traceCacheMisses = true,
                    priority = priority++,
                    initialBatchSize = configuration.initialBatchSize,
                    minimumBatchSize = configuration.minimumBatchSize,
                    maximumBatchSize = configuration.maximumBatchSize,
                    targetFrameMilliseconds = configuration.targetFrameMilliseconds,
                    deadlineMilliseconds = startupPhase
                        ? configuration.startupDeadlineMilliseconds
                        : configuration.deferredDeadlineMilliseconds,
                    estimatedMillisecondsPerState =
                        configuration.estimatedMillisecondsPerState,
                    bootstrapBatchSize = configuration.bootstrapBatchSize,
                    budgetSafetyMarginMilliseconds =
                        configuration.budgetSafetyMarginMilliseconds,
                    budgetCostSafetyMultiplier =
                        configuration.budgetCostSafetyMultiplier,
                    budgetCooldownFrames = configuration.budgetCooldownFrames,
                    preinteractiveBootstrap =
                        startupPhase && configuration.preinteractiveBootstrap,
                    expectedUseProbability = startupPhase
                        ? configuration.startupExpectedUseProbability
                        : configuration.deferredExpectedUseProbability,
                    hotSetTier = startupPhase
                        ? configuration.startupHotSetTier
                        : configuration.deferredHotSetTier,
                });
            }

            var plan = new PsoWarmupPlanDocument
            {
                profileId = configuration.profileId,
                generatedUtc = PsoFileUtility.UtcNowText(),
                runtimePlatform = first.platform.ToString(),
                graphicsDeviceType = first.graphicsApi.ToString(),
                qualityLevelName = first.quality,
                adapterId = first.manifest.adapterId,
                adapterVersion = "1",
                sourceSessionCount = planCandidates.Count,
                sourceSessionHashes = planCandidates
                    .Select(candidate => candidate.manifest.collectionSha256)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                phases = phases.ToArray(),
            };
            plan.planSha256 = PsoPlanValidation.ComputeContentHash(plan);

            string planPath = Path.Combine(profileDirectory, PsoConstants.DefaultPlanFileName);
            PsoFileUtility.WriteJsonAtomic(planPath, plan);
            List<string> planIssues = PsoPlanValidation.Validate(plan, planPath, false, true);
            if (planIssues.Count > 0)
                throw new InvalidDataException(string.Join(Environment.NewLine, planIssues));

            var receipt = new PsoMergeReceipt
            {
                generatedUtc = PsoFileUtility.UtcNowText(),
                profileId = configuration.profileId,
                outputPlan = planPath,
                outputPlanSha256 = PsoFileUtility.ComputeSha256(planPath),
                acceptedSessions = allReceipts.Count(item => item.accepted),
                rejectedSessions = allReceipts.Count(item => !item.accepted),
                inputs = allReceipts.ToArray(),
            };
            string receiptPath = Path.Combine(
                profileDirectory,
                "merge-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".json");
            PsoFileUtility.WriteJsonAtomic(receiptPath, receipt);

            return new PsoInboxProcessingResult
            {
                planPath = planPath,
                receiptPath = receiptPath,
                plan = plan,
                receipt = receipt,
            };
        }

        private static void InspectManifest(
            string manifestPath,
            ICollection<Candidate> candidates,
            ICollection<PsoMergeInputReceipt> receipts)
        {
            var receipt = new PsoMergeInputReceipt { manifestFile = manifestPath };
            receipts.Add(receipt);

            try
            {
                PsoSessionManifest manifest = PsoFileUtility.ReadJson<PsoSessionManifest>(manifestPath);
                receipt.sessionId = manifest.sessionId;
                receipt.phase = manifest.phase;
                receipt.collectionSha256 = manifest.collectionSha256;
                if (manifest.schemaVersion != PsoConstants.SchemaVersion)
                    throw new InvalidDataException("Unsupported manifest schemaVersion.");
                if (!manifest.saved || !string.IsNullOrWhiteSpace(manifest.error))
                    throw new InvalidDataException("Trace session did not save successfully.");
                if (manifest.variantCount <= 0 || manifest.graphicsStateCount <= 0)
                    throw new InvalidDataException(
                        "Trace session is empty; no shader variants or graphics states were observed.");
                if (manifest.environment == null)
                    throw new InvalidDataException("Trace session has no environment snapshot.");
                if (string.IsNullOrWhiteSpace(manifest.phase))
                    throw new InvalidDataException("Trace phase is empty.");
                if (!string.Equals(
                        manifest.adapterId,
                        PsoUnityGraphicsStateTraceBackend.UnityAdapterId,
                        StringComparison.Ordinal))
                    throw new InvalidDataException(
                        "This Unity inbox processor cannot consume adapter '" +
                        manifest.adapterId + "'.");

                string directory = Path.GetDirectoryName(manifestPath);
                string collectionPath = PsoFileUtility.ResolveChildPath(
                    directory,
                    manifest.collectionFile);
                receipt.collectionFile = collectionPath;
                if (!File.Exists(collectionPath))
                    throw new FileNotFoundException("Trace collection is missing.", collectionPath);
                if (!string.Equals(
                        PsoFileUtility.ComputeSha256(collectionPath),
                        manifest.collectionSha256,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Trace collection SHA-256 does not match.");

                if (!Enum.TryParse(manifest.environment.runtimePlatform, out RuntimePlatform platform))
                    throw new InvalidDataException("Unknown runtime platform.");
                if (!Enum.TryParse(
                        manifest.environment.graphicsDeviceType,
                        out GraphicsDeviceType graphicsApi))
                    throw new InvalidDataException("Unknown graphics API.");

                var collection = new GraphicsStateCollection();
                if (!collection.LoadFromFile(collectionPath))
                    throw new InvalidDataException("Unity could not load the trace collection.");
                if (collection.runtimePlatform != platform ||
                    collection.graphicsDeviceType != graphicsApi)
                    throw new InvalidDataException(
                        "Collection metadata differs from the session manifest.");

                string quality = manifest.environment.qualityLevelName ?? string.Empty;
                if (!string.Equals(
                        collection.qualityLevelName ?? string.Empty,
                        quality,
                        StringComparison.Ordinal))
                    throw new InvalidDataException(
                        "Collection quality differs from the session manifest.");
                if (collection.variantCount != manifest.variantCount ||
                    collection.totalGraphicsStateCount != manifest.graphicsStateCount)
                    throw new InvalidDataException(
                        "Collection counts differ from the session manifest.");
                string key = manifest.adapterId + "|" + platform + "|" +
                             graphicsApi + "|" + quality;
                candidates.Add(new Candidate
                {
                    manifestPath = manifestPath,
                    collectionPath = collectionPath,
                    manifest = manifest,
                    platform = platform,
                    graphicsApi = graphicsApi,
                    quality = quality,
                    groupKey = key,
                    receipt = receipt,
                });
                receipt.reason = "Valid; awaiting environment-profile selection.";
            }
            catch (Exception exception)
            {
                receipt.accepted = false;
                receipt.reason = exception.Message;
            }
        }

        private static List<Candidate> SelectProfileGroup(
            PsoProjectConfiguration configuration,
            IEnumerable<Candidate> candidates)
        {
            IEnumerable<Candidate> filtered = candidates;
            if (!string.IsNullOrWhiteSpace(configuration.targetRuntimePlatform))
                filtered = filtered.Where(candidate => string.Equals(
                    candidate.platform.ToString(),
                    configuration.targetRuntimePlatform,
                    StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(configuration.targetGraphicsDeviceType))
                filtered = filtered.Where(candidate => string.Equals(
                    candidate.graphicsApi.ToString(),
                    configuration.targetGraphicsDeviceType,
                    StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(configuration.targetQualityLevelName))
                filtered = filtered.Where(candidate => string.Equals(
                    candidate.quality,
                    configuration.targetQualityLevelName,
                    StringComparison.Ordinal));

            IGrouping<string, Candidate> selected = filtered
                .GroupBy(candidate => candidate.groupKey, StringComparer.Ordinal)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.Ordinal)
                .FirstOrDefault();
            return selected == null
                ? new List<Candidate>()
                : selected.OrderBy(item => item.manifestPath, StringComparer.OrdinalIgnoreCase)
                    .ToList();
        }

        private static GraphicsStateCollection ApplyPhaseShaderFilter(
            PsoProjectConfiguration configuration,
            string phase,
            GraphicsStateCollection source,
            PsoMergeInputReceipt receipt)
        {
            receipt.sourceVariantCount = source.variantCount;
            receipt.sourceGraphicsStateCount = source.totalGraphicsStateCount;
            receipt.filteredVariantCount = source.variantCount;
            receipt.filteredGraphicsStateCount = source.totalGraphicsStateCount;

            PsoPhaseShaderFilter filter =
                (configuration.phaseShaderFilters ??
                 Array.Empty<PsoPhaseShaderFilter>())
                .FirstOrDefault(item => item != null && string.Equals(
                    item.phase,
                    phase,
                    StringComparison.OrdinalIgnoreCase));
            if (filter == null)
                return source;

            var allowlist = new HashSet<string>(
                filter.shaderNames ?? Array.Empty<string>(),
                StringComparer.Ordinal);
            var variants = new List<GraphicsStateCollection.ShaderVariant>();
            source.GetVariants(variants);
            string[] observedShaderNames = variants
                .Select(variant => variant.shader == null
                    ? "<unresolved>"
                    : variant.shader.name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            var filtered = new GraphicsStateCollection
            {
                runtimePlatform = source.runtimePlatform,
                graphicsDeviceType = source.graphicsDeviceType,
                qualityLevelName = source.qualityLevelName,
            };
            var states = new List<GraphicsStateCollection.GraphicsState>();
            for (int index = 0; index < variants.Count; index++)
            {
                GraphicsStateCollection.ShaderVariant variant = variants[index];
                if (variant.shader == null || !allowlist.Contains(variant.shader.name))
                    continue;
                states.Clear();
                source.GetGraphicsStatesForVariant(variant, states);
                // A variant can be observed before its graphics-state payload is
                // committed. Do not copy an empty shell into the warmup plan.
                if (states.Count == 0)
                    continue;
                if (!filtered.AddVariant(
                        variant.shader,
                        variant.passId,
                        variant.keywords))
                {
                    throw new InvalidDataException(
                        "Unity refused to copy an allowed shader variant from phase '" +
                        phase + "'.");
                }
                for (int stateIndex = 0; stateIndex < states.Count; stateIndex++)
                {
                    if (!filtered.AddGraphicsStateForVariant(
                            variant.shader,
                            variant.passId,
                            variant.keywords,
                            states[stateIndex]))
                    {
                        throw new InvalidDataException(
                            "Unity refused to copy an allowed graphics state from phase '" +
                            phase + "'.");
                    }
                }
            }

            receipt.shaderFilterApplied = true;
            receipt.shaderAllowlist = allowlist.OrderBy(
                value => value,
                StringComparer.Ordinal).ToArray();
            receipt.filteredVariantCount = filtered.variantCount;
            receipt.filteredGraphicsStateCount = filtered.totalGraphicsStateCount;
            receipt.excludedVariantCount = Math.Max(
                0,
                receipt.sourceVariantCount - receipt.filteredVariantCount);
            receipt.excludedGraphicsStateCount = Math.Max(
                0,
                receipt.sourceGraphicsStateCount -
                receipt.filteredGraphicsStateCount);
            if (filtered.variantCount <= 0 || filtered.totalGraphicsStateCount <= 0)
            {
                throw new InvalidDataException(
                    "The shader allowlist removed every warmable state from phase '" +
                    phase + "'. Observed shaders: " +
                    string.Join(", ", observedShaderNames) + ".");
            }
            UnityEngine.Object.DestroyImmediate(source);
            return filtered;
        }

        private static int PhasePriority(string phase)
        {
            return string.Equals(phase, "startup", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(phase, "bootstrap", StringComparison.OrdinalIgnoreCase)
                ? 0
                : 1;
        }
    }
}
