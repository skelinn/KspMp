# KSP Multiplayer Mod ("KspMp") — Implementation Plan

## Context

The user wants a new Kerbal Space Program multiplayer mod for playing with a small group of friends. Headline features, in the user's words: seamless control of rockets, being in the same rocket as a friend, building together in the VAB and SPH, docking, and eventually mod support.

Why a new mod: the existing KSP 1 multiplayer mods (LunaMultiplayer, DarkMultiPlayer, the dead KerbalMultiPlayer) all use one controlling player per vessel, enforced by a server lock system, plus per-player "subspaces" for time warp. Neither shared control of one vessel nor collaborative editing has ever been built for KSP 1 (verified 2026-09-02 on GitHub and the KSP forums). Docking across subspaces has been a bug source in DMP since 2016 and in LMP through 2026. The user's goals cut directly against that model, so a fresh design is warranted.

Target: **KSP 1.12.5** (the final KSP 1 release). KSP 2 is abandoned (Intercept Games closed June 2024; multiplayer never shipped) and is not a target.

Decisions made with the user on 2026-09-02:

| Decision | Choice |
|---|---|
| Codebase | Fresh codebase; LunaMultiplayer (MIT) and DarkMultiPlayer (MIT) used as reference, small pieces borrowed with attribution |
| Time model | One shared timeline; warp negotiated (lowest requested rate wins, anyone can cancel) |
| Player model | **Each player is embodied as their own Kerbal avatar** |
| Game modes | Sandbox first; science/career scenario sync is a later milestone |
| Platforms | **Windows first**: the user plays on Windows and develops on both a Windows home PC and this Mac (at school). Build is cross-platform; Windows is the release/play target; the Mac install is used for local testing |

Intended outcome: a buildable, deployable mod skeleton with a two-client local test loop, then milestone-by-milestone delivery of connection, vessel replication, shared time, roster/avatars, shared control, docking, shared VAB/SPH, and a mod API.

## Environment (verified 2026-09-02)

- Project dir `/Users/campbellscott/ksp mp` is empty and not a git repo (initialized in M0).
- Mac KSP root: `/Users/campbellscott/Library/Application Support/Steam/steamapps/common/Kerbal Space Program` — 1.12.5 build 3190, Unity 2019.4.18f1, x86_64 under Rosetta 2 on this arm64 Mac. Both DLCs. GameData is vanilla (no Harmony, no ModuleManager). Managed dir: `KSP.app/Contents/Resources/Data/Managed/`; executable `KSP.app/Contents/MacOS/KSP`.
- **The Managed folder ships a minimal BCL**: `mscorlib`, `System`, `System.Core`, `System.Xml`, `System.Configuration`, `System.Security`, `Mono.*` only. **No `netstandard.dll` facade, no `System.Runtime`, `System.Numerics`, `System.IO.Compression`, no Steamworks.NET.** Consequence: every DLL placed in GameData must be a **net472** build; netstandard2.0 NuGet binaries will not load. `System.IO.Compression.DeflateStream` (in `System.dll`) is available.
- Windows home PC (not surveyed; assume Steam default): root `C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program`, managed `KSP_x64_Data\Managed\`, executable `KSP_x64.exe`. Confirm in M0 that its Managed folder matches the Mac one.
- Toolchain on this Mac: no .NET SDK, Mono, MSBuild, NuGet, Homebrew or IDE. git 2.50, gh 2.97, Node 24 exist.
- Template precedent: `~/wbmp` (the user's WorldBox Multiplayer mod): `global.json`, `Directory.Build.props` with env-var-fed assemblies dir, net472 csproj with `<Private>false</Private>` HintPath references and a target that errors when the assemblies dir is missing, `INetTransport.cs`. Reuse the shape, not the code.

## Toolchain decisions

| Concern | Choice | Notes |
|---|---|---|
| SDK | .NET 10 SDK on both machines. Mac: `curl -sSL https://dot.net/v1/dotnet-install.sh \| bash /dev/stdin --channel 10.0 --install-dir ~/.dotnet` then `DOTNET_ROOT=$HOME/.dotnet`, PATH += `$DOTNET_ROOT:$DOTNET_ROOT/tools`. Windows: `winget install Microsoft.DotNet.SDK.10` or `dotnet-install.ps1 -Channel 10.0` | net472 builds without Mono because the SDK pulls `Microsoft.NETFramework.ReferenceAssemblies`. `global.json`: `10.0.x`, `rollForward: latestFeature` |
| Source sync | git repo in the project dir + GitHub remote (`gh repo create`, after the user confirms) so work moves between Mac and PC | `.gitignore`: `bin/ obj/ artifacts/ *.user KspRoot.user.props decompiled/ test-installs/ GameData/KspMp/Plugins/ *.dll *.pdb` |
| Client target | `net472`, `LangVersion 12`, `AllowUnsafeBlocks`, portable PDBs | Runtime is Mono 4.7.2: no `Span<T>`, `System.Numerics`, `ZipArchive`; records/`init` need an `IsExternalInit` shim |
| Shared/Server libs | multi-target `net472;netstandard2.0` | Client consumes net472 builds; the console host consumes netstandard |
| Networking | **LiteNetLib 1.3.5 compiled from source** as a git submodule at tag `1.3.5` inside `src/KspMp.Net.LiteNetLib` (`net472;netstandard2.0`, `LangVersion 7.3`) | 1.3.5 release notes: net471 dropped from the NuGet package "but it will work from source". NOT 2.x (netstandard2.1-only). NOT 1.3.0/1.3.1 (deprecated) |
| KSP references | Hand-rolled `<Reference>` + `HintPath` into `$(KspManagedDir)` (like `~/wbmp`); `KspRoot` from `KspRoot.user.props` → `KSP_ROOT` env var → per-OS Steam default | KSPBuildTools 1.1.1 does the same discovery (also honours `KSP_ROOT`) but is young and auto-references every KSP DLL; switching later is cheap |
| Private access | `Krafs.Publicizer` 2.2.1 on Assembly-CSharp (whole assembly first; narrow to members later) | The version KSPCommunityFixes ships with; works on Mono with default strategies |
| Patching | Harmony 2.2.x: `Lib.Harmony 2.2.2` NuGet with `ExcludeAssets="runtime"`; the deploy target copies `0Harmony.dll` to `GameData/000_Harmony/` **only if that folder is absent** | Compatible with the community HarmonyKSP package (2.2.1). Never put `0Harmony.dll` in `KspMp/Plugins` |
| Serialization | `INetSerializable` structs over LiteNetLib `NetDataWriter`/`NetDataReader`; ConfigNode text deflated when > 512 B | No JSON on the hot path |
| Server host | `KspMp.Server.Host` net10.0 console first; in-process "Host game" from the main menu in M8 | Same library, two hosts |
| API research | `dotnet tool install --global ilspycmd`, then decompile Assembly-CSharp into untracked `decompiled/` and grep it | Never commit KSP assemblies or decompiled code |
| Local testing | Duplicate the KSP install twice (Windows `D:\ksp-a`, `D:\ksp-b` via robocopy; Mac `~/ksp-a`, `~/ksp-b` via rsync) and launch the executable directly with separate `-logFile` paths; server on `127.0.0.1:7777` | Bypasses the PD launcher and Steam's single-instance check; KSP is DRM-free; each copy has its own `settings.cfg`/`KSP.log` |

