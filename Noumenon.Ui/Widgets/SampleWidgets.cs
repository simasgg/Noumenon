using Noumenon.Dsp.Oscillators;
using Noumenon.Engine;
using Noumenon.Engine.Parameters;
using Noumenon.Engine.Samples;

namespace Noumenon.Ui.Widgets;

/// <summary>
/// The wavetable on the Wavetable Key as a waterfall: a dozen of its frames drawn back to front,
/// each one cycle, with the frame that a Wavetable slot's Shape points at drawn bright in front
/// of the others, and which slots play it. It reads the table the engine reads, so a sample that
/// is still loading shows as "no table". Redrawn only when the table, the pointed-at frames or
/// the slots change.
/// </summary>
public sealed class WavetableViewWidget : Widget
{
    private const int ShownFrames = 12;
    private const int Points = 64;

    private readonly NoumenonEngine engine;
    private readonly PointF[] line = new PointF[Points];
    private Wavetable? shownTable;
    private int shownHash = -1;

    public WavetableViewWidget(NoumenonEngine engine, float x, float y, float width, float height)
    {
        this.engine = engine;
        Bounds = new RectangleF(x, y, width, height);
    }

    public override string? Tooltip => "The wavetable on the Wavetable Key: its frames back to front, the ones Wavetable slots point at in front";

    private Wavetable? Table => engine.Samples.Get(engine.Parameters.GetInt(ParamId.WavetableKey))?.Data.Wavetable;

    public override void Draw(Graphics g)
    {
        EditorDraw.Display(g, Bounds, 3f);
        var table = Table;
        var inner = RectangleF.Inflate(Bounds, -4f, -4f);
        if (table is null)
        {
            EditorDraw.Text(g, $"key {engine.Parameters.GetInt(ParamId.WavetableKey)}: no sample, no table", inner, EditorTheme.SmallFont, EditorTheme.TextDim, EditorTheme.Center);
            return;
        }

        // A waterfall, back to front: the last frame at the back (up and to the right), each one
        // filled beneath its line so it hides the ones behind it.
        EditorDraw.Text(g, $"{table.FrameCount} frames · played by {SlotNames()}", new RectangleF(inner.X, inner.Y, inner.Width, 10f), EditorTheme.SmallFont, EditorTheme.TextDim, EditorTheme.Left);
        var area = new RectangleF(inner.X, inner.Y + 14f, inner.Width, inner.Height - 14f);
        var shown = Math.Min(ShownFrames, table.FrameCount);
        var depthX = area.Width * 0.22f;
        var cycleWidth = area.Width - depthX;
        var amp = Math.Min(area.Height * 0.2f, 28f);
        var depthY = area.Height - 2f * amp - 2f;
        using (var dim = new Pen(EditorTheme.TraceDim, 1f))
        using (var fill = new SolidBrush(EditorTheme.Display))
        {
            for (var k = shown - 1; k >= 0; k--)
            {
                var position = shown == 1 ? 0f : k / (float)(shown - 1);
                DrawCycle(g, dim, fill, table, position, area, depthX, depthY, cycleWidth, amp);
            }
        }

        using var bright = new Pen(EditorTheme.Trace, 1.5f);
        foreach (var position in SlotPositions())
            DrawCycle(g, bright, null, table, position, area, depthX, depthY, cycleWidth, amp);
    }

    public override bool Update()
    {
        var table = Table;
        var hash = 17;
        foreach (var position in SlotPositions())
            hash = hash * 31 + (int)(position * 200f);

        if (ReferenceEquals(table, shownTable) && hash == shownHash)
            return false;

        shownTable = table;
        shownHash = hash;

        return true;
    }

    /// <summary>One frame of the waterfall: frame position 0 in front at the lower left, 1 at the back.</summary>
    private void DrawCycle(Graphics g, Pen pen, Brush? fill, Wavetable table, float position, RectangleF area, float depthX, float depthY, float width, float amp)
    {
        var x = area.X + depthX * position;
        var baseline = area.Bottom - amp - 1f - depthY * position;
        for (var i = 0; i < Points; i++)
        {
            var phase = i / (double)(Points - 1);
            line[i] = new PointF(x + width * (float)phase, baseline - amp * Math.Clamp(table.Read(phase % 1.0, position, 2), -1.2f, 1.2f));
        }

        if (fill is not null)
        {
            using var shape = new System.Drawing.Drawing2D.GraphicsPath();
            shape.AddLines(line);
            shape.AddLine(line[^1], new PointF(line[^1].X, baseline + amp + 1f));
            shape.AddLine(new PointF(line[^1].X, baseline + amp + 1f), new PointF(line[0].X, baseline + amp + 1f));
            shape.CloseFigure();
            g.FillPath(fill, shape);
        }

        g.DrawLines(pen, line);
    }

    /// <summary>The Shape of every slot that plays the Wavetable wave, in panel order.</summary>
    private IEnumerable<float> SlotPositions()
    {
        foreach (var (section, slot) in WavetableSlots())
            yield return engine.Resolved.Get(ParameterTable.Osc(section, slot, OscParam.Shape));
    }

