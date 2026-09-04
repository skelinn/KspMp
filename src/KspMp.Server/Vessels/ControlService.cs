using System;
using System.Collections.Generic;
using System.Linq;
using KspMp.Shared.Protocol;
using LiteNetLib.Utils;

namespace KspMp.Server.Vessels
{
    /// <summary>
    /// Seat-based control. From each vessel snapshot the server knows which avatars are aboard and who holds the
    /// command seat: that player is the Pilot, everyone else aboard is a Co-pilot, and physics authority follows the
    /// pilot. Control input and discrete actions from people aboard are forwarded to the physics owner.
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
        }

        private readonly ServerCore _server;
        private readonly Dictionary<Guid, Roles> _roles = new Dictionary<Guid, Roles>();

        public ControlService(ServerCore server)
        {
            _server = server;
        }

        public bool TryGetRoles(Guid vesselId, out Roles roles) => _roles.TryGetValue(vesselId, out roles);
        public int PilotOf(Guid vesselId) => _roles.TryGetValue(vesselId, out var r) ? r.PilotClientId : 0;
        public bool IsAboard(Guid vesselId, int clientId) => _roles.TryGetValue(vesselId, out var r) && r.Aboard.Contains(clientId);

        /// <summary>Recomputes roles for a vessel from its snapshot; assigns authority to the pilot and announces changes.</summary>
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
            Recompute(record.Id, crew);
        }

        public void OnVesselRemoved(Guid vesselId)
        {
            if (_roles.Remove(vesselId)) Broadcast(new Roles { VesselId = vesselId });
        }

        /// <summary>Client ids change on reconnect: refresh every vessel that has this player's avatar aboard.</summary>
        public void OnClientsChanged()
        {
            foreach (var record in _server.Vessels.All.ToList()) OnVesselSnapshot(record);
        }

        private void Recompute(Guid vesselId, VesselCrewInfo crew)
        {
            var roles = new Roles { VesselId = vesselId };
            foreach (var name in crew.AllCrew())
            {
                var owner = _server.Roster.AvatarOwner(name);
                if (owner == null) continue;
                var clientId = _server.Roster.OnlineClientIdOf(owner);
                roles.AboardKerbals.Add(name);
                if (clientId != 0 && !roles.Aboard.Contains(clientId)) roles.Aboard.Add(clientId);
            }
            var pilotKerbal = crew.CommandSeatOccupant(name => _server.Roster.OnlineClientIdOf(_server.Roster.AvatarOwner(name)) != 0);
            if (pilotKerbal != null)
            {
                roles.PilotKerbal = pilotKerbal;
                roles.PilotClientId = _server.Roster.OnlineClientIdOf(_server.Roster.AvatarOwner(pilotKerbal));
            }

            var changed = !_roles.TryGetValue(vesselId, out var old) || old.PilotClientId != roles.PilotClientId || !old.Aboard.SequenceEqual(roles.Aboard);
            _roles[vesselId] = roles;
            if (changed)
            {
                _server.Log("Roles for vessel " + vesselId.ToString().Substring(0, 8) + ": pilot " + (roles.PilotClientId != 0 ? "#" + roles.PilotClientId + " (" + roles.PilotKerbal + ")" : "none") + ", aboard [" + string.Join(", ", roles.Aboard.Select(c => "#" + c)) + "]");
                Broadcast(roles);
            }
            // The pilot simulates the vessel, unless a docking approach temporarily moved it under the other owner.
            if (roles.PilotClientId != 0 && _server.Authority.OwnerOf(vesselId) != roles.PilotClientId && !_server.Authority.IsDockingHeld(vesselId))
                _server.Authority.Assign(vesselId, roles.PilotClientId, AuthorityReason.Granted);
        }

        private void Broadcast(Roles roles)
        {
            _server.Broadcast(MessageId.VesselRoles, new VesselRolesMsg
            {
                VesselId = roles.VesselId,
                PilotClientId = roles.PilotClientId,
                AboardClientIds = roles.Aboard.ToArray(),
                SharedStick = _server.Config.SharedStickDefault,
            }, Channel.Control, Delivery.ReliableOrdered);
        }

        public void SendRolesTo(ClientSession client)
        {
            foreach (var roles in _roles.Values)
                _server.Send(client.Peer, MessageId.VesselRoles, new VesselRolesMsg { VesselId = roles.VesselId, PilotClientId = roles.PilotClientId, AboardClientIds = roles.Aboard.ToArray(), SharedStick = _server.Config.SharedStickDefault }, Channel.Control, Delivery.ReliableOrdered);
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
