using Noumenon.Dsp.Sampler;
using Noumenon.Engine;
using Noumenon.Engine.Parameters;

namespace Noumenon.Ui.Widgets;

/// <summary>
/// The sampler's display: the selected key's waveform with the loop range as two handles, the
/// play head, and the file's name. Drag a handle to move a loop point, drag the region to shift
/// the whole loop, double-click (or drop a file on it) to load a sample into the selected key,
/// right-click for load, clear, loop reset and retrigger. Peaks are computed once per sample and
/// kept until the sample changes.
/// </summary>
public sealed class WaveformWidget : Widget, IFileDropTarget
{
    private const float HandleGrab = 5f;
    private const float MinLoop = 0.01f;

    private enum DragMode
    {
        None,
        Start,
        End,
        Both,
    }

    private readonly NoumenonEngine engine;
    private readonly IEditorHost host;
    private readonly ParamBinding loopStart;
    private readonly ParamBinding loopEnd;
    private SampleData? cachedData;
    private float[] peaks = [];
    private DragMode drag;
    private float dragOffsetStart;
    private float dragOffsetEnd;
    private int shownPosition = -1;
    private float shownStart = float.NaN;
    private float shownEnd = float.NaN;
    private SampleData? shownData;

    public WaveformWidget(NoumenonEngine engine, IEditorHost host, float x, float y, float width, float height)
    {
        this.engine = engine;
        this.host = host;
        loopStart = new ParamBinding(engine, ParamId.SamplerLoopStart);
        loopEnd = new ParamBinding(engine, ParamId.SamplerLoopEnd);
        Bounds = new RectangleF(x, y, width, height);
    }

    public override string? Tooltip => Selected is { } slot
        ? $"Key {slot.Key}: {slot.Name} ({slot.Data.Seconds:0.00} s, {slot.Data.SampleRate} Hz)\nloop {loopStart.ValueText} .. {loopEnd.ValueText}\ndrag handles: loop · double-click or drop: load · right-click: menu"
        : "No sample on the selected key\ndouble-click or drop a file to load one";

    private int Key => engine.Parameters.GetInt(ParamId.SamplerSelect);

    private Engine.Samples.SampleSlot? Selected => engine.Samples.Get(Key);

    private RectangleF Inner => RectangleF.Inflate(Bounds, -2f, -2f);

    public override void Draw(Graphics g)
    {
        EditorDraw.Display(g, Bounds, 3f);
        var slot = Selected;
        var inner = Inner;
        if (slot is null)
        {
            EditorDraw.Text(g, $"key {Key}: no sample — drop a file or double-click", inner, EditorTheme.SmallFont, EditorTheme.TextDim, EditorTheme.Center);
            return;
        }

        EnsurePeaks(slot.Data, (int)inner.Width);
        var start = loopStart.Value;
        var end = loopEnd.Value;
        using (var loopBrush = new SolidBrush(Color.FromArgb(90, EditorTheme.Loop)))
        {
            g.FillRectangle(loopBrush, inner.X + inner.Width * start, inner.Y, inner.Width * Math.Max(0f, end - start), inner.Height);
        }

        var mid = inner.Y + inner.Height / 2f;
        var amp = inner.Height / 2f - 1f;
        var columns = peaks.Length / 2;
        using (var tracePen = new Pen(EditorTheme.Trace, 1f))
        using (var dimPen = new Pen(EditorTheme.TraceDim, 1f))
        {
            for (var c = 0; c < columns; c++)
            {
                var fraction = c / (float)columns;
                var x = inner.X + c + 0.5f;
                var pen = fraction >= start && fraction <= end ? tracePen : dimPen;
                g.DrawLine(pen, x, mid - peaks[c * 2 + 1] * amp, x, mid - peaks[c * 2] * amp);
            }
        }

        using (var handlePen = new Pen(EditorTheme.Armed, 1.5f))
        {
            var sx = inner.X + inner.Width * start;
            var ex = inner.X + inner.Width * end;
            g.DrawLine(handlePen, sx, inner.Y, sx, inner.Bottom);
            g.DrawLine(handlePen, ex, inner.Y, ex, inner.Bottom);
        }

        var length = Math.Max(1, slot.Data.Length);
        var position = (float)Math.Clamp(engine.SamplePosition / length, 0.0, 1.0);
        using (var headPen = new Pen(EditorTheme.TextLight, 1f))
        {
            var px = inner.X + inner.Width * position;
            g.DrawLine(headPen, px, inner.Y, px, inner.Bottom);
        }

        EditorDraw.Text(g, $"{slot.Key}: {slot.Name}", new RectangleF(inner.X + 2f, inner.Y, inner.Width - 60f, 10f), EditorTheme.SmallFont, EditorTheme.TextDim, EditorTheme.Left);
        EditorDraw.Text(g, $"{slot.Data.Seconds:0.00} s", new RectangleF(inner.Right - 58f, inner.Y, 56f, 10f), EditorTheme.SmallFont, EditorTheme.TextDim, EditorTheme.Right);
    }

