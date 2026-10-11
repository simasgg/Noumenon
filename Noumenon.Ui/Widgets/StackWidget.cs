using System.Drawing.Drawing2D;
using Noumenon.Dsp.Sections;
using Noumenon.Dsp.Shared;
using Noumenon.Engine;
using Noumenon.Engine.Parameters;
using Noumenon.Ui.Analysis;

namespace Noumenon.Ui.Widgets;

/// <summary>
/// One section's stack on a log-frequency axis: the live spectrum after the section filter as a
/// dim fill (what you hear), the filter's own curve, and a mark per oscillator at the pitch the
/// engine set it to (what you set), its height from level × trim through the section's gain
/// staging so a mark's top meets the spectrum peak it causes; noise slots draw as a band up to
/// their cutoff. A mark is dragged sideways for Semitone and up or down for Level, the wheel
/// steps Semitone, a double-click resets the pitch. The spectrum renders into a bitmap at the
/// device resolution thirty times a second; marks and text draw on top at logical scale.
/// </summary>
public sealed class StackWidget : Widget
{
    private const float Inset = 3f;
    private const float Grab = 6f;
    private const float MinDamping = 0.02f;

    private readonly NoumenonEngine engine;
    private readonly SectionId section;
    private readonly ParamBinding[] on;
    private readonly ParamBinding[] wave;
    private readonly ParamBinding[] semitone;
    private readonly ParamBinding[] fine;
    private readonly ParamBinding[] level;
    private readonly ParamBinding[] trim;
    private readonly ParamBinding[] noiseCutoff;
    private readonly ParamBinding cutoff;
    private readonly ParamBinding reso;
    private readonly ParamBinding filterType;
    private readonly ParamBinding slave;
    private readonly ParamBinding masterCutoff;
    private readonly ParamBinding sectionLevel;
    private readonly ParamBinding a4;
    private readonly SpectrumAnalyzer analyzer = new();
    private readonly float[] a = new float[ScopeBuffer.Capacity];
    private readonly float[] b = new float[ScopeBuffer.Capacity];
    private readonly PointF[] curve = new PointF[160];
    private Bitmap? image;
    private PointF[] fill = [];
    private int dragSlot = -1;
    private int hoverSlot = -1;

    public StackWidget(NoumenonEngine engine, SectionId section, float x, float y, float width, float height)
    {
        this.engine = engine;
        this.section = section;
        var slots = ParameterTable.SlotsPerSection;
        on = new ParamBinding[slots];
        wave = new ParamBinding[slots];
        semitone = new ParamBinding[slots];
        fine = new ParamBinding[slots];
        level = new ParamBinding[slots];
        trim = new ParamBinding[slots];
        noiseCutoff = new ParamBinding[slots];
        for (var slot = 0; slot < slots; slot++)
        {
            on[slot] = new ParamBinding(engine, ParameterTable.Osc(section, slot, OscParam.On));
            wave[slot] = new ParamBinding(engine, ParameterTable.Osc(section, slot, OscParam.Wave));
            semitone[slot] = new ParamBinding(engine, ParameterTable.Osc(section, slot, OscParam.Semitone));
            fine[slot] = new ParamBinding(engine, ParameterTable.Osc(section, slot, OscParam.Fine));
            level[slot] = new ParamBinding(engine, ParameterTable.Osc(section, slot, OscParam.Level));
            trim[slot] = new ParamBinding(engine, ParameterTable.Osc(section, slot, OscParam.Trim));
            noiseCutoff[slot] = new ParamBinding(engine, ParameterTable.Osc(section, slot, OscParam.NoiseCutoff));
        }

        cutoff = new ParamBinding(engine, ParameterTable.Section(section, SectionParam.Cutoff));
        reso = new ParamBinding(engine, ParameterTable.Section(section, SectionParam.Reso));
        filterType = new ParamBinding(engine, ParameterTable.Section(section, SectionParam.FilterType));
        slave = new ParamBinding(engine, ParameterTable.Section(section, SectionParam.Slave));
        masterCutoff = new ParamBinding(engine, ParamId.MasterFilterCutoff);
        sectionLevel = new ParamBinding(engine, ParameterTable.Section(section, SectionParam.Level));
        a4 = new ParamBinding(engine, ParamId.TuningA4);
        Bounds = new RectangleF(x, y, width, height);
    }

    public override string? Tooltip
    {
        get
        {
            var slot = hoverSlot >= 0 ? hoverSlot : dragSlot;
            if (slot < 0)
                return $"Section {section}: spectrum after the filter, the filter curve, a mark per oscillator\ndrag a mark: pitch / level · wheel: semitone · double-click: reset pitch";

            return $"{section}{slot + 1}: {wave[slot].ValueText}, {semitone[slot].ValueText} {fine[slot].ValueText}, level {level[slot].ValueText} × {trim[slot].ValueText}, {(on[slot].Value >= 0.5f ? "on" : "off")}\ndrag: pitch / level · wheel: semitone · double-click: reset pitch";
        }
    }

