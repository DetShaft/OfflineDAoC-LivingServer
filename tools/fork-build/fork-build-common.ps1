# Shared by install-server-build.ps1 and rollback-server-build.ps1 (docs/RELEASE-PLAN.md).
# Windows PowerShell 5.1 compatible.

$Script:ServerFolders = @('runtime/server', 'runtime/server/lib', 'runtime/server/win-x64')
# The launcher (its Living Server tab): the game's own copy, and the 0.37c new client's copy when present.
$Script:LauncherFolders = @('runtime', 'new-client/runtime')
$Script:LauncherFiles = @('OfflineDAoC.dll', 'OfflineDAoC.exe', 'OfflineDAoC.pdb', 'OfflineDAoC.deps.json', 'OfflineDAoC.runtimeconfig.json')
# The server's own libraries beside GameServer.dll, shipped in the build's core\ folder (Living Server 1.1.2: save safety
# changed CoreDatabase, CoreBase and CoreServer, and a server whose GameServer.dll was new but CoreDatabase.dll released
# stopped at start, playtest 2026-11-04). Each one replaces the folder's copy only where the folder has one; its .pdb
# goes with it where the folder has a .pdb too.
$Script:CoreFiles = @('CoreBase.dll', 'CoreDatabase.dll', 'CoreServer.dll')
# Also the 0.37c new client and its launcher, which share the game's runtime.
$Script:ProcessNames = @('CoreServer', 'DOLServer', 'OfflineDAoC', 'game', 'camelot', 'connect', 'Godot_v4.7.2-stable_mono_win64')

function Stop-WithMessage([string]$Message) {
    Write-Host ''
    Write-Host "NOTHING WAS CHANGED: $Message" -ForegroundColor Red
    exit 1
}

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Read-KeyValueFile([string]$Path) {
    $values = @{}
    foreach ($line in Get-Content -LiteralPath $Path) {
        if ($line -match '^\s*([^=#]+?)\s*=\s*(.*?)\s*$') { $values[$Matches[1]] = $Matches[2] }
    }
    return $values
}

function Test-GameFolder([string]$Path) {
    return $Path -and (Test-Path -LiteralPath (Join-Path $Path 'runtime\server\GameServer.dll'))
}

# The game folder: the one given, else the folder this build sits in (or its parents), or a
# playable*/OfflineDAoC* folder inside one of those.
function Find-GameFolder([string]$Given, [string]$BuildDir) {
    if ($Given) {
        $full = [IO.Path]::GetFullPath($Given)
        if (Test-GameFolder $full) { return $full }
        Stop-WithMessage "No Offline DAoC game in '$full' (runtime\server\GameServer.dll is missing)."
    }
    $parent = Split-Path -Parent $BuildDir
    # The documented place is inside the game folder: when the build sits there, that folder wins, even if other
    # playable* folders sit next to it (playtest 2026-11-04: three side-by-side installs refused to install).
    if ($parent -and (Test-GameFolder $parent)) { return [IO.Path]::GetFullPath($parent) }
    $starts = @()
    if ($parent) { $starts += $parent; $grand = Split-Path -Parent $parent; if ($grand) { $starts += $grand } }
    $starts += $BuildDir
    $found = @()
    foreach ($start in $starts) {
        if (Test-GameFolder $start) { $found += [IO.Path]::GetFullPath($start); continue }
        foreach ($child in @(Get-ChildItem -LiteralPath $start -Directory -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -like 'playable*' -or $_.Name -like 'OfflineDAoC*' })) {
            if (Test-GameFolder $child.FullName) { $found += $child.FullName }
        }
    }
    $found = @($found | Select-Object -Unique)
    if ($found.Count -eq 1) { return $found[0] }
    if ($found.Count -gt 1) {
        Stop-WithMessage ("More than one game folder found. Run again with -GameFolder and one of:`n  " + ($found -join "`n  "))
    }
    Stop-WithMessage ("Can't find the game. Put this build's folder inside your Offline DAoC folder (the one with " +
        "START OFFLINE DAOC.cmd), or run INSTALL.cmd -GameFolder ""C:\path\to\playable-v0.37b"".")
}

function Assert-GameClosed {
    $running = @(Get-Process -Name $Script:ProcessNames -ErrorAction SilentlyContinue)
    if ($running.Count -gt 0) {
        Stop-WithMessage ("Close the server, launcher and game first (running: " +
            (($running | ForEach-Object { $_.ProcessName } | Select-Object -Unique) -join ', ') + ').')
    }
}

# 'hash  relative/path' lines of the release's PACKAGE MANIFEST.sha256, keyed by path.
function Read-PackageManifest([string]$Game) {
    return Read-ManifestFile (Join-Path $Game 'PACKAGE MANIFEST.sha256')
}

