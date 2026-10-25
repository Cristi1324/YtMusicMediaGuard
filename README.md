# YouTube Music media key guard

Windows tray app for [YouTube Music](https://music.youtube.com) in Chrome. While a YouTube Music tab is open anywhere in Chrome, Volume Up, Volume Down, and Mute change the player instead of Windows. Play, next, and previous keep using Chrome, unless a game or a Remote Desktop window is focused. The companion extension ships in the [`extension`](extension) folder and is loaded unpacked. It is not published in the Chrome Web Store.

A Windows service cannot do this. Services run in Session 0 and never see the desktop keyboard, so this is a normal per-user app that stays in the notification area.

## What the keys do

- Another app on this PC is focused: Play, next, and previous stay with Chrome. Volume Up, Volume Down, and Mute are taken from Windows and applied to the YouTube Music player.
- A game is focused: every media key is taken and applied to the YouTube Music player. A window counts as a game when it covers its monitor, when Windows reports an exclusive fullscreen app, or when Windows Game Bar already knows that program. The first time a game running as administrator is focused, Windows asks permission once. Accept it. The tray app stays a normal user, and a small helper then forwards those keys.
- Remote Desktop (`mstsc.exe`, `msrdc.exe`, or `vmconnect.exe`) is focused on this PC, with YouTube Music playing here: every media key stays on this PC. The extension presses play, next, previous, volume, or mute on the tab that is playing.
- This PC is the Remote Desktop host and the key is pressed on the client: the key arrives in this session. Volume and mute go to the player. Play, next, and previous stay with Chrome.
- No YouTube Music tab is open: every media key is passed through to Windows.

## Setup

Do these steps in order. Each one has a check before you continue.

### 1. Requirements

- Windows 10 or 11, signed in as the user who listens to YouTube Music.
- Google Chrome. Edge, Firefox, and the YouTube Music Windows app are not detected.
- [Tweaks for YouTube](https://chromewebstore.google.com/detail/tweaks-for-youtube/ogkoifddpkoabehfemkolflcjhklmkge) only if you want its extra global actions. Volume and mute do not need it.

Run Chrome and the tray app as your normal user. Do not use **Run as administrator** on the tray app. The first time an administrator window is focused, accept the permission prompt. That starts the only elevated part of the app, a helper that forwards media keys.

### 2. Get the app

**From a GitHub release:** download `YtMusicMediaGuard-win-x64.zip` and unzip it. The folder contains `YtMusicMediaGuard.exe`, `README.md`, and `extension`. That exe includes the .NET runtime.

**From a clone:** install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), then from the repository root:

```powershell
.\publish.ps1
```

The package is written to `dist\YtMusicMediaGuard`. Run `dist\YtMusicMediaGuard\YtMusicMediaGuard.exe`.

The first launch registers three things for the current user:

- Sign-in startup: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\YtMusicMediaGuard`
- Chrome native host: `HKCU\Software\Google\Chrome\NativeMessagingHosts\com.ytmusic.mediaguard`
- Settings: `%APPDATA%\YtMusicMediaGuard\config.json`

A balloon tip appears the first time. After that, look in the notification area. The icon may be under the overflow arrow next to the clock.

| Icon | Meaning |
| --- | --- |
| Green play | Armed. A YouTube Music tab is open. Volume and mute control the player. |
| Gray dash | Idle. No YouTube Music tab is open. Keys control Windows. |
| Orange pause | Paused from the menu. Keys control Windows until you resume. |

The tray menu opens with a left-click or a right-click. It is dark, and so is **Settings…**. The menu contains the current state, **Pause guard** / **Resume guard**, **Start with Windows**, **Settings…**, and **Exit**. **Start with Windows** is on by default.

If you move the exe, run it once from the new folder so the native-host manifest points at that path, then restart Chrome. The administrator helper is a separate scheduled task, `YtMusicMediaGuard.Hook`, and it keeps the old exe path. Delete that task with the command in [Uninstall](#uninstall), then focus an administrator app again and accept the permission prompt.

### 3. Load the extension manually

The extension stays in this repository. Chrome will not install it for you.

1. Open `chrome://extensions`.
2. Turn on **Developer mode**.
3. Click **Load unpacked**.
4. Select the `extension` folder. From a release zip, that is the `extension` folder next to the exe. From a clone, it is the `extension` folder at the repository root.
5. Confirm the card is named **YouTube Music Media Key Guard**, is enabled, and shows the id `mbehpmabnicdenlcifcenanlcijcecgk`.
6. Quit Chrome completely, including its background icon, then open it again. Chrome reads a new native host only after a restart.

Leave the `key` field in [`extension/manifest.json`](extension/manifest.json) in place. It keeps that extension id stable. Removing it makes Chrome assign a new id, and the tray app will refuse the connection.

Load the extension in every Chrome profile where you open YouTube Music. Repeat steps 1–5 in each profile, then restart Chrome once.

### 4. Optional Tweaks shortcuts

Play, next, and previous already reach YouTube Music through Chrome. Volume and mute are handled by this extension, so they do not need a Tweaks chord. The app does not type these shortcuts itself.

To use other [Tweaks for YouTube](https://chromewebstore.google.com/detail/tweaks-for-youtube/ogkoifddpkoabehfemkolflcjhklmkge) actions from any app, open **Settings…** and choose **Chrome shortcuts** (or go to `chrome://extensions/shortcuts`), find Tweaks, and set the scope to **Global**. Shortcuts set only inside the Tweaks options page work when the YouTube Music tab is focused. Use next video and previous video, not the chapter commands.

| Action | Suggested chord |
| --- | --- |
| Toggle play | `Ctrl+Alt+Shift+P` |
| Next video | `Ctrl+Alt+Shift+N` |
| Previous video | `Ctrl+Alt+Shift+B` |
| Volume up | `Ctrl+Alt+Shift+Up` |
| Volume down | `Ctrl+Alt+Shift+Down` |
| Mute | `Ctrl+Alt+Shift+M` |

Chrome signs extension shortcut preferences, so this app cannot write them. **Settings…** can record the same six chords. They are saved in `%APPDATA%\YtMusicMediaGuard\config.json`. Volume and mute in that window are only a record of the chords. While the guard is armed, those keys go to the extension, and play, next, and previous stay with Chrome unless a game or Remote Desktop is focused.

### 5. Confirm YouTube Music is armed

1. Open `https://music.youtube.com` and start a song.
2. Reload that tab once after loading the extension.
3. Switch to another app, such as Notepad. The YouTube Music tab can stay in the background.
4. The tray icon should turn green. The menu's first line should say **Armed**.
5. `%APPDATA%\YtMusicMediaGuard\guard.log` records errors and the administrator helper (`Elevated hook connected`, or a line that the prompt was declined). A Debug build also records `Extension host connected` and `Armed`.

Press Volume Up or Volume Down. Windows volume should stay put and the YouTube Music player should change. Close the YouTube Music tab. The icon returns to gray and the volume keys control Windows again.

## Checks

1. With no YouTube Music tab, volume keys still control Windows.
2. Open `music.youtube.com`, switch to Notepad, and press Volume Up, Volume Down, and Mute. Windows volume stays where it was. YouTube Music changes.
3. Leave that tab open behind another Chrome tab. Volume still controls YouTube Music.
4. Close the YouTube Music tab. Volume controls Windows again.
5. Quit Chrome. The tray returns to Idle.
6. Focus a game while YouTube Music is playing. Play, next, previous, and the volume keys change YouTube Music. Windows volume stays where it was.
7. Focus a Remote Desktop window on this PC while YouTube Music is playing here. Media keys are not typed into the remote session, and playback still changes on this PC.
8. Connect to this PC from another computer and press the keys while Notepad is focused in the session. YouTube Music on this PC responds.
9. Choose **Pause guard** in the tray. The keys control Windows even though the tab is still open.

## If something does not work

**The tray icon never appears.** Start `YtMusicMediaGuard.exe` again. Check the overflow arrow by the clock. If a second launch does nothing, the app is already running.

**The icon stays gray while YouTube Music is open.** The extension is missing, disabled, or loaded in a different Chrome profile. Chrome was not restarted after the first tray launch. The page is not `music.youtube.com`. Read `guard.log` for errors such as Chrome registration or the pipe failing to open. A Debug build also records whether the extension host connected. If Chrome reports that the native host was not found, Chrome was not restarted, or the exe was moved after registration. The extension id on `chrome://extensions` must be `mbehpmabnicdenlcifcenanlcijcecgk`.

**Volume still changes Windows.** The icon is gray or orange, so capture is off. Some keyboard suites, such as Logitech or Corsair software, change volume in their own driver before Windows reports the key. Turn that suite's media-key handling off. The guard puts the Windows volume back when it can see the change.

**Windows volume stays put, but the player does not change.** Reload the YouTube Music tab so the extension can see the player. The tab has to be a real `music.youtube.com` page, not a discarded tab Chrome has not loaded.

**Play or pause fires twice.** Chrome's hardware media-key handling and another shortcut both toggled playback. Open `chrome://flags/#hardware-media-key-handling`, set **Hardware Media Key Handling** to **Disabled**, and relaunch Chrome.

**Nothing happens while a game is focused.** If Windows shows a permission prompt, choose **Yes**. That installs the helper used for administrator games. Read `guard.log`. A line that the elevated hook was not allowed means the prompt was declined; start the tray app again and accept it. A windowed game that Windows has never marked as a game, and that does not cover the screen, is left on the normal path: volume goes to YouTube Music, and play, next, and previous stay with Chrome.

**Remote Desktop still receives the keys.** The focused program is not `mstsc.exe`, `msrdc.exe`, or `vmconnect.exe`. TeamViewer, AnyDesk, and nested Remote Desktop sessions are not treated as Remote Desktop. If the extension is not connected, the guard drops the key instead of typing it into the remote session.

**You moved the exe or reinstalled Chrome.** Run the exe once from its current folder, then restart Chrome. If administrator games stop responding, delete the scheduled task `YtMusicMediaGuard.Hook` and accept the permission prompt again. That task still points at the previous exe.

## Limits

- Some keyboard suites change the volume inside their own driver. The app puts the Windows volume back when it can see that change.
- Headset buttons that use Bluetooth media control, rather than keyboard keys, still go to Windows.
- A discarded background tab counts as open, but nothing can control a player Chrome has not loaded.
- While the guard is armed, volume and mute no longer reach other apps on this PC. Play, next, and previous still go to Chrome unless a game or a Remote Desktop window is focused.
- Administrator games are handled by a helper that Windows starts after a one-time permission prompt. Headset buttons and keyboard suites that consume the key in their own driver are still invisible to the app.
- Nested Remote Desktop is not handled. TeamViewer, AnyDesk, and other third-party clients are not treated as Remote Desktop.
- The player controls used for volume, mute, and Remote Desktop depend on the YouTube Music page. A redesign of that page can break them.

## Uninstall

1. Open the tray menu, clear **Start with Windows**, and choose **Exit**.
2. Remove **YouTube Music Media Key Guard** from `chrome://extensions`. Tweaks for YouTube can stay.
3. In Registry Editor, delete `HKCU\Software\Google\Chrome\NativeMessagingHosts\com.ytmusic.mediaguard`.
4. Delete the folder `%APPDATA%\YtMusicMediaGuard`.
5. Delete the downloaded folder. Clearing **Start with Windows** before exit removes the sign-in entry.
6. Remove the administrator helper, if it was installed:

```powershell
schtasks /Delete /TN "YtMusicMediaGuard.Hook" /F
```

## Develop

Source for the Windows app is under `src/YtMusicMediaGuard`. `Program.cs` starts the tray, or the native host when Chrome connects to it. `--hook` is the administrator helper, `--install-hook` registers that helper, and `--self-test` runs the checks below. The other folders are:

| Folder | What it contains |
| --- | --- |
| `App` | Config, paths, logging, and sign-in startup |
| `Tray` | Tray icon, menu, settings window, and dark theme |
| `Input` | Media-key hook, shortcut chords, volume restore, and focus detection |
| `Bridge` | Named pipe, native messaging, and commands sent to the extension |
| `Chrome` | Native-host registration and the stable extension id |
| `ElevatedHook` | The administrator helper used while an elevated app is focused |
| `Testing` | The self-test |

`extension` is the unpacked Manifest V3 extension. Leave the `key` in `extension/manifest.json` in place.

```powershell
dotnet run --project src\YtMusicMediaGuard\YtMusicMediaGuard.csproj -- --self-test
```

The self-test checks the extension id, shortcut formatting, the keyboard hook, the volume endpoint, fullscreen and known-game detection, a private message pipe, and an elevated-hook message on that private pipe. It does not press media keys and it does not talk to the running tray app.

`publish.ps1` writes `dist\YtMusicMediaGuard-win-x64.zip`. Tagging `v*` on GitHub runs [`.github/workflows/release.yml`](.github/workflows/release.yml), which builds that zip and attaches it to the release. You can also run that workflow by hand.
