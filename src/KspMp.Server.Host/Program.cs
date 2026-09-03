using KspMp.Server;
using KspMp.Shared.Protocol;

var port = 7777;
var universe = "universe";
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--port" when i + 1 < args.Length:
            port = int.Parse(args[++i]);
            break;
        case "--universe" when i + 1 < args.Length:
            universe = args[++i];
            break;
        case "-h":
        case "--help":
            Console.WriteLine("KspMp dedicated server\n  --port <udp port>      default 7777\n  --universe <dir>       default ./universe");
            return 0;
    }
}

Directory.CreateDirectory(universe);
void Log(string message) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");

var transport = new LiteNetLibTransport(new TransportOptions { IsServer = true, Port = port }, Log);
using var server = new ServerCore(transport, Log);
server.Start();
Log($"KspMp server {typeof(ServerCore).Assembly.GetName().Version} listening on UDP {port}, universe '{Path.GetFullPath(universe)}'. Ctrl+C to stop.");

var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
while (!stop.IsCancellationRequested)
{
    server.Poll();
    Thread.Sleep(15);
}
server.Stop();
Log("Server stopped.");
return 0;
