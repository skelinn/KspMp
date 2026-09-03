# Installs the .NET 10 SDK for the current user (no admin rights needed).
$ErrorActionPreference = 'Stop'
$installDir = if ($env:DOTNET_INSTALL_DIR) { $env:DOTNET_INSTALL_DIR } else { Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet' }
$script = Join-Path $env:TEMP 'dotnet-install.ps1'
Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $script
& $script -Channel 10.0 -InstallDir $installDir
Write-Host ''
Write-Host "Add $installDir to your PATH (User environment variables), or install machine-wide with:"
Write-Host '  winget install Microsoft.DotNet.SDK.10'
