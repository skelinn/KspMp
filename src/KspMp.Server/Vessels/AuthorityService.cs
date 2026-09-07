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
        private readonly Dictionary<Guid, uint> _seq = new Dictionary<Guid, uint>();

        /// <summary>How long after the last DockIntent the pilot rule stays suspended for the vessel that yielded.</summary>
        public int DockingHoldSeconds = 60;

        public AuthorityService(ServerCore server)
        {
            _server = server;
        }

        public int OwnerOf(Guid vesselId) => _owners.TryGetValue(vesselId, out var owner) ? owner : 0;
        /// <summary>How many authority decisions this vessel has had. Rides on every message that carries an owner.</summary>
        public uint SeqOf(Guid vesselId) => _seq.TryGetValue(vesselId, out var seq) ? seq : 0;
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
                // Deliberately not "unless the owner looks idle": presence arrives a beat after the snapshot that
                // claims a vessel, so a client entering flight would race the launcher for their own rocket.
                // Taking a vessel off an owner who is not flying it is what ControlRequest (R3) is for.
                Tell(client, vesselId, AuthorityReason.Denied);
                return;
            }
            Assign(vesselId, client.ClientId, AuthorityReason.Granted);
        }

        public void Release(ClientSession client, Guid vesselId)
        {
            if (OwnerOf(vesselId) != client.ClientId) return;
            // Prefer someone else who is aboard and in flight over leaving the vessel unsimulated.
            if (_server.Control.TryHandOverOnDeparture(vesselId, client.ClientId)) return;
            Assign(vesselId, 0, AuthorityReason.Released);
        }

        public void ReleaseAll(ClientSession client)
        {
            foreach (var vesselId in VesselsOwnedBy(client.ClientId))
            {
                if (_server.Control.TryHandOverOnDeparture(vesselId, client.ClientId)) continue;
                Assign(vesselId, 0, AuthorityReason.OwnerLeft);
            }
        }

        public void Forget(Guid vesselId)
        {
            _owners.Remove(vesselId);
            _dockingHolds.Remove(vesselId);
            // The sequence is kept on purpose: a client that missed the removal still holds the old number, and
            // a later assignment restarting at 1 would look stale to it for the rest of the session.
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

            // "Has a pilot" here means somebody is aboard and actually in flight on it: an unattended vessel is
            // the one that should yield, and a vessel whose crew is not in the flight scene is unattended.
            var myPilot = _server.Control.FlyingCrewOf(mine);
            var otherPilot = _server.Control.FlyingCrewOf(other);
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
            var seq = SeqOf(vesselId) + 1;
            _seq[vesselId] = seq;
            _server.Broadcast(MessageId.AuthorityAssign, new AuthorityAssignMsg { VesselId = vesselId, OwnerClientId = ownerClientId, Reason = reason, AuthoritySeq = seq }, Channel.Control, Delivery.ReliableOrdered);
            _server.Control.OnAuthorityChanged(vesselId);
        }

        /// <summary>Tells one client who owns a vessel without changing anything (a refusal, or an answer to a request).</summary>
        public void Tell(ClientSession client, Guid vesselId, AuthorityReason reason)
        {
            _server.Send(client.Peer, MessageId.AuthorityAssign, new AuthorityAssignMsg { VesselId = vesselId, OwnerClientId = OwnerOf(vesselId), Reason = reason, AuthoritySeq = SeqOf(vesselId) }, Channel.Control, Delivery.ReliableOrdered);
        }
    }
}
