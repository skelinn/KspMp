# Runs the dedicated server. Env: KSPMP_PORT (7777), KSPMP_UNIVERSE (.\universe)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$port = if ($env:KSPMP_PORT) { $env:KSPMP_PORT } else { 7777 }
$universe = if ($env:KSPMP_UNIVERSE) { $env:KSPMP_UNIVERSE } else { Join-Path $repo 'universe' }
dotnet run --project (Join-Path $repo 'src\KspMp.Server.Host') -- --port $port --universe $universe @args
