# Unslop Unity Asset Bridge — User Guide

## Install the package

1. Unity 6.0+ project (URP recommended for automatic materials).
2. Package Manager → **Add package from git URL** (`https://github.com/unslop/unslop-unity.git`) or **Add package from disk** (repo `package.json`). Do not open this repo as the Unity project root.
3. Open **Unslop → Asset Bridge**.

## Connect and bind

1. On **Connect**, paste a Bridge API key (`usk_…`). Keys are stored under `Library/Unslop/Auth` only — never in the project, lock file, or logs.
2. **Test Connection**, then select a project to bind.
3. If a key is revoked, the UI shows a recoverable auth state — enter a new key without losing installed assets.

## Browse and install

1. Open **Browse**, refresh assets, select an asset and published version.
2. Click **Install Selected Version**.
3. The bridge downloads with hash verification, stages under `Assets/Unslop/__Staging`, generates URP materials, and builds a stable wrapper prefab under `Assets/Unslop/Installed/<assetId>/Prefabs/`.
4. Place `Asset.prefab` in your scene. References survive Editor reload via preserved GUIDs.

## Check updates (staged acceptance)

1. Click **Check Updates** on Browse (or use Installed workflows).
2. Updates download into **staging** and produce a diff — they are **not** applied silently.
3. Review geometry / material / bounds changes, then **Accept** or **Discard**.
4. Incomplete transactions are recovered on Editor load (restore prior install).

## Materials

When a managed material was edited locally, the Material Review UI offers:

- Accept remote
- Keep local (local_override)
- Textures only / Properties only
- Remap to a project material

Local overrides are never overwritten on update or rollback unless you choose Accept remote.

## Scale

1. Select a wrapper instance in the scene.
2. **Set Canonical Scale** measures renderer bounds and writes a physical-spec revision (`If-Match` ETag; HTTP 412 means refresh and retry).
3. **Confirm Scale** submits measurement evidence and shows an online confirmation badge.

## Rollback, pins, drift

- Rollback stages a historical version through the same accept/discard pipeline. It does **not** change the global recommended version.
- Pins are written locally in `Unslop.lock.json` and online via the API.
- Drift diagnostics can restore declared state, adopt local GUIDs, reconnect wrappers, or export a redacted support zip under `Library/Unslop/Diagnostics`.

## Feature flags

EditorPrefs keys under `Unslop.Feature.*`:

- `unity_bridge_enabled`
- `unity_bridge_updates_enabled`
- `unity_bridge_material_resolution_v1`
- `unity_bridge_canonical_scale_write`
- `unity_bridge_scale_confirmation`
- `unity_bridge_rollback`
