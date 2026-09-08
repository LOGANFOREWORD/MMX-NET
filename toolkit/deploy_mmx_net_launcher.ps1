#Requires -Version 5.1
<#
.SYNOPSIS
  Publish entrambi i launcher (User + Dev) nella root MMX-Net.
#>
$ErrorActionPreference = "Stop"
$toolkit = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $toolkit "MmxNetLauncher\MmxNetLauncher.csproj"
$out = Split-Path -Parent $toolkit
$outUser = Join-Path $out "dist\user"
$outDevkit = Join-Path $out "dist\devkit"

Write-Host ("Root: {0}" -f $out)
Write-Host "Kill processi launcher..."
Get-Process -Name "MMX-Net-Launcher","MMX-Net-Launcher-dev" -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue

New-Item -ItemType Directory -Force -Path $outUser | Out-Null
New-Item -ItemType Directory -Force -Path $outDevkit | Out-Null

Write-Host "Publish UTENTE -> $outUser"
dotnet publish $proj -c Release -o $outUser -p:BuildFlavor=User
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Publish DEVKIT -> $outDevkit"
dotnet publish $proj -c Release -o $outDevkit -p:BuildFlavor=Dev
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Copy-Item -LiteralPath (Join-Path $outUser "MMX-Net-Launcher.exe") -Destination $out -Force
Copy-Item -LiteralPath (Join-Path $outDevkit "MMX-Net-Launcher-dev.exe") -Destination $out -Force

Write-Host "OK:"
Write-Host "  $out\MMX-Net-Launcher.exe"
Write-Host "  $out\MMX-Net-Launcher-dev.exe"
Write-Host "  $outUser\MMX-Net-Launcher.exe"
Write-Host "  $outDevkit\MMX-Net-Launcher-dev.exe"
