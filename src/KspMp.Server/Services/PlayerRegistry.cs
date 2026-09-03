using System.Linq;
using KspMp.Shared.Protocol;

namespace KspMp.Server.Services
{
    /// <summary>Tells everyone who is online: PlayerList after Welcome (and periodically for pings), PlayerJoined/PlayerLeft on change.</summary>
    public sealed class PlayerRegistry
    {
        private readonly ServerCore _server;

        public PlayerRegistry(ServerCore server)
        {
            _server = server;
        }

        public PlayerInfo ToInfo(ClientSession client) => new PlayerInfo
        {
            ClientId = client.ClientId,
            PlayerId = client.PlayerId,
            Name = client.PlayerName,
            PingMs = _server.Transport.GetPeerPingMs(client.Peer),
            AvatarKerbalName = client.AvatarKerbalName,
        };

        public PlayerListMsg BuildList() => new PlayerListMsg { Players = _server.HandshakenClients.Select(ToInfo).ToArray() };

        public void OnJoined(ClientSession client)
        {
            _server.Send(client.Peer, MessageId.PlayerList, BuildList(), Channel.Control, Delivery.ReliableOrdered);
            _server.Broadcast(MessageId.PlayerJoined, new PlayerJoinedMsg { Player = ToInfo(client) }, Channel.Control, Delivery.ReliableOrdered, client.Peer);
        }

        public void OnLeft(ClientSession client, string reason)
        {
            _server.Broadcast(MessageId.PlayerLeft, new PlayerLeftMsg { ClientId = client.ClientId, Name = client.PlayerName, Reason = reason }, Channel.Control, Delivery.ReliableOrdered);
        }

        public void BroadcastList()
        {
            if (!_server.HandshakenClients.Any()) return;
            _server.Broadcast(MessageId.PlayerList, BuildList(), Channel.Control, Delivery.ReliableOrdered);
        }
    }
}
