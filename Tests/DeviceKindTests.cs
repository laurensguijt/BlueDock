using System;
using BlueDock.Services;
using Xunit;

namespace BlueDock.Tests
{
    /// <summary>
    /// Mapping Windows' own device categories onto our icons. These dotted ids are what
    /// System.Devices.Aep.Category returns; guessing from the name is only a fallback.
    /// </summary>
    public class DeviceKindTests
    {
        [Theory]
        [InlineData("Audio.Headphone", DeviceKind.Headphones)]
        [InlineData("Communication.Headset.Bluetooth", DeviceKind.Headphones)]
        [InlineData("Audio.Speaker", DeviceKind.Speaker)]
        [InlineData("Input.Mouse", DeviceKind.Mouse)]
        [InlineData("Input.Keyboard", DeviceKind.Keyboard)]
        [InlineData("Input.Gaming", DeviceKind.Gamepad)]
        [InlineData("Communication.Phone", DeviceKind.Phone)]
        [InlineData("Computer.Desktop", DeviceKind.Computer)]
        public void Recognises_the_categories_windows_reports(string category, DeviceKind expected) =>
            Assert.Equal(expected, BluetoothService.KindFromCategories(new[] { category }));

        [Fact]
        public void Unknown_category_stays_unknown()
        {
            // Better a generic Bluetooth icon than a confidently wrong one.
            Assert.Equal(DeviceKind.Other, BluetoothService.KindFromCategories(new[] { "Sensor.Thermometer" }));
            Assert.Equal(DeviceKind.Other, BluetoothService.KindFromCategories(Array.Empty<string>()));
        }

        [Fact]
        public void Matching_is_case_insensitive()
        {
            Assert.Equal(DeviceKind.Mouse, BluetoothService.KindFromCategories(new[] { "INPUT.MOUSE" }));
        }

        [Fact]
        public void The_first_category_decides()
        {
            // Windows lists the primary category first. A headset also reports plain "Audio",
            // and a phone reports "Audio" as well; order is what tells them apart.
            Assert.Equal(DeviceKind.Headphones,
                BluetoothService.KindFromCategories(new[] { "Communication.Headset.Bluetooth", "Audio" }));
            Assert.Equal(DeviceKind.Phone,
                BluetoothService.KindFromCategories(new[] { "Communication.Phone", "Audio" }));
        }

        [Fact]
        public void Falls_back_to_the_name_only_when_the_category_says_nothing()
        {
            Assert.Equal(DeviceKind.Headphones, BluetoothService.KindFromName("Galaxy Buds"));
            Assert.Equal(DeviceKind.Mouse, BluetoothService.KindFromName("MX Master 3S"));
            Assert.Equal(DeviceKind.Other, BluetoothService.KindFromName("Zone 305"));
        }
    }
}
