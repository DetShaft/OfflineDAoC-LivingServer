#!/usr/bin/env python3
"""Headless Godot check for Living Server new-client content patches.

Reproduces the 1.1.6 hang: INSTALL drops Create scripts and a patched game_ui.gd onto a
packaged 0.37c client whose .godot/global_script_class_cache.cfg does not list FriendsWindow /
LfgWindow / CommanderPanel. Godot does not regenerate that cache for loose drops, so type
annotations against those global class_names fail to compile GameUI.

This check:
  1. Builds a stand-in "shipped" project (released 0.37 game_ui/keybinds/server_world, no Create scripts)
  2. Imports once to produce a class cache like 0.37c (without the Create-only classes)
  3. Overlays Living Server content payloads (what INSTALL writes) without re-importing
  4. Runs Godot 4.7 headless load_check — must succeed without regenerating the cache

    python3 tools/release/check_newclient_scripts.py [--godot PATH]

Exit 0 when every shipped content .gd loads against the frozen cache; 1 on parse/load failure.
"""
from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
import tempfile
import urllib.request
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
GODOT_PROJECT = ROOT / "source/new-client/godot"
CONTENT = ROOT / "tools/fork-build/content/godot"
# 0.37 release commit: content-patches Released hashes for game_ui / keybinds / server_world.
RELEASE_COMMIT = "ab4df02a1ad26192357477dbc912756c6703fb66"
SHIPPED = [
    "game/ui/game_ui.gd",
    "game/ui/chat_window.gd",
    "game/ui/keybinds.gd",
    "game/ui/friends_window.gd",
    "game/ui/lfg_window.gd",
    "game/ui/commander_panel.gd",
    "game/net/server_world.gd",
    "game/combat/spell_fx.gd",
    "game/perf_governor.gd",
    "game/anim_lod.gd",
]
CREATE_ONLY = [
    "game/ui/friends_window.gd",
    "game/ui/lfg_window.gd",
    "game/ui/commander_panel.gd",
]
RELEASED_PATHS = [
    "game/ui/game_ui.gd",
    "game/ui/chat_window.gd",
    "game/ui/keybinds.gd",
    "game/net/server_world.gd",
    "game/combat/spell_fx.gd",
    "game/perf_governor.gd",
    "game/anim_lod.gd",
]
NEW_CLASSES = ("FriendsWindow", "LfgWindow", "CommanderPanel")
GODOT_VERSION = "4.7.2"
GODOT_URL = (
    f"https://github.com/godotengine/godot/releases/download/{GODOT_VERSION}-stable/"
    f"Godot_v{GODOT_VERSION}-stable_linux.x86_64.zip"
)


def find_godot(explicit: str | None) -> Path:
    if explicit:
        path = Path(explicit)
        if not path.is_file():
            raise SystemExit(f"--godot is not a file: {path}")
        return path
    env = os.environ.get("GODOT")
    if env and Path(env).is_file():
        return Path(env)
    cached = Path(os.environ.get("RUNNER_TEMP", "/tmp")) / f"godot-{GODOT_VERSION}" / f"Godot_v{GODOT_VERSION}-stable_linux.x86_64"
    if cached.is_file():
        return cached
    cached.parent.mkdir(parents=True, exist_ok=True)
    zip_path = cached.parent / "godot.zip"
    print(f"Downloading Godot {GODOT_VERSION}…")
    urllib.request.urlretrieve(GODOT_URL, zip_path)
    with zipfile.ZipFile(zip_path) as zf:
        zf.extractall(cached.parent)
    cached.chmod(0o755)
    return cached


