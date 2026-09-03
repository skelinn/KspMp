using System;
using KspMp.Harmony;
using KspMp.Spike;
using KspMp.Ui;
using UnityEngine;

namespace KspMp
{
    /// <summary>
    /// Mod entry point. Created once by KSP's AddonLoader as soon as plugins load and kept alive across scenes.
    /// Owns settings, the Harmony patches and (later) the network client and all per-scene systems.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public sealed class KspMpAddon : MonoBehaviour
    {
        public static KspMpAddon Instance { get; private set; }
        public static string Version => typeof(KspMpAddon).Assembly.GetName().Version.ToString(3);

        public Settings Settings { get; private set; }
        public NetSpike Spike { get; private set; }

        private DebugWindow _debugWindow;

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
            try
            {
                HarmonyBootstrap.Patch();
            }
            catch (Exception e)
            {
                Log.Exception("Applying Harmony patches", e);
            }

            Spike = new NetSpike();
            _debugWindow = new DebugWindow(this) { Visible = Settings.ShowDebugWindow };
        }

        private void Start()
        {
            Spike.Begin();
        }

        private void Update()
        {
            Spike.Update();
            if (Input.GetKeyDown(KeyCode.F10) && (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)))
                _debugWindow.Visible = !_debugWindow.Visible;
        }

        private void OnGUI()
        {
            _debugWindow?.Draw();
        }

        private void OnDestroy()
        {
            Spike?.Shutdown();
            if (Instance == this) Instance = null;
        }
    }
}