    private string SlotNames()
    {
        var names = WavetableSlots().Select(s => $"{s.Section}{s.Slot + 1}").ToArray();

        return names.Length == 0 ? "no slot (pick Wavetable as a wave)" : string.Join(" ", names);
    }

    private IEnumerable<(SectionId Section, int Slot)> WavetableSlots()
    {
        for (var s = 0; s < ParameterTable.SectionCount; s++)
        {
            for (var slot = 0; slot < ParameterTable.SlotsPerSection; slot++)
            {
                var section = (SectionId)s;
                if (engine.Parameters.GetBool(ParameterTable.Osc(section, slot, OscParam.On)) && engine.Parameters.GetInt(ParameterTable.Osc(section, slot, OscParam.Wave)) == (int)Waveform.Wavetable)
                    yield return (section, slot);
            }
        }
    }
}

/// <summary>
/// The sample map as a list: every key that holds a sample, with its name, length and rate, the
/// selected key lit and the wavetable's key marked. Click a row to select that key for the
/// sampler, right-click it to make it the wavetable or clear it.
/// </summary>
public sealed class SampleKeysWidget : Widget
{
    private const float RowHeight = 13f;

    private readonly NoumenonEngine engine;
    private readonly List<SampleSlot> rows = new();
    private int shownHash = -1;

    public SampleKeysWidget(NoumenonEngine engine, float x, float y, float width, float height)
    {
        this.engine = engine;
        Bounds = new RectangleF(x, y, width, height);
    }

    public override string? Tooltip => "Loaded keys · click: select for the sampler · right-click: wavetable, clear";

    private int Capacity => Math.Max(1, (int)((Bounds.Height - 4f) / RowHeight));

    public override void Draw(Graphics g)
    {
        EditorDraw.Display(g, Bounds, 3f);
        Collect();
        if (rows.Count == 0)
        {
            EditorDraw.Text(g, "no samples — drop a file on the display above", Bounds, EditorTheme.SmallFont, EditorTheme.TextDim, EditorTheme.Center);
            return;
        }

        var selected = engine.Parameters.GetInt(ParamId.SamplerSelect);
        var wavetable = engine.Parameters.GetInt(ParamId.WavetableKey);
        var shown = Math.Min(rows.Count, Capacity);
        for (var i = 0; i < shown; i++)
        {
            var slot = rows[i];
            var row = new RectangleF(Bounds.X + 2f, Bounds.Y + 2f + i * RowHeight, Bounds.Width - 4f, RowHeight);
            if (slot.Key == selected)
            {
                using var lit = new SolidBrush(EditorTheme.ButtonOn);
                g.FillRectangle(lit, row);
            }

            var color = slot.Key == selected ? EditorTheme.TextLight : EditorTheme.Readout;
            EditorDraw.Text(g, slot.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), new RectangleF(row.X + 2f, row.Y, 24f, row.Height), EditorTheme.SmallValueFont, color, EditorTheme.Right);
            EditorDraw.Text(g, slot.Name, new RectangleF(row.X + 32f, row.Y, row.Width - 140f, row.Height), EditorTheme.SmallFont, color, EditorTheme.Left);
            EditorDraw.Text(g, $"{slot.Data.Seconds:0.0} s · {slot.Data.SampleRate / 1000.0:0.#} k{(slot.Data.IsStereo ? " st" : "")}", new RectangleF(row.Right - 106f, row.Y, 84f, row.Height), EditorTheme.SmallValueFont, EditorTheme.TextDim, EditorTheme.Right);
            if (slot.Key == wavetable)
                EditorDraw.Text(g, "WT", new RectangleF(row.Right - 20f, row.Y, 18f, row.Height), EditorTheme.SmallFont, EditorTheme.Accent, EditorTheme.Right);
        }
    }

    public override bool Update()
    {
        Collect();
        var hash = engine.Parameters.GetInt(ParamId.SamplerSelect) * 131 + engine.Parameters.GetInt(ParamId.WavetableKey) * 7919;
        foreach (var slot in rows)
            hash = hash * 31 + slot.Key + slot.Data.GetHashCode();

        if (hash == shownHash)
            return false;

        shownHash = hash;

        return true;
    }

    public override void MouseDown(PointF p, MouseButtons button, int clicks)
    {
        Collect();
        var index = (int)((p.Y - Bounds.Y - 2f) / RowHeight);
        if (index < 0 || index >= rows.Count || index >= Capacity)
            return;

        var key = rows[index].Key;
        if (button == MouseButtons.Left)
        {
            Commit(() => engine.Parameters.Set(ParamId.SamplerSelect, key));
            Invalidate();
            return;
        }

        if (button != MouseButtons.Right)
            return;

        var menu = new ContextMenuStrip();
        menu.Items.Add($"Select key {key} for the sampler", null, (_, _) => Commit(() => engine.Parameters.Set(ParamId.SamplerSelect, key)));
        menu.Items.Add($"Use key {key} as the wavetable", null, (_, _) => Commit(() => engine.Parameters.Set(ParamId.WavetableKey, key)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add($"Clear key {key}", null, (_, _) => engine.Samples.Clear(key));
        ShowMenu(menu, p);
    }

    private void Collect()
    {
        rows.Clear();
        rows.AddRange(engine.Samples.Loaded);
    }
}
