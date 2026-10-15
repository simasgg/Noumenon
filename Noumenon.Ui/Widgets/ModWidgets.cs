using Noumenon.Engine;
using Noumenon.Engine.Parameters;

namespace Noumenon.Ui.Widgets;

/// <summary>
/// A modulation source drawn as it moves: the last two seconds of its value as a trace scrolling
/// right to left, sampled at the panel's frame rate, with a centre line for the bipolar sources
/// (the LFOs, P1 and P2). It reads the value the matrix used on its last tick, so what is drawn is
/// what the targets got.
/// </summary>
public sealed class SourceTraceWidget : Widget
{
    private const int History = 60;

    private readonly Func<float> value;
    private readonly bool bipolar;
    private readonly string? tooltip;
    private readonly float[] history = new float[History];
    private readonly PointF[] points = new PointF[History];
    private int write;
    private int still;
    private bool primed;
    private float last = float.NaN;

    public SourceTraceWidget(Func<float> value, bool bipolar, float x, float y, float width, float height, string? tooltip = null)
    {
        this.value = value;
        this.bipolar = bipolar;
        this.tooltip = tooltip;
        Bounds = new RectangleF(x, y, width, height);
    }

    public override string? Tooltip => tooltip;

    public override void Draw(Graphics g)
    {
        EditorDraw.Display(g, Bounds, 2f);
        var inner = RectangleF.Inflate(Bounds, -2f, -3f);
        if (bipolar)
        {
            using var grid = new Pen(EditorTheme.DisplayGrid, 1f);
            g.DrawLine(grid, inner.X, inner.Y + inner.Height / 2f, inner.Right, inner.Y + inner.Height / 2f);
        }

        for (var i = 0; i < History; i++)
        {
            var v = history[(write + i) % History];
            var level = bipolar ? 0.5f + 0.5f * Math.Clamp(v, -1f, 1f) : Math.Clamp(v, 0f, 1f);
            points[i] = new PointF(inner.X + inner.Width * i / (History - 1), inner.Bottom - inner.Height * level);
        }

        using var pen = new Pen(EditorTheme.Trace, 1.3f);
        g.DrawLines(pen, points);
    }

    public override bool Update()
    {
        var v = value();
        if (!primed)
        {
            Array.Fill(history, v);
            primed = true;
        }

        history[write] = v;
        write = (write + 1) % History;

        // The trace keeps scrolling until a steady value has filled it.
        if (v != last)
        {
            last = v;
            still = 0;
        }
        else
        {
            still++;
        }

        return still <= History;
    }
}

/// <summary>
/// What one matrix slot is adding right now: depth × source as a bar growing from the centre,
/// in the target's normalized range (a full bar to either side is the whole range). Empty while
/// the slot has no source or no target.
/// </summary>
public sealed class ModSlotMeterWidget : Widget
{
    private readonly NoumenonEngine engine;
    private readonly int slot;
    private float shown = float.NaN;

    public ModSlotMeterWidget(NoumenonEngine engine, int slot, float x, float y, float width, float height)
    {
        this.engine = engine;
        this.slot = slot;
        Bounds = new RectangleF(x, y, width, height);
    }

    public override string? Tooltip => $"Slot {slot + 1} adds {Amount * 100f:+0;-0;0} % of its target's range now";

    private float Amount
    {
        get
        {
            var bank = engine.Resolved;
            var source = bank.GetInt(ParameterTable.ModSlot(slot, ModSlotParam.Source));
            var target = bank.GetInt(ParameterTable.ModSlot(slot, ModSlotParam.Target));
            if (source <= 0 || target <= 0)
                return 0f;

            return bank.Get(ParameterTable.ModSlot(slot, ModSlotParam.Depth)) * engine.ModulationSource(source);
        }
    }

    public override void Draw(Graphics g)
    {
        EditorDraw.Frame(g, Bounds, 2f, EditorTheme.Slot, EditorTheme.ButtonEdge, 1f);
        var inner = RectangleF.Inflate(Bounds, -1.5f, -1.5f);
        var centre = inner.X + inner.Width / 2f;
        using (var grid = new Pen(EditorTheme.FrameLight, 1f))
        {
            g.DrawLine(grid, centre, inner.Y, centre, inner.Bottom);
        }

        var amount = Math.Clamp(shown, -1f, 1f);
        if (float.IsNaN(amount) || amount == 0f)
            return;

        using var brush = new SolidBrush(EditorTheme.Accent);
        var width = inner.Width / 2f * MathF.Abs(amount);
        g.FillRectangle(brush, amount > 0f ? centre : centre - width, inner.Y, width, inner.Height);
    }

