# Copies GameData/KspMp (and 000_Harmony if missing) into KSP installs.
# Usage: scripts/deploy.ps1 [install-dir ...]
#   default: the two test installs if they exist, otherwise $env:KSP_ROOT / the Steam install.
param([string[]] $Installs)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$dest = if ($env:KSP_TEST_DIR) { $env:KSP_TEST_DIR } else { Join-Path $env:USERPROFILE 'ksp-test' }
$harmony = Get-ChildItem "$env:USERPROFILE\.nuget\packages\lib.harmony\2.2.*\lib\net472\0Harmony.dll" -ErrorAction SilentlyContinue | Select-Object -Last 1
if (-not $Installs) {
    if (Test-Path (Join-Path $dest 'ksp-a')) { $Installs = @((Join-Path $dest 'ksp-a'), (Join-Path $dest 'ksp-b')) }
    elseif ($env:KSP_ROOT) { $Installs = @($env:KSP_ROOT) }
    else { $Installs = @('C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program') }
}
foreach ($install in $Installs) {
    if (-not (Test-Path (Join-Path $install 'GameData'))) { Write-Warning "Skipping $install: no GameData folder"; continue }
    Write-Host "Deploying to $install\GameData\KspMp"
    robocopy (Join-Path $repo 'GameData\KspMp') (Join-Path $install 'GameData\KspMp') /MIR /XD PluginData /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE" }
    $harmonyDir = Join-Path $install 'GameData\000_Harmony'
    if (-not (Test-Path $harmonyDir) -and $harmony) {
        New-Item -ItemType Directory -Path $harmonyDir | Out-Null
        Copy-Item $harmony.FullName $harmonyDir
        Write-Host '  added 000_Harmony\0Harmony.dll'
    }
}
