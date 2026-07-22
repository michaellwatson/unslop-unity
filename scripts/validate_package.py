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


def require(path: Path, kind: str = "file") -> None:
    if kind == "file" and not path.is_file():
        errors.append(f"missing file: {path.relative_to(ROOT)}")
    elif kind == "dir" and not path.is_dir():
        errors.append(f"missing dir: {path.relative_to(ROOT)}")


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

    pkg = json.loads((ROOT / "package.json").read_text(encoding="utf-8"))
    if pkg.get("name") != "com.unslop.unity-bridge":
        errors.append(f"package.json name expected com.unslop.unity-bridge, got {pkg.get('name')!r}")
    if pkg.get("unity") != "6000.0":
        errors.append(f"package.json unity expected 6000.0, got {pkg.get('unity')!r}")
    deps = pkg.get("dependencies") or {}
    if "com.unity.render-pipelines.universal" not in deps:
        errors.append("package.json missing URP dependency")
    if "com.unity.nuget.newtonsoft-json" not in deps:
        errors.append("package.json missing Newtonsoft.Json dependency")

    runtime_asm = json.loads((ROOT / "Runtime" / "Unslop.UnityBridge.Runtime.asmdef").read_text(encoding="utf-8"))
    editor_asm = json.loads((ROOT / "Editor" / "Unslop.UnityBridge.Editor.asmdef").read_text(encoding="utf-8"))
    if runtime_asm.get("name") != "Unslop.UnityBridge.Runtime":
        errors.append("runtime asmdef name mismatch")
    if editor_asm.get("name") != "Unslop.UnityBridge.Editor":
        errors.append("editor asmdef name mismatch")
    if "Editor" not in (editor_asm.get("includePlatforms") or []):
        errors.append("editor asmdef must includePlatforms Editor")

    gitignore = (ROOT / ".gitignore").read_text(encoding="utf-8")
    if ".local" not in gitignore:
        errors.append(".gitignore must ignore .local")
    if ".cursor" not in gitignore:
        errors.append(".gitignore must ignore .cursor")

    # Ensure no secrets in tracked package sources
    for path in ROOT.rglob("*"):
        if not path.is_file():
            continue
        rel = str(path.relative_to(ROOT)).replace("\\", "/")
        if rel.startswith(".local/") or rel.startswith(".git/") or "/.local/" in rel:
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
