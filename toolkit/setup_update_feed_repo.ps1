#Requires -Version 5.1
<#
.SYNOPSIS
  Collega PublishTarget + FeedBaseUrl alla repo GitHub PRIVATA esistente MMX-NET.
#>
$ErrorActionPreference = "Stop"
$git = "C:\Program Files\Git\cmd\git.exe"
if (-not (Test-Path $git)) { $git = "git" }
$env:Path = "C:\Program Files\Git\cmd;C:\Program Files\GitHub CLI;" + $env:Path

$toolkit = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $toolkit
$repoName = "MMX-NET"
$cloneDir = Join-Path $Root "dist\update-feed"

Write-Host "Check gh auth..."
gh auth status 2>&1 | Out-Host
if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "Esegui: gh auth login --hostname github.com --git-protocol https --web"
    Write-Host "Poi rilancia questo script."
    exit 1
}

$user = (gh api user --jq .login).Trim()
if ([string]::IsNullOrWhiteSpace($user)) { throw "Impossibile leggere utente GitHub" }
Write-Host "Utente GitHub: $user"

$exists = $false
$prevEap = $ErrorActionPreference
$ErrorActionPreference = "Continue"
gh repo view "$user/$repoName" 2>$null | Out-Null
if ($LASTEXITCODE -eq 0) { $exists = $true }
$ErrorActionPreference = $prevEap

if (-not $exists) {
    throw "Repo $user/$repoName non trovata. Creala su GitHub (privata) o correggi il nome — non inventare una repo nuova da questo script."
}

Write-Host "Repo: https://github.com/$user/$repoName"

New-Item -ItemType Directory -Force -Path (Split-Path $cloneDir) | Out-Null
if (-not (Test-Path (Join-Path $cloneDir ".git"))) {
    if (Test-Path $cloneDir) { Remove-Item -LiteralPath $cloneDir -Recurse -Force }
    & $git clone "https://github.com/$user/$repoName.git" $cloneDir
    if ($LASTEXITCODE -ne 0) { throw "git clone fallito" }
} else {
    Push-Location $cloneDir
    & $git pull --ff-only
    Pop-Location
}

$readme = Join-Path $cloneDir "README.md"
@"
# MMX-NET

Feed privato **MMX-Net** (``ac_update_manifest.json``, ``ac_version.json``, ``ac-update.zip``).

Non mettere PAT o secret in questo repository.
"@ | Set-Content -LiteralPath $readme -Encoding UTF8

$gitignore = Join-Path $cloneDir ".gitignore"
if (-not (Test-Path $gitignore)) {
    @"
.env
*.token
*secret*
"@ | Set-Content -LiteralPath $gitignore -Encoding UTF8
}

$src = Join-Path $Root "dist\update"
if (Test-Path $src) {
    foreach ($name in @("ac_update_manifest.json", "ac_version.json", "ac-update.zip", "LEGGIMI_FEED.txt")) {
        $f = Join-Path $src $name
        if (Test-Path $f) {
            Copy-Item -LiteralPath $f -Destination (Join-Path $cloneDir $name) -Force
        }
    }
}

Push-Location $cloneDir
try {
    & $git add -A
    $st = & $git status --porcelain
    if ($st) {
        & $git commit -m "sync feed MMX-Net"
        & $git branch -M main
        & $git push -u origin main
        if ($LASTEXITCODE -ne 0) { throw "push fallito" }
    } else {
        Write-Host "Clone gia allineato, niente da commitare."
    }
}
finally { Pop-Location }

$feedUrl = "https://raw.githubusercontent.com/$user/$repoName/main/"
$devPubPath = Join-Path $Root "ac_dev_publish.json"
$cfg = [ordered]@{
    FeedBaseUrl        = $feedUrl
    PublishTarget      = $cloneDir
    OutDir             = "dist\update"
    AbsolutePackageUrl = $true
    AutoBumpPatch      = $true
}
($cfg | ConvertTo-Json -Depth 5) | Set-Content -LiteralPath $devPubPath -Encoding UTF8

foreach ($cfgPath in @(
    (Join-Path $Root "ac_config.json"),
    (Join-Path $Root "toolkit\pack\ac_config.user.json")
)) {
    if (-not (Test-Path $cfgPath)) { continue }
    try {
        $j = Get-Content -LiteralPath $cfgPath -Raw | ConvertFrom-Json
        $j | Add-Member -NotePropertyName updateFeedUrl -NotePropertyValue $feedUrl -Force
        if (-not ($j.PSObject.Properties.Name -contains "updateFeedToken")) {
            $j | Add-Member -NotePropertyName updateFeedToken -NotePropertyValue "" -Force
        }
        $j.checkUpdatesOnStart = $true
        ($j | ConvertTo-Json -Depth 5) | Set-Content -LiteralPath $cfgPath -Encoding UTF8
    } catch {
        Write-Host "Skip bake $cfgPath : $($_.Exception.Message)"
    }
}

Write-Host ""
Write-Host "OK"
Write-Host "  Repo: https://github.com/$user/$repoName"
Write-Host "  FeedBaseUrl: $feedUrl"
Write-Host "  PublishTarget: $cloneDir"
Write-Host "  Prossimi passi: invita collaboratori; amici mettono updateFeedToken; dopo CARICA esegui push_update_feed.ps1"
