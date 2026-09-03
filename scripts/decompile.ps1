# Decompiles Assembly-CSharp.dll into decompiled\ (gitignored) for API research.
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$kspRoot = if ($env:KSP_ROOT) { $env:KSP_ROOT } else { 'C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program' }
$managed = if ($env:KSP_MANAGED_DIR) { $env:KSP_MANAGED_DIR } else { Join-Path $kspRoot 'KSP_x64_Data\Managed' }
if (-not (Test-Path (Join-Path $managed 'Assembly-CSharp.dll'))) { throw "Assembly-CSharp.dll not found in $managed" }
if (-not (Get-Command ilspycmd -ErrorAction SilentlyContinue)) { dotnet tool install --global ilspycmd }
$out = Join-Path $repo 'decompiled\Assembly-CSharp'
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Path $out | Out-Null
ilspycmd -p -o $out --nested-directories -r $managed (Join-Path $managed 'Assembly-CSharp.dll')
Write-Host "Decompiled to $out"
