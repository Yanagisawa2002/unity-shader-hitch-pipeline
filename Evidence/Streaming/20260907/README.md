# Addressables streaming correctness evidence

Validated implementation: `92f8e357378000841ac3689a46f8f156e511b7e5`.
The only uncommitted file during capture was integration documentation; this
directory preserves the original provenance without rewriting that status.

- Engine-neutral smoke: **127 assertions passed**, existing core smoke passed.
- Actual Unity 6000.5.2f1 Development Player, Addressables **2.10.3**, Windows D3D12,
  AMD Radeon AI PRO R9700, driver **32.0.31041.1004** (raw Player log).
- Player build GUID: `482ea06cdb844e8f9200755bcdf7edd6`.
- Both final frozen Player revision captures contain **3 graphics states** and
  `nativeCollectionValidated: true`. Each bundled native collection and the
  independent live capture have identical full variant/state unions.
- Runtime smoke: **70 checks passed**, **7 submitted states in 3 batches**.
  Two overlapping owners retain all **4 prefab/GSC handles** through unload until
  the native fence; final retained handle count is zero. Reload, real material
  revision, late load cancellation, bad identity, bad state count, bad hash and
  missing Addressables key checks pass.
- `nativeCollectionValidated` is a trace-mode field; the smoke receipt's default
  `false` does not override the two trace validation records that smoke verifies.

`provenance.json` hashes all raw final artifacts and **265 Player files**, including
managed code, native libraries and actual shipped bundles/catalog. Raw logs and
collections are retained at the evidence path specified there. This directory
contains unmodified small receipts; `retained-failures.json` preserves paths and
hashes for the earlier failed attempts instead of hiding them.

The initial capture was used to import native `.graphicsstate` assets into an
Addressables group sharing the explicitly assigned shader bundle. The final
Player was then independently traced and validated **without another rebuild**.
Old build identity was never relabeled as the current build. Warmup ran in a
separate process from the final validation trace.

Run after integration:

```powershell
& ./Tools/Invoke-PsoAddressablesSmoke.ps1 `
  -SerializationScript '<control>/Invoke-SerializedValidation.ps1' `
  -EvidenceRoot '<repo>/Evidence/Local/addressables-integrated'
```

This is bounded correctness evidence, not performance, cache-cold, first-present
or OS-capture evidence. Large-catalog O(N²) exact interning cost remains unmeasured.
Strict `PsoBuildIdentityCapture.DeclareBuildInputs` and loaded `PsoCompatibility`
bridges require the compatibility worker's merge; the local fixture used its
explicit exact-build/bundle/full-native-state validation policy. Integrated
compatibility and the final formal R9700/system matrix remain required gates.
