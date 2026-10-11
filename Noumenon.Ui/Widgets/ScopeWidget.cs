using System.Drawing.Drawing2D;
using Noumenon.Engine;
using Noumenon.Engine.Parameters;
using Noumenon.Ui.Analysis;

namespace Noumenon.Ui.Widgets;

/// <summary>
/// The main display, in the mode the Scope Mode parameter selects. Output: the post-Drift
/// signal from the engine's scope ring, left against right, each read through its own delay tap
/// (the X and Y phase parameters) so a drone draws rotating figures instead of a diagonal line,
/// plus a third tap as depth and a slow rotation that turns the figure into a wireframe tube.
/// Sections: section A against section B, the ring modulation made visible. Spectrum: the
/// output's spectrum on a log axis. Drawn into its own bitmap with persistence, at the device
/// resolution, thirty times a second. Drag to set the phases (Output), wheel to zoom,
/// double-click to reset.
/// </summary>
public sealed class ScopeWidget : Widget
{
    private const int Points = 1024;
    private const float MaxPhaseMs = 50f;
    private const float MsPerPixel = 0.25f;
    private const float RotationDegreesPerFrame = 0.35f;
    private const float TiltRadians = 0.5f;
    private const int PersistenceAlpha = 70;
    private const float FillFraction = 0.9f;

    private readonly NoumenonEngine engine;
    private readonly ParamBinding phaseX;
    private readonly ParamBinding phaseY;
    private readonly ParamBinding zoom;
    private readonly ParamBinding mode;
    private readonly float[] left = new float[ScopeBuffer.Capacity];
    private readonly float[] right = new float[ScopeBuffer.Capacity];
    private readonly float[] mono = new float[SpectrumAnalyzer.Size];
    private readonly PointF[] projected = new PointF[Points];
    private readonly SpectrumAnalyzer analyzer = new();
    private PointF[] fill = [];
    private Bitmap? image;
    private float angle;
    private bool dragging;
    private PointF dragStart;
    private float dragStartX;
    private float dragStartY;

    public ScopeWidget(NoumenonEngine engine, float x, float y, float width, float height)
    {
        this.engine = engine;
        phaseX = new ParamBinding(engine, ParamId.ScopePhaseX);
        phaseY = new ParamBinding(engine, ParamId.ScopePhaseY);
        zoom = new ParamBinding(engine, ParamId.ScopeZoom);
        mode = new ParamBinding(engine, ParamId.ScopeMode);
        Bounds = new RectangleF(x, y, width, height);
    }

    private int Mode => (int)MathF.Round(mode.Value);

    public override string? Tooltip => Mode switch
    {
        1 => $"A against B · zoom {zoom.ValueText}\nwheel: zoom · double-click: reset",
        2 => "Output spectrum, 30 Hz to 8 kHz\nwheel: zoom",
        _ => $"XY output · X phase {phaseX.ValueText}, Y phase {phaseY.ValueText}, zoom {zoom.ValueText}\ndrag: phases · wheel: zoom · double-click: reset",
    };

    public override void Draw(Graphics g)
    {
        var scale = Surface?.PanelScale ?? 1f;
        var w = Math.Max(8, (int)MathF.Round(Bounds.Width * scale));
        var h = Math.Max(8, (int)MathF.Round(Bounds.Height * scale));
        if (image is null || image.Width != w || image.Height != h)
        {
            image?.Dispose();
            image = new Bitmap(w, h);
            using var clear = Graphics.FromImage(image);
            clear.Clear(EditorTheme.Display);
        }

        switch (Mode)
        {
            case 1:
                RenderSections(image, w, h);
                break;
            case 2:
                RenderSpectrum(image, w, h);
                break;
            default:
                RenderOutput(image, w, h);
                break;
        }

        var state = g.Save();
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.DrawImage(image, Bounds);
        g.Restore(state);
        using var edge = new Pen(EditorTheme.DisplayEdge, 1f);
        g.DrawRectangle(edge, Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height);
    }

    public override bool Update() => true;

    public override void MouseDown(PointF p, MouseButtons button, int clicks)
    {
        if (button != MouseButtons.Left)
            return;

        if (clicks >= 2)
        {
            phaseX.Reset();
            phaseY.Reset();
            zoom.Reset();
            return;
        }

        if (Mode != 0)
            return;

        dragging = true;
        dragStart = p;
        dragStartX = phaseX.Value;
        dragStartY = phaseY.Value;
    }

    public override void MouseMove(PointF p, MouseButtons buttons)
    {
        if (!dragging)
            return;

        phaseX.Value = Math.Clamp(dragStartX + (p.X - dragStart.X) * MsPerPixel, 0f, MaxPhaseMs);
        phaseY.Value = Math.Clamp(dragStartY - (p.Y - dragStart.Y) * MsPerPixel, 0f, MaxPhaseMs);
    }

    public override void MouseUp(PointF p, MouseButtons button) => dragging = false;

    public override void MouseWheel(PointF p, int delta) => Commit(() => zoom.Value += delta > 0 ? 0.1f : -0.1f);

    private static void Fade(Graphics g, int w, int h)
    {
        using var fade = new SolidBrush(Color.FromArgb(PersistenceAlpha, EditorTheme.Display));
        g.FillRectangle(fade, 0, 0, w, h);
    }

    private static void Crosshair(Graphics g, int w, int h)
    {
        using var grid = new Pen(Color.FromArgb(40, EditorTheme.Trace), 1f);
        g.DrawLine(grid, w / 2f, 0f, w / 2f, h);
        g.DrawLine(grid, 0f, h / 2f, w, h / 2f);
    }

