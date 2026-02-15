using OpenDrop.App.ViewModels;
using OpenDrop.Core.Crypto;
using OpenDrop.Core.Discovery;
using OpenDrop.Core.Transfer;
using OpenDrop.Infrastructure.Crypto;
using OpenDrop.Infrastructure.Discovery;
using OpenDrop.Infrastructure.Transfer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Navigation;
using Serilog;
using Serilog.Extensions.Logging;
using OpenDrop.App.Views;

namespace OpenDrop.App
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window window = Window.Current;
        private IHost? _host;
        private TransferServer? _transferServer;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();
            _host = CreateHost();
        }

        /// <summary>
        /// Invoked when the application is launched normally by the end user.  Other entry points
        /// will be used such as when the application is launched to open a specific file.
        /// </summary>
        /// <param name="e">Details about the launch request and process.</param>
        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            window ??= new Window();

            if (window.Content is not Frame rootFrame)
            {
                rootFrame = new Frame();
                rootFrame.NavigationFailed += OnNavigationFailed;
                window.Content = rootFrame;
            }

            _ = rootFrame.Navigate(typeof(MainPage), e.Arguments);
            window.Activate();

            _ = StartBackgroundAsync();
        }

        /// <summary>
        /// Invoked when Navigation to a certain page fails
        /// </summary>
        /// <param name="sender">The Frame which failed navigation</param>
        /// <param name="e">Details about the navigation failure</param>
        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }

        public static T GetService<T>() where T : notnull
        {
            var app = (App)Current;
            return app._host!.Services.GetRequiredService<T>();
        }

        private static IHost CreateHost()
        {
            var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenDrop", "Logs");
            Directory.CreateDirectory(logDir);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(Path.Combine(logDir, "app-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
                .CreateLogger();

            var host = Host.CreateDefaultBuilder()
                .ConfigureLogging(lb =>
                {
                    lb.ClearProviders();
                    lb.AddProvider(new SerilogLoggerProvider(Log.Logger, dispose: false));
                })
                .ConfigureServices(services =>
                {
                    services.AddSingleton<IKex, EcdhP256Kex>();
                    services.AddSingleton<IKeyDerivation, HkdfSha256Deriver>();
                    services.AddSingleton<IAead, AesGcmAead>();

                    var port = 8777;
                    var incoming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenDrop", "Incoming");
                    var tsOptions = new TransferServerOptions(
                        BindAddress: "0.0.0.0",
                        Port: port,
                        StorageDirectory: incoming,
                        MaxFileSizeBytes: 2L * 1024 * 1024 * 1024, // 2 GiB
                        AllowedExtensions: Array.Empty<string>());

                    services.AddSingleton(tsOptions);
                    services.AddSingleton(sp =>
                        new TransferServer(
                            sp.GetRequiredService<TransferServerOptions>(),
                            sp.GetRequiredService<IKex>(),
                            sp.GetRequiredService<IKeyDerivation>(),
                            sp.GetRequiredService<IAead>(),
                            sp.GetRequiredService<ILogger<TransferServer>>()));

                    services.AddSingleton<IDeviceDiscovery>(sp =>
                    {
                        var opts = new DiscoveryOptions(
                            DeviceId: GetOrCreateDeviceId(),
                            DisplayName: Environment.MachineName,
                            AvatarPngBase64: null,
                            Capabilities: new[] { "send", "receive" },
                            ServicePort: port);
                        return new UdpDeviceDiscovery(opts, sp.GetRequiredService<ILogger<UdpDeviceDiscovery>>());
                    });

                    services.AddHttpClient<TransferHttpClient>();
                    services.AddTransient<ITransferClient>(sp => sp.GetRequiredService<TransferHttpClient>());
                    services.AddTransient<SecureFileSender>();

                    services.AddSingleton<MainViewModel>();
                })
                .Build();

            return host;
        }

        private static string GetOrCreateDeviceId()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenDrop");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "deviceid.txt");
            if (File.Exists(path))
                return File.ReadAllText(path).Trim();

            var id = Guid.NewGuid().ToString("n");
            File.WriteAllText(path, id);
            return id;
        }

        private async Task StartBackgroundAsync()
        {
            if (_host is null) return;
            await _host.StartAsync();

            _transferServer = _host.Services.GetRequiredService<TransferServer>();
            await _transferServer.StartAsync(CancellationToken.None);
        }
    }
}
