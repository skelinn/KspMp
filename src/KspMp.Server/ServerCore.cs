using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using KspMp.Server.Services;
using KspMp.Server.Editor;
using KspMp.Server.Roster;
using KspMp.Server.Universe;
using KspMp.Server.Vessels;
using KspMp.Shared.Protocol;
using LiteNetLib.Utils;

namespace KspMp.Server
{
    /// <summary>
    /// The multiplayer server. It never runs KSP: it validates, stores and relays. Transport-agnostic so the same
    /// class runs in the console host and, later, in-process inside the game ("Host game").
    /// Single-threaded: call <see cref="Poll"/> from one thread, ideally every 10-20 ms.
    /// </summary>
    public sealed class ServerCore : IDisposable
    {
        private sealed class PendingDisconnect
        {
            public PeerId Peer;
            public string Reason;
            public DateTime DueUtc;
        }

        private readonly Action<string> _log;
        private readonly NetDataWriter _writer = new NetDataWriter();
        private readonly Dictionary<PeerId, ClientSession> _clients = new Dictionary<PeerId, ClientSession>();
        private readonly List<PendingDisconnect> _pendingDisconnects = new List<PendingDisconnect>();
        private readonly Dictionary<Guid, KnownPlayer> _knownPlayers;
        private readonly Stopwatch _uptime = Stopwatch.StartNew();
        private int _nextClientId = 1;
        private double _nextTimeSyncAt;
        private double _nextPlayerListAt;
        private double _nextSaveAt;

        /// <summary>Delay between sending a Reject and closing the connection, so the reason reaches the client.</summary>
        public int RejectGraceMs = 500;
        public double TimeSyncIntervalSeconds = 0.5;
        public double PlayerListRefreshSeconds = 5;
        public double SaveIntervalSeconds = 60;

        public ServerCore(INetTransport transport, ServerConfig config, UniverseStore universe, Action<string> log)
        {
            Transport = transport ?? throw new ArgumentNullException(nameof(transport));
            Config = config ?? new ServerConfig();
            Universe = universe ?? new UniverseStore(null);
            _log = log ?? (_ => { });

            var ut = Config.InitialUniversalTime;
            var rate = 1f;
            if (Universe.TryLoadTime(out var savedUt, out var savedRate))
            {
                ut = savedUt;
                rate = savedRate;
            }
            Time = new TimeService(ut, rate);
            _knownPlayers = Universe.LoadPlayers();
            Players = new PlayerRegistry(this);
            Chat = new ChatService(this);
            Vessels = new VesselStore(Universe, _log);
            Authority = new AuthorityService(this);
            Warp = new WarpService(this);
            Roster = new RosterService(this, new RosterStore(Universe, _log));
            Control = new ControlService(this);
            Editor = new EditorSessionService(this);

            Transport.PeerConnected += OnPeerConnected;
            Transport.PeerDisconnected += OnPeerDisconnected;
            Transport.Received += OnReceived;
        }

        public INetTransport Transport { get; }
        public ServerConfig Config { get; }
        public UniverseStore Universe { get; }
        public TimeService Time { get; }
        public PlayerRegistry Players { get; }
        public ChatService Chat { get; }
        public VesselStore Vessels { get; }
        public AuthorityService Authority { get; }
        public WarpService Warp { get; }
        public RosterService Roster { get; }
        public ControlService Control { get; }
        public EditorSessionService Editor { get; }

        public IEnumerable<ClientSession> Clients => _clients.Values;
        public IEnumerable<ClientSession> HandshakenClients => _clients.Values.Where(c => c.IsOnline);
        public int ClientCount => _clients.Count;
        public int OnlineCount => _clients.Values.Count(c => c.IsOnline);
        public bool IsRunning => Transport.IsRunning;
        public IReadOnlyCollection<KnownPlayer> KnownPlayers => _knownPlayers.Values;

        public void Log(string message) => _log(message);

