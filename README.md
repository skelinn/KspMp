# KspMp — multiplayer for Kerbal Space Program 1.12.5

Play KSP with your friends on one shared timeline: everyone is their own Kerbal, you can sit in the same
rocket, share the controls, build together in the VAB/SPH, and dock.

Status: **M0-M7 flown, and played over Steam between two machines** with two clients on one machine: connect, lobby and chat; a shared clock; vessel
replication with physics authority; negotiated warp; Kerbal avatars and a shared roster; shared control, where
two players ride the same rocket and the co-pilot can stage, use action groups and steer; docking, which merges
the two craft and leaves the other player aboard as co-pilot; and a shared VAB/SPH workbench, where both
builders converge on one craft hash. See `docs/PLAN.md` for the plan, and the gaps below before you rely on any
of it.

Two players have since connected across the internet over Steam, which negotiated a direct link through a
double NAT that neither UPnP nor port forwarding could get past. Everything before that was verified over
localhost at `rtt 0 ms`, so the timing work - the shared clock, replica interpolation, shared-stick input - has
still had very little exposure to real latency.

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

The windows are drawn in the mod's own dark skin rather than KSP's pale one, which is a good deal easier to
read over a bright sky or a lit VAB. Each player gets a colour, and keeps it everywhere: their dot in the
player list, their name in chat, and the label over their cursor on the shared workbench. **Interface size**
at the foot of the main-menu window scales the windows, since IMGUI draws in raw pixels and would otherwise
shrink as your resolution grows; a fresh install works it out from your screen, and the choice is saved.

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
    -kspmp-screenshot D          D seconds after the main menu appears, save kspmp-screenshot.png beside the
                                 install, for checking how the interface actually renders

`scripts/run-clients.ps1 -kspmp-connect 127.0.0.1:7777 -kspmp-enter` launches both test copies straight into the game.

Server files live in the universe folder: `server.cfg` (name, port, max players, MOTD, `password`,
`sharedStickDefault`, `hostControlsWarp`, `upnp`), `time.cfg` (shared UT, saved every minute and on shutdown), `players.cfg` (known players and
their Kerbal avatars), `vessels/<id>.cfg` and `roster/<name>.cfg` (the shared world, readable KSP ConfigNode text).

## Hosting from inside the game

The server can run inside KSP, so hosting does not mean running a second program:

    KSP_x64.exe -kspmp-host 7777 -kspmp-allow "76561198000000000"

It listens two ways at once. The UDP socket takes players on the same network - and the host's own game, which
connects to 127.0.0.1 like anyone else, so hosting cannot quietly behave differently from joining. Steam takes
everyone else, without anybody touching a router; the log prints the Steam ID friends need. Either can fail to
start without taking the other down, so a host with no Steam is still reachable over UDP.

The world lives in `GameData/KspMp/PluginData/universe`, in the same format the dedicated server uses, so a
game started this way can be moved to a real server later by copying the folder.

Friends join with `-kspmp-steamjoin <your steam id>`, or with an address as before. Steam only delivers packets
from a player whose session has been accepted, and without a P2PSessionRequest callback the only way to accept
one is up front, which is why `-kspmp-allow` takes the Steam IDs you expect.

None of that needs the command line. The Multiplayer window on the main menu shows your Steam ID to copy and
send, a box to paste a friend's and join them, a friends list for the IDs allowed into a game you host, and a
Host button. The whole section is hidden when Steam is not available, since none of it would work.

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

### Scripted testing

`scripts/run-clients.ps1` takes `-ArgsA` / `-ArgsB` to give the two copies different arguments, which is how
they get different avatars (those must be unique) and different scripted actions:

    scripts\run-clients.ps1 -kspmp-connect 127.0.0.1:7777 -kspmp-enter `
      -ArgsA @('-kspmp-avatar','Alice Kerman:Pilot','-kspmp-launch','Ships/VAB/Kerbal X.craft','-kspmp-crew','Bob Kerman') `
      -ArgsB @('-kspmp-avatar','Bob Kerman:Pilot','-kspmp-joinflight')

