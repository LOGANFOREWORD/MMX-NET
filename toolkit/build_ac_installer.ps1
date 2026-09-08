#Requires -Version 5.1
<#
.SYNOPSIS
  Build launcher utente + devkit + installer + overlay zip in dist\installer\
#>
param(
    [string]$Root = ""
)

$ErrorActionPreference = "Stop"
$toolkit = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $Root) { $Root = Split-Path -Parent $toolkit }
$launcherProj = Join-Path $toolkit "MmxNetLauncher\MmxNetLauncher.csproj"
$installerProj = Join-Path $toolkit "MmxNetInstaller\MmxNetInstaller.csproj"
$outInstaller = Join-Path $Root "dist\installer"
$outUpdate = Join-Path $Root "dist\update"
$outDevkit = Join-Path $Root "dist\devkit"
$outDownload = Join-Path $Root "dist\download"
$devPub = Join-Path $Root "ac_dev_publish.json"
$userCfgTemplate = Join-Path $toolkit "pack\ac_config.user.json"

Write-Host ("=== MMX-Net: build installer + launcher User/Dev ===")
Write-Host ("Root: {0}" -f $Root)

Get-Process -Name "MMX-Net-Launcher","MMX-Net-Launcher-dev","MMX-Net-Installer" -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue

# Nota: due publish sulla stessa cartella si sovrascrivono a vicenda (clean).
# Pubblichiamo in cartelle separate e poi copiamo entrambi gli exe in Root.
$userStage = Join-Path $Root "dist\user"
New-Item -ItemType Directory -Force -Path $userStage | Out-Null
New-Item -ItemType Directory -Force -Path $outDevkit | Out-Null

Write-Host ("1) Publish launcher UTENTE -> {0}" -f $userStage)
dotnet publish $launcherProj -c Release -o $userStage -p:BuildFlavor=User
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ("2) Publish launcher DEVKIT -> {0}" -f $outDevkit)
dotnet publish $launcherProj -c Release -o $outDevkit -p:BuildFlavor=Dev
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "2b) Copia entrambi gli exe in Root"
Copy-Item -LiteralPath (Join-Path $userStage "MMX-Net-Launcher.exe") -Destination $Root -Force
Copy-Item -LiteralPath (Join-Path $outDevkit "MMX-Net-Launcher-dev.exe") -Destination $Root -Force

Write-Host "3) Genera update pack (zip + manifest) - solo launcher utente"
& (Join-Path $toolkit "publish_ac_update.ps1") -Root $Root -SkipLauncherPublish
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ("4) Publish installer -> {0}" -f $outInstaller)
New-Item -ItemType Directory -Force -Path $outInstaller | Out-Null
dotnet publish $installerProj -c Release -o $outInstaller
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# FeedBaseUrl da ac_dev_publish.json: bake in ac_config overlay installer
$feedUrl = ""
if (Test-Path -LiteralPath $devPub) {
    try {
        $dp = Get-Content -LiteralPath $devPub -Raw | ConvertFrom-Json
        if ($dp.FeedBaseUrl) { $feedUrl = ([string]$dp.FeedBaseUrl).Trim() }
    }
    catch { }
}