# The same for any manifest file (0.37c's new client has its own: new-client\PACKAGE MANIFEST.sha256).
function Read-ManifestFile([string]$path) {
    if (!(Test-Path -LiteralPath $path)) { return $null }
    $manifest = @{}
    foreach ($line in Get-Content -LiteralPath $path) {
        if ($line -match '^([0-9a-fA-F]{64})\s+(.+)$') { $manifest[$Matches[2].Trim()] = $Matches[1].ToLowerInvariant() }
    }
    return $manifest
}

# Whether a release's PACKAGE MANIFEST.sha256 is Offline DAoC <base>. Each release lists its own docs/RELEASE-<version>.md and
# keeps the earlier ones, so the newest listed is the game's release (an updated 0.37 lists 0.36 and 0.37). An updated
# game has no editions\ folder (the 0.37 update removes it), so that check, which the 0.36 full downloads pass, is only
# the fallback for a manifest without release notes. Found on a real 0.36c game updated to 0.37c (2026-10-18).
# A build may fit several releases (BaseRelease=0.37,0.38 in BUILD-INFO.txt): once its server code carries a newer
# release's server changes, that release is added so the build installs on either. The game must be one of them.
function Get-BaseReleases([string]$base) {
    return @($base -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}

function Test-ReleaseManifest($manifest, [string]$base) {
    $bases = Get-BaseReleases $base
    $versions = @($manifest.Keys | ForEach-Object { if ($_ -match '^docs/RELEASE-(\d+\.\d+)\.md$') { [version]$Matches[1] } })
    if ($versions.Count -gt 0) {
        $newest = $versions | Sort-Object | Select-Object -Last 1
        return [bool]($bases | Where-Object { [version]$_ -eq $newest } | Select-Object -First 1)
    }
    foreach ($one in $bases) {
        if ($manifest.Keys | Where-Object { $_ -like "editions/$one-no-custom-class/*" } | Select-Object -First 1) { return $true }
    }
    return $false
}

function Get-InstallRecords([string]$Game) {
    $root = Join-Path $Game 'runtime\server\deployment-backups'
    if (!(Test-Path -LiteralPath $root)) { return @() }
    return @(Get-ChildItem -LiteralPath $root -Directory -Filter 'fork-build-*' |
        Sort-Object Name |
        ForEach-Object {
            $file = Join-Path $_.FullName 'install-record.json'
            if (Test-Path -LiteralPath $file) {
                $record = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
                $record | Add-Member -NotePropertyName BackupFolder -NotePropertyValue $_.FullName -Force
                $record | Add-Member -NotePropertyName RolledBack -NotePropertyValue (Test-Path -LiteralPath (Join-Path $_.FullName 'rolled-back.txt')) -Force
                $record
            }
        })
}

function Get-BackupName([string]$Folder) { return ($Folder -replace '[\\/]', '_') }

# Game files this build fixes in place (content-patches.json, made by make_content_patch.py): byte patches against the
# released file, so no game file ships whole. A file is changed only when it has the released fingerprint, and the
# result must have the fixed one. Beta 1.0: Hall of the Corrupt's navmesh and eight new-client gate models; Beta 1.1:
# the game's loading screen (pregame/splash.mpk, shipped whole under content/); Beta 1.1.1: the new client's quest
# marker script (godot/game/quests/quest_markers.gd), a text file patched the same way. Beta 1.1.5: friends / LFG /
# commander .gd scripts and MMOEngine.dll — Create for brand-new scripts, ReplaceAny for the compiled network client
# (0.37c loads loose .gd from new-client/godot, not a .pck).
function Read-ContentPatches([string]$BuildDir) {
    $path = Join-Path $BuildDir 'content-patches.json'
    if (!(Test-Path -LiteralPath $path)) { return @() }
    return @(Get-Content -LiteralPath $path -Raw | ConvertFrom-Json)
}

# An entry with "File" ships its fixed file whole in the build folder instead of byte ops (Living Server's own art, the
# game's loading screen, owner 2026-10-28): only for files that are entirely ours, never a game file.
function Get-ContentPatchFile($Patch, [string]$BuildDir) {
    $relative = [string]$Patch.File
    if ([IO.Path]::IsPathRooted($relative) -or $relative -match '(^|[\\/])\.\.([\\/]|$)') { throw "Bad content file path: $relative" }
    return (Join-Path $BuildDir ($relative.Replace('/', [IO.Path]::DirectorySeparatorChar)))
}

function Expand-ContentPatch($Patch, [byte[]]$Released, [string]$BuildDir) {
    if ($Patch.File) { return ,([IO.File]::ReadAllBytes((Get-ContentPatchFile $Patch $BuildDir))) }
    $out = New-Object IO.MemoryStream
    foreach ($op in @($Patch.Ops)) {
        if ($null -ne $op.copy) { $out.Write($Released, [int]$op.copy[0], [int]$op.copy[1]) }
        else { $bytes = [Convert]::FromBase64String([string]$op.data); $out.Write($bytes, 0, $bytes.Length) }
    }
    return ,$out.ToArray()
}

function Get-BytesSha256([byte[]]$Bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return (($sha.ComputeHash($Bytes) | ForEach-Object { $_.ToString('x2') }) -join '') } finally { $sha.Dispose() }
}

