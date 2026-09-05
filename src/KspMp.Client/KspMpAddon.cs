using System;
using System.Collections.Generic;
using KspMp.Harmony;
using KspMp.Net;
using KspMp.Systems;
using KspMp.Ui;
using KspMp.Vessels;
using UnityEngine;

namespace KspMp
{
    /// <summary>
    /// Mod entry point. Created once by KSP's AddonLoader as soon as plugins load and kept alive across scenes.
    /// Owns settings, the Harmony patches, the network client, the systems and the UI windows.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public sealed class KspMpAddon : MonoBehaviour
    {
        public static KspMpAddon Instance { get; private set; }
        public static string Version => typeof(KspMpAddon).Assembly.GetName().Version.ToString(3);

        public Settings Settings { get; private set; }
        public ClientNetwork Network { get; private set; }
        public SystemRegistry Systems { get; private set; }
        public PlayersSystem Players { get; private set; }
        public ChatSystem Chat { get; private set; }
        public TimeSyncSystem TimeSync { get; private set; }
        public WarpSystem Warp { get; private set; }
        public RosterSystem Roster { get; private set; }
        public PresenceSystem Presence { get; private set; }
        public ControlSystem Control { get; private set; }
        public DockSystem Dock { get; private set; }
        public EditorSystem Editor { get; private set; }
        public VesselRegistry Vessels { get; private set; }
        public VesselProtoSystem VesselProto { get; private set; }
        public VesselStateSystem VesselState { get; private set; }
        public AuthoritySystem Authority { get; private set; }
        public LaunchOptions Launch { get; private set; }

        private MainMenuWindow _mainMenu;
        private bool _autoConnectDone;
        private bool _autoLaunchDone;
        private float _flyAt = -1f;
        private float _warpAt = -1f;
        private float _warpCancelAt = -1f;
        private float _stageAt = -1f;
        private float _toggleAt = -1f;
        private float _partEventAt = -1f;
        private float _orbitAt = -1f;
        private bool _dockSequenceStarted;
        private float _inputAt = -1f;
        private float _inputUntil = -1f;
        private HudWindow _hud;
        private DebugWindow _debug;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            Log.Info("KspMp " + Version + " starting. KSP " + Versioning.GetVersionString() + ", Unity " + Application.unityVersion
                     + ", " + Application.platform + ", runtime " + Environment.Version);

            Settings = Settings.Load();
            Launch = LaunchOptions.Parse(Environment.GetCommandLineArgs());
            if (!string.IsNullOrEmpty(Launch.PlayerName)) Settings.PlayerName = Launch.PlayerName;
            if (Launch.AutoConnect) Log.Info("Launch options: connect " + Launch.ConnectHost + ":" + Launch.ConnectPort + (Launch.EnterGame ? ", enter game" : "") + (Launch.Say != null ? ", say" : ""));
            try
            {
                HarmonyBootstrap.Patch();
            }
            catch (Exception e)
            {
                Log.Exception("Applying Harmony patches", e);
            }

            Network = new ClientNetwork(Settings);
            Vessels = new VesselRegistry();
            Network.Welcomed += welcome => { Vessels.LocalClientId = welcome.ClientId; RefreshSystems(); };
            Network.Disconnected += _ => { RefreshSystems(); Vessels.LocalClientId = 0; };
            Network.Welcomed += OnWelcomedForLaunchOptions;

            Systems = new SystemRegistry();
            Systems.Add(Players = new PlayersSystem(this));
            Systems.Add(Chat = new ChatSystem(this));
            Systems.Add(TimeSync = new TimeSyncSystem(this));
            Systems.Add(Warp = new WarpSystem(this));
            Systems.Add(Roster = new RosterSystem(this));
            Systems.Add(Presence = new PresenceSystem(this));
            Systems.Add(Authority = new AuthoritySystem(this));
            Systems.Add(Control = new ControlSystem(this));
            Systems.Add(Dock = new DockSystem(this));
            Systems.Add(Editor = new EditorSystem(this));
            Roster.SyncCompleted += TryAutoEnter;
            Roster.AvatarChanged += TryAutoEnter;
            Systems.Add(VesselProto = new VesselProtoSystem(this));
            Systems.Add(VesselState = new VesselStateSystem(this));

            _mainMenu = new MainMenuWindow(this);
            _hud = new HudWindow(this) { Visible = Settings.ShowHud };
            _debug = new DebugWindow(this) { Visible = Settings.ShowDebugWindow || Launch.Debug };

            GameEvents.onLevelWasLoadedGUIReady.Add(OnLevelLoaded);
        }

