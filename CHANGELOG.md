# Changelog

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
