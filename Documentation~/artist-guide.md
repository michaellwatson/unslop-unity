# Unslop Unity Asset Bridge — Artist Guide

## What artists own

| Layer | Owned by | Notes |
|---|---|---|
| Canonical physical size | Project / Unslop physical-spec | Set via **Set Canonical Scale** in Unity or artist tools online |
| Visual correction | Local scene / prefab `VisualCorrection` | Temporary engine-side scale; reset after canonical write |
| Managed materials | Bridge (regenerated from `materials.json`) | Edit carefully — mark **Keep local** if you customise |
| Local override materials | You | Never overwritten on update/rollback |
| UserContent hierarchy | You | Preserved across updates when hierarchy-compatible |

## Publishing expectations (server-side)

Artists publish FBX + textures + `materials.json` + manifests through Unslop (not from this Unity package). For Unity consumers:

- Prefer metres, Y-up, bottom-centre pivot.
- Declare hierarchy / material slot compatibility on new versions.
- Withdrawn versions can still be rolled back project-locally; they should not be recommended.

## Canonical scale workflow

1. Place the wrapper in a clean scene (identity parent scale).
2. Adjust **VisualCorrection** until the object matches real-world size.
3. **Set Canonical Scale** — writes dimensions to the physical-spec revision and resets visual correction to `1,1,1` so scale does not compound.
4. If you see HTTP 412, someone else updated the physical spec — refresh the ETag and retry; Unity will not overwrite remote state.
5. **Confirm Scale** after installs so other engines see a Unity confirmation badge for that exact version + spec.

## Material review tips

- Prefer **Textures only** when albedo/normal/roughness updated but your shader tweaks should remain.
- Prefer **Keep local** for hero hero materials with project-specific lookdev.
- Quarantined materials land under `Materials/_Quarantine` when remote slots disappear.

## Wrapper hierarchy (do not rename)

```
UnslopAssetRoot          ← UnslopAssetReference
├── VisualCorrection     ← scale correction only
│   └── Model
│       └── Visual (nested prefab)
└── UserContent          ← put sockets / gameplay attachments here
```

Renaming these nodes breaks update preservation and scale measurement.
