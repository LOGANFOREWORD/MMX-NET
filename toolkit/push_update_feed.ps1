#Requires -Version 5.1
$ErrorActionPreference = "Stop"
$git = "C:\Program Files\Git\cmd\git.exe"
if (-not (Test-Path $git)) { $git = "git" }
$toolkit = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $toolkit
$devPub = Join-Path $Root "ac_dev_publish.json"
$target = ""
if (Test-Path $devPub) {
  try { $j = Get-Content $devPub -Raw | ConvertFrom-Json; $target = [string]$j.PublishTarget } catch {}
}
if ([string]::IsNullOrWhiteSpace($target)) { $target = Join-Path $Root "dist\mmx-net-feed" }
$target = [IO.Path]::GetFullPath($target)
if (-not (Test-Path (Join-Path $target ".git"))) { throw "PublishTarget non e clone git: $target" }
Push-Location $target
try {
  $origin = & $git remote get-url origin
  # Feed amici = MMX-NET pubblica (legacy: anche MMX-NET-feed)
  if ($origin -notmatch 'MMX-NET(-feed)?(\.git)?$') { throw "Origin non e MMX-NET: $origin" }
  $src = Join-Path $Root "dist\update"
  foreach ($name in @("ac_update_manifest.json","ac_version.json","ac-update.zip","LEGGIMI_FEED.txt")) {
    $f = Join-Path $src $name
    if (Test-Path $f) { Copy-Item $f (Join-Path $target $name) -Force }
  }
  & $git add -- ac_update_manifest.json ac_version.json ac-update.zip LEGGIMI_FEED.txt README.md .gitignore 2>$null
  $status = & $git status --porcelain
  if (-not $status) { Write-Host "Nessuna modifica da pushare in $target"; return }
  $ver = "update"
  try { $ver = [string](Get-Content (Join-Path $target "ac_version.json") -Raw | ConvertFrom-Json).Version } catch {}
  & $git commit -m "feed $ver"
  if ($LASTEXITCODE -ne 0) { throw "git commit fallito" }
  & $git push origin HEAD:main
  if ($LASTEXITCODE -ne 0) { throw "git push fallito" }
  Write-Host "OK: feed $ver pushato su MMX-NET"
} finally { Pop-Location }
