using System.Runtime.InteropServices;
using KspMp.Server;
using KspMp.Server.Universe;
using KspMp.Shared.Protocol;

int? portOverride = null;
var universeDir = "universe";
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--port" when i + 1 < args.Length:
            portOverride = int.Parse(args[++i]);
            break;
        case "--universe" when i + 1 < args.Length:
            universeDir = args[++i];
            break;
        case "-h":
        case "--help":
            Console.WriteLine("KspMp dedicated server\n  --universe <dir>   world folder, created if missing (default ./universe)\n  --port <udp port>  overrides port from <universe>/server.cfg (default 7777)");
            return 0;
    }
}

void Log(string message) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");

universeDir = Path.GetFullPath(universeDir);
var universe = new UniverseStore(universeDir);
var config = ServerConfig.Load(universeDir);
if (portOverride.HasValue) config.Port = portOverride.Value;

var transport = new LiteNetLibTransport(new TransportOptions { IsServer = true, Port = config.Port, MaxPeers = config.MaxPlayers + 4 }, Log);
using var server = new ServerCore(transport, config, universe, Log);
server.Start();
Log($"KspMp server {typeof(ServerCore).Assembly.GetName().Version?.ToString(3)} '{config.ServerName}' listening on UDP {config.Port}; universe {universeDir}");
Log("Players connect from the KSP main menu (Multiplayer window). Ctrl+C stops the server and saves.");

var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
// Also stop cleanly on SIGINT/SIGTERM when there is no console (scripts, service managers).
using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, ctx => { ctx.Cancel = true; stop.Cancel(); });
using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => { ctx.Cancel = true; stop.Cancel(); });
while (!stop.IsCancellationRequested)
{
    server.Poll();
    Thread.Sleep(10);
}
server.Stop();
Log("Server stopped, universe saved.");
return 0;
