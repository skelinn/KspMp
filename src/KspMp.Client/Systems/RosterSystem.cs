using System;
using System.Collections.Generic;
using KspMp.Roster;
using KspMp.Shared.Protocol;
using KspMp.Vessels;
using LiteNetLib.Utils;
using UnityEngine;

namespace KspMp.Systems
{
    /// <summary>
    /// The shared KerbalRoster and each player's avatar. Every kerbal is replicated; an avatar can only be changed
    /// by its owner. Vessel snapshots decide where a kerbal sits; roster messages decide everything else.
    /// </summary>
    public sealed class RosterSystem : SystemBase
    {
        private readonly Dictionary<string, RemoteKerbal> _kerbals = new Dictionary<string, RemoteKerbal>(StringComparer.Ordinal);
        private bool _bootstrapPending;
        private bool _avatarUploadPending;

        /// <summary>
        /// How long a dead or missing Kerbal stays that way. Stock KSP brings a missing Kerbal back after
        /// hours of game time, which in a shared timeline nobody can warp through alone; a player whose avatar
        /// died would be locked out of launching until everyone agreed to warp. So whoever reported the death
        /// brings the Kerbal back to the astronaut complex shortly after, and reports that too.
        /// </summary>
        public const float ReviveSeconds = 5f;
        private readonly Dictionary<string, float> _reviveAt = new Dictionary<string, float>(StringComparer.Ordinal);

        /// <summary>
        /// Kerbals whose status changes are not ours to report for a while: the crew of a vessel somebody
        /// else simulates that we are about to blow up locally because the server removed it. Their deaths
        /// happen on the owner's machine and are reported from there; ours would race those reports, and the
        /// registry entry that ShouldReport relies on is already gone by then.
        /// </summary>
        private readonly Dictionary<string, float> _quietUntil = new Dictionary<string, float>(StringComparer.Ordinal);

        /// <summary>
        /// Statuses that arrived (Control channel) before the kerbal they belong to (Bulk channel): the two are
        /// ordered independently, and a hire-and-launch in the same second used to lose the Assigned.
        /// </summary>
        private readonly Dictionary<string, KerbalStatusMsg> _statusBeforeKerbal = new Dictionary<string, KerbalStatusMsg>(StringComparer.Ordinal);

        public void QuietCrewOf(Vessel vessel, float seconds)
        {
            if (vessel == null) return;
            var crew = vessel.loaded ? vessel.GetVesselCrew() : vessel.protoVessel != null ? vessel.protoVessel.GetVesselCrew() : null;
            if (crew == null) return;
            var until = Time.realtimeSinceStartup + seconds;
            for (var i = 0; i < crew.Count; i++)
                if (crew[i] != null && !IsMyAvatar(crew[i].name)) _quietUntil[crew[i].name] = until;
        }

        public RosterSystem(KspMpAddon addon) : base(addon) { }

        public override string Name => "Roster";
        public IEnumerable<RemoteKerbal> All => _kerbals.Values;
        public int Count => _kerbals.Count;
        /// <summary>True while we write server data into the local roster, so our own event handlers stay quiet.</summary>
        public bool Applying { get; private set; }
        public bool Synced { get; private set; }
        public int ServerKerbalCount { get; private set; }

        // ---- avatar ----
        public bool NeedsAvatar { get; private set; }
        public string AvatarName { get; private set; } = "";
        public string AvatarTrait { get; private set; } = "Pilot";
        public string ClaimError { get; private set; } = "";
        public bool ClaimPending { get; private set; }
        public bool HasAvatar => !string.IsNullOrEmpty(AvatarName);

        public event Action SyncCompleted;
        public event Action AvatarChanged;

        public bool TryGet(string name, out RemoteKerbal kerbal) => _kerbals.TryGetValue(name, out kerbal);
        public bool IsOtherPlayersAvatar(string name) => _kerbals.TryGetValue(name, out var k) && k.IsAvatar && k.AvatarPlayerId != Addon.Settings.PlayerId;
        public bool IsMyAvatar(string name) => !string.IsNullOrEmpty(name) && name == AvatarName;

        public string AvatarOwnerName(string kerbalName)
        {
            if (!_kerbals.TryGetValue(kerbalName, out var k) || !k.IsAvatar) return null;
            return Addon.Players.TryGet(k.AvatarClientId, out var p) ? p.Name : "another player";
        }

