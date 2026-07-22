# Unslop Unity Asset Bridge — User Guide

## Install the package

1. Unity 6.0+ project (URP recommended for automatic materials).
2. Package Manager → **Add package from git URL** (`https://github.com/unslop/unslop-unity.git`) or **Add package from disk** (repo `package.json`). Do not open this repo as the Unity project root.
3. Open **Unslop → Asset Bridge**.

## Connect and bind

1. On **Connect**, paste a Bridge API key (`usk_…`).
2. Click **Test Connection** (this saves the key into `Library/Unslop/Auth` automatically, then lists projects). You can also use **Save Key** first.
3. Select a project in the list to bind it.
4. If a key is revoked, the UI shows a recoverable auth state — paste a new key and Test Connection again without losing installed assets.

## Browse and install

1. Open **Browse** → **Refresh Assets**, select an asset and a published version.
2. Click **Install Selected Version**.
3. The bridge downloads with hash verification, stages under `Assets/Unslop/__Staging`, generates URP materials, and builds a stable wrapper prefab under `Assets/Unslop/Installed/<DisplayName>_<shortId>/Prefabs/<DisplayName>.prefab`.
4. Drag the **wrapper prefab** (named after the asset, not the raw FBX) into your scene. Keep the `UnslopAssetReference` component — scale/update tools look for it.

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

1. Drag the wrapper prefab from `Assets/Unslop/Installed/<DisplayName>_<shortId>/Prefabs/` into the scene (or select that instance / prefab in the Hierarchy / Project).
2. With the wrapper selected, click **Set Canonical Scale** — measures renderer bounds and writes a physical-spec revision online (`If-Match` ETag; HTTP 412 means refresh and retry).
3. Then click **Confirm Scale** — submits Unity measurement evidence and shows an online confirmation badge.

You must have a scene/project selection that carries `UnslopAssetReference` (the wrapper), not just the FBX mesh.

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
