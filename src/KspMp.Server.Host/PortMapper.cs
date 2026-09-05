using System.Net;
using Mono.Nat;

namespace KspMp.Server.Host;

/// <summary>
/// Asks the router to forward the server's UDP port, so hosting from home does not mean logging into a
/// router first. Every part of this is best-effort: plenty of routers have UPnP switched off, and an ISP
/// that puts you behind carrier-grade NAT cannot be helped by it at all. A failure here is worth one line
/// of explanation and nothing more - the server still works for anyone who forwarded the port by hand, or
/// who is on the same network or a VPN.
/// </summary>
internal sealed class PortMapper : IAsyncDisposable
{
    private readonly int _port;
    private readonly Action<string> _log;
    private readonly List<(INatDevice Device, Mapping Mapping)> _mapped = new();
    private readonly TaskCompletionSource _firstDevice = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _started;

    public PortMapper(int port, Action<string> log)
    {
        _port = port;
        _log = log;
    }

    /// <summary>Discovers a router and maps the port, giving up quietly after <paramref name="timeout"/>.</summary>
    public async Task TryMapAsync(TimeSpan timeout)
    {
        _started = true;
        NatUtility.DeviceFound += OnDeviceFound;
        try
        {
            NatUtility.StartDiscovery();
            await Task.WhenAny(_firstDevice.Task, Task.Delay(timeout));
            if (_mapped.Count == 0)
                _log("No router answered UPnP within " + (int)timeout.TotalSeconds + "s. If your friends cannot reach you, "
                     + "forward UDP " + _port + " by hand, or put both machines on the same VPN.");
        }
        catch (Exception e)
        {
            _log("UPnP failed (" + e.GetType().Name + ": " + e.Message + "). Forward UDP " + _port + " by hand if needed.");
        }
        finally
        {
            NatUtility.StopDiscovery();
        }
    }

    private async void OnDeviceFound(object sender, DeviceEventArgs e)
    {
        try
        {
            var mapping = new Mapping(Protocol.Udp, _port, _port, 0, "KspMp");
            await e.Device.CreatePortMapAsync(mapping);
            lock (_mapped) _mapped.Add((e.Device, mapping));

            IPAddress external = null;
            try { external = await e.Device.GetExternalIPAsync(); }
            catch { /* the mapping is what matters; the address is only for the log line */ }

            if (external == null)
                _log("UPnP: router forwarded UDP " + _port + ", but would not say what your public address is. "
                     + "Check it at whatismyip.com and give friends that address with :" + _port + ".");
            else if (IsPrivate(external))
                // A private address here means the device that answered is not the edge router: there is another
                // NAT above it, either a second router of your own or the ISP's. The mapping is real but useless
                // from outside, and saying "connect to 192.168.x.x" would send everyone down a dead end.
                _log("UPnP: a router forwarded UDP " + _port + ", but it reports its own address as " + external
                     + ", which is a private one. That means there is another router or your ISP's NAT above it, "
                     + "so this mapping does not open you up to the internet. Forward the port on the outer router "
                     + "too, or put both machines on a VPN, or run the server somewhere with a public address.");
            else
                _log("UPnP: router forwarded UDP " + _port + ". Friends connect to " + external + ":" + _port + ".");
            _firstDevice.TrySetResult();
        }
        catch (Exception ex)
        {
            _log("UPnP: the router refused to forward UDP " + _port + " (" + ex.GetType().Name + "). Forward it by hand if needed.");
            _firstDevice.TrySetResult();
        }
    }

    /// <summary>
    /// Addresses that cannot be reached from the internet: the RFC1918 ranges, the carrier-grade NAT range an
    /// ISP hands out when it has run out of real addresses, and loopback and link-local.
    /// </summary>
    private static bool IsPrivate(IPAddress address)
    {
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
        var b = address.GetAddressBytes();
        return b[0] == 10
               || b[0] == 127
               || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
               || (b[0] == 192 && b[1] == 168)
               || (b[0] == 169 && b[1] == 254)
               || (b[0] == 100 && b[1] >= 64 && b[1] <= 127);
    }

    /// <summary>Hands the port back, so a router is not left holding a mapping for a server that has stopped.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_started) NatUtility.DeviceFound -= OnDeviceFound;
        List<(INatDevice Device, Mapping Mapping)> mapped;
        lock (_mapped) { mapped = new List<(INatDevice, Mapping)>(_mapped); _mapped.Clear(); }
        foreach (var (device, mapping) in mapped)
        {
            try { await device.DeletePortMapAsync(mapping); }
            catch { /* the router drops it on its own soon enough */ }
        }
        if (mapped.Count > 0) _log("UPnP: released the port mapping.");
    }
}