# Overlay installer = update zip + ac_config.json utente (solo primo install)
$overlaySrc = Join-Path $outUpdate "ac-update.zip"
$overlayDst = Join-Path $outInstaller "ac-overlay.zip"
if (Test-Path -LiteralPath $overlaySrc) {
    $stageOverlay = Join-Path $env:TEMP ("ac_ovl_" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Force -Path $stageOverlay | Out-Null
    try {
        Expand-Archive -LiteralPath $overlaySrc -DestinationPath $stageOverlay -Force

        $cfgObj = [ordered]@{
            useSteam            = $true
            friendsProfile      = $true
            autoUpdateGame      = $false
            preferDirectSteam   = $false
            checkUpdatesOnStart = $true
            updateChannel       = "dev"
            updateFeedUrl       = $feedUrl
        }
        if (Test-Path -LiteralPath $userCfgTemplate) {
            try {
                $t = Get-Content -LiteralPath $userCfgTemplate -Raw | ConvertFrom-Json
                if ($null -ne $t.useSteam) { $cfgObj.useSteam = [bool]$t.useSteam }
                if ($null -ne $t.friendsProfile) { $cfgObj.friendsProfile = [bool]$t.friendsProfile }
                if ($null -ne $t.checkUpdatesOnStart) { $cfgObj.checkUpdatesOnStart = [bool]$t.checkUpdatesOnStart }
                if ($t.updateChannel) { $cfgObj.updateChannel = [string]$t.updateChannel }
                if ($t.updateFeedUrl -and -not $feedUrl) { $cfgObj.updateFeedUrl = [string]$t.updateFeedUrl }
            }
            catch { }
        }
        ($cfgObj | ConvertTo-Json -Depth 5) | Set-Content -LiteralPath (Join-Path $stageOverlay "ac_config.json") -Encoding UTF8

        $devInZip = Join-Path $stageOverlay "MMX-Net-Launcher-dev.exe"
        if (Test-Path -LiteralPath $devInZip) { Remove-Item -LiteralPath $devInZip -Force }

        if (Test-Path -LiteralPath $overlayDst) { Remove-Item -LiteralPath $overlayDst -Force }
        Compress-Archive -Path (Join-Path $stageOverlay "*") -DestinationPath $overlayDst -CompressionLevel Optimal
    }
    finally {
        Remove-Item -LiteralPath $stageOverlay -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if (Test-Path -LiteralPath $userCfgTemplate) {
    Copy-Item -LiteralPath $userCfgTemplate -Destination (Join-Path $outInstaller "ac_config.user.json") -Force
}

$feedHint = if ($feedUrl) { $feedUrl } else { "(dopo publish: URL pubblico di dist/update/ - vedi ac_dev_publish.json FeedBaseUrl)" }
$readmeLines = @(
    "MMX-Net - pacchetto installer",
    "==================================",
    "",
    "1. Tieni MMX-Net-Installer.exe e ac-overlay.zip nella STESSA cartella.",
    "2. Esegui MMX-Net-Installer.exe",
    "3. Crea / scegli una cartella a parte chiamata MMX-Net",
    "   (non usare Anomaly vanilla).",
    "4. Se la cartella e vuota: spunta Copia da install Anomaly coop esistente",
    "   oppure copia prima tu bin/db/gamedata/fsgame.ltx + file di release protocollo.",
    "5. Avvia MMX-Net-Launcher.exe dalla cartella MMX-Net.",
    "   Il launcher usa SEMPRE la cartella dell exe (qualsiasi disco), non un path fisso.",
    "",
    "Requisiti: Anomaly 1.5.3 coop, Steam, Call of Pripyat (41700) per inviti.",
    "Se HEALTH ha FATAL su DLL/script coop = install incompleta (solo overlay).",
    "Aggiornamenti: nel launcher icona download / AGGIORNA (serve updateFeedUrl).",
    "",
    "URL feed (updateFeedUrl):",
    "  $feedHint",
    "",
    "Logan (dev):",
    "  1) MMX-Net-Launcher-dev.exe -> Prepara/Pubblica (genera dist/update/)",
    "  2) Carica dist/update/ online (GitHub raw / itch / server)",
    "  3) Incolla l URL in ac_dev_publish.json -> FeedBaseUrl",
    "  4) Rebuild installer: amici nuovi ricevono updateFeedUrl gia settato",
    "  Vedi toolkit/pack/UPDATE_FEED.md"
)
Set-Content -LiteralPath (Join-Path $outInstaller "LEGGIMI.txt") -Value $readmeLines -Encoding UTF8

# Pacchetto download amici (zip con installer + overlay + leggimi)
if (Test-Path -LiteralPath $outDownload) {
    Get-ChildItem -LiteralPath $outDownload -Force | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Force -Path $outDownload | Out-Null
Copy-Item -LiteralPath (Join-Path $outInstaller "LEGGIMI.txt") -Destination (Join-Path $outDownload "LEGGIMI.txt") -Force
$downloadZip = Join-Path $outDownload "MmxNet-Download.zip"
$packFiles = @(
    (Join-Path $outInstaller "MMX-Net-Installer.exe"),
    (Join-Path $outInstaller "ac-overlay.zip"),
    (Join-Path $outInstaller "LEGGIMI.txt"),
    (Join-Path $outInstaller "ac_config.user.json")
) | Where-Object { Test-Path -LiteralPath $_ }
if ($packFiles.Count -gt 0) {
    Compress-Archive -LiteralPath $packFiles -DestinationPath $downloadZip -CompressionLevel Optimal
}

Write-Host ""
Write-Host "OK:"
Write-Host ("  {0}\MMX-Net-Launcher.exe       (utente)" -f $Root)
Write-Host ("  {0}\MMX-Net-Launcher-dev.exe   (devkit Logan)" -f $Root)
Write-Host ("  {0}\MMX-Net-Launcher-dev.exe" -f $outDevkit)
Write-Host ("  {0}\MMX-Net-Installer.exe" -f $outInstaller)
Write-Host ("  {0}\ac-overlay.zip" -f $outInstaller)
Write-Host ("  {0}\LEGGIMI.txt" -f $outInstaller)
Write-Host ("  {0}\  (feed da caricare online)" -f $outUpdate)
Write-Host ("  {0}" -f $downloadZip)
if ($feedUrl) {
    Write-Host ("  updateFeedUrl baked: {0}" -f $feedUrl)
}
else {
    Write-Host '  FeedBaseUrl vuoto: amici devono impostare updateFeedUrl dopo che carichi dist\update\'
}
