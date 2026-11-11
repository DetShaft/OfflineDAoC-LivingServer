# Watches the public GitHub releases of DetShaft/OfflineDAoC-LivingServer (no token).
# Each run downloads the newest tag that is not in seen-tags.txt and runs Invoke-WorldEntrySmoke.ps1.
# The tag is recorded after a pass (0) or a fail (1). A blocked run (2) is left unseen so the next
# run tries it again. PowerShell 5.1 compatible.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\smoke\Watch-Releases.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\smoke\Watch-Releases.ps1 -SeedSeen
param(
    [string]$LiveRoot = 'E:\Offline DAOC',
    [string]$StagingRoot = '',
    [string]$Zone = 'Gotar',
    [string]$GodotPath = '',
    [string]$Repo = 'DetShaft/OfflineDAoC-LivingServer',
    [switch]$SeedSeen
)

$ErrorActionPreference = 'Stop'

function Get-SmokeHome {
    if (!$env:LOCALAPPDATA) { throw 'LOCALAPPDATA is not set' }
    $dir = Join-Path $env:LOCALAPPDATA 'LivingServerSmoke'
    if (!(Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    return $dir
}

function Write-WatcherLog([string]$Dir, [string]$Line) {
    $path = Join-Path $Dir 'watcher.log'
    $stamp = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    Add-Content -LiteralPath $path -Value ("$stamp  $Line") -Encoding ASCII
    Write-Host $Line
}

function Get-SeenTags([string]$Path) {
    if (!(Test-Path -LiteralPath $Path)) { return @() }
    return @((Get-Content -LiteralPath $Path | ForEach-Object { $_.Trim() } | Where-Object { $_ -and $_ -notmatch '^#' }))
}

function Add-SeenTag([string]$Path, [string]$Tag) {
    $seen = @(Get-SeenTags $Path)
    if ($seen -contains $Tag) { return }
    Add-Content -LiteralPath $Path -Value $Tag -Encoding ASCII
}

try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch { }

$homeDir = Get-SmokeHome
$seenPath = Join-Path $homeDir 'seen-tags.txt'
$downloadDir = Join-Path $homeDir 'downloads'
if (!(Test-Path -LiteralPath $downloadDir)) { New-Item -ItemType Directory -Path $downloadDir -Force | Out-Null }

$headers = @{ 'User-Agent' = 'LivingServerSmoke'; 'Accept' = 'application/vnd.github+json' }
$uri = "https://api.github.com/repos/$Repo/releases?per_page=100"
Write-WatcherLog $homeDir "GET $uri"
# Invoke-RestMethod returns the JSON array as one object. foreach enumerates it; wrapping
# it in @() does not, and then .draft is a list of false values which counts as true.
$raw = Invoke-RestMethod -Uri $uri -Headers $headers -Method Get
$releaseList = New-Object System.Collections.Generic.List[object]
if ($raw -is [System.Array]) {
    foreach ($release in $raw) { $releaseList.Add($release) }
} elseif ($null -ne $raw) {
    $releaseList.Add($raw)
}
$usable = @()
foreach ($release in $releaseList) {
    if ($release.draft -eq $true) { continue }
    $zip = $null
    foreach ($asset in @($release.assets)) {
        if ([string]$asset.name -like 'OfflineDAoC-LivingServer-*.zip') { $zip = $asset; break }
    }
    if ($null -eq $zip) { continue }
    $usable += [pscustomobject]@{
        Tag = [string]$release.tag_name
        Published = [string]$release.published_at
        Name = [string]$zip.name
        Url = [string]$zip.browser_download_url
    }
}
$usable = @($usable | Sort-Object Published -Descending)

if ($SeedSeen) {
    foreach ($one in $usable) { Add-SeenTag $seenPath $one.Tag }
    Write-WatcherLog $homeDir ("Seeded " + $usable.Count + " tag(s) without testing. Later runs only see newer tags.")
    exit 0
}

$seen = @(Get-SeenTags $seenPath)
$pending = @($usable | Where-Object { $seen -notcontains $_.Tag })
if ($pending.Count -eq 0) {
    Write-WatcherLog $homeDir 'No unseen release zip.'
    exit 0
}

# One release per run so a 30-minute task does not pile up. Newest first: the release just published
# is the one that matters, and older tags are walked on later runs.
$target = $pending[0]
Write-WatcherLog $homeDir ("Smoke " + $target.Tag + " (" + $pending.Count + " unseen)")
$zipPath = Join-Path $downloadDir ($target.Tag + '.zip')
if (!(Test-Path -LiteralPath $zipPath) -or ((Get-Item -LiteralPath $zipPath).Length -lt 1024)) {
    Write-WatcherLog $homeDir ("Download " + $target.Url)
    Invoke-WebRequest -Uri $target.Url -OutFile $zipPath -UseBasicParsing -Headers $headers
}

$smoke = Join-Path $PSScriptRoot 'Invoke-WorldEntrySmoke.ps1'
$shellExe = (Get-Process -Id $PID).Path
$arg = '-NoProfile -ExecutionPolicy Bypass -File "' + $smoke + '" -ReleaseZip "' + $zipPath +
    '" -LiveRoot "' + $LiveRoot + '" -Zone "' + $Zone + '" -Tag "' + $target.Tag + '"'
if ($StagingRoot) { $arg += ' -StagingRoot "' + $StagingRoot + '"' }
if ($GodotPath) { $arg += ' -GodotPath "' + $GodotPath + '"' }
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $shellExe
$psi.Arguments = $arg
$psi.UseShellExecute = $false
$proc = New-Object System.Diagnostics.Process
$proc.StartInfo = $psi
[void]$proc.Start()
$proc.WaitForExit()
$code = $proc.ExitCode
Write-WatcherLog $homeDir ("$($target.Tag) exit $code")
if ($code -eq 0 -or $code -eq 1) {
    Add-SeenTag $seenPath $target.Tag
    Write-WatcherLog $homeDir ("Recorded $($target.Tag)")
} else {
    Write-WatcherLog $homeDir ("$($target.Tag) blocked; not recorded")
}
exit $code
