# BlueDock

A small Bluetooth panel for the Windows system tray. Click the tray icon and a compact window
opens next to the clock: your devices, whether they are connected, their battery level, and a
button to connect or disconnect them.

## Why

Managing Bluetooth on Windows means opening Settings, waiting for the page, scrolling to the
device and clicking through. For something you do several times a day — headset on, headset
off — that is a lot of clicks for very little.

I built BlueDock for myself to make that one click. It is shared here because the problem is
not mine alone, and because the few existing tray tools each miss something: they are a context
menu rather than a panel, or audio only, or they only watch battery levels.

It is a personal project, not a product. It does what I need it to do and it is maintained when
something annoys me. Issues and pull requests are welcome, but there is no roadmap promise
behind it.

> **The interface is in Dutch.** The code and documentation are in English. If you would use an
> English interface, say so in an issue; the strings are not scattered, so it is a modest change.

## What it does

- **One click from the tray.** Left click opens the panel, right click gives a small menu
  (open, Windows Bluetooth settings, exit).
- **Your devices at a glance**, split into connected and merely paired, with the type icon
  Windows itself uses for them.
- **Connect and disconnect from the panel.** Click a disconnected device to connect it, or use
  the button on a connected one. What is offered depends on what Windows genuinely supports per
  device; see [Connecting](#connecting).
- **Battery level** where Windows or the device reports one. Never estimated: if there is no
  reading, nothing is shown.
- **Bluetooth on or off** with the switch in the header.
- **Send the sound here.** A connected audio device gets a speaker button that makes it the
  Windows playback device, for media and for calls, and the button lights up on whichever
  device sound is currently going to. Windows has no supported API for this, so it goes through
  the same undocumented interface every audio switcher uses, and the result is verified by
  reading the default back rather than trusting the call.
- **Notifications** when a device connects, disconnects, or runs low, carrying the device's own
  icon. See [Notifications](#notifications).
- **Three themes**, each in light and dark, following the Windows colour setting by default.
- **Pairing** opens the Windows "Add a device" dialog, which is the one part Windows does well.
- **Update check.** On start BlueDock asks GitHub whether a newer release exists and, if so,
  offers a bar in the panel that downloads and runs the installer for you. It only ever
  downloads from this repository's releases, never offers a downgrade, and can be switched off
  under *Algemeen*.

Settings live in `%AppData%\BlueDock\settings.json`.

## Install

Grab the newest [release](https://github.com/laurensguijt/BlueDock/releases):

- **`BlueDock-x.y.z-setup.exe`** installs for the current user, so it never asks for
  administrator rights, and adds a Start Menu entry. Uninstall through Windows Settings.
  Installing over a running copy works; it closes the old one for you.
- **`BlueDock-x.y.z-portable.exe`** runs from anywhere without installing. The first start takes
  a moment while it unpacks itself, and notifications fall back to tray balloons
  ([why](#notifications)).

Both carry their own .NET, so nothing needs to be installed first. That is also why they are
around 70 MB.

The installer is not code signed, so Windows SmartScreen will warn the first time. "More info"
then "Run anyway" gets past it, or build it yourself from source.

Requires Windows 10 2004 (build 19041) or newer. The Mica and Acrylic backdrops need Windows 11
22H2 or newer; older builds get a solid background instead.

"Start with Windows" is a setting inside the app, not something the installer arranges.

## Connecting

Windows has no general "connect this paired device" API. BlueDock uses what is actually
available per device type, and offers nothing where there is no working mechanism, rather than
a button that quietly does nothing:

| Device | Connect | Disconnect | How |
| --- | --- | --- | --- |
| Audio (headset, speaker) | yes | yes | `KSPROPERTY_ONESHOT_RECONNECT` / `_DISCONNECT` on the Kernel Streaming filter behind the device's audio endpoint, reached through Core Audio (`IMMDevice` → `IDeviceTopology` → `IKsControl`) |
| Bluetooth LE (mouse, tracker) | yes | no | a `GattSession` with `MaintainConnection`, held while the device is connected. Windows owns the link, so it cannot be forced down again |
| Other classic (phone, …) | no | no | no supported mechanism |

Both mechanisms only *request* a state change. BlueDock then waits for the DeviceWatcher to
confirm it (12s for connect, 8s for disconnect) and says so on the card when the device never
gets there — usually one that is switched off or out of range.

A dual mode device registers twice with Windows, once classic and once LE. Those are folded
into a single entry by container id, the way Windows does it, so a headset is listed once and
connecting it raises one notification rather than two.

## Notifications

Connect, disconnect and low battery show a Windows toast with the device's icon, drawn from the
same icon set as the panel: white on a filled circle, so it reads on a light and a dark toast
alike.

Toasts only work when BlueDock is **installed**. Windows accepts a notification from an
unpackaged app only under an application user model id it can resolve, and the installer's
Start Menu shortcut is what provides one. Without it, `CreateToastNotifier` fails with
`ERROR_NOT_FOUND`. The portable build therefore falls back to a tray balloon — on Windows 11
the shell draws those through the same notification UI, so the visible difference is the app
icon instead of the device icon. Which route is in use is written to
`%AppData%\BlueDock\notifications.log`.

If you are tempted to detect whether a toast arrived: `Show()` returns happily while Windows
discards the notification, and the notification store misses toasts that really appeared. Both
of those sent this project down a blind alley. Decide up front whether toasts are possible, as
BlueDock does.

## Build and run

Needs the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
dotnet run
```

Releases are built by [the workflow](.github/workflows/build.yml) when a version tag is pushed:

```powershell
git tag v0.1.0; git push origin v0.1.0
```

To build the installer by hand you also need [Inno Setup 6](https://jrsoftware.org/isinfo.php)
(`winget install JRSoftware.InnoSetup`, which puts `ISCC.exe` under `%LocalAppData%\Programs`
rather than in Program Files):

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

```powershell
iscc Installer\BlueDock.iss /DAppVersion=0.1.0 /DSourceExe=..\publish\BlueDock.exe
```

Kill a running instance before rebuilding, or the exe stays locked:

```powershell
taskkill /IM BlueDock.exe /F
```

## Project layout

```
App.xaml(.cs)              startup, single instance, wiring
Views/PopupWindow.xaml     the popup (main panel + settings panel)
ViewModels/                MainViewModel, DeviceViewModel
Services/
  BluetoothService.cs      DeviceWatchers, grouping, connection + battery tracking
  RadioService.cs          Bluetooth adapter on/off
  TrayService.cs           tray icon, menu, notification routing
  ToastService.cs          Windows toasts; ToastIcons renders the device icon
  ThemeService.cs          runtime theme and light/dark switching
  SystemTheme.cs           the Windows colour setting
  SettingsService.cs       settings.json
  AutoStart.cs             HKCU Run key
Interop/
  WindowEffects.cs         DWM: Mica/Acrylic, dark mode, rounded corners
  CoreAudio.cs             Core Audio + Kernel Streaming, for audio connect/disconnect
Controls/
  BatteryRing.cs           circular battery indicator (Tiles theme)
  TablerIcon.cs            draws an icon from Themes/Icons.xaml
  Converters.cs            shared value converters
Themes/
  Shared.xaml              theme independent styles (buttons, switch, dropdown)
  Icons.xaml               icon geometries
  <Theme>.xaml             theme structure: templates, radii, spacing
  <Theme>.Light/.Dark.xaml the two palettes of that theme, colours only
Installer/BlueDock.iss     Inno Setup script
```

### Adding a theme

A theme is one structure file plus two palettes. Copy `Fluent.xaml` for the structure
(templates, radii, spacing), copy `Fluent.Light.xaml` and `Fluent.Dark.xaml` for the colours,
and add the theme to `ThemeService.Available`. Keep every colour in the palettes: a colour
written straight into a template looks right in one mode and wrong in the other. Both palettes
must define the same keys.

## Contributing

Issues and pull requests are welcome. The conventions, and the reasoning behind the awkward
parts of the Windows APIs, are in [CLAUDE.md](CLAUDE.md) — worth reading before touching the
Bluetooth or notification code, because several of those behaviours are not what they look like.

In short: interface text in Dutch, code and comments in English, explicit `using` directives
(`ImplicitUsings` is off), and never show estimated data such as a guessed battery level —
leave it out instead.

## Credits

Icons are [Tabler Icons](https://tabler.io/icons) (MIT, Copyright © 2020-2024 Paweł Kuna),
embedded as path geometries in `Themes/Icons.xaml`. Nothing has to be installed for them: they
are vector data, so they scale at any DPI, follow the theme colour, and work the same on
Windows 10, which has no Segoe Fluent Icons font.

## License

[MIT](LICENSE)
