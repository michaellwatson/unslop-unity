# Unslop Unity Asset Bridge

UPM package `com.unslop.unity-bridge` for Unity 6+. Synchronises Unslop static FBX assets, materials, scale and versions into Editor projects with staged review and transactional acceptance.

## Requirements

- Unity **6000.0** or newer
- **URP or HDRP** for automatic material generation. The active render pipeline is detected at install time (`HDRP/Lit` or `Universal Render Pipeline/Lit`). Neither pipeline is a Package Manager dependency of this package.
- An Unslop Bridge API key (`usk_…`) from your project page

> Install this package into an existing Unity project. Do **not** open this git repository as the Unity project root.

## Install via Unity Package Manager

### From Git URL (recommended)

**Window → Package Manager → + → Add package from git URL…**

```text
https://github.com/unslop/unslop-unity.git
```

Optional pin:

```text
https://github.com/unslop/unslop-unity.git#v1.0.0
```

### From `manifest.json`

```json
{
  "dependencies": {
    "com.unslop.unity-bridge": "https://github.com/unslop/unslop-unity.git"
  }
}
```

### From disk (local clone)

1. Clone this repository.
2. In Unity: **Window → Package Manager → + → Add package from disk…**
3. Select the `package.json` at the root of this repository.

## Quick start

1. Open **Unslop → Asset Bridge**.
2. Paste your Bridge API key (stored under `Library/Unslop/Auth`, never in project assets or the lock file).
3. Select and bind your Unslop project.
4. Browse published assets and install a version into `Assets/Unslop/Installed/`.
5. Place the generated wrapper prefab (named after the asset display name) in a scene.
6. Optional: use the **Publish** tab to create a catalog asset (or new version) from a local prefab.

Declared project state is written to `Unslop.lock.json` at the project root (safe to commit). Machine-local caches and journals live under `Library/Unslop/`.

## API

Live service: [https://unsloplabs.com/api/docs](https://unsloplabs.com/api/docs)  
Base URL default: `https://unsloplabs.com/api/v1`

## Security

- Do not commit Bridge API keys.
- Signed download URLs and tokens are redacted from logs and diagnostics.
- Remote packages may contain models, images, JSON and previews only — scripts, DLLs and shaders are rejected.

## License

MIT — see [LICENSE.md](LICENSE.md).
