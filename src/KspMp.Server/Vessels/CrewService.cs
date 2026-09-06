using System.Linq;
using KspMp.Shared.Protocol;

namespace KspMp.Server.Vessels
{
    /// <summary>
    /// Getting in and out of other people's craft.
    ///
    /// Only the client simulating a vessel may add or remove its crew, so a player who climbs out of a friend's
    /// rocket cannot do it locally: they say what they did and the owner does it. The server checks that the
    /// kerbal really is the sender's avatar - nobody gets to throw somebody else's kerbal out of an airlock - and
    /// forwards it to whoever owns the vessel.
    /// </summary>
    public sealed class CrewService
    {
        private readonly ServerCore _server;

        public CrewService(ServerCore server)
        {
            _server = server;
        }

        public void HandleEva(ClientSession client, CrewEvaMsg msg)
        {
            if (!IsOwnAvatar(client, msg.KerbalName)) { Refuse(client, msg.KerbalName, "leave"); return; }
            msg.FromClientId = client.ClientId;
            if (!Forward(client, msg.FromVesselId, MessageId.CrewEva, msg)) return;
            _server.Log(client.DisplayName + ": " + msg.KerbalName + " left vessel " + msg.FromVesselId.ToString().Substring(0, 8));
        }

        public void HandleBoard(ClientSession client, CrewBoardMsg msg)
        {
            if (!IsOwnAvatar(client, msg.KerbalName)) { Refuse(client, msg.KerbalName, "board"); return; }
            msg.FromClientId = client.ClientId;
            if (!Forward(client, msg.ToVesselId, MessageId.CrewBoard, msg)) return;
            _server.Log(client.DisplayName + ": " + msg.KerbalName + " boarded vessel " + msg.ToVesselId.ToString().Substring(0, 8));
        }

        private bool IsOwnAvatar(ClientSession client, string kerbalName)
        {
            if (string.IsNullOrEmpty(kerbalName)) return false;
            var owner = _server.Roster.AvatarOwner(kerbalName);
            return owner != null && _server.Roster.OnlineClientIdOf(owner) == client.ClientId;
        }

        private void Refuse(ClientSession client, string kerbalName, string what)
        {
            _server.Log(client.DisplayName + " tried to make '" + kerbalName + "' " + what + " a vessel, but that is not their kerbal; ignored");
        }

        /// <summary>
        /// Sends to the vessel's physics owner. Nothing to do when the sender already owns it - they applied it
        /// themselves before telling anyone - or when the vessel is unknown or nobody simulates it.
        /// </summary>
        private bool Forward<T>(ClientSession client, System.Guid vesselId, MessageId id, T msg) where T : LiteNetLib.Utils.INetSerializable
        {
            if (!_server.Vessels.TryGet(vesselId, out _)) return false;
            var owner = _server.Authority.OwnerOf(vesselId);
            if (owner == 0 || owner == client.ClientId) return false;
            var target = _server.HandshakenClients.FirstOrDefault(c => c.ClientId == owner);
            if (target == null) return false;
            _server.Send(target.Peer, id, msg, Channel.Control, Delivery.ReliableOrdered);
            return true;
        }
    }
}
