using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.Radios;

namespace BlueDock.Services
{
    /// <summary>Reads and switches the Bluetooth radio (adapter) on/off.</summary>
    public sealed class RadioService
    {
        private Radio? _radio;

        public event EventHandler? StateChanged;

        public bool IsAvailable => _radio != null;
        public bool IsOn => _radio?.State == RadioState.On;

        public async Task InitializeAsync()
        {
            try
            {
                var access = await Radio.RequestAccessAsync();
                if (access != RadioAccessStatus.Allowed)
                    return;

                var radios = await Radio.GetRadiosAsync();
                _radio = radios.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
                if (_radio != null)
                    _radio.StateChanged += (s, e) => StateChanged?.Invoke(this, EventArgs.Empty);
            }
            catch
            {
                _radio = null;
            }
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public async Task SetAsync(bool on)
        {
            if (_radio == null) return;
            try
            {
                await _radio.SetStateAsync(on ? RadioState.On : RadioState.Off);
            }
            catch
            {
                // Can be blocked by policy or airplane mode; the UI re-reads the real state.
            }
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
