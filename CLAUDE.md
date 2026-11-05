# BlueDock

Windows system-tray Bluetooth popup (WPF, .NET 8). See README.md for features and layout.

## Build / run
- `dotnet build` to compile, `dotnet run` to start (the app lives in the tray; exit via right-click > Afsluiten)
- Kill a running instance before rebuilding: `taskkill /IM BlueDock.exe /F` (otherwise the exe is locked)
- Target: `net8.0-windows10.0.19041.0` (WinRT APIs: Windows.Devices.Enumeration, Bluetooth, Radios)

## Architecture
- Services/BluetoothService.cs: DeviceWatchers (Classic + LE, AssociationEndpoint), battery via DEVPKEY_Bluetooth_Battery and GATT 0x180F. Events fire on background threads; MainViewModel marshals to the UI thread.
- Device type comes from System.Devices.Aep.Category (the classification Windows itself uses:
  "Audio.Headphone", "Communication.Headset.Bluetooth", "Input.Mouse", ...), requested through
  the watcher. Do NOT use System.Devices.Category on the container: it is the localized display
  text and comes back empty for Bluetooth. The container property that does hold data is
  System.Devices.CategoryIds. Name guessing is only a last resort.
- Themes: a look is built from three merged dictionaries. Index 0 Shared.xaml (styles, all
  DynamicResource), index 1 a palette (<Theme>.Light.xaml / <Theme>.Dark.xaml, colours only),
  index 2 the theme structure (<Theme>.xaml: templates, radii, spacing). ThemeService swaps
  1 and 2 together. A colour belongs in the palette, never in a template: a hardcoded colour
  silently breaks the other mode. Both palettes of a theme must define exactly the same keys.
- Light/dark: ThemeMode is Auto (default), Light or Dark. Auto follows Windows via
  Services/SystemTheme.cs, which reads AppsUseLightTheme from the registry (there is no WinRT
  API for it in a desktop app) and listens to SystemEvents.UserPreferenceChanged on a system
  thread, so handlers marshal to the UI thread themselves.
- Icons: Tabler geometries in Themes/Icons.xaml, drawn by Controls/TablerIcon.cs (stroke only,
  24x24 grid, Foreground is inherited). Icons.xaml is merged from inside Shared.xaml so the
  theme stays at index 1. There is no icon font: do not reintroduce Segoe glyph code points.
  DeviceViewModel.IconKey maps a DeviceKind to a key; Icon resolves it.
- Interop/WindowEffects.cs: DWM Mica/Acrylic (build >= 22621), fallback brush otherwise.
- Interop/CoreAudio.cs: COM interop reaching the KS filter behind a Bluetooth audio endpoint
  (IMMDevice -> IDeviceTopology -> IConnector -> IKsControl). IKsControl is NOT available on the
  endpoint itself; the topology must be walked to the filter first. Used for connect/disconnect.
- Connect/disconnect: BluetoothService.SetConnectedAsync. ConnectAbility per device decides what
  the UI offers (audio = both ways, LE = connect only, rest = nothing). Both mechanisms only
  request a state; the result is confirmed by waiting on the DeviceWatcher, never by the HRESULT.
- Notifications are tray balloons (TrayService.Notify). Windows toasts were tried and reverted:
  an unpackaged exe gets past CreateToastNotifier once an AUMID plus COM activator are registered,
  but Windows still never stores or shows the notification (verified against wpndatabase.db)
  unless the app is properly installed. ToastNotifier.Setting reports Enabled regardless, and
  Show() does not throw, so neither can be used to detect delivery. Read the Notifications
  section in README.md before attempting this again.
- Settings: %AppData%\BlueDock\settings.json

## Conventions
- UI text in Dutch, code and comments in English
- ImplicitUsings is disabled (WinForms + WPF name clashes): add explicit usings
- WinForms is only used for the NotifyIcon; alias it as `Forms`
- Never estimate missing data (e.g. battery): show nothing instead
- Prefer values from hardware/settings over hardcoded ones