    private void RenderOutput(Bitmap target, int w, int h)
    {
        using var g = Graphics.FromImage(target);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Fade(g, w, h);
        Crosshair(g, w, h);

        var sampleRate = engine.SampleRate;
        var dx = (int)(phaseX.Value * sampleRate / 1000.0);
        var dy = (int)(phaseY.Value * sampleRate / 1000.0);
        var dz = dx + dy + 16;
        var maxDelay = Math.Min(dz, ScopeBuffer.Capacity - Points);
        dx = Math.Min(dx, maxDelay);
        dy = Math.Min(dy, maxDelay);
        dz = Math.Min(dz, maxDelay);
        engine.Scope.CopyLatest(left, right, Points + maxDelay);

        angle += RotationDegreesPerFrame * MathF.PI / 180f;
        if (angle > MathF.PI * 2f)
            angle -= MathF.PI * 2f;

        var cosT = MathF.Cos(angle);
        var sinT = MathF.Sin(angle);
        var cosP = MathF.Cos(TiltRadians);
        var sinP = MathF.Sin(TiltRadians);
        var half = Math.Min(w, h) * 0.5f * FillFraction * zoom.Value;
        var cx = w * 0.5f;
        var cy = h * 0.5f;
        var baseX = maxDelay - dx;
        var baseY = maxDelay - dy;
        var baseZ = maxDelay - dz;
        for (var i = 0; i < Points; i++)
        {
            var x = left[baseX + i];
            var y = right[baseY + i];
            var z = (left[baseZ + i] + right[baseZ + i]) * 0.5f;
            var rx = x * cosT + z * sinT;
            var rz = -x * sinT + z * cosT;
            var ry = y * cosP - rz * sinP;
            projected[i] = new PointF(cx + rx * half, cy - ry * half);
        }

        using var pen = new Pen(Color.FromArgb(200, EditorTheme.Trace), 1f) { LineJoin = LineJoin.Round };
        g.DrawLines(pen, projected);
    }

    private void RenderSections(Bitmap target, int w, int h)
    {
        using var g = Graphics.FromImage(target);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Fade(g, w, h);
        Crosshair(g, w, h);
        engine.SectionScope.CopyLatest(left, right, Points);
        var half = Math.Min(w, h) * 0.5f * FillFraction * zoom.Value;
        var cx = w * 0.5f;
        var cy = h * 0.5f;
        for (var i = 0; i < Points; i++)
            projected[i] = new PointF(cx + left[i] * half, cy - right[i] * half);

        using var pen = new Pen(Color.FromArgb(200, EditorTheme.Trace), 1f) { LineJoin = LineJoin.Round };
        g.DrawLines(pen, projected);
        EditorDraw.Text(g, "A", new RectangleF(w - 14f, h / 2f - 12f, 12f, 12f), EditorTheme.SmallFont, EditorTheme.TextDim, EditorTheme.Center);
        EditorDraw.Text(g, "B", new RectangleF(w / 2f + 2f, 1f, 12f, 12f), EditorTheme.SmallFont, EditorTheme.TextDim, EditorTheme.Center);
    }

    private void RenderSpectrum(Bitmap target, int w, int h)
    {
        using var g = Graphics.FromImage(target);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(EditorTheme.Display);
        using (var grid = new Pen(EditorTheme.DisplayGrid, 1f))
        {
            for (var hz = 62.5f; hz < SpectrumAnalyzer.MaxHz; hz *= 2f)
            {
                var x = w * SpectrumAnalyzer.Position(hz);
                g.DrawLine(grid, x, 0f, x, h);
            }

            for (var line = 0f; line > SpectrumAnalyzer.FloorDb; line -= 12f)
            {
                var y = h * (1f - SpectrumAnalyzer.Level(line));
                g.DrawLine(grid, 0f, y, w, y);
            }
        }

        engine.Scope.CopyLatest(left, right, SpectrumAnalyzer.Size);
        for (var i = 0; i < SpectrumAnalyzer.Size; i++)
            mono[i] = (left[i] + right[i]) * 0.5f;

        analyzer.Compute(mono, SpectrumAnalyzer.Size);
        var columns = Math.Max(2, w);
        if (fill.Length != columns + 2)
            fill = new PointF[columns + 2];

        var binHz = (float)(engine.SampleRate / SpectrumAnalyzer.Size);
        var db = analyzer.Db;
        for (var c = 0; c < columns; c++)
        {
            var bin0 = Math.Clamp((int)(SpectrumAnalyzer.HzAt(c / (float)columns) / binHz), 1, db.Length - 1);
            var bin1 = Math.Clamp((int)(SpectrumAnalyzer.HzAt((c + 1) / (float)columns) / binHz), bin0, db.Length - 1);
            var peak = db[bin0];
            for (var k = bin0 + 1; k <= bin1; k++)
            {
                if (db[k] > peak)
                    peak = db[k];
            }

            fill[c + 1] = new PointF(c, h * (1f - SpectrumAnalyzer.Level(peak)));
        }

        fill[0] = new PointF(0f, h);
        fill[columns + 1] = new PointF(columns - 1, h);
        using var brush = new SolidBrush(Color.FromArgb(80, EditorTheme.Accent));
        using var outline = new Pen(Color.FromArgb(200, EditorTheme.Accent), 1f);
        g.FillPolygon(brush, fill);
        g.DrawLines(outline, fill.AsSpan(1, columns).ToArray());
    }
}
