using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace YtMusicMediaGuard;

internal static class DarkTheme
{
    public static readonly Color Window = Color.FromArgb(28, 28, 30);
    public static readonly Color Surface = Color.FromArgb(44, 44, 48);
    public static readonly Color Hover = Color.FromArgb(58, 58, 64);
    public static readonly Color Border = Color.FromArgb(72, 72, 78);
    public static readonly Color Text = Color.FromArgb(244, 244, 244);
    public static readonly Color Muted = Color.FromArgb(168, 168, 174);
    public static readonly Color Accent = Color.FromArgb(36, 150, 74);
    public static readonly Color AccentHover = Color.FromArgb(46, 170, 88);
    public static readonly Font Ui = new("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font Heading = new("Segoe UI", 10f, FontStyle.Bold, GraphicsUnit.Point);

    public static void Apply(ToolStrip menu)
    {
        menu.Renderer = new DarkMenuRenderer();
        menu.BackColor = Window;
        menu.ForeColor = Text;
        menu.Font = Ui;
        if (menu is ToolStripDropDownMenu dropDown)
        {
            dropDown.ShowImageMargin = true;
            dropDown.ShowCheckMargin = false;
        }
    }

    public static void UseDarkFrame(Form form)
    {
        var dark = 1;
        _ = DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int));
        _ = DwmSetWindowAttribute(form.Handle, 19, ref dark, sizeof(int));
        var caption = ColorRef(Window);
        var text = ColorRef(Text);
        var border = ColorRef(Border);
        _ = DwmSetWindowAttribute(form.Handle, 35, ref caption, sizeof(int));
        _ = DwmSetWindowAttribute(form.Handle, 36, ref text, sizeof(int));
        _ = DwmSetWindowAttribute(form.Handle, 34, ref border, sizeof(int));
    }

    public static Button Button(string text, bool primary)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            MinimumSize = new Size(88, 34),
            Padding = new Padding(12, 4, 12, 4),
            FlatStyle = FlatStyle.Flat,
            Font = Ui,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
            ForeColor = Text,
            BackColor = primary ? Accent : Surface,
        };
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.MouseOverBackColor = primary ? AccentHover : Hover;
        button.FlatAppearance.MouseDownBackColor = primary ? Accent : Hover;
        return button;
    }

    public static TextBox Box(string text)
    {
        return new TextBox
        {
            Text = text,
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Surface,
            ForeColor = Text,
            Font = Ui,
            Dock = DockStyle.Fill,
            Margin = new Padding(12, 6, 8, 6),
            TabStop = false,
        };
    }

    public static Label Caption(string text, bool muted)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Font = Ui,
            ForeColor = muted ? Muted : Text,
            BackColor = Window,
            Margin = new Padding(0, 8, 8, 8),
        };
    }

    private static int ColorRef(Color color) => color.R | (color.G << 8) | (color.B << 16);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColors())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Text : Muted;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var bounds = e.ImageRectangle;
            var x = bounds.Left + 4;
            var y = bounds.Top + (bounds.Height / 2);
            using var pen = new Pen(Text, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.DrawLines(pen, new[]
            {
                new Point(x, y),
                new Point(x + 4, y + 4),
                new Point(x + 10, y - 4),
            });
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var bounds = e.Item.ContentRectangle;
            var y = bounds.Top + (bounds.Height / 2);
            var left = e.Item.Owner is ToolStripDropDownMenu { ShowImageMargin: true } ? bounds.Left + 28 : bounds.Left + 8;
            using var pen = new Pen(Border);
            e.Graphics.DrawLine(pen, left, y, bounds.Right - 8, y);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (e.ToolStrip is not ToolStripDropDown) return;
            var bounds = e.AffectedBounds;
            bounds.Width -= 1;
            bounds.Height -= 1;
            using var pen = new Pen(Border);
            e.Graphics.DrawRectangle(pen, bounds);
        }
    }

    private sealed class DarkColors : ProfessionalColorTable
    {
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color MenuItemPressedGradientBegin => Hover;
        public override Color MenuItemPressedGradientEnd => Hover;
        public override Color MenuItemPressedGradientMiddle => Hover;
        public override Color MenuItemBorder => Hover;
        public override Color MenuBorder => Border;
        public override Color ToolStripDropDownBackground => Window;
        public override Color ImageMarginGradientBegin => Window;
        public override Color ImageMarginGradientMiddle => Window;
        public override Color ImageMarginGradientEnd => Window;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Border;
        public override Color CheckBackground => Window;
        public override Color CheckSelectedBackground => Hover;
        public override Color CheckPressedBackground => Hover;
        public override Color ButtonSelectedHighlight => Hover;
        public override Color ButtonSelectedGradientBegin => Hover;
        public override Color ButtonSelectedGradientMiddle => Hover;
        public override Color ButtonSelectedGradientEnd => Hover;
        public override Color ButtonPressedGradientBegin => Hover;
        public override Color ButtonPressedGradientMiddle => Hover;
        public override Color ButtonPressedGradientEnd => Hover;
        public override Color ToolStripGradientBegin => Window;
        public override Color ToolStripGradientMiddle => Window;
        public override Color ToolStripGradientEnd => Window;
    }
}

internal sealed class MenuHeading : ToolStripLabel
{
}
