using System;
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
            Systems.Add(Authority = new AuthoritySystem(this));
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
            if (scene == GameScenes.FLIGHT && Launch.FlyAfterSeconds >= 0 && _autoLaunchDone && _flyAt < 0)
                _flyAt = Time.realtimeSinceStartup + Launch.FlyAfterSeconds;
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
                Log.Info("Auto-launch: " + craft + " from " + site + " with default crew");
                FlightDriver.StartWithNewLaunch(path, "Squad/Flags/default", site, manifest);
            }
            catch (Exception e)
            {
                Log.Exception("Auto-launch", e);
            }
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

        private void OnWelcomedForLaunchOptions(Shared.Protocol.WelcomeMsg welcome)
        {
            if (!string.IsNullOrEmpty(Launch.Say)) Chat.Send(Launch.Say);
            if (Launch.EnterGame && HighLogic.LoadedScene == GameScenes.MAINMENU)
            {
                try
                {
                    Game.SessionStarter.EnterGame(welcome.UniversalTime);
                }
                catch (Exception e)
                {
                    Log.Exception("Auto enter game", e);
                }
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
