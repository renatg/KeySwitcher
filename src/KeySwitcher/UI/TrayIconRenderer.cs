using System.Drawing.Drawing2D;
using System.Drawing.Text;
using KeySwitcher.Layout;
using static KeySwitcher.Native.NativeMethods;

namespace KeySwitcher.UI;

/// <summary>Draws the tray icons ("EN", "RU", greyed when paused) once and caches them.</summary>
internal sealed class TrayIconRenderer : IDisposable
{
    private readonly Dictionary<(string Text, bool Disabled), Icon> _cache = new();

    private static readonly Color EnColor = Color.FromArgb(37, 99, 235);
    private static readonly Color RuColor = Color.FromArgb(220, 38, 38);
    private static readonly Color AppColor = Color.FromArgb(79, 70, 229);
    private static readonly Color DisabledColor = Color.FromArgb(120, 120, 128);

    public Icon Get(LayoutKind layout, bool disabled, bool showLayout)
    {
        string text = !showLayout ? "KS" : layout switch
        {
            LayoutKind.En => "EN",
            LayoutKind.Ru => "RU",
            _ => "KS",
        };
        var key = (text, disabled);
        if (_cache.TryGetValue(key, out var icon)) return icon;

        var color = disabled ? DisabledColor : text switch
        {
            "EN" => EnColor,
            "RU" => RuColor,
            _ => AppColor,
        };
        icon = Draw(text, color);
        _cache[key] = icon;
        return icon;
    }

    private static Icon Draw(string text, Color background)
    {
        int size = Math.Max(16, SystemInformation.SmallIconSize.Width);
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            float r = size * 0.22f;
            using (var path = RoundedRect(new RectangleF(0, 0, size - 1, size - 1), r))
            using (var brush = new SolidBrush(background))
                g.FillPath(brush, path);

            using var font = new Font("Segoe UI", size * 0.47f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(text, font, Brushes.White, new RectangleF(0, size * 0.03f, size, size), format);
        }

        IntPtr hIcon = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(hIcon);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    private static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        float d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public void Dispose()
    {
        foreach (var icon in _cache.Values) icon.Dispose();
        _cache.Clear();
    }
}
