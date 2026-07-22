#!/usr/bin/env python3
"""Headless structural validation for com.unslop.unity-bridge (no Unity required)."""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
errors: list[str] = []
# Real Bridge keys look like usk_<long alnum>; docs may mention usk_ as a prefix.
SECRET_KEY = re.compile(r"usk_[A-Za-z0-9]{16,}")

IGNORED_DIR_NAMES = {".git", ".local", ".cursor", "__pycache__"}
IGNORED_FILE_NAMES = {".gitignore", ".gitattributes", ".DS_Store", "Thumbs.db"}


def require(path: Path, kind: str = "file") -> None:
    if kind == "file" and not path.is_file():
        errors.append(f"missing file: {path.relative_to(ROOT)}")
    elif kind == "dir" and not path.is_dir():
        errors.append(f"missing dir: {path.relative_to(ROOT)}")


def is_unity_ignored(path: Path) -> bool:
    for part in path.relative_to(ROOT).parts:
        if part in IGNORED_DIR_NAMES:
            return True
        if part.endswith("~"):
            return True
        if part.startswith(".") and part not in {".", ".."}:
            return True
    if path.name in IGNORED_FILE_NAMES:
        return True
    if path.name.endswith(".meta"):
        return True
    if path.suffix.lower() in {".pyc", ".user", ".suo", ".tmp", ".log", ".csproj", ".sln"}:
        return True
    return False


def validate_metas() -> None:
    """Every Unity-imported path under the package must ship a .meta (immutable UPM)."""
    for path in sorted(ROOT.rglob("*"), key=lambda p: str(p).lower()):
        if path == ROOT or is_unity_ignored(path):
            continue
        meta = path.with_name(path.name + ".meta")
        if not meta.is_file():
            errors.append(f"missing Unity .meta: {path.relative_to(ROOT)}")
            continue
        text = meta.read_text(encoding="utf-8", errors="ignore")
        if "guid:" not in text:
            errors.append(f"invalid Unity .meta (no guid): {meta.relative_to(ROOT)}")


