using System.Runtime.InteropServices;
using System.Windows.Media;
using Drawing = System.Drawing;

namespace JukkasDell;

internal static class AppIcon
{
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    public static Drawing.Icon Tray()
    {
        using var bitmap = new Drawing.Bitmap(32, 32);
        using var g = Drawing.Graphics.FromImage(bitmap);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var fill = new Drawing.SolidBrush(Drawing.Color.FromArgb(36, 95, 86));
        g.FillEllipse(fill, 1, 1, 30, 30);
        using var font = new Drawing.Font("Segoe UI", 20, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel);
        using var white = new Drawing.SolidBrush(Drawing.Color.White);
        g.DrawString("J", font, white, 9, 2);
        var handle = bitmap.GetHicon();
        try { using var borrowed = Drawing.Icon.FromHandle(handle); return (Drawing.Icon)borrowed.Clone(); }
        finally { DestroyIcon(handle); }
    }
    public static ImageSource Window()
    {
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(36, 95, 86)), null, new System.Windows.Point(16, 16), 15, 15);
            var text = new FormattedText("J", System.Globalization.CultureInfo.InvariantCulture,
                System.Windows.FlowDirection.LeftToRight, new Typeface("Segoe UI"), 23, Brushes.White, 1);
            dc.DrawText(text, new System.Windows.Point(10, 0));
        }
        group.Freeze();
        return new DrawingImage(group);
    }
}