## Repository layout

```
KspMp.sln
global.json                      .NET SDK 10.0.x
Directory.Build.props            Deterministic, lock files, KspRoot/KspManagedDir/KspGameData resolution (OS-aware)
Directory.Packages.props         Lib.Harmony 2.2.2, Krafs.Publicizer 2.2.1, xunit
KspRoot.user.props               (gitignored) per-machine <KspRoot> override
.gitmodules                      third_party/LiteNetLib -> https://github.com/RevenantX/LiteNetLib @ 1.3.5
README.md  THIRD_PARTY_NOTICES.md (LMP, DMP, LiteNetLib, Harmony: MIT)
scripts/  install-dotnet, make-test-installs, deploy, run-server, run-clients, decompile   (.ps1 first, .sh twins)
src/KspMp.Shared/            net472;netstandard2.0  protocol, models, codecs, minimal ConfigNode parser; no Unity/KSP refs
src/KspMp.Net.LiteNetLib/    net472;netstandard2.0  <Compile Include="../../third_party/LiteNetLib/LiteNetLib/**/*.cs"/>
src/KspMp.Server/            net472;netstandard2.0  transport-agnostic server library
src/KspMp.Server.Host/       net10.0 console host (--port, --universe)
src/KspMp.Client/            net472 KSP plugin -> KspMp.dll (Systems/, Harmony/, Ui/, Net/, Game/)
src/KspMp.Api/               net472 public surface for other mods
tests/KspMp.Shared.Tests/    net10.0 xunit: codec round-trips, merge policy, warp negotiation, editor op log
tests/KspMp.Server.Tests/    net10.0 xunit: in-memory transport, authority migration, roster rules
GameData/KspMp/KspMp.version (KSP-AVC), Plugins/ (build output), PluginData/settings.cfg (runtime)
```

`Directory.Build.props` core:

```xml
<Import Project="$(MSBuildThisFileDirectory)KspRoot.user.props" Condition="Exists('$(MSBuildThisFileDirectory)KspRoot.user.props')" />
<PropertyGroup>
  <KspRoot Condition="'$(KspRoot)' == ''">$(KSP_ROOT)</KspRoot>
  <KspRoot Condition="'$(KspRoot)' == '' and $([MSBuild]::IsOSPlatform('Windows'))">C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program</KspRoot>
  <KspRoot Condition="'$(KspRoot)' == '' and $([MSBuild]::IsOSPlatform('OSX'))">$(HOME)/Library/Application Support/Steam/steamapps/common/Kerbal Space Program</KspRoot>
  <KspManagedDir Condition="$([MSBuild]::IsOSPlatform('Windows'))">$(KspRoot)\KSP_x64_Data\Managed</KspManagedDir>
  <KspManagedDir Condition="$([MSBuild]::IsOSPlatform('OSX'))">$(KspRoot)/KSP.app/Contents/Resources/Data/Managed</KspManagedDir>
  <KspGameData>$(KspRoot)/GameData</KspGameData>
</PropertyGroup>
```

`KspMp.Client.csproj` essentials: references (all `<Private>false</Private>`) to `Assembly-CSharp`, `Assembly-CSharp-firstpass`, `UnityEngine`, `UnityEngine.CoreModule`, `PhysicsModule`, `IMGUIModule`, `InputLegacyModule`, `UI`, `UIModule`, `TextRenderingModule`, `AnimationModule`, plus whichever assembly holds TextMeshPro (grep Managed in M0); `<Publicize Include="Assembly-CSharp" />`; a `CheckKsp` target that errors if `$(KspManagedDir)/Assembly-CSharp.dll` is missing; a `Deploy` target (`-p:KspMpDeploy=true`) copying `KspMp*.dll/pdb` to `$(KspGameData)/KspMp/Plugins` and `0Harmony.dll` to `000_Harmony` if absent. Assembly attributes `[KSPAssembly("KspMp", 0, 1)]` and `[KSPAssemblyDependency("0Harmony", 2, 2)]`.

## Architecture

### Client runtime (`src/KspMp.Client`)
- `KspMpAddon`: `[KSPAddon(KSPAddon.Startup.Instantly, true)]` MonoBehaviour, `DontDestroyOnLoad` (LMP `MainSystem.cs` / DMP `Client/Main.cs` pattern). `Update` → `ClientNetwork.Poll()` → `SystemRegistry.Update()`; `FixedUpdate`/`LateUpdate` forwarded; `OnGUI` → windows.
- `Net/ClientNetwork`: owns LiteNetLib `NetManager` with `UnsyncedEvents=false`, so every callback fires inside `PollEvents()` on the Unity main thread (this is the dispatcher). `ChannelsCount=4`, `UpdateTime=15`, `DisconnectTimeout=10000`. A `MainThread.Post(Action)` queue for the rare background task (deflating large protos).
- `Systems/SystemBase` (`Scenes[]`, `OnEnable/OnDisable/Update/FixedUpdate/LateUpdate`) + `SystemRegistry` toggled on `GameEvents.onLevelWasLoadedGUIReady` and `onGameSceneLoadRequested`. Systems: Handshake, Players, Chat, TimeSync, Warp, Roster, Presence, VesselProto, VesselState, Authority, Control, PartEvents, Crew, Dock, Editor, ModApi, Scenario (later).
- `Settings`: `GameData/KspMp/PluginData/settings.cfg` via `ConfigNode` (`PlayerName`, `PlayerId` Guid, `LastServer`, `Port`, `AvatarKerbalName`, `UiScale`, `LogLevel`).
- `Ui/` (IMGUI `GUILayout.Window`): main-menu "Multiplayer" button, ConnectWindow, AvatarWindow (first join: name + trait), LobbyWindow (players, presence, chat, Enter game), FlightOverlay (players, chat, warp state, pilot/co-pilot badges, Request pilot, Spectate), DebugWindow (net stats, authority table, craft hash).
- `Game/SessionStarter`: after handshake build a sandbox `Game` (`HighLogic.CurrentGame = new Game()`, `Mode = SANDBOX`, params from server, `startScene = SPACECENTER`, `flightState.universalTime = server UT`), `HighLogic.SaveFolder = "KspMp"`, apply roster then protos, `GamePersistence.SaveGame(..., "persistent", ..., SaveMode.OVERWRITE)`, `HighLogic.CurrentGame.Start()` (the DMP `StartGame()` / LMP `StartGameNow()` sequence).
- `Harmony/HarmonyBootstrap`: `new Harmony("com.campbellscott.kspmp").PatchAll()`; attribute patches in `Harmony/*.cs`, one class per patched KSP method, mirroring LMP's `LmpClient/Harmony/` naming.

