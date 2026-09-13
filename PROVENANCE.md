# Clean-room provenance

The September 8 native external-scene integration contains independently authored
adapter code, upstream Git/source digests and links to license notices. It does
not include Megacity assets. See `Integrations/MegacityMetroNative/source-lock.json`
and its README for the pinned official source and Unity Companion License notice.
The old controlled reveal and its historical measurements remain separately
labelled. No new external performance results were produced in this repair.

Shader Hitch Pipeline is an independent, engine-tooling project created in a
separate repository. Its implementation is based on public Unity APIs and public
graphics documentation.

The repository must not contain employer project source, scenes, shaders, data,
test fixtures, captures, internal documents, or employer benchmark evidence.
The bundled showcase and its published measurements use synthetic geometry,
procedural materials, and purpose-written shaders. Third-party application
content remains in a separate upstream checkout. Independently authored adapters
based on public APIs and source/license locks may live under `Integrations/`;
they do not convey rights to redistribute upstream assets. Employer integrations
and any private content remain outside this repository and require a separate
rights decision.

Initial implementation target:

- Unity 6000.5.x
- Windows Standalone
- Direct3D 12 graphics-state tracing and prewarming
- `UnityEngine.Rendering.GraphicsStateCollection`