        public void Start()
        {
            Transport.Start();
            _log(Config.ServerName + ": UT " + Time.UniversalTime.ToString("F1") + " at " + Time.Rate + "x, " + _knownPlayers.Count + " known player(s), " + Vessels.Count + " vessel(s), " + Roster.Store.Count + " kerbal(s)"
                 + (Universe.IsPersistent ? ", universe " + Universe.Dir : ", in-memory universe"));
        }

        public void Stop()
        {
            Save();
            Transport.Stop();
            _clients.Clear();
            _pendingDisconnects.Clear();
        }

        public void Dispose() => Stop();

        public void Poll()
        {
            Transport.Poll();
            Time.Advance();

            var now = _uptime.Elapsed.TotalSeconds;
            if (_pendingDisconnects.Count > 0) ProcessPendingDisconnects();
            if (now >= _nextTimeSyncAt)
            {
                _nextTimeSyncAt = now + TimeSyncIntervalSeconds;
                if (OnlineCount > 0) Broadcast(MessageId.TimeSync, Time.Snapshot(0), Channel.State, Delivery.Unreliable);
            }
            if (now >= _nextPlayerListAt)
            {
                _nextPlayerListAt = now + PlayerListRefreshSeconds;
                Players.BroadcastList();
            }
            if (now >= _nextSaveAt)
            {
                _nextSaveAt = now + SaveIntervalSeconds;
                Save();
            }
        }

        public void Save()
        {
            if (!Universe.IsPersistent) return;
            try
            {
                Universe.SaveTime(Time.UniversalTime, Time.Rate);
                Universe.SavePlayers(_knownPlayers.Values);
                Vessels.SaveDirty();
                Roster.Store.SaveDirty();
            }
            catch (Exception e)
            {
                _log("Saving the universe failed: " + e);
            }
        }

        // ---- transport events ----

        private void OnPeerConnected(PeerId peer)
        {
            _clients[peer] = new ClientSession { Peer = peer };
            _log(peer + " connected, awaiting hello");
        }

        private void OnPeerDisconnected(PeerId peer, string reason)
        {
            if (!_clients.TryGetValue(peer, out var client)) return;
            _clients.Remove(peer);
            _log(client.DisplayName + " disconnected (" + reason + ")");
            if (!client.IsOnline) return;
            Touch(client);
            Authority.ReleaseAll(client);
            Warp.OnClientLeft(client);
            Editor.HandleLeave(client, announce: false);
            Players.OnLeft(client, reason);
            Control.OnClientsChanged();
            Broadcast(MessageId.Presence, new PresenceMsg { ClientId = client.ClientId, State = PresenceState.MissionControl, VesselId = Guid.Empty, VesselName = string.Empty, Scene = 0 }, Channel.Control, Delivery.ReliableOrdered);
            Chat.ServerNotice(client.PlayerName + " left");
        }

        private void OnReceived(PeerId from, byte[] buffer, int offset, int length, Channel channel)
        {
            if (!_clients.TryGetValue(from, out var client) || client.Rejected) return;
            var reader = new NetDataReader(buffer, offset, length);
            if (!Envelope.TryReadHeader(reader, out var id, out var flags, out _)) return;
            try
            {
                var body = Envelope.OpenBody(reader, flags);
                Handle(client, id, body);
            }
            catch (Exception e)
            {
                _log("Error handling " + id + " from " + client.DisplayName + ": " + e);
            }
        }

