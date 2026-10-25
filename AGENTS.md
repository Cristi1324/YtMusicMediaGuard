# AGENTS.md

Windows tray app and unpacked Chrome extension. While a YouTube Music tab is open, Volume Up, Volume Down, and Mute from the keyboard control the player. The Logitech Pro X 2 Lightspeed volume wheel stays with Windows. Play, next, and previous stay with Chrome, except when Remote Desktop or a game is focused on this PC.

## Layout

- `src/YtMusicMediaGuard` is the .NET 8 Windows Forms app. `Program.cs` chooses the tray, the native host, the elevated hook, or the self-test. The rest of the source is grouped by role:
  - `App` — config, paths, logging, and sign-in startup.
  - `Tray` — tray icon, menu, settings window, and theme.
  - `Input` — media-key hook, shortcuts, volume restore, and focus detection. `Input/Headsets` matches receivers from the detection strings in config.
  - `Bridge` — named pipe, native messaging, and commands sent to the extension.
  - `Chrome` — native-host registration and the stable extension id.
  - `ElevatedHook` — the administrator helper used while an elevated app is focused.
  - `Testing` — the self-test.
- `extension` is the Manifest V3 extension loaded unpacked. The `key` in `extension/manifest.json` is the Chrome extension public key. It keeps the id `mbehpmabnicdenlcifcenanlcijcecgk`. Leave it in place.
- `publish.ps1` builds the self-contained `win-x64` zip.
- `.github/workflows/release.yml` publishes that zip when a `v*` tag is pushed.

The app runs in the signed-in user's session so it can see the desktop keyboard. Read `README.md` before changing setup, registration, or key handling. The GitHub repository is GPL-3.0. `LICENSE` is already on `main`. Keep that file. Do not add a second license.

`Program.cs` is the entry point:

- No arguments, or `--tray`: the tray app. A single-instance mutex is `Local\YtMusicMediaGuard`.
- Stdin is a pipe, and the process was not started with `--tray`: the Chrome native host `com.ytmusic.mediaguard`.
- `--hook`: the elevated helper. Its mutex is `Local\YtMusicMediaGuard.Hook`.
- `--install-hook`: registers scheduled task `YtMusicMediaGuard.Hook` and starts that helper. This path is started with a one-time consent prompt.
- `--self-test`: the checks in `Testing`.

The namespace stays `YtMusicMediaGuard` in every folder. Volume and mute from the keyboard are sent to the extension. `Input/HeadsetVolume.cs` watches the device. A receiver stays on Windows volume when its id or name contains a string in `HeadsetDetections`, and not a string in `HeadsetExclusions`. The defaults are `PID_0ABA`, `PID_0AF7`, `PRO X Wireless`, and `PRO X 2 LIGHTSPEED`. Play, next, and previous are passed through to Chrome unless `Input/FocusDetector.cs` reports a game or a Remote Desktop client. `extension/content.js` pauses the video element when the pause button says paused and playback is still advancing. The stored Tweaks chords are not typed by the live hook. The elevated helper handles keys only while the foreground process is elevated, because the tray hook cannot see those keys.

## Logging

`Logger.Error` always appends to `%APPDATA%\YtMusicMediaGuard\guard.log`. That includes failures and the elevated-hook lines (`Elevated hook connected`, `Elevated hook is running`, registration and permission failures). `Logger.Info` appends only in a Debug build, including `Armed` and `Extension host connected`. Do not log executable paths, profile ids, window titles, or shortcut values. The extension has no console logging.

## Checks

```powershell
dotnet run --project src\YtMusicMediaGuard\YtMusicMediaGuard.csproj -- --self-test
```

`publish.ps1` is for a release package. `bin/`, `obj/`, and `dist/` stay untracked.
