using System;
using System.Collections.Generic;
using Yanagisawa.ShaderHitchPipeline;

static class ContentLifecycleTests
{
    sealed class Sink : IPsoContentPhaseSink
    {
        public readonly List<string> Activated = new();
        public readonly List<string> Cancelled = new();
        public bool Ready, Accept = true, FailCancel;
        public bool Activate(string phase) { Activated.Add(phase); return Accept; }
        public void Cancel(string phase) { if (FailCancel) throw new InvalidOperationException("unproven fence"); Cancelled.Add(phase); }
        public void Unload(string phase) => Cancel(phase);
        public bool IsComplete(string phase) => Ready;
    }
    static int checks;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    public static void Run()
    {
        var sink = new Sink();
        using var lifecycle = new PsoContentPhaseLifecycle(sink);
        var a = lifecycle.Request("scene-a", "v1", "city");
        var b = lifecycle.Request("scene-b", "v1", "city");
        Check(sink.Activated.Count == 0, "Must not warm before dependencies are loaded");
        Check(lifecycle.DependenciesReady(a), "First load ready");
        Check(lifecycle.DependenciesReady(b) && sink.Activated.Count == 1, "Shared phase activated once");
        lifecycle.Cancel(a);
        Check(sink.Cancelled.Count == 0 && a.Status == PsoContentPhaseStatus.Cancelled, "Other scene retains demand");
        Check(!lifecycle.DependenciesReady(a), "Late async completion after cancellation ignored");
        lifecycle.Refresh();
        Check(b.Status == PsoContentPhaseStatus.Active, "No false completion");
        sink.Ready = true;
        lifecycle.Refresh();
        Check(b.Status == PsoContentPhaseStatus.Complete, "Backend completion observed");
        sink.FailCancel = true;
        try { lifecycle.Unload(b); throw new Exception("Expected failed fence"); } catch (InvalidOperationException) { }
        Check(b.Status == PsoContentPhaseStatus.Complete, "Failed cancellation retains ownership");
        sink.FailCancel = false;
        lifecycle.Unload(b);
        Check(sink.Cancelled.Count == 1 && b.Status == PsoContentPhaseStatus.Unloaded, "Last owner fenced on unload");
        var old = lifecycle.Request("scene-a", "v1", "city");
        lifecycle.DependenciesReady(old);
        var replacement = lifecycle.Request("scene-a", "v2", "city");
        Check(old.Status == PsoContentPhaseStatus.Unloaded && replacement.Generation > old.Generation, "Revision retires old generation");
        Check(!lifecycle.DependenciesReady(old), "Old generation cannot resurrect");
        sink.Accept = false;
        Check(!lifecycle.DependenciesReady(replacement) && replacement.Status == PsoContentPhaseStatus.Failed,
            "Unknown phase remains a visible failure");
        var pending = lifecycle.Request("pending", "1", "another");
        lifecycle.Cancel(pending);
        Check(!lifecycle.DependenciesReady(pending), "Cancelled load never submitted");
        var cold = lifecycle.Request("cold", "1", "another");
        int priorActivations = sink.Activated.Count;
        Check(lifecycle.DependenciesReady(cold, true) && cold.Status == PsoContentPhaseStatus.RenderingCold &&
            sink.Activated.Count == priorActivations, "Cold arm records bypass without claiming completed warmup");
        using var foreign = new PsoContentPhaseLifecycle(sink);
        try { foreign.Cancel(b); throw new Exception("Expected foreign handle rejection"); } catch (ArgumentException) { checks++; }
        lifecycle.Dispose();
        try { lifecycle.Request("late", "v1", "city"); throw new Exception("Expected disposed rejection"); }
        catch (ObjectDisposedException) { checks++; }
        Console.WriteLine("CONTENT_LIFECYCLE_OK checks=" + checks);
    }
}
