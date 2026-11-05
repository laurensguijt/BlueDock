using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;
using BlueDock.Interop;

namespace BlueDock.Services
{
    public enum DeviceKind { Other, Headphones, Speaker, Mouse, Keyboard, Phone, Gamepad, Computer }

    /// <summary>
    /// How far we can steer a device's connection. Windows has no general "connect this paired
    /// device" API, so this says what is actually possible per device instead of offering a
    /// button that would silently do nothing.
    /// </summary>
    public enum ConnectAbility
    {
        /// <summary>No supported way to connect or disconnect (classic non-audio devices, e.g. a phone).</summary>
        None,
        /// <summary>Audio device: connect and disconnect through the Core Audio KS one shot properties.</summary>
        AudioToggle,
        /// <summary>LE device: a GATT session can bring it up, but we cannot force it down again.</summary>
        LeConnectOnly,
    }

    public sealed record DeviceSnapshot(string Id, string Name, bool IsConnected, DeviceKind Kind, int? Battery,
                                        bool IsLowEnergy, ConnectAbility Ability);

    public sealed class ConnectionChangedEventArgs : EventArgs
    {
        public ConnectionChangedEventArgs(string name, bool isConnected, DeviceKind kind)
        {
            Name = name;
            IsConnected = isConnected;
            Kind = kind;
        }

        public string Name { get; }
        public bool IsConnected { get; }
        public DeviceKind Kind { get; }
    }

    public sealed class LowBatteryEventArgs : EventArgs
    {
        public LowBatteryEventArgs(string name, int percent, DeviceKind kind)
        {
            Name = name;
            Percent = percent;
            Kind = kind;
        }

        public string Name { get; }
        public int Percent { get; }
        public DeviceKind Kind { get; }
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
        // The classification Windows itself uses in Settings, e.g. "Audio.Headphone",
        // "Communication.Headset.Bluetooth", "Input.Mouse". Lives on the association endpoint,
        // so it arrives with the watcher and needs no second lookup.
        private const string AepCategoryKey = "System.Devices.Aep.Category";
        // Same idea on the device container. Note it is CategoryIds, not Category: the latter
        // is the localized display text and comes back empty for Bluetooth devices.
        private const string CategoryIdsKey = "System.Devices.CategoryIds";
        // DEVPKEY_Bluetooth_Battery: battery level (byte, 0-100) that Windows publishes for Bluetooth devices
        private const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";

        private static readonly string[] WatcherProps = { IsConnectedKey, ContainerIdKey, AepCategoryKey };

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
            public ConnectAbility Ability;

            public DeviceSnapshot Snapshot() => new(Id, Name, IsConnected, Kind, Battery, IsLowEnergy, Ability);
        }

        /// <summary>An LE connection we hold open ourselves; disposing it lets the link drop again.</summary>
        private sealed class LeLink : IDisposable
        {
            public BluetoothLEDevice? Device;
            public GattSession? Session;

            public void Dispose()
            {
                try { Session?.Dispose(); } catch { /* already gone */ }
                try { Device?.Dispose(); } catch { /* already gone */ }
                Session = null;
                Device = null;
            }
        }

        private readonly object _lock = new();
        private readonly Dictionary<string, Entry> _devices = new();
        private readonly Dictionary<string, LeLink> _leLinks = new();
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

            lock (_lock)
            {
                foreach (var link in _leLinks.Values) link.Dispose();
                _leLinks.Clear();
            }

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
            // Prefer what Windows says this device is; only guess from the name if it says nothing.
            entry.Kind = KindFromCategories(GetStrings(info.Properties, AepCategoryKey));
            if (entry.Kind == DeviceKind.Other)
                entry.Kind = KindFromName(entry.Name);

            lock (_lock)
            {
                if (_devices.ContainsKey(entry.Id)) return;
                _devices[entry.Id] = entry;
            }

            DeviceAdded?.Invoke(this, entry.Snapshot());
            _ = ResolveKindAsync(entry);
            _ = ResolveAbilityAsync(entry);
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
                    ConnectionChanged?.Invoke(this, new ConnectionChangedEventArgs(entry.Name, entry.IsConnected, entry.Kind));

