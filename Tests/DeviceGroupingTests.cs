using System;
using BlueDock.Services;
using Xunit;

namespace BlueDock.Tests
{
    /// <summary>
    /// Folding the endpoints of one physical device into a single view of it. A dual mode
    /// device registers twice with Windows, and getting this wrong listed it twice and sent
    /// two notifications for one connect.
    /// </summary>
    public class DeviceGroupingTests
    {
        private static BluetoothService.Entry Endpoint(
            string id, string name, bool connected = false, bool le = false,
            DeviceKind kind = DeviceKind.Other, int? battery = null,
            ConnectAbility ability = ConnectAbility.None, Guid? container = null) =>
            new()
            {
                Id = id,
                Name = name,
                IsConnected = connected,
                IsLowEnergy = le,
                Kind = kind,
                Battery = battery,
                Ability = ability,
                ContainerId = container,
            };

        private static BluetoothService.Group Group(params BluetoothService.Entry[] endpoints)
        {
            var group = new BluetoothService.Group { Key = "container-key" };
            group.Entries.AddRange(endpoints);
            return group;
        }

        [Fact]
        public void Name_comes_from_the_classic_endpoint()
        {
            // The LE side of a headset is often prefixed, and for a phone it can be a single digit.
            var snapshot = BluetoothService.BuildSnapshot(Group(
                Endpoint("le", "LE-Zone 305", le: true),
                Endpoint("classic", "Zone 305")));

            Assert.Equal("Zone 305", snapshot.Name);
        }

        [Fact]
        public void Name_falls_back_to_the_le_endpoint_when_that_is_all_there_is()
        {
            var snapshot = BluetoothService.BuildSnapshot(Group(Endpoint("le", "MX Master 3S", le: true)));
            Assert.Equal("MX Master 3S", snapshot.Name);
        }

        [Fact]
        public void Connected_when_any_endpoint_is()
        {
            var snapshot = BluetoothService.BuildSnapshot(Group(
                Endpoint("a", "x", connected: false),
                Endpoint("b", "x", connected: true, le: true)));

            Assert.True(snapshot.IsConnected);
        }

        [Fact]
        public void Disconnected_only_when_every_endpoint_is()
        {
            var snapshot = BluetoothService.BuildSnapshot(Group(
                Endpoint("a", "x"),
                Endpoint("b", "x", le: true)));

            Assert.False(snapshot.IsConnected);
        }

        [Fact]
        public void Battery_prefers_a_reading_from_a_connected_endpoint()
        {
            // The disconnected side can hold a stale value from before it dropped.
            var snapshot = BluetoothService.BuildSnapshot(Group(
                Endpoint("stale", "x", connected: false, battery: 10),
                Endpoint("live", "x", connected: true, le: true, battery: 80)));

            Assert.Equal(80, snapshot.Battery);
        }

        [Fact]
        public void Battery_is_null_when_nothing_reports_one()
        {
            var snapshot = BluetoothService.BuildSnapshot(Group(Endpoint("a", "x", connected: true)));
            Assert.Null(snapshot.Battery);
        }

        [Fact]
        public void Audio_beats_le_even_though_the_enum_says_otherwise()
        {
            // ConnectAbility.LeConnectOnly has the higher value, so a Max() here would pick the
            // weaker ability and lose the ability to disconnect.
            var snapshot = BluetoothService.BuildSnapshot(Group(
                Endpoint("le", "x", le: true, ability: ConnectAbility.LeConnectOnly),
                Endpoint("classic", "x", ability: ConnectAbility.AudioToggle)));

            Assert.Equal(ConnectAbility.AudioToggle, snapshot.Ability);
        }

        [Fact]
        public void Ability_is_none_when_no_endpoint_can_be_steered()
        {
            var snapshot = BluetoothService.BuildSnapshot(Group(Endpoint("a", "iPhone")));
            Assert.Equal(ConnectAbility.None, snapshot.Ability);
        }

        [Fact]
        public void Counts_as_low_energy_only_when_every_endpoint_is()
        {
            var mixed = BluetoothService.BuildSnapshot(Group(
                Endpoint("le", "x", le: true),
                Endpoint("classic", "x")));
            var pureLe = BluetoothService.BuildSnapshot(Group(Endpoint("le", "x", le: true)));

            Assert.False(mixed.IsLowEnergy);
            Assert.True(pureLe.IsLowEnergy);
        }

        [Fact]
        public void Kind_prefers_the_classic_endpoint_and_skips_unknowns()
        {
            var snapshot = BluetoothService.BuildSnapshot(Group(
                Endpoint("le", "x", le: true, kind: DeviceKind.Other),
                Endpoint("classic", "x", kind: DeviceKind.Headphones)));

            Assert.Equal(DeviceKind.Headphones, snapshot.Kind);
        }

        [Fact]
        public void Kind_uses_the_le_endpoint_when_the_classic_one_knows_nothing()
        {
            var snapshot = BluetoothService.BuildSnapshot(Group(
                Endpoint("classic", "x", kind: DeviceKind.Other),
                Endpoint("le", "x", le: true, kind: DeviceKind.Mouse)));

            Assert.Equal(DeviceKind.Mouse, snapshot.Kind);
        }

        [Fact]
        public void Snapshot_is_identified_by_the_group_and_carries_the_container()
        {
            var container = Guid.NewGuid();
            var snapshot = BluetoothService.BuildSnapshot(Group(
                Endpoint("a", "x", container: container),
                Endpoint("b", "x", le: true, container: container)));

            Assert.Equal("container-key", snapshot.Id);
            Assert.Equal(container, snapshot.ContainerId);
        }
    }
}