    public override bool Update()
    {
        var slot = Selected;
        var data = slot?.Data;
        var position = data is null ? -1 : (int)(engine.SamplePosition / Math.Max(1, data.Length) * Inner.Width);
        var start = loopStart.Value;
        var end = loopEnd.Value;
        if (ReferenceEquals(data, shownData) && position == shownPosition && start == shownStart && end == shownEnd)
            return false;

        shownData = data;
        shownPosition = position;
        shownStart = start;
        shownEnd = end;

        return true;
    }

    public override void MouseDown(PointF p, MouseButtons button, int clicks)
    {
        if (button == MouseButtons.Right)
        {
            ShowMenu(BuildMenu(), p);
            return;
        }

        if (button != MouseButtons.Left)
            return;

        if (clicks >= 2)
        {
            host.LoadSample(Key);
            return;
        }

        if (Selected is null)
            return;

        var inner = Inner;
        var fraction = (p.X - inner.X) / inner.Width;
        var start = loopStart.Value;
        var end = loopEnd.Value;
        var startPx = inner.X + inner.Width * start;
        var endPx = inner.X + inner.Width * end;
        if (MathF.Abs(p.X - startPx) <= HandleGrab)
        {
            drag = DragMode.Start;
        }
        else if (MathF.Abs(p.X - endPx) <= HandleGrab)
        {
            drag = DragMode.End;
        }
        else
        {
            drag = DragMode.Both;
            dragOffsetStart = start - fraction;
            dragOffsetEnd = end - fraction;
        }
    }

    public override void MouseMove(PointF p, MouseButtons buttons)
    {
        if (drag == DragMode.None)
            return;

        var inner = Inner;
        var fraction = Math.Clamp((p.X - inner.X) / inner.Width, 0f, 1f);
        switch (drag)
        {
            case DragMode.Start:
                loopStart.Value = Math.Min(fraction, loopEnd.Value - MinLoop);
                break;
            case DragMode.End:
                loopEnd.Value = Math.Max(fraction, loopStart.Value + MinLoop);
                break;
            case DragMode.Both:
                var length = dragOffsetEnd - dragOffsetStart;
                var start = Math.Clamp(fraction + dragOffsetStart, 0f, 1f - length);
                loopStart.Value = start;
                loopEnd.Value = start + length;
                break;
        }

        Invalidate();
    }

    public override void MouseUp(PointF p, MouseButtons button) => drag = DragMode.None;

    public void DropFiles(string[] paths) => host.LoadSampleFile(Key, paths[0]);

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        var key = Key;
        menu.Items.Add($"Load sample into key {key}…", null, (_, _) => host.LoadSample(key));
        menu.Items.Add("Clear key", null, (_, _) => engine.Samples.Clear(key));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Reset loop to the whole sample", null, (_, _) => Commit(() =>
        {
            loopStart.Reset();
            loopEnd.Reset();
        }));
        menu.Items.Add("Retrigger (gate)", null, (_, _) => engine.Gate());

        return menu;
    }

    private void EnsurePeaks(SampleData data, int columns)
    {
        columns = Math.Max(1, columns);
        if (ReferenceEquals(data, cachedData) && peaks.Length == columns * 2)
            return;

        cachedData = data;
        peaks = new float[columns * 2];
        var length = data.Length;
        for (var c = 0; c < columns; c++)
        {
            var from = (int)((long)length * c / columns);
            var to = Math.Max(from + 1, (int)((long)length * (c + 1) / columns));
            var min = 0f;
            var max = 0f;
            for (var i = from; i < to && i < length; i++)
            {
                var v = (data.Left[i] + data.Right[i]) * 0.5f;
                if (v < min)
                    min = v;

                if (v > max)
                    max = v;
            }

            peaks[c * 2] = min;
            peaks[c * 2 + 1] = max;
        }
    }
}
