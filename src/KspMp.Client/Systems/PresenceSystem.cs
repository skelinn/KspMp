using System;
using System.Collections.Generic;
using KspMp.Shared.Protocol;
using LiteNetLib.Utils;
using UnityEngine;

namespace KspMp.Systems
{
    /// <summary>
    /// Where each player is. Ours is derived from where our avatar sits (or the scene we are in) and reported on
    /// change; in flight the camera follows the vessel our avatar is aboard.
    /// </summary>
    public sealed class PresenceSystem : SystemBase
    {
        private readonly Dictionary<int, PresenceMsg> _others = new Dictionary<int, PresenceMsg>();
        private PresenceMsg _mine;
        private bool _reported;
        private float _nextCheckAt;
        private Guid _lastSnappedTo;
        private Guid _lastEnteredFor;
        private float _sceneEnteredAt;

        public PresenceSystem(KspMpAddon addon) : base(addon) { }

        public override string Name => "Presence";
        public PresenceMsg Mine => _mine;
        public IReadOnlyDictionary<int, PresenceMsg> Others => _others;

        protected override void OnActivate()
        {
            Net.RegisterHandler(MessageId.Presence, OnPresence);
            GameEvents.onLevelWasLoadedGUIReady.Add(OnLevelLoaded);
            _reported = false;
            _nextCheckAt = 0f;
            _sceneEnteredAt = Time.realtimeSinceStartup;
        }

        protected override void OnDeactivate()
        {
            Net.UnregisterHandler(MessageId.Presence, OnPresence);
            GameEvents.onLevelWasLoadedGUIReady.Remove(OnLevelLoaded);
            _others.Clear();
            _reported = false;
        }

        private void OnLevelLoaded(GameScenes scene)
        {
            _sceneEnteredAt = Time.realtimeSinceStartup;
            _lastSnappedTo = Guid.Empty;
        }

        /// <summary>Our Kerbal was seated in a vessel that is now flying (a friend launched with us aboard): join it.</summary>
        private void MaybeEnterFlight(Vessel avatarVessel)
        {
            if (avatarVessel == null) return;
            var proto = avatarVessel.protoVessel;
            MaybeEnterFlightFromProto(proto, avatarVessel.GetDisplayName(), avatarVessel.id, proto != null ? proto.situation : avatarVessel.situation);
        }

        private void MaybeEnterFlightFromProto(ProtoVessel proto, string name)
        {
            if (proto != null) MaybeEnterFlightFromProto(proto, name, proto.vesselID, proto.situation);
        }

        private void MaybeEnterFlightFromProto(ProtoVessel proto, string name, Guid vesselId, Vessel.Situations situation)
        {
            var scene = HighLogic.LoadedScene;
            if (scene != GameScenes.SPACECENTER && scene != GameScenes.TRACKSTATION) return;
            if (Time.realtimeSinceStartup - _sceneEnteredAt < 3f || _lastEnteredFor == vesselId) return;
            if (situation == Vessel.Situations.LANDED || situation == Vessel.Situations.SPLASHED) return;
            var game = HighLogic.CurrentGame;
            var index = game.flightState.protoVessels.FindIndex(p => p != null && p.vesselID == vesselId);
            if (index < 0) return;
            _lastEnteredFor = vesselId;
            Log.Info("Entering flight: our Kerbal is aboard " + name + " (" + situation + ")");
            Addon.Chat.AddLocal("Your Kerbal is aboard " + name + "; joining the flight.");
            FlightDriver.StartAndFocusVessel(game, index);
        }

        public string Describe(int clientId)
        {
            if (clientId == Net.ClientId) return Describe(_mine);
            return _others.TryGetValue(clientId, out var p) ? Describe(p) : "";
        }

        public static string Describe(PresenceMsg p)
        {
            switch (p.State)
            {
                case PresenceState.InFlight: return "aboard " + p.VesselName;
                case PresenceState.OnEva: return "on EVA";
                case PresenceState.Editor: return "in the " + ((GameScenes)p.Scene == GameScenes.EDITOR ? "editor" : "VAB");
                default: return (GameScenes)p.Scene == GameScenes.TRACKSTATION ? "tracking station" : (GameScenes)p.Scene == GameScenes.FLIGHT ? "mission control (flight)" : "space center";
            }
        }