        private void OnLevelLoaded(GameScenes scene)
        {
            RefreshSystems();
            if (scene == GameScenes.MAINMENU && Launch.AutoConnect && !_autoConnectDone)
            {
                _autoConnectDone = true;
                Log.Info("Auto-connecting to " + Launch.ConnectHost + ":" + Launch.ConnectPort);
                Network.Connect(Launch.ConnectHost, Launch.ConnectPort);
            }
            if (scene == GameScenes.SPACECENTER && !string.IsNullOrEmpty(Launch.LaunchCraft) && !_autoLaunchDone && Network.IsConnected)
            {
                _autoLaunchDone = true;
                StartCoroutine(AutoLaunchAfterDelay(3f));
            }
            if (scene == GameScenes.SPACECENTER && !string.IsNullOrEmpty(Launch.EditorFacilityName) && !_autoEditorDone && Network.IsConnected)
            {
                _autoEditorDone = true;
                StartCoroutine(AutoOpenEditor(Launch.EditorAfterSeconds));
            }
            if (scene == GameScenes.EDITOR && !string.IsNullOrEmpty(Launch.EditorLoadCraft) && !_editorLoadDone)
            {
                _editorLoadDone = true;
                StartCoroutine(AutoLoadCraft(Launch.EditorLoadAfterSeconds));
            }
            if (scene == GameScenes.EDITOR && Launch.EditorWatchSeconds > 0 && !_editorWatchStarted)
            {
                _editorWatchStarted = true;
                StartCoroutine(EditorWatch(Launch.EditorWatchSeconds));
            }
            if (scene == GameScenes.FLIGHT && Launch.FlyAfterSeconds >= 0 && _autoLaunchDone && _flyAt < 0)
                _flyAt = Time.realtimeSinceStartup + Launch.FlyAfterSeconds;
            if (scene == GameScenes.FLIGHT && Launch.StageAfterSeconds >= 0 && _stageAt < 0)
                _stageAt = Time.realtimeSinceStartup + Launch.StageAfterSeconds;
            if (scene == GameScenes.FLIGHT && Launch.ToggleAfterSeconds >= 0 && _toggleAt < 0)
                _toggleAt = Time.realtimeSinceStartup + Launch.ToggleAfterSeconds;
            if (scene == GameScenes.FLIGHT && Launch.PartEventAfterSeconds >= 0 && _partEventAt < 0)
                _partEventAt = Time.realtimeSinceStartup + Launch.PartEventAfterSeconds;
            if (scene == GameScenes.FLIGHT && Launch.OrbitAfterSeconds >= 0 && _orbitAt < 0)
                _orbitAt = Time.realtimeSinceStartup + Launch.OrbitAfterSeconds;
            if (scene == GameScenes.FLIGHT && Launch.DockAfterSeconds >= 0 && !_dockSequenceStarted)
            {
                _dockSequenceStarted = true;
                StartCoroutine(AutoDockSequence(Launch.DockAfterSeconds));
            }
            if (scene == GameScenes.FLIGHT && Launch.InputAfterSeconds >= 0 && _inputAt < 0)
            {
                _inputAt = Time.realtimeSinceStartup + Launch.InputAfterSeconds;
                _inputUntil = _inputAt + Launch.InputDurationSeconds;
            }
            if (scene == GameScenes.FLIGHT && Launch.WarpIndex >= 0 && _autoLaunchDone && _warpAt < 0)
            {
                _warpAt = Time.realtimeSinceStartup + Launch.WarpAfterSeconds;
                _warpCancelAt = _warpAt + Launch.WarpDurationSeconds;
            }
        }

