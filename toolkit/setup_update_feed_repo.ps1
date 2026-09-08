#Requires -Version 5.1
<#
.SYNOPSIS
  Crea/collega la repo PUBBLICA solo-feed LOGANFOREWORD/MMX-NET-feed.
  Contiene SOLO: ac_version.json, ac_update_manifest.json, ac-update.zip (+ README).
  Il codice prodotto resta su MMX-NET PRIVATA. Solo owner ha write su entrambe.
#>
$ErrorActionPreference = "Stop"
$git = "C:\Program Files\Git\cmd\git.exe"
if (-not (Test-Path $git)) { $git = "git" }
$env:Path = "C:\Program Files\Git\cmd;C:\Program Files\GitHub CLI;" + $env:Path

$toolkit = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $toolkit
$repoName = "MMX-NET-feed"
$cloneDir = Join-Path $Root "dist\update-feed"
$feedUrl = $null

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

$prevEap = $ErrorActionPreference
$ErrorActionPreference = "Continue"
gh repo view "$user/$repoName" 2>$null | Out-Null
$exists = ($LASTEXITCODE -eq 0)
$ErrorActionPreference = $prevEap

if (-not $exists) {
    Write-Host "Creo repo pubblica $user/$repoName (solo feed update)..."
    gh repo create "$user/$repoName" --public --description "MMX-Net update feed only (manifest + zip). No source."
    if ($LASTEXITCODE -ne 0) { throw "gh repo create fallito" }
} else {
    $vis = gh repo view "$user/$repoName" --json isPrivate,visibility --jq "{isPrivate,visibility}"
    Write-Host "Repo esistente: https://github.com/$user/$repoName  $vis"
    # Assicura pubblica (amici senza PAT)
    gh repo edit "$user/$repoName" --visibility public 2>$null
}

$feedUrl = "https://raw.githubusercontent.com/$user/$repoName/main/"

# Clone fresco: se PublishTarget puntava a MMX-NET (prodotto), NON riusarlo — pusherebbe il source.
$needClone = $true
if (Test-Path (Join-Path $cloneDir ".git")) {
    Push-Location $cloneDir
    $origin = (& $git remote get-url origin 2>$null)
    Pop-Location
    if ($origin -match [regex]::Escape("$user/$repoName")) {
        $needClone = $false
        Push-Location $cloneDir
        & $git pull --ff-only 2>$null
        Pop-Location
    } else {
        Write-Host "PublishTarget non e' $repoName (era: $origin) — backup e re-clone."
    }
}
if ($needClone) {
    New-Item -ItemType Directory -Force -Path (Split-Path $cloneDir) | Out-Null
    if (Test-Path $cloneDir) {
        $bak = $cloneDir + ".old-" + (Get-Date -Format "yyyyMMddHHmmss")
        Rename-Item -LiteralPath $cloneDir -NewName (Split-Path $bak -Leaf) -Force
    }
    & $git clone "https://github.com/$user/$repoName.git" $cloneDir
    if ($LASTEXITCODE -ne 0) { throw "git clone fallito" }
}

# README solo-feed (se assente)
$readme = Join-Path $cloneDir "README.md"
if (-not (Test-Path $readme)) {
    $readmeLines = @(
        "# MMX-NET-feed",
        "",
        "Repo pubblica solo per gli aggiornamenti MMX-Net.",
        "",
        "Contiene esclusivamente:",
        "- ac_version.json",
        "- ac_update_manifest.json",
        "- ac-update.zip",
        "",
        "Il codice / prodotto resta su MMX-NET (privata). Solo l owner scrive su entrambe le repo.",
        "",
        "updateFeedUrl amici:",
        "https://raw.githubusercontent.com/$user/$repoName/main/",
        "",
        "Nessun PAT richiesto."
    )
    ($readmeLines -join "`r`n") | Set-Content -LiteralPath $readme -Encoding UTF8
}

# .gitignore: non committare mai source
$gi = Join-Path $cloneDir ".gitignore"
$giLines = @(
    "*",
    "!ac_version.json",
    "!ac_update_manifest.json",
    "!ac-update.zip",
    "!LEGGIMI_FEED.txt",
    "!README.md",
    "!.gitignore"
)
($giLines -join "`r`n") | Set-Content -LiteralPath $gi -Encoding UTF8

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
    (Join-Path $Root "toolkit\pack\ac_config.user.json"),
    (Join-Path $Root "pack\ac_config.user.json"),
    (Join-Path $Root "toolkit\pack\ac_config.example_feed.json")
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
Write-Host "OK — feed PUBBLICO $user/$repoName (solo zip/manifest)"
Write-Host "  FeedBaseUrl: $feedUrl"
Write-Host "  PublishTarget: $cloneDir"
Write-Host "  Codice: resta su MMX-NET privata"
Write-Host "  Write: solo owner su entrambe le repo"
Write-Host "  Dopo CARICA: toolkit\push_update_feed.ps1"
