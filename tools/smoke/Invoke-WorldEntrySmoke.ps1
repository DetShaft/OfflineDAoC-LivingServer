# Headless world-entry smoke for a Living Server release zip.
# PowerShell 5.1 compatible. Never writes the live install. Never uploads zone assets.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\smoke\Invoke-WorldEntrySmoke.ps1 `
#       -ReleaseZip C:\path\OfflineDAoC-LivingServer-1.1.10-beta.zip
#
# Exit 0 pass, 1 fail, 2 blocked. A report and the Godot log tail land in
# %LOCALAPPDATA%\LivingServerSmoke\results\<tag>\.
param(
    [Parameter(Mandatory = $true)][string]$ReleaseZip,
    [string]$LiveRoot = 'E:\Offline DAOC',
    [string]$StagingRoot = '',
    [string]$Zone = 'Gotar',
    [string]$Tag = '',
    [string]$GodotPath = '',
    [string]$ResultsRoot = '',
    [int]$GodotTimeoutSeconds = 240,
    [int]$ServerTimeoutSeconds = 180
)

$ErrorActionPreference = 'Stop'
$script:SmokePort = 11300
$script:RegionPort = 11400
$script:BootClasses = @(
    'ZoneAudio', 'Accounts', 'LoginScreen', 'GameUI', 'UiArt', 'SkySystem', 'ZoneLoader',
    'GroundClutter', 'RegionWorld', 'FriendsWindow', 'LfgWindow', 'CommanderPanel'
)
$script:Snapshot = $null
$script:TagName = ''
$script:ResultsDir = ''
$script:GodotLog = ''
$script:ServerNote = ''
$script:Detail = New-Object System.Collections.Generic.List[string]

function Add-Detail([string]$Line) {
    [void]$script:Detail.Add($Line)
    Write-Host $Line
}

function Test-IsLink([string]$Path) {
    if (!(Test-Path -LiteralPath $Path)) { return $false }
    $item = Get-Item -LiteralPath $Path -Force
    $reparse = [IO.FileAttributes]::ReparsePoint
    if (($item.Attributes -band $reparse) -eq $reparse) { return $true }
    if ($item.LinkType) { return $true }
    return $false
}

function Remove-LinkOnly([string]$Path) {
    if (!(Test-Path -LiteralPath $Path)) { return }
    if ($env:OS -eq 'Windows_NT') {
        $item = Get-Item -LiteralPath $Path -Force
        if ($item.PSIsContainer) {
            & "$env:SystemRoot\System32\cmd.exe" /c rmdir "$Path" | Out-Null
            if (Test-Path -LiteralPath $Path) { throw "Could not remove the junction $Path" }
            return
        }
    }
    Remove-Item -LiteralPath $Path -Force
}

function Remove-TreeNoFollow([string]$Root) {
    if (!(Test-Path -LiteralPath $Root)) { return }
    if (Test-IsLink $Root) { Remove-LinkOnly $Root; return }
    foreach ($child in @(Get-ChildItem -LiteralPath $Root -Force)) {
        if ($child.PSIsContainer) { Remove-TreeNoFollow $child.FullName }
        elseif (Test-IsLink $child.FullName) { Remove-LinkOnly $child.FullName }
        else { Remove-Item -LiteralPath $child.FullName -Force }
    }
    if (Test-Path -LiteralPath $Root) { Remove-Item -LiteralPath $Root -Force }
}

