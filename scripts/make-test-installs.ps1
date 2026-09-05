# Creates two independent copies of the KSP install for local multiplayer testing.
# Usage: scripts/make-test-installs.ps1 [-Stock]
#   default: mirrors the whole install, mods included (~= the size of your install).
#   -Stock : copies only the stock GameData (Squad, SquadExpansion) and leaves every mod
#            behind. Much smaller, loads in seconds, and keeps a Kraken from being someone
#            else's bug. 000_Harmony and KspMp are added afterwards by scripts/deploy.ps1.
param([switch] $Stock)
$ErrorActionPreference = 'Stop'
$kspRoot = if ($env:KSP_ROOT) { $env:KSP_ROOT } else { 'C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program' }
$dest = if ($env:KSP_TEST_DIR) { $env:KSP_TEST_DIR } else { Join-Path $env:USERPROFILE 'ksp-test' }
if (-not (Test-Path (Join-Path $kspRoot 'GameData'))) { throw "KSP not found at $kspRoot (set KSP_ROOT)" }

# GameData entries a stock copy keeps; everything else is a mod. 000_Harmony and KspMp are
# not copied from the source install but must survive re-runs, since deploy.ps1 writes them.
$stockKeep = @('Squad', 'SquadExpansion', '000_Harmony', 'KspMp')

# robocopy retries a locked or unreadable file 1,000,000 times at 30s apiece by default,
# which looks exactly like a hang. Fail fast instead, skip junctions, and copy in parallel.
$robocopyOpts = @('/R:2', '/W:5', '/XJ', '/MT:16', '/NFL', '/NDL', '/NJH', '/NJS', '/NP')
# CKAN holds registry.locked open whenever it is running, so a copy that includes it fails.
# A duplicate install is never managed by CKAN, so skip the whole folder.
$excludeDirs = @('saves', 'Logs', 'CKAN')
$excludeFiles = @('KSP.log', 'registry.locked')

foreach ($name in 'ksp-a', 'ksp-b') {
    $target = Join-Path $dest $name
    Write-Host "Copying $kspRoot -> $target ..."
    if ($Stock) {
        # Everything outside GameData, then the stock GameData folders only.
        robocopy $kspRoot $target /MIR /XD @excludeDirs (Join-Path $kspRoot 'GameData') /XF @excludeFiles @robocopyOpts | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE" }
        foreach ($folder in 'Squad', 'SquadExpansion') {
            $src = Join-Path $kspRoot "GameData\$folder"
            if (-not (Test-Path $src)) { continue }
            robocopy $src (Join-Path $target "GameData\$folder") /MIR @robocopyOpts | Out-Null
            if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE" }
        }
        # Drop anything a previous non-stock run left in GameData.
        $targetGameData = Join-Path $target 'GameData'
        Get-ChildItem $targetGameData -Force -ErrorAction SilentlyContinue |
            Where-Object { $stockKeep -notcontains $_.Name } |
            ForEach-Object { Write-Host "  removing mod: $($_.Name)"; Remove-Item $_.FullName -Recurse -Force }
    }
    else {
        robocopy $kspRoot $target /MIR /XD @excludeDirs (Join-Path $kspRoot 'GameData\KspMp') /XF @excludeFiles @robocopyOpts | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE" }
    }
}
Write-Host 'Done. Deploy the mod with scripts/deploy.ps1, then launch with scripts/run-clients.ps1'
# robocopy exits 1-7 on success (1 = files copied), and PowerShell would otherwise pass that
# on as the script's exit code, so a healthy run looks like a failure to any caller.
exit 0