        private void Handle(ClientSession client, MessageId id, NetDataReader body)
        {
            if (id == MessageId.Hello)
            {
                HandleHello(client, Envelope.Read<HelloMsg>(body));
                return;
            }
            if (!client.Handshaken)
            {
                _log("Ignoring " + id + " from " + client.DisplayName + " before hello");
                return;
            }

            switch (id)
            {
                case MessageId.Ping:
                {
                    var ping = Envelope.Read<PingMsg>(body);
                    Send(client.Peer, MessageId.Pong, new PongMsg { ClientTicks = ping.ClientTicks, ServerTicks = DateTime.UtcNow.Ticks }, Channel.State, Delivery.Unreliable);
                    break;
                }
                case MessageId.TimeSyncReq:
                {
                    var req = Envelope.Read<TimeSyncReqMsg>(body);
                    Send(client.Peer, MessageId.TimeSync, Time.Snapshot(req.ClientTicks), Channel.State, Delivery.Unreliable);
                    break;
                }
                case MessageId.Chat:
                    Chat.HandleChat(client, Envelope.Read<ChatMsg>(body));
                    break;
                case MessageId.VesselProto:
                    HandleVesselProto(client, Envelope.Read<VesselProtoMsg>(body));
                    break;
                case MessageId.VesselState:
                {
                    var state = Envelope.Read<VesselStateMsg>(body);
                    if (!Authority.IsOwnedBy(state.VesselId, client.ClientId)) break;
                    Vessels.UpdateState(state);
                    Broadcast(MessageId.VesselState, state, Channel.State, Delivery.Sequenced, client.Peer);
                    break;
                }
                case MessageId.VesselRemove:
                {
                    var remove = Envelope.Read<VesselRemoveMsg>(body);
                    var owner = Authority.OwnerOf(remove.VesselId);
                    if (owner != 0 && owner != client.ClientId)
                    {
                        _log(client.DisplayName + " tried to remove vessel " + remove.VesselId + " owned by #" + owner);
                        break;
                    }
                    if (Vessels.Remove(remove.VesselId)) _log(client.DisplayName + " removed vessel " + remove.VesselId + " (" + remove.Reason + ")");
                    Authority.Forget(remove.VesselId);
                    Control.OnVesselRemoved(remove.VesselId);
                    Broadcast(MessageId.VesselRemove, remove, Channel.Bulk, Delivery.ReliableOrdered, client.Peer);
                    break;
                }
                case MessageId.KerbalProto:
                    Roster.HandleKerbalProto(client, Envelope.Read<KerbalProtoMsg>(body));
                    break;
                case MessageId.KerbalStatus:
                    Roster.HandleKerbalStatus(client, Envelope.Read<KerbalStatusMsg>(body));
                    break;
                case MessageId.KerbalRemoved:
                    Roster.HandleKerbalRemoved(client, Envelope.Read<KerbalRemovedMsg>(body));
                    break;
                case MessageId.AvatarClaim:
                    Roster.HandleAvatarClaim(client, Envelope.Read<AvatarClaimMsg>(body));
                    break;
                case MessageId.Presence:
                {
                    var presence = Envelope.Read<PresenceMsg>(body);
                    presence.ClientId = client.ClientId;
                    client.Presence = presence;
                    Broadcast(MessageId.Presence, presence, Channel.Control, Delivery.ReliableOrdered);
                    break;
                }
                case MessageId.WarpRequest:
                    Warp.OnRequest(client, Envelope.Read<WarpRequestMsg>(body));
                    break;
                case MessageId.CtrlInput:
                {
                    var input = Envelope.Read<CtrlInputMsg>(body);
                    input.FromClientId = client.ClientId;
                    Control.ForwardToOwner(client, input.VesselId, MessageId.CtrlInput, input, Channel.State, Delivery.Sequenced);
                    break;
                }
                case MessageId.CtrlState:
                {
                    var state = Envelope.Read<CtrlInputMsg>(body);
                    state.FromClientId = client.ClientId;
                    Control.RelayStateToAboard(client, state.VesselId, state);
                    break;
                }
                case MessageId.Stage:
                {
                    var stage = Envelope.Read<StageMsg>(body);
                    stage.FromClientId = client.ClientId;
                    if (Control.ForwardToOwner(client, stage.VesselId, MessageId.Stage, stage, Channel.Control, Delivery.ReliableOrdered)) _log(client.DisplayName + " staged vessel " + stage.VesselId.ToString().Substring(0, 8));
                    break;
                }
                case MessageId.ActionGroup:
                {
                    var ag = Envelope.Read<ActionGroupMsg>(body);
                    ag.FromClientId = client.ClientId;
                    Control.ForwardToOwner(client, ag.VesselId, MessageId.ActionGroup, ag, Channel.Control, Delivery.ReliableOrdered);
                    break;
                }
                case MessageId.SasMode:
                {
                    var sas = Envelope.Read<SasModeMsg>(body);
                    sas.FromClientId = client.ClientId;
                    Control.ForwardToOwner(client, sas.VesselId, MessageId.SasMode, sas, Channel.Control, Delivery.ReliableOrdered);
                    break;
                }
                case MessageId.PartEvent:
                {
                    var ev = Envelope.Read<PartEventMsg>(body);
                    ev.FromClientId = client.ClientId;
                    if (Control.ForwardToOwner(client, ev.VesselId, MessageId.PartEvent, ev, Channel.Control, Delivery.ReliableOrdered)) _log(client.DisplayName + " pressed " + ev.EventName + " on vessel " + ev.VesselId.ToString().Substring(0, 8));
                    break;
                }
                case MessageId.EditorJoin:
                    Editor.HandleJoin(client, Envelope.Read<EditorJoinMsg>(body));
                    break;
                case MessageId.EditorLeave:
                    Editor.HandleLeave(client);
                    break;
                case MessageId.EditorSnapshot:
                    Editor.HandleSnapshot(client, Envelope.Read<EditorSnapshotMsg>(body));
                    break;
                case MessageId.EditorPresence:
                    Editor.HandlePresence(client, Envelope.Read<EditorPresenceMsg>(body));
                    break;
                case MessageId.EditorLaunch:
                    Editor.HandleLaunch(client, Envelope.Read<EditorLaunchMsg>(body));
                    break;
                case MessageId.DockIntent:
                    Authority.HandleDockIntent(client, Envelope.Read<DockIntentMsg>(body));
                    break;
                case MessageId.DockCommit:
                    HandleDockCommit(client, Envelope.Read<DockCommitMsg>(body));
                    break;
                case MessageId.AuthorityRequest:
                    Authority.Request(client, Envelope.Read<AuthorityRequestMsg>(body).VesselId);
                    break;
                case MessageId.AuthorityRelease:
                    Authority.Release(client, Envelope.Read<AuthorityReleaseMsg>(body).VesselId);
                    break;
                default:
                    _log("Unhandled message " + id + " from " + client.DisplayName);
                    break;
            }
        }

