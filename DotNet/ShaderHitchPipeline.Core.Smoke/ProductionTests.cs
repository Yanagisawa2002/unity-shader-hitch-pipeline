using System;
using System.IO;
using System.Linq;
using Yanagisawa.ShaderHitchPipeline;

static class ProductionTests
{
    static int checks;
    static void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "pso-production-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "plan.json");
            PsoAtomicFile.WriteText(path, "{\"version\":1}");
            PsoAtomicFile.WriteText(path, "{\"version\":2,\"name\":\"城市\"}");
            Check(File.ReadAllText(path).Contains("城市"), "Complete UTF-8 replacement published");
            Check(Directory.GetFiles(root).Length == 1, "No leftover temporary file");
            string blocked = Path.Combine(root, "blocked");
            Directory.CreateDirectory(blocked);
            File.WriteAllText(Path.Combine(blocked, "previous"), "retained");
            try { PsoAtomicFile.WriteText(blocked, "new"); throw new Exception("Expected publish failure"); }
            catch (IOException) { checks++; }
            Check(File.ReadAllText(Path.Combine(blocked, "previous")) == "retained", "Failed publish preserves old destination");
            Check(Directory.GetFiles(root, "*.tmp").Length == 0, "Failed publish cleans only temporary file");
            foreach (string invalid in new[] { "../escape", "..\\escape", "C:/escape", "collections/file:stream", "/absolute" })
            {
                try { PsoRelativePath.Resolve(root, invalid); throw new Exception("Expected unsafe path rejection"); }
                catch (InvalidOperationException) { checks++; }
            }
            Check(PsoRelativePath.Resolve(root, "collections/phase.bin") == Path.Combine(root, "collections", "phase.bin"), "Portable path resolves below root");
            var plan = new PsoWarmupPlanDocument { profileId = "test", phases = new[] {
                new PsoWarmupPhasePlan { phase = "city", collectionFile = "city.graphicsstate" } } };
            foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                plan.phases[0].estimatedMillisecondsPerState = invalid;
                Check(PsoPlanRules.Validate(plan).Any(x => x.Contains("finite")), "Non-finite prior rejected");
            }
            plan.phases[0].estimatedMillisecondsPerState = 0.25;
            plan.phases[0].graphicsStateCount = -1;
            Check(PsoPlanRules.Validate(plan).Any(x => x.Contains("negative")), "Negative state count rejected");
        }
        finally
        {
            string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(root).StartsWith(temp, StringComparison.Ordinal)) throw new Exception("Unsafe temporary cleanup");
            Directory.Delete(root, true);
        }
        Console.WriteLine("PRODUCTION_OK checks=" + checks);
    }
}
