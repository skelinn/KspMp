using System.Collections.Generic;
using System.Linq;
using KspMp.Shared.Protocol;

namespace KspMp.Server.Services
{
    /// <summary>
    /// One shared timeline: the slowest warp anyone asks for wins, anyone can drop back to 1x, and a client's
    /// altitude limit caps on-rails warp for everyone. With HostControlsWarp only the longest-connected client's
    /// wish counts.
    /// </summary>
    public sealed class WarpService
    {
        private sealed class ClientWish
        {
            public WarpMode Mode;
            public int Desired;
            public int MaxRails = -1;
        }

        private readonly ServerCore _server;
        private readonly Dictionary<int, ClientWish> _wishes = new Dictionary<int, ClientWish>();

        public WarpService(ServerCore server)
        {
            _server = server;
        }

        public WarpMode Mode { get; private set; } = WarpMode.Rails;
        public int RateIndex { get; private set; }
        public float Rate => WarpRates.Rate(Mode, RateIndex);
        public int RequesterClientId { get; private set; }
        public int LimitingClientId { get; private set; }

        public WarpStateMsg Snapshot() => new WarpStateMsg
        {
            Mode = Mode,
            RateIndex = RateIndex,
            Rate = Rate,
            Ut = _server.Time.UniversalTime,
            RequesterClientId = RequesterClientId,
            LimitingClientId = LimitingClientId,
        };

        public void OnRequest(ClientSession client, WarpRequestMsg request)
        {
            if (!_wishes.TryGetValue(client.ClientId, out var wish)) _wishes[client.ClientId] = wish = new ClientWish();
            var changed = wish.Desired != request.DesiredIndex || wish.Mode != request.Mode || wish.MaxRails != request.MaxRailsIndex;
            wish.Mode = request.Mode;
            wish.Desired = request.DesiredIndex;
            wish.MaxRails = request.MaxRailsIndex;
            if (changed && request.DesiredIndex > 0)
                _server.Log(client.DisplayName + " wants " + WarpRates.Rate(request.Mode, request.DesiredIndex) + "x " + request.Mode + " warp");
            else if (changed && request.DesiredIndex == 0 && RateIndex > 0)
                _server.Log(client.DisplayName + " drops warp");
            Recompute(changed);
        }

        public void OnClientLeft(ClientSession client)
        {
            if (_wishes.Remove(client.ClientId)) Recompute(true);
        }

        private void Recompute(bool announce)
        {
            var mode = WarpMode.Rails;
            var index = 0;
            var requester = 0;
            var limiting = 0;

            var hostId = _server.Config.HostControlsWarp ? _server.HandshakenClients.OrderBy(c => c.ClientId).Select(c => c.ClientId).FirstOrDefault() : 0;
            var bestRate = float.MaxValue;
            foreach (var pair in _wishes)
            {
                if (pair.Value.Desired <= 0) continue;
                if (hostId != 0 && pair.Key != hostId) continue;
                var rate = WarpRates.Rate(pair.Value.Mode, pair.Value.Desired);
                if (rate >= bestRate) continue;
                bestRate = rate;
                mode = pair.Value.Mode;
                index = pair.Value.Desired;
                requester = pair.Key;
            }

            if (index > 0 && mode == WarpMode.Rails)
            {
                foreach (var pair in _wishes)
                {
                    if (pair.Value.MaxRails < 0 || pair.Value.MaxRails >= index) continue;
                    index = pair.Value.MaxRails;
                    limiting = pair.Key;
                }
                if (index == 0) requester = 0;
            }

            var changed = mode != Mode || index != RateIndex || requester != RequesterClientId || limiting != LimitingClientId;
            Mode = mode;
            RateIndex = index;
            RequesterClientId = requester;
            LimitingClientId = limiting;
            _server.Time.SetRate(Rate);
            if (changed || announce)
            {
                _server.Log("Warp: " + Rate + "x " + Mode + (requester != 0 ? " requested by #" + requester : "") + (limiting != 0 ? ", limited by #" + limiting : ""));
                _server.Broadcast(MessageId.WarpState, Snapshot(), Channel.Control, Delivery.ReliableOrdered);
            }
        }
    }
}
