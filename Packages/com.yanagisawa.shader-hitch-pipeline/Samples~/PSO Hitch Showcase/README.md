# PSO Hitch Showcase

This sample reveals 384 real material/render-state combinations over 48 frames. Its local shader keywords produce 256 variants, while blend, depth, and cull properties produce distinct modern graphics states.

Use **Tools > Shader Hitch Pipeline > Build PSO Showcase**, or run the repository-level `Tools/Invoke-PsoShowcase.ps1` workflow. A cache-isolated cold Player trains the plan; the matching final Player supplies Unity all-at-once and deadline-scheduled controls. The runner also searches native async-worker and progressive-batch candidates.

The workload contains no artificial sleeps or simulated frame spikes. Its live scanline, presentation clock, and frame metrics are rendered by the Player, and statistics reset exactly at `WORKLOAD_START`. Reports retain every ≥8.33 ms presentation-budget miss and separately gate ≥16.67 ms severe stalls. Keep the Windows Player visible while capturing so D3D12 submits presentable frames.
