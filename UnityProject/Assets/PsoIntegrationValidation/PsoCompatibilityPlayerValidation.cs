using System;
using System.IO;
using UnityEngine;
using Yanagisawa.ShaderHitchPipeline;

// Opt-in real-Player evidence only. The ordinary showcase is unchanged when the flag is absent.
public sealed class PsoCompatibilityPlayerValidation : MonoBehaviour
{
    [Serializable] private sealed class Receipt
    {
        public string runId, generatedUtc, planFile, planSha256, error;
        public string priorCacheSha256, savedCacheSha256;
        public bool completed, costCacheRequested, costCacheApplied, immediateReplayApplied;
        public string[] importReasons, immediateReplayReasons;
        public PsoEnvironmentSnapshot environment;
        public PsoCostCacheEntry[] measuredEntries;
    }
    private Receipt receipt;
    private string path, cachePath, requiredPhase;
    private bool finished;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (!PsoCommandLine.Current.HasFlag("-pso-compatibility-validation")) return;
        var host = new GameObject("PSO integrated compatibility validation");
        DontDestroyOnLoad(host);
        host.AddComponent<PsoCompatibilityPlayerValidation>();
    }

    private void Start()
    {
        var orchestrator = PsoWarmupOrchestrator.Instance;
        string root = PsoCommandLine.Current.GetString(PsoConstants.OutputArgument, PsoFileUtility.DefaultRuntimeOutputRoot());
        cachePath = PsoCommandLine.Current.GetString("-pso-save-cost-cache", string.Empty);
        requiredPhase = PsoCommandLine.Current.GetString("-pso-compatibility-validation-phase", "combat");
        receipt = new Receipt { runId = PsoFileUtility.CreateRunId("compatibility-player"),
            generatedUtc = PsoFileUtility.UtcNowText(), environment = PsoUnityEnvironment.Capture() };
        path = Path.Combine(root, "Compatibility", receipt.runId + ".json");
        if (orchestrator == null || !orchestrator.HasLoadedPlan || string.IsNullOrWhiteSpace(cachePath))
        { Finish("A loaded plan and explicit save-cost-cache path are required."); return; }
        receipt.planFile = orchestrator.PlanPath;
        receipt.planSha256 = orchestrator.PlanHash;
        receipt.costCacheRequested = orchestrator.CostCacheRequested;
        receipt.costCacheApplied = orchestrator.CostCacheApplied;
        receipt.importReasons = orchestrator.CostCacheReasons;
        if (File.Exists(cachePath)) receipt.priorCacheSha256 = PsoFileUtility.ComputeSha256(cachePath);
    }

    private void Update()
    {
        if (finished || receipt == null) return;
        var orchestrator = PsoWarmupOrchestrator.Instance;
        if (orchestrator.HasFailed) { Finish(orchestrator.Failure); return; }
        if (!orchestrator.IsComplete || !orchestrator.IsPhaseComplete(requiredPhase)) return;
        try
        {
            orchestrator.SaveCostCache(cachePath);
            var cache = PsoFileUtility.ReadJson<PsoCostCacheDocument>(cachePath);
            receipt.savedCacheSha256 = PsoFileUtility.ComputeSha256(cachePath);
            receipt.measuredEntries = cache.entries;
            var detachedPlan = PsoPlanValidation.LoadAndValidate(receipt.planFile, true, true);
            receipt.immediateReplayApplied = PsoCostCacheStorage.TryApply(cachePath, detachedPlan, out string[] reasons);
            receipt.immediateReplayReasons = reasons;
            if (!receipt.immediateReplayApplied) throw new InvalidDataException("Saved measured cache failed its actual runtime replay: " + string.Join("; ", reasons));
            if (PsoCompatibility.ValidateIdentity(receipt.environment.identity).Count != 0 ||
                receipt.environment.driverIdentitySource != PsoCompatibility.DriverIdentitySource ||
                string.IsNullOrWhiteSpace(receipt.environment.driverIdentity))
                throw new InvalidDataException("Actual build/driver identity is unavailable.");
            receipt.completed = true;
            Finish(null);
        }
        catch (Exception error) { Finish(error.ToString()); }
    }

    private void Finish(string error)
    {
        finished = true;
        receipt.error = error;
        PsoFileUtility.WriteJsonAtomic(path, receipt);
        Debug.Log("PSO_COMPATIBILITY_PLAYER_" + (receipt.completed ? "OK" : "FAILED") + " " + path);
    }
    private void OnApplicationQuit()
    {
        if (receipt != null && !finished) Finish("Player quit before the declared phase and cache replay completed.");
    }
}
