# Packages the mod for another player: GameData/KspMp (without PluginData - that holds THIS machine's player
# identity and hosted world, and copying it to a friend makes the server see one player twice) plus
# 000_Harmony, the licence, the notices and the READ ME FIRST, as artifacts/KspMp-<short commit>.zip.
# Usage: scripts/package.ps1 [-Source <KSP install with the built mod deployed>]
param([string] $Source = (Join-Path $env:USERPROFILE 'ksp-test\ksp-a'))
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$stage = Join-Path $repo 'artifacts\KspMp-for-friend'
$commit = (git -C $repo rev-parse --short HEAD).Trim()
$zip = Join-Path $repo ("artifacts\KspMp-" + $commit + ".zip")

$plugin = Join-Path $stage 'GameData\KspMp'
if (Test-Path $plugin) { Remove-Item $plugin -Recurse -Force }
Copy-Item (Join-Path $Source 'GameData\KspMp') $plugin -Recurse
$pluginData = Join-Path $plugin 'PluginData'
if (Test-Path $pluginData) { Remove-Item $pluginData -Recurse -Force }
Get-ChildItem $plugin -Recurse -Filter *.pdb | Remove-Item -Force

if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
"Packaged $zip"
