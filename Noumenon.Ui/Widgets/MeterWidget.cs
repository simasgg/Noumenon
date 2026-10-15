namespace Noumenon.Ui.Widgets;

public enum MeterScale
{
    /// <summary>A linear peak shown on a dB scale from −60 to 0.</summary>
    Peak,

    /// <summary>A gain reduction in dB, 0 to 12, growing from the left or the top.</summary>
    Reduction,

    /// <summary>A plain fraction, 0 to 1, filling from the left or the bottom: progress, a level.</summary>
    Fraction,
}

/// <summary>
/// A bar meter driven by a function: instant attack, a timed release in the UI poll so it
/// falls smoothly whatever the engine's own hold does, a caption at the start of the bar. Peak
/// meters fill from the bottom or left on a dB scale; reduction meters grow from zero the other
/// way, as a limiter's gain-reduction display does; fraction meters fill linearly and follow at
/// once, as a progress bar should.
/// </summary>
public sealed class MeterWidget : Widget
{
    private const float ReleasePerFrame = 0.025f;
    private const float PeakFloorDb = -60f;
    private const float ReductionMaxDb = 12f;
    private const float LabelWidth = 16f;

    private readonly Func<float> value;
    private readonly MeterScale scale;
    private readonly string? label;
    private readonly bool vertical;
    private float shown;

    public MeterWidget(Func<float> value, float x, float y, float width, float height, MeterScale scale, string? label = null, bool vertical = false)
    {
        this.value = value;
        this.scale = scale;
        this.label = label;
        this.vertical = vertical;
        Bounds = new RectangleF(x, y, width, height);
    }

    public override string? Tooltip => scale switch
    {
        MeterScale.Peak => $"{label} peak {20f * MathF.Log10(MathF.Max(value(), 1e-6f)):0.0} dB",
        MeterScale.Reduction => $"{label} gain reduction {value():0.0} dB",
        _ => $"{label} {value() * 100f:0} %",
    };

    private RectangleF Bar => label is null ? Bounds : vertical ? new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height - 10f) : new RectangleF(Bounds.X + LabelWidth, Bounds.Y, Bounds.Width - LabelWidth, Bounds.Height);

    public override void Draw(Graphics g)
    {
        var bar = Bar;
        EditorDraw.Frame(g, bar, 2f, EditorTheme.Slot, EditorTheme.ButtonEdge, 1f);
        var inner = RectangleF.Inflate(bar, -1.5f, -1.5f);
        var fraction = Math.Clamp(shown, 0f, 1f);
        if (fraction > 0f)
        {
            using var brush = new SolidBrush(scale switch
            {
                MeterScale.Peak => fraction > 0.95f ? EditorTheme.Recording : EditorTheme.Accent,
                MeterScale.Reduction => EditorTheme.Armed,
                _ => EditorTheme.AccentDark,
            });
            if (vertical)
                g.FillRectangle(brush, inner.X, inner.Bottom - inner.Height * fraction, inner.Width, inner.Height * fraction);
            else
                g.FillRectangle(brush, inner.X, inner.Y, inner.Width * fraction, inner.Height);
        }

        if (label is null)
            return;

        if (vertical)
            EditorDraw.Text(g, label, new RectangleF(Bounds.X - 6f, bar.Bottom, Bounds.Width + 12f, 10f), EditorTheme.SmallFont, EditorTheme.TextDim, EditorTheme.Center);
        else
            EditorDraw.Text(g, label, new RectangleF(Bounds.X, Bounds.Y, LabelWidth - 2f, Bounds.Height), EditorTheme.SmallFont, EditorTheme.TextDim, EditorTheme.Left);
    }

    public override bool Update()
    {
        var v = value();
        var target = scale switch
        {
            MeterScale.Peak => Math.Clamp((20f * MathF.Log10(MathF.Max(v, 1e-6f)) - PeakFloorDb) / -PeakFloorDb, 0f, 1f),
            MeterScale.Reduction => Math.Clamp(v / ReductionMaxDb, 0f, 1f),
            _ => Math.Clamp(v, 0f, 1f),
        };
        var next = target > shown || scale == MeterScale.Fraction ? target : MathF.Max(target, shown - ReleasePerFrame);
        if (MathF.Abs(next - shown) < 0.004f)
            return false;

        shown = next;

        return true;
    }
}