### Transport and wire protocol (`src/KspMp.Shared/Protocol`)
- `INetTransport { Start(); Poll(); Send(PeerId, ArraySegment<byte>, Channel, Delivery); events Received/PeerConnected/PeerDisconnected }`. Implementations: `LiteNetLibTransport` (client+server), `LoopbackTransport` (tests, in-process host), `SteamTransport` (M8).
- Channels: `0 Control` ReliableOrdered, `1 State` Sequenced, `2 Bulk` ReliableOrdered (auto-fragmented protos/snapshots), `3 ChatMod` ReliableOrdered. Unreliable packets stay under ~1.2 KB (MTU 1432).
- Envelope `ushort MsgId | byte Flags(deflate, hasSeq) | [uint Seq] | payload`; each message is a `struct : INetSerializable` registered by explicit stable id in `MessageRegistry`.
- `ConfigNodeCodec`: `ConfigNode` → text using the publicized private `WriteNode`/`PreFormatConfig`/`RecurseFormat` (LMP does the same by reflection in `LmpClient/Extensions/ConfigNodeSerializer.cs`) → Deflate. Shared also carries a minimal ConfigNode text parser (`CfgNode`) so the server can inspect vessel documents without KSP.
- Catalogue (direction, delivery/channel, rate):

| Domain | Messages | Delivery | Rate |
|---|---|---|---|
| Handshake | `Hello{ProtocolVer, ModVersion, PlayerId, Name, KspVersion, ModHashes}` `Welcome{ClientId, Ut, Params, NeedsAvatar}` `Reject` `SyncBegin/Item/End` | Reliable 0/2 | once |
| Players/Chat | `PlayerJoined/Left`, `PlayerList`, `Ping/Pong`, `Chat` | Reliable 0/3 | on change |
| Time/Warp | `TimeSync{ServerUtcTicks, Ut, Rate}`, `TimeSyncReq`; `WarpRequest{RateIndex, Mode, MaxIndex}`, `WarpCancel`, `WarpState{Rate, Mode, Requesters, Ut}` | Unreliable 1; Reliable 0 | 2 Hz; on change |
| Roster | `KerbalProto{Name, Node}`, `KerbalStatus{Name, Status, InactiveEnd}`, `KerbalRemoved`, `AvatarClaim{Name, Trait}`, `AvatarClaimResult`, `AvatarRespawn{Name, AtUt}` | Reliable 0 | on change |
| Presence | `Presence{ClientId, State, VesselId, Scene, EditorSession}` | Reliable 0 | on change |
| Vessel | `VesselProto{VesselId, PersistentId, Reason, Node}`, `VesselRemove`, `VesselIdRemap{Old, New}`; `VesselState{VesselId, Ut, Body, Landed, Splashed, LatLonAlt, SrfVel, SrfRelRot, AngVel, OrbitElements(inc,e,sma,lan,argPe,mEp,epoch), HeightFromTerrain, Flags}` (~150 B) | Reliable 2; Sequenced 1 | events + 30 s; 20 Hz when a player is within pack range or aboard, else 2 Hz, on-rails 0.2 Hz |
| Authority | `AuthorityAssign/Request/Release`, `ControlLockRequest/Grant/Release`, `PilotRequest/Offer/Accept/Assign` | Reliable 0 | on change |
| Control | `CtrlInput{VesselId, Seq, Pitch, Yaw, Roll, X, Y, Z, MainThrottle, WheelSteer, WheelThrottle, Trims, KillRot, ActiveMask}` (fields as LMP `VesselFlightStateMsgData`) | Sequenced 1 | 30 Hz active, 2 Hz idle |
| Discrete | `Stage`, `ActionGroup`, `SasMode`, `PartEvent{VesselId, PartFlightId, ModuleIndex, EventName}`, `PartFieldChange` | Reliable 0 | user |
| Crew | `CrewEva`, `CrewBoard`, `CrewTransfer`, `SeatChange` | Reliable 0 | user |
| Docking | `DockIntent`, `DockCommit{DominantVesselId, PartFlightId, CoupledVesselId, CoupledPartFlightId, Ut}`, `Undock{VesselId, PartFlightId, NewVesselId, DockedVesselInfo}`, `Decouple` | Reliable 0 (+proto on 2) | user |
| Editor | `EditorSessionCreate/Join/Leave`, `EditorSnapshot{SessionId, Rev, CraftNode, ManifestNode}`, `EditorOp{SessionId, Rev, op}`, `EditorPresence{CursorRay, HeldPartUid, LockedSubtreeRoot}`, `EditorLaunch{SessionId, Site, ManifestNode}` | Reliable 0/2; presence Sequenced 1 | user; 10 Hz |
| Mod / Scenario | `ModMessage{Channel, Relay, Data}`; `ScenarioModule{Name, Node}` (M8) | per caller / Reliable 2 | caller |

### Server (`src/KspMp.Server`)
- Never runs KSP. It is an authoritative document store plus a relay that validates ownership. Documents on disk under `--universe`: `vessels/<id>.cfg`, `roster/<name>.cfg`, `players.json` (playerId → avatar), `time.json` (UT, rate, wall clock), `editor/<session>.log`, `server.json` (port, maxPlayers, hostControlsWarp, respawnSeconds, sharedStickDefault). Saved every 60 s and on shutdown.
- Bootstrap: an empty universe is populated by the first client to enter the game (it uploads the roster and empty vessel set of its fresh `new Game()`); later joiners replace their local roster/vessels with the server's.
- Services: `HandshakeService`, `PlayerRegistry`, `TimeService` (advances UT by wall clock × rate), `WarpService`, `RosterService`, `VesselStore`, `AuthorityService` (physics owner, pilot-by-seat, control locks, docking merge, migration), `EditorSessionService`, `ChatService`, `ModChannelRelay`.
- Pilot-by-seat and kerbal status are computed from vessel documents: each `PART { crew = Name }` line is a seat in order; the vessel-level `ref = <flightID>` names the reference (command) part.

