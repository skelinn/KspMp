using System;
using System.Collections.Generic;
using KspMp.Shared.Protocol;
using KspMp.Vessels;
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
        /// <summary>Invites the player turned down; not offered again while their Kerbal stays aboard.</summary>
        private readonly HashSet<Guid> _declined = new HashSet<Guid>();
        private float _sceneEnteredAt;

        public PresenceSystem(KspMpAddon addon) : base(addon) { }

        public override string Name => "Presence";
        public PresenceMsg Mine => _mine;
        public IReadOnlyDictionary<int, PresenceMsg> Others => _others;

        /// <summary>Anyone else in the flight scene (aboard anything, or on EVA): they have loaded copies of our vessels to mirror on.</summary>
        public bool OthersInFlight()
        {
            foreach (var p in _others.Values)
                if (p.State == PresenceState.InFlight || p.State == PresenceState.OnEva || p.State == PresenceState.Spectating) return true;
            return false;
        }

        protected override void OnActivate()
        {
            Net.RegisterHandler(MessageId.Presence, OnPresence);
            GameEvents.onLevelWasLoadedGUIReady.Add(OnLevelLoaded);
            GameEvents.onGameSceneLoadRequested.Add(OnSceneLoadRequested);
            _reported = false;
            _nextCheckAt = 0f;
            _sceneEnteredAt = Time.realtimeSinceStartup;
        }

        protected override void OnDeactivate()
        {
            Net.UnregisterHandler(MessageId.Presence, OnPresence);
            GameEvents.onLevelWasLoadedGUIReady.Remove(OnLevelLoaded);
            GameEvents.onGameSceneLoadRequested.Remove(OnSceneLoadRequested);
            _others.Clear();
            _reported = false;
            if (Invite != null && Addon.Notices != null) Addon.Notices.Dismiss(InviteKey);
            Invite = null;
            _lastEnteredFor = Guid.Empty;
            _declined.Clear();
        }

        /// <summary>
        /// Leaving flight is announced at once. The last report would otherwise stand through the loading
        /// screen, and "in flight on that vessel" is what the server hands authority to - to a player who is
        /// already on their way to the space centre.
        /// </summary>
        private void OnSceneLoadRequested(GameScenes scene)
        {
            if (!HighLogic.LoadedSceneIsFlight || !Net.IsConnected || scene == GameScenes.FLIGHT) return;
            var presence = new PresenceMsg
            {
                State = scene == GameScenes.EDITOR ? PresenceState.Editor : PresenceState.MissionControl,
                VesselId = Guid.Empty,
                VesselName = string.Empty,
                Scene = (byte)scene,
            };
            _mine = presence;
            _reported = true;
            Net.Send(MessageId.Presence, presence, Channel.Control, Delivery.ReliableOrdered);
            Log.Info("Presence: leaving flight for " + scene);
        }

        private void OnLevelLoaded(GameScenes scene)
        {
            _sceneEnteredAt = Time.realtimeSinceStartup;
            _lastSnappedTo = Guid.Empty;
            // The wording and the countdown depend on the scene, so a kept invite is re-raised after the move.
            if (Invite != null) RaiseInviteNotice();
        }

        // ---- being invited into somebody else's flight ----

        /// <summary>An offer to join a flight our avatar is aboard: raised by a launch notice, or by noticing our
        /// kerbal on a vessel we are not flying. It survives scene changes, because accepting one usually means
        /// walking out of the VAB first.</summary>
        public sealed class FlightInvite
        {
            public Guid VesselId;
            public string VesselName;
            public string LauncherName;
            /// <summary>When the automatic join fires, or -1 while it is not counting down.</summary>
            public float AutoAt = -1f;
        }

        public FlightInvite Invite { get; private set; }

        /// <summary>The launch notice arrived before the vessel exists here, so the invite is raised from it.</summary>
        public void OnLaunchNotice(EditorLaunchMsg msg, Guid vesselId)
        {
            var avatar = Addon.Roster.AvatarName;
            if (string.IsNullOrEmpty(avatar) || msg.AboardKerbals == null) return;
            var aboard = false;
            for (var i = 0; i < msg.AboardKerbals.Length; i++)
                if (string.Equals(msg.AboardKerbals[i], avatar, StringComparison.Ordinal)) { aboard = true; break; }
            if (!aboard) return;
            Offer(vesselId, msg.ShipName, NameOf(msg.FromClientId));
        }

        /// <summary>
        /// Raises (or completes) an invite. The launch notice arrives before the craft does - it is sent from a
        /// prefix on launchVessel, when the vessel does not exist even on the launcher's machine - so an invite
        /// starts without a vessel id and is completed by the first snapshot that puts our kerbal aboard one.
        /// </summary>
        private void Offer(Guid vesselId, string vesselName, string launcherName)
        {
            if (vesselId != Guid.Empty && (_lastEnteredFor == vesselId || _declined.Contains(vesselId))) return;
            if (Invite != null)
            {
                if (Invite.VesselId == vesselId) return;
                if (Invite.VesselId != Guid.Empty || vesselId == Guid.Empty) return;
                Invite.VesselId = vesselId;                              // the craft we were promised has arrived
                if (!string.IsNullOrEmpty(vesselName)) Invite.VesselName = vesselName;
                RaiseInviteNotice();
                return;
            }
            Invite = new FlightInvite { VesselId = vesselId, VesselName = vesselName, LauncherName = launcherName };
            RaiseInviteNotice();
        }

        public void DismissInvite()
        {
            // "Not now" means not now: without remembering it, the next one-second check found our Kerbal still
            // aboard and raised the same invite again, countdown and all.
            if (Invite != null && Invite.VesselId != Guid.Empty) _declined.Add(Invite.VesselId);
            Invite = null;
            Addon.Notices.Dismiss(InviteKey);
        }

        private const string InviteKey = "flight-invite";

        /// <summary>
        /// The notice for the current invite, reworded for where the player is standing. Only somebody idle at the
        /// space centre gets a countdown - yanking a player out of the VAB mid-build would be worse than the
        /// problem it solves - so in an editor the button says what it will cost instead.
        /// </summary>
        private void RaiseInviteNotice()
        {
            var invite = Invite;
            if (invite == null) return;
            var scene = HighLogic.LoadedScene;
            var idleAtBase = scene == GameScenes.SPACECENTER || scene == GameScenes.TRACKSTATION;
            var inEditor = scene == GameScenes.EDITOR;
            var who = string.IsNullOrEmpty(invite.LauncherName) ? "Someone" : invite.LauncherName;
            var name = string.IsNullOrEmpty(invite.VesselName) ? "a craft" : invite.VesselName;
            var ready = invite.VesselId != Guid.Empty;
            var text = who + " launched " + name + " with your Kerbal aboard";

            NoticeSystem.Action[] actions;
            if (inEditor)
            {
                actions = new[]
                {
                    new NoticeSystem.Action { Label = "Leave the VAB and join", Primary = true, OnClick = () => HighLogic.LoadScene(GameScenes.SPACECENTER) },
                    new NoticeSystem.Action { Label = "Stay here", OnClick = DismissInvite },
                };
            }
            else
            {
                actions = new[]
                {
                    new NoticeSystem.Action { Label = ready ? "Enter flight" : "waiting for the launch...", Primary = ready, OnClick = ready ? (System.Action)(() => JoinFlight(invite.VesselId)) : null },
                    new NoticeSystem.Action { Label = "Not now", OnClick = DismissInvite },
                };
            }

            var notice = Addon.Notices.Post(InviteKey, text, Ui.Theme.Accent, actions, ttlSeconds: 0f);
            // Only a player standing idle at the space centre gets pulled in automatically; anywhere else the
            // countdown would interrupt something they were doing on purpose.
            if (ready && idleAtBase && Time.realtimeSinceStartup - _sceneEnteredAt >= 3f)
            {
                // Re-raising the notice (the craft arriving, a scene settling) must not restart a countdown
                // that is already running, or a player idle at the space centre would never actually get pulled in.
                if (invite.AutoAt < 0f) invite.AutoAt = Time.realtimeSinceStartup + AutoJoinSeconds;
                notice.CountdownUntil = invite.AutoAt;
                notice.CountdownText = "joining";
                // A join that cannot happen yet (the craft has not arrived here) re-arms a fresh countdown
                // instead of firing a deadline already in the past on every frame.
                notice.OnCountdown = () => { if (!JoinFlight(invite.VesselId)) invite.AutoAt = -1f; };
            }
            else
            {
                invite.AutoAt = -1f;
            }
        }

        public const float AutoJoinSeconds = 10f;

        /// <summary>
        /// Loads the flight scene focused on a vessel.
        ///
        /// It cannot simply look the vessel up in <c>flightState.protoVessels</c>: that list is written by the
        /// save file, and vessels that arrived over the network are never added to it - which is the second
        /// reason the old join path never fired. <see cref="Game.Updated"/> rebuilds the flight state from every
        /// live vessel instead, which is the same thing KSP does when you fly a craft from the tracking station.
        /// </summary>
        public bool JoinFlight(Guid vesselId)
        {
            if (vesselId == Guid.Empty) return false;
            if (!VesselLoader.GameReady || FlightGlobals.FindVessel(vesselId) == null)
            {
                Log.Info("Cannot join the flight yet: vessel " + vesselId.ToString().Substring(0, 8) + " has not arrived here");
                return false;
            }
            try
            {
                GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);
                var game = HighLogic.CurrentGame.Updated();
                var index = game.flightState.protoVessels.FindIndex(pv => pv != null && pv.vesselID == vesselId);
                if (index < 0)
                {
                    Log.Warn("Cannot join the flight: vessel " + vesselId.ToString().Substring(0, 8) + " is not in the refreshed flight state");
                    return false;
                }
                Log.Info("Entering flight on vessel " + vesselId.ToString().Substring(0, 8) + " (index " + index + " of " + game.flightState.protoVessels.Count + ")");
                FlightDriver.StartAndFocusVessel(game, index);
                // Only after the start took: a throw above must leave the invite standing for another try.
                _lastEnteredFor = vesselId;
                Invite = null;
                Addon.Notices.Dismiss(InviteKey);
                return true;
            }
            catch (Exception e)
            {
                Log.Exception("Joining a flight", e);
                return false;
            }
        }

        private string NameOf(int clientId) => Addon.Players.TryGet(clientId, out var p) ? p.Name : "#" + clientId;

        public string Describe(int clientId)
        {
            var state = clientId == Net.ClientId ? _mine : _others.TryGetValue(clientId, out var p) ? p : default(PresenceMsg);
            // "in the editor" is true but useless; the builders list knows which bench and what is on it.
            if (state.State == PresenceState.Editor && Addon.Builders != null)
            {
                var built = Addon.Builders.Describe(clientId);
                if (!string.IsNullOrEmpty(built)) return built;
            }
            return Describe(state);
        }

        public static string Describe(PresenceMsg p)
        {
            switch (p.State)
            {
                case PresenceState.Spectating: return "watching " + (string.IsNullOrEmpty(p.VesselName) ? "a craft" : p.VesselName);
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

            // Our kerbal is aboard a vessel we are not flying: offer to join it.
            if (avatarVessel != null && !HighLogic.LoadedSceneIsFlight)
                Offer(avatarVessel.id, avatarVessel.GetDisplayName(), "");
            else if (avatarVessel == null && _declined.Count > 0)
                _declined.Clear();   // the Kerbal is home again; a later launch is a new invitation
            if (Invite != null)
            {
                var id = Invite.VesselId;
                if (id != Guid.Empty && (_lastEnteredFor == id || (HighLogic.LoadedSceneIsFlight && FlightGlobals.ActiveVessel != null && FlightGlobals.ActiveVessel.id == id)))
                    DismissInvite();
                else if (!Addon.Notices.Has(InviteKey)) RaiseInviteNotice();
                else if (id != Guid.Empty && Invite.AutoAt < 0 && (HighLogic.LoadedScene == GameScenes.SPACECENTER || HighLogic.LoadedScene == GameScenes.TRACKSTATION)) RaiseInviteNotice();
                if (Invite != null && Invite.VesselId != Guid.Empty && Addon.Launch != null && Addon.Launch.JoinFlightAutomatically) JoinFlight(Invite.VesselId);
            }

            // The camera follows our Kerbal: if they sit in a loaded vessel that is not active, switch to it -
            // unless what we are flying is ours. Taking Jeb out for a walk is flying a vessel we own, and being
            // yanked back into the pod one second later because "our Kerbal is aboard" is not following anyone.
            var flyingOurOwn = HighLogic.LoadedSceneIsFlight && FlightGlobals.ActiveVessel != null && Addon.Vessels.IsMine(FlightGlobals.ActiveVessel.id);
            if (HighLogic.LoadedSceneIsFlight && FlightGlobals.ready && avatarVessel != null && avatarVessel.loaded && FlightGlobals.ActiveVessel != avatarVessel && !flyingOurOwn && _lastSnappedTo != avatarVessel.id)
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
                        if (avatarVessel == null && proto.situation != Vessel.Situations.LANDED && proto.situation != Vessel.Situations.SPLASHED)
                            Offer(proto.vesselID, protoName, "");
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
            // In flight without our kerbal aboard anything: watching whatever the camera is on. This used to
            // read as "mission control", so the pilot's staging and throttle were not sent to us and our copy
            // of their rocket showed no plumes, no noise and no chutes.
            var watching = HighLogic.LoadedSceneIsFlight && FlightGlobals.fetch != null ? FlightGlobals.ActiveVessel : null;
            if (watching != null)
                return new PresenceMsg
                {
                    ClientId = Net.ClientId,
                    State = PresenceState.Spectating,
                    VesselId = watching.id,
                    VesselName = watching.GetDisplayName(),
                    Scene = scene,
                };
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