Options added for the shared-building, shared-flight and EVA work, on top of those already in
`src/KspMp.Client/LaunchOptions.cs`:

    -kspmp-editordelete D     delete the last non-root part D seconds in (a deletion round-trip is the check
                              the ghost-part bug used to fail)
    -kspmp-editorjoin D       join the other player's workbench
    -kspmp-editorleave D      go back to our own
    -kspmp-joinflight         accept a flight invite as soon as it can be accepted, no countdown
    -kspmp-givecontrol D      give the active vessel to the other player aboard
    -kspmp-requestcontrol D   ask the pilot for control
    -kspmp-sharedstick D      turn shared stick on
    -kspmp-nametags on|off|all   `all` also tags our own vessel, for a self-check screenshot
    -kspmp-near D             pull up alongside another player's ship without docking
    -kspmp-eva D              send our avatar out of the airlock
    -kspmp-board D            climb back into the nearest craft with a free seat
    -kspmp-evamode frozen|live   pose remote kerbals ourselves (default), or leave KerbalEVA running
    -kspmp-evasync off        do not load other players' kerbals on EVA at all
    -kspmp-revert D           revert to launch D seconds into the flight
    -kspmp-reverteditor D     revert to the VAB D seconds into the flight
    -kspmp-launchafter D      wait D seconds at the space centre before -kspmp-launch (default 3), so one
                              player can launch after the other's flight has begun
    -kspmp-crash D            blow up the active vessel D seconds into the flight (every part explodes)

## Known gaps

Worth knowing before you play, roughly in the order you would hit them.

- **Hosting from inside the game keeps serving while the host loads a scene** (the server has its own
  thread), but the host's own rocket is not simulated during the load, so the others see it hold still and
  then catch up. A dedicated server (`KspMp.Server.Host`) is still the smoother option for three or more.
- **The introducer trusts registrations.** Two hosts using the same join code overwrite each other; pick a
  code nobody else would.
- **A host must know a joining player's Steam ID up front.** Steam discards packets from a session nobody
  accepted, and learning that someone wants in needs a P2PSessionRequest callback that is not written, so the
  IDs go in the Friends box or `-kspmp-allow` beforehand. The list is read when hosting starts, so adding
  somebody means restarting the host. Fine for friends, useless for strangers.
- **Hole punching has only been proven on one machine.** The registration, code lookup, introduction and
  connect all work, and a client whose only direct address was unroutable still reached the server through an
  introducer. But both ends were on the same machine, so LiteNetLib paired them on the internal address:
  traversal through two separate home routers is untested, and symmetric NAT or carrier-grade NAT on both ends
  will defeat it however well the rest works.
- **Both sides need identical GameData.** The handshake carries the number of loaded parts and a hash of their
  names, and the server tells everyone in chat when two players' differ (and when their KSP versions do), but
  it cannot say which part is missing where, and a craft using a part the other side lacks still fails while
  loading on their machine. A stock install on both sides is the safe option.
- **A co-pilot's part-menu sliders and toggles change their copy only.** Staging, action groups, SAS,
  the navball buttons, the brakes key and part-menu buttons (Deploy, Decouple, ...) reach the pilot; a
  thrust limiter, a fuel-flow toggle, a resource transfer or a crew transfer made by a co-pilot does not, and
  the pilot's next snapshot puts it back. The pilot's own changes of that kind reach the co-pilot with the
  next snapshot (within about thirty seconds), not at once.
- **Docking hands the merged craft to whoever docked, and it stays theirs until someone leaves it.** Only
  the player simulating the merged craft can undock; the piece that comes off is theirs as a new vessel, and
  a piece whose pilot is somebody else goes back to that pilot the moment they are no longer aboard the same
  craft. For three minutes after a separation the approach rule leaves the pair alone, so nobody's ship is
  pulled out from under them while they drift apart. Ask for control with **Request control** otherwise.
- **Crew seating is shared by part type.** Who sits where travels with the bench, so whoever launches a
  shared craft launches it with the seating you both saw. Seats are matched by part type and rank, so a craft
  with two identical crewed parts may swap their occupants on the other machine.
- **Stock docking magnets do not fire on a teleported approach.** The test harness closes the last centimetres
  itself, through `ModuleDockingNode.DockToVessel`, which is what the mod patches. A hand-flown dock has not been
  tried, so it is not known whether this affects normal play or only the harness.
- **An unowned vessel goes to whoever is aboard and flying it**, else to whoever asked first. After a server
  restart nothing is owned until somebody asks.

