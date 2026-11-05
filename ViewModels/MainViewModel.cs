using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
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

        // ---------- List maintenance ----------

        private void AddOrUpdate(DeviceSnapshot s)
        {
            if (!_byId.TryGetValue(s.Id, out var vm))
            {
                vm = new DeviceViewModel(_bluetooth, s);
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
