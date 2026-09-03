# Creates two independent copies of the KSP install for local multiplayer testing (~6 GB each).
$ErrorActionPreference = 'Stop'
$kspRoot = if ($env:KSP_ROOT) { $env:KSP_ROOT } else { 'C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program' }
$dest = if ($env:KSP_TEST_DIR) { $env:KSP_TEST_DIR } else { Join-Path $env:USERPROFILE 'ksp-test' }
if (-not (Test-Path (Join-Path $kspRoot 'GameData'))) { throw "KSP not found at $kspRoot (set KSP_ROOT)" }
foreach ($name in 'ksp-a', 'ksp-b') {
    $target = Join-Path $dest $name
    Write-Host "Copying $kspRoot -> $target ..."
    robocopy $kspRoot $target /MIR /XD saves Logs (Join-Path $kspRoot 'GameData\KspMp') /XF KSP.log /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE" }
}
Write-Host 'Done. Deploy the mod with scripts/deploy.ps1, then launch with scripts/run-clients.ps1'