        private System.Collections.IEnumerator AutoLaunchAfterDelay(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            try
            {
                var craft = Launch.LaunchCraft.Replace('\\', '/');
                var path = System.IO.Path.IsPathRooted(craft) ? craft : KSPUtil.ApplicationRootPath + craft;
                if (!System.IO.File.Exists(path))
                {
                    Log.Error("Auto-launch: craft file not found: " + path);
                    yield break;
                }
                var site = !string.IsNullOrEmpty(Launch.LaunchSite) ? Launch.LaunchSite : craft.IndexOf("/SPH/", StringComparison.OrdinalIgnoreCase) >= 0 ? "Runway" : "LaunchPad";
                var craftNode = ConfigNode.Load(path);
                var manifest = HighLogic.CurrentGame.CrewRoster.DefaultCrewForVessel(craftNode);
                var seated = Game.SessionStarter.SeatAvatar(manifest);
                var extra = Game.SessionStarter.SeatCrew(manifest, Launch.ExtraCrew);
                Log.Info("Auto-launch: " + craft + " from " + site + (seated ? " with " + Roster.AvatarName + " in the first seat" : " with default crew") + (extra > 0 ? " and " + extra + " extra crew" : ""));
                FlightDriver.StartWithNewLaunch(path, "Squad/Flags/default", site, manifest);
            }
            catch (Exception e)
            {
                Log.Exception("Auto-launch", e);
            }
        }

        /// <summary>Toggles an action group the way the keyboard does, so co-pilot relaying is exercised.</summary>
        private void AutoToggleGroup()
        {
            var vessel = FlightGlobals.ActiveVessel;
            if (vessel == null) return;
            try
            {
                var group = (KSPActionGroup)Enum.Parse(typeof(KSPActionGroup), Launch.ToggleGroup, true);
                Log.Info("Auto-toggle: " + group + " on " + vessel.GetDisplayName());
                vessel.ActionGroups.ToggleGroup(group);
            }
            catch (Exception e)
            {
                Log.Exception("Auto-toggle " + Launch.ToggleGroup, e);
            }
        }

        /// <summary>
        /// Fires a part-menu action by name. A real player clicks the button (which the UIPartActionButton patch
        /// intercepts); this takes the same relay path directly so the flow can be tested without a mouse.
        /// </summary>
        private void AutoPartEvent()
        {
            var vessel = FlightGlobals.ActiveVessel;
            if (vessel == null || vessel.parts == null) return;
            foreach (var part in vessel.parts)
            {
                for (var m = 0; m < part.Modules.Count; m++)
                {
                    var evt = part.Modules[m].Events[Launch.PartEventName];
                    if (evt == null || !evt.active) continue;
                    if (Vessels.IsOwnedByOther(vessel.id) && Control.IAmAboard(vessel.id))
                    {
                        Log.Info("Auto-partevent: relaying '" + evt.name + "' on " + part.partInfo.title + " to the owner");
                        Control.SendPartEvent(vessel.id, part.flightID, m, evt.name);
                    }
                    else
                    {
                        Log.Info("Auto-partevent: invoking '" + evt.name + "' on " + part.partInfo.title + " locally");
                        evt.Invoke();
                    }
                    return;
                }
            }
            var available = new List<string>();
            foreach (var part in vessel.parts)
                for (var m = 0; m < part.Modules.Count; m++)
                    foreach (BaseEvent evt in part.Modules[m].Events)
                        if (evt.active && evt.guiActive && !available.Contains(evt.name)) available.Add(evt.name);
            Log.Warn("Auto-partevent: no active event named '" + Launch.PartEventName + "' on " + vessel.GetDisplayName() + ". Available: " + string.Join(", ", available.ToArray()));
        }

