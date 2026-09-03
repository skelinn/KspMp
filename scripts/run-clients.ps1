# Launches the two test installs directly (bypasses Steam and the launcher). Env: KSP_TEST_DIR (%USERPROFILE%\ksp-test)
param([string[]] $Names = @('ksp-a', 'ksp-b'))
$ErrorActionPreference = 'Stop'
$dest = if ($env:KSP_TEST_DIR) { $env:KSP_TEST_DIR } else { Join-Path $env:USERPROFILE 'ksp-test' }
foreach ($name in $Names) {
    $dir = Join-Path $dest $name
    $exe = Join-Path $dir 'KSP_x64.exe'
    if (-not (Test-Path $exe)) { throw "Missing $exe (run scripts/make-test-installs.ps1)" }
    Write-Host "Launching $exe"
    Start-Process -FilePath $exe -WorkingDirectory $dir -ArgumentList @('-screen-width', '1280', '-screen-height', '720', '-screen-fullscreen', '0', '-popupwindow', '-logFile', "`"$dir\player.log`"")
}
