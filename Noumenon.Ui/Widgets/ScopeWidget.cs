using System.Drawing.Drawing2D;
using Noumenon.Engine;
using Noumenon.Engine.Parameters;

namespace Noumenon.Ui.Widgets;

/// <summary>
/// The XY display: the post-Spin signal from the engine's scope ring, left against right, each
/// read through its own delay tap (the X and Y phase parameters) so a drone draws rotating
/// figures instead of a diagonal line, plus a third tap as depth and a slow rotation that turns
/// the figure into the wireframe tube the original is known for. Drawn into its own bitmap with
/// persistence, at the device resolution, thirty times a second. Drag to set the phases, wheel
/// to zoom, double-click to reset.
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
    private readonly float[] left = new float[ScopeBuffer.Capacity];
    private readonly float[] right = new float[ScopeBuffer.Capacity];
    private readonly PointF[] projected = new PointF[Points];
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
        Bounds = new RectangleF(x, y, width, height);
    }

    public override string? Tooltip => $"XY scope · X phase {phaseX.ValueText}, Y phase {phaseY.ValueText}, zoom {zoom.ValueText}\ndrag: phases · wheel: zoom · double-click: reset";

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

        Render(image, w, h);
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

    private void Render(Bitmap target, int w, int h)
    {
        using var g = Graphics.FromImage(target);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var fade = new SolidBrush(Color.FromArgb(PersistenceAlpha, EditorTheme.Display)))
        {
            g.FillRectangle(fade, 0, 0, w, h);
        }

        using (var grid = new Pen(Color.FromArgb(40, EditorTheme.Trace), 1f))
        {
            g.DrawLine(grid, w / 2f, 0f, w / 2f, h);
            g.DrawLine(grid, 0f, h / 2f, w, h / 2f);
        }

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
}
