# Clean-room provenance

Shader Hitch Pipeline is an independent, engine-tooling project created in a
separate repository. Its implementation is based on public Unity APIs and public
graphics documentation.

The repository must not contain employer project source, scenes, shaders, data,
test fixtures, captures, internal documents, or employer benchmark evidence.
The bundled showcase and its published measurements use synthetic geometry,
procedural materials, and purpose-written shaders. Integrations with third-party
or employer projects belong in separate adapter repositories and require an
explicit rights decision.

Initial implementation target:

- Unity 6000.5.x
- Windows Standalone
- Direct3D 12 graphics-state tracing and prewarming
- `UnityEngine.Rendering.GraphicsStateCollection`
