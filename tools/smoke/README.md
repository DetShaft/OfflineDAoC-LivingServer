# World-entry smoke

Checks that a Living Server release zip can be installed onto a **copy** of a real 0.37c game and that
the new client enters a zone. The live folder (default `E:\Offline DAOC`) is only read. Zone files and
other copyrighted assets stay on that PC. They are not committed or uploaded.

The client prints these lines only when it is started with `--smoke`, then quits:

```
LS_SMOKE ZONE_BUILT Gotar
LS_SMOKE IN_WORLD Gotar
```

`Gotar` is the `"name"` in that zone's `zone.json`. The smoke passes only when both lines are present
and the Godot log has no `SCRIPT ERROR` and no `Parse Error`.

## Register the 30-minute check (Jay-PC)

From a checkout of this repo, while logged on. No administrator rights:

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools\smoke\Register-SmokeTask.ps1
```

That registers the task `LivingServerWorldEntrySmoke` for the current user, only while logged on,
every 30 minutes. The task runs `Watch-Releases.ps1`, which asks the public GitHub releases API (no
token) for `DetShaft/OfflineDAoC-LivingServer`, downloads one unseen zip, and runs the smoke test.
A pass or a fail is remembered. A blocked run (game is open, zone missing, ports busy, files could
not be linked) is tried again next time.

If the report says it could not link files without writing `E:\Offline DAOC`, put the staging folder
on the same drive as the game and register again:

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools\smoke\Register-SmokeTask.ps1 -StagingRoot "E:\LivingServerSmoke\staging"
```

Reports and the Godot log tail are written to
`%LOCALAPPDATA%\LivingServerSmoke\results\<tag>\`.

## One zone, one zip

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools\smoke\Invoke-WorldEntrySmoke.ps1 -ReleaseZip "C:\path\OfflineDAoC-LivingServer-1.1.10-beta.zip"
```

Exit codes: `0` pass, `1` fail, `2` blocked. The staging server listens on `11300` / `11400`, then
stops, so it does not take the live server's `10300`.

To mark every release that already exists as seen, without testing them:

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools\smoke\Watch-Releases.ps1 -SeedSeen
```

Leave that off if the current release should be tested on the first run.