function Get-ContentPath([string]$Game, [string]$Relative) {
    return (Join-Path $Game ($Relative.Replace('/', [IO.Path]::DirectorySeparatorChar)))
}

# class_name declarations in the Godot project (first 40 lines of each .gd). The packaged client runs
# `godot --path new-client\godot` and does not rebuild global_script_class_cache.cfg, so every name here
# must already be in that file or autoloads fail and the window stays grey (1.1.8 deleted the cache).
function Get-GodotClassNames([string]$GodotRoot) {
    $names = New-Object System.Collections.Generic.List[string]
    if (!(Test-Path -LiteralPath $GodotRoot)) { return @() }
    Get-ChildItem -LiteralPath $GodotRoot -Recurse -Filter '*.gd' -File -ErrorAction SilentlyContinue | ForEach-Object {
        if ($_.FullName -match '[\\/]\.godot[\\/]') { return }
        $head = @(Get-Content -LiteralPath $_.FullName -TotalCount 40 -ErrorAction SilentlyContinue)
        foreach ($line in $head) {
            if ($line -match '^\s*class_name\s+([A-Za-z_][A-Za-z0-9_]*)\b') {
                if (-not $names.Contains($Matches[1])) { $names.Add($Matches[1]) }
                break
            }
        }
    }
    return @($names)
}

function Get-GodotCacheClasses([string]$CacheText) {
    $found = New-Object System.Collections.Generic.List[string]
    foreach ($m in [regex]::Matches($CacheText, '"class":\s*&"([^"]+)"')) {
        $found.Add($m.Groups[1].Value)
    }
    return @($found)
}

function Get-GodotCacheEntry([string]$CacheText, [string]$Name) {
    $pattern = '(?s)\{\s*"base":.*?"class":\s*&"' + [regex]::Escape($Name) + '".*?\}'
    $m = [regex]::Match($CacheText, $pattern)
    if ($m.Success) { return $m.Value }
    return $null
}

# Restores or completes new-client\godot\.godot\global_script_class_cache.cfg from the copy shipped in
# the build. Never deletes the cache. Fails the install when a class_name in the project is still absent.
function Repair-GodotClassCache([string]$Game, [string]$BuildDir) {
    $godot = Join-Path $Game 'new-client\godot'
    if (!(Test-Path -LiteralPath (Join-Path $godot 'project.godot'))) { return }
    $shipped = Join-Path $BuildDir 'content\godot-class-cache\global_script_class_cache.cfg'
    if (!(Test-Path -LiteralPath $shipped)) {
        Stop-WithMessage "This build is missing content\godot-class-cache\global_script_class_cache.cfg (damaged download?)."
    }
    $cacheDir = Join-Path $godot '.godot'
    $cache = Join-Path $cacheDir 'global_script_class_cache.cfg'
    New-Item -ItemType Directory -Path $cacheDir -Force | Out-Null
    $shippedText = [IO.File]::ReadAllText($shipped)
    $need = @(Get-GodotClassNames $godot)
    if (!(Test-Path -LiteralPath $cache)) {
        [IO.File]::WriteAllText($cache, $shippedText)
        Write-Host "  restored .godot\global_script_class_cache.cfg (Godot --path does not rebuild a deleted cache; 1.1.8 removed it and the client stayed a grey screen)"
    } else {
        $text = [IO.File]::ReadAllText($cache)
        $have = @(Get-GodotCacheClasses $text)
        $missing = @($need | Where-Object { $have -notcontains $_ })
        if ($missing.Count -gt 0) {
            $blocks = @()
            foreach ($name in $missing) {
                $block = Get-GodotCacheEntry $shippedText $name
                if ($block) { $blocks += $block }
            }
            if ($blocks.Count -gt 0) {
                $trimmed = $text.TrimEnd()
                if ($trimmed.EndsWith(']')) {
                    $trimmed = $trimmed.Substring(0, $trimmed.Length - 1).TrimEnd()
                    $text = $trimmed + ",`n" + ($blocks -join ",`n") + "`n]"
                    [IO.File]::WriteAllText($cache, $text)
                    Write-Host "  appended $($blocks.Count) class(es) to global_script_class_cache.cfg (cache kept; not deleted)"
                }
            }
        }
    }
    $text = [IO.File]::ReadAllText($cache)
    $have = @(Get-GodotCacheClasses $text)
    $still = @($need | Where-Object { $have -notcontains $_ })
    if ($still.Count -gt 0) {
        $shippedHave = @(Get-GodotCacheClasses $shippedText)
        $shippedMissing = @($need | Where-Object { $shippedHave -notcontains $_ })
        if ($shippedMissing.Count -eq 0) {
            [IO.File]::WriteAllText($cache, $shippedText)
            Write-Host "  wrote the shipped class cache over an incomplete one ($($still.Count) class(es) were missing)"
            $have = $shippedHave
            $still = @($need | Where-Object { $have -notcontains $_ })
        }
    }
    if ($still.Count -gt 0) {
        Stop-WithMessage ("Godot class cache is missing class_name entries (the client stays a grey screen without them):`n  " + ($still -join ', ') +
            "`nRe-download 1.1.9. Do not delete new-client\godot\.godot\global_script_class_cache.cfg.")
    }
    Write-Host "  class cache lists all $($need.Count) project class_name script(s)"
}

