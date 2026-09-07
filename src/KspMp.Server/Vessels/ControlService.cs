using System;
using System.Collections.Generic;
using System.Linq;
using KspMp.Shared.Protocol;
using LiteNetLib.Utils;

namespace KspMp.Server.Vessels
{
    /// <summary>
    /// Who flies what.
    ///
    /// The pilot IS the physics owner - the command seat decides nothing. An earlier design handed authority to
    /// whoever sat in the command seat, which meant that seating a friend's kerbal before launch gave the rocket
    /// to a player who was still standing in the VAB: nobody simulated it, and the player who actually launched it
    /// was locked out of his own controls as a "spectator".
    ///
    /// The rules, in full:
    ///   R1 Whoever sends a vessel's first snapshot owns it (it is their launch) and keeps it.
    ///   R2 The owner may Give it to a player whose presence says they are in flight on that same vessel.
    ///      To anyone else the server answers NotInFlight and changes nothing.
    ///   R3 Someone aboard and in flight may Request it. Granted at once when the owner is gone, offline or no
    ///      longer aboard; otherwise the request is relayed to the owner, who may Decline.
    ///   R4 When the owner's avatar leaves the vessel, or they release it or disconnect, it goes to the first
    ///      player aboard who is in flight on it (the command seat only breaks a tie). If there is nobody, an
    ///      owner who merely left the vessel keeps it - a pilot on EVA still simulates the rocket beside them -
    ///      and an owner who released or disconnected leaves it unowned for the volunteer path to pick up.
    ///   R5 Otherwise authority does not move. In particular the server never assigns a vessel to a client that is
    ///      not in flight on it: that leaves nobody simulating it, which is worse than the wrong player simulating
    ///      it (see "Docking authority: why the obvious fix is not the fix" in docs/PLAN.md). The exceptions are
    ///      R1, a client's own AuthorityRequest, and the docking hand-off.
    ///
    /// Control input and discrete actions from people aboard are forwarded to the physics owner. Co-pilots'
    /// flight axes are locked on their own client unless the pilot turns on shared stick for that vessel.
    /// </summary>
    public sealed class ControlService
    {
        public sealed class Roles
        {
            public Guid VesselId;
            public int PilotClientId;
            public List<int> Aboard = new List<int>();
            public string PilotKerbal;
            public List<string> AboardKerbals = new List<string>();
            public bool SharedStick;
        }

        private readonly ServerCore _server;
        private readonly Dictionary<Guid, Roles> _roles = new Dictionary<Guid, Roles>();
        /// <summary>The last crew placement seen per vessel, so a departure can be worked out after the owner is gone.</summary>
        private readonly Dictionary<Guid, VesselCrewInfo> _crew = new Dictionary<Guid, VesselCrewInfo>();

        public ControlService(ServerCore server)
        {
            _server = server;
        }

        public bool TryGetRoles(Guid vesselId, out Roles roles) => _roles.TryGetValue(vesselId, out roles);
        public int PilotOf(Guid vesselId) => _roles.TryGetValue(vesselId, out var r) ? r.PilotClientId : 0;
        public bool IsAboard(Guid vesselId, int clientId) => _roles.TryGetValue(vesselId, out var r) && r.Aboard.Contains(clientId);

        /// <summary>Is this client in the flight scene, on this very vessel? Only such a client can simulate it.</summary>
        public bool IsFlying(int clientId, Guid vesselId)
        {
            if (clientId == 0) return false;
            var client = _server.HandshakenClients.FirstOrDefault(c => c.ClientId == clientId);
            if (client == null) return false;
            var presence = client.Presence;
            return (presence.State == PresenceState.InFlight || presence.State == PresenceState.OnEva) && presence.VesselId == vesselId;
        }

        /// <summary>A client aboard this vessel and in flight on it, command seat first; 0 when there is none.</summary>
        public IEnumerable<string> CrewNamesOf(Guid vesselId)
        {
            return _crew.TryGetValue(vesselId, out var crew) ? new List<string>(crew.AllCrew()) : new List<string>();
        }

        public int FlyingCrewOf(Guid vesselId)
        {
            if (!_roles.TryGetValue(vesselId, out var roles)) return 0;
            if (roles.PilotClientId != 0 && IsFlying(roles.PilotClientId, vesselId)) return roles.PilotClientId;
            foreach (var clientId in roles.Aboard)
                if (IsFlying(clientId, vesselId)) return clientId;
            return 0;
        }

