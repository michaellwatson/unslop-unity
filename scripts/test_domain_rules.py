#!/usr/bin/env python3
"""Headless domain-rule tests: forbidden extensions, path traversal, hash normalize, redaction."""
from __future__ import annotations

import re
import sys
from pathlib import Path

FORBIDDEN = {
    ".cs",
    ".dll",
    ".so",
    ".dylib",
    ".exe",
    ".bat",
    ".cmd",
    ".ps1",
    ".js",
    ".shader",
    ".compute",
    ".cginc",
    ".hlsl",
    ".asmdef",
}

ALLOWED = {".fbx", ".png", ".jpg", ".jpeg", ".tga", ".tif", ".tiff", ".exr", ".json", ".txt"}

SECRET_RE = re.compile(
    r"(usk_[A-Za-z0-9]{16,})|(Bearer\s+[A-Za-z0-9\-._~+/]+=*)|(X-Amz-Signature=[A-Za-z0-9]+)",
    re.I,
)


def valid_path(path: str) -> bool:
    if not path or ".." in path or path.startswith("/") or path.startswith("\\") or ":" in path:
        return False
    if Path(path).is_absolute():
        return False
    ext = Path(path).suffix.lower()
    if ext in FORBIDDEN:
        return False
    return ext in ALLOWED


def normalize_hash(value: str) -> str:
    value = (value or "").strip()
    if value.lower().startswith("sha256:"):
        value = value[7:]
    return value.lower()


def hashes_equal(a: str, b: str) -> bool:
    return normalize_hash(a) == normalize_hash(b)


def redact(message: str) -> str:
    return SECRET_RE.sub("[REDACTED]", message or "")


def main() -> int:
    errors: list[str] = []

    # Path traversal
    for bad in ("../evil.fbx", "..\\evil.fbx", "/abs/model.fbx", "C:\\abs\\model.fbx", "foo/../../x.png"):
        if valid_path(bad):
            errors.append(f"traversal allowed: {bad}")

    # Forbidden extensions
    for bad in ("hack.cs", "lib.dll", "x.shader", "y.asmdef", "z.exe"):
        if valid_path(bad):
            errors.append(f"forbidden allowed: {bad}")

    # Allowed content
    for good in ("textures/basecolor.png", "model.fbx", "materials.json", "preview.jpg"):
        if not valid_path(good):
            errors.append(f"valid rejected: {good}")

    # Hash normalize
    if normalize_hash("SHA256:AbCDef") != "abcdef":
        errors.append("hash normalize failed")
    if normalize_hash("sha256:aa") != "aa":
        errors.append("hash prefix strip failed")
    if not hashes_equal("sha256:AA", "aa"):
        errors.append("hash equality failed")
    if hashes_equal("aa", "bb"):
        errors.append("hash inequality failed")

    # Redaction
    sample = (
        "Bearer supersecrettokenvalue123 "
        "https://x?X-Amz-Signature=deadbeef "
        "usk_abcdefghijklmnopqrstuv"
    )
    out = redact(sample)
    if "supersecret" in out or "deadbeef" in out or "usk_abcd" in out:
        errors.append("redaction failed: " + out)
    if "[REDACTED]" not in out:
        errors.append("redaction produced no marker")

    if errors:
        print("FAIL: domain rules")
        for e in errors:
            print(" -", e)
        return 1

    print("PASS: domain rules")
    return 0


if __name__ == "__main__":
    sys.exit(main())
