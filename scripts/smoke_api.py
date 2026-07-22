#!/usr/bin/env python3
"""Optional live smoke test against Unslop /api/v1 (Latch project).

Requires env UNSLOP_API_KEY. Does not hardcode secrets.
Uses curl.exe because Cloudflare blocks default Python-urllib signatures.
Exit 0 on success or when the key is unset (skipped).
"""
from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys

DEFAULT_BASE = "https://unsloplabs.com/api/v1"
LATCH_PROJECT_ID = "c64b0622-074d-440b-96b7-fc604d2caec6"


def get_json(base: str, path: str, api_key: str) -> dict:
    curl = shutil.which("curl.exe") or shutil.which("curl")
    if not curl:
        raise RuntimeError("curl is required for smoke_api on this host")
    url = base.rstrip("/") + path
    proc = subprocess.run(
        [
            curl,
            "-sS",
            "-A",
            "UnslopUnityBridgeSmoke/1.0",
            "-H",
            f"Authorization: Bearer {api_key}",
            "-H",
            "Accept: application/json",
            "-H",
            "X-Correlation-ID: smoke-api-curl",
            url,
        ],
        capture_output=True,
        text=True,
        encoding="utf-8",
        check=False,
    )
    if proc.returncode != 0:
        raise RuntimeError(proc.stderr.strip() or f"curl failed ({proc.returncode})")
    body = proc.stdout
    if body.lstrip().startswith("<!") or '"cloudflare_error"' in body:
        raise RuntimeError(f"blocked or non-JSON response: {body[:300]}")
    return json.loads(body)


def main() -> int:
    api_key = os.environ.get("UNSLOP_API_KEY", "").strip()
    if not api_key:
        print("SKIP: UNSLOP_API_KEY not set")
        return 0
    if not api_key.startswith("usk_"):
        print("FAIL: UNSLOP_API_KEY must start with usk_")
        return 1

    base = os.environ.get("UNSLOP_API_BASE", DEFAULT_BASE).strip() or DEFAULT_BASE
    try:
        projects = get_json(base, "/projects", api_key)
        project_rows = projects.get("data") or []
        print(f"projects: {len(project_rows)}")
        latch = next(
            (p for p in project_rows if p.get("project_id") == LATCH_PROJECT_ID or p.get("name") == "Latch"),
            None,
        )
        project_id = latch["project_id"] if latch else LATCH_PROJECT_ID
        print(f"project: {project_id}")
        assets = get_json(base, f"/projects/{project_id}/assets", api_key)
        asset_rows = assets.get("data") or []
        print(f"assets: {len(asset_rows)}")
        for asset in asset_rows:
            print(f" - {asset.get('display_name')} recommended={asset.get('recommended_version_id')}")
            versions = get_json(base, f"/assets/{asset['asset_id']}/versions", api_key)
            print(f"   versions={len(versions.get('data') or [])}")
        if not asset_rows:
            print("FAIL: expected Latch assets")
            return 1
        print("PASS: smoke_api")
        return 0
    except Exception as exc:  # noqa: BLE001
        print(f"FAIL: {exc}")
        return 1


if __name__ == "__main__":
    sys.exit(main())
