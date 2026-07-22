# Supported matrix (MVP)

| Area | Supported | Notes |
|---|---|---|
| Unity | 6000.0+ | Package `"unity": "6000.0"` |
| Render pipeline | URP (soft) | Detected via Lit shader / `UNSLOP_HAS_URP`; not a hard package dependency. HDRP adapter is a placeholder |
| Content | Static FBX + textures + `materials.json` | No scripts, DLLs, shaders, or compute in packages |
| Texture formats | png, jpg, jpeg, tga, tif/tiff, exr | Colour / normal / roughness roles |
| Auth | Bridge API key `usk_…` | Stored in `Library/Unslop/Auth` only |
| API | `https://unsloplabs.com/api/v1` | Correlation ID, Idempotency-Key, If-Match |
| Install | Hash-verified download + staging + wrapper | GUID-stable friendly-named wrapper / visual prefabs |
| Updates | Staged + explicit accept/discard | No silent install |
| Materials | Managed + local_override resolutions | Feature flag `unity_bridge_material_resolution_v1` |
| Scale | Canonical write + confirmation | Feature flags for write / confirm |
| Rollback | Historical version via same pipeline | Pins local + online; recommended version unchanged |
| OS (first workstation) | Windows | macOS/Linux expected later |

## Explicitly out of scope (MVP)

- OAuth/PKCE (later behind same client interface)
- Scoped registry publishing
- Runtime networking / live sync
- Animated / skinned character pipelines
- Automatic HDRP material generation
