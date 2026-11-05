# BlueDock

Windows system-tray Bluetooth popup (WPF, .NET 8). See README.md for features and layout.

## Build / run
- `dotnet build` to compile, `dotnet run` to start (the app lives in the tray; exit via right-click > Afsluiten)
- Kill a running instance before rebuilding: `taskkill /IM BlueDock.exe /F` (otherwise the exe is locked)
- Target: `net8.0-windows10.0.19041.0` (WinRT APIs: Windows.Devices.Enumeration, Bluetooth, Radios)

## Architecture
- Services/BluetoothService.cs: DeviceWatchers (Classic + LE, AssociationEndpoint), battery via DEVPKEY_Bluetooth_Battery and GATT 0x180F. Events fire on background threads; MainViewModel marshals to the UI thread.
- Themes: Shared.xaml (styles, all DynamicResource) + one active theme (Fluent / Glass / Tiles) swapped at merged-dictionary index 1 by ThemeService. Every theme must define the same resource keys.
- Interop/WindowEffects.cs: DWM Mica/Acrylic (build >= 22621), fallback brush otherwise.
- Settings: %AppData%\BlueDock\settings.json

## Conventions
- UI text in Dutch, code and comments in English
- ImplicitUsings is disabled (WinForms + WPF name clashes): add explicit usings
- WinForms is only used for the NotifyIcon; alias it as `Forms`
- Never estimate missing data (e.g. battery): show nothing instead
- Prefer values from hardware/settings over hardcoded ones
