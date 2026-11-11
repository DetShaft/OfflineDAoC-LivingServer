# Living Server Beta 1.1.10: release notes

The text below the line is the GitHub release notes for `v1.1.10-beta`. In the file table the Setup row must keep
exactly `(filled in when the exe is built)`: the release workflow replaces only that text with the exe's SHA-256. The
zip's SHA-256 is filled in when publishing.

---

### World-entry smoke markers

1.1.10 is a **DetShaft fork prerelease**. Same Offline DAoC **0.37 / 0.37b / 0.37c** base. The 1.1.9 class-cache
repair is unchanged: INSTALL/Setup still restore `global_script_class_cache.cfg` and never delete it.

**Client.** `zone_viewer.gd` understands `--smoke`. When that flag is set it skips the loading screen, prints
two lines, waits a couple of seconds, and quits:

```
LS_SMOKE ZONE_BUILT Gotar
LS_SMOKE IN_WORLD Gotar
```

The zone name is the `"name"` from that zone's `zone.json`. Without `--smoke` the game does not print them and
does not quit. The banner reads `LivingServer client patch 1.1.10`.

**Smoke tools** (in the git repo, `tools/smoke`, not inside the game folder):

- `Invoke-WorldEntrySmoke.ps1` copies a live install to a staging folder without modifying the live folder.
  Big read-only asset and zone trees are directory junctions. Anything the installer or the server writes
  (scripts, server binaries, the world database, a content file the patch would change) is a real copy.
  It installs the given release zip into staging, checks the class cache and the patched script hashes,
  starts that staging server on ports 11300/11400, stops it, then runs headless Godot with `--smoke`.
  Exit 0 is pass, 1 is fail, 2 is blocked.
- `Watch-Releases.ps1` asks the public GitHub releases API (no token) for new
  DetShaft/OfflineDAoC-LivingServer zips and runs that test. A tag is remembered after a pass or a fail.
  A blocked run is retried next time.
- `Register-SmokeTask.ps1` registers a scheduled task every 30 minutes, current user, only while logged on.

Zones and other game assets are never committed or uploaded. A pass on a machine that is not Jay's PC is
tests only until his log shows the same lines.

### Files

| File | SHA-256 |
|---|---|
| `LivingServer-Setup-1.1.10-beta.exe` | (filled in when the exe is built) |
| `OfflineDAoC-LivingServer-1.1.10-beta.zip` | (zip SHA-256: filled in by hand at publish) |

### Credits

Built on [shadowofze's Offline DAoC](https://github.com/shadowofze/OfflineDAoC). DetShaft fork packaging. GPL-3.0.
