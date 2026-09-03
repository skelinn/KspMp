using System.Collections.Generic;
using KspMp.Shared.Protocol;
using LiteNetLib.Utils;

namespace KspMp.Systems
{
    /// <summary>Who is online, as told by the server.</summary>
    public sealed class PlayersSystem : SystemBase
    {
        private readonly List<PlayerInfo> _players = new List<PlayerInfo>();

        public PlayersSystem(KspMpAddon addon) : base(addon) { }

        public override string Name => "Players";
        public IReadOnlyList<PlayerInfo> Players => _players;
        public int Count => _players.Count;

        public bool TryGet(int clientId, out PlayerInfo player)
        {
            foreach (var p in _players)
            {
                if (p.ClientId != clientId) continue;
                player = p;
                return true;
            }
            player = default(PlayerInfo);
            return false;
        }

        protected override void OnActivate()
        {
            Net.RegisterHandler(MessageId.PlayerList, OnPlayerList);
            Net.RegisterHandler(MessageId.PlayerJoined, OnPlayerJoined);
            Net.RegisterHandler(MessageId.PlayerLeft, OnPlayerLeft);
        }

        protected override void OnDeactivate()
        {
            Net.UnregisterHandler(MessageId.PlayerList, OnPlayerList);
            Net.UnregisterHandler(MessageId.PlayerJoined, OnPlayerJoined);
            Net.UnregisterHandler(MessageId.PlayerLeft, OnPlayerLeft);
            _players.Clear();
        }

        private void OnPlayerList(NetDataReader body)
        {
            var list = Envelope.Read<PlayerListMsg>(body);
            _players.Clear();
            if (list.Players != null) _players.AddRange(list.Players);
            _players.Sort((a, b) => a.ClientId.CompareTo(b.ClientId));
        }

        private void OnPlayerJoined(NetDataReader body)
        {
            var joined = Envelope.Read<PlayerJoinedMsg>(body).Player;
            _players.RemoveAll(p => p.ClientId == joined.ClientId);
            _players.Add(joined);
            _players.Sort((a, b) => a.ClientId.CompareTo(b.ClientId));
            Log.Info("Player joined: " + joined.Name + " (#" + joined.ClientId + ")");
        }

        private void OnPlayerLeft(NetDataReader body)
        {
            var left = Envelope.Read<PlayerLeftMsg>(body);
            _players.RemoveAll(p => p.ClientId == left.ClientId);
            Log.Info("Player left: " + left.Name + " (#" + left.ClientId + ", " + left.Reason + ")");
        }
    }
}