    private RectangleF Plot => new(Bounds.X + Inset, Bounds.Y + Inset, Bounds.Width - 2f * Inset, Bounds.Height - 2f * Inset);

    public override void Draw(Graphics g)
    {
        var scale = Surface?.PanelScale ?? 1f;
        var w = Math.Max(8, (int)MathF.Round(Bounds.Width * scale));
        var h = Math.Max(8, (int)MathF.Round(Bounds.Height * scale));
        if (image is null || image.Width != w || image.Height != h)
        {
            image?.Dispose();
            image = new Bitmap(w, h);
        }

        RenderSpectrum(image, w, h, scale);
        var state = g.Save();
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.DrawImage(image, Bounds);
        g.Restore(state);
        using (var edge = new Pen(EditorTheme.DisplayEdge, 1f))
        {
            g.DrawRectangle(edge, Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height);
        }

        DrawMarks(g);
    }

    public override bool Update() => true;

    public override void MouseMove(PointF p, MouseButtons buttons)
    {
        if (dragSlot < 0)
        {
            hoverSlot = SlotAt(p);
            return;
        }

        var plot = Plot;
        var hz = SpectrumAnalyzer.HzAt((p.X - plot.X) / plot.Width);
        var note = Tuning.HzToNote(hz, a4.Value) - Tuning.MiddleC - fine[dragSlot].Value / 100f;
        semitone[dragSlot].Value = MathF.Round(Math.Clamp(note, semitone[dragSlot].Info.Min, semitone[dragSlot].Info.Max));
        var fraction = Math.Clamp((plot.Bottom - p.Y) / plot.Height, 0f, 1f);
        var db = SpectrumAnalyzer.FloorDb + fraction * (SpectrumAnalyzer.CeilingDb - SpectrumAnalyzer.FloorDb);
        var stage = MathF.Max(1e-3f, trim[dragSlot].Resolved * Section.SumGain * sectionLevel.Resolved);
        level[dragSlot].Value = Math.Clamp(DspHelper.DbToLinear(db) / stage, 0f, 1f);
        Invalidate();
    }

    public override void MouseDown(PointF p, MouseButtons button, int clicks)
    {
        var slot = SlotAt(p);
        if (slot < 0 || button != MouseButtons.Left)
            return;

        if (clicks >= 2)
        {
            Commit(() =>
            {
                semitone[slot].Reset();
                fine[slot].Reset();
            });
            Invalidate();
            return;
        }

        dragSlot = slot;
        hoverSlot = slot;
    }

    public override void MouseUp(PointF p, MouseButtons button)
    {
        dragSlot = -1;
        hoverSlot = SlotAt(p);
        Invalidate();
    }

    public override void MouseWheel(PointF p, int delta)
    {
        var slot = SlotAt(p);
        if (slot < 0)
            return;

        Commit(() => semitone[slot].Value += delta > 0 ? 1f : -1f);
        Invalidate();
    }

    private int SlotAt(PointF p)
    {
        var best = -1;
        var bestDistance = Grab;
        for (var slot = 0; slot < on.Length; slot++)
        {
            var distance = MathF.Abs(MarkX(slot) - p.X);
            if (distance <= bestDistance)
            {
                best = slot;
                bestDistance = distance;
            }
        }

        return best;
    }

    private float SlotHz(int slot)
    {
        var live = engine.OscillatorHz(section, slot);

        return live > 0f ? live : Tuning.NoteToHz(Tuning.MiddleC + semitone[slot].Resolved + fine[slot].Resolved / 100f, a4.Value);
    }

    private float MarkX(int slot) => Plot.X + Plot.Width * SpectrumAnalyzer.Position(SlotHz(slot));

    /// <summary>The mark's height on the dB axis: level × trim through the sum gain and the section level, so it meets the spectrum it causes.</summary>
    private float MarkLevel(int slot)
    {
        var linear = level[slot].Resolved * trim[slot].Resolved * Section.SumGain * sectionLevel.Resolved;

        return SpectrumAnalyzer.Level(DspHelper.LinearToDb(linear));
    }

