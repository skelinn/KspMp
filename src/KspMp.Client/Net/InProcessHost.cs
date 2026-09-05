using System;
using System.Collections.Generic;
using System.IO;
using KspMp.Server;
using KspMp.Server.Universe;
using KspMp.Shared.Protocol;

namespace KspMp.Net
{
    /// <summary>
    /// Runs the server inside KSP, so hosting a game does not mean running a second program.
    ///
    /// The world it serves is the same one the dedicated server keeps, in the same readable format, so a game
    /// started here can be moved to a real server later by copying the folder.
    ///
    /// It listens two ways at once. The UDP socket takes players on the same network - and the host's own
    /// game, which connects to 127.0.0.1 like anybody else, because a host cannot send Steam packets to
    /// itself. Steam takes everyone else, without anyone touching a router. Either can fail to start without
    /// taking the other down.
    /// </summary>
    public sealed class InProcessHost : IDisposable
    {
        private ServerCore _server;
        private CompositeTransport _transport;
        private Steam.SteamP2PTransport _steam;

        public bool Running => _server != null;
        /// <summary>The Steam ID friends need to join, or 0 when hosting is UDP-only.</summary>
        public ulong SteamId { get; private set; }
        public int Port { get; private set; }

        /// <summary>Where a hosted world lives, beside the mod's other per-install state.</summary>
        public static string DefaultUniverseDir =>
            Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "KspMp", "PluginData", "universe");

        /// <summary>
        /// Starts serving. <paramref name="expectedSteamIds"/> are the friends allowed in over Steam: Steam
        /// discards packets from anyone whose session was never accepted, and without a P2PSessionRequest
        /// callback the only way to accept one is to know the ID beforehand.
        /// </summary>
        public bool Start(int port, string password, IEnumerable<ulong> expectedSteamIds, string universeDir = null)
        {
            if (Running) return true;
            try
            {
                var dir = universeDir ?? DefaultUniverseDir;
                var universe = new UniverseStore(dir);
                var config = ServerConfig.Load(dir);
                config.Port = port > 0 ? port : config.Port;
                if (password != null) config.Password = password;
                config.Upnp = false;   // the dedicated host does this; in-process we leave the router alone
                config.Save(dir);

                var transports = new List<INetTransport>
                {
                    new LiteNetLibTransport(new TransportOptions
                    {
                        IsServer = true,
                        Port = config.Port,
                        MaxPeers = config.MaxPlayers + 4,
                    }, m => Log.Info("[host/udp] " + m)),
                };

                if (Steam.SteamP2P.TryInitialise())
                {
                    _steam = new Steam.SteamP2PTransport(true, 0, expectedSteamIds, m => Log.Info("[host/steam] " + m));
                    transports.Add(_steam);
                    SteamId = Steam.SteamP2P.LocalSteamId;
                }
                else
                {
                    Log.Info("Hosting without Steam: " + Steam.SteamP2P.Unavailable);
                }

                _transport = new CompositeTransport(transports, m => Log.Warn("[host] " + m));
                _server = new ServerCore(_transport, config, universe, m => Log.Info("[host] " + m));
                _server.Start();
                Port = config.Port;

                Log.Info("Hosting on UDP " + Port + (SteamId != 0 ? ", and over Steam as " + SteamId : "")
                         + "; world in " + dir);
                return true;
            }
            catch (Exception e)
            {
                Log.Exception("Starting the in-process host", e);
                Stop();
                return false;
            }
        }

        /// <summary>Lets a friend in without restarting the game. False when Steam is not hosting.</summary>
        public bool Allow(ulong steamId) => _steam != null && _steam.Allow(steamId);

        /// <summary>Call once a frame, alongside the client's own polling.</summary>
        public void Poll()
        {
            if (_server == null) return;
            try { _server.Poll(); }
            catch (Exception e) { Log.Exception("Hosted server", e); }
        }

        public void Stop()
        {
            if (_server != null)
            {
                try { _server.Stop(); }
                catch (Exception e) { Log.Exception("Stopping the hosted server", e); }
                _server = null;
            }
            if (_transport != null)
            {
                try { _transport.Dispose(); }
                catch (Exception e) { Log.Exception("Disposing host transports", e); }
                _transport = null;
            }
            _steam = null;
            SteamId = 0;
        }

        public void Dispose() => Stop();
    }
}