### Vessel replication and physics authority
- Identity: `Vessel.id` (Guid, assignable), `Vessel.persistentId`, parts by `Part.flightID` (`FlightGlobals.FindPartByID`).
- Full snapshots (owner): `vessel.BackupVessel().Save(node)` → `VesselProto`. Triggers (LMP `VesselProtoEvents.cs`): `onFlightReady`, `onVesselWasModified` (debounced 0.5 s), after dock/undock, `onVesselCrewWasModified`, EVA/board, `onGameSceneLoadRequested` (final proto when leaving flight), `onVesselGoOnRails` for landed vessels, 30 s periodic for loaded owned vessels, 5 min for unloaded owned vessels.
- Applying a proto (LMP `VesselUtilities/VesselLoader.cs` sequence): `new ProtoVessel(node, HighLogic.CurrentGame)`; skip if the existing vessel has identical part/crew counts; else remove the old vessel (`FlightGlobals.RemoveVessel`, purge `flightState.protoVessels`, deactivate and destroy parts), `pv.Load(HighLogic.CurrentGame.flightState)`, null-check `vesselRef`, scrub null crew, `orbitDriver.updateFromParameters()` unless PRELAUNCH, NaN check, `KSCVesselMarkers.fetch.RefreshMarkers()`. Never reload a vessel the local player is aboard while docking or warping; reload the active vessel only via `FlightGlobals.ForceSetActiveVessel` after `OrbitPhysicsManager.HoldVesselUnpack(5)`. Removal uses a 2.5 s tombstone so a late proto cannot resurrect a vessel.
- State stream (owner, body-relative so it survives `FloatingOrigin`/`Krakensbane` shifts): body index, Landed/Splashed, lat/lon/alt, `srfRelRotation`, body-relative surface velocity, `angularVelocity`, `heightFromTerrain`, orbit elements.
- Receiver "replica" for any vessel not owned locally (`Systems/Vessel/Replica.cs`, the riskiest file): on attach disable `FlightIntegrator`, `CollisionEnhancer`, `PartBuoyancy`, set `crashTolerance` to infinity (LMP `SetImmortal`); each `FixedUpdate` (registered at `TimingManager` Precalc stage) interpolate between the last two states by UT (extrapolate ≤ 250 ms), position from `body.GetWorldSurfacePosition(lat, lon, alt)` when landed/splashed/below 10 km else from a temp `Orbit(...).getPositionAtUT(UT)`, rotation `body.rotation * srfRelRotation`; apply as LMP `VesselPositioner.cs` does (part transforms + `rb.position/rotation` + `ResumeVelocity` when unpacked; `vesselTransform` when unloaded), write back lat/lon/alt/Landed/Splashed, then `orbit.UpdateFromStateVectors` + `orbitDriver.updateFromParameters()` for packed vessels. Copy LMP's `OrbitDriver_UpdateFromParameters` prefix so KSP stops re-deriving orbits for non-owned vessels. M2 spike: `rb.isKinematic = true` on replicas (cleaner) with LMP's non-kinematic posing as fallback.
- Physics authority (server table `vesselId → ownerClientId`): **Rule A** the Pilot's client owns physics. **Rule B** uncrewed vessel → holder of its Mission Control control lock, else the client whose active vessel is nearest within 22.5 km, else nobody (every client propagates the orbit locally, which is deterministic given identical UT). **Rule C** two players' vessels within 2.5 km of each other stay owned by their own pilots; each simulates its own and replicates the other. Migration on `PlayerLeft`, presence change or range loss: server sends `AuthorityAssign`; the new owner applies the last `VesselState` (not the older proto) so there is no jump.
- Physics ranges from `Physics.cfg`: orbit/landed load 2250 / unload 2500 / pack 350 / unpack 200 m; flying load 2250 / unload 22500 / pack 25000 / unpack 2000 m. A friend's vessel within 200 m (orbit) or 2 km (flying) unpacks locally, so "loaded and unpacked but not owned" is the normal case the replica handles.

### Roster and avatars (MVP)
- Claim: `Welcome.NeedsAvatar` → AvatarWindow → `AvatarClaim{Name, Trait}`. Server validates uniqueness and stores `players.json`. The claiming client creates the kerbal: `HighLogic.CurrentGame.CrewRoster.GetNewKerbal(ProtoCrewMember.KerbalType.Crew)`, `ChangeName`, `KerbalRoster.SetExperienceTrait`, status `Available`, then sends `KerbalProto` which the server flags as an avatar. Every client keeps `AvatarRegistry` (kerbalName → playerId).
- Tamper protection (Harmony, LMP patches): prefix `KerbalRoster.SackAvailable`, prefix `ProtoCrewMember.Die` for avatars owned by others, `onCrewTransferSelected` handler setting `canTransfer=false` unless the mover owns the avatar, editor seat rules (below). NPC kerbals stay shared (`KerbalRoster.HireApplicant`, `OnCrewmemberHired/Sacked`).
- Replication: full roster on join (roster before vessels); deltas from `onKerbalAdded/Removed`, `onKerbalStatusChange`, `onKerbalTypeChange`, `onKerbalLevelUp`, `onKerbalNameChanged`, `onCrewKilled`. Receiver: `new ProtoCrewMember(mode, node)`; update in place via publicized `_rosterStatus`/`_type` (LMP `KerbalSystem` does this by reflection) to avoid re-firing events; else `CrewRoster.AddCrewMember`.
- Ordering rule: the **vessel proto is authoritative for placement** (which part/seat), the **roster message is authoritative for everything else** (status, experience, name, trait). Server derives Assigned/Available from vessel documents and re-emits `KerbalStatus` after each proto commit, so out-of-order arrival converges. LMP's `Part_RegisterCrew` prefix guards unknown names.
- Death/respawn: `onCrewKilled` on the owner → `KerbalStatus{Dead|Missing}`; with `MissingCrewsRespawn` the server schedules `AvatarRespawn{AtUt = deathUt + RespawnTimer}` and broadcasts `Available` at that UT; the player drops to Mission Control meanwhile.

### Presence and Mission Control
- `PresenceState = InFlight(vesselId) | OnEva(evaVesselId) | MissionControl(scene) | Editor(sessionId)`, derived from which vessel proto contains the avatar (server computes the same and rejects inconsistent claims).
- In flight the `Presence` system forces the active vessel to the avatar's vessel (`FlightGlobals.SetActiveVessel`, `ForceSetActiveVessel` after `HoldVesselUnpack(5)` while loading; LMP `VesselSwitcherSystem` waits for `loaded`). Vessel switching keys are locked (`InputLockManager.SetControlLock(ControlTypes.VESSEL_SWITCHING, "KspMp.presence")`) and `onVesselSwitching`/`onVesselChange` snap back. A remotely owned active vessel is simply a replica (this is LMP's spectate path, known to work with some jitter).
- Mission Control (avatar Available/Dead/Missing): KSC, Tracking Station, editors. From the Tracking Station (patch `SpaceTracking.FlyVessel` like LMP `SpaceTracking_FlyVessel.cs`) the player may **spectate** any vessel (all ship controls locked) or **remote-control a probe** (vessel with `ModuleCommand.minimumCrew == 0` and no avatar aboard) via a first-come `ControlLock` that also grants physics authority. Entering flight: `FlightDriver.StartAndFocusVessel(HighLogic.CurrentGame, index)` after the local `protoVessels` list is current.

