using System;
using System.Collections.Generic;
using System.Linq;
using KspMp.Shared.Protocol;

namespace KspMp.Server.Editor
{
    /// <summary>
    /// Building together.
    ///
    /// Everyone gets their own workbench. Opening the VAB opens a session keyed by your client id, private until
    /// somebody joins it; the list of open benches goes to every player in every scene, so anyone can see who is
    /// building what and click Join. One shared bench per facility - what this used to be - meant two players who
    /// simply both wanted to build were dropped onto the same craft and fought over it.
    ///
    /// Within a session the old rules still hold: the server keeps the latest craft and a revision number, and a
    /// snapshot built on a stale revision is refused and answered with the current craft, so two people editing at
    /// once converge instead of overwriting each other. The craft lives only for the session; launching or the
    /// owner leaving the editor ends it.
    /// </summary>
    public sealed class EditorSessionService
    {
        public sealed class Session
        {
            /// <summary>Whose bench this is. Also the session's id.</summary>
            public int OwnerClientId;
            public EditorFacilityKind Facility;
            /// <summary>The owner plus every guest, owner first.</summary>
            public readonly List<int> Builders = new List<int>();
            public int Revision;
            public string ShipName = "";
            public int PartCount;
            public byte[] CraftDeflated = Array.Empty<byte>();
            public byte[] ManifestDeflated = Array.Empty<byte>();

            public bool HasCraft => CraftDeflated.Length > 0;
        }

        private readonly ServerCore _server;
        private readonly Dictionary<int, Session> _sessions = new Dictionary<int, Session>();

        public EditorSessionService(ServerCore server)
        {
            _server = server;
        }

        public IEnumerable<Session> Sessions => _sessions.Values;

        public Session Get(int ownerClientId) => _sessions.TryGetValue(ownerClientId, out var session) ? session : null;

        /// <summary>The session a client is building in, own or guest; null when they are not in an editor.</summary>
        public Session SessionOf(int clientId)
        {
            foreach (var session in _sessions.Values)
                if (session.Builders.Contains(clientId)) return session;
            return null;
        }

        public int BuilderCount(int ownerClientId) => Get(ownerClientId)?.Builders.Count ?? 0;

        /// <summary>A client opened an editor: give them their own empty bench in that facility.</summary>
        public void HandleJoin(ClientSession client, EditorJoinMsg join)
        {
            LeaveGuestSessions(client.ClientId, announce: false);
            var session = OpenOwnSession(client.ClientId, join.Facility);
            _server.Log(client.DisplayName + " opened their own " + join.Facility + " workbench");
            BroadcastList();
        }

        private Session OpenOwnSession(int clientId, EditorFacilityKind facility)
        {
            if (!_sessions.TryGetValue(clientId, out var session))
                _sessions[clientId] = session = new Session { OwnerClientId = clientId, Facility = facility };
            session.Facility = facility;
            if (!session.Builders.Contains(clientId)) session.Builders.Insert(0, clientId);
            return session;
        }

        /// <summary>Join another player's bench, or 0 to go back to your own.</summary>
        public void HandleSessionJoin(ClientSession client, EditorSessionJoinMsg msg)
        {
            var current = SessionOf(client.ClientId);
            var facility = current?.Facility ?? EditorFacilityKind.Vab;

            if (msg.OwnerClientId == 0 || msg.OwnerClientId == client.ClientId)
            {
                if (current != null && current.OwnerClientId == client.ClientId) return;   // already home
                LeaveGuestSessions(client.ClientId, announce: true);
                OpenOwnSession(client.ClientId, facility);
                _server.Log(client.DisplayName + " went back to their own " + facility + " workbench");
                BroadcastList();
                return;
            }

            var target = Get(msg.OwnerClientId);
            if (target == null || target.OwnerClientId == client.ClientId)
            {
                SendListTo(client);
                return;
            }
            if (target.Facility != facility)
            {
                // Two facilities are two different buildings; joining across them would put a spaceplane on a
                // VAB bench. The client hides the button, so this only catches a stale list.
                _server.Log(client.DisplayName + " tried to join a " + target.Facility + " bench from the " + facility);
                SendListTo(client);
                return;
            }

            // A guest has no bench of their own while they are visiting.
            if (_sessions.TryGetValue(client.ClientId, out var own))
            {
                own.Builders.Remove(client.ClientId);
                if (own.Builders.Count == 0) _sessions.Remove(client.ClientId);
            }
            LeaveGuestSessions(client.ClientId, announce: false);
            if (!target.Builders.Contains(client.ClientId)) target.Builders.Add(client.ClientId);
            _server.Log(client.DisplayName + " joined #" + target.OwnerClientId + "'s " + target.Facility + " workbench (" + target.Builders.Count + " builder(s), revision " + target.Revision + ")");
            if (target.HasCraft)
                _server.Send(client.Peer, MessageId.EditorSnapshot, ToSnapshot(target, 0), Channel.Bulk, Delivery.ReliableOrdered);
            BroadcastList();
        }

        /// <summary>Left the editor entirely: their own bench is gone and any bench they were visiting loses them.</summary>
        public void HandleLeave(ClientSession client, bool announce = true)
        {
            var changed = LeaveGuestSessions(client.ClientId, announce);
            if (_sessions.TryGetValue(client.ClientId, out var own))
            {
                _sessions.Remove(client.ClientId);
                changed = true;
                if (announce) _server.Log(client.DisplayName + " left the editor; their workbench is closed (" + (own.Builders.Count - 1) + " guest(s) sent home)");
                // Guests of a closed bench go back to their own, keeping whatever is on it - the alternative is
                // silently dropping the craft they were helping with.
                foreach (var guest in own.Builders.ToList())
                    if (guest != client.ClientId) OpenOwnSession(guest, own.Facility);
            }
            if (changed) BroadcastList();
        }

