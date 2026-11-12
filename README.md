# DisplayMGR

A Windows tray app for controlling monitors without touching their buttons: brightness,
input switching, picture settings and power, plus a small widget that sits on the desktop.

## What it does

- **Brightness** for every monitor, from a panel that opens above the tray. Use the slider or the mouse wheel.
- **Input switching** (HDMI, DisplayPort, USB-C) with one click.
- **More controls** per monitor, where the monitor supports them: contrast, volume and mute,
  sharpness, red/green/blue gain, picture mode, colour preset, standby and power off,
  colour reset and factory reset.
- **Raw VCP console** to read or write any control code, for features that have no dedicated control.
- **Desktop widget** with brightness sliders and input buttons. Drag it anywhere; right-click it for options.
- **Move in unison** to set all monitors to the same brightness level together.
- **Start with Windows**, and automatic re-detection when monitors are plugged in or removed.
- **Laptop panels** are supported for brightness, through Windows rather than DDC/CI.

## Get it

Every push builds a self-contained `DisplayMGR.exe` in GitHub Actions: open the latest
**build** run under the repository's *Actions* tab and download the `DisplayMGR-win-x64` artifact.
It needs Windows 10 or 11 (64-bit) and nothing else installed.

To build it yourself you need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows:

```bash
dotnet publish src/DisplayMGR -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

```bash
dotnet test tests/DisplayMGR.Tests
```

## Using it

- **Left-click the tray icon** to open or close the panel. **Right-click** it for the menu.
- **More** on a monitor card shows every control that monitor offers.
- **Rescan** detects the monitors again and re-reads their feature lists.
- The widget is pinned to the desktop, under your other windows, by default.
  Right-click it and choose **Always on top** to keep it above them instead.

## Limits you should know about

The app talks to external monitors over DDC/CI, so it can only do what a monitor's firmware allows.

- **Not every on-screen-menu item is reachable.** A control appears only if the monitor reports it.
  Vendor-specific features (overdrive, crosshairs, picture-in-picture, KVM switching, HDR toggles)
  usually have no standard code. If you know the code for your model, the raw VCP console can send it.
- **Switching input is one-way.** Once the monitor shows another device, this PC can no longer
  talk to it. Switch back with the monitor's buttons or from the other device.
- **Names of picture modes and inputs may not match your monitor's menu.** They come from the
  VESA standard, and manufacturers do not always follow it.
- **Power off may need the physical button to undo**, because some monitors stop listening once off.
- **If a monitor shows "No response over DDC/CI"**, enable DDC/CI in its on-screen menu.
  Some docks, adapters, KVM switches and TVs block DDC/CI entirely.
- **"Show desktop" (Win+D) hides the widget** while it is pinned to the desktop, until you
  click a window again. Use **Always on top** to avoid that.
- The widget is a desktop window, not a Windows 11 Widgets Board widget.

Settings are stored in `%AppData%\DisplayMGR\settings.json`; errors are logged next to it.

## Layout

| Path | Contents |
| --- | --- |
| `src/DisplayMGR/Monitors` | Monitor detection, DDC/CI and WMI access, capabilities parsing |
| `src/DisplayMGR/ViewModels` | State and commands behind the panel and the widget |
| `src/DisplayMGR/Views` | The panel and the widget windows |
| `src/DisplayMGR/Services` | Settings, start-up registration, tray icon |
| `tests/DisplayMGR.Tests` | Tests for the capabilities parser |