        /// <summary>Recomputes roles for a vessel from its snapshot. Never assigns authority; see R5.</summary>
        public void OnVesselSnapshot(VesselRecord record)
        {
            VesselCrewInfo crew;
            try
            {
                crew = VesselCrewInfo.Parse(System.Text.Encoding.UTF8.GetString(Shared.Codec.DeflateCodec.Decompress(record.ProtoDeflated, 0, record.ProtoDeflated.Length)));
            }
            catch (Exception e)
            {
                _server.Log("Could not read crew of vessel " + record.Id + ": " + e.Message);
                return;
            }
            _crew[record.Id] = crew;
            Recompute(record.Id, crew);
            ApplyDeparture(record.Id);
        }

        public void OnVesselRemoved(Guid vesselId)
        {
            _crew.Remove(vesselId);
            if (_roles.Remove(vesselId)) Broadcast(new Roles { VesselId = vesselId });
        }

        /// <summary>Client ids change on reconnect: refresh every vessel that has this player's avatar aboard.</summary>
        public void OnClientsChanged()
        {
            foreach (var record in _server.Vessels.All.ToList()) OnVesselSnapshot(record);
        }

        /// <summary>The owner changed: the pilot follows the owner, so the roles have to be re-announced.</summary>
        public void OnAuthorityChanged(Guid vesselId)
        {
            if (!_crew.TryGetValue(vesselId, out var crew)) return;
            Recompute(vesselId, crew);
        }

        private void Recompute(Guid vesselId, VesselCrewInfo crew)
        {
            var owner = _server.Authority.OwnerOf(vesselId);
            var hadRoles = _roles.TryGetValue(vesselId, out var old);
            var roles = new Roles { VesselId = vesselId, SharedStick = hadRoles ? old.SharedStick : _server.Config.SharedStickDefault };
            foreach (var name in crew.AllCrew())
            {
                var avatarOwner = _server.Roster.AvatarOwner(name);
                if (avatarOwner == null) continue;
                var clientId = _server.Roster.OnlineClientIdOf(avatarOwner);
                roles.AboardKerbals.Add(name);
                if (clientId != 0 && !roles.Aboard.Contains(clientId)) roles.Aboard.Add(clientId);
            }

            // The pilot is the physics owner, and only when their own kerbal is actually aboard. A player flying
            // an uncrewed probe owns it but is nobody's pilot; a kerbal in the command seat whose player is not
            // the owner is a co-pilot like everyone else aboard.
            if (owner != 0 && roles.Aboard.Contains(owner))
            {
                roles.PilotClientId = owner;
                foreach (var name in roles.AboardKerbals)
                {
                    var avatarOwner = _server.Roster.AvatarOwner(name);
                    if (avatarOwner != null && _server.Roster.OnlineClientIdOf(avatarOwner) == owner) { roles.PilotKerbal = name; break; }
                }
            }

            var changed = !hadRoles || old.PilotClientId != roles.PilotClientId || !old.Aboard.SequenceEqual(roles.Aboard) || old.SharedStick != roles.SharedStick;
            // A vessel nobody is aboard (debris, probes, a freshly separated booster) has nothing worth announcing.
            var worthAnnouncing = roles.Aboard.Count > 0 || roles.PilotClientId != 0 || (hadRoles && (old.Aboard.Count > 0 || old.PilotClientId != 0));
            _roles[vesselId] = roles;
            if (changed && worthAnnouncing)
            {
                _server.Log("Roles for vessel " + vesselId.ToString().Substring(0, 8) + ": pilot " + (roles.PilotClientId != 0 ? "#" + roles.PilotClientId + " (" + roles.PilotKerbal + ")" : "none") + ", aboard [" + string.Join(", ", roles.Aboard.Select(c => "#" + c)) + "]");
                Broadcast(roles);
            }
        }

        /// <summary>R4: the owner's avatar is no longer aboard, so hand the vessel to somebody who is.</summary>
        private void ApplyDeparture(Guid vesselId)
        {
            var owner = _server.Authority.OwnerOf(vesselId);
            if (owner == 0 || !_roles.TryGetValue(vesselId, out var roles)) return;
            if (roles.Aboard.Contains(owner)) return;            // still aboard: nothing to do
            if (roles.Aboard.Count == 0) return;                 // nobody else either: an owner on EVA keeps their rocket
            if (_server.Authority.IsDockingHeld(vesselId)) return;
            TryHandOverOnDeparture(vesselId, owner);
        }

