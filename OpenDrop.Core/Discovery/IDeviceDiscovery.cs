using OpenDrop.Core.Devices;

namespace OpenDrop.Core.Discovery;

public interface IDeviceDiscovery : IAsyncDisposable
{
    event EventHandler<DeviceDescriptor> DeviceUpserted;
    event EventHandler<string> DeviceExpired; // DeviceId

    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