def main() -> int:
    require(ROOT / "package.json")
    require(ROOT / "README.md")
    require(ROOT / "CHANGELOG.md")
    require(ROOT / "LICENSE.md")
    require(ROOT / "Runtime" / "Unslop.UnityBridge.Runtime.asmdef")
    require(ROOT / "Editor" / "Unslop.UnityBridge.Editor.asmdef")
    require(ROOT / "Runtime" / "UnslopAssetReference.cs")
    require(ROOT / "Runtime" / "UnslopSocket.cs")
    require(ROOT / "Editor" / "Bootstrap" / "UnslopBootstrap.cs")
    require(ROOT / "Editor" / "Bootstrap" / "PackageInfo.cs")
    require(ROOT / "Editor" / "Settings" / "UnslopProjectSettings.cs")
    require(ROOT / "Editor" / "Api" / "UnslopApiClient.cs")
    require(ROOT / "Editor" / "Api" / "IUnslopApiClient.cs")
    require(ROOT / "Editor" / "UI" / "UnslopBridgeWindow.cs")
    require(ROOT / "Editor" / "Authentication" / "ProjectBindingService.cs")
    require(ROOT / "Editor" / "Services" / "BridgeServices.cs")
    require(ROOT / "Editor" / "Manifests" / "ManifestValidator.cs")
    require(ROOT / "Editor" / "Downloads" / "DownloadManager.cs")
    require(ROOT / "Editor" / "Install" / "AssetInstallService.cs")
    require(ROOT / "Editor" / "Materials" / "UrpMaterialAdapter.cs")
    require(ROOT / "Editor" / "Transactions" / "AssetTransitionCoordinator.cs")
    require(ROOT / "Editor" / "Scale" / "ScaleMeasurementService.cs")
    require(ROOT / "Editor" / "Browser" / "RollbackService.cs")
    require(ROOT / "Documentation~" / "user-guide.md")
    require(ROOT / "Tests" / "Editor" / "Unslop.UnityBridge.Editor.Tests.asmdef")
    require(ROOT / "Tests" / "Fixtures" / "sample_asset_manifest.json")
    require(ROOT / "Documentation~", "dir")
    require(ROOT / "scripts~", "dir")

    # scripts~ must stay Unity-ignored (trailing ~)
    if (ROOT / "scripts").is_dir():
        errors.append("scripts/ must be renamed to scripts~ so Unity does not import it")

    pkg = json.loads((ROOT / "package.json").read_text(encoding="utf-8"))
    if pkg.get("name") != "com.unslop.unity-bridge":
        errors.append(f"package.json name expected com.unslop.unity-bridge, got {pkg.get('name')!r}")
    if pkg.get("unity") != "6000.0":
        errors.append(f"package.json unity expected 6000.0, got {pkg.get('unity')!r}")
    deps = pkg.get("dependencies") or {}
    if "com.unity.nuget.newtonsoft-json" not in deps:
        errors.append("package.json missing Newtonsoft.Json dependency")
    # URP is optional at package resolve time; materials use Shader.Find when present.
    if "com.unity.render-pipelines.universal" in deps:
        errors.append(
            "package.json must not hard-pin com.unity.render-pipelines.universal "
            "(use versionDefines / Shader.Find instead)"
        )

    runtime_asm = json.loads((ROOT / "Runtime" / "Unslop.UnityBridge.Runtime.asmdef").read_text(encoding="utf-8"))
    editor_asm = json.loads((ROOT / "Editor" / "Unslop.UnityBridge.Editor.asmdef").read_text(encoding="utf-8"))
    if runtime_asm.get("name") != "Unslop.UnityBridge.Runtime":
        errors.append("runtime asmdef name mismatch")
    if editor_asm.get("name") != "Unslop.UnityBridge.Editor":
        errors.append("editor asmdef name mismatch")
    if "Editor" not in (editor_asm.get("includePlatforms") or []):
        errors.append("editor asmdef must includePlatforms Editor")
    urp_refs = {
        "Unity.RenderPipelines.Universal.Runtime",
        "Unity.RenderPipelines.Core.Runtime",
    }
    hard_urp = urp_refs.intersection(editor_asm.get("references") or [])
    if hard_urp:
        errors.append(f"editor asmdef must not hard-reference URP assemblies: {sorted(hard_urp)}")
    version_defines = editor_asm.get("versionDefines") or []
    if not any(
        isinstance(vd, dict)
        and vd.get("name") == "com.unity.render-pipelines.universal"
        and vd.get("define") == "UNSLOP_HAS_URP"
        for vd in version_defines
    ):
        errors.append("editor asmdef missing URP versionDefines entry for UNSLOP_HAS_URP")

    settings_cs = (ROOT / "Editor" / "Settings" / "UnslopProjectSettings.cs").read_text(encoding="utf-8")
    if "Assets/Unslop/Settings/UnslopProjectSettings.asset" not in settings_cs:
        errors.append("UnslopProjectSettings must write under Assets/Unslop/Settings/")
    bootstrap_cs = (ROOT / "Editor" / "Bootstrap" / "UnslopBootstrap.cs").read_text(encoding="utf-8")
    if "UnslopProjectSettings.EnsureExists" not in bootstrap_cs:
        errors.append("UnslopBootstrap must ensure project settings under Assets/Unslop")

    gitignore = (ROOT / ".gitignore").read_text(encoding="utf-8")
    if ".local" not in gitignore:
        errors.append(".gitignore must ignore .local")
    if ".cursor" not in gitignore:
        errors.append(".gitignore must ignore .cursor")

    validate_metas()

    # Ensure no secrets in tracked Unity-imported / docs sources (skip scripts~ tooling).
    for path in ROOT.rglob("*"):
        if not path.is_file():
            continue
        rel = str(path.relative_to(ROOT)).replace("\\", "/")
        if rel.startswith(".local/") or rel.startswith(".git/") or "/.local/" in rel:
            continue
        if rel.startswith("scripts~/") or "/scripts~/" in rel:
            continue
        if path.suffix.lower() not in {".cs", ".md", ".json", ".asmdef", ".txt", ".yaml", ".yml"}:
            continue
        text = path.read_text(encoding="utf-8", errors="ignore")
        if SECRET_KEY.search(text):
            errors.append(f"possible API key material in {rel}")

    if errors:
        print("FAIL: package validation")
        for e in errors:
            print(" -", e)
        return 1

    print("PASS: package validation")
    return 0


if __name__ == "__main__":
    sys.exit(main())
