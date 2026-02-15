using System.Collections.ObjectModel;
using OpenDrop.Core.Discovery;
using OpenDrop.Core.Devices;
using OpenDrop.Infrastructure.Transfer;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace OpenDrop.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IDeviceDiscovery _discovery;
    private readonly SecureFileSender _sender;
    private readonly ILogger<MainViewModel> _log;

    public ObservableCollection<DeviceViewModel> Devices { get; } = new();

    [ObservableProperty] private bool isRunning;
    [ObservableProperty] private string statusText = "Idle";

    public MainViewModel(IDeviceDiscovery discovery, SecureFileSender sender, ILogger<MainViewModel> log)
    {
        _discovery = discovery;
        _sender = sender;
        _log = log;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        if (IsRunning) return;
        await _discovery.StartAsync(ct);
        IsRunning = true;
        StatusText = "Discovering nearby devices…";
    }

    public async Task SendFilesAsync(IEnumerable<string> filePaths, CancellationToken ct)
    {
        var target = Devices.FirstOrDefault(d => d.IsSelected) ?? Devices.FirstOrDefault();
        if (target is null)
        {
            StatusText = "No device selected.";
            return;
        }

        foreach (var path in filePaths)
        {
            try
            {
                StatusText = $"Sending {Path.GetFileName(path)} to {target.DisplayName}…";
                await _sender.SendFileAsync(target.BaseUri, senderDeviceId: Environment.MachineName, filePath: path, cancellationToken: ct);
                StatusText = $"Sent {Path.GetFileName(path)}.";
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Send failed.");
                StatusText = $"Send failed: {ex.Message}";
            }
        }
    }

    public void UpsertDevice(DeviceDescriptor dd)
    {
        var existing = Devices.FirstOrDefault(d => d.DeviceId == dd.DeviceId);
        if (existing is null)
        {
            Devices.Add(new DeviceViewModel
            {
                DeviceId = dd.DeviceId,
                DisplayName = dd.DisplayName,
                ServiceHost = dd.ServiceHost,
                ServicePort = dd.ServicePort
            });
        }
        else
        {
            existing.DisplayName = dd.DisplayName;
            existing.ServiceHost = dd.ServiceHost;
            existing.ServicePort = dd.ServicePort;
        }
    }

    public void ExpireDevice(string deviceId)
    {
        var existing = Devices.FirstOrDefault(d => d.DeviceId == deviceId);
        if (existing is not null)
            Devices.Remove(existing);
    }

    public async ValueTask DisposeAsync()
    {
        await _discovery.DisposeAsync();
    }
}
