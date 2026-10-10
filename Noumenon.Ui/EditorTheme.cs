namespace Noumenon.Ui;

/// <summary>
/// The panel's look: an olive instrument chassis with dark green-black displays, yellow traces,
/// teal knob caps and green LEDs — an original design in the spirit of the hardware-styled
/// Reaktor ensembles the instrument follows, drawn entirely from these values. Sizes are
/// logical pixels at 1× (the surface scales them) and fonts are specified in pixels so they
/// scale with the drawing transform. Everything here is shared on the UI thread only.
/// </summary>
public static class EditorTheme
{
    public static readonly Color Panel = Color.FromArgb(0x8E, 0x96, 0x6C);
    public static readonly Color PanelLight = Color.FromArgb(0xA4, 0xAB, 0x84);
    public static readonly Color PanelDark = Color.FromArgb(0x70, 0x78, 0x54);
    public static readonly Color PanelInset = Color.FromArgb(0x82, 0x8A, 0x62);
    public static readonly Color Frame = Color.FromArgb(0x42, 0x49, 0x31);
    public static readonly Color FrameLight = Color.FromArgb(0xB8, 0xBE, 0x9A);
    public static readonly Color Display = Color.FromArgb(0x0F, 0x15, 0x0D);
    public static readonly Color DisplayEdge = Color.FromArgb(0x2A, 0x33, 0x24);
    public static readonly Color DisplayGrid = Color.FromArgb(0x1C, 0x27, 0x17);
    public static readonly Color Trace = Color.FromArgb(0xE8, 0xF2, 0x3E);
    public static readonly Color TraceDim = Color.FromArgb(0x6E, 0x74, 0x22);
    public static readonly Color Accent = Color.FromArgb(0x3E, 0xB2, 0x96);
    public static readonly Color AccentDark = Color.FromArgb(0x22, 0x6A, 0x5B);
    public static readonly Color KnobBody = Color.FromArgb(0x2A, 0x30, 0x24);
    public static readonly Color KnobRing = Color.FromArgb(0x5A, 0x62, 0x47);
    public static readonly Color KnobTrack = Color.FromArgb(0x4E, 0x55, 0x3C);
    public static readonly Color Pointer = Color.FromArgb(0xEE, 0xF2, 0xE0);
    public static readonly Color LedOn = Color.FromArgb(0x8C, 0xFF, 0x52);
    public static readonly Color LedOff = Color.FromArgb(0x2B, 0x35, 0x24);
    public static readonly Color Text = Color.FromArgb(0x15, 0x19, 0x0F);
    public static readonly Color TextLight = Color.FromArgb(0xE2, 0xE7, 0xCE);
    public static readonly Color TextDim = Color.FromArgb(0x8A, 0x92, 0x74);
    public static readonly Color TextOnDark = Color.FromArgb(0xC8, 0xD0, 0xB0);
    public static readonly Color Slot = Color.FromArgb(0x1E, 0x24, 0x19);
    public static readonly Color Cap = Color.FromArgb(0xC4, 0xC9, 0xB2);
    public static readonly Color CapEdge = Color.FromArgb(0x6B, 0x71, 0x5A);
    public static readonly Color CapLine = Color.FromArgb(0x3A, 0x40, 0x30);
    public static readonly Color Button = Color.FromArgb(0x36, 0x3D, 0x2E);
    public static readonly Color ButtonOn = Color.FromArgb(0x53, 0x64, 0x39);
    public static readonly Color ButtonDown = Color.FromArgb(0x24, 0x2A, 0x1E);
    public static readonly Color ButtonEdge = Color.FromArgb(0x1A, 0x1F, 0x14);
    public static readonly Color Armed = Color.FromArgb(0xF0, 0xC0, 0x24);
    public static readonly Color Recording = Color.FromArgb(0xE6, 0x44, 0x2C);
    public static readonly Color Playing = Color.FromArgb(0x8C, 0xFF, 0x52);
    public static readonly Color Loop = Color.FromArgb(0x5E, 0x66, 0x1E);

    public static readonly Font LabelFont = new("Segoe UI", 9f, FontStyle.Bold, GraphicsUnit.Pixel);
    public static readonly Font SmallFont = new("Segoe UI", 8f, FontStyle.Bold, GraphicsUnit.Pixel);
    public static readonly Font ValueFont = new("Consolas", 10f, FontStyle.Regular, GraphicsUnit.Pixel);
    public static readonly Font SmallValueFont = new("Consolas", 9f, FontStyle.Regular, GraphicsUnit.Pixel);
    public static readonly Font TitleFont = new("Segoe UI", 10f, FontStyle.Bold, GraphicsUnit.Pixel);
    public static readonly Font HugeFont = new("Segoe UI", 28f, FontStyle.Bold, GraphicsUnit.Pixel);
    public static readonly Font BadgeFont = new("Segoe UI", 13f, FontStyle.Bold, GraphicsUnit.Pixel);

    public static readonly StringFormat Center = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.None };
    public static readonly StringFormat Left = new() { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.None };
    public static readonly StringFormat Right = new() { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.None };
    public static readonly StringFormat TopLeft = new() { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near, FormatFlags = StringFormatFlags.NoWrap };
}