        protected override void OnActivate()
        {
            _activatedAt = Time.realtimeSinceStartup;
            _askedAgain = false;
            Net.RegisterHandler(MessageId.KerbalProto, OnKerbalProto);
            Net.RegisterHandler(MessageId.KerbalStatus, OnKerbalStatus);
            Net.RegisterHandler(MessageId.KerbalRemoved, OnKerbalRemoved);
            Net.RegisterHandler(MessageId.AvatarClaimResult, OnAvatarClaimResult);
            Net.RegisterHandler(MessageId.SyncComplete, OnSyncComplete);
            GameEvents.onKerbalAdded.Add(OnKerbalAdded);
            GameEvents.onKerbalRemoved.Add(OnLocalKerbalRemoved);
            GameEvents.onKerbalStatusChanged.Add(OnKerbalStatusChanged);
            GameEvents.onKerbalLevelUp.Add(OnKerbalChanged);
            GameEvents.onKerbalNameChanged.Add(OnKerbalRenamed);
            GameEvents.onCrewTransferSelected.Add(OnCrewTransferSelected);
            GameEvents.onLevelWasLoadedGUIReady.Add(OnLevelLoaded);
            var welcome = Net.Welcome;
            NeedsAvatar = welcome.NeedsAvatar;
            AvatarName = welcome.AvatarKerbalName ?? "";
            Synced = false;
            ClaimError = "";
        }

        protected override void OnDeactivate()
        {
            Net.UnregisterHandler(MessageId.KerbalProto, OnKerbalProto);
            Net.UnregisterHandler(MessageId.KerbalStatus, OnKerbalStatus);
            Net.UnregisterHandler(MessageId.KerbalRemoved, OnKerbalRemoved);
            Net.UnregisterHandler(MessageId.AvatarClaimResult, OnAvatarClaimResult);
            Net.UnregisterHandler(MessageId.SyncComplete, OnSyncComplete);
            GameEvents.onKerbalAdded.Remove(OnKerbalAdded);
            GameEvents.onKerbalRemoved.Remove(OnLocalKerbalRemoved);
            GameEvents.onKerbalStatusChanged.Remove(OnKerbalStatusChanged);
            GameEvents.onKerbalLevelUp.Remove(OnKerbalChanged);
            GameEvents.onKerbalNameChanged.Remove(OnKerbalRenamed);
            GameEvents.onCrewTransferSelected.Remove(OnCrewTransferSelected);
            GameEvents.onLevelWasLoadedGUIReady.Remove(OnLevelLoaded);
            _kerbals.Clear();
            _reviveAt.Clear();
            _quietUntil.Clear();
            _statusBeforeKerbal.Clear();
            Synced = false;
        }

        // ---- avatar claim ----

        public void Claim(string name, string trait)
        {
            name = (name ?? string.Empty).Trim();
            if (name.Length == 0 || !Net.IsConnected) return;
            ClaimPending = true;
            ClaimError = "";
            AvatarTrait = trait;
            Net.Send(MessageId.AvatarClaim, new AvatarClaimMsg { KerbalName = name, Trait = trait }, Channel.Control, Delivery.ReliableOrdered);
        }

        private void OnAvatarClaimResult(NetDataReader body)
        {
            var result = Envelope.Read<AvatarClaimResultMsg>(body);
            ClaimPending = false;
            if (!result.Ok)
            {
                ClaimError = result.Reason;
                Log.Warn("Avatar claim refused: " + result.Reason);
                return;
            }
            AvatarName = result.KerbalName;
            AvatarTrait = result.Trait;
            NeedsAvatar = false;
            Addon.Settings.AvatarKerbalName = AvatarName;
            Addon.Settings.Save();
            Log.Info("Avatar claimed: " + AvatarName + " (" + AvatarTrait + ")");
            Addon.Chat.AddLocal("You are now " + AvatarName + ", " + AvatarTrait + ".");
            if (HighLogic.LoadedSceneIsGame && HighLogic.CurrentGame != null) EnsureAvatar(HighLogic.CurrentGame, true);
            AvatarChanged?.Invoke();
        }

