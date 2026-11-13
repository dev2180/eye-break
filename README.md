# Eye Break

A 20-20-20 eye-rest app for Windows. Every 20 minutes your eyes close over the whole screen like settling into
meditation, you fall into a ray-traced black hole for 20 seconds, a white star is born in the darkness, and the
third eye opens you back to your screen. Your apps keep running underneath.

![The black hole you fall into during a break](docs/blackhole.png)

**[Download EyeBreak-Setup.exe](https://github.com/dev2180/eye-break/releases/latest)** — one file, no admin rights
or .NET needed. If Windows says "Windows protected your PC", click **More info → Run anyway** (it isn't code-signed yet).

<img src="docs/installer.png" alt="The Eye Break installer" width="520">

## Features

- **The break:** native eyelid animation, then a real-time ray-traced black hole (gravitational lensing, Doppler
  beaming, turbulent accretion disk) with a glowing ring timer and guidance lines, then the white star and the third eye.
- **Infinite mode:** float by the black hole with YouTube music (presets or your own link), a music bar with
  previous / play-pause / next and text-only YouTube search, and calm quotes. Press **Esc** to return.
- **Tray panel:** time to next break, take a break now, skip breaks (30 min, 1 h, 2 h, until resume), infinite mode,
  and settings (interval, break length, start with Windows, hide from screen sharing).
- **Always on top:** covers every monitor; blocks the Windows key, Alt+Tab, Alt+Esc, Ctrl+Esc and Alt+F4 during a
  break while normal typing still reaches your app.
- **Exclusive-fullscreen games:** fades the display output (gamma ramp) as a fallback, always restored afterwards.
- **Screen sharing:** breaks are hidden from screen sharing and recordings (on by default).

## Build and run

Needs Windows 10/11, the .NET 9 SDK and the WebView2 runtime (built into Windows 11).

```
dotnet build -c Release
bin\Release\net9.0-windows\EyeBreak.exe
```

Testing flags: `--break-now`, `--infinite-now`, `--show-panel`, `--show-in-capture`, `--force-dim`.

## Installer

```
powershell -ExecutionPolicy Bypass -File build-installer.ps1
```

This makes `dist\EyeBreak-Setup.exe`, a single file anyone can run. It needs no admin rights and no .NET install,
because the app and its runtime are packed inside. It installs to `%LOCALAPPDATA%\Programs\EyeBreak` and adds a Start
menu shortcut, an optional desktop shortcut, an optional start-with-Windows entry, and an uninstaller listed in
Settings › Apps › Installed apps. While it runs it plays ambient music it generates itself (♪ mutes it).

## Notes

- Ctrl+Alt+Del → Task Manager can always end the app. If the screen ever stays dim, Win+Ctrl+Shift+B resets it.
- To let the fullscreen-game fallback dim fully to black, set
  `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM\GdiIcmGammaRange = 256` (DWORD) as administrator.
- Settings: `%AppData%\EyeBreak\settings.json`; log: `%AppData%\EyeBreak\log.txt`.
- `preview/` holds the browser prototypes used while designing the animation.
