# KspMp — multiplayer for Kerbal Space Program 1.12.5

Play KSP with your friends on one shared timeline: everyone is their own Kerbal, you can sit in the same
rocket, share the controls, build together in the VAB/SPH, and dock.

Status: **M0-M2 done** (connect, lobby, chat, shared clock, vessel replication with physics authority, all verified
with two clients), **M3 in progress** (shared warp). See `docs/PLAN.md` for the architecture and milestones.

## Requirements

- Kerbal Space Program 1.12.5 (Steam). Windows is the primary platform; macOS works for development and testing.
- .NET 10 SDK: `scripts/install-dotnet.ps1` (Windows) or `scripts/install-dotnet.sh` (macOS),
  or `winget install Microsoft.DotNet.SDK.10`.
- git (LiteNetLib is a submodule).

## Build and deploy

Windows (PowerShell):

    git clone --recurse-submodules https://github.com/skelinn/KspMp.git kspmp
    cd kspmp
    $env:KSP_ROOT = 'C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program'   # only if not the Steam default
    dotnet build -c Debug -p:KspMpDeploy=true

macOS:

    git clone --recurse-submodules https://github.com/skelinn/KspMp.git kspmp && cd kspmp
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

In game: the main menu shows the KspMp window (connect, lobby, chat, Enter game); Alt+M toggles the in-game
players/chat window; Alt+F10 toggles the debug window. Logs go to `<install>/KSP.log`; grep for `[KspMp]`.

Launch options (handy for testing and for jumping straight into your usual server):

    -kspmp-connect host[:port]   connect as soon as the main menu is ready
    -kspmp-name Name             player name for this run (not saved)
    -kspmp-avatar "Name:Trait"   claim this Kerbal on first join (Pilot, Engineer or Scientist)
    -kspmp-enter                 enter the game once the world has synced and you have a Kerbal
    -kspmp-say "text"            send one chat message after joining
    -kspmp-debug                 show the debug window
    -kspmp-launch "Ships/VAB/Kerbal X.craft"   launch a craft from the space center with your Kerbal in the first seat
    -kspmp-site LaunchPad|Runway  launch site (default from the craft folder)
    -kspmp-crew "Name,Name"      seat these Kerbals too (a friend's Kerbal can be seated before they join)
    -kspmp-fly N                 N seconds after launch: SAS on, full throttle, stage once
    -kspmp-stage N               N seconds after entering flight: press space once
    -kspmp-input D:S             D seconds after entering flight: hold pitch/throttle input for S seconds
    -kspmp-warp I:D:S            D seconds after entering flight request warp index I, cancel S seconds later

`scripts/run-clients.ps1 -kspmp-connect 127.0.0.1:7777 -kspmp-enter` launches both test copies straight into the game.

Server files live in the universe folder: `server.cfg` (name, port, max players, MOTD, `sharedStickDefault`,
`hostControlsWarp`), `time.cfg` (shared UT, saved every minute and on shutdown), `players.cfg` (known players and
their Kerbal avatars), `vessels/<id>.cfg` and `roster/<name>.cfg` (the shared world, readable KSP ConfigNode text).

## How playing together works

- Everyone shares one timeline. Warp is negotiated: the slowest request wins, anyone can drop back to 1x, and a
  player who cannot warp (in the atmosphere, moving on the ground) limits everyone.
- You are your Kerbal. Sit in a rocket with a friend and the player in the command seat is the pilot (and runs the
  physics); everyone else aboard can stage, use action groups, SAS and part buttons. With `sharedStickDefault = True`
  co-pilots can also steer when the pilot's hands are off the stick.
- A vessel with nobody's Kerbal aboard is simulated by whoever is nearest; uncrewed probes can be flown by anyone.
- Pause only pauses your menu; quickload and revert are disabled.

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