### Shared timeline and warp
- Server owns UT and rate; `TimeSync` at 2 Hz carries server wall-clock ticks and UT; client target UT = `Ut + elapsed × Rate`. Correction tiers from LMP `TimeSyncSystem`: < 25 ms ignore; 25 ms–3.5 s skew `Time.timeScale` within 0.85–1.20 (only at 1×); > 3.5 s hard `Planetarium.SetUniversalTime` then `orbitDriver.updateFromParameters()` on every non-owned vessel. Never hard-set during docking or while the local player owns an unpacked vessel with others aboard (queue it).
- Warp negotiation: Harmony prefix on `TimeWarp.SetRate(int, bool, bool)` returns false and sends `WarpRequest` (LMP `TimeWarp_setRate.cs`). Server: effective rate = min of all requests (clients that do not care are excluded), `WarpCancel` returns everyone to 1×, `hostControlsWarp` option. Clients apply `TimeWarp.fetch.Mode` + `TimeWarp.SetRate(idx, true, false)` (DMP `WarpWorker`) with a re-entrancy guard; each client reports its altitude cap (`TimeWarp.fetch.GetMaxRateForAltitude`) as `MaxIndex`.
- Physics warp (2–4×) only when every loaded vessel in the requester's bubble is owned by the requester. After any warp ends: fresh `TimeSync`, owners send protos, replicas resync from state.
- Replacing LMP's "no pause/quickload/revert": pause is local UI only (`FlightDriver.SetPause` prefix returns false, time keeps flowing); quicksave allowed; quickload blocked (`QuickSaveLoad.quickLoad` prefix); revert blocked (`FlightDriver.RevertToLaunch/RevertToPrelaunch/ReturnToEditor` prefixes, `PauseMenu.drawStockRevertOptions` postfix); "Recover vessel" only for owners with no other player aboard.

### Seat-based shared control ("same rocket")
- Roles per vessel: **Pilot** = player whose avatar sits in seat 0 of the reference command part (`ProtoCrewMember.seatIdx == 0` in `Vessel.GetReferenceTransformPart()` with a `ModuleCommand`), else the first avatar in any `ModuleCommand` part; **Co-pilot** = any other player aboard; uncrewed → the Mission Control controller. Server recomputes on every proto/seat change and broadcasts `PilotAssign`; handover via `PilotRequest → PilotOffer → PilotAccept`, automatic when the pilot disconnects, EVAs or changes seats. Physics authority follows the pilot.
- Input path: the pilot's `Vessel.OnFlyByWire` callback merges co-pilot/controller `CtrlInput`s (LMP `VesselFlightStateSystem` hooks the same callback). Per axis: "last active input wins with a 300 ms hold" when `SharedStick` is on; otherwise only the pilot's axes count. Co-pilot clients capture their own `FlightCtrlState` in the replica's `OnFlyByWire`, send `CtrlInput` at 30 Hz, then overwrite it with the owner's merged state so plumes and control surfaces match; when `SharedStick` is off they get `ControlTypes.ALL_SHIP_CONTROLS` locked while staging and action groups stay free.
- Discrete actions (anyone aboard, and the controller): staging via prefix on `StageManager.ActivateNextStage/ActivateStage` (non-owners send `Stage`, owner applies); action groups via postfix `ActionGroupList.ToggleGroup` (LMP `ActionGroupList_ToggleGroup.cs`) → `vessel.ActionGroups.SetGroup` under a guard; SAS via prefix `VesselAutopilot.SetMode/Enable/Disable`; part buttons via prefix `UIPartActionButton.OnClick` (LMP `UIPartActionButton_OnClick.cs`) → `PartEvent` → `part.Modules[i].Events[name].Invoke()` guarded; tweakables via `BaseField.OnValueModified`/`UI_Control.onFieldChanged` first, LMP's `FieldChangeTranspiler` later, periodic proto as the safety net. Science, resources: proto-driven.
- Sitting in a remote vessel: `SetActiveVessel(replica)` plus IVA work normally; G-force/heat effects come later via `VesselState.Flags`; maneuver nodes are shared through the proto's `FLIGHTPLAN` node.

### Boarding, EVA, death
- EVA: stock hatch → `FlightEVA.fetch.spawnEVA(...)`; `onCrewOnEva` gives the new EVA vessel (`vessel.isEVA`). Client sends `CrewEva` + the EVA vessel's proto once the EVA FSM is ready (LMP waits for it). Only the avatar's owner may EVA their avatar (`onAttemptEva` handler + `ControlTypes.EVA_INPUT` lock). Presence → `OnEva`; source vessel roles recomputed (pilot EVA → handover).
- Boarding: postfix `KerbalEVA.proceedAndBoard`/`BoardPart`/`BoardSeat` (LMP `KerbalEVA_proceedAndBoard.cs`, `KerbalEVA_BoardSeat.cs`) → `CrewBoard`; boarding client sends `VesselRemove` for its EVA vessel and applies the crew locally (`part.AddCrewmember`); the owner's next proto confirms; presence → `InFlight`.
- Crew transfer: `onCrewTransferred` → `CrewTransfer` → `from.RemoveCrewmember` / `to.AddCrewmemberAt`; seat changes recompute Pilot.
- Death: owner sends `KerbalStatus{Dead|Missing}`; the player sees a short overlay then `HighLogic.LoadScene(GameScenes.SPACECENTER)`; respawn per roster section; the vessel continues under the recomputed pilot/owner.
- Several EVA kerbals near one vessel: each EVA vessel is owned by its player; others see kinematic replicas.

### Docking
- Precondition: both vessels owned by the same client. `Dock` system watches loaded vessel pairs closer than 50 m; the server forces authority of the non-pilot-crewed vessel to the other client; if both have pilots aboard the lower `persistentId` yields (that pilot temporarily flies a replica through input relay). Harmony prefixes on `ModuleDockingNode.DockToVessel` and `Part.Couple` return false unless `Authority.OwnsBoth(a, b)` (LMP `ModuleDockingNode_DockToVessel.cs`, `Part_Couple.cs`).
- Commit: owner sends `DockCommit` on `onDockingComplete`/`onPartCoupleComplete` then a proto of the merged vessel. Receivers (LMP `VesselCouple.ProcessCouple`) find both vessels and parts by `flightID`, `weakNode.DockToVessel(dominantNode)` (or `coupledPart.Couple(part)` for grapples) under a guard, then reconcile from the merged proto. `VesselIdRemap` moves presence/roles of players from the merged-away vessel (dominant pilot stays pilot, others become co-pilots). Replicas ignore `VesselState` for ids with a pending remap.
- Undock/decouple (owner only): prefix/postfix `Part.Undock`, `ModuleDockingNode.Undock`, `Part.decouple` (LMP `Part_Undock.cs`, `Part_Decouple.cs`) → `Undock`/`Decouple` with `NewVesselId` + both protos; receivers (LMP `VesselUndock.ProcessUndock`) undock locally then set `vessel.id = NewVesselId`; server recomputes roles/authority.