function New-DirectoryLink([string]$Link, [string]$Target) {
    if (Test-Path -LiteralPath $Link) { return }
    $parent = Split-Path -Parent $Link
    if ($parent -and !(Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    if ($env:OS -eq 'Windows_NT') {
        New-Item -ItemType Junction -Path $Link -Target $Target -ErrorAction Stop | Out-Null
        return
    }
    New-Item -ItemType SymbolicLink -Path $Link -Target $Target -ErrorAction Stop | Out-Null
}

function New-FileLink([string]$Link, [string]$Target) {
    if (Test-Path -LiteralPath $Link) { return $true }
    $parent = Split-Path -Parent $Link
    if ($parent -and !(Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    if ($env:OS -eq 'Windows_NT') {
        $cmd = "$env:SystemRoot\System32\cmd.exe"
        & $cmd /c mklink /H "$Link" "$Target" | Out-Null
        if (Test-Path -LiteralPath $Link) { return $true }
        & $cmd /c mklink "$Link" "$Target" | Out-Null
        if (Test-Path -LiteralPath $Link) { return $true }
        return $false
    }
    try {
        New-Item -ItemType SymbolicLink -Path $Link -Target $Target -ErrorAction Stop | Out-Null
        return $true
    } catch {
        return $false
    }
}

function Get-Sha256([string]$Path) {
    $sha = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::Open($Path, 'Open', 'Read', 'ReadWrite')
    try {
        return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '').ToLowerInvariant()
    } finally {
        $stream.Dispose()
        $sha.Dispose()
    }
}

function Copy-TreeReal([string]$Source, [string]$Destination) {
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    foreach ($child in @(Get-ChildItem -LiteralPath $Source -Force)) {
        $to = Join-Path $Destination $child.Name
        if ($child.PSIsContainer) {
            if (Test-IsLink $child.FullName) { New-DirectoryLink $to $child.FullName }
            else { Copy-TreeReal $child.FullName $to }
        } else {
            Copy-Item -LiteralPath $child.FullName -Destination $to -Force
        }
    }
}

function Copy-InstallRecords([string]$Source, [string]$Destination) {
    if (!(Test-Path -LiteralPath $Source)) { return }
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    foreach ($dir in @(Get-ChildItem -LiteralPath $Source -Force -Directory -ErrorAction SilentlyContinue)) {
        if ($dir.Name -notlike 'fork-build-*') { continue }
        $json = Join-Path $dir.FullName 'install-record.json'
        if (!(Test-Path -LiteralPath $json)) { continue }
        $to = Join-Path $Destination $dir.Name
        New-Item -ItemType Directory -Path $to -Force | Out-Null
        Copy-Item -LiteralPath $json -Destination $to -Force
        $rolled = Join-Path $dir.FullName 'rolled-back.txt'
        if (Test-Path -LiteralPath $rolled) { Copy-Item -LiteralPath $rolled -Destination $to -Force }
    }
}

function Publish-LinkedChildren([string]$Destination, [string]$LiveDir) {
    $pending = New-Object System.Collections.Generic.List[object]
    foreach ($child in @(Get-ChildItem -LiteralPath $LiveDir -Force)) {
        $to = Join-Path $Destination $child.Name
        if ($child.PSIsContainer) {
            New-DirectoryLink $to $child.FullName
            continue
        }
        if (New-FileLink $to $child.FullName) { continue }
        $pending.Add($child)
    }
    $bytes = [int64]0
    foreach ($file in $pending) { $bytes += [int64]$file.Length }
    if ($bytes -gt 64MB) {
        throw ("BLOCKED: cannot link " + $pending.Count + " file(s) under $LiveDir (" +
            [math]::Round($bytes / 1MB) + " MB) without copying them or writing the live install. " +
            "Re-register with -StagingRoot on the same drive as the game so hardlinks work, " +
            "or turn on Windows Developer Mode so file symlinks work.")
    }
    foreach ($file in $pending) {
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $Destination $file.Name) -Force
    }
}

# Make this staging path a real directory. A junction is removed (the live target is not) and its
# children are linked. A missing live directory becomes an empty real directory.
function Expand-IfLink([string]$Destination, [string]$LiveDir) {
    if (!(Test-Path -LiteralPath $Destination)) {
        if (!(Test-Path -LiteralPath $LiveDir)) {
            New-Item -ItemType Directory -Path $Destination -Force | Out-Null
            return
        }
        $item = Get-Item -LiteralPath $LiveDir -Force
        if ($item.PSIsContainer) { New-DirectoryLink $Destination $LiveDir }
        else {
            Copy-Item -LiteralPath $LiveDir -Destination $Destination -Force
            return
        }
    }
    if (!(Test-IsLink $Destination)) { return }
    if (!(Test-Path -LiteralPath $LiveDir)) { throw "BLOCKED: $Destination points at a missing folder." }
    Remove-LinkOnly $Destination
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    Publish-LinkedChildren $Destination $LiveDir
}

function Protect-PatchFile([string]$Live, [string]$Staging, [string]$Relative) {
    $parts = @($Relative -split '[\\/]' | Where-Object { $_ })
    if ($parts.Count -eq 0) { return }
    $liveAcc = $Live
    $dstAcc = $Staging
    for ($i = 0; $i -lt ($parts.Count - 1); $i++) {
        $liveAcc = Join-Path $liveAcc $parts[$i]
        $dstAcc = Join-Path $dstAcc $parts[$i]
        Expand-IfLink $dstAcc $liveAcc
    }
    $liveFile = Join-Path $liveAcc $parts[$parts.Count - 1]
    $dstFile = Join-Path $dstAcc $parts[$parts.Count - 1]
    if (!(Test-Path -LiteralPath $liveFile)) { return }
    if (Test-Path -LiteralPath $dstFile) {
        if (Test-IsLink $dstFile) { Remove-LinkOnly $dstFile }
    }
    Copy-Item -LiteralPath $liveFile -Destination $dstFile -Force
}

function Test-LivingServerGd($Patch) {
    $path = [string]$Patch.Path
    if ($path -notlike 'new-client/godot/game/*.gd' -and $path -notlike 'new-client\godot\game\*.gd') { return $false }
    return [bool]$Patch.File
}

function Test-InstallerWillWrite($Patch, [string]$LiveFile) {
    $replaceAny = [bool]$Patch.ReplaceAny -or (Test-LivingServerGd $Patch)
    $create = [bool]$Patch.Create
    $exists = Test-Path -LiteralPath $LiveFile
    if (!$exists) { return ($create -or $replaceAny) }
    $hash = Get-Sha256 $LiveFile
    $fixed = ([string]$Patch.Fixed).ToLowerInvariant()
    if ($fixed -and $hash -eq $fixed) { return $false }
    if ($replaceAny -or $create) { return $true }
    $released = ([string]$Patch.Released).ToLowerInvariant()
    if ($released -and $hash -eq $released) { return $true }
    return $false
}

function Copy-ServerTree([string]$Source, [string]$Destination) {
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    foreach ($child in @(Get-ChildItem -LiteralPath $Source -Force)) {
        $to = Join-Path $Destination $child.Name
        if ($child.Name -eq 'logs') {
            New-Item -ItemType Directory -Path $to -Force | Out-Null
            continue
        }
        if ($child.Name -eq 'navmesh' -and $child.PSIsContainer) {
            New-DirectoryLink $to $child.FullName
            continue
        }
        if ($child.Name -eq 'deployment-backups' -and $child.PSIsContainer) {
            Copy-InstallRecords $child.FullName $to
            continue
        }
        if ($child.PSIsContainer) {
            if (Test-IsLink $child.FullName) { New-DirectoryLink $to $child.FullName }
            else { Copy-TreeReal $child.FullName $to }
        } else {
            Copy-Item -LiteralPath $child.FullName -Destination $to -Force
        }
    }
}

function Copy-DataFiles([string]$Source, [string]$Destination) {
    if (!(Test-Path -LiteralPath $Source)) { return }
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    foreach ($child in @(Get-ChildItem -LiteralPath $Source -Force -File)) {
        Copy-Item -LiteralPath $child.FullName -Destination (Join-Path $Destination $child.Name) -Force
    }
}

function Build-GodotTree([string]$Source, [string]$Destination) {
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    foreach ($child in @(Get-ChildItem -LiteralPath $Source -Force -File)) {
        Copy-Item -LiteralPath $child.FullName -Destination (Join-Path $Destination $child.Name) -Force
    }
    foreach ($child in @(Get-ChildItem -LiteralPath $Source -Force -Directory)) {
        $to = Join-Path $Destination $child.Name
        if ($child.Name -eq 'game') {
            Copy-TreeReal $child.FullName $to
            continue
        }
        if ($child.Name -eq '.godot') {
            New-Item -ItemType Directory -Path $to -Force | Out-Null
            foreach ($inner in @(Get-ChildItem -LiteralPath $child.FullName -Force)) {
                $innerTo = Join-Path $to $inner.Name
                if ($inner.PSIsContainer) { New-DirectoryLink $innerTo $inner.FullName }
                else { Copy-Item -LiteralPath $inner.FullName -Destination $innerTo -Force }
            }
            continue
        }
        New-DirectoryLink $to $child.FullName
    }
}

function Build-ToolsTree([string]$Source, [string]$Destination) {
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    if (!(Test-Path -LiteralPath $Source)) { return }
    foreach ($child in @(Get-ChildItem -LiteralPath $Source -Force)) {
        $to = Join-Path $Destination $child.Name
        if ($child.Name -eq 'claude-version' -and $child.PSIsContainer) {
            Copy-TreeReal $child.FullName $to
        } elseif ($child.PSIsContainer) {
            New-DirectoryLink $to $child.FullName
        } else {
            Copy-Item -LiteralPath $child.FullName -Destination $to -Force
        }
    }
}

function Seed-Staging([string]$Live, [string]$Staging) {
    New-Item -ItemType Directory -Path $Staging -Force | Out-Null
    foreach ($child in @(Get-ChildItem -LiteralPath $Live -Force -File)) {
        Copy-Item -LiteralPath $child.FullName -Destination (Join-Path $Staging $child.Name) -Force
    }
    foreach ($child in @(Get-ChildItem -LiteralPath $Live -Force -Directory)) {
        $to = Join-Path $Staging $child.Name
        if ($child.Name -eq 'runtime') {
            New-Item -ItemType Directory -Path $to -Force | Out-Null
            foreach ($inner in @(Get-ChildItem -LiteralPath $child.FullName -Force -File)) {
                Copy-Item -LiteralPath $inner.FullName -Destination (Join-Path $to $inner.Name) -Force
            }
            foreach ($inner in @(Get-ChildItem -LiteralPath $child.FullName -Force -Directory)) {
                $innerTo = Join-Path $to $inner.Name
                if ($inner.Name -eq 'server') { Copy-ServerTree $inner.FullName $innerTo }
                elseif ($inner.Name -eq 'data') { Copy-DataFiles $inner.FullName $innerTo }
                else { New-DirectoryLink $innerTo $inner.FullName }
            }
            continue
        }
        if ($child.Name -eq 'new-client') {
            New-Item -ItemType Directory -Path $to -Force | Out-Null
            foreach ($inner in @(Get-ChildItem -LiteralPath $child.FullName -Force -File)) {
                Copy-Item -LiteralPath $inner.FullName -Destination (Join-Path $to $inner.Name) -Force
            }
            foreach ($inner in @(Get-ChildItem -LiteralPath $child.FullName -Force -Directory)) {
                $innerTo = Join-Path $to $inner.Name
                if ($inner.Name -eq 'godot') { Build-GodotTree $inner.FullName $innerTo }
                elseif ($inner.Name -eq 'runtime') { Copy-TreeReal $inner.FullName $innerTo }
                else { New-DirectoryLink $innerTo $inner.FullName }
            }
            continue
        }
        if ($child.Name -eq 'tools') {
            Build-ToolsTree $child.FullName $to
            continue
        }
        New-DirectoryLink $to $child.FullName
    }
    $tools = Join-Path $Staging 'tools'
    if (!(Test-Path -LiteralPath $tools)) { New-Item -ItemType Directory -Path $tools -Force | Out-Null }
}

function Get-KeyValueFile([string]$Path) {
    $map = @{}
    foreach ($line in @(Get-Content -LiteralPath $Path)) {
        if ($line -match '^(.+?)=(.*)$') { $map[$Matches[1]] = $Matches[2] }
    }
    return $map
}

function Find-InstallScript([string]$Extracted) {
    $hit = @(Get-ChildItem -LiteralPath $Extracted -Recurse -Filter 'install-server-build.ps1' -File -ErrorAction SilentlyContinue |
        Select-Object -First 1)
    if ($hit.Count -eq 0) { return $null }
    return $hit[0].FullName
}

function Save-LiveSnapshot([string]$Live, $Patches) {
    $paths = New-Object System.Collections.Generic.List[string]
    foreach ($extra in @(
        'runtime\server\config\serverconfig.xml',
        'runtime\server\GameServer.dll',
        'runtime\server\lib\GameServer.dll',
        'new-client\godot\.godot\global_script_class_cache.cfg',
        'new-client\godot\game\zone_viewer.gd',
        'new-client\godot\game\ui\game_ui.gd',
        'PACKAGE MANIFEST.sha256'
    )) { $paths.Add($extra) }
    foreach ($patch in @($Patches)) {
        $rel = ([string]$patch.Path).Replace('/', '\')
        if ($paths -notcontains $rel) { $paths.Add($rel) }
    }
    $snap = @{}
    foreach ($rel in $paths) {
        $full = Join-Path $Live $rel
        if (Test-Path -LiteralPath $full) { $snap[$rel] = Get-Sha256 $full }
        else { $snap[$rel] = 'MISSING' }
    }
    return $snap
}

function Test-LiveChanged([string]$Live, $Snapshot) {
    if ($null -eq $Snapshot) { return '' }
    $changed = New-Object System.Collections.Generic.List[string]
    foreach ($rel in @($Snapshot.Keys)) {
        $full = Join-Path $Live $rel
        $now = 'MISSING'
        if (Test-Path -LiteralPath $full) { $now = Get-Sha256 $full }
        if ($now -ne $Snapshot[$rel]) { $changed.Add("$rel") }
    }
    if ($changed.Count -eq 0) { return '' }
    return ('LiveRoot changed: ' + ($changed -join ', '))
}

function Test-PortOpen([int]$Port) {
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $wait = $client.BeginConnect('127.0.0.1', $Port, $null, $null)
        $ok = $wait.AsyncWaitHandle.WaitOne(400, $false)
        if (!$ok) { return $false }
        $client.EndConnect($wait)
        return $true
    } catch {
        return $false
    } finally {
        $client.Close()
    }
}

function Set-SmokePorts([string]$ConfigPath) {
    if (!(Test-Path -LiteralPath $ConfigPath)) {
        $dir = Split-Path -Parent $ConfigPath
        if (!(Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
        $xml = @"
<?xml version="1.0" encoding="utf-8"?>
<root>
  <Server>
    <Port>11300</Port>
    <IP>127.0.0.1</IP>
    <RegionIP>127.0.0.1</RegionIP>
    <RegionPort>11400</RegionPort>
    <UdpIP>127.0.0.1</UdpIP>
    <UdpPort>11400</UdpPort>
    <EnableUPnP>False</EnableUPnP>
    <DetectRegionIP>False</DetectRegionIP>
  </Server>
</root>
"@
        [IO.File]::WriteAllText($ConfigPath, $xml)
        return $true
    }
    $text = [IO.File]::ReadAllText($ConfigPath)
    $text = [regex]::Replace($text, '<Port>\s*\d+\s*</Port>', '<Port>11300</Port>', 1)
    $text = [regex]::Replace($text, '<RegionPort>\s*\d+\s*</RegionPort>', '<RegionPort>11400</RegionPort>')
    $text = [regex]::Replace($text, '<UdpPort>\s*\d+\s*</UdpPort>', '<UdpPort>11400</UdpPort>')
    $text = [regex]::Replace($text, '<EnableUPnP>\s*[^<]*\s*</EnableUPnP>', '<EnableUPnP>False</EnableUPnP>')
    $text = [regex]::Replace($text, '<IP>\s*[^<]*\s*</IP>', '<IP>127.0.0.1</IP>', 1)
    $text = [regex]::Replace($text, '<RegionIP>\s*[^<]*\s*</RegionIP>', '<RegionIP>127.0.0.1</RegionIP>')
    $text = [regex]::Replace($text, '<UdpIP>\s*[^<]*\s*</UdpIP>', '<UdpIP>127.0.0.1</UdpIP>')
    if ($text -notmatch '<Port>11300</Port>') { return $false }
    if ($text -match '<Port>10300</Port>') { return $false }
    if ($text -match '<RegionPort>10400</RegionPort>') { return $false }
    if ($text -match '<UdpPort>10400</UdpPort>') { return $false }
    [IO.File]::WriteAllText($ConfigPath, $text)
    return $true
}

function Find-ZoneInfo([string]$GodotRoot, [string]$ZoneName) {
    $zones = Join-Path $GodotRoot 'content\zones'
    if (!(Test-Path -LiteralPath $zones)) { return $null }
    if ($ZoneName -match '^\d+$') {
        $id = [int]$ZoneName
        $json = Join-Path $zones (('zone{0:d3}' -f $id) + '\zone.json')
        if (!(Test-Path -LiteralPath $json)) { return $null }
        $name = ''
        $text = Get-Content -LiteralPath $json -Raw
        if ($text -match '"name"\s*:\s*"([^"]+)"') { $name = $Matches[1] }
        return @{ Id = $id; Name = $name }
    }
    foreach ($dir in @(Get-ChildItem -LiteralPath $zones -Force -Directory)) {
        if ($dir.Name -notmatch '^zone(\d+)$') { continue }
        $id = [int]$Matches[1]
        $json = Join-Path $dir.FullName 'zone.json'
        if (!(Test-Path -LiteralPath $json)) { continue }
        $text = Get-Content -LiteralPath $json -Raw
        if ($text -match '"name"\s*:\s*"([^"]+)"' -and $Matches[1] -eq $ZoneName) {
            return @{ Id = $id; Name = $Matches[1] }
        }
    }
    return $null
}

function Find-GodotExe([string]$Staging, [string]$Explicit) {
    if ($Explicit) {
        if (Test-Path -LiteralPath $Explicit) { return (Get-Item -LiteralPath $Explicit).FullName }
        return $null
    }
    $dir = Join-Path $Staging 'new-client\tools\godot_net'
    if (!(Test-Path -LiteralPath $dir)) { return $null }
    $hit = @(Get-ChildItem -LiteralPath $dir -Force -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -like 'Godot*.exe' })
    if ($hit.Count -eq 0) { return $null }
    return $hit[0].FullName
}

function Stop-ProcessIfUnder([string]$Root, [string[]]$Names) {
    foreach ($proc in @(Get-Process -Name $Names -ErrorAction SilentlyContinue)) {
        $path = ''
        try { $path = $proc.Path } catch { $path = '' }
        if ($path -and $path.StartsWith($Root, [StringComparison]::OrdinalIgnoreCase)) {
            Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        }
    }
}

function Get-LiveGameRunning([string]$Staging) {
    $names = @('CoreServer', 'DOLServer', 'OfflineDAoC', 'game', 'camelot', 'connect', 'Godot_v4.7.2-stable_mono_win64')
    $bad = New-Object System.Collections.Generic.List[string]
    foreach ($proc in @(Get-Process -Name $names -ErrorAction SilentlyContinue)) {
        $path = ''
        try { $path = $proc.Path } catch { $path = '' }
        if ($path -and $Staging -and $path.StartsWith($Staging, [StringComparison]::OrdinalIgnoreCase)) { continue }
        $bad.Add($proc.ProcessName)
    }
    return @($bad)
}

# PowerShell event handlers cannot run on the process-output thread ("no Runspace"). The copy
# stays in C# so Windows PowerShell 5.1 and pwsh both drain stdout without that crash.
if (-not ('SmokeIo' -as [type])) {
    Add-Type -TypeDefinition @'
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
public static class SmokeIo {
    public static Task OutTask;
    public static Task ErrTask;
    public static Process Start(string file, string args, string work, string stdout, string stderr) {
        var psi = new ProcessStartInfo(file, args);
        if (!string.IsNullOrEmpty(work)) psi.WorkingDirectory = work;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        var p = new Process();
        p.StartInfo = psi;
        p.Start();
        var outWriter = new StreamWriter(new FileStream(stdout, FileMode.Create, FileAccess.Write, FileShare.ReadWrite));
        var errWriter = new StreamWriter(new FileStream(stderr, FileMode.Create, FileAccess.Write, FileShare.ReadWrite));
        outWriter.AutoFlush = true;
        errWriter.AutoFlush = true;
        OutTask = Task.Run(() => {
            string line;
            while ((line = p.StandardOutput.ReadLine()) != null) outWriter.WriteLine(line);
            outWriter.Dispose();
        });
        ErrTask = Task.Run(() => {
            string line;
            while ((line = p.StandardError.ReadLine()) != null) errWriter.WriteLine(line);
            errWriter.Dispose();
        });
        return p;
    }
    public static void Drain() {
        if (OutTask != null) OutTask.Wait(10000);
        if (ErrTask != null) ErrTask.Wait(10000);
    }
}
'@
}

function Start-Captured([string]$File, [string]$Arguments, [string]$WorkingDirectory, [string]$StdoutPath, [string]$StderrPath) {
    return [SmokeIo]::Start($File, $Arguments, $WorkingDirectory, $StdoutPath, $StderrPath)
}

function Write-SmokeReport([int]$Code, [string]$Result, [string]$Summary) {
    if (!$script:ResultsDir) { return }
    if (!(Test-Path -LiteralPath $script:ResultsDir)) {
        New-Item -ItemType Directory -Path $script:ResultsDir -Force | Out-Null
    }
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("tag: $script:TagName")
    $lines.Add("result: $Result")
    $lines.Add("exit: $Code")
    $lines.Add("summary: $Summary")
    foreach ($line in $script:Detail) { $lines.Add($line) }
    $report = Join-Path $script:ResultsDir 'report.txt'
    [IO.File]::WriteAllLines($report, $lines)
    if ($script:GodotLog -and (Test-Path -LiteralPath $script:GodotLog)) {
        $all = @(Get-Content -LiteralPath $script:GodotLog -ErrorAction SilentlyContinue)
        $tail = @()
        if ($all.Count -gt 80) { $tail = $all[($all.Count - 80)..($all.Count - 1)] }
        else { $tail = $all }
        [IO.File]::WriteAllLines((Join-Path $script:ResultsDir 'godot-log-tail.txt'), $tail)
    }
    Write-Host "report: $report"
}

function Complete-Smoke([int]$Code, [string]$Result, [string]$Summary) {
    $changed = ''
    if ($script:LiveFull -and $script:Snapshot) {
        $changed = Test-LiveChanged $script:LiveFull $script:Snapshot
    }
    if ($changed) {
        $Code = 1
        $Result = 'FAIL'
        $Summary = "$changed $Summary"
        Add-Detail $changed
    }
    Write-SmokeReport $Code $Result $Summary
    exit $Code
}

if (!$StagingRoot) {
    if (!$env:LOCALAPPDATA) { Write-Host 'BLOCKED: LOCALAPPDATA is not set'; exit 2 }
    $StagingRoot = Join-Path $env:LOCALAPPDATA 'LivingServerSmoke\staging'
}
if (!$ResultsRoot) {
    if (!$env:LOCALAPPDATA) { Write-Host 'BLOCKED: LOCALAPPDATA is not set'; exit 2 }
    $ResultsRoot = Join-Path $env:LOCALAPPDATA 'LivingServerSmoke\results'
}

$script:TagName = $Tag
if (!$script:TagName) { $script:TagName = [IO.Path]::GetFileNameWithoutExtension($ReleaseZip) }
$script:ResultsDir = Join-Path $ResultsRoot $script:TagName

try {
    if (!(Test-Path -LiteralPath $ReleaseZip)) {
        Complete-Smoke 2 'BLOCKED' "Release zip not found: $ReleaseZip"
    }
    if (!(Test-Path -LiteralPath $LiveRoot)) {
        Complete-Smoke 2 'BLOCKED' "LiveRoot not found: $LiveRoot"
    }
    $script:LiveFull = [IO.Path]::GetFullPath($LiveRoot).TrimEnd('\', '/')
    $stagingFull = [IO.Path]::GetFullPath($StagingRoot).TrimEnd('\', '/')
    $sep = [IO.Path]::DirectorySeparatorChar
    if ($stagingFull.StartsWith($script:LiveFull + $sep, [StringComparison]::OrdinalIgnoreCase) -or
        $stagingFull -eq $script:LiveFull) {
        Complete-Smoke 2 'BLOCKED' 'StagingRoot is inside LiveRoot. Pick a folder outside the game.'
    }
    if ($script:LiveFull.StartsWith($stagingFull + $sep, [StringComparison]::OrdinalIgnoreCase)) {
        Complete-Smoke 2 'BLOCKED' 'LiveRoot is inside StagingRoot.'
    }
    $project = Join-Path $script:LiveFull 'new-client\godot\project.godot'
    if (!(Test-Path -LiteralPath $project)) {
        Complete-Smoke 2 'BLOCKED' 'LiveRoot has no new-client\godot\project.godot (this smoke needs the 0.37c client).'
    }

    $extract = Join-Path ([IO.Path]::GetTempPath()) ('ls-smoke-' + [guid]::NewGuid().ToString('N'))
    Expand-Archive -LiteralPath $ReleaseZip -DestinationPath $extract
    $install = Find-InstallScript $extract
    if (!$install) { Complete-Smoke 2 'BLOCKED' 'The zip has no install-server-build.ps1.' }
    $buildDir = Split-Path -Parent $install
    $infoPath = Join-Path $buildDir 'BUILD-INFO.txt'
    if (!(Test-Path -LiteralPath $infoPath)) { Complete-Smoke 2 'BLOCKED' 'The zip has no BUILD-INFO.txt.' }
    $info = Get-KeyValueFile $infoPath
    if ($info['BuildName']) {
        $script:TagName = $info['BuildName']
        if ($script:TagName -notmatch '^v') { $script:TagName = 'v' + $script:TagName }
        if ($Tag) { $script:TagName = $Tag }
        $script:ResultsDir = Join-Path $ResultsRoot $script:TagName
    }
    Add-Detail "tag: $script:TagName"
    Add-Detail "live: $script:LiveFull"
    Add-Detail "staging: $stagingFull"

    $patchPath = Join-Path $buildDir 'content-patches.json'
    $patches = @()
    if (Test-Path -LiteralPath $patchPath) {
        $patches = @(Get-Content -LiteralPath $patchPath -Raw | ConvertFrom-Json)
    }
    $script:Snapshot = Save-LiveSnapshot $script:LiveFull $patches

    $running = @(Get-LiveGameRunning $stagingFull)
    if ($running.Count -gt 0) {
        Complete-Smoke 2 'BLOCKED' ("The live game, launcher or server is running (" + ($running -join ', ') + '). Smoke will retry.')
    }

    Stop-ProcessIfUnder $stagingFull @('CoreServer', 'DOLServer', 'Godot_v4.7.2-stable_mono_win64', 'Godot_v4.7.2-stable_linux.x86_64')
    if (Test-Path -LiteralPath $stagingFull) { Remove-TreeNoFollow $stagingFull }
    Add-Detail 'seeding staging (junctions for asset trees, copies for files the installer writes)'
    Seed-Staging $script:LiveFull $stagingFull
    foreach ($patch in $patches) {
        $rel = [string]$patch.Path
        $liveFile = Join-Path $script:LiveFull ($rel.Replace('/', '\'))
        if (Test-InstallerWillWrite $patch $liveFile) {
            Add-Detail "materialise (installer would write): $rel"
            Protect-PatchFile $script:LiveFull $stagingFull $rel
        }
    }

    if (!(Test-Path -LiteralPath $script:ResultsDir)) {
        New-Item -ItemType Directory -Path $script:ResultsDir -Force | Out-Null
    }
    Add-Detail 'running install-server-build.ps1 into staging'
    # A child process so the installer's `exit` does not end this script. Arguments are one quoted
    # string so a path with spaces (E:\Offline DAOC) is not split on Windows PowerShell 5.1.
    $shellExe = (Get-Process -Id $PID).Path
    $installOut = Join-Path $script:ResultsDir 'install-stdout.txt'
    $installErr = Join-Path $script:ResultsDir 'install-stderr.txt'
    $installArgs = '-NoProfile -ExecutionPolicy Bypass -File "' + $install + '" -GameFolder "' + $stagingFull + '"'
    $installProc = Start-Captured $shellExe $installArgs '' $installOut $installErr
    if (!$installProc.WaitForExit(20 * 60 * 1000)) {
        try { $installProc.Kill() } catch { }
        Complete-Smoke 1 'FAIL' 'install-server-build.ps1 did not finish within 20 minutes.'
    }
    # The no-argument wait flushes redirected output after the process has already exited.
    $installProc.WaitForExit() | Out-Null
    [SmokeIo]::Drain()
    if ($installProc.ExitCode -ne 0) {
        Complete-Smoke 1 'FAIL' "install-server-build.ps1 exited $($installProc.ExitCode)"
    }

    $cache = Join-Path $stagingFull 'new-client\godot\.godot\global_script_class_cache.cfg'
    if (!(Test-Path -LiteralPath $cache)) {
        Complete-Smoke 1 'FAIL' 'Class cache is missing after install.'
    }
    $cacheText = Get-Content -LiteralPath $cache -Raw
    $missing = @()
    foreach ($name in $script:BootClasses) {
        if ($cacheText -notmatch ('"class":\s*&"' + [regex]::Escape($name) + '"')) { $missing += $name }
    }
    if ($missing.Count -gt 0) {
        Complete-Smoke 1 'FAIL' ('Class cache is missing boot classes: ' + ($missing -join ', '))
    }
    Add-Detail 'class cache: boot classes present'
    foreach ($patch in $patches) {
        $must = [bool]$patch.ReplaceAny -or [bool]$patch.Create -or (Test-LivingServerGd $patch)
        if (!$must -or !$patch.File) { continue }
        $rel = ([string]$patch.Path).Replace('/', '\')
        $full = Join-Path $stagingFull $rel
        if (!(Test-Path -LiteralPath $full)) {
            Complete-Smoke 1 'FAIL' "Patched file missing after install: $rel"
        }
        $hash = Get-Sha256 $full
        $want = ([string]$patch.Fixed).ToLowerInvariant()
        if ($hash -ne $want) {
            Complete-Smoke 1 'FAIL' "Patched hash mismatch: $rel"
        }
    }
    Add-Detail 'patched hashes: ReplaceAny and Create files match Fixed'

    $config = Join-Path $stagingFull 'runtime\server\config\serverconfig.xml'
    if (!(Set-SmokePorts $config)) {
        Complete-Smoke 1 'FAIL' 'Could not point the staging serverconfig.xml at ports 11300 and 11400.'
    }
    $configText = [IO.File]::ReadAllText($config)
    if ($configText -match '<Port>10300</Port>' -or $configText -notmatch '<Port>11300</Port>') {
        Complete-Smoke 1 'FAIL' 'Staging serverconfig.xml still uses the live port.'
    }
    if (Test-PortOpen $script:SmokePort) {
        Complete-Smoke 2 'BLOCKED' "Port $script:SmokePort is already in use. Smoke will retry."
    }
    $serverExe = Join-Path $stagingFull 'runtime\server\CoreServer.exe'
    if (!(Test-Path -LiteralPath $serverExe)) {
        Complete-Smoke 2 'BLOCKED' 'Staging has no runtime\server\CoreServer.exe, so the server boot cannot be checked.'
    }
    Add-Detail "starting staging server on port $script:SmokePort"
    $server = New-Object System.Diagnostics.Process
    $server.StartInfo.FileName = $serverExe
    $server.StartInfo.WorkingDirectory = (Join-Path $stagingFull 'runtime\server')
    $server.StartInfo.Arguments = '--start'
    $server.StartInfo.UseShellExecute = $false
    $server.StartInfo.RedirectStandardInput = $true
    $server.StartInfo.CreateNoWindow = $true
    [void]$server.Start()
    $deadline = (Get-Date).AddSeconds($ServerTimeoutSeconds)
    $listening = $false
    while ((Get-Date) -lt $deadline) {
        if ($server.HasExited) { break }
        if (Test-PortOpen $script:SmokePort) { $listening = $true; break }
        Start-Sleep -Milliseconds 500
    }
    if ($listening) {
        Add-Detail "server: listening on $script:SmokePort"
        try {
            $server.StandardInput.WriteLine('exit')
        } catch { }
        if (!$server.WaitForExit(20000)) {
            try { $server.Kill() } catch { }
            $server.WaitForExit(5000) | Out-Null
            Add-Detail 'server: stopped with kill after exit was ignored'
        } else {
            Add-Detail 'server: stopped with exit'
        }
    } else {
        try { if (!$server.HasExited) { $server.Kill() } } catch { }
        Complete-Smoke 1 'FAIL' "Staging server did not listen on port $script:SmokePort within $ServerTimeoutSeconds s."
    }
    if (Test-PortOpen 10300) {
        Add-Detail 'note: something is listening on 10300 (left alone; staging used 11300)'
    }

    $zoneInfo = Find-ZoneInfo (Join-Path $stagingFull 'new-client\godot') $Zone
    if ($null -eq $zoneInfo) {
        Complete-Smoke 2 'BLOCKED' "Zone '$Zone' was not found under new-client\godot\content\zones (no zone.json with that name)."
    }
    Add-Detail ("zone: {0} id {1}" -f $zoneInfo.Name, $zoneInfo.Id)
    $godot = Find-GodotExe $stagingFull $GodotPath
    if (!$godot) {
        Complete-Smoke 2 'BLOCKED' 'Godot executable not found under new-client\tools\godot_net and -GodotPath was not set.'
    }
    $saveDir = Join-Path $stagingFull 'new-client\smoke-save'
    New-Item -ItemType Directory -Path $saveDir -Force | Out-Null
    $logDir = Join-Path $script:ResultsDir 'logs'
    if (!(Test-Path -LiteralPath $logDir)) { New-Item -ItemType Directory -Path $logDir -Force | Out-Null }
    $stdout = Join-Path $logDir 'godot-stdout.txt'
    $stderr = Join-Path $logDir 'godot-stderr.txt'
    $script:GodotLog = Join-Path $logDir 'godot.log'
    $godotPathArg = Join-Path $stagingFull 'new-client\godot'
    $godotArgs = '--headless --path "' + $godotPathArg + '" res://game/zone_viewer.tscn -- --zone=' +
        $zoneInfo.Id + ' --smoke --mute --single-zone'
    Add-Detail "godot: $godotArgs"
    $savedApp = $env:APPDATA
    $savedXdg = $env:XDG_DATA_HOME
    $env:APPDATA = $saveDir
    $env:XDG_DATA_HOME = $saveDir
    $godotProc = $null
    try {
        $godotProc = Start-Captured $godot $godotArgs (Join-Path $stagingFull 'new-client') $stdout $stderr
        if (!$godotProc.WaitForExit($GodotTimeoutSeconds * 1000)) {
            try { $godotProc.Kill() } catch { }
            $godotProc.WaitForExit(5000) | Out-Null
            $timedOut = $true
        } else {
            $timedOut = $false
            $godotProc.WaitForExit() | Out-Null
            [SmokeIo]::Drain()
        }
    } finally {
        if ($null -eq $savedApp) { Remove-Item Env:APPDATA -ErrorAction SilentlyContinue }
        else { $env:APPDATA = $savedApp }
        if ($null -eq $savedXdg) { Remove-Item Env:XDG_DATA_HOME -ErrorAction SilentlyContinue }
        else { $env:XDG_DATA_HOME = $savedXdg }
    }
    try { [SmokeIo]::Drain() } catch { }
    $combined = ''
    if (Test-Path -LiteralPath $stdout) { $combined += [IO.File]::ReadAllText($stdout) }
    if (Test-Path -LiteralPath $stderr) { $combined += "`n" + [IO.File]::ReadAllText($stderr) }
    [IO.File]::WriteAllText($script:GodotLog, $combined)
    $builtPattern = '(?m)^LS_SMOKE ZONE_BUILT ' + [regex]::Escape([string]$zoneInfo.Name) + '\s*$'
    $worldPattern = '(?m)^LS_SMOKE IN_WORLD ' + [regex]::Escape([string]$zoneInfo.Name) + '\s*$'
    $built = [regex]::IsMatch($combined, $builtPattern)
    $inWorld = [regex]::IsMatch($combined, $worldPattern)
    $scriptErrors = ([regex]::Matches($combined, 'SCRIPT ERROR')).Count
    $parseErrors = ([regex]::Matches($combined, 'Parse Error')).Count
    Add-Detail "markers: ZONE_BUILT=$built IN_WORLD=$inWorld script_errors=$scriptErrors parse_errors=$parseErrors timeout=$timedOut"
    if ($timedOut) {
        Complete-Smoke 1 'FAIL' "Godot did not exit within $GodotTimeoutSeconds s."
    }
    if ($built -and $inWorld -and $scriptErrors -eq 0 -and $parseErrors -eq 0) {
        Complete-Smoke 0 'PASS' ("LS_SMOKE ZONE_BUILT {0} and LS_SMOKE IN_WORLD {0}" -f $zoneInfo.Name)
    }
    Complete-Smoke 1 'FAIL' 'Godot finished without both smoke markers, or the log has SCRIPT ERROR / Parse Error.'
} catch {
    $message = $_.Exception.Message
    if ($message -like 'BLOCKED:*') {
        Complete-Smoke 2 'BLOCKED' $message
    }
    Add-Detail $message
    Complete-Smoke 1 'FAIL' $message
}