        private void HandleHello(ClientSession client, HelloMsg hello)
        {
            if (client.Handshaken) return;
            if (hello.ProtocolVersion != ProtocolVersion.Current)
            {
                Reject(client, "Protocol version mismatch: server " + ProtocolVersion.Current + ", client " + hello.ProtocolVersion + ". Update KspMp on the side that is behind.");
                return;
            }
            if (!PasswordHash.Matches(Config.Password, hello.PasswordHash))
            {
                Reject(client, string.IsNullOrEmpty(hello.PasswordHash)
                    ? "This server needs a password. Enter it in the Multiplayer window before connecting."
                    : "Wrong password.");
                return;
            }
            if (OnlineCount >= Config.MaxPlayers)
            {
                Reject(client, "Server is full (" + Config.MaxPlayers + " players)");
                return;
            }
            if (hello.PlayerId != Guid.Empty && HandshakenClients.Any(c => c.PlayerId == hello.PlayerId))
            {
                Reject(client, "A player with your id is already connected. If you copied the mod folder between installs, delete GameData/KspMp/PluginData/settings.cfg in one of them.");
                return;
            }

            var name = SanitizeName(hello.PlayerName);
            client.ClientId = _nextClientId++;
            client.PlayerId = hello.PlayerId;
            client.PlayerName = name;
            client.Handshaken = true;
            Touch(client);
            if (_knownPlayers.TryGetValue(client.PlayerId, out var known) && !string.IsNullOrEmpty(known.AvatarKerbalName))
                client.AvatarKerbalName = known.AvatarKerbalName;
            _log(client.DisplayName + " joined (KSP " + hello.KspVersion + ", mod " + hello.ModVersion + ")");

            Send(client.Peer, MessageId.Welcome, new WelcomeMsg
            {
                ClientId = client.ClientId,
                ServerName = Config.ServerName,
                UniversalTime = Time.UniversalTime,
                TimeRate = Time.Rate,
                NeedsAvatar = !client.HasAvatar,
                AvatarKerbalName = client.AvatarKerbalName,
            }, Channel.Control, Delivery.ReliableOrdered);
            Send(client.Peer, MessageId.TimeSync, Time.Snapshot(0), Channel.Control, Delivery.ReliableOrdered);
            Send(client.Peer, MessageId.WarpState, Warp.Snapshot(), Channel.Control, Delivery.ReliableOrdered);
            Players.OnJoined(client);
            Roster.Sync(client);
            SyncVessels(client);
            foreach (var other in HandshakenClients)
                if (other != client && other.Presence.ClientId != 0)
                    Send(client.Peer, MessageId.Presence, other.Presence, Channel.Control, Delivery.ReliableOrdered);
            Control.OnClientsChanged();
            Control.SendRolesTo(client);
            Send(client.Peer, MessageId.SyncComplete, new SyncCompleteMsg { Kerbals = Roster.Store.Count, Vessels = Vessels.Count }, Channel.Bulk, Delivery.ReliableOrdered);
            if (!string.IsNullOrEmpty(Config.MessageOfTheDay))
                Send(client.Peer, MessageId.Chat, new ChatMsg { FromClientId = 0, FromName = "Server", Text = Config.MessageOfTheDay }, Channel.ChatMod, Delivery.ReliableOrdered);
            Chat.ServerNotice(name + " joined");
        }

