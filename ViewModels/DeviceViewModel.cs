using CommunityToolkit.Mvvm.ComponentModel;

namespace AirDropLike.App.ViewModels;

public sealed partial class DeviceViewModel : ObservableObject
{
    [ObservableProperty] private string deviceId = "";
    [ObservableProperty] private string displayName = "";
    [ObservableProperty] private string serviceHost = "";
    [ObservableProperty] private int servicePort;
    [ObservableProperty] private bool isSelected;

    public Uri BaseUri => new($"http://{ServiceHost}:{ServicePort}");
}
