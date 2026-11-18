<p align="center">
  <img src="assets/wakelight.png" width="120" alt="Wakelight logo">
</p>

<h1 align="center">Wakelight</h1>
<p align="center"><b>Your live wallpaper, awake while you're away.</b></p>

Wakelight is a small Windows companion for [Wallpaper Engine](https://www.wallpaperengine.io/).
When you step away from your PC, it plays your live wallpaper full screen with a clock,
keeps the Windows lock screen matched to it, and locks the PC when you come back.

<p align="center">
  <img src="docs/installer-welcome.png" width="640" alt="Wakelight setup">
</p>

## Features

- **Idle showcase.** After a few minutes idle, your Wallpaper Engine scene, video or web wallpaper fills every monitor, live, at full frame rate, with an optional clock and date. Move the mouse or press a key to return.
- **Lock when you return.** Back after your chosen lock time? You land on the Windows sign-in screen. Set it to *Never* if you don't want that.
- **Matching lock screen.** Wakelight captures a sharp still of your wallpaper during each showcase and sets it as the Windows lock screen.
- **Showcase-and-lock hotkey.** Press <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>L</kbd> (customizable) to show your wallpaper now and lock when you return.
- **Polite by default.** Waits while a full-screen app (video, game) is open, can be paused for 30 min / 1 h / 3 h, and hides desktop icons only while showing.
- **Live preview** of the current wallpaper and an idle timeline in the control window.
- **Tray icon** with quick actions, and start with Windows.
- **Finds Wallpaper Engine automatically** (from the running app or your Steam libraries), or point it to the folder yourself.

## How it works

Wallpaper Engine renders into the desktop window. Wakelight shows that live render in its own
full-screen window through a DWM thumbnail, so nothing is re-encoded and Wallpaper Engine's
own settings, effects and audio-reactive features are preserved. Wakelight never modifies
Wallpaper Engine; it only reads its `config.json`.

The Windows lock screen itself can't play video (it runs on a separate, locked-down desktop),
so Wakelight shows the live wallpaper *before* the lock and sets a matching still *on* the lock screen.

## Install

Download `WakelightSetup.exe` from the [Releases](../../releases) page and run it. Setup:

- installs to `C:\Program Files\Wakelight`,
- adds Start menu / desktop shortcuts and the startup entry (each optional),
- grants your account permission to set the lock screen image (this is why setup asks for admin once).

Uninstall from **Settings → Apps** or by running setup again. Uninstalling gives the lock screen back to Windows.

## Build from source

No SDK required: Wakelight builds with the C# compiler that ships with Windows (.NET Framework 4.8).

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

Output: `dist\Wakelight.exe` and `dist\WakelightSetup.exe` (the installer embeds the app).
`tools\make-icon.ps1` regenerates the logo and icon.

```
src/Wakelight   the app: engine, showcase, lock screen sync, control window
src/Setup       the installer
src/Shared      the Wakelight theme (colors, switches, sliders, buttons)
assets          logo and icon
```

## Requirements

Windows 10 or 11 and Wallpaper Engine.

## License

[MIT](LICENSE). Wakelight is an independent project and is not affiliated with Wallpaper Engine.