        private void HandleVesselProto(ClientSession client, VesselProtoMsg proto)
        {
            if (proto.VesselId == Guid.Empty) return;
            var owner = Authority.OwnerOf(proto.VesselId);
            if (owner != 0 && owner != client.ClientId)
            {
                _log(client.DisplayName + " sent a snapshot of vessel " + proto.VesselId + " owned by #" + owner + "; ignored");
                return;
            }
            var record = Vessels.Upsert(proto, Time.UniversalTime);
            if (owner == 0)
            {
                Authority.Assign(proto.VesselId, client.ClientId, AuthorityReason.Created);
                owner = client.ClientId;
            }
            Control.OnVesselSnapshot(record);
            owner = Authority.OwnerOf(proto.VesselId);
            if (proto.Reason != ProtoReason.Periodic)
                _log(client.DisplayName + " snapshot of '" + record.Name + "' " + proto.VesselId.ToString().Substring(0, 8) + " (" + proto.Reason + ", " + record.ProtoDeflated.Length + " bytes)");
            Broadcast(MessageId.VesselProto, record.ToProtoMessage(owner, proto.Reason), Channel.Bulk, Delivery.ReliableOrdered, client.Peer);
        }

        /// <summary>Docking finished on the owner: one vessel absorbed the other.</summary>
        private void HandleDockCommit(ClientSession client, DockCommitMsg commit)
        {
            if (!Authority.IsOwnedBy(commit.SurvivorVesselId, client.ClientId))
            {
                _log(client.DisplayName + " reported a docking of vessel " + commit.SurvivorVesselId + " it does not own; ignored");
                return;
            }
            var removedOwner = Authority.OwnerOf(commit.RemovedVesselId);
            if (removedOwner != 0 && removedOwner != client.ClientId)
            {
                _log(client.DisplayName + " reported docking with vessel " + commit.RemovedVesselId + " owned by #" + removedOwner + "; ignored");
                return;
            }
            var record = Vessels.Upsert(new VesselProtoMsg
            {
                VesselId = commit.SurvivorVesselId,
                PersistentId = Vessels.TryGet(commit.SurvivorVesselId, out var existing) ? existing.PersistentId : 0,
                Name = commit.Name,
                VesselType = existing != null ? existing.VesselType : "Ship",
                Reason = ProtoReason.Modified,
                ProtoDeflated = commit.ProtoDeflated,
            }, Time.UniversalTime);
            Vessels.Remove(commit.RemovedVesselId);
            Authority.Forget(commit.RemovedVesselId);
            Control.OnVesselRemoved(commit.RemovedVesselId);
            _log(client.DisplayName + ": vessel " + commit.RemovedVesselId.ToString().Substring(0, 8) + " docked into '" + record.Name + "' " + commit.SurvivorVesselId.ToString().Substring(0, 8));
            commit.OwnerClientId = client.ClientId;
            Broadcast(MessageId.DockCommit, commit, Channel.Bulk, Delivery.ReliableOrdered, client.Peer);
            Control.OnVesselSnapshot(record);
        }

