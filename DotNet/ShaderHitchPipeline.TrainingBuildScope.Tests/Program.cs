using System;
using Yanagisawa.ShaderHitchPipeline.Editor;

if (PsoTrainingBuildScope.IsActive) throw new Exception("Unexpected training bypass");
try
{
    using (new PsoTrainingBuildScope())
    {
        using (var nested = new PsoTrainingBuildScope())
        {
            if (!PsoTrainingBuildScope.IsActive) throw new Exception("Missing nested scope");
            nested.Dispose();
        }
        if (!PsoTrainingBuildScope.IsActive) throw new Exception("Nested dispose cleared outer scope");
        throw new InvalidOperationException("Simulated build failure");
    }
}
catch (InvalidOperationException) { }
if (PsoTrainingBuildScope.IsActive) throw new Exception("Failed build left training bypass active");
Console.WriteLine("TRAINING_BUILD_SCOPE_PASS nested and failed builds restore the final-build gate");
