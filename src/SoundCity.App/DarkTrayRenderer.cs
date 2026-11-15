using System.Drawing;
using System.Windows.Forms;

namespace SoundCity.App;

internal sealed class DarkTrayRenderer : ToolStripProfessionalRenderer
{
    internal static readonly Color Surface = Color.FromArgb(20, 25, 34);
    internal static readonly Color Ink = Color.FromArgb(243, 241, 236);
    private static readonly Color Accent = Color.FromArgb(255, 145, 71);

    internal DarkTrayRenderer() : base(new Palette()) => RoundedEdges = false;

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Ink : Color.FromArgb(139, 150, 168);
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Accent;
        base.OnRenderArrow(e);
    }

    private sealed class Palette : ProfessionalColorTable
    {
        internal Palette() => UseSystemColors = false;
        public override Color ToolStripDropDownBackground => Surface;
        public override Color MenuItemSelected => Color.FromArgb(48, 34, 27);
        public override Color MenuItemBorder => Accent;
        public override Color MenuBorder => Color.FromArgb(43, 52, 67);
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color SeparatorDark => MenuBorder;
        public override Color SeparatorLight => Surface;
    }
}
