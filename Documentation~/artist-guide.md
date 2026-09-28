# Unslop Unity Asset Bridge — Artist Guide

## What artists own

| Layer | Owned by | Notes |
|---|---|---|
| Canonical physical size | Project / Unslop physical-spec | Set via **Set Canonical Scale** in Unity or artist tools online |
| Visual correction | Local scene / prefab `VisualCorrection` | Temporary engine-side scale; reset after canonical write |
| Managed materials | Bridge (regenerated from `materials.json`) | Edit carefully — mark **Keep local** if you customise |
| Local override materials | You | Never overwritten on update/rollback |
| UserContent hierarchy | You | Preserved across updates when hierarchy-compatible |

## Publishing expectations

Artists can publish FBX + textures + `materials.json` + manifests through Unslop (web / pipeline), **or** from Unity via **Unslop → Asset Bridge → Publish** (exports a neutral static-mesh package with `pipeline_origin: unity_prefab_export`). For Unity consumers:

- Prefer metres, Y-up, bottom-centre pivot.
- Declare hierarchy / material slot compatibility on new versions.
- Withdrawn versions can still be rolled back project-locally; they should not be recommended.
- Do not bake temporary `VisualCorrection` into exports — use **Set Canonical Scale** / the server-side scale-bake pipeline for that.

## Canonical scale workflow

1. Place the wrapper in a clean scene (identity parent scale).
2. Adjust **VisualCorrection** until the object matches real-world size.
3. **Set Canonical Scale** — writes dimensions to the physical-spec revision and resets visual correction to `1,1,1` so scale does not compound.
4. If you see HTTP 412, someone else updated the physical spec — refresh the ETag and retry; Unity will not overwrite remote state.
5. **Confirm Scale** after installs so other engines see a Unity confirmation badge for that exact version + spec.

## Material review tips

- Prefer **Textures only** when albedo/normal/roughness updated but your shader tweaks should remain.
- Prefer **Keep local** for hero materials with project-specific lookdev.
- Quarantined materials land under `Materials/_Quarantine` when remote slots disappear.

## Wrapper hierarchy (do not rename)

```
UnslopAssetRoot          ← UnslopAssetReference (scene placement lives here)
├── VisualCorrection     ← scale correction only
│   └── Model
│       └── Visual (nested prefab — refreshed on version update)
└── UserContent          ← put sockets / gameplay attachments here
```

Renaming **VisualCorrection** / **UserContent** / **Model** nodes breaks update preservation and scale measurement. The wrapper root and Project folder/prefab names use the asset display name (with a short id suffix on folders).

Version updates refresh the Visual nested content **in place**. Scene instances keep their root transform / parenting; you should not need to re-place the asset after install.

When `pipeline_origin` is **`canonical_scale_bake`**, VisualCorrection is reset to `1,1,1` automatically (scale is in the mesh). Scene instances keep their floor contact point so they do not jump.

## Debug logging (Meshy vs artist transforms)

Enable **Debug logging** on **Unslop → Asset Bridge → Connect** (or **Project Settings → Unslop**). The bridge appends a session log at `Library/Unslop/Diagnostics/debug-session.log` covering install/update activity, with hierarchy **scale and rotation** deltas between the previously installed model (often Meshy) and the incoming version (often artist). Use **Upload Debug Log** / **Pull Latest Debug Log** against the bound project (`POST|GET /api/v1/projects/{project}/diagnostics`); installs/updates also auto-flush while debug is on.