        private bool LeaveGuestSessions(int clientId, bool announce)
        {
            var changed = false;
            foreach (var session in _sessions.Values.ToList())
            {
                if (session.OwnerClientId == clientId || !session.Builders.Remove(clientId)) continue;
                changed = true;
                if (announce) _server.Log("#" + clientId + " left #" + session.OwnerClientId + "'s workbench (" + session.Builders.Count + " builder(s) left)");
            }
            return changed;
        }

        /// <summary>
        /// The session a message is addressed to. 0 or the sender's own id means their own bench, created if a
        /// join went missing: relying on the join alone means one lost packet leaves a player invisible, unable to
        /// send or receive a craft, with nothing to recover it short of leaving and coming back.
        /// </summary>
        private Session Route(ClientSession client, int sessionOwnerClientId, EditorFacilityKind facility)
        {
            if (sessionOwnerClientId == 0 || sessionOwnerClientId == client.ClientId)
            {
                var own = SessionOf(client.ClientId);
                if (own != null && own.OwnerClientId == client.ClientId) return own;
                if (own != null) return own;   // a guest whose client still thinks it is at home
                return OpenOwnSession(client.ClientId, facility);
            }
            var target = Get(sessionOwnerClientId);
            if (target == null || !target.Builders.Contains(client.ClientId))
            {
                SendListTo(client);
                return null;
            }
            return target;
        }

        public void HandleSnapshot(ClientSession client, EditorSnapshotMsg snapshot)
        {
            var session = Route(client, snapshot.SessionOwnerClientId, snapshot.Facility);
            if (session == null) return;

            // Built on an older revision: someone else changed the craft first, so send the current one back.
            if (snapshot.Revision < session.Revision)
            {
                _server.Send(client.Peer, MessageId.EditorSnapshot, ToSnapshot(session, 0), Channel.Bulk, Delivery.ReliableOrdered);
                return;
            }

            var wasEmpty = !session.HasCraft;
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
                SessionOwnerClientId = session.OwnerClientId,
            }, Channel.Bulk, Delivery.ReliableOrdered);
            // The list carries the ship name and part count, so the first craft on a bench changes what it says.
            if (wasEmpty || session.PartCount != snapshot.PartCount) BroadcastList();
        }

        public void HandlePresence(ClientSession client, EditorPresenceMsg presence)
        {
            var session = Route(client, presence.SessionOwnerClientId, presence.Facility);
            if (session == null) return;
            presence.ClientId = client.ClientId;
            presence.SessionOwnerClientId = session.OwnerClientId;
            foreach (var peer in Peers(session, except: client.ClientId))
                _server.Send(peer.Peer, MessageId.EditorPresence, presence, Channel.State, Delivery.Sequenced);
        }

        /// <summary>Any builder may launch, and doing so ends the session for everyone on it.</summary>
        public void HandleLaunch(ClientSession client, EditorLaunchMsg launch)
        {
            var session = Route(client, launch.SessionOwnerClientId, launch.Facility);
            if (session == null) return;
            launch.FromClientId = client.ClientId;
            launch.SessionOwnerClientId = session.OwnerClientId;
            _server.Log(client.DisplayName + " launched '" + launch.ShipName + "' from the " + launch.Facility + " to " + launch.LaunchSite);
            // Everyone needs this, not just the other builders: a player stood in the space center about to
            // launch has no other way to know the pad is about to be taken, and two craft on one pad destroy
            // each other along with everybody aboard.
            foreach (var peer in _server.HandshakenClients)
                if (peer.ClientId != client.ClientId)
                    _server.Send(peer.Peer, MessageId.EditorLaunch, launch, Channel.Control, Delivery.ReliableOrdered);
            Clear(session, "launched");
            BroadcastList();
        }

        private void Clear(Session session, string why)
        {
            if (session.HasCraft) _server.Log("Cleared #" + session.OwnerClientId + "'s " + session.Facility + " workbench (" + why + ")");
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
            SessionOwnerClientId = session.OwnerClientId,
        };

        private IEnumerable<ClientSession> Peers(Session session, int except) =>
            _server.HandshakenClients.Where(c => c.ClientId != except && session.Builders.Contains(c.ClientId));

        public EditorSessionListMsg BuildList() => new EditorSessionListMsg
        {
            Sessions = _sessions.Values.Select(s => new EditorSessionInfo
            {
                OwnerClientId = s.OwnerClientId,
                Facility = s.Facility,
                ShipName = s.ShipName,
                PartCount = s.PartCount,
                Revision = s.Revision,
                BuilderClientIds = s.Builders.ToArray(),
            }).ToArray(),
        };

        /// <summary>Everyone gets the list, in every scene: the players panel says who is building what.</summary>
        public void BroadcastList() => _server.Broadcast(MessageId.EditorSessionList, BuildList(), Channel.Control, Delivery.ReliableOrdered);

        public void SendListTo(ClientSession client) =>
            _server.Send(client.Peer, MessageId.EditorSessionList, BuildList(), Channel.Control, Delivery.ReliableOrdered);
    }
}