        /// <summary>Sends every known vessel (with its current owner) to one client.</summary>
        private void SyncVessels(ClientSession client)
        {
            foreach (var record in Vessels.All)
                Send(client.Peer, MessageId.VesselProto, record.ToProtoMessage(Authority.OwnerOf(record.Id), ProtoReason.Sync), Channel.Bulk, Delivery.ReliableOrdered);
            if (Vessels.Count > 0) _log("Synced " + Vessels.Count + " vessel(s) to " + client.DisplayName);
        }

        private static string SanitizeName(string name)
        {
            name = (name ?? string.Empty).Trim();
            if (name.Length == 0) name = "Player";
            if (name.Length > 24) name = name.Substring(0, 24);
            return name;
        }

        private void Touch(ClientSession client)
        {
            if (client.PlayerId == Guid.Empty) return;
            if (!_knownPlayers.TryGetValue(client.PlayerId, out var known))
                _knownPlayers[client.PlayerId] = known = new KnownPlayer { PlayerId = client.PlayerId };
            known.Name = client.PlayerName;
            known.LastSeenUtc = DateTime.UtcNow;
        }

        /// <summary>Records a player's avatar kerbal (persisted in players.cfg).</summary>
        public void SetAvatar(ClientSession client, string kerbalName)
        {
            client.AvatarKerbalName = kerbalName;
            Touch(client);
            if (client.PlayerId != Guid.Empty && _knownPlayers.TryGetValue(client.PlayerId, out var known)) known.AvatarKerbalName = kerbalName;
            Save();
        }

        /// <summary>Sends a Reject, then disconnects after <see cref="RejectGraceMs"/> (the reason also rides in the disconnect payload).</summary>
        private void Reject(ClientSession client, string reason)
        {
            client.Rejected = true;
            _log("Rejecting " + client.DisplayName + ": " + reason);
            Send(client.Peer, MessageId.Reject, new RejectMsg { Reason = reason }, Channel.Control, Delivery.ReliableOrdered);
            _pendingDisconnects.Add(new PendingDisconnect { Peer = client.Peer, Reason = reason, DueUtc = DateTime.UtcNow.AddMilliseconds(RejectGraceMs) });
        }

        private void ProcessPendingDisconnects()
        {
            var now = DateTime.UtcNow;
            for (var i = _pendingDisconnects.Count - 1; i >= 0; i--)
            {
                var pending = _pendingDisconnects[i];
                if (pending.DueUtc > now) continue;
                _pendingDisconnects.RemoveAt(i);
                Transport.Disconnect(pending.Peer, pending.Reason);
            }
        }

        // ---- sending ----

        public void Send<T>(PeerId to, MessageId id, T message, Channel channel, Delivery delivery) where T : INetSerializable
        {
            Envelope.Write(_writer, id, message);
            Transport.Send(to, _writer.Data, 0, _writer.Length, channel, delivery);
        }

        /// <summary>Sends to every handshaken client, optionally skipping one peer.</summary>
        public void Broadcast<T>(MessageId id, T message, Channel channel, Delivery delivery, PeerId except = default(PeerId)) where T : INetSerializable
        {
            Envelope.Write(_writer, id, message);
            foreach (var client in _clients.Values)
            {
                if (!client.IsOnline || client.Peer == except) continue;
                Transport.Send(client.Peer, _writer.Data, 0, _writer.Length, channel, delivery);
            }
        }
    }
}
