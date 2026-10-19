using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace StypeUdpRelay;

public sealed record Target(string Address, int Port, bool Enabled = true);
public sealed class RelayConfig
{
    public string ListenAddress { get; set; } = "0.0.0.0";
    public int ListenPort { get; set; } = 6299;
    public bool AutoStart { get; set; }
    public List<Target> Targets { get; set; } = [new("10.97.17.39", 6301), new("10.97.17.48", 6301)];
}

public sealed class Relay : IDisposable
{
    private readonly Socket socket;
    private readonly IPEndPoint[] allTargets;
    private readonly bool[] enabled;
    private readonly object targetLock = new();
    private IPEndPoint[] targets;
    private readonly CancellationTokenSource cancel = new();
    private Task? worker;
    private long received, sent, errors, lastTicks;
    private string lastSender = "—", lastError = "";
    public long Received => Interlocked.Read(ref received);
    public long Sent => Interlocked.Read(ref sent);
    public long Errors => Interlocked.Read(ref errors);
    public DateTime? LastPacket => Interlocked.Read(ref lastTicks) is var t && t > 0 ? new DateTime(t, DateTimeKind.Utc) : null;
    public string LastSender => Volatile.Read(ref lastSender);
    public string LastError => Volatile.Read(ref lastError);
    public bool Running => worker is { IsCompleted: false };
    public int ActiveTargetCount => Volatile.Read(ref targets).Length;

    public void SetTargetEnabled(int index, bool value)
    {
        lock (targetLock)
        {
            if ((uint)index >= (uint)enabled.Length) throw new ArgumentOutOfRangeException(nameof(index));
            enabled[index] = value;
            // Publish one complete routing snapshot; the receiving socket stays open.
            Volatile.Write(ref targets, allTargets.Where((_, i) => enabled[i]).Distinct().ToArray());
        }
    }

    public static IPAddress ParseAddress(string address)
    {
        if (!IPAddress.TryParse(address, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
            throw new ArgumentException($"Invalid IPv4 address: {address}");
        return ip;
    }

    public Relay(RelayConfig config)
    {
        static void CheckPort(int port) { if (port < 1 || port > 65535) throw new ArgumentException("Port must be 1–65535."); }
        CheckPort(config.ListenPort);
        var listen = ParseAddress(config.ListenAddress);
        if (config.Targets.Count == 0) throw new ArgumentException("Add at least one destination.");
        var local = NetworkInterface.GetAllNetworkInterfaces().SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => a.Address).ToHashSet();
        allTargets = config.Targets.Select(t =>
        {
            CheckPort(t.Port);
            var ip = ParseAddress(t.Address);
            if (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.Broadcast) || ip.GetAddressBytes()[0] >= 224)
                throw new ArgumentException("Use a unicast destination IPv4 address.");
            if (t.Port == config.ListenPort && (ip.Equals(listen) || IPAddress.IsLoopback(ip) || local.Contains(ip)))
                throw new ArgumentException("Destination points back to this receiver. Use a different destination port.");
            return new IPEndPoint(ip, t.Port);
        }).ToArray();
        enabled = config.Targets.Select(t => t.Enabled).ToArray();
        targets = allTargets.Where((_, i) => enabled[i]).Distinct().ToArray();
        socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            socket.ExclusiveAddressUse = true;
            socket.ReceiveBufferSize = 1024 * 1024;
            // Ignore ICMP port-unreachable notifications; one absent destination must not stop receiving.
            socket.IOControl((IOControlCode)unchecked((int)0x9800000C), new byte[4], null);
            socket.Bind(new IPEndPoint(listen, config.ListenPort));
        }
        catch { socket.Dispose(); cancel.Dispose(); throw; }
    }

    public void Start()
    {
        if (worker != null) throw new InvalidOperationException("Relay already started.");
        worker = RunAsync();
    }

    private async Task RunAsync()
    {
        byte[] buffer = new byte[65535];
        EndPoint any = new IPEndPoint(IPAddress.Any, 0);
        while (!cancel.IsCancellationRequested)
        {
            try
            {
                var packet = await socket.ReceiveFromAsync(buffer.AsMemory(), SocketFlags.None, any, cancel.Token).ConfigureAwait(false);
                Interlocked.Increment(ref received);
                Interlocked.Exchange(ref lastTicks, DateTime.UtcNow.Ticks);
                Volatile.Write(ref lastSender, packet.RemoteEndPoint.ToString() ?? "—");
                foreach (var target in Volatile.Read(ref targets))
                {
                    try
                    {
                        socket.SendTo(buffer, 0, packet.ReceivedBytes, SocketFlags.None, target);
                        Interlocked.Increment(ref sent);
                    }
                    catch (SocketException ex) { Interlocked.Increment(ref errors); Volatile.Write(ref lastError, $"{target}: {ex.SocketErrorCode}"); }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException ex)
            {
                if (cancel.IsCancellationRequested) break;
                Interlocked.Increment(ref errors);
                Volatile.Write(ref lastError, $"Receive stopped: {ex.SocketErrorCode}");
                break;
            }
        }
    }

    public void Dispose()
    {
        cancel.Cancel();
        socket.Dispose();
        worker?.GetAwaiter().GetResult();
        cancel.Dispose();
    }
}