        public override void Update()
        {
            var now = Time.realtimeSinceStartup;
            if (now < _nextCheckAt) return;
            _nextCheckAt = now + 1f;
            if (!HighLogic.LoadedSceneIsGame) return;

            var presence = Compute(out var avatarVessel);
            if (!_reported || presence.State != _mine.State || presence.VesselId != _mine.VesselId || presence.Scene != _mine.Scene)
            {
                _mine = presence;
                _reported = true;
                Net.Send(MessageId.Presence, presence, Channel.Control, Delivery.ReliableOrdered);
                Log.Info("Presence: " + Describe(presence));
            }

            MaybeEnterFlight(avatarVessel);

            // The camera follows our Kerbal: if they sit in a loaded vessel that is not active, switch to it.
            if (HighLogic.LoadedSceneIsFlight && FlightGlobals.ready && avatarVessel != null && avatarVessel.loaded && FlightGlobals.ActiveVessel != avatarVessel && _lastSnappedTo != avatarVessel.id)
            {
                _lastSnappedTo = avatarVessel.id;
                Log.Info("Switching to " + avatarVessel.GetDisplayName() + " because our Kerbal is aboard");
                FlightGlobals.SetActiveVessel(avatarVessel);
            }
        }

        private PresenceMsg Compute(out Vessel avatarVessel)
        {
            avatarVessel = null;
            var scene = (byte)HighLogic.LoadedScene;
            var avatar = Addon.Roster.AvatarName;
            if (!string.IsNullOrEmpty(avatar) && FlightGlobals.fetch != null)
            {
                var vessels = FlightGlobals.Vessels;
                for (var i = 0; i < vessels.Count; i++)
                {
                    var vessel = vessels[i];
                    if (vessel == null) continue;
                    var crew = vessel.loaded ? vessel.GetVesselCrew() : vessel.protoVessel != null ? vessel.protoVessel.GetVesselCrew() : null;
                    if (crew == null) continue;
                    for (var c = 0; c < crew.Count; c++)
                    {
                        if (crew[c] == null || crew[c].name != avatar) continue;
                        avatarVessel = vessel;
                        return new PresenceMsg
                        {
                            ClientId = Net.ClientId,
                            State = vessel.isEVA ? PresenceState.OnEva : PresenceState.InFlight,
                            VesselId = vessel.id,
                            VesselName = vessel.GetDisplayName(),
                            Scene = scene,
                        };
                    }
                }
            }
            if (!string.IsNullOrEmpty(avatar) && HighLogic.CurrentGame != null && HighLogic.CurrentGame.flightState != null)
            {
                foreach (var proto in HighLogic.CurrentGame.flightState.protoVessels)
                {
                    if (proto == null) continue;
                    var crew = proto.GetVesselCrew();
                    if (crew == null) continue;
                    for (var c = 0; c < crew.Count; c++)
                    {
                        if (crew[c] == null || crew[c].name != avatar) continue;
                        avatarVessel = proto.vesselRef;
                        var protoName = KSP.Localization.Localizer.Format(proto.vesselName);
                        if (avatarVessel == null) MaybeEnterFlightFromProto(proto, protoName);
                        return new PresenceMsg
                        {
                            ClientId = Net.ClientId,
                            State = proto.vesselType == VesselType.EVA ? PresenceState.OnEva : PresenceState.InFlight,
                            VesselId = proto.vesselID,
                            VesselName = protoName,
                            Scene = scene,
                        };
                    }
                }
            }
            return new PresenceMsg
            {
                ClientId = Net.ClientId,
                State = HighLogic.LoadedScene == GameScenes.EDITOR ? PresenceState.Editor : PresenceState.MissionControl,
                VesselId = Guid.Empty,
                VesselName = string.Empty,
                Scene = scene,
            };
        }

        private void OnPresence(NetDataReader body)
        {
            var msg = Envelope.Read<PresenceMsg>(body);
            if (msg.ClientId == Net.ClientId) return;
            _others[msg.ClientId] = msg;
        }
    }
}
