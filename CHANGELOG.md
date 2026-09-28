# Changelog

## [1.2.0] - 2026-09-28

### Added
- HDRP/Lit material generation from `materials.json`. Metallic, ambient occlusion, and roughness pack into an HDRP mask map (smoothness in alpha) and assign onto the installed prefab.
- Active render pipeline detection, so an HDRP project no longer requests the URP adapter.

## [1.1.0] - 2026-08-19

### Added
- **Publish** tab: select a Unity prefab and create a catalog asset / new Bridge version (`pipeline_origin: unity_prefab_export`); after publish, automatically installs and refreshes the local prefab / scene instances
- Bridge API client methods for create-asset, draft, multipart file upload, submit, and publish
- Soft dependency on Unity FBX Exporter (`UNSLOP_HAS_FBX_EXPORTER`) when the mesh is not already an `.fbx` asset
- Feature flag `unity_bridge_publish_from_prefab` (default on)
- **Debug logging** checkbox (Connect tab / Project Settings): session file under `Library/Unslop/Diagnostics/debug-session.log` with verbose install/update traces, including Meshy vs artist scale and rotation hierarchy diffs; **Upload Debug Log** / **Pull Latest Debug Log** via project-scoped `POST|GET /projects/{project}/diagnostics` (auto-flush on install / staged update)

## [1.0.0] - 2026-07-22

### Added
- Initial Unslop Unity Asset Bridge UPM package for Unity 6+
- Bridge API key authentication and project binding
- Catalogue browse of Unslop assets and versions
- Hash-verified download and staged install of static FBX packages
- URP material generation from `materials.json`
- Stable wrapper prefab hierarchy with GUID preservation
- Staged update review, transactional accept/discard/recovery
- Material ownership modes and conflict resolutions
- Bidirectional canonical scale and Unity scale confirmation
- Rollback, pins, drift diagnostics, and support export

### Fixed
- Ship Unity `.meta` files so git/Package Manager installs are not treated as incomplete immutable packages
- Drop hard URP package/asmdef dependency; detect URP via `Shader.Find` and optional `UNSLOP_HAS_URP` versionDefine
- Keep helper Python tooling under `scripts~` so Unity does not import it
