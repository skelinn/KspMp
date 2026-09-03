using System;
using System.Collections.Generic;
using System.Linq;
using KspMp.Shared.Protocol;

namespace KspMp.Server.Vessels
{
    /// <summary>
    /// Who simulates which vessel. Exactly one client (or nobody) owns a vessel's physics; only the owner may send
    /// its state and snapshots. M2 rules: first requester wins, ownership ends on release or disconnect.
    /// </summary>
    public sealed class AuthorityService
    {
        private readonly ServerCore _server;
        private readonly Dictionary<Guid, int> _owners = new Dictionary<Guid, int>();

        public AuthorityService(ServerCore server)
        {
            _server = server;
        }

        public int OwnerOf(Guid vesselId) => _owners.TryGetValue(vesselId, out var owner) ? owner : 0;
        public bool IsOwnedBy(Guid vesselId, int clientId) => OwnerOf(vesselId) == clientId;
        public bool IsUnowned(Guid vesselId) => OwnerOf(vesselId) == 0;
        public IEnumerable<Guid> VesselsOwnedBy(int clientId) => _owners.Where(p => p.Value == clientId).Select(p => p.Key).ToList();

        /// <summary>A client asks to simulate a vessel. Granted when nobody (online) owns it; otherwise the client is told who does.</summary>
        public void Request(ClientSession client, Guid vesselId)
        {
            var owner = OwnerOf(vesselId);
            if (owner == client.ClientId) return;
            if (owner != 0)
            {
                _server.Send(client.Peer, MessageId.AuthorityAssign, new AuthorityAssignMsg { VesselId = vesselId, OwnerClientId = owner, Reason = AuthorityReason.Denied }, Channel.Control, Delivery.ReliableOrdered);
                return;
            }
            Assign(vesselId, client.ClientId, AuthorityReason.Granted);
        }

        public void Release(ClientSession client, Guid vesselId)
        {
            if (OwnerOf(vesselId) != client.ClientId) return;
            Assign(vesselId, 0, AuthorityReason.Released);
        }

        public void ReleaseAll(ClientSession client)
        {
            foreach (var vesselId in VesselsOwnedBy(client.ClientId))
                Assign(vesselId, 0, AuthorityReason.OwnerLeft);
        }

        public void Forget(Guid vesselId)
        {
            _owners.Remove(vesselId);
        }

        public void Assign(Guid vesselId, int ownerClientId, AuthorityReason reason)
        {
            if (ownerClientId == 0) _owners.Remove(vesselId);
            else _owners[vesselId] = ownerClientId;
            _server.Broadcast(MessageId.AuthorityAssign, new AuthorityAssignMsg { VesselId = vesselId, OwnerClientId = ownerClientId, Reason = reason }, Channel.Control, Delivery.ReliableOrdered);
        }
    }
}
