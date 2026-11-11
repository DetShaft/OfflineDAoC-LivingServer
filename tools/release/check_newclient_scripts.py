#!/usr/bin/env python3
"""Headless Godot check for Living Server new-client content patches.

Reproduces the 1.1.6 hang: a packaged 0.37c client's global_script_class_cache.cfg does not
list FriendsWindow / LfgWindow / CommanderPanel. INSTALL Create-adds those .gd files and
replaces game_ui.gd; if game_ui still type-annotates those global classes, GameUI fails to
compile and the "Entering Offline DAoC" loading screen never finishes.

    python3 tools/release/check_newclient_scripts.py [--godot PATH]

Needs Godot 4.7.x (standard build is enough). CI downloads it when GODOT is unset.
Exit 0 when every shipped content .gd loads; 1 on parse/load failure.
"""
from __future__ import annotations

import argparse
import os
import re
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
SHIPPED = [
    "game/ui/game_ui.gd",
    "game/ui/keybinds.gd",
    "game/ui/friends_window.gd",
    "game/ui/lfg_window.gd",
    "game/ui/commander_panel.gd",
    "game/net/server_world.gd",
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


def strip_new_classes(cache_text: str) -> str:
    text = cache_text
    for name in NEW_CLASSES:
        text2, n = re.subn(
            rf',\s*\{{\s*"base":[^}}]*?"class": &"{name}",[^}}]*?\}}',
            "",
            text,
            count=1,
            flags=re.S,
        )
        if n == 0:
            text2, n = re.subn(
                rf'\{{\s*"base":[^}}]*?"class": &"{name}",[^}}]*?\}}\s*,',
                "",
                text,
                count=1,
                flags=re.S,
            )
        if n != 1:
            raise SystemExit(f"could not strip class {name} from global_script_class_cache.cfg ({n} matches)")
        text = text2
        if f'&"{name}"' in text:
            raise SystemExit(f"class {name} still present after strip")
    return text


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--godot", help="Godot 4.7.x binary (else $GODOT or a downloaded linux build)")
    args = ap.parse_args(argv)
    if not GODOT_PROJECT.is_dir():
        print(f"missing {GODOT_PROJECT}", file=sys.stderr)
        return 1
    for rel in SHIPPED:
        src = CONTENT / rel
        if not src.is_file():
            print(f"missing content payload {src}", file=sys.stderr)
            return 1

    godot = find_godot(args.godot)
    with tempfile.TemporaryDirectory(prefix="newclient-script-check-") as tmp:
        work = Path(tmp) / "godot"
        shutil.copytree(
            GODOT_PROJECT,
            work,
            ignore=shutil.ignore_patterns(".godot", "content", "tests"),
        )
        # Minimal tests/ with load_check only (full tests/ is huge and not needed).
        (work / "tests").mkdir()
        shutil.copy2(GODOT_PROJECT / "tests/load_check.gd", work / "tests/load_check.gd")
        # Overlay Living Server content payloads (what INSTALL ships).
        for rel in SHIPPED:
            dest = work / rel
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(CONTENT / rel, dest)

        # Import once so Godot builds a full class cache including Create scripts.
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
        # Simulate packaged 0.37c: cache without the Create-only global classes.
        cache.write_text(strip_new_classes(cache.read_text(encoding="utf-8")), encoding="utf-8")

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
        parse = [line for line in out.splitlines() if "Parse Error" in line and any(c in line for c in NEW_CLASSES)]
        if fails or parse or proc.returncode not in (0,):
            # load_check quits with the fail count; also treat parse errors as failure.
            if fails or parse:
                print(f"{len(fails)} load_check FAIL(s), {len(parse)} class Parse Error(s).", file=sys.stderr)
                return 1
            if proc.returncode != 0:
                print(f"Godot exited {proc.returncode}", file=sys.stderr)
                return 1
        oks = [line for line in out.splitlines() if "[load_check] ok " in line]
        if len(oks) < len(SHIPPED):
            print(f"expected {len(SHIPPED)} ok lines, got {len(oks)}", file=sys.stderr)
            return 1
        print(
            "New-client script check OK "
            f"(Godot {GODOT_VERSION}; class cache stripped of {', '.join(NEW_CLASSES)}; "
            f"{len(SHIPPED)} content .gd files load)."
        )
        return 0


if __name__ == "__main__":
    sys.exit(main())
