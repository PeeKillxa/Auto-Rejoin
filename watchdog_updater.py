#!/usr/bin/env python3
"""Fetch a verified code-only update for Emu_Watchdog from Auto-Rejoin."""

import concurrent.futures
import hashlib
import json
import os
import re
import sys
import tempfile
import urllib.request
from pathlib import Path


BASE_URL = "https://raw.githubusercontent.com/PeeKillxa/Auto-Rejoin/main"
ALLOWED_FILES = frozenset(
    {
        "watchdog.py",
        "mumu_controller.py",
        "adb_manager.py",
        "ldconsole.py",
        "autorun_manager.py",
        "rokid_services.py",
        "watchdog_updater.py",
    }
)
MAX_MANIFEST_BYTES = 64 * 1024
MAX_FILE_BYTES = 4 * 1024 * 1024
SHA256_RE = re.compile(r"^[0-9a-f]{64}$")


def fetch_bytes(url, limit):
    request = urllib.request.Request(
        url,
        headers={"User-Agent": "Auto-Rejoin-Watchdog-Updater/1.0"},
    )
    with urllib.request.urlopen(request, timeout=12) as response:
        data = response.read(limit + 1)
    if len(data) > limit:
        raise ValueError("download exceeds the allowed size")
    return data


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def load_manifest():
    data = fetch_bytes(BASE_URL + "/manifest.json", MAX_MANIFEST_BYTES)
    manifest = json.loads(data.decode("utf-8-sig"))
    files = manifest.get("files") if isinstance(manifest, dict) else None
    if not isinstance(files, dict) or set(files) != ALLOWED_FILES:
        raise ValueError("manifest file list does not match the updater allowlist")
    for name, digest in files.items():
        if Path(name).name != name or not isinstance(digest, str) or not SHA256_RE.fullmatch(digest):
            raise ValueError("manifest contains an invalid filename or SHA-256")
    return manifest


def download_file(name, expected_hash):
    url = BASE_URL + "/" + name + "?v=" + expected_hash
    data = fetch_bytes(url, MAX_FILE_BYTES)
    if sha256(data) != expected_hash:
        raise ValueError("SHA-256 mismatch for " + name)
    return name, data


def write_temp(directory, name, data):
    fd, path = tempfile.mkstemp(prefix=".watchdog-update-", suffix=".tmp", dir=str(directory))
    try:
        with os.fdopen(fd, "wb") as stream:
            stream.write(data)
        return Path(path)
    except Exception:
        try:
            os.unlink(path)
        except OSError:
            pass
        raise


def apply_updates(directory, updates):
    staged = {}
    previous = {}
    applied = []
    try:
        for name, data in updates.items():
            destination = directory / name
            previous[name] = destination.read_bytes() if destination.exists() else None
            staged[name] = write_temp(directory, name, data)

        for name in updates:
            os.replace(str(staged[name]), str(directory / name))
            applied.append(name)
    except Exception:
        for name in reversed(applied):
            destination = directory / name
            old_data = previous[name]
            try:
                if old_data is None:
                    destination.unlink(missing_ok=True)
                else:
                    restore_path = write_temp(directory, name, old_data)
                    os.replace(str(restore_path), str(destination))
            except OSError:
                pass
        raise
    finally:
        for path in staged.values():
            try:
                path.unlink(missing_ok=True)
            except OSError:
                pass


def main():
    directory = Path(__file__).resolve().parent
    try:
        manifest = load_manifest()
        changed = []
        for name, expected_hash in manifest["files"].items():
            try:
                current_hash = sha256((directory / name).read_bytes())
            except OSError:
                current_hash = None
            if current_hash != expected_hash:
                changed.append((name, expected_hash))

        if not changed:
            print("[UPDATE] Watchdog is already up to date.")
            return 0

        with concurrent.futures.ThreadPoolExecutor(max_workers=7) as pool:
            updates = dict(
                pool.map(lambda item: download_file(item[0], item[1]), changed)
            )

        apply_updates(directory, updates)
        print("[UPDATE] Updated: " + ", ".join(sorted(updates)))
        return 0
    except Exception as exc:
        # Updates are best-effort: keep the local version usable if GitHub is offline.
        print("[UPDATE] Could not fetch an update; starting the installed version. " + str(exc))
        return 0


if __name__ == "__main__":
    sys.exit(main())
