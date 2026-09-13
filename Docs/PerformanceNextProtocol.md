# September 2026 independent performance follow-up

Baseline: b04ea55846e1bb681bcff8448da302236434ecb7. Historical system-v4 and
hotset outcomes are development evidence only. New source commits, build
receipts, binary inventories, generated workload identity, and raw outputs
remain separate. No default promotion, original-output overwrite, or push.

## Order of work

1. Replay every historical owned-PID CSV row using validated native QPC anchors.
   Partition by every adjacent marker, including pre/post workload. Decode the
   retained ETL with Windows tracerpt; report event loss and decoding limitations.
   This stage launches no GPU workload.
2. Build a fresh cache-busted Grid trace and compatible scheduled Player. Freeze
   its identity and the capture declaration before running ten new processes.
3. Evaluate capture disturbance before starting two new held-out hotset routes.

## Capture disturbance

Five adjacent independent-process pairs. Fixed order: PM/WPR, WPR/PM, PM/WPR,
WPR/PM, PM/WPR. Both arms use PresentMon 2.5.1 native QPC CSV, the identical
Development Player, 7200 retained benchmark frames, discard=0, 3-second engine
benchmark delay, four asynchronous PSO workers, 12 startup and 388 combat states,
1280x720 visible D3D12 window. Driver cache is uncontrolled and never cleared.
Both arms start PresentMon, allow its standard one-second startup, then wait
exactly 20 seconds before starting Player. WPR arm starts the GPU file-mode
profile before PresentMon. No WPR-only extra wait. PresentMon limit 180 seconds;
owned Player limit 150 seconds, leaving nine seconds beyond worst-case launch
and wait. Each capture owns only its own recorder sessions and process.

All 10 processes run under one shared validation mutex; ordinary builds do not
elevate. A reviewed finite runner may use standard Windows UAC, requiring actual
user consent. No scheduled-task/service workaround or permission changes.

Storage declaration: the historical 71-second ETL is 2,692,743,168 bytes, about
37.9 MB/s. Five typical captures are approximately 13.5 GB. Five worst-case
200-second recorder envelopes at that rate are approximately 38 GB; require
60 GiB initially and stop before launching another cell if free space falls
below 20 GiB. Retain all historical ETLs. These are estimates, not a disk quota;
Player/recorder timeouts bound recording duration, and stop/merge remains owned.

Primary outputs: whole-record CPU cadence p99/max/count >16.67 ms, displayed
cadence p99/max/count >16.67 ms, GPU busy p99/max/count >16.67 ms, separately.
Report the same statistics for each adjacent native marker interval, all missing
values, boundary crossings and uncaptured launch/shutdown intervals. No frame
exclusions, winsorization or frame-level statistical replication. The engine's
8.33-ms hitch flag remains unchanged and is reported as a separate engine metric.

Compute WPR-minus-PM differences per pair and equal-pair mean/median/range. For
p99 disturbance, an exploratory directional signal requires all five paired
differences to have the same sign (one-sided exact sign probability 1/32 under
exchangeability). It is not a confirmatory significance test: deterministic
interleaving, uncontrolled cache/background activity and three metric families
limit inference. Show every pair, including failures. No automatic extra rounds.
Successful evidence reproduction is independent of the all-frame hard budget.

## New held-out policy comparison

After the overhead readout, use fresh discovery, calibration and training
processes. Training route definitions remain train-a=[0,1,1,2,1] and
train-b=[0,1,2,1,1]; none of their old measurements are reused. Freeze the startup
budget and selection before held-out launches. Preserve the existing rule:
required measured cost + half the sum of optional measured costs.

New routes: next-held-a=[0,3,1,2,3], next-held-b=[0,2,3,1,2]. Three independent
processes per route/arm, 18 total; route outer, replicate inner, arm position
rotated by route+replicate. Controls: required-only, hotset, all-at-once. Each
uses required u0, the same native state catalog, 24 frames per stage, 120 Hz target,
640x360 visible D3D12 window, four workers, and exactly 480 actual render-use
events. Entire 119-sample later-frame interval is retained. Report startup engine
proxy, blocking warmup, subsequent frames, coverage, state work and allocation
deltas per cell, then equal-process paired contrasts within each route.

Reliable OS first-Present is unavailable in the existing fixture: engine render
completion is only an engine proxy. This round explicitly retains that limitation
and makes no first-screen or displayed-cadence benefit claim. Opaque driver PSO
memory is unavailable. Existing budget/correctness gates remain unchanged.
Without a credible, repeatable benefit, retain required-only as the recommendation.

Failures and noisy cells remain evidence. A reproducible code defect may be fixed
and affected runs repeated into fresh directories, with the reason documented;
statistical failure must not trigger more sampling.