### Collaborative VAB/SPH
- **M7a snapshot sync + presence** (fast to get working): server-hosted editor session per facility; every `onEditorShipModified` on any client (debounced 300 ms, suppressed while a part is held) sends `EditorSnapshot` (`EditorLogic.fetch.ship.SaveShip()`); receivers rebuild via `ShipConstruct.LoadShip(node)` after clearing the current ship, preserving the local held part and camera; `EditorPresence` at 10 Hz draws other players' cursor labels (`editorCamera.WorldToScreenPoint`) and highlights their locked subtree (`Part.SetHighlight`). Last snapshot wins; the debug window shows a hash of `SaveShip()` on both clients for verification.
- **M7b part-level op log** (scales beyond two builders): stable ids are `Part.craftID`, allocated from server-issued ranges on `EditorSessionJoin`. Ops: `Spawn`, `Attach{parent, node id or surface, attPos0, attRotation0, symmetry mode/method}`, `Detach`, `Delete`, `Move`, `SetRoot`, `Tweak`, `Variant`, `ActionGroup`, `Crew{craftId, seat, kerbalName}`, `Meta`, `Subassembly`. Server serializes ops (ReliableOrdered), assigns `Rev`, rebroadcasts, appends to the session log; conflicts are last-writer-wins per part, and ops on a subtree locked by another player's drag are rejected with a resync. Capture points (names from KSPCommunityFixes `QoL/BetterEditorUndoRedo.cs`): `EditorLogic.attachPart`/`detachPart` (private), `DeletePart`, `SpawnPart`, `RestoreState` (undo/redo → snapshot), `GameEvents.onEditorPartEvent(ConstructionEventType, Part)`, `onEditorVariantApplied`, `onEditorSymmetryModeChange`, `onCrewDialogChange`; apply via `PartLoader.getPartInfoByName(...).partPrefab` instantiate, `part.setParent`, `AttachNode.attachedPart`, `EditorLogic.fetch.ship.Add`, `SetBackup()`, `EditorLogic.DeletePart`, `BaseField.SetValue`, `VesselCrewManifest.GetPartCrewManifest(craftId).AddCrewToSeat/RemoveCrewFromSeat` + `CrewAssignmentDialog.Instance.RefreshCrewLists(manifest, true, true)`. Symmetry: apply to the primary part and re-run stock symmetry; fall back to explicit counterpart ops. Snapshots every 10 s remain the recovery path.
- Crew rule: only the owning player may seat/unseat their avatar; NPCs are free.
- Shared launch: `EditorLaunch` from any session member; the launching client calls `EditorLogic.fetch.launchVessel(site)`; postfix `ShipConstruction.AssembleForLaunch` (LMP `ShipConstruction_AssembleForLaunch.cs`) sends the proto with `Reason=Launch` and the server makes the launcher physics owner; every player whose avatar is in the manifest gets `Presence{InFlight}` and enters flight via `StartAndFocusVessel`; Pilot by the seat rule. Copy LMP's `ShipConstruction_FindVesselsLandedAt`/`LaunchSiteClear_Test` patches so other players' pad vessels do not block launch; the server refuses a launch when another player's vessel sits on that pad.

### Mod support API (`src/KspMp.Api`)
```csharp
public static class KspMp.API {
  public static bool IsConnected { get; }
  public static Guid LocalPlayerId { get; }
  public static void RegisterHandler(string channel, Action<byte[]> onMessage);            // main thread, Update
  public static void RegisterFixedUpdateHandler(string channel, Action<byte[]> onMessage);
  public static bool UnregisterHandler(string channel);
  public static void Send(string channel, byte[] data, bool relay, DeliveryMode mode = DeliveryMode.ReliableOrdered);
  public static event Action<Guid> OnPlayerJoined, OnPlayerLeft;
  public static bool IsVesselOwnedLocally(Guid vesselId);
}
```
Semantics mirror DMP `DMPModInterface` and LMP `ModApiSystem.SendModMessage`. Part-module state already syncs through ProtoVessel ConfigNodes (`PartModule.OnSave`), so most part mods need nothing. Later: server `mod-control.json` (required/optional/forbidden DLL hashes, allowed parts/resources) compared against `Hello.ModHashes` (client hashes `GameData/**/*.dll` as LMP `ModSystem` does).

## Milestones

