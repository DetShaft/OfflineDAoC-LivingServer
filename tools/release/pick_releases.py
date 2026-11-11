"""Picks the published Living Server releases the installer workflow builds and tests with.

Used by .github/workflows/installer.yml (job "releases"):

    python tools/release/pick_releases.py [--package v1.1.10-beta]
        package  the release whose package Setup embeds: --package when given (the release workflow passes the tag it
                 is finishing), otherwise the newest published release;
        from/to  the launcher-update test's pair: "to" is that same release, "from" the newest published release older
                 than it. The launcher built from the commit under test updates a fake game from one to the other.

Only published (not draft) releases of this repository that carry their OfflineDAoC-LivingServer-<build>.zip count,
ordered by version, not by date. Older tags (v1.0-beta to v1.1.3-beta) were never published as releases in this fork,
so they are never picked. Results go to GitHub step outputs (package, from, to) and are printed.
--releases-json FILE reads the release list from a file instead of GitHub (tests and local checks).
Reading a public repository's releases works without a token; GITHUB_TOKEN or GH_TOKEN is used when set.
"""
import argparse
import json
import os
import re
import sys
import urllib.request
from pathlib import Path

REPO = os.environ.get("GITHUB_REPOSITORY", "DetShaft/OfflineDAoC-LivingServer")
API = os.environ.get("GITHUB_API_URL", "https://api.github.com")
TAG = re.compile(r"^v(\d+(?:\.\d+)+)-beta$")


class Stop(Exception):
    """No usable release; the message says why."""


def version(tag):
    m = TAG.match(tag or "")
    return tuple(int(n) for n in m.group(1).split(".")) if m else None


def package_name(tag):
    return f"OfflineDAoC-LivingServer-{tag[1:]}.zip"


def usable(releases):
    """Published releases with their package zip, newest version first."""
    found = []
    for r in releases:
        tag = r.get("tag_name", "")
        if r.get("draft") or version(tag) is None:
            continue
        if not any(a.get("name") == package_name(tag) for a in r.get("assets", [])):
            continue
        found.append(tag)
    return sorted(found, key=version, reverse=True)


def pick(releases, package=None):
    tags = usable(releases)
    if not tags:
        raise Stop(f"{REPO} has no published release with its OfflineDAoC-LivingServer-<build>.zip.")
    if package:
        if package not in tags:
            raise Stop(f"{package} is not a published release of {REPO} with {package_name(package)}. "
                       f"Published: {', '.join(tags)}.")
    else:
        package = tags[0]
    older = [t for t in tags if version(t) < version(package)]
    if not older:
        raise Stop(f"No published release is older than {package}, so the launcher-update test has nothing to update "
                   f"from. Published: {', '.join(tags)}.")
    return {"package": package, "from": older[0], "to": package}


def fetch(token=None):
    headers = {"Accept": "application/vnd.github+json", "X-GitHub-Api-Version": "2022-11-28",
               "User-Agent": "living-server-release"}
    if token:
        headers["Authorization"] = f"Bearer {token}"
    releases, page = [], 1
    while True:
        req = urllib.request.Request(f"{API}/repos/{REPO}/releases?per_page=100&page={page}", headers=headers)
        with urllib.request.urlopen(req, timeout=60) as r:
            batch = json.loads(r.read())
        releases += batch
        if len(batch) < 100:
            return releases
        page += 1


def run(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--package", default="", help="the release Setup embeds (default: the newest published release)")
    ap.add_argument("--releases-json", help="read the release list from this file instead of GitHub")
    a = ap.parse_args(argv)
    try:
        if a.releases_json:
            releases = json.loads(Path(a.releases_json).read_text(encoding="utf-8"))
        else:
            releases = fetch(os.environ.get("GITHUB_TOKEN") or os.environ.get("GH_TOKEN"))
        picked = pick(releases, a.package.strip() or None)
    except Stop as e:
        print(f"::error title=Releases::{e}" if os.environ.get("GITHUB_ACTIONS") else f"error: {e}")
        return 1
    out = os.environ.get("GITHUB_OUTPUT")
    if out:
        with open(out, "a", encoding="utf-8") as f:
            for k, v in picked.items():
                f.write(f"{k}={v}\n")
    print(f"package={picked['package']} from={picked['from']} to={picked['to']}")
    return 0


if __name__ == "__main__":
    sys.exit(run())