        /// <summary>
        /// Test harness: rendezvous with another player's ship and let the docking magnets take over. Runs in
        /// stages because each one needs the game a few seconds to catch up (load the target, unpack physics).
        /// </summary>
        private System.Collections.IEnumerator AutoDockSequence(float delaySeconds)
        {
            yield return new WaitForSeconds(delaySeconds);
            var ours = FlightGlobals.ActiveVessel;
            if (ours == null) { Log.Warn("Auto-dock: no active vessel"); yield break; }

            // The other ship may already be under our control, so look for anyone else's ship, ours or not.
            var target = Testing.TestRendezvous.FindDockingTarget(ours, id => id != ours.id && Vessels.IsKnown(id) && Vessels.OwnerOf(id) != 0);
            if (target == null) { Log.Warn("Auto-dock: found no other player's ship with a docking port"); yield break; }
            Log.Info("Auto-dock: target is " + target.GetDisplayName() + " at " + (target.GetWorldPos3D() - ours.GetWorldPos3D()).magnitude.ToString("F0") + " m"
                     + (Launch.DockRendezvous ? "" : "; assist mode, we will not move our ship"));

            var ourVesselId = ours.id;
            var ourPartCount = ours.parts != null ? ours.parts.Count : 0;
            if (Launch.DockRendezvous)
            {
                if (!Testing.TestRendezvous.MoveNear(ours, target, 30f)) yield break;
                yield return new WaitForSeconds(10f);
            }

            var aligned = false;
            var alignedDistance = double.MaxValue;
            for (var attempt = 1; attempt <= 12; attempt++)
            {
                var stillOurs = FlightGlobals.FindVessel(ourVesselId);
                var stillTheirs = FlightGlobals.FindVessel(target.id);
                if (stillOurs == null && stillTheirs != null)
                {
                    // Our ship is gone. That is a merge only if theirs grew; otherwise we were destroyed.
                    var grew = stillTheirs.parts != null && stillTheirs.parts.Count > ourPartCount;
                    Log.Info(grew
                        ? "Auto-dock: our ship merged into " + stillTheirs.GetDisplayName() + " (" + stillTheirs.parts.Count + " parts), docking succeeded"
                        : "Auto-dock: our ship was destroyed, not docked (" + stillTheirs.GetDisplayName() + " still has " + (stillTheirs.parts != null ? stillTheirs.parts.Count : 0) + " parts)");
                    yield break;
                }
                ours = stillOurs;
                target = stillTheirs;
                if (ours == null || target == null) { Log.Warn("Auto-dock: lost a vessel"); yield break; }
                if (ours.parts != null && ours.parts.Count > ourPartCount)
                {
                    Log.Info("Auto-dock: our ship absorbed theirs (" + ourPartCount + " -> " + ours.parts.Count + " parts), docking succeeded");
                    yield break;
                }
                if (!target.loaded)
                {
                    Log.Info("Auto-dock: waiting for " + target.GetDisplayName() + " to load (attempt " + attempt + ")");
                    yield return new WaitForSeconds(4f);
                    continue;
                }
                var weOwnBoth = Vessels.IsMine(ours.id) && Vessels.IsMine(target.id);
                Log.Info("Auto-dock: attempt " + attempt + "; we simulate ours=" + Vessels.IsMine(ours.id) + " theirs=" + Vessels.IsMine(target.id)
                         + "; distance " + (ours.GetWorldPos3D() - target.GetWorldPos3D()).magnitude.ToString("F1") + " m; target packed=" + target.packed
                         + "; " + Testing.TestRendezvous.DescribePorts(ours, target));
                if (!weOwnBoth)
                {
                    // Only the client simulating both ships can put them together; the other one waits for the
                    // server to hand the pair over, which the approach reports keep requesting.
                    yield return new WaitForSeconds(5f);
                    continue;
                }
                // Both ships are ours to move, so KSP's own docking logic should be engaging by now. Dump
                // what the nodes themselves think, above all whether their approach scan sees each other.
                Log.Info("Auto-dock: nodes " + Testing.TestRendezvous.DescribeDockingNodes(ours, target));
                // Teleport into place only once: repeating it every few seconds resets the physics and never
                // lets the magnets finish pulling the ports together. The approach is then held by renewing
                // velocity alone, below.
                if (!aligned || (ours.GetWorldPos3D() - target.GetWorldPos3D()).magnitude > alignedDistance + 1.0)
                {
                    // Start a little further out and drift in, the way a player finishes a docking.
                    Testing.TestRendezvous.AlignPorts(ours, target, 0.6f, closingSpeed: 0.15f);
                    aligned = true;
                    alignedDistance = (ours.GetWorldPos3D() - target.GetWorldPos3D()).magnitude;
                }
                // Give stock acquisition a couple of attempts, then drive the dock ourselves: the node's
                // approach scan never pairs teleported ports, so waiting longer only burns attempts.
                if (aligned && attempt >= 3 && Testing.TestRendezvous.ForceDock(ours, target))
                {
                    yield return new WaitForSeconds(3f);
                    continue;
                }
                // One velocity write fades within a frame or two as KSP re-derives part velocities, so a
                // single push per attempt leaves the ports creeping apart faster than they close. Renew the
                // approach twice a second instead, without teleporting anything.
                for (var tick = 0; tick < 12; tick++)
                {
                    yield return new WaitForSeconds(0.5f);
                    var closingOurs = FlightGlobals.FindVessel(ourVesselId);
                    var closingTarget = FlightGlobals.FindVessel(target.id);
                    if (closingOurs == null || closingTarget == null) break;
                    if (closingOurs.parts != null && closingOurs.parts.Count > ourPartCount) break;
                    if (!Testing.TestRendezvous.HoldApproach(closingOurs, closingTarget, 0.15f)) break;
                }
            }
            Log.Warn("Auto-dock: gave up after 12 attempts");
        }

