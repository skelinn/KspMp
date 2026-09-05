# KspMp — multiplayer for Kerbal Space Program 1.12.5

Play KSP with your friends on one shared timeline: everyone is their own Kerbal, you can sit in the same
rocket, share the controls, build together in the VAB/SPH, and dock.

Status: **M0-M7 flown** with two clients on one machine: connect, lobby and chat; a shared clock; vessel
replication with physics authority; negotiated warp; Kerbal avatars and a shared roster; shared control, where
two players ride the same rocket and the co-pilot can stage, use action groups and steer; docking, which merges
the two craft and leaves the other player aboard as co-pilot; and a shared VAB/SPH workbench, where both
builders converge on one craft hash. See `docs/PLAN.md` for the plan, and the gaps below before you rely on any
of it.

Everything so far has been verified over localhost, at `rtt 0 ms`. None of the timing work - the shared clock,
replica interpolation, shared-stick input - has ever seen real latency.

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

    scripts/make-test-installs.ps1 -Stock   # copies the install to %USERPROFILE%\ksp-test\ksp-a and ksp-b (once)
    scripts/deploy.ps1               # deploys the mod into both copies
    scripts/run-server.ps1           # dedicated server on UDP 7777
    scripts/run-clients.ps1          # launches both copies windowed, bypassing Steam and the launcher

macOS: the same scripts with a `.sh` extension (copies go to `~/ksp-test`).

`-Stock` (`--stock` on macOS) copies only the stock `GameData` (Squad, SquadExpansion) and leaves your
mods behind: far smaller, loads in seconds, and a Kraken is never another mod's fault. Drop the flag to
mirror the whole install when you need to test against your mod set.

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
    -kspmp-toggle Group:D        D seconds after entering flight toggle an action group (Light, Gear, RCS, ...)
    -kspmp-partevent Name:D      D seconds after entering flight fire a part-menu action by name

Test harness only. These teleport craft around and drive KSP directly, so they are for testing, not for play:

    -kspmp-orbit ALT:D           D seconds after entering flight, place us in a circular orbit ALT km up
    -kspmp-dock D                D seconds after entering flight, rendezvous with another player's ship and dock
    -kspmp-dockassist D          like -kspmp-dock but never moves our ship; helps finish a dock someone else started
    -kspmp-undock D              D seconds after a dock completes, split the pair again
    -kspmp-editor VAB|SPH:D      D seconds after reaching the space center, open that editor
    -kspmp-editorload "path":D   D seconds after the editor opens, load that craft onto the shared workbench
    -kspmp-editorwatch D         log the local craft hash every D seconds, so two clients can be compared

`scripts/run-clients.ps1 -kspmp-connect 127.0.0.1:7777 -kspmp-enter` launches both test copies straight into the game.

Server files live in the universe folder: `server.cfg` (name, port, max players, MOTD, `password`,
`sharedStickDefault`, `hostControlsWarp`, `upnp`), `time.cfg` (shared UT, saved every minute and on shutdown), `players.cfg` (known players and
their Kerbal avatars), `vessels/<id>.cfg` and `roster/<name>.cfg` (the shared world, readable KSP ConfigNode text).

## Playing over the internet

The server asks your router to forward its port over UPnP on startup, which is enough on its own for many home
connections. The startup log says if it could not: UPnP switched off, a second router above the first, or the
ISP's own NAT.

When that is not enough, run an introducer somewhere with a public address and let it broker the connection.
Neither of two machines behind home routers can reach the other to begin with, so both talk to the introducer,
which sees the real external address each router presents and tells each about the other. It brokers the
handshake and nothing else - the game traffic that follows is peer to peer and never passes through it, so one
small instance serves any number of games.

    KspMp.Server.Host --introducer-server --port 7000          # once, on a public address

    KspMp.Server.Host --introducer example.com:7000 --code kerbal    # whoever is hosting the game

Players then join with that code instead of an address:

    -kspmp-connect <any address> -kspmp-introducer example.com:7000 -kspmp-code kerbal

The address stays as a fallback: if nobody answers within twelve seconds the client dials it directly, which
still works on a LAN or a VPN. Failing all of that, forward the port by hand or put both machines on a VPN such
as Tailscale.

## Server passwords

Set `password` in `server.cfg` and players need it to join; leave it empty and anyone who can reach the port can
play. Worth setting on anything reachable from the internet, since UDP ports get scanned and there is nothing
else stopping a stranger flying your ships.

Players type it in the Multiplayer window, or pass `-kspmp-password "text"`. It is remembered per install, so
it only has to be typed once.

The password is hashed before it leaves the client, which keeps the password itself off the network - people
reuse them. It is not strong authentication: there is no challenge from the server, so anyone who can read a
join packet could replay it. Treat it as a lock on the door, not a guarantee about who is behind it.

## Known gaps

Worth knowing before you play, roughly in the order you would hit them.

- **Hole punching has only been proven on one machine.** The registration, code lookup, introduction and
  connect all work, and a client whose only direct address was unroutable still reached the server through an
  introducer. But both ends were on the same machine, so LiteNetLib paired them on the internal address:
  traversal through two separate home routers is untested, and symmetric NAT or carrier-grade NAT on both ends
  will defeat it however well the rest works.
- **Both sides need identical GameData.** The handshake checks the protocol version and nothing else - there is
  no mod manifest - so a single part mod on one side and not the other will fail while loading a vessel rather
  than telling you why. A stock install on both sides is the safe option.
- **Undocking is not implemented.** There is no `Undock` or `Decouple` message and no patch for either, so once
  two craft are docked they stay that way.
- **Physics authority does not return to the pilot after a dock.** The server hands the target vessel to the
  approaching player and never hands it back, so the merged craft ends up piloted by one player and simulated by
  the other. This is also why neither side can undock.
- **Stock docking magnets do not fire on a teleported approach.** The test harness closes the last centimetres
  itself, through `ModuleDockingNode.DockToVessel`, which is what the mod patches. A hand-flown dock has not been
  tried, so it is not known whether this affects normal play or only the harness.
- **Which client ends up owning a shared vessel is not deterministic.** The same scenario can hand authority to
  either player from one run to the next.

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
