using System.Runtime.InteropServices;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace JukkasDell;

internal static class DarkTheme
{
    // Window-local DWM attributes; native caption buttons, resizing and Snap stay intact.
    // https://learn.microsoft.com/windows/apps/desktop/modernize/apply-windows-themes
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    public static void ApplyTitleBar(IntPtr window)
    {
        int enabled = 1;
        _ = DwmSetWindowAttribute(window, 20, ref enabled, sizeof(int));
        int caption = 0x00161410; // COLORREF for #101416 (BGR).
        _ = DwmSetWindowAttribute(window, 35, ref caption, sizeof(int));
    }

    public static void ApplyMenu(Forms.ToolStrip menu)
    {
        menu.Renderer = new MenuRenderer();
        menu.BackColor = Drawing.Color.FromArgb(24, 30, 34);
        menu.ForeColor = Drawing.Color.FromArgb(237, 242, 243);
    }

    private sealed class MenuRenderer : Forms.ToolStripProfessionalRenderer
    {
        public MenuRenderer() : base(new MenuColors()) { RoundedEdges = false; }
        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Drawing.Color.FromArgb(237, 242, 243) : Drawing.Color.FromArgb(140, 155, 164);
            base.OnRenderItemText(e);
        }
        protected override void OnRenderItemCheck(Forms.ToolStripItemImageRenderEventArgs e)
        {
            var r = e.ImageRectangle;
            if (r.Width <= 0 || r.Height <= 0) return;
            using var pen = new Drawing.Pen(Drawing.Color.FromArgb(131, 227, 193), Math.Max(1.5f, r.Width / 9f));
            var old = e.Graphics.SmoothingMode;
            try
            {
                e.Graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
                e.Graphics.DrawLines(pen, new Drawing.PointF[] {
                    new(r.X + r.Width * .20f, r.Y + r.Height * .50f),
                    new(r.X + r.Width * .43f, r.Y + r.Height * .72f),
                    new(r.X + r.Width * .82f, r.Y + r.Height * .27f) });
            }
            finally { e.Graphics.SmoothingMode = old; }
        }
        protected override void OnRenderArrow(Forms.ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Drawing.Color.FromArgb(163, 177, 184);
            base.OnRenderArrow(e);
        }
    }

    private sealed class MenuColors : Forms.ProfessionalColorTable
    {
        private static Drawing.Color Surface => Drawing.Color.FromArgb(24, 30, 34);
        private static Drawing.Color Hover => Drawing.Color.FromArgb(40, 55, 60);
        private static Drawing.Color Stroke => Drawing.Color.FromArgb(48, 60, 67);
        public MenuColors() { UseSystemColors = false; }
        public override Drawing.Color ToolStripDropDownBackground => Surface;
        public override Drawing.Color ImageMarginGradientBegin => Surface;
        public override Drawing.Color ImageMarginGradientMiddle => Surface;
        public override Drawing.Color ImageMarginGradientEnd => Surface;
        public override Drawing.Color MenuItemSelected => Hover;
        public override Drawing.Color MenuItemSelectedGradientBegin => Hover;
        public override Drawing.Color MenuItemSelectedGradientEnd => Hover;
        public override Drawing.Color MenuItemPressedGradientBegin => Hover;
        public override Drawing.Color MenuItemPressedGradientMiddle => Hover;
        public override Drawing.Color MenuItemPressedGradientEnd => Hover;
        public override Drawing.Color MenuItemBorder => Stroke;
        public override Drawing.Color MenuBorder => Stroke;
        public override Drawing.Color SeparatorDark => Stroke;
        public override Drawing.Color SeparatorLight => Surface;
        public override Drawing.Color CheckBackground => Hover;
        public override Drawing.Color CheckSelectedBackground => Hover;
        public override Drawing.Color CheckPressedBackground => Hover;
    }
}
