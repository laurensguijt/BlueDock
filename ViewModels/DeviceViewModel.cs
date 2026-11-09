using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using BlueDock.Services;

namespace BlueDock.ViewModels
{
    public sealed class DeviceViewModel : ObservableObject
    {
        /// <summary>How long a failure message stays on the card before the normal status returns.</summary>
        private const int MessageSeconds = 6;

        private readonly BluetoothService _bluetooth;
        private string _name = "";
        private bool _isConnected;
        private DeviceKind _kind;
        private int? _battery;
        private ConnectAbility _ability;
        private bool _isBusy;
        private string? _message;
        private int _messageToken;

        public DeviceViewModel(BluetoothService bluetooth, DeviceSnapshot s)
        {
            _bluetooth = bluetooth;
            Id = s.Id;
            ToggleConnectionCommand = new AsyncRelayCommand(ToggleConnectionAsync, () => CanToggle);
            Update(s);
        }

        public string Id { get; }

        public string Name { get => _name; private set => Set(ref _name, value); }

        public bool IsConnected
        {
            get => _isConnected;
            private set
            {
                if (!Set(ref _isConnected, value)) return;
                ClearMessage();
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(ActionText));
                OnPropertyChanged(nameof(CanToggle));
                ToggleConnectionCommand.RaiseCanExecuteChanged();
            }
        }

        public DeviceKind Kind
        {
            get => _kind;
            private set
            {
                if (!Set(ref _kind, value)) return;
                OnPropertyChanged(nameof(IconKey));
                OnPropertyChanged(nameof(Icon));
                OnPropertyChanged(nameof(KindText));
            }
        }

        public int? Battery
        {
            get => _battery;
            private set
            {
                if (Set(ref _battery, value))
                {
                    OnPropertyChanged(nameof(HasBattery));
                    OnPropertyChanged(nameof(BatteryValue));
                    OnPropertyChanged(nameof(BatteryText));
                    OnPropertyChanged(nameof(IsBatteryLow));
                    OnPropertyChanged(nameof(StatusText));
                }
            }
        }

        public ConnectAbility Ability
        {
            get => _ability;
            private set
            {
                if (!Set(ref _ability, value)) return;
                OnPropertyChanged(nameof(CanToggle));
                ToggleConnectionCommand.RaiseCanExecuteChanged();
            }
        }

        public bool HasBattery => _battery.HasValue;
        public double BatteryValue => _battery ?? 0;
        public string BatteryText => _battery.HasValue ? $"{_battery}%" : "";
        public bool IsBatteryLow => _battery.HasValue && _battery.Value <= App.Settings.Current.LowBatteryThreshold;

        // ---------- Connecting ----------

        public AsyncRelayCommand ToggleConnectionCommand { get; }

        /// <summary>
        /// Whether a connect/disconnect button makes sense for this device. LE devices can be
        /// brought up but not forced down, so they only get a button while disconnected.
        /// </summary>
        public bool CanToggle => Ability switch
        {
            ConnectAbility.AudioToggle => true,
            ConnectAbility.LeConnectOnly => !IsConnected,
            _ => false,
        };

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (!Set(ref _isBusy, value)) return;
                OnPropertyChanged(nameof(ActionText));
                OnPropertyChanged(nameof(StatusText));
                ToggleConnectionCommand.RaiseCanExecuteChanged();
            }
        }

        public string ActionText
        {
            get
            {
                if (IsBusy) return IsConnected ? "Disconnecting…" : "Connecting…";
                return IsConnected ? "Disconnect" : "Connect";
            }
        }

        private async Task ToggleConnectionAsync()
        {
            bool connect = !IsConnected;
            ClearMessage();
            IsBusy = true;
            try
            {
                bool ok = await _bluetooth.SetConnectedAsync(Id, connect);
                if (!ok)
                {
                    ShowMessage(connect
                        ? "Could not connect — is the device switched on?"
                        : "Could not disconnect");
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ShowMessage(string text)
        {
            _message = text;
            int token = Interlocked.Increment(ref _messageToken);
            OnPropertyChanged(nameof(StatusText));

            // Fall back to the normal status after a while, unless something newer happened.
            _ = Task.Delay(MessageSeconds * 1000).ContinueWith(_ =>
            {
                if (Volatile.Read(ref _messageToken) != token) return;
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null) return;
                dispatcher.BeginInvoke(new Action(() =>
                {
                    if (Volatile.Read(ref _messageToken) != token) return;
                    _message = null;
                    OnPropertyChanged(nameof(StatusText));
                }));
            });
        }

        private void ClearMessage()
        {
            Interlocked.Increment(ref _messageToken);
            if (_message == null) return;
            _message = null;
            OnPropertyChanged(nameof(StatusText));
        }

        // ---------- Display ----------

        public string StatusText
        {
            get
            {
                if (_message != null) return _message;
                if (IsBusy) return IsConnected ? "Disconnecting…" : "Connecting…";
                if (!IsConnected) return "Not connected";
                if (IsBatteryLow) return "Connected · battery low";
                return "Connected";
            }
        }

        public string KindText => Kind switch
        {
            DeviceKind.Headphones => "Headphones",
            DeviceKind.Speaker => "Speaker",
            DeviceKind.Mouse => "Mouse",
            DeviceKind.Keyboard => "Keyboard",
            DeviceKind.Phone => "Phone",
            DeviceKind.Gamepad => "Controller",
            DeviceKind.Computer => "Computer",
            _ => "Bluetooth device",
        };

        /// <summary>Resource key of the Tabler icon for this device type (see Themes/Icons.xaml).</summary>
        public string IconKey => Kind switch
        {
            DeviceKind.Headphones => "Icon.Headphones",
            DeviceKind.Speaker => "Icon.Speaker",
            DeviceKind.Mouse => "Icon.Mouse",
            DeviceKind.Keyboard => "Icon.Keyboard",
            DeviceKind.Phone => "Icon.Phone",
            DeviceKind.Gamepad => "Icon.Gamepad",
            DeviceKind.Computer => "Icon.Computer",
            _ => "Icon.Bluetooth",
        };

        /// <summary>The icon geometry itself; the icons live in a theme independent dictionary.</summary>
        public Geometry? Icon => Application.Current?.TryFindResource(IconKey) as Geometry;

        /// <summary>Called when the low battery threshold changed; what counts as low moved.</summary>
        public void RefreshBatteryThreshold()
        {
            OnPropertyChanged(nameof(IsBatteryLow));
            OnPropertyChanged(nameof(StatusText));
        }

        public void Update(DeviceSnapshot s)
        {
            Name = s.Name;
            Kind = s.Kind;
            IsConnected = s.IsConnected;
            Battery = s.Battery;
            Ability = s.Ability;
        }
    }
}
