using System;
using System.Linq;
using KspMp.Server.Universe;
using KspMp.Shared.Protocol;

namespace KspMp.Server.Roster
{
    /// <summary>
    /// Roster replication and avatar ownership. Every kerbal is shared; an avatar can only be changed, moved or
    /// removed by the player who owns it. Status is derived from what clients report (the vessel snapshots decide
    /// placement, roster messages decide everything else).
    /// </summary>
    public sealed class RosterService
    {
        private static readonly string[] Traits = { "Pilot", "Engineer", "Scientist" };

        private readonly ServerCore _server;

        public RosterService(ServerCore server, RosterStore store)
        {
            _server = server;
            Store = store;
        }

        public RosterStore Store { get; }

        public KnownPlayer AvatarOwner(string kerbalName)
        {
            if (string.IsNullOrEmpty(kerbalName)) return null;
            return _server.KnownPlayers.FirstOrDefault(p => p.AvatarKerbalName == kerbalName);
        }

        public bool IsAvatar(string kerbalName) => AvatarOwner(kerbalName) != null;

        public int OnlineClientIdOf(KnownPlayer player)
        {
            if (player == null) return 0;
            var client = _server.HandshakenClients.FirstOrDefault(c => c.PlayerId == player.PlayerId);
            return client != null ? client.ClientId : 0;
        }

        public KerbalProtoMsg ToMessage(KerbalRecord record, KerbalReason reason)
        {
            var owner = AvatarOwner(record.Name);
            return new KerbalProtoMsg
            {
                Name = record.Name,
                Reason = reason,
                IsAvatar = owner != null,
                AvatarPlayerId = owner != null ? owner.PlayerId : Guid.Empty,
                AvatarClientId = OnlineClientIdOf(owner),
                NodeText = record.NodeText,
            };
        }

        /// <summary>A client may write a kerbal if it is not an avatar, or if the client owns that avatar.</summary>
        public bool CanWrite(ClientSession client, string kerbalName)
        {
            var owner = AvatarOwner(kerbalName);
            return owner == null || owner.PlayerId == client.PlayerId;
        }

        public void HandleKerbalProto(ClientSession client, KerbalProtoMsg msg)
        {
            if (string.IsNullOrEmpty(msg.Name)) return;
            if (!CanWrite(client, msg.Name))
            {
                _server.Log(client.DisplayName + " tried to change " + msg.Name + ", another player's avatar; ignored");
                return;
            }
            var record = Store.Upsert(msg.Name, msg.NodeText);
            if (msg.Reason != KerbalReason.Bootstrap) _server.Log(client.DisplayName + " updated kerbal " + msg.Name + " (" + msg.Reason + ", " + RosterStore.StatusName(record.Status) + ")");
            _server.Broadcast(MessageId.KerbalProto, ToMessage(record, msg.Reason), Channel.Bulk, Delivery.ReliableOrdered, client.Peer);
        }

        public void HandleKerbalStatus(ClientSession client, KerbalStatusMsg msg)
        {
            if (!CanWrite(client, msg.Name)) return;
            if (!Store.UpdateStatus(msg.Name, msg.Status, msg.InactiveTimeEnd)) return;
            _server.Log(client.DisplayName + ": " + msg.Name + " is now " + RosterStore.StatusName(msg.Status));
            _server.Broadcast(MessageId.KerbalStatus, msg, Channel.Control, Delivery.ReliableOrdered, client.Peer);
        }

        public void HandleKerbalRemoved(ClientSession client, KerbalRemovedMsg msg)
        {
            if (IsAvatar(msg.Name))
            {
                _server.Log(client.DisplayName + " tried to remove avatar " + msg.Name + "; ignored");
                return;
            }
            if (!Store.Remove(msg.Name)) return;
            _server.Log(client.DisplayName + " removed kerbal " + msg.Name);
            _server.Broadcast(MessageId.KerbalRemoved, msg, Channel.Control, Delivery.ReliableOrdered, client.Peer);
        }

        public void HandleAvatarClaim(ClientSession client, AvatarClaimMsg msg)
        {
            var name = (msg.KerbalName ?? string.Empty).Trim();
            var trait = Traits.Contains(msg.Trait) ? msg.Trait : "Pilot";
            string reason = null;
            if (name.Length < 3 || name.Length > 40) reason = "Pick a name between 3 and 40 characters.";
            else if (client.HasAvatar) reason = "You already have an avatar: " + client.AvatarKerbalName;
            else
            {
                var owner = AvatarOwner(name);
                if (owner != null && owner.PlayerId != client.PlayerId) reason = name + " is already another player's avatar.";
                // A Kerbal already crewing a vessel belongs to that mission; becoming them mid-flight would drop
                // this player straight into someone else's rocket.
                else if (Store.TryGet(name, out var record) && record.Status == 1)
                    reason = name + " is already assigned to a vessel. Pick a Kerbal who is at the space center.";
            }
            if (reason != null)
            {
                _server.Send(client.Peer, MessageId.AvatarClaimResult, new AvatarClaimResultMsg { Ok = false, KerbalName = name, Trait = trait, Reason = reason }, Channel.Control, Delivery.ReliableOrdered);
                return;
            }

            _server.SetAvatar(client, name);
            _server.Log(client.DisplayName + " claimed avatar " + name + " (" + trait + ")");
            _server.Send(client.Peer, MessageId.AvatarClaimResult, new AvatarClaimResultMsg { Ok = true, KerbalName = name, Trait = trait, Reason = string.Empty }, Channel.Control, Delivery.ReliableOrdered);
            if (Store.TryGet(name, out var existing))
                _server.Broadcast(MessageId.KerbalProto, ToMessage(existing, KerbalReason.Avatar), Channel.Bulk, Delivery.ReliableOrdered);
            _server.Players.BroadcastList();
        }

        public void Sync(ClientSession client)
        {
            foreach (var record in Store.All)
                _server.Send(client.Peer, MessageId.KerbalProto, ToMessage(record, KerbalReason.Sync), Channel.Bulk, Delivery.ReliableOrdered);
        }
    }
}
