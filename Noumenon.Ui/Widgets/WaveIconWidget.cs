using Noumenon.Engine;
using Noumenon.Engine.Parameters;

namespace Noumenon.Ui.Widgets;

/// <summary>
/// One oscillator slot's waveform icon with its on/off LED in the corner: clicking the glyph
/// opens the waveform list, the wheel cycles it, clicking the LED (or right-clicking anywhere)
/// toggles the slot. A slot that is off draws its glyph dimmed.
/// </summary>
public sealed class WaveIconWidget : Widget
{
    private const float LedZone = 9f;

    private readonly ParamBinding wave;
    private readonly ParamBinding on;
    private readonly string tag;
    private int shownWave = -1;
    private bool shownOn;

    public WaveIconWidget(NoumenonEngine engine, SectionId section, int slot, float x, float y, float width, float height)
    {
        wave = new ParamBinding(engine, ParameterTable.Osc(section, slot, OscParam.Wave));
        on = new ParamBinding(engine, ParameterTable.Osc(section, slot, OscParam.On));
        tag = $"{section}{slot + 1}";
        Bounds = new RectangleF(x, y, width, height);
    }

    public override string? Tooltip => $"Osc {tag}: {wave.ValueText}, {(on.Value >= 0.5f ? "on" : "off")}\nclick: waveform list · wheel: next · LED or right-click: on/off";

    public override void Draw(Graphics g)
    {
        EditorDraw.Display(g, Bounds, 2f);
        var isOn = on.Value >= 0.5f;
        EditorDraw.WaveGlyph(g, new RectangleF(Bounds.X + LedZone - 2f, Bounds.Y + 1f, Bounds.Width - LedZone + 1f, Bounds.Height - 2f), (int)MathF.Round(wave.Value), isOn ? EditorTheme.Trace : EditorTheme.TraceDim);
        EditorDraw.Led(g, new RectangleF(Bounds.X + 2f, Bounds.Y + 2f, 4f, 4f), isOn, EditorTheme.LedOn);
    }

    public override bool Update()
    {
        var w = (int)MathF.Round(wave.Value);
        var isOn = on.Value >= 0.5f;
        if (w == shownWave && isOn == shownOn)
            return false;

        shownWave = w;
        shownOn = isOn;

        return true;
    }

    public override void MouseDown(PointF p, MouseButtons button, int clicks)
    {
        if (button == MouseButtons.Right || (button == MouseButtons.Left && p.X < Bounds.X + LedZone))
        {
            on.Value = on.Value >= 0.5f ? 0f : 1f;
            Invalidate();
            return;
        }

        if (button != MouseButtons.Left)
            return;

        var menu = new ContextMenuStrip { ShowCheckMargin = true, ShowImageMargin = false };
        var current = (int)MathF.Round(wave.Value);
        for (var i = 0; i < ParameterTable.Waveforms.Count; i++)
        {
            var index = i;
            var item = new ToolStripMenuItem(ParameterTable.Waveforms[i]) { Checked = current == i };
            item.Click += (_, _) => Commit(() => wave.Value = index);
            menu.Items.Add(item);
        }

        ShowMenu(menu, new PointF(Bounds.X, Bounds.Bottom));
    }

    public override void MouseWheel(PointF p, int delta)
    {
        Commit(() => wave.Value = ((int)MathF.Round(wave.Value) + (delta > 0 ? ParameterTable.Waveforms.Count - 1 : 1)) % ParameterTable.Waveforms.Count);
        Invalidate();
    }
}
