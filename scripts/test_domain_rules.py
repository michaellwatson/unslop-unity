#!/usr/bin/env python3
"""Headless domain-rule checks: forbidden extensions, path traversal, redaction."""
from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
errors: list[str] = []

FORBIDDEN_EXTENSIONS = {
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

TRAVERSAL_CASES = [
    "../evil.fbx",
    "..\\evil.fbx",
    "/abs/path.fbx",
    "C:/abs/path.fbx",
    "textures/../../secret.bin",
    "foo\x00bar.png",
]

SAFE_CASES = [
    "model.fbx",
    "textures/basecolor.png",
    "materials.json",
    "asset.json",
    "preview.png",
]

REDACT_PATTERN = re.compile(
    r"(usk_[A-Za-z0-9]+)|(Bearer\s+[A-Za-z0-9\-._~+/]+=*)|"
    r"(https?://[^\s\"']*(X-Amz-|Signature=|token=)[^\s\"']*)",
    re.IGNORECASE,
)


def check(cond: bool, message: str) -> None:
    if not cond:
        errors.append(message)


def extension_forbidden(path: str) -> bool:
    ext = Path(path).suffix.lower()
    return ext in FORBIDDEN_EXTENSIONS


def path_traversal(path: str) -> bool:
    if not path:
        return True
    normalized = path.replace("\\", "/")
    return (
        ".." in normalized
        or normalized.startswith("/")
        or ":" in normalized
        or "\x00" in path
        or Path(path).is_absolute()
    )


def main() -> int:
    guard = ROOT / "Editor" / "Security" / "PackageContentGuard.cs"
    log = ROOT / "Editor" / "Diagnostics" / "BridgeLog.cs"
    check(guard.is_file(), "missing PackageContentGuard.cs")
    check(log.is_file(), "missing BridgeLog.cs")

    if guard.is_file():
        text = guard.read_text(encoding="utf-8")
        for ext in FORBIDDEN_EXTENSIONS:
            check(ext in text, f"PackageContentGuard missing forbidden ext {ext}")
        check("Path traversal" in text or "ContainsTraversal" in text or ".." in text, "PackageContentGuard missing traversal checks")

    for bad in (
        "payload.cs",
        "hack.dll",
        "x.shader",
        "y.compute",
        "z.asmdef",
        "run.bat",
    ):
        check(extension_forbidden(bad), f"expected forbidden: {bad}")

    for good in SAFE_CASES:
        check(not extension_forbidden(good), f"expected allowed: {good}")

    for bad in TRAVERSAL_CASES:
        check(path_traversal(bad), f"expected traversal reject: {bad}")

    for good in SAFE_CASES:
        check(not path_traversal(good), f"expected safe path: {good}")

    samples = [
        "Authorization: Bearer usk_ABCDEFGHIJKLMNOP1234",
        "https://cdn.example.com/file?X-Amz-Signature=abc123&token=zzz",
        "plain message without secrets",
    ]
    redacted = [REDACT_PATTERN.sub("[REDACTED]", s) for s in samples]
    check("[REDACTED]" in redacted[0], "API key/bearer not redacted")
    check("[REDACTED]" in redacted[1], "signed URL not redacted")
    check(redacted[2] == samples[2], "plain text should not redact")

    if log.is_file():
        log_text = log.read_text(encoding="utf-8")
        check("usk_" in log_text and "REDACTED" in log_text, "BridgeLog should define usk_/redaction behavior")

    if errors:
        print("FAIL: domain rules")
        for e in errors:
            print(" -", e)
        return 1

    print("PASS: domain rules")
    return 0


if __name__ == "__main__":
    sys.exit(main())
