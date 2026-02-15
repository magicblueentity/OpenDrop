using OpenDrop.App.ViewModels;
using OpenDrop.Core.Discovery;
using OpenDrop.Core.Devices;
using Microsoft.UI.Dispatching;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace OpenDrop.App.Views
{
    /// <summary>
    /// A simple page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public partial class MainPage : Page
    {
        private readonly MainViewModel _vm;
        private readonly IDeviceDiscovery _discovery;
        private readonly DispatcherQueue _dq;

        public MainPage()
        {
            this.InitializeComponent();
            _dq = DispatcherQueue.GetForCurrentThread();

            _vm = App.GetService<MainViewModel>();
            _discovery = App.GetService<IDeviceDiscovery>();

            DataContext = _vm;

            _discovery.DeviceUpserted += DiscoveryOnDeviceUpserted;
            _discovery.DeviceExpired += DiscoveryOnDeviceExpired;

            Loaded += async (_, __) => await _vm.StartAsync(CancellationToken.None);
            Unloaded += async (_, __) =>
            {
                _discovery.DeviceUpserted -= DiscoveryOnDeviceUpserted;
                _discovery.DeviceExpired -= DiscoveryOnDeviceExpired;
                await _vm.DisposeAsync();
            };
        }

        private void DiscoveryOnDeviceUpserted(object? sender, DeviceDescriptor dd)
        {
            _dq.TryEnqueue(() => _vm.UpsertDevice(dd));
        }

        private void DiscoveryOnDeviceExpired(object? sender, string deviceId)
        {
            _dq.TryEnqueue(() => _vm.ExpireDevice(deviceId));
        }

        private void OnDeviceItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is not DeviceViewModel clicked) return;
            foreach (var d in _vm.Devices)
                d.IsSelected = d == clicked;
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }

        private async void OnDrop(object sender, DragEventArgs e)
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
            var items = await e.DataView.GetStorageItemsAsync();
            var files = items.OfType<StorageFile>().Select(f => f.Path).ToArray();
            if (files.Length == 0) return;
            await _vm.SendFilesAsync(files, CancellationToken.None);
        }
    }
}
