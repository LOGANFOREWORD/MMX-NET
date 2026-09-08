#Requires -Version 5.1
<#
.SYNOPSIS
  Genera dist\update\ (zip overlay + manifest) da pubblicare sul feed.
  Usa il launcher UTENTE (MMX-Net-Launcher.exe), non il -dev.
#>
param(
    [string]$Version = "",
    [string]$Notes = "",
    [string]$Channel = "",
    [string]$PackageUrl = "",
    [string]$Root = "",
    [switch]$SkipLauncherPublish
)

$ErrorActionPreference = "Stop"
$toolkit = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $Root) { $Root = Split-Path -Parent $toolkit }
$launcherProj = Join-Path $toolkit "MmxNetLauncher\MmxNetLauncher.csproj"
$outUpdate = Join-Path $Root "dist\update"
$verFile = Join-Path $Root "ac_version.json"
$devPub = Join-Path $Root "ac_dev_publish.json"

Write-Host ("Root: {0}" -f $Root)

if (-not (Test-Path -LiteralPath $verFile)) {
    throw "Manca ac_version.json in $Root"
}

$verObj = Get-Content -LiteralPath $verFile -Raw | ConvertFrom-Json
if ($Version) { $verObj.Version = $Version }
if ($Notes) { $verObj.Notes = $Notes }
if ($Channel) { $verObj.Channel = $Channel }

$verObj | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $verFile -Encoding UTF8
Write-Host ("Versione pack: {0} [{1}]" -f $verObj.Version, $verObj.Channel)

if (-not $SkipLauncherPublish) {
    $userStage = Join-Path $Root "dist\user"
    New-Item -ItemType Directory -Force -Path $userStage | Out-Null
    Write-Host ("Publish launcher UTENTE -> {0}" -f $userStage)
    Get-Process -Name "MMX-Net-Launcher","MMX-Net-Launcher-dev" -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue
    # Publish in stage separata: non cancellare MMX-Net-Launcher-dev.exe in Root
    dotnet publish $launcherProj -c Release -o $userStage -p:BuildFlavor=User
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Copy-Item -LiteralPath (Join-Path $userStage "MMX-Net-Launcher.exe") -Destination $Root -Force
}

New-Item -ItemType Directory -Force -Path $outUpdate | Out-Null
$zipPath = Join-Path $outUpdate "ac-update.zip"
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }

$stage = Join-Path $env:TEMP ("ac_pub_" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $stage | Out-Null
try {
    # ac_config.json NON va nell'update (preserva updateFeedUrl amici).
    # Solo launcher utente — mai -dev.
    $rootFiles = @(
        "ac_version.json",
        "ac_steam_cop_bridge.cmd",
        "ac_steam_launch.args",
        "steam_appid.txt",
        "MMX-Net-Launcher.exe"
        # MMX-Net-Uninstaller.exe: solo installer/download zip (self-contained ~55MB;
        # con launcher supera il limite 100MB di GitHub raw sul feed).
    )
    foreach ($f in $rootFiles) {
        $src = Join-Path $Root $f
        if (Test-Path -LiteralPath $src) {
            Copy-Item -LiteralPath $src -Destination (Join-Path $stage $f) -Force
        }
    }

    $include = Join-Path $toolkit "pack\ac_pack_include.txt"
    if (Test-Path -LiteralPath $include) {
        Get-Content -LiteralPath $include | ForEach-Object {
            $line = $_.Trim()
            if (-not $line -or $line.StartsWith("#") -or $line.StartsWith(";")) { return }
            $rel = $line -replace "/", "\"
            $src = Join-Path $Root $rel
            if (Test-Path -LiteralPath $src) {
                $dst = Join-Path $stage $rel
                $dstDir = Split-Path -Parent $dst
                New-Item -ItemType Directory -Force -Path $dstDir | Out-Null
                Copy-Item -LiteralPath $src -Destination $dst -Force
            }
            else {
                Write-Host ("SKIP (manca): {0}" -f $rel)
            }
        }
    }

    Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zipPath -CompressionLevel Optimal
}
finally {
    Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
}

$sha = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
Copy-Item -LiteralPath $verFile -Destination (Join-Path $outUpdate "ac_version.json") -Force

# FeedBaseUrl / PublishTarget da ac_dev_publish.json
# Feed pubblico amici = MMX-NET (repo pubblica).
$publicFeed = "https://raw.githubusercontent.com/LOGANFOREWORD/MMX-NET/main/"
$feedBase = ""
$syncTarget = ""
if (Test-Path -LiteralPath $devPub) {
    try {
        $dp = Get-Content -LiteralPath $devPub -Raw | ConvertFrom-Json
        if ($dp.FeedBaseUrl) { $feedBase = [string]$dp.FeedBaseUrl }
        if ($dp.PublishTarget) { $syncTarget = ([string]$dp.PublishTarget).Trim() }
    }
    catch {
        Write-Host ("Nota: ac_dev_publish.json non letto: {0}" -f $_.Exception.Message)
    }
}
if (-not $feedBase -or ($feedBase -match '(?i)MMX-NET-feed')) {
    $feedBase = $publicFeed
    Write-Host "FeedBaseUrl allineato a MMX-NET (pubblico)."
    if (Test-Path -LiteralPath $devPub) {
        try {
            $dpFix = Get-Content -LiteralPath $devPub -Raw | ConvertFrom-Json
            $dpFix | Add-Member -NotePropertyName FeedBaseUrl -NotePropertyValue $publicFeed -Force
            ($dpFix | ConvertTo-Json -Depth 5) | Set-Content -LiteralPath $devPub -Encoding UTF8
        } catch { }
    }
}

if (-not $PackageUrl) {
    if ($feedBase) {
        $PackageUrl = ($feedBase.TrimEnd('/') + "/") + "ac-update.zip"
    }
    else {
        $PackageUrl = "ac-update.zip"
    }
}

$manifest = [ordered]@{
    Name               = $verObj.Name
    Version            = $verObj.Version
    Channel            = $verObj.Channel
    Protocol           = $verObj.Protocol
    Engine             = $verObj.Engine
    Notes              = $verObj.Notes
    PackageUrl         = $PackageUrl
    PackageSha256      = $sha
    PublishedUtc       = [DateTime]::UtcNow.ToString("o")
    MinLauncherVersion = "0.1.0"
}
$manifestPath = Join-Path $outUpdate "ac_update_manifest.json"
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

if ($syncTarget) {
    New-Item -ItemType Directory -Force -Path $syncTarget | Out-Null
    Copy-Item -LiteralPath $zipPath -Destination (Join-Path $syncTarget "ac-update.zip") -Force
    Copy-Item -LiteralPath (Join-Path $outUpdate "ac_version.json") -Destination (Join-Path $syncTarget "ac_version.json") -Force
    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $syncTarget "ac_update_manifest.json") -Force
    Write-Host ("Sync publishTarget: {0}" -f $syncTarget)
}

$distInstaller = Join-Path $Root "dist\installer"
New-Item -ItemType Directory -Force -Path $distInstaller | Out-Null
Copy-Item -LiteralPath $zipPath -Destination (Join-Path $distInstaller "ac-overlay.zip") -Force

Write-Host ""
Write-Host ("OK - cartella da caricare sul feed: {0}" -f $outUpdate)
Write-Host "File: ac_update_manifest.json, ac-update.zip, ac_version.json"
Write-Host ("SHA-256: {0}" -f $sha)
Write-Host ""
if ($feedBase) {
    Write-Host ("updateFeedUrl amici: {0}" -f ($feedBase.TrimEnd('/') + "/"))
}
else {
    Write-Host "Poi imposta negli amici (ac_config.json) updateFeedUrl verso questa cartella."
    Write-Host "Oppure salva FeedBaseUrl in ac_dev_publish.json (e usa il Devkit)."
}
Write-Host "Se PackageUrl e relativo, il client lo risolve rispetto alla cartella del feed."
Write-Host "Per URL assoluto usa -PackageUrl https://host/path/ac-update.zip"