                if (entry.IsConnected)
                    _ = RefreshBatteryAfterConnectAsync(entry);
                else
                    DropLeLink(entry.Id);   // a stale session would block a later connect attempt
            }
        }

        private void OnRemoved(string id)
        {
            LeLink? link;
            lock (_lock)
            {
                if (!_devices.Remove(id)) return;
                if (_leLinks.Remove(id, out link)) { /* disposed below, outside the lock */ }
            }
            link?.Dispose();
            DeviceRemoved?.Invoke(this, id);
        }

        // ---------- Connect / disconnect ----------

        /// <summary>
        /// Connects or disconnects a paired device, and waits for Windows to confirm the new state.
        /// Returns false when the device cannot be steered, when the request is rejected, or when
        /// the device never reaches the requested state (switched off, out of range).
        /// </summary>
        public async Task<bool> SetConnectedAsync(string id, bool connect)
        {
            Entry? entry;
            lock (_lock)
            {
                if (!_devices.TryGetValue(id, out entry)) return false;
                if (entry.IsConnected == connect) return true;
            }

            switch (entry.Ability)
            {
                case ConnectAbility.AudioToggle:
                    if (entry.ContainerId is not Guid container) return false;
                    // COM enumeration plus a driver round trip: keep it off the UI thread.
                    if (!await Task.Run(() => CoreAudio.SendOneShot(container, connect))) return false;
                    break;

                case ConnectAbility.LeConnectOnly:
                    if (!connect) return false;
                    if (!await OpenLeLinkAsync(entry)) return false;
                    break;

                default:
                    return false;
            }

            // The KS property and the GATT session both only *request* a state; the DeviceWatcher
            // tells us what actually happened.
            bool reached = await WaitForStateAsync(entry, connect, TimeSpan.FromSeconds(connect ? 12 : 8));

            if (!reached && connect)
                DropLeLink(entry.Id);   // do not keep a session open for a device that never came up

            return reached;
        }

        /// <summary>Holds a GATT session with MaintainConnection, which asks Windows to bring the LE link up.</summary>
        private async Task<bool> OpenLeLinkAsync(Entry entry)
        {
            var link = new LeLink();
            try
            {
                link.Device = await BluetoothLEDevice.FromIdAsync(entry.Id);
                if (link.Device == null) { link.Dispose(); return false; }

                link.Session = await GattSession.FromDeviceIdAsync(link.Device.BluetoothDeviceId);
                if (link.Session == null) { link.Dispose(); return false; }

                link.Session.MaintainConnection = true;
            }
            catch
            {
                link.Dispose();
                return false;
            }

            LeLink? previous;
            lock (_lock)
            {
                _leLinks.Remove(entry.Id, out previous);
                _leLinks[entry.Id] = link;
            }
            previous?.Dispose();
            return true;
        }

        private void DropLeLink(string id)
        {
            LeLink? link;
            lock (_lock)
            {
                if (!_leLinks.Remove(id, out link)) return;
            }
            link?.Dispose();
        }

        private async Task<bool> WaitForStateAsync(Entry entry, bool desired, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (true)
            {
                lock (_lock)
                {
                    if (entry.IsConnected == desired) return true;
                }
                if (DateTime.UtcNow >= deadline) return false;
                await Task.Delay(250);
            }
        }

        private async Task ResolveAbilityAsync(Entry entry)
        {
            ConnectAbility ability = ConnectAbility.None;
            try
            {
                if (entry.ContainerId is Guid container
                    && await Task.Run(() => CoreAudio.HasAudioEndpoint(container)))
                {
                    ability = ConnectAbility.AudioToggle;
                }
                else if (entry.IsLowEnergy)
                {
                    ability = ConnectAbility.LeConnectOnly;
                }
            }
            catch
            {
                ability = ConnectAbility.None;
            }

            if (ability == entry.Ability) return;
            entry.Ability = ability;
            DeviceUpdated?.Invoke(this, entry.Snapshot());
        }

        // ---------- Device type (icon) ----------

        /// <summary>
        /// Last resort when the association endpoint carried no category: ask the device
        /// container. Rarely needed, since Aep.Category is populated for paired devices.
        /// </summary>
        private async Task ResolveKindAsync(Entry entry)
        {
            if (entry.Kind != DeviceKind.Other) return;
            if (entry.ContainerId is not Guid container) return;
            try
            {
                var info = await DeviceInformation.CreateFromIdAsync(
                    "{" + container + "}", new[] { CategoryIdsKey }, DeviceInformationKind.DeviceContainer);

                var kind = KindFromCategories(GetStrings(info.Properties, CategoryIdsKey));
                if (kind != DeviceKind.Other && kind != entry.Kind)
                {
                    entry.Kind = kind;
                    DeviceUpdated?.Invoke(this, entry.Snapshot());
                }
            }
            catch
            {
                // Keep what we have.
            }
        }

        /// <summary>
        /// Maps Windows' device categories onto our icon set. Categories are dotted ids such as
        /// "Audio.Headphone" or "Communication.Headset.Bluetooth"; the first entry is the primary
        /// one, so each category is matched in full before moving on to the next.
        /// </summary>
        private static DeviceKind KindFromCategories(IEnumerable<string> categories)
        {
            foreach (var category in categories)
            {
                var c = category.ToLowerInvariant();

                if (c.Contains("keyboard")) return DeviceKind.Keyboard;
                if (c.Contains("mouse") || c.Contains("pointing")) return DeviceKind.Mouse;
                if (c.Contains("gaming") || c.Contains("gamepad") || c.Contains("controller")) return DeviceKind.Gamepad;
                if (c.Contains("headset") || c.Contains("headphone")) return DeviceKind.Headphones;
                if (c.Contains("speaker")) return DeviceKind.Speaker;
                if (c.Contains("phone")) return DeviceKind.Phone;
                if (c.StartsWith("computer")) return DeviceKind.Computer;
            }
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
                    LowBattery?.Invoke(this, new LowBatteryEventArgs(entry.Name, pct, entry.Kind));
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

        private static IEnumerable<string> GetStrings(IReadOnlyDictionary<string, object> props, string key) =>
            props.TryGetValue(key, out var v) && v is string[] a ? a : Array.Empty<string>();
    }
}