    public override bool Update()
    {
        var amount = Amount;
        if (MathF.Abs(amount - shown) < 0.004f)
            return false;

        shown = amount;

        return true;
    }
}

/// <summary>
/// The learned MIDI controllers, one row each — "CC 21 → Drift Time 1" — with the parameter
/// waiting to learn on top in amber. Right-click a row to forget it; the button beside the list
/// clears them all.
/// </summary>
public sealed class MidiMapListWidget : Widget
{
    private const float RowHeight = 13f;

    private readonly NoumenonEngine engine;
    private readonly List<(int Controller, ParamId Parameter)> rows = new();
    private int shownVersion = -1;
    private ParamId? shownLearning;

    public MidiMapListWidget(NoumenonEngine engine, float x, float y, float width, float height)
    {
        this.engine = engine;
        Bounds = new RectangleF(x, y, width, height);
    }

    public override string? Tooltip => "Learned controllers · right-click a row to forget it\nlearn one from any control's right-click menu";

    private int Capacity => Math.Max(1, (int)((Bounds.Height - 4f) / RowHeight));

    public override void Draw(Graphics g)
    {
        EditorDraw.Display(g, Bounds, 3f);
        var y = Bounds.Y + 2f;
        if (engine.MidiMap.Learning is { } learning)
        {
            EditorDraw.Text(g, $"learning: move a controller for {ParameterTable.Get(learning).Name}", new RectangleF(Bounds.X + 4f, y, Bounds.Width - 8f, RowHeight), EditorTheme.SmallFont, EditorTheme.Armed, EditorTheme.Left);
            y += RowHeight;
        }

        if (rows.Count == 0)
        {
            EditorDraw.Text(g, "no controllers learned yet", new RectangleF(Bounds.X, y, Bounds.Width, Bounds.Bottom - y), EditorTheme.SmallFont, EditorTheme.TextDim, EditorTheme.Center);
            return;
        }

        var shown = Math.Min(rows.Count, Capacity - (engine.MidiMap.Learning is null ? 0 : 1));
        for (var i = 0; i < shown; i++)
        {
            var (controller, parameter) = rows[i];
            var row = new RectangleF(Bounds.X + 4f, y + i * RowHeight, Bounds.Width - 8f, RowHeight);
            EditorDraw.Text(g, $"CC {controller}", new RectangleF(row.X, row.Y, 48f, row.Height), EditorTheme.SmallValueFont, EditorTheme.Readout, EditorTheme.Left);
            EditorDraw.Text(g, "→ " + ParameterTable.Get(parameter).Name, new RectangleF(row.X + 50f, row.Y, row.Width - 50f, row.Height), EditorTheme.SmallFont, EditorTheme.Text, EditorTheme.Left);
        }

        if (rows.Count > shown)
            EditorDraw.Text(g, $"+{rows.Count - shown} more", new RectangleF(Bounds.X, Bounds.Bottom - RowHeight - 2f, Bounds.Width - 6f, RowHeight), EditorTheme.SmallFont, EditorTheme.TextDim, EditorTheme.Right);
    }

    public override bool Update()
    {
        var map = engine.MidiMap;
        var version = map.Version;
        var learning = map.Learning;
        if (version == shownVersion && learning == shownLearning)
            return false;

        shownVersion = version;
        shownLearning = learning;
        rows.Clear();
        foreach (var mapping in map.Capture())
        {
            if (Enum.TryParse<ParamId>(mapping.Parameter, out var id))
                rows.Add((mapping.Controller, id));
        }

        return true;
    }

    public override void MouseDown(PointF p, MouseButtons button, int clicks)
    {
        var learningRow = engine.MidiMap.Learning is null ? 0 : 1;
        var index = (int)((p.Y - Bounds.Y - 2f) / RowHeight) - learningRow;
        if (button == MouseButtons.Left && index < 0 && learningRow == 1)
        {
            engine.MidiMap.CancelLearn();
            Invalidate();
            return;
        }

        if (button != MouseButtons.Right || index < 0 || index >= rows.Count)
            return;

        var (controller, parameter) = rows[index];
        var menu = new ContextMenuStrip();
        menu.Items.Add($"Forget CC {controller} → {ParameterTable.Get(parameter).Name}", null, (_, _) => engine.MidiMap.Forget(parameter));
        ShowMenu(menu, p);
    }
}
