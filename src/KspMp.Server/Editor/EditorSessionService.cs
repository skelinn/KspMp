using System;
using System.Collections.Generic;
using System.Linq;
using KspMp.Shared.Protocol;

namespace KspMp.Server.Editor
{
    /// <summary>
    /// Shared building. One session per facility (VAB and SPH); everyone in it works on the same craft. The server
    /// keeps the latest craft snapshot and a revision number: a snapshot built on a stale revision is rejected and
    /// the sender gets the current craft instead, so two people editing at once converge instead of fighting.
    /// The craft lives only for the session; launching or the last builder leaving clears it.
    /// </summary>
    public sealed class EditorSessionService
    {
        public sealed class Session
        {
            public EditorFacilityKind Facility;
            public readonly List<int> Builders = new List<int>();
            public int Revision;
            public string ShipName = "";
            public int PartCount;
            public byte[] CraftDeflated = Array.Empty<byte>();
            public byte[] ManifestDeflated = Array.Empty<byte>();

            public bool HasCraft => CraftDeflated.Length > 0;
        }

        private readonly ServerCore _server;
        private readonly Dictionary<EditorFacilityKind, Session> _sessions = new Dictionary<EditorFacilityKind, Session>();

        public EditorSessionService(ServerCore server)
        {
            _server = server;
        }

        public IEnumerable<Session> Sessions => _sessions.Values;

        public Session Get(EditorFacilityKind facility)
        {
            if (!_sessions.TryGetValue(facility, out var session)) _sessions[facility] = session = new Session { Facility = facility };
            return session;
        }

        public int BuilderCount(EditorFacilityKind facility) => Get(facility).Builders.Count;

        public void HandleJoin(ClientSession client, EditorJoinMsg join)
        {
            var session = Get(join.Facility);
            if (!session.Builders.Contains(client.ClientId)) session.Builders.Add(client.ClientId);
            _server.Log(client.DisplayName + " is building in the " + join.Facility + " (" + session.Builders.Count + " builder(s), revision " + session.Revision + ")");

            // Hand the newcomer whatever is on the workbench, then tell everyone who is here.
            if (session.HasCraft)
                _server.Send(client.Peer, MessageId.EditorSnapshot, ToSnapshot(session, 0), Channel.Bulk, Delivery.ReliableOrdered);
            BroadcastRoster(session);
        }

        public void HandleLeave(ClientSession client, bool announce = true)
        {
            foreach (var session in _sessions.Values)
            {
                if (!session.Builders.Remove(client.ClientId)) continue;
                if (announce) _server.Log(client.DisplayName + " left the " + session.Facility + " (" + session.Builders.Count + " builder(s) left)");
                if (session.Builders.Count == 0) Clear(session, "everyone left");
                else BroadcastRoster(session);
            }
        }

        public void HandleSnapshot(ClientSession client, EditorSnapshotMsg snapshot)
        {
            var session = Get(snapshot.Facility);
            if (!session.Builders.Contains(client.ClientId)) return;

            // Built on an older revision: someone else changed the craft first, so send the current one back.
            if (snapshot.Revision < session.Revision)
            {
                _server.Send(client.Peer, MessageId.EditorSnapshot, ToSnapshot(session, 0), Channel.Bulk, Delivery.ReliableOrdered);
                return;
            }

            session.Revision++;
            session.ShipName = snapshot.ShipName ?? string.Empty;
            session.PartCount = snapshot.PartCount;
            session.CraftDeflated = snapshot.CraftDeflated ?? Array.Empty<byte>();
            session.ManifestDeflated = snapshot.ManifestDeflated ?? Array.Empty<byte>();
            var outgoing = ToSnapshot(session, client.ClientId);
            foreach (var peer in Peers(session, except: client.ClientId))
                _server.Send(peer.Peer, MessageId.EditorSnapshot, outgoing, Channel.Bulk, Delivery.ReliableOrdered);
            // Echo the accepted revision so the sender stops resending it.
            _server.Send(client.Peer, MessageId.EditorSnapshot, new EditorSnapshotMsg
            {
                Facility = session.Facility,
                FromClientId = client.ClientId,
                Revision = session.Revision,
                ShipName = session.ShipName,
                PartCount = session.PartCount,
                CraftDeflated = Array.Empty<byte>(),   // the sender already has the craft
                ManifestDeflated = Array.Empty<byte>(),
            }, Channel.Bulk, Delivery.ReliableOrdered);
        }

        public void HandlePresence(ClientSession client, EditorPresenceMsg presence)
        {
            var session = Get(presence.Facility);
            if (!session.Builders.Contains(client.ClientId)) return;
            presence.ClientId = client.ClientId;
            foreach (var peer in Peers(session, except: client.ClientId))
                _server.Send(peer.Peer, MessageId.EditorPresence, presence, Channel.State, Delivery.Sequenced);
        }

        public void HandleLaunch(ClientSession client, EditorLaunchMsg launch)
        {
            var session = Get(launch.Facility);
            if (!session.Builders.Contains(client.ClientId)) return;
            launch.FromClientId = client.ClientId;
            _server.Log(client.DisplayName + " launched '" + launch.ShipName + "' from the " + launch.Facility + " to " + launch.LaunchSite);
            foreach (var peer in Peers(session, except: client.ClientId))
                _server.Send(peer.Peer, MessageId.EditorLaunch, launch, Channel.Control, Delivery.ReliableOrdered);
            Clear(session, "launched");
        }

        private void Clear(Session session, string why)
        {
            if (session.HasCraft) _server.Log("Cleared the " + session.Facility + " workbench (" + why + ")");
            session.CraftDeflated = Array.Empty<byte>();
            session.ManifestDeflated = Array.Empty<byte>();
            session.ShipName = "";
            session.PartCount = 0;
            session.Revision = 0;
        }

        private EditorSnapshotMsg ToSnapshot(Session session, int fromClientId) => new EditorSnapshotMsg
        {
            Facility = session.Facility,
            FromClientId = fromClientId,
            Revision = session.Revision,
            ShipName = session.ShipName,
            PartCount = session.PartCount,
            CraftDeflated = session.CraftDeflated,
            ManifestDeflated = session.ManifestDeflated,
        };

        private IEnumerable<ClientSession> Peers(Session session, int except) =>
            _server.HandshakenClients.Where(c => c.ClientId != except && session.Builders.Contains(c.ClientId));

        private void BroadcastRoster(Session session)
        {
            // Builders learn about each other through presence; an empty presence announces arrival/departure.
            foreach (var peer in Peers(session, except: 0))
                _server.Send(peer.Peer, MessageId.EditorPresence, new EditorPresenceMsg { Facility = session.Facility, ClientId = 0, Holding = false, HeldPartName = string.Empty }, Channel.Control, Delivery.ReliableOrdered);
        }
    }
}
