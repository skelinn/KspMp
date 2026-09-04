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
        private readonly Dictionary<Guid, DateTime> _dockingHolds = new Dictionary<Guid, DateTime>();

        /// <summary>How long after the last DockIntent the pilot rule stays suspended for the vessel that yielded.</summary>
        public int DockingHoldSeconds = 20;

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
            _dockingHolds.Remove(vesselId);
        }

        public bool IsDockingHeld(Guid vesselId)
        {
            if (!_dockingHolds.TryGetValue(vesselId, out var until)) return false;
            if (until > DateTime.UtcNow) return true;
            _dockingHolds.Remove(vesselId);
            return false;
        }

        /// <summary>
        /// Two vessels are about to dock. Both must be simulated by one client: an unpiloted vessel yields to the
        /// piloted one; with two pilots the vessel with the lower persistent id yields. The yielding vessel gets a
        /// docking hold so the seat rule does not hand it straight back.
        /// </summary>
        public void HandleDockIntent(ClientSession client, DockIntentMsg intent)
        {
            var mine = intent.MyVesselId;
            var other = intent.OtherVesselId;
            if (!IsOwnedBy(mine, client.ClientId)) return;
            var otherOwner = OwnerOf(other);
            if (otherOwner == client.ClientId) return;
            if (!_server.Vessels.TryGet(mine, out var mineRecord) || !_server.Vessels.TryGet(other, out var otherRecord)) return;

            var myPilot = _server.Control.PilotOf(mine);
            var otherPilot = _server.Control.PilotOf(other);
            Guid yielding;
            int newOwner;
            if (otherOwner == 0 || otherPilot == 0) { yielding = other; newOwner = client.ClientId; }
            else if (myPilot == 0) { yielding = mine; newOwner = otherOwner; }
            else if (mineRecord.PersistentId < otherRecord.PersistentId) { yielding = mine; newOwner = otherOwner; }
            else { yielding = other; newOwner = client.ClientId; }

            var refresh = IsDockingHeld(yielding);
            _dockingHolds[yielding] = DateTime.UtcNow.AddSeconds(DockingHoldSeconds);
            if (OwnerOf(yielding) == newOwner) return;
            _server.Log("Docking approach (" + intent.DistanceMeters.ToString("F0") + " m): vessel " + yielding.ToString().Substring(0, 8) + " yields to #" + newOwner + (refresh ? " (refreshed)" : ""));
            Assign(yielding, newOwner, AuthorityReason.Granted);
        }

        public void Assign(Guid vesselId, int ownerClientId, AuthorityReason reason)
        {
            if (ownerClientId == 0) _owners.Remove(vesselId);
            else _owners[vesselId] = ownerClientId;
            _server.Broadcast(MessageId.AuthorityAssign, new AuthorityAssignMsg { VesselId = vesselId, OwnerClientId = ownerClientId, Reason = reason }, Channel.Control, Delivery.ReliableOrdered);
        }
    }
}