        /// <summary>
        /// Gives the vessel to the first player aboard who is in flight on it (command seat as a tie-break).
        /// Returns false when there is nobody who could actually simulate it, so the caller can decide what
        /// "nobody" should mean - which differs between leaving a vessel and leaving the game.
        /// </summary>
        public bool TryHandOverOnDeparture(Guid vesselId, int leavingClientId)
        {
            if (!_roles.TryGetValue(vesselId, out var roles)) return false;
            var candidate = 0;
            if (_crew.TryGetValue(vesselId, out var crew))
            {
                var seatKerbal = crew.CommandSeatOccupant(name =>
                {
                    var avatarOwner = _server.Roster.AvatarOwner(name);
                    var clientId = _server.Roster.OnlineClientIdOf(avatarOwner);
                    return clientId != 0 && clientId != leavingClientId && IsFlying(clientId, vesselId);
                });
                if (seatKerbal != null) candidate = _server.Roster.OnlineClientIdOf(_server.Roster.AvatarOwner(seatKerbal));
            }
            if (candidate == 0)
                foreach (var clientId in roles.Aboard)
                    if (clientId != leavingClientId && IsFlying(clientId, vesselId)) { candidate = clientId; break; }
            if (candidate == 0 || candidate == _server.Authority.OwnerOf(vesselId)) return false;
            _server.Log("Vessel " + vesselId.ToString().Substring(0, 8) + ": #" + leavingClientId + " left it, #" + candidate + " is aboard and flying it, so it takes over");
            _server.Authority.Assign(vesselId, candidate, AuthorityReason.PilotLeft);
            return true;
        }

        // ---- the pilot's own controls ----

        public void HandleSetSharedStick(ClientSession client, ControlSetSharedStickMsg msg)
        {
            if (!_server.Authority.IsOwnedBy(msg.VesselId, client.ClientId)) return;
            if (!_roles.TryGetValue(msg.VesselId, out var roles)) return;
            if (roles.SharedStick == msg.Enabled) return;
            roles.SharedStick = msg.Enabled;
            _server.Log(client.DisplayName + (msg.Enabled ? " shared" : " took back") + " the stick on vessel " + msg.VesselId.ToString().Substring(0, 8));
            Broadcast(roles);
        }

        /// <summary>R2. Refusing tells the sender who still owns it, so their UI does not sit waiting.</summary>
        public void HandleGive(ClientSession client, ControlGiveMsg msg)
        {
            if (!_server.Authority.IsOwnedBy(msg.VesselId, client.ClientId)) return;
            if (msg.ToClientId == client.ClientId) return;
            if (!IsFlying(msg.ToClientId, msg.VesselId))
            {
                _server.Log(client.DisplayName + " tried to give vessel " + msg.VesselId.ToString().Substring(0, 8) + " to #" + msg.ToClientId + ", who is not flying it; refused");
                _server.Authority.Tell(client, msg.VesselId, AuthorityReason.NotInFlight);
                return;
            }
            _server.Log(client.DisplayName + " gave vessel " + msg.VesselId.ToString().Substring(0, 8) + " to #" + msg.ToClientId);
            _server.Authority.Assign(msg.VesselId, msg.ToClientId, AuthorityReason.HandedOver);
        }

        /// <summary>R3.</summary>
        public void HandleRequest(ClientSession client, ControlRequestMsg msg)
        {
            if (!IsFlying(client.ClientId, msg.VesselId)) { _server.Authority.Tell(client, msg.VesselId, AuthorityReason.NotInFlight); return; }
            if (!IsAboard(msg.VesselId, client.ClientId) && !_server.Authority.IsUnowned(msg.VesselId)) return;

            var owner = _server.Authority.OwnerOf(msg.VesselId);
            if (owner == 0 || owner == client.ClientId || !IsFlying(owner, msg.VesselId) || !IsAboard(msg.VesselId, owner))
            {
                _server.Log(client.DisplayName + " asked for vessel " + msg.VesselId.ToString().Substring(0, 8) + "; nobody is flying it, so it is theirs");
                _server.Authority.Assign(msg.VesselId, client.ClientId, AuthorityReason.HandedOver);
                return;
            }
            var target = _server.HandshakenClients.FirstOrDefault(c => c.ClientId == owner);
            if (target == null) return;
            msg.FromClientId = client.ClientId;
            _server.Send(target.Peer, MessageId.ControlRequest, msg, Channel.Control, Delivery.ReliableOrdered);
            _server.Log(client.DisplayName + " asked #" + owner + " for control of vessel " + msg.VesselId.ToString().Substring(0, 8));
        }

        public void HandleDecline(ClientSession client, ControlDeclineMsg msg)
        {
            if (!_server.Authority.IsOwnedBy(msg.VesselId, client.ClientId)) return;
            var target = _server.HandshakenClients.FirstOrDefault(c => c.ClientId == msg.ToClientId);
            if (target == null) return;
            msg.FromClientId = client.ClientId;
            _server.Send(target.Peer, MessageId.ControlDecline, msg, Channel.Control, Delivery.ReliableOrdered);
        }

