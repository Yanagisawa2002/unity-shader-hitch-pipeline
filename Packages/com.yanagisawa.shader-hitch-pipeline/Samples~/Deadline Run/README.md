# Deadline Run

`Deadline Run` is the 12-second portfolio and acceptance scene for the Shader
Hitch Pipeline. A fixed high-speed camera crosses an industrial tunnel and
reveals a rain-soaked combat airspace. Tunnel turbines, the tracked vehicle,
rain, hostile drones, shields, and a real trail make a presented-frame freeze
visible in the world rather than through a synthetic scanline. A continuously
advancing flight clock holds and jumps with the real presented picture.

The scene preallocates every object and material before measurement. The future
airspace is excluded from the camera by a layer until `CONTENT_REVEAL`; there is
no runtime instantiation, asset streaming, sleep, busy loop, or fabricated frame
time. The future scene contains 320 real shader-variant/render-state
combinations. `CONTENT_REQUEST` is emitted 2.0 seconds into the flight and both
warmup policies receive the same 2.5-second deadline.

Timed samples use a preallocated value-type buffer, scheduler candidate storage
is reused, and HUD styles are cached before measurement so the evidence layer
does not manufacture its own GC hitch.
Every graphics-state artifact is loaded before `CAPTURE_READY`; the timed
request transfers a preconstructed execution with an already resident backend.
Benchmark phase logs, receipt finalization/writes, and post-ready marker writes
are deferred until after the measured run.

- `baseline`: no prewarm; first use happens at the reveal.
- `naive`: Unity all-at-once warmup begins at the request.
- `scheduled`: deadline + cost + hot-set admission uses available frame slack.

The visual acceptance contract is intentionally separate from the deterministic
tile microbenchmark:

- cold baseline reveal-window maximum frame time must be at least 80 ms;
- scheduled must contain zero frames at or above 16.67 ms;
- the scheduled deferred phase must meet its deadline and hard-budget receipt;
- all runs must use the same environment and retain raw frame samples.

Import the sample and use **Tools > Shader Hitch Pipeline > Build Deadline Run**,
or run `Tools/Invoke-PsoDeadlineRun.ps1` after explicitly deciding to execute the
full build/train/measure/capture workflow.
