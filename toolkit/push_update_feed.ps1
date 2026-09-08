#Requires -Version 5.1
<#
.SYNOPSIS
  Dopo CARICA AGGIORNAMENTO: commit + push della cartella PublishTarget (clone del feed GitHub).
#>
$ErrorActionPreference = "Stop"
$git = "C:\Program Files\Git\cmd\git.exe"
if (-not (Test-Path $git)) { $git = "git" }

$toolkit = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $toolkit
$devPub = Join-Path $Root "ac_dev_publish.json"

$target = ""
if (Test-Path $devPub) {
    try {
        $j = Get-Content -LiteralPath $devPub -Raw | ConvertFrom-Json
        $target = [string]$j.PublishTarget
    } catch { }
}
if ([string]::IsNullOrWhiteSpace($target)) {
    $target = Join-Path $Root "dist\update-feed"
}
$target = [System.IO.Path]::GetFullPath($target)

if (-not (Test-Path (Join-Path $target ".git"))) {
    throw "PublishTarget non e' un clone git: $target`nClona la repo anomaly-coop-updates li' oppure imposta PublishTarget."
}

# Copia fresca da dist\update se presente (CARICA puo' aver gia' syncato)
$src = Join-Path $Root "dist\update"
if (Test-Path $src) {
    foreach ($name in @("ac_update_manifest.json", "ac_version.json", "ac-update.zip", "LEGGIMI_FEED.txt")) {
        $f = Join-Path $src $name
        if (Test-Path $f) {
            Copy-Item -LiteralPath $f -Destination (Join-Path $target $name) -Force
        }
    }
}

Push-Location $target
try {
    & $git add -A
    $status = & $git status --porcelain
    if (-not $status) {
        Write-Host "Nessuna modifica da pushare in $target"
        return
    }
    $ver = "update"
    $verFile = Join-Path $target "ac_version.json"
    if (Test-Path $verFile) {
        try {
            $vj = Get-Content -LiteralPath $verFile -Raw | ConvertFrom-Json
            if ($vj.Version) { $ver = [string]$vj.Version }
        } catch { }
    }
    & $git commit -m "feed $ver"
    if ($LASTEXITCODE -ne 0) { throw "git commit fallito" }
    & $git push
    if ($LASTEXITCODE -ne 0) { throw "git push fallito — controlla auth gh/git" }
    Write-Host "OK: feed $ver pushato su origin"
}
finally {
    Pop-Location
}