        private void Broadcast(Roles roles)
        {
            _server.Broadcast(MessageId.VesselRoles, new VesselRolesMsg
            {
                VesselId = roles.VesselId,
                PilotClientId = roles.PilotClientId,
                AboardClientIds = roles.Aboard.ToArray(),
                SharedStick = roles.SharedStick,
            }, Channel.Control, Delivery.ReliableOrdered);
        }

        public void SendRolesTo(ClientSession client)
        {
            foreach (var roles in _roles.Values)
                _server.Send(client.Peer, MessageId.VesselRoles, new VesselRolesMsg { VesselId = roles.VesselId, PilotClientId = roles.PilotClientId, AboardClientIds = roles.Aboard.ToArray(), SharedStick = roles.SharedStick }, Channel.Control, Delivery.ReliableOrdered);
        }

        /// <summary>May this client act on the vessel (as pilot, co-pilot or the physics owner controlling a probe)?</summary>
        public bool MayControl(ClientSession client, Guid vesselId)
        {
            if (_server.Authority.IsOwnedBy(vesselId, client.ClientId)) return true;
            return IsAboard(vesselId, client.ClientId);
        }

        /// <summary>Forwards a control message to the vessel's physics owner (or drops it when unauthorised / no owner).</summary>
        public bool ForwardToOwner<T>(ClientSession from, Guid vesselId, MessageId id, T message, Channel channel, Delivery delivery) where T : INetSerializable
        {
            if (!MayControl(from, vesselId)) return false;
            var owner = _server.Authority.OwnerOf(vesselId);
            if (owner == 0 || owner == from.ClientId) return false;
            var target = _server.HandshakenClients.FirstOrDefault(c => c.ClientId == owner);
            if (target == null) return false;
            _server.Send(target.Peer, id, message, channel, delivery);
            return true;
        }

        /// <summary>
        /// The owner staged, toggled a group or pressed a part button on their own vessel: everyone else aboard
        /// mirrors it on their copy. Without this a co-pilot's rocket never lit its engines or opened its chutes.
        /// </summary>
        public int RelayActionToAboard<T>(ClientSession from, Guid vesselId, MessageId id, T message, Channel channel, Delivery delivery) where T : INetSerializable
        {
            if (!_server.Authority.IsOwnedBy(vesselId, from.ClientId) || !_roles.TryGetValue(vesselId, out var roles)) return 0;
            var sent = 0;
            foreach (var clientId in roles.Aboard)
            {
                if (clientId == from.ClientId) continue;
                var target = _server.HandshakenClients.FirstOrDefault(c => c.ClientId == clientId);
                if (target == null) continue;
                _server.Send(target.Peer, id, message, channel, delivery);
                sent++;
            }
            return sent;
        }

        /// <summary>
        /// Stages, part buttons, action groups and SAS modes go to everyone in flight, not only those aboard: a
        /// player watching from outside the rocket has a loaded copy of it too, and mirroring the action on
        /// that copy is what lets them see the decoupler fire instead of the copy being torn down and rebuilt.
        /// </summary>
        public int RelayActionToFlying<T>(ClientSession from, Guid vesselId, MessageId id, T message, Channel channel, Delivery delivery) where T : INetSerializable
        {
            if (!_server.Authority.IsOwnedBy(vesselId, from.ClientId)) return 0;
            var sent = 0;
            foreach (var target in _server.HandshakenClients)
            {
                if (target.ClientId == from.ClientId) continue;
                var aboard = _roles.TryGetValue(vesselId, out var roles) && roles.Aboard.Contains(target.ClientId);
                var flying = target.Presence.State == PresenceState.InFlight || target.Presence.State == PresenceState.OnEva;
                if (!aboard && !flying) continue;
                _server.Send(target.Peer, id, message, channel, delivery);
                sent++;
            }
            return sent;
        }

        /// <summary>The owner's merged control state goes to everyone else aboard.</summary>
        public void RelayStateToAboard(ClientSession from, Guid vesselId, CtrlInputMsg state)
        {
            if (!_server.Authority.IsOwnedBy(vesselId, from.ClientId) || !_roles.TryGetValue(vesselId, out var roles)) return;
            foreach (var clientId in roles.Aboard)
            {
                if (clientId == from.ClientId) continue;
                var target = _server.HandshakenClients.FirstOrDefault(c => c.ClientId == clientId);
                if (target != null) _server.Send(target.Peer, MessageId.CtrlState, state, Channel.State, Delivery.Sequenced);
            }
        }
    }
}
