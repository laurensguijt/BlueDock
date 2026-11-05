# BlueDock

A small, modern Bluetooth popup for the Windows system tray.

Click the tray icon and a compact panel opens next to the clock with your Bluetooth devices,
their connection status and battery level, and a button to pair a new device.

## Features

- Tray icon: left click opens/closes the popup, right click gives a menu (open, Windows Bluetooth settings, exit)
- Live list of paired devices (Classic and Bluetooth LE), split into **Verbonden** and **Gekoppeld**
- Connect and disconnect from the panel: click a disconnected device to connect it, or use
  **Verbreken** on a connected one. What is offered depends on what Windows actually supports
  per device (see *Connecting* below); devices that cannot be steered simply do not react.
- Battery level when Windows or the device reports it (Windows battery property, or the GATT Battery Service for LE devices)
- Bluetooth radio on/off switch in the header
- **Apparaat toevoegen** opens the Windows "Add a device" dialog
- Settings (gear icon):
  - Theme: Windows 11 Fluent, Liquid Glass, Soft Tiles (switches live)
  - Mode: Licht, Donker or Automatisch. Automatisch follows the Windows colour setting and
    changes along with it while running; it is the default on a fresh install. Every theme
    has both a light and a dark palette.
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
Interop/
  WindowEffects.cs         DWM: Mica/Acrylic, dark mode, rounded corners
  CoreAudio.cs             Core Audio + Kernel Streaming interop for audio connect/disconnect
Controls/
  BatteryRing.cs           circular battery indicator (Tiles theme)
  TablerIcon.cs            draws an icon from Themes/Icons.xaml
  Converters.cs            shared value converters
Themes/
  Shared.xaml              theme independent styles (buttons, switch, dropdown)
  Icons.xaml               Tabler icon geometries
  Fluent.xaml              theme structure: templates, radii, spacing
  Fluent.Light/.Dark.xaml  the two palettes of that theme (colours only)
  Fluent.xaml / Glass.xaml / Tiles.xaml
```

### Adding a theme

A theme is a structure file plus two palettes. Copy `Fluent.xaml` for the structure (templates,
radii, spacing), copy `Fluent.Light.xaml` and `Fluent.Dark.xaml` for the colours, and add the
theme to `ThemeService.Available`. Keep every colour in the palettes: a colour written straight
into a template looks right in one mode and wrong in the other. Both palettes must define the
same keys.

## Connecting

Windows has no general "connect this paired device" API, so BlueDock uses what is available
per device type and offers nothing where there is no working mechanism:

| Device | Connect | Disconnect | How |
| --- | --- | --- | --- |
| Audio (headset, speaker) | yes | yes | `KSPROPERTY_ONESHOT_RECONNECT` / `_DISCONNECT` on the Kernel Streaming filter behind the device's audio endpoint, reached through Core Audio (`IMMDevice` → `IDeviceTopology` → `IKsControl`) |
| Bluetooth LE (mouse, tracker) | yes | no | a `GattSession` with `MaintainConnection`, held while the device is connected. Windows owns the link, so it cannot be forced down again |
| Other classic (phone, …) | no | no | no supported mechanism |

Both mechanisms only *request* a state change. BlueDock then waits for the DeviceWatcher to
confirm the new state (12s for connect, 8s for disconnect) and reports a failure on the card
if the device never gets there, which is usually a device that is switched off or out of range.

## Notifications

Connect, disconnect and low battery show a tray balloon.

Modern Windows toasts, with the device icon, were tried and did not work for a portable exe.
The findings are kept here so nobody has to repeat the afternoon:

- An unpackaged app cannot simply ask for a toast. Windows only accepts one from an application
  user model id registered, together with a COM activator, under `HKCU\Software\Classes`;
  without that, `CreateToastNotifier` fails with `ERROR_NOT_FOUND` (`0x80070490`).
- Registering it by hand, with a Start Menu shortcut carrying the AUMID and a
  `ToastActivatorCLSID`, was not enough. `CommunityToolkit.WinUI.Notifications` does get past
  `CreateToastNotifier`, but that only means the call returned.
- Nothing was ever delivered. The Windows notification store (`wpndatabase.db`, under
  `%LocalAppData%\Microsoft\Windows\Notifications`) held no handler and no notification for the
  app in any of these variants, while `ToastNotifier.Setting` still reported `Enabled`. No group
  policy blocks notifications on the machine this was tested on.
- The unpackaged apps that do get toasts there (PowerToys, Outlook) are properly installed, with
  a Start Menu shortcut Windows has long since indexed. That is the difference, and it is not
  something a portable exe run from a build folder can arrange on the spot.

So this needs an installer, not more interop. **Do not judge a toast by whether `Show()` threw**:
it returns happily while Windows discards the notification, which also defeats any fallback that
trusts it. Check `wpndatabase.db` instead.

## Roadmap

- Toast notifications with device icon, once there is an installer that registers the app

## Contributing

Issues and pull requests are welcome. Keep to the conventions in `CLAUDE.md`:
UI text in Dutch, code and comments in English, explicit `using` directives
(`ImplicitUsings` is disabled), and never show estimated data such as a guessed
battery level — leave it out instead.

## Credits

Icons are [Tabler Icons](https://tabler.io/icons) (MIT, Copyright (c) 2020-2024 Paweł Kuna),
embedded as path geometries in `Themes/Icons.xaml`. Nothing has to be installed for them: they
are vector data, so they scale at any DPI, follow the theme colour, and work the same on
Windows 10, which has no Segoe Fluent Icons font.

## License

[MIT](LICENSE)
