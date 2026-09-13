using System;

namespace Yanagisawa.ShaderHitchPipeline.Editor
{
    /// <summary>Explicit synchronous training build; never changes project gate settings.</summary>
    public sealed class PsoTrainingBuildScope : IDisposable
    {
        private static int depth;
        private bool disposed;
        public static bool IsActive => depth > 0;

        public PsoTrainingBuildScope() { depth++; }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            depth--;
        }
    }
}