        /// <summary>Makes sure our avatar exists in the given game's roster; uploads it when created here.</summary>
        public void EnsureAvatar(global::Game game, bool uploadNow)
        {
            if (!HasAvatar || game == null || game.CrewRoster == null) return;
            if (game.CrewRoster.Exists(AvatarName))
            {
                var existing = game.CrewRoster[AvatarName];
                if (existing.type != ProtoCrewMember.KerbalType.Crew) SetApplying(() => existing.type = ProtoCrewMember.KerbalType.Crew);
                return;
            }
            ProtoCrewMember kerbal = null;
            SetApplying(() =>
            {
                kerbal = new ProtoCrewMember(ProtoCrewMember.KerbalType.Crew, AvatarName);
                KerbalRoster.SetExperienceTrait(kerbal, AvatarTrait);
                kerbal.rosterStatus = ProtoCrewMember.RosterStatus.Available;
                game.CrewRoster.AddCrewMember(kerbal);
            });
            Log.Info("Created avatar kerbal " + AvatarName + " (" + AvatarTrait + ")");
            if (uploadNow) Upload(kerbal, KerbalReason.Avatar);
            else _avatarUploadPending = true;
        }

        // ---- receiving ----

        private void OnSyncComplete(NetDataReader body)
        {
            var msg = Envelope.Read<SyncCompleteMsg>(body);
            Synced = true;
            Addon.SyncedOnce = true;
            ServerKerbalCount = msg.Kerbals;
            _bootstrapPending = msg.Kerbals == 0;
            Log.Info("Sync complete: " + msg.Kerbals + " kerbal(s), " + msg.Vessels + " vessel(s)" + (_bootstrapPending ? "; this universe has no roster yet, ours will seed it" : ""));
            SyncCompleted?.Invoke();
        }

        private void OnKerbalProto(NetDataReader body)
        {
            var msg = Envelope.Read<KerbalProtoMsg>(body);
            if (string.IsNullOrEmpty(msg.Name)) return;
            if (!_kerbals.TryGetValue(msg.Name, out var kerbal)) _kerbals[msg.Name] = kerbal = new RemoteKerbal { Name = msg.Name };
            kerbal.NodeText = msg.NodeText;
            kerbal.IsAvatar = msg.IsAvatar;
            kerbal.AvatarPlayerId = msg.AvatarPlayerId;
            kerbal.AvatarClientId = msg.AvatarClientId;
            kerbal.Dirty = true;
            if (_statusBeforeKerbal.TryGetValue(msg.Name, out var early))
            {
                _statusBeforeKerbal.Remove(msg.Name);
                kerbal.Status = early.Status;
                kerbal.InactiveTimeEnd = early.InactiveTimeEnd;
            }
            if (msg.Reason != KerbalReason.Sync && msg.Reason != KerbalReason.Bootstrap)
                Log.Info("Kerbal " + msg.Name + " updated (" + msg.Reason + (msg.IsAvatar ? ", avatar of #" + msg.AvatarClientId : "") + ")");
            if (msg.IsAvatar && msg.AvatarPlayerId == Addon.Settings.PlayerId && AvatarName != msg.Name)
            {
                AvatarName = msg.Name;
                NeedsAvatar = false;
                AvatarChanged?.Invoke();
            }
            TryApply(kerbal);
        }

        private void OnKerbalStatus(NetDataReader body)
        {
            var msg = Envelope.Read<KerbalStatusMsg>(body);
            if (!_kerbals.TryGetValue(msg.Name, out var kerbal))
            {
                _statusBeforeKerbal[msg.Name] = msg;
                return;
            }
            ApplyStatus(kerbal, msg);
        }

        private void ApplyStatus(RemoteKerbal kerbal, KerbalStatusMsg msg)
        {
            kerbal.Status = msg.Status;
            kerbal.InactiveTimeEnd = msg.InactiveTimeEnd;
            var roster = HighLogic.CurrentGame != null ? HighLogic.CurrentGame.CrewRoster : null;
            if (roster == null || !roster.Exists(msg.Name)) return;
            var pcm = roster[msg.Name];
            SetApplying(() =>
            {
                pcm.inactiveTimeEnd = msg.InactiveTimeEnd;
                var status = (ProtoCrewMember.RosterStatus)msg.Status;
                if (pcm.rosterStatus != status) pcm.rosterStatus = status;
                if (status == ProtoCrewMember.RosterStatus.Missing && !pcm.inactive) pcm.inactive = true;
                if (status == ProtoCrewMember.RosterStatus.Available && pcm.inactive) pcm.inactive = false;
            });
            Log.Info("Kerbal " + msg.Name + " is now " + (ProtoCrewMember.RosterStatus)msg.Status);
        }

