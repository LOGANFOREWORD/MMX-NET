#Requires -Version 5.1
param(
  [Parameter(Mandatory=$true)][string]$GitHubUser,
  [string]$RepoName = "MMX-NET"
)
$ErrorActionPreference = "Stop"
$Root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if (Test-Path (Join-Path $PSScriptRoot "..\..\ac_dev_publish.json")) {
  # script lives in toolkit\
}
$toolkit = $PSScriptRoot
$Root = Split-Path $toolkit -Parent
$user = $GitHubUser.Trim().TrimEnd('/')
if ($user -match 'github\.com/([^/]+)') { $user = $Matches[1] }
$feed = "https://raw.githubusercontent.com/$user/$RepoName/main/"
$cloneDir = Join-Path $Root "dist\update-feed"
$cfg = [ordered]@{
  FeedBaseUrl = $feed
  PublishTarget = $cloneDir
  OutDir = "dist\update"
  AbsolutePackageUrl = $true
  AutoBumpPatch = $true
}
($cfg | ConvertTo-Json) | Set-Content (Join-Path $Root "ac_dev_publish.json") -Encoding UTF8
foreach ($p in @((Join-Path $Root "ac_config.json"), (Join-Path $Root "toolkit\pack\ac_config.user.json"))) {
  if (-not (Test-Path $p)) { continue }
  $j = Get-Content $p -Raw | ConvertFrom-Json
  $j | Add-Member -NotePropertyName updateFeedUrl -NotePropertyValue $feed -Force
  if (-not ($j.PSObject.Properties.Name -contains "updateFeedToken")) {
    $j | Add-Member -NotePropertyName updateFeedToken -NotePropertyValue "" -Force
  }
  $j.checkUpdatesOnStart = $true
  ($j | ConvertTo-Json) | Set-Content $p -Encoding UTF8
}
Write-Host "FeedBaseUrl = $feed"
Write-Host "Incolla nel DEVKIT: $feed"
