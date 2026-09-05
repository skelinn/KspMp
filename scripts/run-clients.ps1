# Launches the two test installs directly (bypasses Steam and the launcher). Env: KSP_TEST_DIR (%USERPROFILE%\ksp-test)
# Extra arguments are passed to both clients, e.g.:
#   scripts/run-clients.ps1 -kspmp-connect 127.0.0.1:7777 -kspmp-enter
# Each client gets -kspmp-name <copy name> unless you pass -kspmp-name yourself.
# Note: both clients get the same extra arguments, so -kspmp-avatar cannot be used here
# (avatars must be unique per player); launch the copies by hand when you need one each.
param([Parameter(ValueFromRemainingArguments = $true)][string[]] $ExtraArgs = @())
$ErrorActionPreference = 'Stop'
$dest = if ($env:KSP_TEST_DIR) { $env:KSP_TEST_DIR } else { Join-Path $env:USERPROFILE 'ksp-test' }

# Start-Process joins -ArgumentList with spaces and does not re-quote, so any value holding a
# space ("Ships/VAB/Kerbal X.craft", "Name:Trait", a chat line) would reach KSP as several
# arguments. Quote those here; the bash twin gets this for free from "$@".
function Format-Arg([string] $value) {
    if ($value -match '\s') { '"' + $value.Replace('"', '\"') + '"' } else { $value }
}

foreach ($name in 'ksp-a', 'ksp-b') {
    $dir = Join-Path $dest $name
    $exe = Join-Path $dir 'KSP_x64.exe'
    if (-not (Test-Path $exe)) { throw "Missing $exe (run scripts/make-test-installs.ps1)" }
    $argList = @('-screen-width', '1280', '-screen-height', '720', '-screen-fullscreen', '0', '-popupwindow',
                 '-logFile', (Join-Path $dir 'player.log'), '-kspmp-name', $name) + $ExtraArgs
    $quoted = @($argList | ForEach-Object { Format-Arg $_ })
    Write-Host "Launching $exe $($quoted -join ' ')"
    Start-Process -FilePath $exe -WorkingDirectory $dir -ArgumentList $quoted
}
