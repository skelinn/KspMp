using KspMp.Shared.Protocol;

namespace KspMp.Server.Services
{
    public sealed class ChatService
    {
        public const int MaxLength = 500;

        private readonly ServerCore _server;

        public ChatService(ServerCore server)
        {
            _server = server;
        }

        public void HandleChat(ClientSession from, ChatMsg message)
        {
            var text = (message.Text ?? string.Empty).Trim();
            if (text.Length == 0) return;
            if (text.Length > MaxLength) text = text.Substring(0, MaxLength);
            _server.Log(from.DisplayName + ": " + text);
            _server.Broadcast(MessageId.Chat, new ChatMsg { FromClientId = from.ClientId, FromName = from.PlayerName, Text = text }, Channel.ChatMod, Delivery.ReliableOrdered);
        }

        public void ServerNotice(string text)
        {
            _server.Log("[notice] " + text);
            _server.Broadcast(MessageId.Chat, new ChatMsg { FromClientId = 0, FromName = "Server", Text = text }, Channel.ChatMod, Delivery.ReliableOrdered);
        }
    }
}