        private void OnKerbalRemoved(NetDataReader body)
        {
            var msg = Envelope.Read<KerbalRemovedMsg>(body);
            _kerbals.Remove(msg.Name);
            var roster = HighLogic.CurrentGame != null ? HighLogic.CurrentGame.CrewRoster : null;
            if (roster == null || !roster.Exists(msg.Name)) return;
            SetApplying(() => roster.Remove(msg.Name));
            Log.Info("Kerbal " + msg.Name + " removed");
        }

        private void TryApply(RemoteKerbal kerbal)
        {
            if (!kerbal.Dirty || !HighLogic.LoadedSceneIsGame || HighLogic.CurrentGame == null || HighLogic.CurrentGame.CrewRoster == null) return;
            ApplyTo(HighLogic.CurrentGame, kerbal);
        }

        private void ApplyTo(global::Game game, RemoteKerbal kerbal)
        {
            try
            {
                var parsed = KerbalCodec.Parse(kerbal.NodeText, game.Mode);
                if (parsed == null) return;
                SetApplying(() =>
                {
                    if (game.CrewRoster.Exists(kerbal.Name)) KerbalCodec.CopyInto(game.CrewRoster[kerbal.Name], parsed);
                    else game.CrewRoster.AddCrewMember(parsed);
                });
                kerbal.Dirty = false;
            }
            catch (Exception e)
            {
                Log.Exception("Applying kerbal " + kerbal.Name, e);
                kerbal.Dirty = false;
            }
        }

        /// <summary>Before entering the game: make the new save's roster match the server's.</summary>
        public void SeedGame(global::Game game)
        {
            if (game == null || game.CrewRoster == null) return;
            var applied = 0;
            foreach (var kerbal in _kerbals.Values)
            {
                ApplyTo(game, kerbal);
                applied++;
            }
            if (_kerbals.Count > 0)
            {
                // Drop the random applicants this fresh game rolled; the server's roster is the roster.
                var extra = new List<ProtoCrewMember>();
                foreach (var pcm in game.CrewRoster.Applicants)
                    if (!_kerbals.ContainsKey(pcm.name)) extra.Add(pcm);
                SetApplying(() => { foreach (var pcm in extra) game.CrewRoster.Remove(pcm); });
                Log.Info("Roster seeded from the server: " + applied + " kerbal(s), " + extra.Count + " local applicant(s) dropped");
            }
            EnsureAvatar(game, false);
        }

        // ---- sending ----

        public void Upload(ProtoCrewMember kerbal, KerbalReason reason)
        {
            if (kerbal == null || !Net.IsConnected) return;
            if (IsOtherPlayersAvatar(kerbal.name)) return;
            try
            {
                var text = KerbalCodec.ToText(kerbal);
                Net.Send(MessageId.KerbalProto, new KerbalProtoMsg { Name = kerbal.name, Reason = reason, NodeText = text }, Channel.Bulk, Delivery.ReliableOrdered);
                if (!_kerbals.TryGetValue(kerbal.name, out var remote)) _kerbals[kerbal.name] = remote = new RemoteKerbal { Name = kerbal.name };
                remote.NodeText = text;
                remote.Status = (byte)kerbal.rosterStatus;
                if (reason == KerbalReason.Avatar)
                {
                    remote.IsAvatar = true;
                    remote.AvatarPlayerId = Addon.Settings.PlayerId;
                    remote.AvatarClientId = Net.ClientId;
                }
                if (reason != KerbalReason.Bootstrap) Log.Info("Uploaded kerbal " + kerbal.name + " (" + reason + ")");
            }
            catch (Exception e)
            {
                Log.Exception("Uploading kerbal " + kerbal.name, e);
            }
        }

        /// <summary>First client on a fresh universe: our whole roster becomes the shared one.</summary>
        private void UploadAll()
        {
            var roster = HighLogic.CurrentGame != null ? HighLogic.CurrentGame.CrewRoster : null;
            if (roster == null) return;
            var count = 0;
            foreach (var pcm in roster.Crew) { Upload(pcm, KerbalReason.Bootstrap); count++; }
            foreach (var pcm in roster.Applicants) { Upload(pcm, KerbalReason.Bootstrap); count++; }
            Log.Info("Uploaded the initial roster: " + count + " kerbal(s)");
        }

