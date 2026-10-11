using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Noumenon.Ui;

/// <summary>
/// The drawing vocabulary every widget is built from — module tiles, displays, knobs, bars,
/// LEDs, button faces, waveform glyphs, text — so the whole editor shares one hand: flat fills,
/// hairline edges, the accent only where a value lives. All coordinates are logical; the
/// caller's transform supplies the scale. Pens and brushes are created per call, which GDI+
/// handles cheaply and keeps the helpers stateless.
/// </summary>
public static class EditorDraw
{
    private const float KnobStartAngle = 135f;
    private const float KnobSweep = 270f;

    private static readonly Graphics Measurer = CreateMeasurer();

    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(radius * 2f, Math.Min(r.Width, r.Height));
        if (d <= 0.5f)
        {
            path.AddRectangle(r);
            return path;
        }

        path.AddArc(r.X, r.Y, d, d, 180f, 90f);
        path.AddArc(r.Right - d, r.Y, d, d, 270f, 90f);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0f, 90f);
        path.AddArc(r.X, r.Bottom - d, d, d, 90f, 90f);
        path.CloseFigure();

        return path;
    }

    public static void Frame(Graphics g, RectangleF r, float radius, Color fill, Color border, float borderWidth = 1f)
    {
        using var path = RoundedRect(r, radius);
        using var brush = new SolidBrush(fill);
        using var pen = new Pen(border, borderWidth);
        g.FillPath(brush, path);
        g.DrawPath(pen, path);
    }

    /// <summary>A module of the chassis: a flat tile with a hairline edge and its title in dim caps at the top-left.</summary>
    public static void PanelFrame(Graphics g, RectangleF r, string? title = null, bool inset = false)
    {
        Frame(g, r, 3f, inset ? EditorTheme.PanelInset : EditorTheme.PanelLight, EditorTheme.Frame, 1f);
        if (title is not null)
            Text(g, title, new RectangleF(r.X + 8, r.Y + 2, r.Width - 16, 13), EditorTheme.TitleFont, EditorTheme.TextDim, EditorTheme.Left);
    }

    public static void Display(Graphics g, RectangleF r, float radius = 2f) => Frame(g, r, radius, EditorTheme.Display, EditorTheme.DisplayEdge, 1f);

    public static void Text(Graphics g, string text, RectangleF r, Font font, Color color, StringFormat format)
    {
        using var brush = new SolidBrush(color);
        g.DrawString(text, font, brush, r, format);
    }

    /// <summary>The logical width of a string as <see cref="Text"/> lays it out, from a screen context, so a widget can size itself without a Graphics.</summary>
    public static float TextWidth(string text, Font font) => Measurer.MeasureString(text, font, PointF.Empty, EditorTheme.Center).Width;

    /// <summary>A readout floating over the panel: a display box around the value, as a knob shows its own under the mouse.</summary>
    public static void Badge(Graphics g, RectangleF r, string text)
    {
        Frame(g, r, 2f, EditorTheme.Display, EditorTheme.DisplayEdge, 1f);
        Text(g, text, r, EditorTheme.SmallValueFont, EditorTheme.TextLight, EditorTheme.Center);
    }

    private static Graphics CreateMeasurer()
    {
        var g = Graphics.FromHwnd(IntPtr.Zero);
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        return g;
    }

    public static void VerticalText(Graphics g, string text, PointF centre, Font font, Color color)
    {
        var state = g.Save();
        g.TranslateTransform(centre.X, centre.Y);
        g.RotateTransform(-90f);
        using var brush = new SolidBrush(color);
        g.DrawString(text, font, brush, new RectangleF(-80, -8, 160, 16), EditorTheme.Center);
        g.Restore(state);
    }

    /// <summary>
    /// A knob: the pointer shows <paramref name="pointer"/> (what the panel set), the outer arc
    /// shows <paramref name="arc"/> (what the engine resolved — a lane or the matrix can move it).
    /// Bipolar arcs grow from twelve o'clock.
    /// </summary>
    public static void Knob(Graphics g, RectangleF r, float pointer, float arc, bool bipolar, bool hot)
    {
        var track = RectangleF.Inflate(r, -1.5f, -1.5f);
        using (var trackPen = new Pen(EditorTheme.KnobTrack, 2f))
        {
            g.DrawArc(trackPen, track, KnobStartAngle, KnobSweep);
        }

        using (var arcPen = new Pen(EditorTheme.Accent, 2f))
        {
            var start = bipolar ? KnobStartAngle + KnobSweep / 2f : KnobStartAngle;
            var sweep = bipolar ? (arc - 0.5f) * KnobSweep : arc * KnobSweep;
            if (MathF.Abs(sweep) > 0.5f)
                g.DrawArc(arcPen, track, start, sweep);
        }

        var body = RectangleF.Inflate(r, -4.5f, -4.5f);
        using (var bodyBrush = new SolidBrush(EditorTheme.KnobBody))
        using (var ringPen = new Pen(hot ? EditorTheme.Accent : EditorTheme.KnobRing, 1f))
        {
            g.FillEllipse(bodyBrush, body);
            g.DrawEllipse(ringPen, body);
        }

        var angle = (KnobStartAngle + KnobSweep * pointer) * MathF.PI / 180f;
        var cx = body.X + body.Width / 2f;
        var cy = body.Y + body.Height / 2f;
        var inner = body.Width * 0.16f;
        var outer = body.Width / 2f - 1.5f;
        using (var pointerPen = new Pen(EditorTheme.Pointer, Math.Max(1.2f, body.Width / 14f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            g.DrawLine(pointerPen, cx + MathF.Cos(angle) * inner, cy + MathF.Sin(angle) * inner, cx + MathF.Cos(angle) * outer, cy + MathF.Sin(angle) * outer);
        }
    }

    public static void Led(Graphics g, RectangleF r, bool on, Color onColor)
    {
        using var brush = new SolidBrush(on ? onColor : EditorTheme.LedOff);
        g.FillEllipse(brush, r);
        if (!on)
            return;

        using var glow = new SolidBrush(Color.FromArgb(60, onColor));
        g.FillEllipse(glow, RectangleF.Inflate(r, 2f, 2f));
    }

    /// <summary>A button face: an outlined tile, filled in the dim accent and edged in the accent while <paramref name="lit"/>.</summary>
    public static void ButtonFace(Graphics g, RectangleF r, bool down, bool lit, string? text = null, Font? font = null)
    {
        Frame(g, r, 3f, down ? EditorTheme.ButtonDown : lit ? EditorTheme.ButtonOn : EditorTheme.Button, lit ? EditorTheme.Accent : EditorTheme.ButtonEdge, 1f);
        if (text is not null)
            Text(g, text, r, font ?? EditorTheme.SmallFont, lit ? EditorTheme.TextLight : EditorTheme.TextOnDark, EditorTheme.Center);
    }

    /// <summary>
    /// A fader as a bar: the track, the accent fill from the zero point (the centre for a bipolar
    /// parameter) to <paramref name="resolved"/> — what the engine plays — and a cap line at
    /// <paramref name="set"/>, what the panel set, so a lane moving the value is visible.
    /// </summary>
    public static void FaderBar(Graphics g, RectangleF track, float set, float resolved, bool bipolar, bool vertical, bool hot)
    {
        Frame(g, track, 3f, EditorTheme.Slot, hot ? EditorTheme.FrameLight : EditorTheme.ButtonEdge, 1f);
        var inner = RectangleF.Inflate(track, -1.5f, -1.5f);
        var zero = bipolar ? 0.5f : 0f;
        var from = Math.Min(zero, resolved);
        var to = Math.Max(zero, resolved);
        using (var fill = new SolidBrush(hot ? EditorTheme.Accent : EditorTheme.AccentDark))
        {
            if (vertical)
                g.FillRectangle(fill, inner.X, inner.Bottom - inner.Height * to, inner.Width, inner.Height * (to - from));
            else
                g.FillRectangle(fill, inner.X + inner.Width * from, inner.Y, inner.Width * (to - from), inner.Height);
        }

        using var cap = new Pen(hot ? EditorTheme.TextLight : EditorTheme.Cap, 2f);
        if (vertical)
        {
            var y = inner.Bottom - inner.Height * set;
            g.DrawLine(cap, track.X - 2f, y, track.Right + 2f, y);
        }
        else
        {
            var x = inner.X + inner.Width * set;
            g.DrawLine(cap, x, track.Y - 2f, x, track.Bottom + 2f);
        }
    }

    /// <summary>The oscillator icons, in the order of the Waveforms choice list (sine … S&amp;H noise).</summary>
    public static void WaveGlyph(Graphics g, RectangleF r, int waveform, Color color)
    {
        using var pen = new Pen(color, 1.3f) { LineJoin = LineJoin.Round };
        var x0 = r.X + 2;
        var x1 = r.Right - 2;
        var w = x1 - x0;
        var mid = r.Y + r.Height / 2f;
        var amp = r.Height / 2f - 2.5f;
        switch (waveform)
        {
            case 0:
                var sine = new PointF[24];
                for (var i = 0; i < sine.Length; i++)
                {
                    var t = i / (float)(sine.Length - 1);
                    sine[i] = new PointF(x0 + w * t, mid - amp * MathF.Sin(t * MathF.PI * 2f));
                }

                g.DrawLines(pen, sine);
                break;
            case 1:
                g.DrawLines(pen, [new PointF(x0, mid), new PointF(x0 + w * 0.25f, mid - amp), new PointF(x0 + w * 0.75f, mid + amp), new PointF(x1, mid)]);
                break;
            case 2:
                g.DrawLines(pen, [new PointF(x0, mid + amp), new PointF(x0 + w * 0.5f, mid - amp), new PointF(x0 + w * 0.5f, mid + amp), new PointF(x1, mid - amp)]);
                break;
            case 3:
                g.DrawLines(pen, [new PointF(x0, mid + amp), new PointF(x0, mid - amp), new PointF(x0 + w * 0.5f, mid - amp), new PointF(x0 + w * 0.5f, mid + amp), new PointF(x1, mid + amp), new PointF(x1, mid - amp)]);
                break;
            case 4:
                g.DrawLines(pen, [new PointF(x0, mid + amp), new PointF(x0, mid - amp), new PointF(x0 + w * 0.22f, mid - amp), new PointF(x0 + w * 0.22f, mid + amp), new PointF(x1, mid + amp)]);
                break;
            case 5:
                g.DrawLines(pen, [new PointF(x0, mid), new PointF(x0, mid - amp), new PointF(x0 + w * 0.2f, mid - amp), new PointF(x0 + w * 0.2f, mid), new PointF(x0 + w * 0.55f, mid), new PointF(x0 + w * 0.55f, mid + amp), new PointF(x0 + w * 0.75f, mid + amp), new PointF(x0 + w * 0.75f, mid), new PointF(x1, mid)]);
                break;
            case 6:
                var noise = new PointF[14];
                var seed = 0x2545F491u;
                for (var i = 0; i < noise.Length; i++)
                {
                    seed = seed * 1664525u + 1013904223u;
                    var n = ((seed >> 9) & 0xFFFF) / 65535f * 2f - 1f;
                    noise[i] = new PointF(x0 + w * i / (noise.Length - 1), mid - amp * n * (i == 0 || i == noise.Length - 1 ? 0.3f : 1f));
                }

                g.DrawLines(pen, noise);
                break;
            default:
                var steps = new List<PointF>();
                var state = 0x9E3779B9u;
                var columns = 6;
                for (var i = 0; i < columns; i++)
                {
                    state = state * 1664525u + 1013904223u;
                    var level = mid - amp * (((state >> 9) & 0xFFFF) / 65535f * 2f - 1f);
                    steps.Add(new PointF(x0 + w * i / columns, level));
                    steps.Add(new PointF(x0 + w * (i + 1) / columns, level));
                }

                g.DrawLines(pen, steps.ToArray());
                break;
        }
    }

    public static void Triangle(Graphics g, RectangleF r, bool pointRight, Color color)
    {
        using var brush = new SolidBrush(color);
        var points = pointRight
            ? new[] { new PointF(r.X, r.Y), new PointF(r.Right, r.Y + r.Height / 2f), new PointF(r.X, r.Bottom) }
            : new[] { new PointF(r.Right, r.Y), new PointF(r.X, r.Y + r.Height / 2f), new PointF(r.Right, r.Bottom) };
        g.FillPolygon(brush, points);
    }

    public static void Chevron(Graphics g, RectangleF r, Color color)
    {
        using var brush = new SolidBrush(color);
        g.FillPolygon(brush, [new PointF(r.X, r.Y), new PointF(r.Right, r.Y), new PointF(r.X + r.Width / 2f, r.Bottom)]);
    }
}