## How playing together works

- Everyone shares one timeline. Warp is negotiated: the slowest request wins, anyone can drop back to 1x, and a
  player who cannot warp (in the atmosphere, moving on the ground) limits everyone.
- **Building.** Opening the VAB or SPH gives you your own workbench. Alt+M lists everyone else's under BUILDERS,
  with a Join button; joining sets your own craft aside and hands it back when you Leave. Anyone on a bench can
  launch it, and that ends the session for everyone on it. The bench is shared the instant it changes, but
  never while you are holding a part - what you have in hand goes out when you let go of it - and a craft
  arriving from the other builder leaves whatever you are holding in your hand. If you both move parts at the
  same moment one of the moves can lose (the whole craft is shared, not the edit), so take turns on a part.
  A craft arriving on an empty bench takes the editor out of its "pick a pod" state, so every part is
  available to whoever joined, not just the ones KSP allows as a first part.
- **Flying together.** The pilot simulates the craft; what they stage, toggle or press is mirrored onto every
  loaded copy of it - a co-pilot's, and a friend's watching from outside or on EVA beside it - so everyone
  sees the engines light, the chutes open and the boosters leave at the same moment. The pieces that
  separate are the pilot's; the copies that came off on your machine are adopted as theirs when the pilot's
  snapshot names them, so nothing blinks out and back. The pilot's tank
  levels are streamed to everyone aboard once a second, so a co-pilot's gauges read the same as the pilot's.
- **On EVA.** Everyone sees everyone's Kerbals, with name tags, and a jetpack's plumes and hiss show on every
  machine: the owner streams what the pack is doing and the others light the same effects KSP would.
- **Launching together.** Seat your friend's Kerbal in the crew tab on your side and launch. They get a notice
  saying their Kerbal is aboard, with a button to join: at the space centre it also counts down from ten and
  joins on its own; in the VAB it says "Leave the VAB and join", because nothing should drag you out of a build.
- **Flying together.** Whoever launched a craft flies it. Everyone else aboard is a co-pilot: they can stage, use
  action groups, SAS and part buttons, but their stick does nothing until the pilot turns on **Shared stick**.
  The pilot can hand over with **Give to <name>**, and a co-pilot can ask with **Request control**; both are in
  the FLIGHT section of Alt+M. Control only goes to somebody who is actually in the flight - handing a rocket to
  a player still in the VAB would leave nobody simulating it.
- **EVA.** Climb out and your friend sees your Kerbal, with your name over them. You can only move your own
  Kerbal, and only the player simulating a craft can take crew out of it or put crew into it - so the mod asks
  them to, rather than doing it behind their back.
- **Nametags** name other players' craft and Kerbals in each player's colour, out to 5 km (1 km for a Kerbal).
  Turn them off in Alt+M.
- A vessel with nobody's Kerbal aboard is simulated by whoever is nearest; uncrewed probes can be flown by anyone.
- **Dying.** A Kerbal who dies is back at the astronaut complex five seconds later, for everyone. Stock KSP
  brings a missing Kerbal back after hours of game time, which nobody can warp through alone on a shared
  timeline, so a player whose Kerbal died would otherwise be locked out of launching. If the craft you are
  riding in is destroyed on its pilot's machine, it is destroyed on yours too, and your Kerbal comes home.
- Another player's rocket stays in your sky all the way up. KSP deletes any vessel it is not simulating once it
  is out of physics range and still in the atmosphere; for a vessel somebody else flies that is switched off,
  since they stream where it is.
- Pause only pauses your menu, and quickload is disabled. **Revert to launch** and **Revert to VAB/SPH** work
  for the player flying a craft, as long as nobody else is aboard: everything that flight created since launch
  (spent stages, a Kerbal on EVA, flags) is withdrawn from the server first, and with revert to launch the
  rocket is back on the pad for everyone. A co-pilot cannot revert, and a pilot cannot revert out from under a
  co-pilot; get them to climb out or leave first.

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

MIT, in `LICENSE`. Everything KspMp is built on is MIT too, so nothing here narrows what you can do with it;
those notices are in `THIRD_PARTY_NOTICES.md`, and keeping them with any copy you pass on is the one thing
their licences ask for.
