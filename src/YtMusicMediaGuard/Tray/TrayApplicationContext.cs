namespace YtMusicMediaGuard;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly Control _invoker;
    private readonly GuardController _controller = new();
    private readonly KeyboardHook _hook;
    private readonly MenuHeading _status;
    private readonly ToolStripMenuItem _pause;
    private readonly ToolStripMenuItem _startup;
    private readonly AppConfig _config;
    private readonly System.Threading.Timer _maintenance;
    private volatile bool _paused;

    public TrayApplicationContext()
    {
        _config = AppConfig.Load();
        try { StartupRegistration.Apply(_config.StartWithWindows); }
        catch (Exception ex) { Logger.Error("Startup registration failed: " + ex.Message); }
        try { ChromeRegistration.Register(); }
        catch (Exception ex) { Logger.Error("Chrome registration failed: " + ex.Message); }

        _invoker = new Control();
        _ = _invoker.Handle;
        _hook = new KeyboardHook(_invoker)
        {
            IsActive = () => !_paused && _controller.IsArmed && !FocusDetector.ForegroundElevated,
            OnAction = HandleAction,
        };

        _controller.Changed += () =>
        {
            PushHookState();
            if (_invoker.IsHandleCreated) _invoker.BeginInvoke(UpdateUi);
        };
        _controller.HookKeyPressed += action =>
        {
            if (_paused || !_controller.IsArmed) return;
            _controller.SendCommand(action);
        };
        _controller.Start();
        VolumeGuard.Start();
        FocusDetector.Start();
        PushHookState();
        _maintenance = new System.Threading.Timer(_ => Maintain(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        try { _hook.Start(); }
        catch (Exception ex)
        {
            Logger.Error(ex.ToString());
            MessageBox.Show(
                "The keyboard hook could not be installed, so media keys will still control Windows.\n\n" + ex.Message,
                "YouTube Music media key guard",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        _status = new MenuHeading
        {
            Text = "Starting…",
            Font = DarkTheme.Heading,
            ForeColor = DarkTheme.Text,
            Margin = new Padding(8, 6, 12, 4),
        };
        _pause = new ToolStripMenuItem("Pause guard", null, (_, _) => TogglePause())
        {
            Padding = new Padding(6, 3, 12, 3),
        };
        _startup = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleStartup())
        {
            Checked = _config.StartWithWindows,
            Padding = new Padding(6, 3, 12, 3),
        };
        var settings = new ToolStripMenuItem("Settings…", null, (_, _) => EditShortcuts())
        {
            Padding = new Padding(6, 3, 12, 3),
        };
        var exit = new ToolStripMenuItem("Exit", null, (_, _) => ExitThread())
        {
            Padding = new Padding(6, 3, 12, 3),
        };
        var menu = new ContextMenuStrip();
        DarkTheme.Apply(menu);
        menu.Items.Add(_status);
        _status.Font = DarkTheme.Heading;
        _status.ForeColor = DarkTheme.Text;
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_pause);
        menu.Items.Add(_startup);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(settings);
        menu.Items.Add(exit);

        _icon = new NotifyIcon
        {
            Icon = TrayIcons.Idle,
            Visible = true,
            Text = "YouTube Music keys",
            ContextMenuStrip = menu,
        };
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            typeof(NotifyIcon).GetMethod("ShowContextMenu", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.Invoke(_icon, null);
        };
        UpdateUi();
        Introduce();
        Logger.Info("Tray app is running.");
    }

    protected override void ExitThreadCore()
    {
        _maintenance.Dispose();
        _controller.StopHook();
        Thread.Sleep(200);
        _hook.Dispose();
        _controller.Dispose();
        FocusDetector.Stop();
        VolumeGuard.Stop();
        _icon.Visible = false;
        _icon.Dispose();
        _invoker.Dispose();
        TrayIcons.Destroy();
        base.ExitThreadCore();
    }

    private void HandleAction(MediaAction action)
    {
        if (_paused || !_controller.IsArmed) return;

        if (action is MediaAction.VolumeUp or MediaAction.VolumeDown or MediaAction.Mute
            || FocusDetector.OwnsTransport)
        {
            _controller.SendCommand(action);
            return;
        }

        try
        {
            ChordSender.Send(Chord.Parse(_config.TextFor(action)));
            if (action is not (MediaAction.VolumeUp or MediaAction.VolumeDown))
                Logger.Info($"Sent the {action} shortcut to Tweaks.");
        }
        catch (Exception ex)
        {
            Logger.Error($"Could not send the {action} shortcut. " + ex.Message);
        }
    }

    private void TogglePause()
    {
        _paused = !_paused;
        _pause.Text = _paused ? "Resume guard" : "Pause guard";
        PushHookState();
        UpdateUi();
    }

    private void Maintain()
    {
        try
        {
            PushHookState();
            HookElevation.Ensure(_controller.HookConnected, FocusDetector.ForegroundElevated);
        }
        catch (Exception ex)
        {
            Logger.Error("Elevated hook check failed: " + ex.Message);
        }
    }

    private void PushHookState() => _controller.PublishHookState(!_paused && _controller.IsArmed);

    private void ToggleStartup()
    {
        _config.StartWithWindows = !_config.StartWithWindows;
        _startup.Checked = _config.StartWithWindows;
        try
        {
            _config.Save();
            StartupRegistration.Apply(_config.StartWithWindows);
        }
        catch (Exception ex)
        {
            Logger.Error("Could not change startup registration. " + ex.Message);
        }
    }

    private void EditShortcuts()
    {
        using var form = new SettingsForm(_config);
        if (form.ShowDialog() != DialogResult.OK) return;
        try
        {
            _config.Save();
            StartupRegistration.Apply(_config.StartWithWindows);
            _startup.Checked = _config.StartWithWindows;
        }
        catch (Exception ex)
        {
            Logger.Error("Could not save shortcuts. " + ex.Message);
            MessageBox.Show(ex.Message, "YouTube Music media key guard", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void UpdateUi()
    {
        if (_paused)
        {
            _status.Text = "Paused";
            _icon.Icon = TrayIcons.Paused;
            _icon.Text = "Paused — media keys control Windows";
            return;
        }

        if (_controller.IsArmed)
        {
            var tabs = _controller.TabCount;
            var noun = tabs == 1 ? "tab" : "tabs";
            _status.Text = $"Armed — {tabs} YouTube Music {noun}";
            _icon.Icon = TrayIcons.Armed;
            _icon.Text = TrimTooltip(_status.Text);
            return;
        }

        _status.Text = "Idle — no YouTube Music tab";
        _icon.Icon = TrayIcons.Idle;
        _icon.Text = "Idle — media keys control Windows";
    }

    private void Introduce()
    {
        try
        {
            if (File.Exists(AppPaths.IntroducedMarker)) return;
            Directory.CreateDirectory(AppPaths.Directory);
            _icon.ShowBalloonTip(
                8000,
                "YouTube Music media key guard",
                "Running in the tray. Load the companion extension, then restart Chrome. Left-click the icon for the menu.",
                ToolTipIcon.Info);
            File.WriteAllText(AppPaths.IntroducedMarker, "1");
        }
        catch (Exception ex)
        {
            Logger.Error(ex.Message);
        }
    }

    private static string TrimTooltip(string text) => text.Length <= 63 ? text : text[..63];
}
