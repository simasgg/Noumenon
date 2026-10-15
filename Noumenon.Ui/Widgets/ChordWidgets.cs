using Noumenon.Engine;
using Noumenon.Engine.Parameters;
using Noumenon.Engine.Scales;

namespace Noumenon.Ui.Widgets;

/// <summary>
/// One step of the chord sequencer as a card: the degree and its chord's root, the six notes the
/// scale-aware generator builds for it (with the sequencer's type, voicing and octave, in the
/// active scale and key), lit while the step plays with a bar for how far it has run, dimmed when
/// it lies beyond the step count. A Hold step shows the chord it keeps ringing. Clicking the card
/// opens the degree list, the wheel steps it — the same as the menu above it.
/// </summary>
public sealed class ChordCellWidget : Widget
{
    private readonly NoumenonEngine engine;
    private readonly int step;
    private readonly ParamBinding degree;
    private readonly float[] notes = new float[ChordGenerator.VoiceCount];
    private int shownHash = -1;

    public ChordCellWidget(NoumenonEngine engine, int step, float x, float y, float width, float height)
    {
        this.engine = engine;
        this.step = step;
        degree = new ParamBinding(engine, ParameterTable.ChordSeqStep(step));
        Bounds = new RectangleF(x, y, width, height);
    }

    public override string? Tooltip => $"Step {step + 1}: {degree.ValueText}\nclick: degree list · wheel: step";

    private bool Active => step < engine.Parameters.GetInt(ParamId.ChordSeqSteps);

    private bool Playing => engine.Parameters.GetBool(ParamId.ChordSeqOn) && engine.Sequencer.Step == step;

    public override void Draw(Graphics g)
    {
        var playing = Playing;
        var active = Active;
        EditorDraw.Frame(g, Bounds, 3f, playing ? EditorTheme.ButtonOn : EditorTheme.Display, playing ? EditorTheme.Accent : EditorTheme.DisplayEdge, 1f);
        var text = active ? EditorTheme.Readout : EditorTheme.TextDim;
        var inner = RectangleF.Inflate(Bounds, -5f, -4f);

        var index = ResolveDegree(out var held);
        var scale = engine.ActiveScale;
        var root = engine.Parameters.GetInt(ParamId.ScaleRoot);
        var octave = engine.Parameters.GetInt(ParamId.ChordSeqOctave);
        ChordGenerator.Generate(scale, root, index, (ChordType)engine.Parameters.GetInt(ParamId.ChordSeqType), (ChordVoicing)engine.Parameters.GetInt(ParamId.ChordSeqVoicing), octave, notes);
        var rootNote = ChordGenerator.RootNote(scale, root, index, octave);
        var title = $"{ParameterTable.ChordDegrees[index + 1]} · {Name(rootNote)}{(held ? " (held)" : "")}";
        EditorDraw.Text(g, title, new RectangleF(inner.X, inner.Y, inner.Width, 13f), EditorTheme.LabelFont, playing ? EditorTheme.TextLight : text, EditorTheme.Left);

        for (var v = 0; v < notes.Length; v++)
        {
            var row = new RectangleF(inner.X + (v % 2) * inner.Width / 2f, inner.Y + 17f + (v / 2) * 12f, inner.Width / 2f, 12f);
            EditorDraw.Text(g, Name(notes[v]), row, EditorTheme.SmallValueFont, text, EditorTheme.Left);
        }

        if (!playing)
            return;

        var bar = new RectangleF(inner.X, inner.Bottom - 4f, inner.Width, 3f);
        using var dim = new SolidBrush(EditorTheme.AccentDark);
        using var bright = new SolidBrush(EditorTheme.Accent);
        g.FillRectangle(dim, bar);
        g.FillRectangle(bright, bar.X, bar.Y, bar.Width * engine.Sequencer.Progress, bar.Height);
    }

    public override bool Update()
    {
        var bank = engine.Parameters;
        var hash = (Playing ? 1 : 0) + (Active ? 2 : 0) + ResolveDegree(out _) * 4 + bank.GetInt(ParamId.ChordSeqType) * 64 + bank.GetInt(ParamId.ChordSeqVoicing) * 1024
            + bank.GetInt(ParamId.ChordSeqOctave) * 4096 + bank.GetInt(ParamId.Scale) * 32768 + bank.GetInt(ParamId.ScaleRoot) * 524288
            + (Playing ? (int)(engine.Sequencer.Progress * 60f) << 24 : 0);
        if (hash == shownHash)
            return false;

        shownHash = hash;

        return true;
    }

    public override void MouseDown(PointF p, MouseButtons button, int clicks)
    {
        if (button != MouseButtons.Left)
            return;

        var menu = new ContextMenuStrip { ShowCheckMargin = true, ShowImageMargin = false };
        var current = (int)MathF.Round(degree.Value);
        for (var i = 0; i < ParameterTable.ChordDegrees.Count; i++)
        {
            var index = i;
            var item = new ToolStripMenuItem(ParameterTable.ChordDegrees[i]) { Checked = current == i };
            item.Click += (_, _) => Commit(() => degree.Value = index);
            menu.Items.Add(item);
        }

        ShowMenu(menu, new PointF(Bounds.X, Bounds.Bottom));
    }

    public override void MouseWheel(PointF p, int delta)
    {
        Commit(() => degree.Step(delta > 0 ? 1 : -1));
        Invalidate();
    }

    /// <summary>The degree this card sounds (0 = I): its own, or for Hold the nearest earlier step's, wrapping round the active steps.</summary>
    private int ResolveDegree(out bool held)
    {
        var bank = engine.Parameters;
        var steps = Math.Max(1, bank.GetInt(ParamId.ChordSeqSteps));
        held = false;
        for (var back = 0; back < ParameterTable.ChordSeqStepCount; back++)
        {
            var index = ((step - back) % steps + steps) % steps;
            if (step >= steps)
                index = step - back;

            if (index < 0)
                break;

            var value = bank.GetInt(ParameterTable.ChordSeqStep(index));
            if (value > 0)
                return value - 1;

            held = true;
        }

        return 0;
    }

    private static string Name(float note)
    {
        var nearest = Math.Clamp((int)MathF.Round(note), 0, 127);
        var cents = (int)MathF.Round((note - nearest) * 100f);

        return cents == 0 ? ParamInfo.NoteName(nearest) : $"{ParamInfo.NoteName(nearest)}{cents:+0;-0}";
    }
}