        private void AutoFly()
        {
            var vessel = FlightGlobals.ActiveVessel;
            if (vessel == null || !FlightGlobals.ready) return;
            _flyAt = -1f;
            try
            {
                vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, true);
                FlightInputHandler.state.mainThrottle = 1f;
                KSP.UI.Screens.StageManager.ActivateNextStage();
                Log.Info("Auto-fly: SAS on, full throttle, staged");
            }
            catch (Exception e)
            {
                Log.Exception("Auto-fly", e);
            }
        }

        private bool _autoEntered;
        private bool _autoEditorDone;
        private bool _editorLoadDone;
        private bool _editorWatchStarted;

        /// <summary>Test harness: open the VAB or SPH so two clients end up on one shared workbench.</summary>
        private System.Collections.IEnumerator AutoOpenEditor(float delaySeconds)
        {
            yield return new WaitForSeconds(Mathf.Max(delaySeconds, 0f));
            var wantSph = Launch.EditorFacilityName != null
                          && Launch.EditorFacilityName.Equals("SPH", StringComparison.OrdinalIgnoreCase);
            var facility = wantSph ? EditorFacility.SPH : EditorFacility.VAB;
            Log.Info("Auto-editor: opening the " + facility);
            EditorDriver.StartupBehaviour = EditorDriver.StartupBehaviours.START_CLEAN;
            EditorDriver.StartEditor(facility);
        }

        private System.Collections.IEnumerator AutoLoadCraft(float delaySeconds)
        {
            yield return new WaitForSeconds(Mathf.Max(delaySeconds, 0f));
            LoadCraftIntoEditor();
        }

        /// <summary>
        /// Test harness: drop a craft onto the shared workbench. This mirrors how EditorSystem applies a
        /// snapshot it receives, but without suppressing the change event, so the local edit path runs
        /// exactly as it would for a part attached by hand and the craft is shared with everyone else.
        /// </summary>
        private void LoadCraftIntoEditor()
        {
            var editor = EditorLogic.fetch;
            if (editor == null) { Log.Warn("Auto-editor: no editor open to load into"); return; }
            var path = System.IO.Path.Combine(KSPUtil.ApplicationRootPath, Launch.EditorLoadCraft.Replace(System.IO.Path.DirectorySeparatorChar, '/'));
            if (!System.IO.File.Exists(path)) { Log.Warn("Auto-editor: no craft file at " + path); return; }
            try
            {
                var file = ConfigNode.Load(path);
                var shipNode = file == null ? null : (file.GetNode("ShipConstruct") ?? file);
                var ship = new ShipConstruct();
                if (shipNode == null || !ship.LoadShip(shipNode))
                {
                    Log.Warn("Auto-editor: could not read a craft out of " + path);
                    return;
                }
                editor.ship.Clear();
                EditorLogic.fetch.ship = ship;
                editor.SetBackup();
                GameEvents.onEditorShipModified.Fire(ship);
                Log.Info("Auto-editor: loaded " + ship.shipName + " ("
                         + (ship.parts != null ? ship.parts.Count : 0) + " part(s)) onto the shared workbench");
            }
            catch (Exception e)
            {
                Log.Exception("Auto-editor: loading a craft", e);
            }
        }

        private System.Collections.IEnumerator EditorWatch(float everySeconds)
        {
            while (HighLogic.LoadedScene == GameScenes.EDITOR)
            {
                yield return new WaitForSeconds(Mathf.Max(everySeconds, 1f));
                LogEditorState();
            }
        }

        /// <summary>What this client believes is on the workbench; two clients' lines should match exactly.</summary>
        private void LogEditorState()
        {
            var editor = EditorLogic.fetch;
            if (editor == null || editor.ship == null) { Log.Info("Auto-editor: nothing on the workbench yet"); return; }
            var parts = editor.ship.parts != null ? editor.ship.parts.Count : 0;
            var hash = "n/a";
            try
            {
                var node = editor.ship.SaveShip();
                if (node != null)
                {
                    var text = KspMp.Vessels.ProtoCodec.ToText(node);
                    hash = text.Length + ":" + text.GetHashCode();
                }
            }
            catch (Exception e) { hash = "threw " + e.GetBaseException().GetType().Name; }
            Log.Info("Auto-editor: workbench '" + editor.ship.shipName + "' parts=" + parts + " hash=" + hash
                     + " builders=" + (Editor != null ? Editor.BuilderCount : 0)
                     + " revision=" + (Editor != null ? Editor.Revision : -1)
                     + " sent=" + (Editor != null ? Editor.SnapshotsSent : -1)
                     + " applied=" + (Editor != null ? Editor.SnapshotsApplied : -1));
        }

        private void OnWelcomedForLaunchOptions(Shared.Protocol.WelcomeMsg welcome)
        {
            if (!string.IsNullOrEmpty(Launch.Say)) Chat.Send(Launch.Say);
            if (welcome.NeedsAvatar && !string.IsNullOrEmpty(Launch.AvatarName))
            {
                Log.Info("Auto-claiming avatar " + Launch.AvatarName + " (" + Launch.AvatarTrait + ")");
                Roster.Claim(Launch.AvatarName, Launch.AvatarTrait);
            }
        }

        /// <summary>-kspmp-enter: enter the game once the roster/vessel sync is complete and we have a Kerbal.</summary>
        private void TryAutoEnter()
        {
            if (!Launch.EnterGame || _autoEntered || HighLogic.LoadedScene != GameScenes.MAINMENU) return;
            if (!Roster.Synced || Roster.NeedsAvatar) return;
            _autoEntered = true;
            try
            {
                Game.SessionStarter.EnterGame(TimeSync.HasSync ? TimeSync.ServerUt : Network.Welcome.UniversalTime);
            }
            catch (Exception e)
            {
                Log.Exception("Auto enter game", e);
            }
        }

        private void RefreshSystems()
        {
            Systems.Refresh(HighLogic.LoadedScene, Network.IsConnected);
        }

        private void Update()
        {
            Network.Poll();
            Systems.Update();

            if (_flyAt >= 0 && Time.realtimeSinceStartup >= _flyAt && HighLogic.LoadedSceneIsFlight) AutoFly();
            if (_stageAt >= 0 && Time.realtimeSinceStartup >= _stageAt && HighLogic.LoadedSceneIsFlight && FlightGlobals.ready)
            {
                _stageAt = -1f;
                Log.Info("Auto-stage: pressing space");
                KSP.UI.Screens.StageManager.ActivateNextStage();
            }
            if (_orbitAt >= 0 && Time.realtimeSinceStartup >= _orbitAt && HighLogic.LoadedSceneIsFlight && FlightGlobals.ready)
            {
                _orbitAt = -1f;
                Testing.TestRendezvous.PlaceInCircularOrbit(FlightGlobals.ActiveVessel, Launch.OrbitAltitudeKm * 1000);
            }
            if (_toggleAt >= 0 && Time.realtimeSinceStartup >= _toggleAt && HighLogic.LoadedSceneIsFlight && FlightGlobals.ready)
            {
                _toggleAt = -1f;
                AutoToggleGroup();
            }
            if (_partEventAt >= 0 && Time.realtimeSinceStartup >= _partEventAt && HighLogic.LoadedSceneIsFlight && FlightGlobals.ready)
            {
                _partEventAt = -1f;
                AutoPartEvent();
            }
            if (_inputAt >= 0 && HighLogic.LoadedSceneIsFlight && FlightGlobals.ready && Time.realtimeSinceStartup >= _inputAt)
            {
                if (Time.realtimeSinceStartup <= _inputUntil)
                {
                    FlightInputHandler.state.pitch = 0.3f;
                    FlightInputHandler.state.mainThrottle = 0.8f;
                }
                else
                {
                    _inputAt = -1f;
                    FlightInputHandler.state.pitch = 0f;
                    Log.Info("Auto-input: released");
                }
            }
            if (_warpAt >= 0 && Time.realtimeSinceStartup >= _warpAt && HighLogic.LoadedSceneIsFlight)
            {
                _warpAt = -1f;
                Log.Info("Auto-warp: requesting warp index " + Launch.WarpIndex);
                Warp.RequestFromUser(Shared.Protocol.WarpMode.Rails, Launch.WarpIndex);
            }
            if (_warpCancelAt >= 0 && Time.realtimeSinceStartup >= _warpCancelAt && HighLogic.LoadedSceneIsFlight)
            {
                _warpCancelAt = -1f;
                Log.Info("Auto-warp: cancelling warp");
                Warp.RequestFromUser(Shared.Protocol.WarpMode.Rails, 0);
            }

            var alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            if (alt && Input.GetKeyDown(KeyCode.F10)) _debug.Visible = !_debug.Visible;
            if (alt && Input.GetKeyDown(KeyCode.M)) _hud.Visible = !_hud.Visible;
        }

        private void FixedUpdate()
        {
            Systems.FixedUpdate();
        }

        private void LateUpdate()
        {
            Systems.LateUpdate();
        }

        private void OnGUI()
        {
            Editor.DrawOverlay();
            _mainMenu.Draw();
            _hud.Draw();
            _debug.Draw();
        }

        private void OnApplicationQuit()
        {
            Network?.Disconnect("quit");
        }

        private void OnDestroy()
        {
            GameEvents.onLevelWasLoadedGUIReady.Remove(OnLevelLoaded);
            Network?.Disconnect("unloaded");
            if (Instance == this) Instance = null;
        }
    }
}
