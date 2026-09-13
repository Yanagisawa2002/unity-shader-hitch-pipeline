using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Yanagisawa.ShaderHitchPipeline;

static class Program
{
    static int checks;
    static void Require(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static void Throws(Action action, string message) { bool threw = false; try { action(); } catch { threw = true; } Require(threw, message); }
    static Dictionary<string, IReadOnlyList<PsoStreamingState>> Plan(params string[] keys) => new Dictionary<string, IReadOnlyList<PsoStreamingState>>
        { ["main"] = Array.ConvertAll(keys, key => new PsoStreamingState(key, key)) };
    static void Main()
    {
        int released = 0;
        var backend = new Backend(); var core = new PsoStreamingCoordinator(backend);
        var assets = new PsoRetainedAsset(() => released++);
        var a = core.RegisterLoaded("a", "r1", "build", Plan("x", "y", "x"), assets);
        var b = core.RegisterLoaded("b", "r1", "build", Plan("x", "z"), assets);
        assets.Dispose(); Require(assets.ReferenceCount == 2, "Shared asset must have two owner references.");
        var ar = core.RequestPhase(a, "main"); var br = core.RequestPhase(b, "main");
        var extra = core.RequestPhase(b, "main"); extra.Dispose();
        core.Tick(1); Require(backend.count == 1, "Batch subset should contain one unique state.");
        core.Unload(a); Require(released == 0 && assets.ReferenceCount == 2, "Submitted owner must remain pinned.");
        Require(ar.IsCancelled, "Unload cancels owner demand.");
        Throws(() => core.RequestPhase(a, "main"), "Stale owner cannot request new work.");
        backend.job.ready = true; core.Tick(8);
        Require(assets.ReferenceCount == 1, "Completed pin must release unloaded owner.");
        Require(backend.count == 2, "Only remaining demanded z should submit; y was cancelled.");
        backend.job.ready = true; core.Tick(); Require(br.IsComplete, "Overlapping phase should complete.");
        core.Unload(b); core.Unload(b); Require(released == 1 && core.RetainedStateCount == 0, "Unload and duplicate unload release once.");
        var reloadAssets = new PsoRetainedAsset(() => released++);
        var reload = core.RegisterLoaded("a", "r1", "build", Plan("x"), reloadAssets); reloadAssets.Dispose();
        core.RequestPhase(reload, "main"); core.Tick(); Require(backend.count == 3, "Reload must not trust an unretained completed cache.");
        core.Dispose(); Require(backend.job.fenced && released == 2, "Dispose must fence unfinished native work before release.");

        // Revision invalidation, failed validation atomicity, same-content concurrent owners and namespace isolation.
        backend = new Backend(); core = new PsoStreamingCoordinator(backend);
        assets = new PsoRetainedAsset(() => released++);
        a = core.RegisterLoaded("same", "r1", "build", Plan("x"), assets); ar = core.RequestPhase(a, "main");
        Throws(() => core.RegisterLoaded("same", "r2", "", Plan("x"), assets), "Unknown identity must reject.");
        Require(!a.IsUnloaded, "Rejected revision must not retire valid content.");
        b = core.RegisterLoaded("same", "r2", "build", Plan("x"), assets);
        Require(a.IsUnloaded && ar.IsCancelled, "Revision replaces old pending demand.");
        var c = core.RegisterLoaded("same", "r2", "build", Plan("x"), assets);
        var d = core.RegisterLoaded("other", "r2", "other-build", Plan("x"), assets);
        assets.Dispose(); core.RequestPhase(b, "main"); core.RequestPhase(c, "main"); core.RequestPhase(d, "main");
        core.Tick(8); Require(backend.count == 2, "Same namespace dedup; changed namespace must not dedup."); core.Dispose();
        Throws(() => core.RegisterLoaded("a", "r", "b", Plan("x"), assets), "Disposed core cannot register.");

        backend = new Backend { failSubmit = true }; core = new PsoStreamingCoordinator(backend);
        assets = new PsoRetainedAsset(() => released++); a = core.RegisterLoaded("failure", "r", "b", Plan("x"), assets); assets.Dispose();
        ar = core.RequestPhase(a, "main"); core.Tick(); Require(ar.Failure != null && !core.HasSubmittedWork && !ar.IsComplete, "Submission failure must be observable without phantom fence.");
        core.Tick(); Require(backend.attempts == 1, "Failed state must not endlessly resubmit."); core.Dispose();

        backend = new Backend(); core = new PsoStreamingCoordinator(backend);
        int fenceRelease = 0; assets = new PsoRetainedAsset(() => fenceRelease++);
        a = core.RegisterLoaded("fence", "r", "b", Plan("x"), assets); assets.Dispose(); core.RequestPhase(a, "main"); core.Tick();
        backend.job.failFence = true;
        Throws(() => core.Dispose(), "Unknown fence failure must propagate.");
        Require(fenceRelease == 0 && core.HasSubmittedWork, "Unproven fence must retain resources.");
        backend.job.failFence = false; core.Dispose(); Require(fenceRelease == 1, "Successful drain retry releases resources.");

        backend = new Backend(); core = new PsoStreamingCoordinator(backend);
        assets = new PsoRetainedAsset(() => {}); a = core.RegisterLoaded("result", "r", "b", Plan("x"), assets); assets.Dispose();
        ar = core.RequestPhase(a, "main"); core.Tick(); backend.job.Failure = "fenced operation failure";
        core.Drain(); Require(ar.Failure != null && !ar.IsComplete && !core.HasSubmittedWork, "Fenced failure must report failure and release job pins."); core.Dispose();

        core = new PsoStreamingCoordinator(new Backend());
        var foreign = new PsoStreamingCoordinator(new Backend()); assets = new PsoRetainedAsset(() => {});
        a = core.RegisterLoaded("thread", "r", "b", Plan("x"), assets); assets.Dispose();
        Throws(() => foreign.Unload(a), "Foreign handle must reject.");
        Require(Task.Run(() => { try { core.Tick(); return false; } catch (InvalidOperationException) { return true; } }).Result, "Off-thread engine access must reject.");
        var mutable = Plan("p"); var copyAssets = new PsoRetainedAsset(() => {});
        b = core.RegisterLoaded("copy", "r", "b", mutable, copyAssets); copyAssets.Dispose(); mutable.Clear();
        br = core.RequestPhase(b, "main"); br.Dispose(); core.Tick(); Require(!core.HasSubmittedWork, "Cancellation before submit removes all queued work; plan is snapshotted.");
        core.Dispose(); foreign.Dispose();

        for (int cycle = 0; cycle < 32; cycle++)
        {
            backend = new Backend(); core = new PsoStreamingCoordinator(backend); int drainedReleases = 0;
            assets = new PsoRetainedAsset(() => drainedReleases++);
            a = core.RegisterLoaded("revision-flight", "r1", "build", Plan("x"), assets); assets.Dispose();
            ar = core.RequestPhase(a, "main"); core.Tick();
            var nextAssets = new PsoRetainedAsset(() => drainedReleases++);
            b = core.RegisterLoaded("revision-flight", "r2", "build", Plan("x"), nextAssets); nextAssets.Dispose();
            br = core.RequestPhase(b, "main");
            Require(a.IsUnloaded && ar.IsCancelled && drainedReleases == 0, "Replacing revision cannot release submitted old shader assets.");
            backend.job.ready = true; core.Tick();
            Require(drainedReleases == 1 && !br.IsComplete && backend.count == 2, "Revision needs separate work after old job fence.");
            core.Dispose(); Require(drainedReleases == 2, "Repeated revision cycles must not leak.");
        }
        backend = new Backend(); core = new PsoStreamingCoordinator(backend); int releasedAfterFailure = 0;
        assets = new PsoRetainedAsset(() => throw new Exception("release callback failed"));
        a = core.RegisterLoaded("release-a", "r", "b", Plan("x"), assets); assets.Dispose();
        var otherAssets = new PsoRetainedAsset(() => releasedAfterFailure++);
        b = core.RegisterLoaded("release-b", "r", "b", Plan("x"), otherAssets); otherAssets.Dispose();
        core.RequestPhase(a, "main"); core.RequestPhase(b, "main"); core.Tick();
        core.Unload(a); core.Unload(b);
        Throws(() => core.Drain(), "Release callback errors must remain observable.");
        Require(releasedAfterFailure == 1 && core.RetainedStateCount == 0 && !core.HasSubmittedWork,
            "One failing post-fence callback must not strand another owner's pin."); core.Dispose();
        core = new PsoStreamingCoordinator(new Backend()); assets = new PsoRetainedAsset(() => throw new Exception("old owner release"));
        a = core.RegisterLoaded("bad-release", "r1", "b", Plan("x"), assets); assets.Dispose();
        otherAssets = new PsoRetainedAsset(() => {});
        Throws(() => core.RegisterLoaded("bad-release", "r2", "b", Plan("x"), otherAssets), "Failed old release should fail registration visibly.");
        Require(core.RetainedStateCount == 0 && otherAssets.ReferenceCount == 1, "Failed revision registration must not strand an unreachable replacement.");
        otherAssets.Dispose(); core.Dispose();
        backend = new Backend(); core = new PsoStreamingCoordinator(backend);
        var twoPhases = Plan("x", "y"); twoPhases["later"] = Plan("y", "z")["main"];
        assets = new PsoRetainedAsset(() => {}); a = core.RegisterLoaded("phases", "r", "b", twoPhases, assets); assets.Dispose();
        ar = core.RequestPhase(a, "main"); br = core.RequestPhase(a, "later"); ar.Dispose();
        core.Tick(); Require(backend.count == 2, "Cancelling startup must preserve overlapping later-phase demand only.");
        core.Drain(); Require(br.IsComplete && ar.IsCancelled, "Distinct concurrent phase results must remain independent."); core.Dispose();
        Console.WriteLine("STREAMING_SMOKE_OK checks=" + checks);
    }
    sealed class Backend : IPsoStreamingBackend
    {
        internal int count, attempts; internal bool failSubmit; internal Job job;
        public IPsoWarmupBatch Submit(IReadOnlyList<PsoStreamingState> states)
        { attempts++; if (failSubmit) throw new Exception("pre-submit failure"); count += states.Count; return job = new Job(); }
    }
    sealed class Job : IPsoWarmupBatch, IPsoStreamingBatchResult
    {
        internal bool ready, fenced, failFence;
        public bool IsCompleted => ready;
        public string Failure { get; set; }
        public void Complete() { if (failFence) throw new Exception("unknown fence failure"); fenced = true; }
        public void Dispose() { if (!fenced) throw new Exception("disposed before fence"); }
    }
}
