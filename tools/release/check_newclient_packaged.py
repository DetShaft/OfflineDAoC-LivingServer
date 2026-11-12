"""Fails when Living Server changes new-client UI/net code without packaging it for INSTALL.

0.37c loads loose scripts from new-client/godot (not a .pck). Living Server ships those fixes through
tools/fork-build/content-patches.json (Ops or File payloads), the same path as quest_markers.gd. Beta 1.1.4
merged friends/LFG/commander into source but never packaged them, so Shift+F / Shift+L did nothing in game.

This check covers source/new-client/godot/game/ui and game/net (the windows and packet client), and since 1.1.13
game/combat and game/perf_governor.gd (spell effects and the performance governor), and from the robe fix
game/fig3_figure.gd (character figures, issue #37). Each file that
differs from the 0.37 release commit BASE must have a content-patches entry whose Fixed hash matches the current
file; brand-new files need Create: true; .cs changes require the MMOEngine.dll File entry.

    python3 tools/release/check_newclient_packaged.py
"""
from __future__ import annotations

import hashlib
import json
import os
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
BASE = "ab4df02a"
SCAN_ROOTS = (
    ROOT / "source/new-client/godot/game/ui",
    ROOT / "source/new-client/godot/game/net",
    ROOT / "source/new-client/godot/game/combat",
    ROOT / "source/new-client/godot/game/perf_governor.gd",
    ROOT / "source/new-client/godot/game/fig3_figure.gd",
)
PATCHES = ROOT / "tools/fork-build/content-patches.json"
DLL_PATH = "new-client/godot/.godot/mono/temp/bin/Debug/MMOEngine.dll"


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def git_show(rel: str) -> bytes | None:
    try:
        return subprocess.check_output(
            ["git", "show", f"{BASE}:source/new-client/godot/{rel}"],
            cwd=ROOT,
            stderr=subprocess.DEVNULL,
        )
    except subprocess.CalledProcessError:
        return None


def base_commit_present() -> bool:
    try:
        subprocess.check_call(
            ["git", "cat-file", "-e", f"{BASE}^{{commit}}"],
            cwd=ROOT,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
        return True
    except (subprocess.CalledProcessError, FileNotFoundError):
        return False


def main() -> int:
    patches = {e["Path"]: e for e in json.loads(PATCHES.read_text(encoding="utf-8"))}
    problems: list[str] = []
    cs_changed = False

    if not base_commit_present():
        print(
            f"error: git commit {BASE} (0.37 release baseline) is not in this clone; "
            "fetch full history (actions/checkout fetch-depth: 0) before running this check",
            file=sys.stderr,
        )
        return 1

    for scan in SCAN_ROOTS:
        for path in [scan] if scan.is_file() else sorted(scan.rglob("*")):
            if not path.is_file() or path.suffix in {".uid", ".import"}:
                continue
            rel = path.relative_to(ROOT / "source/new-client/godot").as_posix()
            current = path.read_bytes()
            old = git_show(rel)
            if old is not None and old == current:
                continue
            if path.suffix == ".cs":
                cs_changed = True
                continue
            game_path = f"new-client/godot/{rel}"
            entry = patches.get(game_path)
            if entry is None:
                problems.append(f"{game_path}: changed vs {BASE} but missing from content-patches.json")
                continue
            fixed = (entry.get("Fixed") or "").lower()
            if fixed != sha256_bytes(current):
                problems.append(
                    f"{game_path}: content-patches Fixed does not match current source "
                    f"(refresh Ops/File payload)"
                )
                continue
            if old is None and not entry.get("Create"):
                problems.append(f"{game_path}: new file vs {BASE} needs Create: true")
                continue
            if entry.get("File"):
                payload = ROOT / "tools/fork-build" / entry["File"]
                if not payload.is_file() or sha256_bytes(payload.read_bytes()) != fixed:
                    problems.append(f"{game_path}: File payload missing or hash mismatch")
            elif not entry.get("Ops"):
                problems.append(f"{game_path}: entry needs Ops or File")

    if cs_changed:
        entry = patches.get(DLL_PATH)
        if entry is None or not entry.get("File"):
            problems.append(
                f"game/net/*.cs changed vs {BASE}: package {DLL_PATH} with a File payload (built MMOEngine.dll)"
            )
        else:
            payload = ROOT / "tools/fork-build" / entry["File"]
            if not payload.is_file():
                problems.append(f"{DLL_PATH}: File payload missing")
            elif (entry.get("Fixed") or "").lower() != sha256_bytes(payload.read_bytes()):
                problems.append(f"{DLL_PATH}: Fixed hash does not match File payload")
            if not entry.get("ReplaceAny") and not entry.get("Create"):
                problems.append(f"{DLL_PATH}: set ReplaceAny (or Create) so 0.37c installs without a known Released hash")

    if problems:
        for p in problems:
            print(f"::error title=New-client packaging::{p}" if "GITHUB_ACTIONS" in os.environ else p)
        print(f"{len(problems)} problem(s). See tools/release/check_newclient_packaged.py.")
        return 1
    print(f"New-client packaging OK (game/ui + game/net diffs vs {BASE} are in content-patches.json).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