# Living Server ships full File payloads for new-client/godot/game/**.gd (UI/net). INSTALL / Setup / apply-update
# must overwrite whenever the on-disk hash is not Fixed — silent "left alone" is why 1.1.7 Setup left Jay on
# 1.1.6's typed game_ui while the server DLL was already current.
function Test-IsLivingServerGdFilePatch($Patch) {
    $p = [string]$Patch.Path
    return [bool]$Patch.File -and $p.StartsWith('new-client/godot/game/') -and $p.EndsWith('.gd')
}

# Stops with a clear message when any required Living Server .gd File payload is missing or not Fixed.
# Setup and zip INSTALL both call this after writes (and before "Already installed") so a partial update cannot
# look successful. Skips when the game has no new-client tree (classic 0.37/0.37b).
function Assert-LivingServerGdFixed([string]$Game, $Patches, [string]$When) {
    if (!(Test-Path -LiteralPath (Join-Path $Game 'new-client'))) { return }
    $bad = @()
    foreach ($patch in @($Patches)) {
        if (!(Test-IsLivingServerGdFilePatch $patch)) { continue }
        $path = Get-ContentPath $Game $patch.Path
        $want = ([string]$patch.Fixed).ToLowerInvariant()
        if (!(Test-Path -LiteralPath $path)) {
            $bad += "$($patch.Path): missing (expected Fixed $want)"
            continue
        }
        $got = Get-Sha256 $path
        if ($got -ne $want) {
            $bad += "$($patch.Path): on disk $got, expected Fixed $want"
        }
    }
    if ($bad.Count -gt 0) {
        Stop-WithMessage ("Living Server new-client scripts are wrong $When (Setup and INSTALL share this path; the launcher " +
            "starts this same new-client\godot folder):`n  " + ($bad -join "`n  ") + "`nRe-run INSTALL / Setup from a fresh package.")
    }
}

# The world database and its WAL files, when present (Living Server 1.1.2: the installer backs them up first). With the
# server closed, the database file and its -wal are a consistent set; -shm is only an index SQLite rebuilds.
function Get-WorldDatabaseFiles([string]$Game) {
    $db = Join-Path $Game 'runtime\data\opendaoc.sqlite3.db'
    if (!(Test-Path -LiteralPath $db)) { return @() }
    $files = @($db)
    foreach ($suffix in @('-wal', '-shm')) { if (Test-Path -LiteralPath "$db$suffix") { $files += "$db$suffix" } }
    return $files
}

function Get-FreeBytes([string]$Path) {
    try {
        $root = [IO.Path]::GetPathRoot([IO.Path]::GetFullPath($Path))
        return (New-Object IO.DriveInfo $root).AvailableFreeSpace
    } catch { return $null }
}

# Keeps the world database copies of the newest $Keep installs (each install's data\ folder) and removes older ones. Only
# the data\ folders go; the server and launcher backups stay, so ROLLBACK.cmd and -ToRelease work as before.
function Remove-OldDatabaseBackups([string]$Game, [int]$Keep) {
    $root = Join-Path $Game 'runtime\server\deployment-backups'
    if (!(Test-Path -LiteralPath $root)) { return 0 }
    $withData = @(Get-ChildItem -LiteralPath $root -Directory -Filter 'fork-build-*' | Sort-Object Name |
        Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'data') })
    $old = if ($withData.Count -gt $Keep) { $withData[0..($withData.Count - $Keep - 1)] } else { @() }
    foreach ($folder in $old) { Remove-Item -LiteralPath (Join-Path $folder.FullName 'data') -Recurse -Force }
    return @($old).Count
}
