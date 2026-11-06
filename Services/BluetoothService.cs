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
                                        bool IsLowEnergy, ConnectAbility Ability, Guid? ContainerId);

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
            public ConnectAbility Ability;
            /// <summary>Which physical device this endpoint belongs to.</summary>
            public string GroupKey = "";
        }

        /// <summary>
        /// One physical device. A dual mode headset registers two association endpoints, a
        /// classic one and an LE one, that share a container id; Windows shows them as a single
        /// device and so do we. Without this the headset appears twice in the list and a single
        /// connect raises two notifications.
        /// </summary>
        private sealed class Group
        {
            public string Key = "";
            public readonly List<Entry> Entries = new();
            public bool Announced;
            public bool WasConnected;
            public bool LowBatteryNotified;
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
        private readonly Dictionary<string, Group> _groups = new();
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

        /// <summary>Applies a new battery polling interval without restarting the watchers.</summary>
        public void SetBatteryInterval(int seconds)
        {
            var interval = TimeSpan.FromSeconds(Math.Max(30, seconds));
            _batteryTimer?.Change(interval, interval);
        }

        public void Stop()
        {
            _batteryTimer?.Dispose();

            lock (_lock)
            {
                foreach (var link in _leLinks.Values) link.Dispose();
                _leLinks.Clear();
                _groups.Clear();
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

            Group group;
            lock (_lock)
            {
                if (_devices.ContainsKey(entry.Id)) return;
                _devices[entry.Id] = entry;

                entry.GroupKey = entry.ContainerId is Guid container ? container.ToString() : entry.Id;
                if (!_groups.TryGetValue(entry.GroupKey, out group!))
                {
                    group = new Group { Key = entry.GroupKey };
                    _groups[entry.GroupKey] = group;
                }
                group.Entries.Add(entry);
            }

            Publish(group);
            _ = ResolveKindAsync(entry);
            _ = ResolveAbilityAsync(entry);
            if (entry.IsConnected)
                _ = RefreshBatteryAsync(entry);
        }

        private void OnUpdated(DeviceInformationUpdate update)
        {
            Entry? entry;
            Group? group;
            bool endpointChanged = false;
            lock (_lock)
            {
                if (!_devices.TryGetValue(update.Id, out entry)) return;
                _groups.TryGetValue(entry.GroupKey, out group);

                if (update.Properties.TryGetValue(IsConnectedKey, out var c) && c is bool connected
                    && connected != entry.IsConnected)
                {
                    entry.IsConnected = connected;
                    endpointChanged = true;
                    if (!connected) entry.Battery = null;
                }

                if (update.Properties.TryGetValue(NameKey, out var n) && n is string name && !string.IsNullOrWhiteSpace(name))
                    entry.Name = name;
            }

            // Publish decides whether the device as a whole changed state: one endpoint of a
            // dual mode device dropping is not the device disconnecting.
            if (group != null) Publish(group);

            if (endpointChanged)
            {
                if (entry.IsConnected)
                    _ = RefreshBatteryAfterConnectAsync(entry);
                else
                    DropLeLink(entry.Id);   // a stale session would block a later connect attempt
            }
        }

        /// <summary>
        /// Raises the right event for a device, built from all of its endpoints. Reports a
        /// connection change only when the device as a whole went up or down.
        /// </summary>
        private void Publish(Group group)
        {
            DeviceSnapshot snapshot;
            bool announce, connectionChanged;

            lock (_lock)
            {
                if (group.Entries.Count == 0) return;
                snapshot = BuildSnapshot(group);

                announce = !group.Announced;
                group.Announced = true;

                connectionChanged = snapshot.IsConnected != group.WasConnected;
                group.WasConnected = snapshot.IsConnected;
                if (connectionChanged && !snapshot.IsConnected)
                    group.LowBatteryNotified = false;
            }

            if (announce)
                DeviceAdded?.Invoke(this, snapshot);
            else
                DeviceUpdated?.Invoke(this, snapshot);

            if (connectionChanged && IsLive)
                ConnectionChanged?.Invoke(this,
                    new ConnectionChangedEventArgs(snapshot.Name, snapshot.IsConnected, snapshot.Kind));
        }

        /// <summary>Folds the endpoints of one physical device into a single view of it.</summary>
        private static DeviceSnapshot BuildSnapshot(Group group)
        {
            // The classic endpoint carries the friendly Bluetooth name. The LE one is often
            // prefixed ("LE-Zone 305") or just whatever the device advertises, which for a phone
            // can be a single digit, so it is only used when there is no classic endpoint.
            var named = group.Entries.FirstOrDefault(e => !e.IsLowEnergy) ?? group.Entries[0];

            var kind = group.Entries
                .OrderBy(e => e.IsLowEnergy ? 1 : 0)
                .Select(e => e.Kind)
                .FirstOrDefault(k => k != DeviceKind.Other);

            // A battery reading from a connected endpoint beats a stale one.
            int? battery = group.Entries.FirstOrDefault(e => e.IsConnected && e.Battery.HasValue)?.Battery
                        ?? group.Entries.FirstOrDefault(e => e.Battery.HasValue)?.Battery;

            // Not Max(): the enum order does not match how useful the abilities are.
            var ability = group.Entries.Any(e => e.Ability == ConnectAbility.AudioToggle)
                ? ConnectAbility.AudioToggle
                : group.Entries.Any(e => e.Ability == ConnectAbility.LeConnectOnly)
                    ? ConnectAbility.LeConnectOnly
                    : ConnectAbility.None;

            return new DeviceSnapshot(
                group.Key,
                named.Name,
                group.Entries.Any(e => e.IsConnected),
                kind,
                battery,
                group.Entries.All(e => e.IsLowEnergy),
                ability,
                group.Entries.Select(e => e.ContainerId).FirstOrDefault(c => c.HasValue));
        }

        private DeviceSnapshot BuildSnapshotLocked(Group group)
        {
            lock (_lock) return BuildSnapshot(group);
        }

        private Group? GroupOf(Entry entry)
        {
            lock (_lock)
                return _groups.TryGetValue(entry.GroupKey, out var group) ? group : null;
        }

        private void PublishGroupOf(Entry entry)
        {
            var group = GroupOf(entry);
            if (group != null) Publish(group);
        }

        private void OnRemoved(string id)
        {
            LeLink? link;
            Group? group = null;
            bool deviceGone = false;

            lock (_lock)
            {
                if (!_devices.Remove(id, out var entry)) return;
                _leLinks.Remove(id, out link);

                if (_groups.TryGetValue(entry.GroupKey, out group))
                {
                    group.Entries.Remove(entry);
                    if (group.Entries.Count == 0)
                    {
                        _groups.Remove(entry.GroupKey);
                        deviceGone = true;
                    }
                }
            }
            link?.Dispose();

            // Losing one endpoint of a dual mode device is an update, not a removal.
            if (deviceGone && group != null) DeviceRemoved?.Invoke(this, group.Key);
            else if (group != null) Publish(group);
        }

        // ---------- Connect / disconnect ----------

        /// <summary>
        /// Connects or disconnects a paired device, and waits for Windows to confirm the new state.
        /// Returns false when the device cannot be steered, when the request is rejected, or when
        /// the device never reaches the requested state (switched off, out of range).
        /// </summary>
        public async Task<bool> SetConnectedAsync(string id, bool connect)
        {
            Group? group;
            Entry? audio, le;
            lock (_lock)
            {
                if (!_groups.TryGetValue(id, out group)) return false;
                if (group.Entries.Any(e => e.IsConnected) == connect) return true;

                // Either endpoint may be the one that can be steered; audio wins because it is
                // the only one that can also disconnect.
                audio = group.Entries.FirstOrDefault(e => e.Ability == ConnectAbility.AudioToggle);
                le = group.Entries.FirstOrDefault(e => e.IsLowEnergy && e.Ability == ConnectAbility.LeConnectOnly);
            }

            if (audio != null && audio.ContainerId is Guid container)
            {
                // COM enumeration plus a driver round trip: keep it off the UI thread.
                if (!await Task.Run(() => CoreAudio.SendOneShot(container, connect))) return false;
            }
            else if (connect && le != null)
            {
                if (!await OpenLeLinkAsync(le)) return false;
            }
            else
            {
                return false;
            }

            // The KS property and the GATT session both only *request* a state; the DeviceWatcher
            // tells us what actually happened.
            bool reached = await WaitForStateAsync(group, connect, TimeSpan.FromSeconds(connect ? 12 : 8));

            if (!reached && connect && le != null)
                DropLeLink(le.Id);   // do not keep a session open for a device that never came up

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

        private async Task<bool> WaitForStateAsync(Group group, bool desired, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (true)
            {
                lock (_lock)
                {
                    if (group.Entries.Any(e => e.IsConnected) == desired) return true;
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
            PublishGroupOf(entry);
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
                    PublishGroupOf(entry);
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

            var group = GroupOf(entry);
            if (group == null) return;
            Publish(group);

            var threshold = App.Settings.Current.LowBatteryThreshold;
            if (level is not int pct) return;

            bool warn = false;
            lock (_lock)
            {
                if (pct <= threshold && !group.LowBatteryNotified)
                {
                    group.LowBatteryNotified = true;
                    warn = true;
                }
                else if (pct > threshold + 5)
                {
                    group.LowBatteryNotified = false; // re-arm after charging
                }
            }

            if (warn)
            {
                var snapshot = BuildSnapshotLocked(group);
                LowBattery?.Invoke(this, new LowBatteryEventArgs(snapshot.Name, pct, snapshot.Kind));
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
