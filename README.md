# BlueDock

A small, modern Bluetooth popup for the Windows system tray.

Click the tray icon and a compact panel opens next to the clock with your Bluetooth devices,
their connection status and battery level, and a button to pair a new device.

## Features

- Tray icon: left click opens/closes the popup, right click gives a menu (open, Windows Bluetooth settings, exit)
- Live list of paired devices (Classic and Bluetooth LE), split into **Verbonden** and **Gekoppeld**
- Battery level when Windows or the device reports it (Windows battery property, or the GATT Battery Service for LE devices)
- Bluetooth radio on/off switch in the header
- **Apparaat toevoegen** opens the Windows "Add a device" dialog
- Settings (gear icon):
  - Theme: Windows 11 Fluent, Liquid Glass, Soft Tiles (switches live)
  - Notifications on connect, on disconnect, on low battery
  - Start with Windows
- Settings are stored in `%AppData%\BlueDock\settings.json`

## Requirements

- Windows 10 2004 (build 19041) or newer. Mica / Acrylic backdrops need Windows 11 22H2 or newer; older builds use a solid fallback.
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) to build

## Build and run

```powershell
cd BlueDock
dotnet run
```

Single portable exe:

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
# output: bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\BlueDock.exe
```

## Project layout

```
App.xaml(.cs)              startup, single instance, wiring
Views/PopupWindow.xaml     the popup (main panel + settings panel)
ViewModels/                MainViewModel, DeviceViewModel
Services/
  BluetoothService.cs      DeviceWatchers, connection + battery tracking
  RadioService.cs          Bluetooth adapter on/off
  TrayService.cs           tray icon, menu, notifications
  ThemeService.cs          runtime theme switching
  SettingsService.cs       settings.json
  AutoStart.cs             HKCU Run key
Interop/WindowEffects.cs   DWM: Mica/Acrylic, dark mode, rounded corners
Controls/                  BatteryRing, converters
Themes/
  Shared.xaml              theme independent styles (buttons, switch, dropdown)
  Fluent.xaml / Glass.xaml / Tiles.xaml
```

### Adding a theme

Copy one of the theme files in `Themes/`, change brushes and templates, and add it to
`ThemeService.Available`. Every key used by the window is defined in each theme file.

## Roadmap

- Connect / disconnect buttons per device (audio devices via the Core Audio KS reconnect property)
- Follow the Windows light/dark setting for the Fluent theme
- Windows toast notifications with device icon

## Contributing

Issues and pull requests are welcome. Keep to the conventions in `CLAUDE.md`:
UI text in Dutch, code and comments in English, explicit `using` directives
(`ImplicitUsings` is disabled), and never show estimated data such as a guessed
battery level — leave it out instead.

## License

[MIT](LICENSE)
