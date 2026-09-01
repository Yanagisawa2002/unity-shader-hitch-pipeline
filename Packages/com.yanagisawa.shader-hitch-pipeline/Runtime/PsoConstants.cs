namespace Yanagisawa.ShaderHitchPipeline
{
    public static class PsoConstants
    {
        public const int SchemaVersion = 1;
        public const string PackageVersion = "0.1.0";
        public const string DefaultStreamingSubdirectory = "ShaderHitchPipeline";
        public const string DefaultPlanFileName = "plan.json";
        public const string DefaultRuntimeDirectoryName = "ShaderHitchPipeline";
        public const string GraphicsStateExtension = ".graphicsstate";
        public const string SessionManifestSuffix = ".session.json";
        public const string WarmupReceiptSuffix = ".warmup.json";
        public const string BenchmarkReceiptSuffix = ".benchmark.json";

        public const string TraceArgument = "-pso-trace";
        public const string TracePhaseArgument = "-pso-trace-phase";
        public const string SessionArgument = "-pso-session";
        public const string OutputArgument = "-pso-output";
        public const string TraceAutoStopArgument = "-pso-trace-auto-stop-seconds";
        public const string SendToEditorArgument = "-pso-send-to-editor";
        public const string DisableWarmupArgument = "-pso-disable-warmup";
        public const string WarmupPlanArgument = "-pso-warmup-plan";
        public const string WarmupPhaseArgument = "-pso-warmup-phase";
        public const string BenchmarkArgument = "-pso-benchmark";
        public const string BenchmarkModeArgument = "-pso-benchmark-mode";
        public const string BenchmarkFramesArgument = "-pso-benchmark-frames";
        public const string BenchmarkDiscardFramesArgument = "-pso-benchmark-discard-frames";
        public const string BenchmarkDelayArgument = "-pso-benchmark-delay-seconds";
        public const string BenchmarkReportArgument = "-pso-benchmark-report";
        public const string BenchmarkNoQuitArgument = "-pso-benchmark-no-quit";
        public const string BenchmarkHitchThresholdArgument = "-pso-hitch-threshold-ms";
        public const string BenchmarkWarmupTimeoutArgument =
            "-pso-benchmark-warmup-timeout-seconds";

        public const string InboxArgument = "-pso-inbox";
        public const string ProfileOutputArgument = "-pso-profile-output";
        public const string ProfileArgument = "-pso-profile";
        public const string InstallPlanArgument = "-pso-install-plan";
        public const string TrainingBuildArgument = "-pso-training-build";
        public const string BuildOutputArgument = "-pso-build-output";
    }
}
