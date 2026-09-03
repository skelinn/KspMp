# Launches the two test installs directly (bypasses Steam and the launcher). Env: KSP_TEST_DIR (%USERPROFILE%\ksp-test)
# Extra arguments are passed to both clients, e.g.:
#   scripts/run-clients.ps1 -kspmp-connect 127.0.0.1:7777 -kspmp-enter
# Each client gets -kspmp-name <copy name> unless you pass -kspmp-name yourself.
param([Parameter(ValueFromRemainingArguments = $true)][string[]] $ExtraArgs = @())
$ErrorActionPreference = 'Stop'
$dest = if ($env:KSP_TEST_DIR) { $env:KSP_TEST_DIR } else { Join-Path $env:USERPROFILE 'ksp-test' }
foreach ($name in 'ksp-a', 'ksp-b') {
    $dir = Join-Path $dest $name
    $exe = Join-Path $dir 'KSP_x64.exe'
    if (-not (Test-Path $exe)) { throw "Missing $exe (run scripts/make-test-installs.ps1)" }
    $argList = @('-screen-width', '1280', '-screen-height', '720', '-screen-fullscreen', '0', '-popupwindow', '-logFile', "`"$dir\player.log`"", '-kspmp-name', $name) + $ExtraArgs
    Write-Host "Launching $exe $($argList -join ' ')"
    Start-Process -FilePath $exe -WorkingDirectory $dir -ArgumentList $argList
}
