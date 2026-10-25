using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace YtMusicMediaGuard;

internal static class TrayIcons
{
    private static readonly List<IntPtr> Handles = new();

    public static Icon Armed { get; } = Create(Color.FromArgb(36, 150, 74), Glyph.Play);
    public static Icon Idle { get; } = Create(Color.FromArgb(110, 116, 124), Glyph.Idle);
    public static Icon Paused { get; } = Create(Color.FromArgb(196, 132, 42), Glyph.Pause);

    public static void Destroy()
    {
        foreach (var handle in Handles)
            DestroyIcon(handle);
        Handles.Clear();
    }

    private static Icon Create(Color color, Glyph glyph)
    {
        using var bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            using var brush = new SolidBrush(color);
            graphics.FillEllipse(brush, 1, 1, 30, 30);
            using var white = new SolidBrush(Color.White);
            switch (glyph)
            {
                case Glyph.Play:
                    graphics.FillPolygon(white, new[] { new Point(12, 9), new Point(12, 23), new Point(23, 16) });
                    break;
                case Glyph.Pause:
                    graphics.FillRectangle(white, 11, 9, 4, 14);
                    graphics.FillRectangle(white, 17, 9, 4, 14);
                    break;
                default:
                    graphics.FillRectangle(white, 9, 14, 14, 4);
                    break;
            }
        }

        var handle = bitmap.GetHicon();
        Handles.Add(handle);
        return Icon.FromHandle(handle);
    }

    private enum Glyph { Play, Pause, Idle }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
