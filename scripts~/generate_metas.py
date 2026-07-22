#!/usr/bin/env python3
"""Generate missing Unity .meta files for UPM package contents.

Unity treats Package Manager installs (git/registry/tarball) as immutable and will not
create .meta files itself. Every imported folder/file must ship with a stable GUID meta.
"""
from __future__ import annotations

import uuid
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

# Unity ignores these package-relative prefixes/suffixes.
IGNORED_DIR_NAMES = {".git", ".local", ".cursor", "__pycache__"}
IGNORED_FILE_NAMES = {".gitignore", ".gitattributes", ".DS_Store", "Thumbs.db"}


def is_ignored(path: Path) -> bool:
    rel_parts = path.relative_to(ROOT).parts
    for part in rel_parts:
        if part in IGNORED_DIR_NAMES:
            return True
        if part.startswith(".") and part not in {".", ".."}:
            # Dotfiles/dirs (except we allow none at package root for import).
            if path.is_dir() or part.startswith("."):
                return True
        if part.endswith("~"):
            return True
    if path.name in IGNORED_FILE_NAMES:
        return True
    if path.name.endswith(".meta"):
        return True
    if path.suffix.lower() in {".pyc", ".user", ".suo", ".tmp", ".log", ".csproj", ".sln"}:
        return True
    return False


def folder_meta(guid: str) -> str:
    return (
        "fileFormatVersion: 2\n"
        f"guid: {guid}\n"
        "folderAsset: yes\n"
        "DefaultImporter:\n"
        "  externalObjects: {}\n"
        "  userData: \n"
        "  assetBundleName: \n"
        "  assetBundleVariant: \n"
    )


def default_meta(guid: str) -> str:
    return (
        "fileFormatVersion: 2\n"
        f"guid: {guid}\n"
        "DefaultImporter:\n"
        "  externalObjects: {}\n"
        "  userData: \n"
        "  assetBundleName: \n"
        "  assetBundleVariant: \n"
    )


def mono_meta(guid: str) -> str:
    return (
        "fileFormatVersion: 2\n"
        f"guid: {guid}\n"
        "MonoImporter:\n"
        "  externalObjects: {}\n"
        "  serializedVersion: 2\n"
        "  defaultReferences: []\n"
        "  executionOrder: 0\n"
        "  icon: {instanceID: 0}\n"
        "  userData: \n"
        "  assetBundleName: \n"
        "  assetBundleVariant: \n"
    )


def asmdef_meta(guid: str) -> str:
    return (
        "fileFormatVersion: 2\n"
        f"guid: {guid}\n"
        "AssemblyDefinitionImporter:\n"
        "  externalObjects: {}\n"
        "  userData: \n"
        "  assetBundleName: \n"
        "  assetBundleVariant: \n"
    )


def text_meta(guid: str) -> str:
    return (
        "fileFormatVersion: 2\n"
        f"guid: {guid}\n"
        "TextScriptImporter:\n"
        "  externalObjects: {}\n"
        "  userData: \n"
        "  assetBundleName: \n"
        "  assetBundleVariant: \n"
    )


def meta_for(path: Path, guid: str) -> str:
    if path.is_dir():
        return folder_meta(guid)
    suffix = path.suffix.lower()
    if suffix == ".cs":
        return mono_meta(guid)
    if suffix == ".asmdef":
        return asmdef_meta(guid)
    if suffix in {".json", ".txt", ".md", ".xml", ".csv", ".html", ".css", ".js"}:
        return text_meta(guid)
    return default_meta(guid)


def main() -> int:
    created = 0
    skipped = 0
    for path in sorted(ROOT.rglob("*"), key=lambda p: str(p).lower()):
        if is_ignored(path):
            continue
        if path == ROOT:
            continue
        meta_path = path.with_name(path.name + ".meta")
        if meta_path.is_file():
            skipped += 1
            continue
        guid = uuid.uuid4().hex
        meta_path.write_text(meta_for(path, guid), encoding="utf-8", newline="\n")
        created += 1
        print(f"created {meta_path.relative_to(ROOT)}")

    print(f"done: created={created} existing={skipped}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