        private void OnLevelLoaded(GameScenes scene)
        {
            if (!HighLogic.LoadedSceneIsGame || HighLogic.CurrentGame == null) return;
            foreach (var kerbal in _kerbals.Values) if (kerbal.Dirty) ApplyTo(HighLogic.CurrentGame, kerbal);
            if (_bootstrapPending)
            {
                _bootstrapPending = false;
                UploadAll();
            }
            if (_avatarUploadPending && HasAvatar && HighLogic.CurrentGame.CrewRoster.Exists(AvatarName))
            {
                _avatarUploadPending = false;
                Upload(HighLogic.CurrentGame.CrewRoster[AvatarName], KerbalReason.Avatar);
            }
        }

        public override void Update()
        {
            if (_reviveAt.Count > 0) ReviveDue();
            // The Welcome comes on one channel and the sync on another; if the sync beat the Welcome its
            // messages were dropped before any handler existed. Ask again rather than wait forever.
            if (!Synced && !_askedAgain && Net.IsConnected && Time.realtimeSinceStartup - _activatedAt > 8f)
            {
                _askedAgain = true;
                Log.Info("No sync after 8 s; asking the server for the world again");
                Net.Send(MessageId.SyncRequest, new SyncRequestMsg(), Channel.Control, Delivery.ReliableOrdered);
            }
        }

        private float _activatedAt;
        private bool _askedAgain;

        private readonly List<string> _due = new List<string>();

        private void ReviveDue()
        {
            var now = Time.realtimeSinceStartup;
            _due.Clear();
            foreach (var pair in _reviveAt) if (now >= pair.Value) _due.Add(pair.Key);
            for (var i = 0; i < _due.Count; i++)
            {
                _reviveAt.Remove(_due[i]);
                Revive(_due[i], "did not survive");
            }
        }

        /// <summary>Puts a Kerbal back in the astronaut complex, and says so; the status change is reported like any other.</summary>
        private void Revive(string name, string what)
        {
            var roster = HighLogic.CurrentGame != null ? HighLogic.CurrentGame.CrewRoster : null;
            if (roster == null || !roster.Exists(name)) return;
            var pcm = roster[name];
            // Even when the local status is already Available (the server freed the crew of the removed
            // vessel before our death report reached it): the Available we send is what settles the race for
            // everyone else, who may have applied our Dead after the server's Available.
            try
            {
                // Set quietly and report by hand: the ordinary report is suppressed while the Kerbal still
                // counts as aboard a vessel somebody else simulates, and a vessel takes a few frames to die.
                SetApplying(() =>
                {
                    pcm.rosterStatus = ProtoCrewMember.RosterStatus.Available;
                    if (pcm.type != ProtoCrewMember.KerbalType.Crew && pcm.type != ProtoCrewMember.KerbalType.Tourist) pcm.type = ProtoCrewMember.KerbalType.Crew;
                });
                if (Net.IsConnected)
                    Net.Send(MessageId.KerbalStatus, new KerbalStatusMsg { Name = name, Status = (byte)ProtoCrewMember.RosterStatus.Available, InactiveTimeEnd = pcm.inactiveTimeEnd }, Channel.Control, Delivery.ReliableOrdered);
                if (_kerbals.TryGetValue(name, out var remote)) remote.Status = (byte)ProtoCrewMember.RosterStatus.Available;
                Log.Info(name + " " + what + " and is back at the astronaut complex");
                if (IsMyAvatar(name))
                {
                    Addon.Notices.Post("revive", "Your Kerbal " + name + " is back at the astronaut complex", Ui.Theme.Accent, ttlSeconds: 10f);
                }
            }
            catch (Exception e)
            {
                Log.Exception("Reviving " + name, e);
            }
        }

        /// <summary>
        /// A vessel somebody else simulates is gone (destroyed, recovered, reverted away). Its crew changes are
        /// theirs to report, except for our own avatar, which nobody else reports and which would otherwise stay
        /// assigned to a vessel that no longer exists, so we could never launch again.
        /// </summary>
        public bool AvatarAboard(Vessel vessel)
        {
            if (!HasAvatar || vessel == null) return false;
            var crew = vessel.loaded ? vessel.GetVesselCrew() : vessel.protoVessel != null ? vessel.protoVessel.GetVesselCrew() : null;
            if (crew == null) return false;
            for (var i = 0; i < crew.Count; i++)
                if (crew[i] != null && IsMyAvatar(crew[i].name)) return true;
            return false;
        }

