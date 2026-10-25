namespace YtMusicMediaGuard;

internal sealed class SettingsForm : Form
{
    private readonly TextBox[] _boxes;
    private readonly Button[] _recordButtons;
    private readonly Chord[] _chords;
    private readonly CheckBox _startup;
    private int _recording = -1;

    public SettingsForm(AppConfig config)
    {
        Text = "YouTube Music media key guard";
        Font = DarkTheme.Ui;
        AutoScaleMode = AutoScaleMode.Font;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ShowIcon = true;
        Icon = TrayIcons.Armed;
        BackColor = DarkTheme.Window;
        ForeColor = DarkTheme.Text;
        KeyPreview = true;
        KeyDown += OnKeyDown;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(20, 16, 20, 16);

        var labels = new[]
        {
            "Play / Pause",
            "Next",
            "Previous",
            "Volume up",
            "Volume down",
            "Mute",
        };
        _chords = new[]
        {
            Chord.Parse(config.PlayPause),
            Chord.Parse(config.Next),
            Chord.Parse(config.Previous),
            Chord.Parse(config.VolumeUp),
            Chord.Parse(config.VolumeDown),
            Chord.Parse(config.Mute),
        };
        _boxes = new TextBox[_chords.Length];
        _recordButtons = new Button[_chords.Length];

        var root = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            BackColor = DarkTheme.Window,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var intro = DarkTheme.Caption(
            "Volume and mute already control YouTube Music. These shortcuts are optional and only matter if you also use Tweaks for YouTube.",
            muted: true);
        intro.MaximumSize = new Size(520, 0);
        intro.Margin = new Padding(0, 0, 0, 8);
        root.Controls.Add(intro);

        var table = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 3,
            RowCount = labels.Length,
            BackColor = DarkTheme.Window,
            Margin = new Padding(0, 4, 0, 4),
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        for (var i = 0; i < labels.Length; i++)
        {
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(DarkTheme.Caption(labels[i], muted: false), 0, i);
            var box = DarkTheme.Box(_chords[i].Format());
            _boxes[i] = box;
            table.Controls.Add(box, 1, i);
            var index = i;
            var record = DarkTheme.Button("Record", primary: false);
            record.Margin = new Padding(0, 4, 0, 4);
            record.Click += (_, _) => ToggleRecord(index);
            _recordButtons[i] = record;
            table.Controls.Add(record, 2, i);
        }

        root.Controls.Add(table);

        _startup = new CheckBox
        {
            Text = "Start with Windows",
            Checked = config.StartWithWindows,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            Font = DarkTheme.Ui,
            ForeColor = DarkTheme.Text,
            BackColor = DarkTheme.Window,
            Margin = new Padding(0, 12, 0, 4),
        };
        root.Controls.Add(_startup);

        var buttons = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            BackColor = DarkTheme.Window,
            Margin = new Padding(0, 16, 0, 0),
        };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        var open = DarkTheme.Button("Chrome shortcuts", primary: false);
        open.Margin = new Padding(0);
        open.Click += (_, _) => ChromeRegistration.OpenShortcutPage();
        buttons.Controls.Add(open, 0, 0);

        var right = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Dock = DockStyle.Fill,
            BackColor = DarkTheme.Window,
            Margin = new Padding(0),
        };
        var cancel = DarkTheme.Button("Cancel", primary: false);
        cancel.DialogResult = DialogResult.Cancel;
        cancel.Margin = new Padding(8, 0, 0, 0);
        var save = DarkTheme.Button("Save", primary: true);
        save.Margin = new Padding(8, 0, 0, 0);
        save.Click += (_, _) => SaveAndClose(config);
        right.Controls.Add(cancel);
        right.Controls.Add(save);
        buttons.Controls.Add(right, 1, 0);
        root.Controls.Add(buttons);

        CancelButton = cancel;
        AcceptButton = save;
        Controls.Add(root);
        Shown += (_, _) => ActiveControl = save;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        DarkTheme.UseDarkFrame(this);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_recording >= 0 && keyData == Keys.Escape)
        {
            CancelRecord();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void ToggleRecord(int index)
    {
        if (_recording == index)
        {
            EndRecord();
            return;
        }

        BeginRecord(index);
    }

    private void BeginRecord(int index)
    {
        if (_recording >= 0) EndRecord();
        _recording = index;
        _boxes[index].Text = "Press a shortcut…";
        _boxes[index].BackColor = Color.FromArgb(28, 56, 40);
        _recordButtons[index].Text = "Cancel";
    }

    private void EndRecord()
    {
        if (_recording < 0) return;
        _boxes[_recording].Text = _chords[_recording].Format();
        _boxes[_recording].BackColor = DarkTheme.Surface;
        _recordButtons[_recording].Text = "Record";
        _recording = -1;
    }

    private void CancelRecord() => EndRecord();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_recording < 0) return;
        e.SuppressKeyPress = true;
        e.Handled = true;
        if (e.KeyCode is Keys.ControlKey or Keys.Menu or Keys.ShiftKey or Keys.LWin or Keys.RWin) return;
        if (e.KeyCode == Keys.Escape)
        {
            CancelRecord();
            return;
        }

        var name = Chord.NameFromKey(e.KeyCode);
        if (name is null) return;
        _chords[_recording] = new Chord { Ctrl = e.Control, Alt = e.Alt, Shift = e.Shift, Key = name };
        EndRecord();
    }

    private void SaveAndClose(AppConfig config)
    {
        if (_recording >= 0) EndRecord();
        foreach (var chord in _chords)
        {
            try { _ = chord.VirtualKey; }
            catch (FormatException ex)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        config.PlayPause = _chords[0].Format();
        config.Next = _chords[1].Format();
        config.Previous = _chords[2].Format();
        config.VolumeUp = _chords[3].Format();
        config.VolumeDown = _chords[4].Format();
        config.Mute = _chords[5].Format();
        config.StartWithWindows = _startup.Checked;
        DialogResult = DialogResult.OK;
        Close();
    }
}
