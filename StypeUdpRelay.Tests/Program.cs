using System.Net;
using System.Net.Sockets;
using StypeUdpRelay;
using System.Text.Json;

try { await Run(); }
catch (Exception ex) { Console.Error.WriteLine($"FAIL: {ex.Message}"); Environment.ExitCode = 1; }

static async Task Run()
{
static UdpClient Receiver() => new(new IPEndPoint(IPAddress.Loopback, 0));
static int Port(UdpClient c) => ((IPEndPoint)c.Client.LocalEndPoint!).Port;
static void Require(bool value, string message) { if (!value) throw new Exception(message); }
using var a = Receiver();
using var b = Receiver();
using var c = Receiver();
using var reserve = Receiver();
int input = Port(reserve); reserve.Dispose();
var config = new RelayConfig { ListenAddress = "127.0.0.1", ListenPort = input, Targets = [new("127.0.0.1", Port(a)), new("127.0.0.1", Port(b)), new("127.0.0.1", Port(c)), new("127.0.0.1", Port(a))] };
foreach (var targets in new List<Target>[] { [new("127.0.0.1", input)], [], [new("not-an-ip", 1234)], [new("127.0.0.1", 0)] })
{
    try { using var bad = new Relay(new RelayConfig { ListenAddress = "127.0.0.1", ListenPort = input, Targets = targets }); throw new Exception("Invalid config accepted"); }
    catch (ArgumentException) { }
}
Console.WriteLine("PASS: self-loop, empty targets, invalid IPv4 and invalid ports rejected.");
var oldTarget = JsonSerializer.Deserialize<Target>("{\"Address\":\"127.0.0.1\",\"Port\":6301}")!;
Require(oldTarget.Enabled, "Legacy settings must keep forwarding enabled");
var disabledTarget = JsonSerializer.Deserialize<Target>(JsonSerializer.Serialize(oldTarget with { Enabled = false }))!;
Require(!disabledTarget.Enabled, "Disabled state not preserved");
using (var switching = new Relay(config))
{
    Require(switching.ActiveTargetCount == 3, "Initial targets");
    switching.SetTargetEnabled(1, false);
    Require(switching.ActiveTargetCount == 2, "Disabling second target affected wrong count");
    switching.SetTargetEnabled(0, false);
    Require(switching.ActiveTargetCount == 2, "Enabled duplicate should keep destination active");
    switching.SetTargetEnabled(3, false);
    switching.SetTargetEnabled(2, false);
    Require(switching.ActiveTargetCount == 0, "All destinations off");
    switching.SetTargetEnabled(1, true);
    Require(switching.ActiveTargetCount == 1, "Re-enabling destination failed");
}
Console.WriteLine("PASS: target switches, independent routes, all-off, duplicates and old/new JSON settings.");
using (var baselineReceiver = Receiver())
using (var baselineSender = new UdpClient())
{
    await baselineSender.SendAsync(new byte[] { 42 }, new IPEndPoint(IPAddress.Loopback, Port(baselineReceiver)));
    using var timeout = new CancellationTokenSource(1500);
    try { await baselineReceiver.ReceiveAsync(timeout.Token); }
    catch (OperationCanceledException)
    {
        Console.WriteLine("BLOCKED: independent UDP loopback baseline failed; network tests cannot run in this environment.");
        Environment.ExitCode = 2;
        return;
    }
}
using (var relay = new Relay(config))
{
    relay.Start();
    using var sender = new UdpClient();
    foreach (int size in new[] { 29, 256, 1400, 60000 })
    {
        var bytes = new byte[size]; Random.Shared.NextBytes(bytes);
        await sender.SendAsync(bytes, new IPEndPoint(IPAddress.Loopback, input));
        Console.WriteLine($"Testing {size} bytes; input {input}");
        foreach (var receiver in new[] { a, b, c })
        {
            using var timeout = new CancellationTokenSource(3000);
            UdpReceiveResult copy;
            try { copy = await receiver.ReceiveAsync(timeout.Token); }
            catch { Console.WriteLine($"Receive timeout: target={Port(receiver)}, input={relay.Received}, sent={relay.Sent}, error={relay.LastError}, running={relay.Running}"); throw; }
            Require(copy.Buffer.SequenceEqual(bytes), "Payload differs");
            Require(copy.RemoteEndPoint.Port == input, "Wrong source port");
        }
    }
    Require(relay.Received == 4 && relay.Sent == 12 && relay.Errors == 0, "Counters or destination deduplication failed");
    Console.WriteLine("PASS: 3 destinations, byte-exact payloads (29 bytes to 60 KB), source port, counters, deduplication.");
    relay.SetTargetEnabled(1, false);
    Require(relay.Running, "Switch stopped receiver");
    byte[] switched = [1, 2, 3];
    await sender.SendAsync(switched, new IPEndPoint(IPAddress.Loopback, input));
    foreach (var receiver in new[] { a, c })
    {
        using var timeout = new CancellationTokenSource(3000);
        Require((await receiver.ReceiveAsync(timeout.Token)).Buffer.SequenceEqual(switched), "Enabled destination stopped");
    }
    using (var timeout = new CancellationTokenSource(200))
    {
        try { await b.ReceiveAsync(timeout.Token); throw new Exception("Disabled destination received data"); }
        catch (OperationCanceledException) { }
    }
    relay.SetTargetEnabled(1, true);
    await sender.SendAsync(switched, new IPEndPoint(IPAddress.Loopback, input));
    foreach (var receiver in new[] { a, b, c })
    {
        using var timeout = new CancellationTokenSource(3000);
        Require((await receiver.ReceiveAsync(timeout.Token)).Buffer.SequenceEqual(switched), "Destination did not resume");
    }
    Console.WriteLine("PASS: live disable/re-enable of second destination keeps first destination running.");
    try { using var conflict = new Relay(config); throw new Exception("Duplicate bind accepted"); }
    catch (SocketException) { Console.WriteLine("PASS: exclusive input-port ownership."); }
    // A missing local destination generates ICMP; live destinations must keep receiving.
    c.Dispose();
    for (int i = 0; i < 20; i++)
    {
        byte[] payload = BitConverter.GetBytes(i);
        await sender.SendAsync(payload, new IPEndPoint(IPAddress.Loopback, input));
        foreach (var receiver in new[] { a, b })
        {
            using var timeout = new CancellationTokenSource(3000);
            Require((await receiver.ReceiveAsync(timeout.Token)).Buffer.SequenceEqual(payload), "Absent target disrupted forwarding");
        }
        await Task.Delay(10);
    }
    Require(relay.Running, "Stopped after ICMP");
    Console.WriteLine("PASS: absent destination does not stop the relay.");
}
using (var restarted = new Relay(config)) { restarted.Start(); }
Console.WriteLine("PASS: stop releases input port for restart.");
}
