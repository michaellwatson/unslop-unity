#!/usr/bin/env python3
"""Validate fixture manifests with the same MVP rules as ManifestValidator."""
from __future__ import annotations

import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ALLOWED = {"model", "texture", "preview", "material_manifest", "asset_manifest"}
FORBIDDEN_EXT = {".cs", ".dll", ".so", ".dylib", ".exe", ".bat", ".cmd", ".ps1", ".js", ".shader", ".compute", ".cginc", ".hlsl", ".asmdef"}


def validate_manifest(data: dict) -> list[str]:
    errors: list[str] = []
    for field in ("asset_id", "asset_version_id", "display_name", "content_kind", "minimum_bridge_version"):
        if not data.get(field):
            errors.append(f"missing {field}")
    model = data.get("model") or {}
    if (model.get("format") or "").lower() != "fbx" and not str(model.get("relative_path", "")).lower().endswith(".fbx"):
        errors.append("FBX model required")
    files = data.get("files") or []
    models = [f for f in files if (f.get("role") or "").lower() == "model"]
    if len(models) != 1:
        errors.append("exactly one model file required")
    for f in files:
        role = f.get("role")
        if role not in ALLOWED:
            errors.append(f"bad role {role}")
        rel = f.get("relative_path") or ""
        if ".." in rel or rel.startswith("/") or ":" in rel:
            errors.append(f"bad path {rel}")
        ext = Path(rel).suffix.lower()
        if ext in FORBIDDEN_EXT:
            errors.append(f"forbidden {rel}")
        if not f.get("sha256") or int(f.get("byte_length") or 0) <= 0:
            errors.append(f"hash/size missing for {rel}")
    return errors


def main() -> int:
    manifest = json.loads((ROOT / "Tests/Fixtures/sample_asset_manifest.json").read_text(encoding="utf-8"))
    materials = json.loads((ROOT / "Tests/Fixtures/sample_materials.json").read_text(encoding="utf-8"))
    errors = validate_manifest(manifest)
    mat_ids = {m["material_id"] for m in materials.get("materials") or []}
    for slot in materials.get("slots") or []:
        if slot.get("material_id") not in mat_ids:
            errors.append(f"slot bad material {slot}")
    # negative case
    bad = json.loads(json.dumps(manifest))
    bad["files"].append({"file_id": "x", "role": "model", "relative_path": "evil.cs", "byte_length": 1, "sha256": "a"})
    neg = validate_manifest(bad)
    if not neg:
        errors.append("expected negative fixture to fail")
    if errors:
        print("FAIL: manifest fixtures")
        for e in errors:
            print(" -", e)
        return 1
    print("PASS: manifest fixtures")
    return 0


if __name__ == "__main__":
    sys.exit(main())
