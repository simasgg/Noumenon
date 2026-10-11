namespace Noumenon.Ui;

/// <summary>
/// The panel's look: a dark neutral chassis of flat tiles with hairline edges, one teal accent
/// for everything live (traces, value arcs, bar fills, lit buttons), amber for an armed lane and
/// red for a recording one, light grey type. Sizes are logical pixels at 1× (the surface scales
/// them) and fonts are specified in pixels so they scale with the drawing transform. Everything
/// here is shared on the UI thread only.
/// </summary>
public static class EditorTheme
{
    // Chassis
    public static readonly Color Panel = Color.FromArgb(0x12, 0x14, 0x17);
    public static readonly Color PanelLight = Color.FromArgb(0x1A, 0x1D, 0x21);
    public static readonly Color PanelDark = Color.FromArgb(0x0D, 0x0F, 0x12);
    public static readonly Color PanelInset = Color.FromArgb(0x15, 0x18, 0x1C);
    public static readonly Color Frame = Color.FromArgb(0x27, 0x2B, 0x31);
    public static readonly Color FrameLight = Color.FromArgb(0x3E, 0x45, 0x4E);

    // Displays
    public static readonly Color Display = Color.FromArgb(0x0C, 0x0E, 0x10);
    public static readonly Color DisplayEdge = Color.FromArgb(0x22, 0x26, 0x2B);
    public static readonly Color DisplayGrid = Color.FromArgb(0x1A, 0x1F, 0x24);
    public static readonly Color Trace = Color.FromArgb(0x4F, 0xD1, 0xB3);
    public static readonly Color TraceDim = Color.FromArgb(0x2A, 0x6B, 0x5E);
    public static readonly Color Readout = Color.FromArgb(0xCF, 0xE3, 0xDE);

    // The accent and the lane states
    public static readonly Color Accent = Color.FromArgb(0x4F, 0xD1, 0xB3);
    public static readonly Color AccentDark = Color.FromArgb(0x2A, 0x6B, 0x5E);
    public static readonly Color Armed = Color.FromArgb(0xE8, 0xB0, 0x4B);
    public static readonly Color Recording = Color.FromArgb(0xE2, 0x5A, 0x45);
    public static readonly Color Playing = Color.FromArgb(0x4F, 0xD1, 0xB3);
    public static readonly Color Loop = Color.FromArgb(0x2A, 0x6B, 0x5E);

    // Controls
    public static readonly Color KnobBody = Color.FromArgb(0x1F, 0x24, 0x29);
    public static readonly Color KnobRing = Color.FromArgb(0x34, 0x3A, 0x42);
    public static readonly Color KnobTrack = Color.FromArgb(0x2A, 0x30, 0x36);
    public static readonly Color Pointer = Color.FromArgb(0xE6, 0xEA, 0xEE);
    public static readonly Color LedOn = Color.FromArgb(0x4F, 0xD1, 0xB3);
    public static readonly Color LedOff = Color.FromArgb(0x2A, 0x2F, 0x36);
    public static readonly Color Slot = Color.FromArgb(0x0E, 0x10, 0x13);
    public static readonly Color Cap = Color.FromArgb(0xC9, 0xCE, 0xD4);
    public static readonly Color CapEdge = Color.FromArgb(0x5A, 0x60, 0x68);
    public static readonly Color CapLine = Color.FromArgb(0x30, 0x35, 0x3C);
    public static readonly Color Button = Color.FromArgb(0x23, 0x27, 0x2D);
    public static readonly Color ButtonOn = Color.FromArgb(0x21, 0x4A, 0x41);
    public static readonly Color ButtonDown = Color.FromArgb(0x16, 0x19, 0x1D);
    public static readonly Color ButtonEdge = Color.FromArgb(0x30, 0x35, 0x3C);

    // Type
    public static readonly Color Text = Color.FromArgb(0xD7, 0xDB, 0xE0);
    public static readonly Color TextLight = Color.FromArgb(0xF2, 0xF4, 0xF6);
    public static readonly Color TextDim = Color.FromArgb(0x7C, 0x83, 0x8C);
    public static readonly Color TextOnDark = Color.FromArgb(0xC4, 0xCA, 0xD1);

    public static readonly Font LabelFont = new("Segoe UI Semibold", 9f, FontStyle.Regular, GraphicsUnit.Pixel);
    public static readonly Font SmallFont = new("Segoe UI Semibold", 8f, FontStyle.Regular, GraphicsUnit.Pixel);
    public static readonly Font ValueFont = new("Consolas", 10f, FontStyle.Regular, GraphicsUnit.Pixel);
    public static readonly Font SmallValueFont = new("Consolas", 9f, FontStyle.Regular, GraphicsUnit.Pixel);
    public static readonly Font TitleFont = new("Segoe UI Semibold", 10f, FontStyle.Regular, GraphicsUnit.Pixel);
    public static readonly Font HugeFont = new("Segoe UI Semibold", 22f, FontStyle.Regular, GraphicsUnit.Pixel);
    public static readonly Font BadgeFont = new("Bahnschrift", 16f, FontStyle.Regular, GraphicsUnit.Pixel);

    public static readonly StringFormat Center = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.None };
    public static readonly StringFormat Left = new() { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.None };
    public static readonly StringFormat Right = new() { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.None };
    public static readonly StringFormat TopLeft = new() { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near, FormatFlags = StringFormatFlags.NoWrap };
}
