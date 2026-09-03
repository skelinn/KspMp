using KspMp.Shared.Protocol;
using LiteNetLib.Utils;
using UnityEngine;

namespace KspMp.Systems
{
    /// <summary>
    /// Shared timeline warp. Local warp keys become requests to the server (TimeWarp_SetRate patch); the server
    /// answers with the negotiated WarpState, which every client applies. Also reports our altitude limit so nobody
    /// can out-warp what our KSP allows.
    /// </summary>
    public sealed class WarpSystem : SystemBase
    {
        private const float SceneGraceSeconds = 3f;

        /// <summary>True while we apply the server's rate, so the SetRate patch lets the call through.</summary>
        public static bool ApplyingServerState { get; private set; }

        private WarpStateMsg _state;
        private bool _hasState;
        private int _desired;
        private WarpMode _desiredMode;
        private int _reportedCap = int.MinValue;
        private float _nextCapCheckAt;
        private float _sceneLoadedAt;
        private float _reapplyAt = -1f;
        private bool _kspRefused;
        private float _nextRefusedRetryAt;

        public WarpSystem(KspMpAddon addon) : base(addon) { }

        public override string Name => "Warp";
        public bool HasState => _hasState;
        public WarpStateMsg State => _state;
        /// <summary>Warp keys pressed right after a scene load are KSP's own resets, not the player's wish.</summary>
        public bool InSceneGrace => Time.realtimeSinceStartup - _sceneLoadedAt < SceneGraceSeconds;

        public string StatusText
        {
            get
            {
                if (!_hasState || _state.RateIndex == 0) return "1x";
                var text = _state.Rate + "x" + (_state.Mode == WarpMode.Physics ? " physics" : "");
                if (_state.RequesterClientId != 0) text += " by " + NameOf(_state.RequesterClientId);
                if (_state.LimitingClientId != 0) text += ", limited by " + NameOf(_state.LimitingClientId);
                return text;
            }
        }

        private string NameOf(int clientId) => clientId == Net.ClientId ? "you" : Addon.Players.TryGet(clientId, out var p) ? p.Name : "#" + clientId;

        protected override void OnActivate()
        {
            Net.RegisterHandler(MessageId.WarpState, OnWarpState);
            GameEvents.onLevelWasLoadedGUIReady.Add(OnLevelLoaded);
            _sceneLoadedAt = Time.realtimeSinceStartup;
        }

        protected override void OnDeactivate()
        {
            Net.UnregisterHandler(MessageId.WarpState, OnWarpState);
            GameEvents.onLevelWasLoadedGUIReady.Remove(OnLevelLoaded);
            _hasState = false;
            _desired = 0;
            _reportedCap = int.MinValue;
        }

        /// <summary>Called by the TimeWarp.SetRate patch when the player (or KSP on their behalf) changes warp.</summary>
        public void RequestFromUser(WarpMode mode, int rateIndex)
        {
            _desired = rateIndex;
            _desiredMode = mode;
            SendRequest();
            if (rateIndex > 0)
                ScreenMessages.PostScreenMessage("Warp " + WarpRates.Rate(mode, rateIndex) + "x requested", 2f, ScreenMessageStyle.UPPER_CENTER);
        }

        private void SendRequest()
        {
            var cap = CurrentCap();
            _reportedCap = cap;
            Net.Send(MessageId.WarpRequest, new WarpRequestMsg { Mode = _desiredMode, DesiredIndex = _desired, MaxRailsIndex = cap }, Channel.Control, Delivery.ReliableOrdered);
        }

        private int CurrentCap()
        {
            if (!HighLogic.LoadedSceneIsFlight || TimeWarp.fetch == null) return -1;
            // KSP declined the shared rate (e.g. "cannot warp while moving over the surface"): what it accepted is our cap.
            if (_kspRefused) return TimeWarp.CurrentRateIndex;
            var vessel = FlightGlobals.ActiveVessel;
            if (vessel == null || vessel.mainBody == null || vessel.LandedOrSplashed) return -1;
            return TimeWarp.fetch.GetMaxRateForAltitude(vessel.altitude, vessel.mainBody);
        }

        public override void Update()
        {
            var now = Time.realtimeSinceStartup;
            if (_reapplyAt >= 0 && now >= _reapplyAt)
            {
                _reapplyAt = -1f;
                Apply();
            }
            if (_kspRefused && now >= _nextRefusedRetryAt)
            {
                // Try the shared rate again; if KSP takes it now, the cap report below lifts the limit.
                _kspRefused = false;
                Apply();
            }
            if (now < _nextCapCheckAt) return;
            _nextCapCheckAt = now + 2f;
            var cap = CurrentCap();
            if (cap != _reportedCap && Net.IsConnected) SendRequest();
        }

        private void OnWarpState(NetDataReader body)
        {
            _state = Envelope.Read<WarpStateMsg>(body);
            _hasState = true;
            Log.Info("Warp state: " + StatusText);
            Apply();
        }

        private void OnLevelLoaded(GameScenes scene)
        {
            _sceneLoadedAt = Time.realtimeSinceStartup;
            _reapplyAt = _sceneLoadedAt + 1f; // KSP resets warp on scene load; put the shared rate back
        }

        private void Apply()
        {
            if (!_hasState || TimeWarp.fetch == null || !HighLogic.LoadedSceneIsGame) return;
            var mode = _state.Mode == WarpMode.Physics ? TimeWarp.Modes.LOW : TimeWarp.Modes.HIGH;
            try
            {
                ApplyingServerState = true;
                if (TimeWarp.fetch.Mode != mode) TimeWarp.fetch.Mode = mode;
                if (TimeWarp.CurrentRateIndex != _state.RateIndex) TimeWarp.SetRate(_state.RateIndex, true, false);
            }
            finally
            {
                ApplyingServerState = false;
            }

            var refused = HighLogic.LoadedSceneIsFlight && _state.Mode == WarpMode.Rails && TimeWarp.CurrentRateIndex < _state.RateIndex;
            if (refused && !_kspRefused)
            {
                _kspRefused = true;
                _nextRefusedRetryAt = Time.realtimeSinceStartup + 5f;
                Log.Info("KSP declined warp index " + _state.RateIndex + " (stayed at " + TimeWarp.CurrentRateIndex + "); reporting that as our limit");
                SendRequest();
            }
            else if (!refused && _kspRefused)
            {
                _kspRefused = false;
                SendRequest();
            }
        }

        /// <summary>True when our KSP will not run the shared rate right now (surface movement, atmosphere, ...).</summary>
        public bool KspRefused => _kspRefused;
    }
}
