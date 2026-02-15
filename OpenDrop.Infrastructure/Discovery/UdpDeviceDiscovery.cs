using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using OpenDrop.Core.Devices;
using OpenDrop.Core.Discovery;
using Microsoft.Extensions.Logging;

namespace OpenDrop.Infrastructure.Discovery;

public sealed class UdpDeviceDiscovery : IDeviceDiscovery
{
    private const int Port = 35353;
    private static readonly IPEndPoint BroadcastV4 = new(IPAddress.Broadcast, Port);

    private readonly ILogger<UdpDeviceDiscovery> _log;
    private readonly DiscoveryOptions _options;
    private readonly TimeSpan _expireAfter = TimeSpan.FromSeconds(8);

    private readonly ConcurrentDictionary<string, DeviceDescriptor> _devices = new(StringComparer.Ordinal);

    private UdpClient? _rx;
    private UdpClient? _tx;
    private CancellationTokenSource? _cts;
    private Task? _rxTask;
    private Task? _txTask;
    private Timer? _expireTimer;

    public event EventHandler<DeviceDescriptor>? DeviceUpserted;
    public event EventHandler<string>? DeviceExpired;

    public UdpDeviceDiscovery(DiscoveryOptions options, ILogger<UdpDeviceDiscovery> log)
    {
        _options = options;
        _log = log;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_cts is not null) return Task.CompletedTask;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _rx = new UdpClient(AddressFamily.InterNetwork);
        _rx.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _rx.EnableBroadcast = true;
        _rx.Client.Bind(new IPEndPoint(IPAddress.Any, Port));

        _tx = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };

        _rxTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));
        _txTask = Task.Run(() => AnnounceLoopAsync(_cts.Token));
        _expireTimer = new Timer(_ => ExpireSweep(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));

        _log.LogInformation("UDP discovery started on port {Port}.", Port);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _expireTimer?.Dispose();
        _expireTimer = null;

        if (_cts is null) return;
        _cts.Cancel();

        try { if (_rxTask is not null) await _rxTask; } catch { }
        try { if (_txTask is not null) await _txTask; } catch { }

        _rx?.Dispose();
        _tx?.Dispose();
        _rx = null;
        _tx = null;

        _cts.Dispose();
        _cts = null;

        _devices.Clear();
    }

    private async Task AnnounceLoopAsync(CancellationToken ct)
    {
        var announce = new Announce
        {
            Id = _options.DeviceId,
            Name = _options.DisplayName,
            Avatar = _options.AvatarPngBase64,
            Caps = _options.Capabilities ?? Array.Empty<string>(),
            Host = GetLocalHostHint(),
            Port = _options.ServicePort
        };

        while (!ct.IsCancellationRequested)
        {
            try
            {
                announce.Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var payload = JsonSerializer.SerializeToUtf8Bytes(announce);
                await _tx!.SendAsync(payload, payload.Length, BroadcastV4);
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Announce failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult res;
            try { res = await _rx!.ReceiveAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _log.LogDebug(ex, "Receive failed."); continue; }

            try
            {
                var announce = JsonSerializer.Deserialize<Announce>(res.Buffer);
                if (announce is null) continue;
                if (string.IsNullOrWhiteSpace(announce.Id)) continue;
                if (string.Equals(announce.Id, _options.DeviceId, StringComparison.Ordinal)) continue;

                var dd = new DeviceDescriptor(
                    DeviceId: announce.Id,
                    DisplayName: announce.Name ?? announce.Id,
                    AvatarPngBase64: announce.Avatar,
                    Capabilities: announce.Caps ?? Array.Empty<string>(),
                    ServiceHost: res.RemoteEndPoint.Address.ToString(),
                    ServicePort: announce.Port,
                    LastSeenUtc: DateTimeOffset.UtcNow);

                _devices.AddOrUpdate(dd.DeviceId, dd, (_, __) => dd);
                DeviceUpserted?.Invoke(this, dd);
            }
            catch
            {
                // Ignore malformed announces.
            }
        }
    }

    private void ExpireSweep()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kv in _devices.ToArray())
        {
            if (now - kv.Value.LastSeenUtc > _expireAfter)
            {
                if (_devices.TryRemove(kv.Key, out _))
                    DeviceExpired?.Invoke(this, kv.Key);
            }
        }
    }

    private static string GetLocalHostHint() => Environment.MachineName;

    public async ValueTask DisposeAsync() => await StopAsync(CancellationToken.None);

    private sealed class Announce
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Avatar { get; set; }
        public string[]? Caps { get; set; }
        public string? Host { get; set; }
        public int Port { get; set; }
        public long Ts { get; set; }
    }
}
