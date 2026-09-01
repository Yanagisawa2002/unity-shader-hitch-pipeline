# PSO Hitch Showcase

This sample reveals 384 real material/render-state combinations over 48 frames. Its local shader keywords produce 256 variants, while blend, depth, and cull properties produce distinct modern graphics states.

Use **Tools > Shader Hitch Pipeline > Build PSO Showcase**, or run the repository-level `Tools/Invoke-PsoShowcase.ps1` workflow. A cold player trace trains the plan and supplies the baseline; the final player embeds that plan and supplies the optimized measurement.

The workload contains no artificial sleeps or simulated frame spikes. Keep the Windows player visible while tracing so D3D12 submits presentable frames.
