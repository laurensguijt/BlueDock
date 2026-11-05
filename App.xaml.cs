using System;
using System.Threading;
using System.Windows;
using BlueDock.Services;
using BlueDock.ViewModels;
using BlueDock.Views;

namespace BlueDock
{
    public partial class App : Application
    {
        private Mutex? _singleInstance;
        private TrayService? _tray;
        private BluetoothService? _bluetooth;
        private PopupWindow? _popup;

        public static SettingsService Settings { get; } = new SettingsService();

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _singleInstance = new Mutex(true, "BlueDock.SingleInstance", out bool isFirst);
            if (!isFirst)
            {
                Shutdown();
                return;
            }

            Settings.Load();
            ThemeService.Apply(Settings.Current.Theme, ThemeService.ParseMode(Settings.Current.ThemeMode));

            _bluetooth = new BluetoothService();
            var radio = new RadioService();
            var vm = new MainViewModel(_bluetooth, radio, Settings);

            _popup = new PopupWindow(vm);
            _tray = new TrayService(_popup, ExitApp);

            _bluetooth.ConnectionChanged += (s, args) =>
            {
                var cfg = Settings.Current;
                if (args.IsConnected && cfg.NotifyOnConnect)
                    _tray.Notify("Verbonden", args.Name, args.Kind);
                else if (!args.IsConnected && cfg.NotifyOnDisconnect)
                    _tray.Notify("Verbinding verbroken", args.Name, args.Kind);
            };

            _bluetooth.LowBattery += (s, args) =>
            {
                if (Settings.Current.NotifyOnLowBattery)
                    _tray.Notify("Accu bijna leeg", $"{args.Name}: {args.Percent}%", args.Kind);
            };

            ToastService.LogRoute();
            _bluetooth.Start();
            _ = radio.InitializeAsync();
        }

        private void ExitApp()
        {
            ToastService.Clear();
            _bluetooth?.Stop();
            _tray?.Dispose();
            _popup?.ForceClose();
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _singleInstance?.Dispose();
            base.OnExit(e);
        }
    }
}
