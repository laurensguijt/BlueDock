using BlueDock.Services;

namespace BlueDock.ViewModels
{
    public sealed class DeviceViewModel : ObservableObject
    {
        private string _name = "";
        private bool _isConnected;
        private DeviceKind _kind;
        private int? _battery;

        public DeviceViewModel(DeviceSnapshot s)
        {
            Id = s.Id;
            Update(s);
        }

        public string Id { get; }

        public string Name { get => _name; private set => Set(ref _name, value); }

        public bool IsConnected
        {
            get => _isConnected;
            private set { if (Set(ref _isConnected, value)) { OnPropertyChanged(nameof(StatusText)); } }
        }

        public DeviceKind Kind
        {
            get => _kind;
            private set { if (Set(ref _kind, value)) { OnPropertyChanged(nameof(Glyph)); OnPropertyChanged(nameof(KindText)); } }
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

        public bool HasBattery => _battery.HasValue;
        public double BatteryValue => _battery ?? 0;
        public string BatteryText => _battery.HasValue ? $"{_battery}%" : "";
        public bool IsBatteryLow => _battery.HasValue && _battery.Value <= App.Settings.Current.LowBatteryThreshold;

        public string StatusText
        {
            get
            {
                if (!IsConnected) return "Niet verbonden";
                if (IsBatteryLow) return "Verbonden · bijna leeg";
                return "Verbonden";
            }
        }

        public string KindText => Kind switch
        {
            DeviceKind.Headphones => "Koptelefoon",
            DeviceKind.Speaker => "Speaker",
            DeviceKind.Mouse => "Muis",
            DeviceKind.Keyboard => "Toetsenbord",
            DeviceKind.Phone => "Telefoon",
            DeviceKind.Gamepad => "Controller",
            DeviceKind.Computer => "Computer",
            _ => "Bluetooth-apparaat",
        };

        /// <summary>Segoe Fluent Icons / Segoe MDL2 Assets glyph.</summary>
        public string Glyph => Kind switch
        {
            DeviceKind.Headphones => "",
            DeviceKind.Speaker => "",
            DeviceKind.Mouse => "",
            DeviceKind.Keyboard => "",
            DeviceKind.Phone => "",
            DeviceKind.Gamepad => "",
            DeviceKind.Computer => "",
            _ => "",
        };

        public void Update(DeviceSnapshot s)
        {
            Name = s.Name;
            Kind = s.Kind;
            IsConnected = s.IsConnected;
            Battery = s.Battery;
        }
    }
}
