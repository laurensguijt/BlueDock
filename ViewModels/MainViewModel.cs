using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Threading.Tasks;
using System.Windows.Input;
using BlueDock.Services;

namespace BlueDock.ViewModels
{
    public sealed class MainViewModel : ObservableObject
    {
        private readonly BluetoothService _bluetooth;
        private readonly RadioService _radio;
        private readonly SettingsService _settings;
        private readonly Dictionary<string, DeviceViewModel> _byId = new();
        private bool _isSettingsOpen;
        private UpdateInfo? _update;
        private bool _isUpdating;
        private string? _updateStatus;

        public MainViewModel(BluetoothService bluetooth, RadioService radio, SettingsService settings)
        {
            _bluetooth = bluetooth;
            _radio = radio;
            _settings = settings;

            bluetooth.DeviceAdded += (s, d) => OnUi(() => AddOrUpdate(d));
            bluetooth.DeviceUpdated += (s, d) => OnUi(() => AddOrUpdate(d));
            bluetooth.DeviceRemoved += (s, id) => OnUi(() => Remove(id));
            radio.StateChanged += (s, e) => OnUi(() =>
            {
                OnPropertyChanged(nameof(IsRadioOn));
                OnPropertyChanged(nameof(IsRadioAvailable));
                OnPropertyChanged(nameof(Subtitle));
            });

            AddDeviceCommand = new RelayCommand(() =>
                TrayService.OpenUri("ms-settings-connectabledevices:devicediscovery"));
            OpenWindowsSettingsCommand = new RelayCommand(() => TrayService.OpenUri("ms-settings:bluetooth"));
            OpenSettingsCommand = new RelayCommand(() => IsSettingsOpen = true);
            CloseSettingsCommand = new RelayCommand(() => IsSettingsOpen = false);
            InstallUpdateCommand = new AsyncRelayCommand(InstallUpdateAsync, () => _update != null && !_isUpdating);
        }

        // ---------- Device lists ----------

        public ObservableCollection<DeviceViewModel> Connected { get; } = new();
        public ObservableCollection<DeviceViewModel> Paired { get; } = new();

        public bool HasConnected => Connected.Count > 0;
        public bool HasPaired => Paired.Count > 0;
        public bool IsEmpty => Connected.Count == 0 && Paired.Count == 0;

        public string Subtitle
        {
            get
            {
                if (IsRadioAvailable && !IsRadioOn) return "Bluetooth staat uit";
                return Connected.Count switch
                {
                    0 => "Geen apparaten verbonden",
                    1 => "1 apparaat verbonden",
                    var n => $"{n} apparaten verbonden",
                };
            }
        }

        // ---------- Radio ----------

        public bool IsRadioAvailable => _radio.IsAvailable;

        public bool IsRadioOn
        {
            get => _radio.IsOn;
            set { _ = _radio.SetAsync(value); }
        }

        // ---------- Navigation ----------

        public bool IsSettingsOpen
        {
            get => _isSettingsOpen;
            set => Set(ref _isSettingsOpen, value);
        }

        // ---------- Updates ----------

        public AsyncRelayCommand InstallUpdateCommand { get; }

        public bool IsUpdateAvailable => _update != null;

        public string UpdateTitle =>
            _update == null ? "" : $"Versie {_update.Version.ToString(3)} is beschikbaar";

        public string UpdateStatus => _updateStatus ?? (_update == null ? "" : Megabytes(_update.Size));

        public string UpdateActionText => _isUpdating ? "Bezig…" : "Bijwerken";

        /// <summary>
        /// Asks GitHub whether there is something newer. Runs in the background and stays quiet
        /// about failures: an app that cannot reach the internet is not a broken app.
        /// </summary>
        public async Task CheckForUpdateAsync()
        {
            if (!_settings.Current.CheckForUpdates) return;

            var found = await UpdateService.CheckAsync();
            if (found == null) return;

            _update = found;
            OnUi(() =>
            {
                OnPropertyChanged(nameof(IsUpdateAvailable));
                OnPropertyChanged(nameof(UpdateTitle));
                OnPropertyChanged(nameof(UpdateStatus));
                InstallUpdateCommand.RaiseCanExecuteChanged();
            });
        }

        private async Task InstallUpdateAsync()
        {
            var update = _update;
            if (update == null) return;

            _isUpdating = true;
            SetUpdateStatus("Downloaden…");

            var progress = new Progress<int>(p => SetUpdateStatus($"Downloaden… {p}%"));
            bool started = await UpdateService.DownloadAndRunAsync(update, progress);

            if (started)
            {
                // The installer closes this copy itself; leave the panel saying what happens.
                SetUpdateStatus("Installatieprogramma gestart");
                return;
            }

            _isUpdating = false;
            SetUpdateStatus("Downloaden niet gelukt — open de releasepagina");
            InstallUpdateCommand.RaiseCanExecuteChanged();
        }

        private void SetUpdateStatus(string text)
        {
            _updateStatus = text;
            OnUi(() =>
            {
                OnPropertyChanged(nameof(UpdateStatus));
                OnPropertyChanged(nameof(UpdateActionText));
            });
        }

        private static string Megabytes(long bytes) =>
            bytes <= 0 ? "" : $"{bytes / 1048576.0:0.#} MB downloaden";

        public bool CheckForUpdates
        {
            get => _settings.Current.CheckForUpdates;
            set { _settings.Current.CheckForUpdates = value; _settings.Save(); OnPropertyChanged(); }
        }

        public ICommand AddDeviceCommand { get; }
        public ICommand OpenWindowsSettingsCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand CloseSettingsCommand { get; }