def git_show(rel: str) -> bytes:
    return subprocess.check_output(
        ["git", "show", f"{RELEASE_COMMIT}:source/new-client/godot/{rel}"],
        cwd=ROOT,
    )


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--godot", help="Godot 4.7.x binary (else $GODOT or a downloaded linux build)")
    args = ap.parse_args(argv)
    if not GODOT_PROJECT.is_dir():
        print(f"missing {GODOT_PROJECT}", file=sys.stderr)
        return 1
    for rel in SHIPPED:
        if not (CONTENT / rel).is_file():
            print(f"missing content payload {CONTENT / rel}", file=sys.stderr)
            return 1

    godot = find_godot(args.godot)
    with tempfile.TemporaryDirectory(prefix="newclient-script-check-") as tmp:
        work = Path(tmp) / "godot"
        # Shipped-like tree: full project sources minus Create-only scripts and without .godot/content/tests.
        shutil.copytree(
            GODOT_PROJECT,
            work,
            ignore=shutil.ignore_patterns(
                ".godot", "content", "tests",
                "friends_window.gd", "lfg_window.gd", "commander_panel.gd",
                "friends_window.gd.uid", "lfg_window.gd.uid", "commander_panel.gd.uid",
            ),
        )
        (work / "tests").mkdir()
        shutil.copy2(GODOT_PROJECT / "tests/load_check.gd", work / "tests/load_check.gd")
        # Released 0.37 scripts (no Living Server type annotations for Create classes).
        for rel in RELEASED_PATHS:
            dest = work / rel
            dest.parent.mkdir(parents=True, exist_ok=True)
            dest.write_bytes(git_show(rel))
        for rel in CREATE_ONLY:
            p = work / rel
            if p.exists():
                p.unlink()

        # One import to stand in for the packaged 0.37c class cache — before INSTALL overlays.
        subprocess.run(
            [str(godot), "--headless", "--path", str(work), "--import", "--quit"],
            check=False,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
        cache = work / ".godot/global_script_class_cache.cfg"
        if not cache.is_file():
            print("Godot import did not write global_script_class_cache.cfg", file=sys.stderr)
            return 1
        cache_before = cache.read_text(encoding="utf-8")
        for name in NEW_CLASSES:
            if f'&"{name}"' in cache_before:
                print(
                    f"stand-in shipped cache unexpectedly lists {name}; "
                    "Create scripts must stay out of the pre-overlay tree",
                    file=sys.stderr,
                )
                return 1

        # INSTALL overlay: write Living Server payloads, do NOT re-import / regenerate the cache.
        for rel in SHIPPED:
            dest = work / rel
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(CONTENT / rel, dest)
        cache_after = cache.read_text(encoding="utf-8")
        if cache_after != cache_before:
            print("class cache changed after overlay — check must not regenerate it", file=sys.stderr)
            return 1
        for name in NEW_CLASSES:
            if f'&"{name}"' in cache_after:
                print(f"cache gained {name} after overlay without import", file=sys.stderr)
                return 1

        cmd = [
            str(godot),
            "--headless",
            "--path",
            str(work),
            "-s",
            "res://tests/load_check.gd",
            "--",
            *[f"res://{rel}" for rel in SHIPPED],
        ]
        print(" ".join(cmd))
        proc = subprocess.run(cmd, text=True, capture_output=True)
        out = (proc.stdout or "") + (proc.stderr or "")
        sys.stdout.write(out)
        fails = [line for line in out.splitlines() if "[load_check] FAIL" in line]
        parse = [
            line for line in out.splitlines()
            if "Parse Error" in line and any(c in line for c in NEW_CLASSES)
        ]
        if fails or parse:
            print(
                f"{len(fails)} load_check FAIL(s), {len(parse)} class Parse Error(s) "
                "(game_ui must preload Create scripts by path, not class_name types).",
                file=sys.stderr,
            )
            return 1
        if proc.returncode not in (0,):
            # load_check exits with the fail count; non-zero means failures.
            print(f"Godot exited {proc.returncode}", file=sys.stderr)
            return 1
        oks = [line for line in out.splitlines() if "[load_check] ok " in line]
        if len(oks) < len(SHIPPED):
            print(f"expected {len(SHIPPED)} ok lines, got {len(oks)}", file=sys.stderr)
            return 1
        # Cache still frozen after the load (no silent regeneration).
        if cache.read_text(encoding="utf-8") != cache_before:
            print("class cache changed during load_check", file=sys.stderr)
            return 1
        print(
            "New-client script check OK "
            f"(Godot {GODOT_VERSION}; frozen 0.37c-like class cache without "
            f"{', '.join(NEW_CLASSES)}; overlay + {len(SHIPPED)} content .gd files load)."
        )
        return 0


if __name__ == "__main__":
    sys.exit(main())
