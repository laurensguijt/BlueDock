using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace BlueDock.Services
{
    public enum DeviceKind { Other, Headphones, Speaker, Mouse, Keyboard, Phone, Gamepad, Computer }

    public sealed record DeviceSnapshot(string Id, string Name, bool IsConnected, DeviceKind Kind, int? Battery, bool IsLowEnergy);

    public sealed class ConnectionChangedEventArgs : EventArgs
    {
        public ConnectionChangedEventArgs(string name, bool isConnected) { Name = name; IsConnected = isConnected; }
        public string Name { get; }
        public bool IsConnected { get; }
    }

    public sealed class LowBatteryEventArgs : EventArgs
    {
        public LowBatteryEventArgs(string name, int percent) { Name = name; Percent = percent; }
        public string Name { get; }
        public int Percent { get; }
    }

    /// <summary>
    /// Watches paired Bluetooth devices (Classic + LE) through WinRT DeviceWatchers.
    /// All events are raised on background threads; the ViewModel marshals to the UI thread.
    /// </summary>
    public sealed class BluetoothService
    {
        private const string IsConnectedKey = "System.Devices.Aep.IsConnected";
        private const string ContainerIdKey = "System.Devices.Aep.ContainerId";
        private const string NameKey = "System.ItemNameDisplay";
        private const string CategoryKey = "System.Devices.Category";
        // DEVPKEY_Bluetooth_Battery: battery level (byte, 0-100) that Windows publishes for Bluetooth devices
        private const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";

        private static readonly string[] WatcherProps = { IsConnectedKey, ContainerIdKey };

        private sealed class Entry
        {
            public string Id = "";
            public string Name = "";
            public bool IsConnected;
            public Guid? ContainerId;
            public DeviceKind Kind;
            public int? Battery;
            public bool IsLowEnergy;
            public bool LowBatteryNotified;

            public DeviceSnapshot Snapshot() => new(Id, Name, IsConnected, Kind, Battery, IsLowEnergy);
        }

        private readonly object _lock = new();
        private readonly Dictionary<string, Entry> _devices = new();
        private DeviceWatcher? _classic;
        private DeviceWatcher? _le;
        private int _enumerationsCompleted;
        private Timer? _batteryTimer;

        public event EventHandler<DeviceSnapshot>? DeviceAdded;
        public event EventHandler<DeviceSnapshot>? DeviceUpdated;
        public event EventHandler<string>? DeviceRemoved;
        public event EventHandler<ConnectionChangedEventArgs>? ConnectionChanged;
        public event EventHandler<LowBatteryEventArgs>? LowBattery;

        /// <summary>True once both watchers finished their initial enumeration (avoids notifications at startup).</summary>
        private bool IsLive => Volatile.Read(ref _enumerationsCompleted) >= 2;

        public void Start()
        {
            _classic = CreateWatcher(BluetoothDevice.GetDeviceSelectorFromPairingState(true), isLe: false);
            _le = CreateWatcher(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true), isLe: true);
            _classic.Start();
            _le.Start();

            var interval = TimeSpan.FromSeconds(Math.Max(30, App.Settings.Current.BatteryRefreshSeconds));
            _batteryTimer = new Timer(_ => RefreshAllBatteries(), null, interval, interval);
        }

        public void Stop()
        {
            _batteryTimer?.Dispose();
            foreach (var w in new[] { _classic, _le })
            {
                if (w == null) continue;
                if (w.Status == DeviceWatcherStatus.Started || w.Status == DeviceWatcherStatus.EnumerationCompleted)
                    w.Stop();
            }
        }

        private DeviceWatcher CreateWatcher(string selector, bool isLe)
        {
            var w = DeviceInformation.CreateWatcher(selector, WatcherProps, DeviceInformationKind.AssociationEndpoint);
            w.Added += (s, info) => OnAdded(info, isLe);
            w.Updated += (s, upd) => OnUpdated(upd);
            w.Removed += (s, upd) => OnRemoved(upd.Id);
            w.EnumerationCompleted += (s, e) => Interlocked.Increment(ref _enumerationsCompleted);
            return w;
        }

        private void OnAdded(DeviceInformation info, bool isLe)
        {
            var entry = new Entry
            {
                Id = info.Id,
                Name = string.IsNullOrWhiteSpace(info.Name) ? "Onbekend apparaat" : info.Name,
                IsConnected = GetBool(info.Properties, IsConnectedKey),
                ContainerId = GetGuid(info.Properties, ContainerIdKey),
                IsLowEnergy = isLe,
            };
            entry.Kind = KindFromName(entry.Name);

            lock (_lock)
            {
                if (_devices.ContainsKey(entry.Id)) return;
                _devices[entry.Id] = entry;
            }

            DeviceAdded?.Invoke(this, entry.Snapshot());
            _ = ResolveKindAsync(entry);
            if (entry.IsConnected)
                _ = RefreshBatteryAsync(entry);
        }

        private void OnUpdated(DeviceInformationUpdate update)
        {
            Entry? entry;
            bool connectionChanged = false;
            lock (_lock)
            {
                if (!_devices.TryGetValue(update.Id, out entry)) return;

                if (update.Properties.TryGetValue(IsConnectedKey, out var c) && c is bool connected
                    && connected != entry.IsConnected)
                {
                    entry.IsConnected = connected;
                    connectionChanged = true;
                    if (!connected)
                    {
                        entry.Battery = null;
                        entry.LowBatteryNotified = false;
                    }
                }

                if (update.Properties.TryGetValue(NameKey, out var n) && n is string name && !string.IsNullOrWhiteSpace(name))
                    entry.Name = name;
            }

            DeviceUpdated?.Invoke(this, entry.Snapshot());

            if (connectionChanged)
            {
                if (IsLive)
                    ConnectionChanged?.Invoke(this, new ConnectionChangedEventArgs(entry.Name, entry.IsConnected));

                if (entry.IsConnected)
                    _ = RefreshBatteryAfterConnectAsync(entry);
            }
        }

        private void OnRemoved(string id)
        {
            lock (_lock)
            {
                if (!_devices.Remove(id)) return;
            }
            DeviceRemoved?.Invoke(this, id);
        }

        // ---------- Device type (icon) ----------

        private async Task ResolveKindAsync(Entry entry)
        {
            if (entry.ContainerId is not Guid container) return;
            try
            {
                var info = await DeviceInformation.CreateFromIdAsync(
                    "{" + container + "}", new[] { CategoryKey }, DeviceInformationKind.DeviceContainer);

                if (info.Properties.TryGetValue(CategoryKey, out var value) && value is string[] categories)
                {
                    var kind = KindFromCategories(categories);
                    if (kind != DeviceKind.Other && kind != entry.Kind)
                    {
                        entry.Kind = kind;
                        DeviceUpdated?.Invoke(this, entry.Snapshot());
                    }
                }
            }
            catch
            {
                // Keep the name based guess.
            }
        }

        private static DeviceKind KindFromCategories(IEnumerable<string> categories)
        {
            var all = string.Join(";", categories).ToLowerInvariant();
            if (all.Contains("keyboard")) return DeviceKind.Keyboard;
            if (all.Contains("mouse") || all.Contains("pointing")) return DeviceKind.Mouse;
            if (all.Contains("game")) return DeviceKind.Gamepad;
            if (all.Contains("speaker")) return DeviceKind.Speaker;
            if (all.Contains("headphone") || all.Contains("headset") || all.Contains("audio")) return DeviceKind.Headphones;
            if (all.Contains("phone")) return DeviceKind.Phone;
            if (all.Contains("computer")) return DeviceKind.Computer;
            return DeviceKind.Other;
        }

        private static DeviceKind KindFromName(string name)
        {
            var n = name.ToLowerInvariant();
            if (n.Contains("keyboard") || n.Contains("keys")) return DeviceKind.Keyboard;
            if (n.Contains("mouse") || n.Contains("mx master") || n.Contains("mx anywhere")) return DeviceKind.Mouse;
            if (n.Contains("controller") || n.Contains("gamepad")) return DeviceKind.Gamepad;
            if (n.Contains("speaker") || n.Contains("flip") || n.Contains("soundlink")) return DeviceKind.Speaker;
            if (n.Contains("buds") || n.Contains("pods") || n.Contains("head") || n.Contains("wh-") || n.Contains("wf-")) return DeviceKind.Headphones;
            if (n.Contains("phone") || n.Contains("pixel") || n.Contains("galaxy")) return DeviceKind.Phone;
            return DeviceKind.Other;
        }

        // ---------- Battery ----------

        private void RefreshAllBatteries()
        {
            List<Entry> connected;
            lock (_lock) connected = _devices.Values.Where(d => d.IsConnected).ToList();
            foreach (var e in connected)
                _ = RefreshBatteryAsync(e);
        }

        private async Task RefreshBatteryAfterConnectAsync(Entry entry)
        {
            // Windows publishes the battery value a few seconds after the profile connects.
            await Task.Delay(TimeSpan.FromSeconds(4));
            await RefreshBatteryAsync(entry);
        }

        private async Task RefreshBatteryAsync(Entry entry)
        {
            int? level = await ReadPnpBatteryAsync(entry);
            if (level == null && entry.IsLowEnergy)
                level = await ReadGattBatteryAsync(entry);

            if (level == entry.Battery) return;
            entry.Battery = level;
            DeviceUpdated?.Invoke(this, entry.Snapshot());

            var threshold = App.Settings.Current.LowBatteryThreshold;
            if (level is int pct)
            {
                if (pct <= threshold && !entry.LowBatteryNotified)
                {
                    entry.LowBatteryNotified = true;
                    LowBattery?.Invoke(this, new LowBatteryEventArgs(entry.Name, pct));
                }
                else if (pct > threshold + 5)
                {
                    entry.LowBatteryNotified = false; // re-arm after charging
                }
            }
        }

        /// <summary>Battery level that Windows itself shows in Settings (Classic devices with HFP/AVRCP battery reporting).</summary>
        private static async Task<int?> ReadPnpBatteryAsync(Entry entry)
        {
            if (entry.ContainerId is not Guid container) return null;
            try
            {
                var aqs = $"System.Devices.ContainerId:=\"{{{container}}}\"";
                var nodes = await DeviceInformation.FindAllAsync(aqs, new[] { BatteryKey }, DeviceInformationKind.Device);
                foreach (var node in nodes)
                {
                    if (node.Properties.TryGetValue(BatteryKey, out var v) && v != null)
                    {
                        var pct = Convert.ToInt32(v);
                        if (pct >= 0 && pct <= 100) return pct;
                    }
                }
            }
            catch
            {
                // Not every device/driver exposes this property.
            }
            return null;
        }

        /// <summary>Battery Service (0x180F) over GATT for LE devices.</summary>
        private static async Task<int?> ReadGattBatteryAsync(Entry entry)
        {
            BluetoothLEDevice? device = null;
            try
            {
                device = await BluetoothLEDevice.FromIdAsync(entry.Id);
                if (device == null) return null;

                var services = await device.GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Uncached);
                if (services.Status != GattCommunicationStatus.Success) return null;

                foreach (var service in services.Services)
                {
                    using (service)
                    {
                        var chars = await service.GetCharacteristicsForUuidAsync(
                            GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Uncached);
                        if (chars.Status != GattCommunicationStatus.Success || chars.Characteristics.Count == 0)
                            continue;

                        var read = await chars.Characteristics[0].ReadValueAsync(BluetoothCacheMode.Uncached);
                        if (read.Status != GattCommunicationStatus.Success) continue;

                        using var reader = DataReader.FromBuffer(read.Value);
                        if (reader.UnconsumedBufferLength < 1) continue;
                        int pct = reader.ReadByte();
                        if (pct >= 0 && pct <= 100) return pct;
                    }
                }
            }
            catch
            {
                // HID devices can block GATT access; that is fine, we just show no value.
            }
            finally
            {
                device?.Dispose();
            }
            return null;
        }

        // ---------- Helpers ----------

        private static bool GetBool(IReadOnlyDictionary<string, object> props, string key) =>
            props.TryGetValue(key, out var v) && v is bool b && b;

        private static Guid? GetGuid(IReadOnlyDictionary<string, object> props, string key) =>
            props.TryGetValue(key, out var v) && v is Guid g ? g : null;
    }
}