        // ---------- Settings ----------

        public IReadOnlyList<KeyValuePair<string, string>> ThemeOptions => ThemeService.Available;

        public string SelectedTheme
        {
            get => _settings.Current.Theme;
            set
            {
                if (value == _settings.Current.Theme) return;
                _settings.Current.Theme = value;
                _settings.Save();
                ThemeService.Apply(value, ThemeMode);
                OnPropertyChanged();
            }
        }

        /// <summary>Light, dark, or follow Windows. Auto is the default on a fresh install.</summary>
        public ThemeMode ThemeMode
        {
            get => ThemeService.ParseMode(_settings.Current.ThemeMode);
            set
            {
                if (value == ThemeMode) return;
                _settings.Current.ThemeMode = value.ToString();
                _settings.Save();
                ThemeService.Apply(_settings.Current.Theme, value);
                OnPropertyChanged();
            }
        }

        public bool NotifyOnConnect
        {
            get => _settings.Current.NotifyOnConnect;
            set { _settings.Current.NotifyOnConnect = value; _settings.Save(); OnPropertyChanged(); }
        }

        public bool NotifyOnDisconnect
        {
            get => _settings.Current.NotifyOnDisconnect;
            set { _settings.Current.NotifyOnDisconnect = value; _settings.Save(); OnPropertyChanged(); }
        }

        public bool NotifyOnLowBattery
        {
            get => _settings.Current.NotifyOnLowBattery;
            set { _settings.Current.NotifyOnLowBattery = value; _settings.Save(); OnPropertyChanged(); }
        }

        /// <summary>Percentages worth warning at; a free number field would only invite typos.</summary>
        public IReadOnlyList<KeyValuePair<int, string>> LowBatteryOptions { get; } = new[]
        {
            new KeyValuePair<int, string>(10, "10%"),
            new KeyValuePair<int, string>(15, "15%"),
            new KeyValuePair<int, string>(20, "20%"),
            new KeyValuePair<int, string>(25, "25%"),
            new KeyValuePair<int, string>(30, "30%"),
        };

        public int LowBatteryThreshold
        {
            get => _settings.Current.LowBatteryThreshold;
            set
            {
                if (value == _settings.Current.LowBatteryThreshold) return;
                _settings.Current.LowBatteryThreshold = value;
                _settings.Save();
                // Cards show "bijna leeg" against this number, so they all have to look again.
                foreach (var device in Connected.Concat(Paired))
                    device.RefreshBatteryThreshold();
                OnPropertyChanged();
            }
        }

        /// <summary>Windows publishes battery levels slowly, so anything under a minute is waste.</summary>
        public IReadOnlyList<KeyValuePair<int, string>> BatteryRefreshOptions { get; } = new[]
        {
            new KeyValuePair<int, string>(60, "elke minuut"),
            new KeyValuePair<int, string>(120, "elke 2 minuten"),
            new KeyValuePair<int, string>(300, "elke 5 minuten"),
            new KeyValuePair<int, string>(600, "elke 10 minuten"),
        };

        public int BatteryRefreshSeconds
        {
            get => _settings.Current.BatteryRefreshSeconds;
            set
            {
                if (value == _settings.Current.BatteryRefreshSeconds) return;
                _settings.Current.BatteryRefreshSeconds = value;
                _settings.Save();
                _bluetooth.SetBatteryInterval(value);
                OnPropertyChanged();
            }
        }

        public bool StartWithWindows
        {
            get
            {
                try { return AutoStart.IsEnabled; } catch { return false; }
            }
            set
            {
                try { AutoStart.IsEnabled = value; } catch { /* registry blocked by policy */ }
                OnPropertyChanged();
            }
        }

        public string Version =>
            "BlueDock " + (typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "");

        /// <summary>
        /// Re-reads which device Windows plays through. Called when the panel opens and after a
        /// switch, since only one device can be the output and the others have to let go.
        /// </summary>
        public void RefreshAudioOutputs()
        {
            foreach (var device in Connected.Concat(Paired))
                _ = device.RefreshAudioOutputAsync();
        }

        // ---------- List maintenance ----------

        private void AddOrUpdate(DeviceSnapshot s)
        {
            if (!_byId.TryGetValue(s.Id, out var vm))
            {
                vm = new DeviceViewModel(_bluetooth, s) { OutputChanged = RefreshAudioOutputs };
                _byId[s.Id] = vm;
            }
            else
            {
                vm.Update(s);
            }

            var target = vm.IsConnected ? Connected : Paired;
            var other = vm.IsConnected ? Paired : Connected;
            other.Remove(vm);
            if (!target.Contains(vm))
                InsertSorted(target, vm);

            RaiseListChanged();
        }

        private void Remove(string id)
        {
            if (!_byId.TryGetValue(id, out var vm)) return;
            _byId.Remove(id);
            Connected.Remove(vm);
            Paired.Remove(vm);
            RaiseListChanged();
        }

        private static void InsertSorted(ObservableCollection<DeviceViewModel> list, DeviceViewModel vm)
        {
            int i = 0;
            while (i < list.Count && string.Compare(list[i].Name, vm.Name, StringComparison.CurrentCultureIgnoreCase) < 0)
                i++;
            list.Insert(i, vm);
        }

        private void RaiseListChanged()
        {
            OnPropertyChanged(nameof(HasConnected));
            OnPropertyChanged(nameof(HasPaired));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(Subtitle));
        }

        private static void OnUi(Action action)
        {
            var d = Application.Current?.Dispatcher;
            if (d == null) return;
            if (d.CheckAccess()) action();
            else d.BeginInvoke(action);
        }
    }
}
