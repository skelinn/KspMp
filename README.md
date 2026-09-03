# KspMp — multiplayer for Kerbal Space Program 1.12.5

Play KSP with your friends on one shared timeline: everyone is their own Kerbal, you can sit in the same
rocket, share the controls, build together in the VAB/SPH, and dock.

Status: **M0** — solution skeleton, build/deploy pipeline, and an in-game network spike (LiteNetLib +
Deflate running inside KSP's Mono runtime). See `docs/PLAN.md` for the architecture and milestones.

## Requirements

- Kerbal Space Program 1.12.5 (Steam). Windows is the primary platform; macOS works for development and testing.
- .NET 10 SDK: `scripts/install-dotnet.ps1` (Windows) or `scripts/install-dotnet.sh` (macOS),
  or `winget install Microsoft.DotNet.SDK.10`.
- git (LiteNetLib is a submodule).

## Build and deploy

Windows (PowerShell):

    git clone --recurse-submodules <repo-url> kspmp
    cd kspmp
    $env:KSP_ROOT = 'C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program'   # only if not the Steam default
    dotnet build -c Debug -p:KspMpDeploy=true

macOS:

    git clone --recurse-submodules <repo-url> kspmp && cd kspmp
    export KSP_ROOT="$HOME/Library/Application Support/Steam/steamapps/common/Kerbal Space Program"   # only if not the Steam default
    dotnet build -c Debug -p:KspMpDeploy=true

Instead of `KSP_ROOT` you can create `KspRoot.user.props` (gitignored) next to the solution:

    <Project><PropertyGroup><KspRoot>D:\Games\Kerbal Space Program</KspRoot></PropertyGroup></Project>

`-p:KspMpDeploy=true` copies `GameData/KspMp` into the KSP install and adds `GameData/000_Harmony/0Harmony.dll`
if no Harmony is installed. Without the flag the build only refreshes `GameData/KspMp/Plugins` in the repo.

## Local multiplayer testing (two clients + server on one machine)

    scripts/make-test-installs.ps1   # copies the install to %USERPROFILE%\ksp-test\ksp-a and ksp-b (once, ~6 GB each)
    scripts/deploy.ps1               # deploys the mod into both copies
    scripts/run-server.ps1           # dedicated server on UDP 7777
    scripts/run-clients.ps1          # launches both copies windowed, bypassing Steam and the launcher

macOS: the same scripts with a `.sh` extension (copies go to `~/ksp-test`).

In game, Alt+F10 toggles the KspMp debug window. Logs go to `<install>/KSP.log`; grep for `[KspMp]`.

## Repository layout

    src/KspMp.Shared           protocol, messages, codecs (net472 + netstandard2.0)
    src/KspMp.Net.LiteNetLib   LiteNetLib 1.3.5 compiled from the submodule for net472/netstandard2.0
    src/KspMp.Server           server library (never runs KSP)
    src/KspMp.Server.Host      dedicated server console app (.NET 10)
    src/KspMp.Client           the KSP plugin (KspMp.dll, net472)
    tests/                     xunit tests that run without KSP
    GameData/KspMp             the mod folder as installed (Plugins/ is build output)
    scripts/                   PowerShell and bash helpers
    docs/PLAN.md               architecture and milestone plan

API research: `scripts/decompile.ps1` / `.sh` decompiles Assembly-CSharp into `decompiled/` (gitignored, never commit it).

## Tests

    dotnet test

## License

Not chosen yet. Third-party notices are in `THIRD_PARTY_NOTICES.md`.