| M | Scope | Verification (two clients A/B + server unless noted) |
|---|---|---|
| **M0** Skeleton, build, deploy, spikes | git init, solution/props/csprojs, LiteNetLib submodule, `KspMpAddon`, `Settings`, `HarmonyBootstrap` with one trivial patch, `LiteNetLibTransport` loopback spike, scripts, decompile, test installs | `dotnet build -p:KspMpDeploy=true` succeeds on Mac (and later on Windows, same DLL). One client: `KSP.log` shows KspMp loaded, the Harmony patch applied, a LiteNetLib `NetManager` bound and a loopback connect completed inside KSP, a `DeflateStream` round trip logged. Confirm the Windows Managed folder matches |
| **M1** Connect, lobby, chat, clock | Server host, Handshake, Players, Chat, TimeSync (clock only), ConnectWindow, LobbyWindow, SessionStarter, universe persistence | A and B connect by name, see each other, chat both ways, ping < 5 ms on localhost; "Enter game" lands both in KSC with the same UT (logged delta); B reconnects after being killed |
| **M2** Vessel replication | VesselProto, VesselState, Replica, Authority (rules A/B/C, migration), tombstones, `OrbitDriver_UpdateFromParameters` patch, kinematic-replica spike | A flies to orbit; B sees the vessel in the Tracking Station with a live orbit. B launches; A sees B on the pad. Both within 2 km: each sees the other move smoothly, no explosions. A quits: B becomes owner (log) and the vessel keeps orbiting; A rejoins and gets it back |
| **M3** Shared timeline and warp | Warp system, `TimeWarp.SetRate` prefix, pause/quickload/revert patches, skew logic | A requests 100×, B 10× → both run 10×; B cancels → 1×; UT drift < 50 ms at 1× and < 1 s after 1000×; physics warp only when alone; pause menu opens without stopping time |
| **M4** Roster and avatars | AvatarWindow, Roster system, AvatarRegistry, tamper patches, Presence (Mission Control, spectate, probe control lock), death/respawn | First join asks for name/trait; both rosters agree; B cannot fire A's avatar; A launches with avatar aboard while B spectates; B remote-controls an uncrewed probe and A cannot; A's avatar dies → A returns to KSC and respawns on both clients after the timer |
| **M5** Shared control (seat model) | Control system, `CtrlInput` merge, discrete-action patches, PartEvent, SasMode, EVA/board/crew-transfer flow | A launches a two-seat craft with both avatars (B enters flight automatically). A flies; B stages, toggles gear/lights, deploys a panel from a part menu; both see everything. B requests pilot, A accepts → B's client owns physics (log). A EVAs and re-boards; rosters and vessels stay consistent |
| **M6** Docking | Dock system, authority merge at 50 m, couple/undock patches, id remap | A's station in orbit; B approaches: at 50 m B's client owns the station (log); docking succeeds, A sees the merged vessel and becomes co-pilot; undock gives a new id and A regains its vessel and pilot role. Repeat with roles swapped |
| **M7** Shared VAB/SPH | M7a snapshots + presence, then M7b op log; crew seat ops; shared launch | A and B enter one VAB session; each attaches parts; the debug window shows identical craft hashes; each seats their own avatar; A launches → both are in flight on the same vessel, A pilots, B stages |
| **M8** Mod API, Steam transport, scenario sync, in-process host | `KspMp.Api`, `ModMessage`, `SteamTransport` (**legacy `ISteamNetworking` P2P against KSP's own `steam_api64.dll`** - see the Steam findings below; the original plan to bundle Steamworks.NET and newer natives does not work), `ScenarioModule` messages, "Host game" button (`LoopbackTransport` + `KspMp.Server` inside KSP) | A tiny test mod sends a channel message A→B; two clients play over Steam relay without port forwarding; a science-mode save shares science |

## How to build and test

1. Install the SDK (Mac command above; Windows `winget` or the install script). `dotnet --version` prints 10.x.
2. `git submodule update --init`, set `KSP_ROOT` (or write `KspRoot.user.props`), then `dotnet build -c Debug -p:KspMpDeploy=true`.
3. `scripts/make-test-installs.ps1|.sh` once per machine (two ~6 GB copies). `scripts/deploy.ps1|.sh` copies GameData into both copies.
4. `scripts/run-server.ps1|.sh` (`dotnet run --project src/KspMp.Server.Host -- --port 7777 --universe ./universe`), then `scripts/run-clients.ps1|.sh` (Windows: `Start-Process D:\ksp-a\KSP_x64.exe -ArgumentList '-screen-width 1280 -screen-height 720 -screen-fullscreen 0 -logFile D:\ksp-a\player.log'`; Mac: `open -n -a ~/ksp-a/KSP.app --args ...`).
5. Watch `<install>/KSP.log` in each copy plus the server console; each milestone's verification column lists what to observe. `dotnet test` covers codecs, merge policy, warp negotiation and authority migration without KSP.

## Risks and early spikes

1. **LiteNetLib from source on Unity 2019.4 Mono** (M0): `LangVersion 7.3`, no `LITENETLIB_UNSAFE`, `UseNativeSockets=false`, `IPv6Enabled=false` if Mono complains.
2. **net472-only GameData** (M0): the design ships no netstandard binaries; confirm the Windows Managed folder is the same minimal set.
3. **Harmony double-loading**: only copy `0Harmony.dll` into `000_Harmony` when absent.
4. **Kinematic replicas** (M2): wheels/legs, ladders, EVA collisions and `Krakensbane` velocity when the active vessel is a replica; fallback to LMP's non-kinematic pose + `ResumeVelocity`.
5. **Floating origin**: all wire data is body-relative; apply positions in the same `FixedUpdate` phase every time and re-derive after `onFloatingOriginShift`; never cache world positions across frames.
6. **ProtoVessel ordering**: roster before vessels; never load a proto during a scene transition; copy LMP's checklist (null crew, NaN orbit).
7. **Editor private API**: confirm `attachPart`/`detachPart` signatures in the decompile; snapshot sync (M7a) is the permanent fallback.
8. **Debugging**: log-first. Debugger attach later via Unity 2019.4.18f1 development player files + `player-connection-debug=1` in `boot.config` (Windows: `KSP_x64_Data\boot.config`; Mac: inside the app bundle).
9. **Rosetta on the Mac**: two clients + server is heavy; use 1280×720 windows; timing-sensitive tests happen on Windows.
10. **Determinism**: none assumed; the owner's stream is truth.

Names to confirm in the decompile during M0 (flagged unverified by the design pass): `KSPAssemblyDependency` constructor arity, `CrewTransfer.CrewTransferData.canTransfer`, `ControlTypes.VESSEL_SWITCHING`/`EVA_INPUT`, `Vessel.GetReferenceTransformPart`, `BaseField.OnValueModified`, `UI_Control.onFieldChanged`, `Vessel.CrewListSetDirty`, `EditorLogic.attachPart/detachPart` parameters, `ShipConstruct.Clear`, `ConstructionEventType` members, `RespawnTimer` units (`ProtoCrewMember.StartRespawnPeriod`), `TimingManager.TimingStage.Precalc`, `MainMenu.Start`.

## Docking authority: why the obvious fix is not the fix (investigated 2026-09-05)

After a dock the merged craft is flown by one player and simulated by another. `HandleDockIntent` hands the
target to the approaching client and sets a 60 s hold; the pilot rule in `ControlService` is skipped while a
hold is active, and nothing re-runs it when the hold expires, so the wrong client keeps simulating.

Two apparently obvious fixes were tried and both are wrong:

- **Releasing the hold when the dock commits.** Authority does then go back to the pilot - the client logs
  `Authority for ... : us (Granted)` - but that client never actually takes up simulating it. `states sent`
  stops climbing on both sides and their altitudes diverge (one holds 100000 m while the other decays), so the
  end state is worse than the bug: instead of the wrong player simulating, nobody does. Something later in the
  merge resets the receiving client's registry ownership, and that has to be found first.
- **Sweeping lapsed holds from the server tick.** This breaks a dock that takes longer than the hold: authority
  is pulled back to the pilot mid-approach, when the whole point of the hold is to keep both craft under one
  simulator. `ServerDockingTests.WithTwoPilotsTheLowerPersistentIdYieldsAndTheHoldExpires` catches it.

So the server-side assignment is not the whole story. The next step is the client: find what resets
`RemoteVessel.OwnerClientId` after a merged proto arrives, and why a client told it owns a vessel does not
start sending its state.

## Steam transport: what is actually possible (verified 2026-09-05)

The M8 note used to say KSP ships no Steam natives and that we would bundle Steamworks.NET plus our own
`steam_api64.dll`. Both are wrong, and the second is not merely wrong but unworkable.

**KSP does ship the native.** `KSP_x64_Data/Plugins/x86_64/steam_api64.dll`, loaded at startup for Squad's
`GameData/Squad/Plugins/KSPSteamCtrlr.dll` (controller support only - it has no networking types). What is
missing from `Managed/` is the *managed wrapper*, not the native library.

**It is an old SDK.** Interface version strings are `SteamUser019` and `SteamUtils009`, which is the
Steamworks 1.42 era. That matters because:

- **The modern API is absent.** No `SteamNetworkingSockets`, no `ConnectP2P`, no `SteamNetworkingUtils`. So
  Steam Datagram Relay, the thing usually meant by "Steam relay", cannot be called at all.
- **Shipping a newer `steam_api64.dll` is not a way round it.** Windows loads a DLL of that name once per
  process, and KSP has already loaded its own before any addon runs. Our P/Invokes would bind to the old one
  and fail at the entry point. Renaming ours means forking Steamworks.NET to change its hardcoded library
  name, and then two Steam API instances would be initialised against one client - unsupported, and liable to
  break Squad's controller integration.

**The legacy P2P API is fully present**, and is enough:

    SteamAPI_ISteamNetworking_SendP2PPacket / ReadP2PPacket / IsP2PPacketAvailable
    SteamAPI_ISteamNetworking_AcceptP2PSessionWithUser / CloseP2PSessionWithUser
    SteamAPI_ISteamNetworking_AllowP2PPacketRelay      <- relay fallback when the punch fails
    SteamAPI_ISteamUser_GetSteamID, SteamAPI_RunCallbacks, SteamAPI_RegisterCallback
    SteamInternal_CreateInterface, SteamAPI_ISteamClient_GetISteamNetworking

`AllowP2PPacketRelay` is the part that matters for the players we cannot otherwise reach: Steam falls back to
its own relay servers when a direct connection cannot be made, which covers carrier-grade NAT, and Valve pays
for it rather than us.

**Steamworks.NET from NuGet is not usable**: 2024.8.0 is netstandard2.1 only, which net472 cannot consume and
which GameData could not load anyway. Either vendor a source release from the era that matches SDK 1.42, or -
probably better - write the dozen P/Invokes by hand, which keeps the surface small enough to audit and avoids
matching library versions to a native we do not control.

**The fiddly part is the session handshake.** Packets do not arrive until the receiving side has called
`AcceptP2PSessionWithUser`, and it learns who is asking from a `P2PSessionRequest` callback - so
`SteamAPI_RegisterCallback` and its dispatch machinery are needed, unless the host is given the joining
player's SteamID up front and accepts it proactively.

**Testing needs two Steam accounts on two machines.** Two KSP instances on one machine under one account
cannot exercise P2P against each other, so none of this can be verified the way M6 and M7 were.

## Reference reading (LMP `LmpClient/...` unless noted; DMP `Client/...`)

- Architecture: `MainSystem.cs`, `Base/System.cs`, `Base/SystemBase.cs`, `Base/MessageSystem.cs`; DMP `Main.cs`.
- Vessels: `Systems/VesselPositionSys/*`, `VesselUtilities/VesselLoader.cs`, `Systems/VesselProtoSys/*`, `Systems/VesselRemoveSys/*`, `Systems/VesselImmortalSys/*`, `Extensions/{VesselExtension,ProtoVesselExtension,ConfigNodeSerializer}.cs`, `Harmony/{OrbitDriver_UpdateFromParameters,OrbitDriver_TrackRigidbody,Vessel_GoOffRails,Vessel_GoOnRails,FlightIntegrator_FixedUpdate,Vessel_CheckKill}.cs`; DMP `VesselWorker.cs`, `VesselUpdate.cs`.
- Locks/switching: `Systems/Lock/LockSystem.cs`, `Systems/VesselLockSys/*`, `Systems/VesselSwitcherSys/*`, `Harmony/SpaceTracking_FlyVessel.cs`.
- Time/warp: `Systems/TimeSync/TimeSyncSystem.cs`, `Systems/Warp/*`, `Harmony/{TimeWarp_setRate,TimeWarp_setMode,FlightDriver_SetPause,PauseMenu_DrawRevertOptions,QuickSaveLoad_QuickLoad,FlightDriver_RevertToLaunch,FlightDriver_ReturnToEditor}.cs`; DMP `WarpWorker.cs`, `TimeSyncer.cs`.
- Control: `Systems/VesselFlightStateSys/*`, `LmpCommon/Message/Data/Vessel/VesselFlightStateMsgData.cs`, `Systems/VesselActionGroupSys/*`, `Systems/VesselPartSyncCallSys/*`, `Systems/VesselPartSyncFieldSys/*`, `ModuleStore/Patching/*`, `Harmony/{ActionGroupList_ToggleGroup,UIPartActionButton_OnClick}.cs`.
- Crew/EVA: `Systems/KerbalSys/*`, `Systems/VesselCrewSys/*`, `Harmony/{KerbalEVA_proceedAndBoard,KerbalEVA_BoardSeat,Part_RegisterCrew,ProtoVessel_AddCrew,ProtoCrewMember_Die,KerbalRoster_SackAvailable}.cs`; DMP `KerbalReassigner.cs`.
- Docking: `Systems/VesselCoupleSys/*`, `Systems/VesselUndockSys/*`, `Systems/VesselDecoupleSys/*`, `Harmony/{Part_Couple,Part_Undock,Part_Decouple,ModuleDockingNode_DockToVessel,ModuleDockingNode_Undock}.cs`.
- Launch/editor: `Harmony/{ShipConstruction_AssembleForLaunch,ShipConstruction_FindVesselsLandedAt,LaunchSiteClear_Test}.cs`, `Systems/SafetyBubble/*`; KSPCommunityFixes `QoL/BetterEditorUndoRedo.cs`.
- Mod API: `Systems/ModApi/ModApiSystem.cs`, `Systems/Mod/ModSystem.cs`; DMP `DMPModInterface.cs`.
- API docs: https://kspmoddinglibs.github.io/KSPDocsSite/ (1.12.4 Doxygen).

## Assumptions made without asking

- GitHub remote creation and the ~12 GB of test-install copies will be confirmed with the user before running.
- Shared VAB ships snapshot sync first (M7a) and the op log second (M7b).
- Publicizer pinned to 2.2.1 (known good with KSP); bump later.
- ~~Steam transport bundles Steamworks.NET rather than reusing KSP's Steam controller plugin.~~ Wrong on both counts; see the Steam findings below.
- Science/career sync, tourist/rescue contract handling and mod-control manifests are all M8+.