    private void RenderSpectrum(Bitmap target, int w, int h, float scale)
    {
        using var g = Graphics.FromImage(target);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(EditorTheme.Display);

        var inset = Inset * scale;
        var plotX = inset;
        var plotW = w - 2f * inset;
        var plotY = inset;
        var plotH = h - 2f * inset;

        using (var grid = new Pen(EditorTheme.DisplayGrid, 1f))
        {
            for (var hz = 62.5f; hz < SpectrumAnalyzer.MaxHz; hz *= 2f)
            {
                var x = plotX + plotW * SpectrumAnalyzer.Position(hz);
                g.DrawLine(grid, x, plotY, x, plotY + plotH);
            }

            var zero = plotY + plotH * (1f - SpectrumAnalyzer.Level(0f));
            g.DrawLine(grid, plotX, zero, plotX + plotW, zero);
        }

        // The spectrum of this section's channel of the ring.
        engine.SectionScope.CopyLatest(a, b, SpectrumAnalyzer.Size);
        analyzer.Compute(section == SectionId.A ? a : b, SpectrumAnalyzer.Size);
        var columns = Math.Max(2, (int)plotW);
        if (fill.Length != columns + 2)
            fill = new PointF[columns + 2];

        var binHz = (float)(engine.SampleRate / SpectrumAnalyzer.Size);
        var db = analyzer.Db;
        for (var c = 0; c < columns; c++)
        {
            var hz0 = SpectrumAnalyzer.HzAt(c / (float)columns);
            var hz1 = SpectrumAnalyzer.HzAt((c + 1) / (float)columns);
            var bin0 = Math.Clamp((int)(hz0 / binHz), 1, db.Length - 1);
            var bin1 = Math.Clamp((int)(hz1 / binHz), bin0, db.Length - 1);
            var peak = db[bin0];
            for (var k = bin0 + 1; k <= bin1; k++)
            {
                if (db[k] > peak)
                    peak = db[k];
            }

            fill[c + 1] = new PointF(plotX + c, plotY + plotH * (1f - SpectrumAnalyzer.Level(peak)));
        }

        fill[0] = new PointF(plotX, plotY + plotH);
        fill[columns + 1] = new PointF(plotX + columns - 1, plotY + plotH);
        using (var brush = new SolidBrush(Color.FromArgb(70, EditorTheme.Accent)))
        using (var outline = new Pen(Color.FromArgb(150, EditorTheme.Accent), 1f))
        {
            g.FillPolygon(brush, fill);
            g.DrawLines(outline, fill.AsSpan(1, columns).ToArray());
        }

        // The filter curve: a 2-pole state-variable response with damping k from 2 (−6 dB) to 0.02 (+34 dB).
        var fc = slave.Resolved >= 0.5f ? masterCutoff.Resolved : cutoff.Resolved;
        var damping = 2f - (2f - MinDamping) * Math.Clamp(reso.Resolved, 0f, 1f);
        var type = (int)MathF.Round(filterType.Resolved);
        for (var i = 0; i < curve.Length; i++)
        {
            var position = i / (float)(curve.Length - 1);
            var wn = SpectrumAnalyzer.HzAt(position) / MathF.Max(1f, fc);
            var denominator = MathF.Sqrt((1f - wn * wn) * (1f - wn * wn) + damping * damping * wn * wn);
            var magnitude = (type == 1 ? wn : type == 2 ? wn * wn : 1f) / MathF.Max(1e-6f, denominator);
            curve[i] = new PointF(plotX + plotW * position, plotY + plotH * (1f - SpectrumAnalyzer.Level(DspHelper.LinearToDb(magnitude))));
        }

        using (var pen = new Pen(Color.FromArgb(170, EditorTheme.TextDim), 1f))
        {
            g.DrawLines(pen, curve);
        }
    }

    private void DrawMarks(Graphics g)
    {
        var plot = Plot;
        for (var slot = 0; slot < on.Length; slot++)
        {
            var isOn = on[slot].Value >= 0.5f;
            var x = MarkX(slot);
            var top = plot.Bottom - plot.Height * MarkLevel(slot);
            var color = isOn ? EditorTheme.Accent : EditorTheme.TextDim;
            var active = slot == dragSlot || slot == hoverSlot;
            var waveform = (int)MathF.Round(wave[slot].Value);
            if (waveform >= 6 && isOn)
            {
                var from = waveform == 6 ? plot.X : plot.X + plot.Width * SpectrumAnalyzer.Position(noiseCutoff[slot].Resolved / 8f);
                var to = plot.X + plot.Width * SpectrumAnalyzer.Position(noiseCutoff[slot].Resolved);
                using var band = new SolidBrush(Color.FromArgb(45, EditorTheme.Accent));
                g.FillRectangle(band, from, top, Math.Max(2f, to - from), plot.Bottom - top);
            }

            using (var pen = new Pen(active ? EditorTheme.TextLight : color, active ? 2f : 1.5f))
            {
                g.DrawLine(pen, x, plot.Bottom, x, top);
            }

            using (var head = new SolidBrush(active ? EditorTheme.TextLight : color))
            {
                g.FillEllipse(head, x - 2.5f, top - 2.5f, 5f, 5f);
            }

            EditorDraw.Text(g, (slot + 1).ToString(), new RectangleF(x - 6f, plot.Bottom - 10f, 12f, 9f), EditorTheme.SmallFont, isOn ? EditorTheme.Text : EditorTheme.TextDim, EditorTheme.Center);
        }
    }
}
