using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
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
    ///
    /// The server runs on its own thread. Polled once a frame it went quiet for every scene load the host
    /// made - twenty seconds on a slow machine - during which nobody else got time, warp, chat or each
    /// other's positions, and a guest's KSP could give the host up for dead. The server touches nothing of
    /// Unity's; only Steam's callback pump stays on the main thread.
    /// </summary>
    public sealed class InProcessHost : IDisposable
    {
        private const int PollIntervalMs = 10;

        private ServerCore _server;
        private CompositeTransport _transport;
        private Steam.SteamP2PTransport _steam;
        private Thread _thread;
        private volatile bool _stopping;
        private readonly object _gate = new object();

        public bool Running => _server != null;
        /// <summary>True when the serving thread died; the main thread then polls in its place.</summary>
        public bool ThreadFaulted { get; private set; }
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

                var udp = new LiteNetLibTransport(new TransportOptions
                {
                    IsServer = true,
                    Port = config.Port,
                    MaxPeers = config.MaxPlayers + 4,
                }, m => Log.Info("[host/udp] " + m));
                var transports = new List<INetTransport> { udp };

                if (Steam.SteamP2P.TryInitialise())
                {
                    _steam = new Steam.SteamP2PTransport(true, 0, expectedSteamIds, m => Log.Info("[host/steam] " + m)) { RunCallbacks = false };
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
                // Our own client joins over this port; a bind that failed (another server on it) was tolerated
                // by the composite transport and left the host "hosting" a game it could not enter itself.
                if (udp.LocalPort == 0) throw new InvalidOperationException("UDP port " + config.Port + " could not be opened - is another KspMp server already using it?");
                Port = udp.LocalPort;
                _stopping = false;
                ThreadFaulted = false;
                _thread = new Thread(Serve) { IsBackground = true, Name = "KspMp host" };
                _thread.Start();

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
        public bool Allow(ulong steamId)
        {
            if (_steam == null) return false;
            lock (_gate) return _steam.Allow(steamId);
        }

        private void Serve()
        {
            try
            {
                while (!_stopping)
                {
                    lock (_gate)
                    {
                        if (_server == null || _stopping) break;
                        try { _server.Poll(); }
                        catch (Exception e) { Log.Exception("Hosted server", e); }
                    }
                    Thread.Sleep(PollIntervalMs);
                }
            }
            catch (Exception e)
            {
                // Not an exception from the server (those are caught above): the thread itself failed. The
                // main thread takes over polling, so the game goes on, slower during scene loads.
                ThreadFaulted = true;
                Log.Exception("The hosting thread stopped; serving from the main thread instead", e);
            }
        }

        /// <summary>Call once a frame: pumps Steam's callbacks, and serves from here only if the thread is gone.</summary>
        public void Poll()
        {
            if (_server == null) return;
            if (_steam != null) Steam.SteamP2P.Poll();
            if (_thread != null && _thread.IsAlive && !ThreadFaulted) return;
            lock (_gate)
            {
                if (_server == null) return;
                try { _server.Poll(); }
                catch (Exception e) { Log.Exception("Hosted server", e); }
            }
        }

        public void Stop()
        {
            _stopping = true;
            if (_thread != null)
            {
                if (_thread.IsAlive && !_thread.Join(3000)) Log.Warn("The hosting thread did not stop in time");
                _thread = null;
            }
            lock (_gate)
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
            }
            SteamId = 0;
        }

        public void Dispose() => Stop();
    }
}
