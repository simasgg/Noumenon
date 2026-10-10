using System.Drawing.Drawing2D;

namespace Noumenon.Ui;

/// <summary>
/// The drawing vocabulary every widget is built from — panel frames, displays, knobs, fader
/// slots and caps, LEDs, button faces, waveform glyphs, text — so the whole editor shares one
/// hand. All coordinates are logical; the caller's transform supplies the scale. Pens and
/// brushes are created per call, which GDI+ handles cheaply and keeps the helpers stateless.
/// </summary>
public static class EditorDraw
{
    private const float KnobStartAngle = 135f;
    private const float KnobSweep = 270f;

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

    /// <summary>A raised module of the chassis with its title in the top-left corner.</summary>
    public static void PanelFrame(Graphics g, RectangleF r, string? title = null, bool inset = false)
    {
        Frame(g, r, 4f, inset ? EditorTheme.PanelInset : EditorTheme.PanelLight, EditorTheme.Frame, 1.2f);
        using (var highlight = new Pen(Color.FromArgb(90, EditorTheme.FrameLight)))
        {
            g.DrawLine(highlight, r.X + 3, r.Y + 1.5f, r.Right - 3, r.Y + 1.5f);
        }

        if (title is not null)
            Text(g, title, new RectangleF(r.X + 8, r.Y + 2, r.Width - 16, 13), EditorTheme.TitleFont, EditorTheme.Text, EditorTheme.Left);
    }

    public static void Display(Graphics g, RectangleF r, float radius = 2f)
    {
        Frame(g, r, radius, EditorTheme.Display, EditorTheme.DisplayEdge, 1f);
        using var shadow = new Pen(Color.FromArgb(120, Color.Black));
        g.DrawLine(shadow, r.X + 1, r.Y + 1, r.Right - 1, r.Y + 1);
    }

    public static void Text(Graphics g, string text, RectangleF r, Font font, Color color, StringFormat format)
    {
        using var brush = new SolidBrush(color);
        g.DrawString(text, font, brush, r, format);
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
        using (var trackPen = new Pen(EditorTheme.KnobTrack, 2.2f))
        {
            g.DrawArc(trackPen, track, KnobStartAngle, KnobSweep);
        }

        using (var arcPen = new Pen(EditorTheme.Trace, 2.2f))
        {
            var start = bipolar ? KnobStartAngle + KnobSweep / 2f : KnobStartAngle;
            var sweep = bipolar ? (arc - 0.5f) * KnobSweep : arc * KnobSweep;
            if (MathF.Abs(sweep) > 0.5f)
                g.DrawArc(arcPen, track, start, sweep);
        }

        var body = RectangleF.Inflate(r, -4.5f, -4.5f);
        using (var bodyBrush = new SolidBrush(EditorTheme.KnobBody))
        using (var ringPen = new Pen(hot ? EditorTheme.FrameLight : EditorTheme.KnobRing, 1f))
        {
            g.FillEllipse(bodyBrush, body);
            g.DrawEllipse(ringPen, body);
        }

        var face = RectangleF.Inflate(body, -body.Width * 0.22f, -body.Height * 0.22f);
        using (var faceBrush = new LinearGradientBrush(face, EditorTheme.Accent, EditorTheme.AccentDark, 70f))
        {
            g.FillEllipse(faceBrush, face);
        }

        var angle = (KnobStartAngle + KnobSweep * pointer) * MathF.PI / 180f;
        var cx = body.X + body.Width / 2f;
        var cy = body.Y + body.Height / 2f;
        var inner = face.Width * 0.18f;
        var outer = body.Width / 2f - 1.5f;
        using (var pointerPen = new Pen(EditorTheme.Pointer, Math.Max(1.2f, body.Width / 14f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            g.DrawLine(pointerPen, cx + MathF.Cos(angle) * inner, cy + MathF.Sin(angle) * inner, cx + MathF.Cos(angle) * outer, cy + MathF.Sin(angle) * outer);
        }
    }

    public static void Led(Graphics g, RectangleF r, bool on, Color onColor)
    {
        using var brush = new SolidBrush(on ? onColor : EditorTheme.LedOff);
        using var pen = new Pen(EditorTheme.ButtonEdge, 1f);
        g.FillEllipse(brush, r);
        g.DrawEllipse(pen, r);
        if (!on)
            return;

        using var glow = new SolidBrush(Color.FromArgb(70, onColor));
        g.FillEllipse(glow, RectangleF.Inflate(r, 2f, 2f));
    }

    /// <summary>A push button face; <paramref name="lit"/> draws the LED strip along the bottom edge.</summary>
    public static void ButtonFace(Graphics g, RectangleF r, bool down, bool lit, string? text = null, Font? font = null)
    {
        Frame(g, r, 2.5f, down ? EditorTheme.ButtonDown : lit ? EditorTheme.ButtonOn : EditorTheme.Button, EditorTheme.ButtonEdge, 1f);
        var strip = new RectangleF(r.X + 3, r.Bottom - 3.5f, r.Width - 6, 2f);
        using (var stripBrush = new SolidBrush(lit ? EditorTheme.LedOn : EditorTheme.LedOff))
        {
            g.FillRectangle(stripBrush, strip);
        }

        if (text is not null)
            Text(g, text, new RectangleF(r.X, r.Y, r.Width, r.Height - 3f), font ?? EditorTheme.SmallFont, lit ? EditorTheme.TextLight : EditorTheme.TextOnDark, EditorTheme.Center);
    }

    public static void FaderSlot(Graphics g, RectangleF r)
    {
        Frame(g, r, 2f, EditorTheme.Slot, EditorTheme.ButtonEdge, 1f);
        using var line = new Pen(Color.FromArgb(70, EditorTheme.FrameLight), 1f);
        if (r.Height > r.Width)
            g.DrawLine(line, r.X + r.Width / 2f, r.Y + 3, r.X + r.Width / 2f, r.Bottom - 3);
        else
            g.DrawLine(line, r.X + 3, r.Y + r.Height / 2f, r.Right - 3, r.Y + r.Height / 2f);
    }

    public static void FaderCap(Graphics g, RectangleF r, bool vertical, bool hot)
    {
        using var path = RoundedRect(r, 2f);
        using var brush = new LinearGradientBrush(r, hot ? Color.FromArgb(0xDE, 0xE2, 0xCE) : EditorTheme.Cap, EditorTheme.CapEdge, vertical ? 90f : 0f);
        using var edge = new Pen(EditorTheme.ButtonEdge, 1f);
        using var line = new Pen(EditorTheme.CapLine, 1.4f);
        g.FillPath(brush, path);
        g.DrawPath(edge, path);
        if (vertical)
            g.DrawLine(line, r.X + 2, r.Y + r.Height / 2f, r.Right - 2, r.Y + r.Height / 2f);
        else
            g.DrawLine(line, r.X + r.Width / 2f, r.Y + 2, r.X + r.Width / 2f, r.Bottom - 2);
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