        public void ReturnAvatar(string why)
        {
            if (!HasAvatar) return;
            // Shortly, not now: the status change is only reported once the vessel is out of FlightGlobals, and
            // KSP tears a dying vessel down over a few frames.
            Log.Info("Our Kerbal " + AvatarName + " was aboard a vessel that is gone (" + why + "); returning them to the astronaut complex");
            _reviveAt[AvatarName] = Time.realtimeSinceStartup + 2f;
        }

        // ---- local game events ----

        private bool ShouldReport(ProtoCrewMember pcm)
        {
            if (Applying || pcm == null || !HighLogic.LoadedSceneIsGame || VesselLoader.IsLoadingRemote || !Net.IsConnected) return false;
            if (IsOtherPlayersAvatar(pcm.name)) return false;
            if (_quietUntil.TryGetValue(pcm.name, out var quietUntil))
            {
                if (Time.realtimeSinceStartup < quietUntil) return false;
                _quietUntil.Remove(pcm.name);
            }
            // Changes to crew aboard a vessel somebody else simulates are theirs to report.
            if (FlightGlobals.fetch != null)
            {
                var vessels = FlightGlobals.Vessels;
                for (var i = 0; i < vessels.Count; i++)
                {
                    var vessel = vessels[i];
                    if (vessel == null || !Addon.Vessels.IsOwnedByOther(vessel.id)) continue;
                    var crew = vessel.loaded ? vessel.GetVesselCrew() : vessel.protoVessel != null ? vessel.protoVessel.GetVesselCrew() : null;
                    if (crew == null) continue;
                    for (var c = 0; c < crew.Count; c++)
                        if (crew[c] != null && crew[c].name == pcm.name) return false;
                }
            }
            return true;
        }

        private void OnKerbalAdded(ProtoCrewMember pcm)
        {
            if (!ShouldReport(pcm)) return;
            Upload(pcm, KerbalReason.Created);
        }

        private void OnLocalKerbalRemoved(ProtoCrewMember pcm)
        {
            if (!ShouldReport(pcm)) return;
            _kerbals.Remove(pcm.name);
            Net.Send(MessageId.KerbalRemoved, new KerbalRemovedMsg { Name = pcm.name }, Channel.Control, Delivery.ReliableOrdered);
        }

        private void OnKerbalStatusChanged(ProtoCrewMember pcm, ProtoCrewMember.RosterStatus from, ProtoCrewMember.RosterStatus to)
        {
            if (from == to || !ShouldReport(pcm)) return;
            Net.Send(MessageId.KerbalStatus, new KerbalStatusMsg { Name = pcm.name, Status = (byte)to, InactiveTimeEnd = pcm.inactiveTimeEnd }, Channel.Control, Delivery.ReliableOrdered);
            if (_kerbals.TryGetValue(pcm.name, out var remote)) remote.Status = (byte)to;
            if (to == ProtoCrewMember.RosterStatus.Dead || to == ProtoCrewMember.RosterStatus.Missing)
            {
                _reviveAt[pcm.name] = Time.realtimeSinceStartup + ReviveSeconds;
                if (IsMyAvatar(pcm.name))
                    Addon.Chat.AddLocal("Your Kerbal " + pcm.name + " is " + to.ToString().ToLowerInvariant() + ". They will be back at the astronaut complex in a moment.");
            }
            else _reviveAt.Remove(pcm.name);
        }

        private void OnKerbalChanged(ProtoCrewMember pcm)
        {
            if (!ShouldReport(pcm)) return;
            Upload(pcm, KerbalReason.Changed);
        }

        private void OnKerbalRenamed(ProtoCrewMember pcm, string oldName, string newName)
        {
            if (!ShouldReport(pcm)) return;
            if (!string.IsNullOrEmpty(oldName) && oldName != newName)
            {
                _kerbals.Remove(oldName);
                Net.Send(MessageId.KerbalRemoved, new KerbalRemovedMsg { Name = oldName }, Channel.Control, Delivery.ReliableOrdered);
            }
            Upload(pcm, KerbalReason.Changed);
        }

        private void OnCrewTransferSelected(CrewTransfer.CrewTransferData data)
        {
            if (data == null || data.crewMember == null || !IsOtherPlayersAvatar(data.crewMember.name)) return;
            data.canTransfer = false;
            ScreenMessages.PostScreenMessage(data.crewMember.name + " belongs to " + AvatarOwnerName(data.crewMember.name), 3f, ScreenMessageStyle.UPPER_CENTER);
        }

        private void SetApplying(Action action)
        {
            var was = Applying;
            Applying = true;
            try { action(); }
            finally { Applying = was; }
        }
    }
}
