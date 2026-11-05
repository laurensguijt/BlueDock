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
  - Notifications on connect, on disconnect, on low battery, as Windows toasts carrying the
    device's own icon (see *Notifications* below)
  - Start with Windows
- Settings are stored in `%AppData%\BlueDock\settings.json`

## Install

Grab the newest [release](https://github.com/laurensguijt/BlueDock/releases):

- **`BlueDock-x.y.z-setup.exe`** installs for the current user, so it never asks for
  administrator rights, and adds a Start Menu entry. Uninstall through Windows Settings.
- **`BlueDock-x.y.z-portable.exe`** runs from anywhere without installing. The first start takes
  a moment while it unpacks itself.

Both carry their own .NET, so nothing has to be installed first. BlueDock lives in the system
tray; "Starten met Windows" is a setting inside the app, not something the installer arranges.

Windows 10 2004 (build 19041) or newer. Mica and Acrylic backdrops need Windows 11 22H2 or
newer; older builds fall back to a solid background.

## Build and run

Needs the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
dotnet run
```

The release builds are produced by [the workflow](.github/workflows/build.yml) on a version tag.
To make one by hand:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

```powershell
iscc Installer\BlueDock.iss /DAppVersion=0.1.0 /DSourceExe=..\publish\BlueDock.exe
```

The installer needs [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install
JRSoftware.InnoSetup`) and writes to `dist\`. Note that winget puts `ISCC.exe` under
`%LocalAppData%\Programs`, not in Program Files.
Cutting a release is pushing a tag: `git tag v0.1.0 && git push origin v0.1.0`.

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

Connect, disconnect and low battery show a Windows toast carrying the device's icon, drawn from
the same Tabler set the panel uses: white on a filled circle, so it reads on a light and on a
dark toast alike. The images are generated once into `%AppData%\BlueDock\toast-icons`.

Toasts only work when BlueDock is **installed**. Windows accepts a notification from an
unpackaged app only for an application user model id it can resolve, and the installer's Start
Menu shortcut is what provides one (`LaurensGuijt.BlueDock`). Without it `CreateToastNotifier`
fails with `ERROR_NOT_FOUND` (`0x80070490`). So the portable build, and a run straight from the
build folder, fall back to a tray balloon. On Windows 11 the shell draws those through the same
notification UI, so the visible difference is the app icon instead of the device icon.

BlueDock decides which of the two it uses once at startup, by checking that the shortcut exists
and that a notifier can be created, and writes the outcome to
`%AppData%\BlueDock\notifications.log`. Deciding up front matters, because there is no reliable
way to tell afterwards whether a toast arrived:

- **`Show()` returning without throwing proves nothing.** It returns happily while Windows
  discards the notification, which is how an earlier attempt managed to show nothing at all.
- **The notification store is not proof either.** `wpndatabase.db` under
  `%LocalAppData%\Microsoft\Windows\Notifications` misses toasts that were really displayed, so
  reading it gives false negatives and sent this work down a blind alley twice.

Which leaves looking at the screen. Worth remembering before trusting a clever check.

Clicking a toast does not open the panel. That needs a COM activator registered alongside the
AUMID, which is not worth it for a tray app that is one click away anyway.

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
