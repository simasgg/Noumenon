using System.Globalization;
using Noumenon.Engine;
using Noumenon.Engine.Parameters;
using Noumenon.Ui.Widgets;

namespace Noumenon.Ui.Pages;

/// <summary>
/// The MOD tab: where modulation comes from and where it goes. Across the top the sources — the
/// three LFOs and the envelope follower with a live trace each (the follower also chooses what it
/// listens to: the sampler, the live input, the sidechain, the sections or the output), and the
/// four macros. Beneath them the twelve-slot matrix, each row with a bar showing what the slot is
/// adding right now. On the right MIDI — the controller the matrix's CC source follows and every
/// controller learned from a control's right-click menu — then host sync and engine rate, and the
/// keys. Any control's right-click also routes a source straight into a free slot.
/// </summary>
internal static class ModPage
{
    private const float Top = 28f;
    private const float SourcesHeight = 122f;
    private const float MatrixTop = 156f;
    private const float RowStep = 38f;

    public static PageContent Build(NoumenonEngine engine)
    {
        var inv = CultureInfo.InvariantCulture;
        var sources = ParameterTable.MatrixSources.ToList();
        var lfoSource = sources.IndexOf("LFO 1");
        var followerSource = sources.IndexOf("Env Follower");
        var w = new List<Widget>();

        for (var i = 0; i < ParameterTable.LfoCount; i++)
        {
            var x = 4 + i * 166f;
            var source = lfoSource + i;
            w.Add(new FrameWidget(x, Top, 162, SourcesHeight, $"LFO {i + 1}"));
            w.Add(new KnobWidget(engine, ParameterTable.Lfo(i, LfoParam.Rate), x + 12, Top + 30, 26, "RATE"));
            w.Add(new KnobWidget(engine, ParameterTable.Lfo(i, LfoParam.Depth), x + 56, Top + 30, 26, "DEPTH"));
            w.Add(new MenuWidget(engine, ParameterTable.Lfo(i, LfoParam.Shape), x + 8, Top + 76, 88));
            w.Add(new SourceTraceWidget(() => engine.ModulationSource(source), true, x + 102, Top + 18, 54, 96, $"LFO {i + 1} as the matrix hears it (times its depth)"));
        }

        var fx = 502f;
        w.Add(new FrameWidget(fx, Top, 230, SourcesHeight, "ENV FOLLOWER"));
        w.Add(new KnobWidget(engine, ParamId.EnvFollowerAttack, fx + 12, Top + 30, 26, "ATTACK"));
        w.Add(new KnobWidget(engine, ParamId.EnvFollowerRelease, fx + 56, Top + 30, 26, "RELEASE"));
        w.Add(new KnobWidget(engine, ParamId.EnvFollowerGain, fx + 100, Top + 30, 26, "GAIN"));
        w.Add(new LabelWidget("HEARS", fx + 8, Top + 76, 34, 14, EditorTheme.Left, EditorTheme.SmallFont, EditorTheme.TextDim));
        w.Add(new MenuWidget(engine, ParamId.EnvFollowerSource, fx + 42, Top + 76, 92));
        w.Add(new LabelWidget("drives the AM route and the matrix", fx + 8, Top + 96, 136, 12, EditorTheme.Left, EditorTheme.SmallFont, EditorTheme.TextDim));
        w.Add(new SourceTraceWidget(() => engine.ModulationSource(followerSource), false, fx + 148, Top + 18, 76, 96, "The envelope follower: the AM route's control and a matrix source"));

        var mx = 736f;
        w.Add(new FrameWidget(mx, Top, 260, SourcesHeight, "MACROS"));
        for (var i = 0; i < 4; i++)
            w.Add(new KnobWidget(engine, ParamId.Macro1 + i, mx + 18 + i * 62, Top + 36, 32, $"M{i + 1}"));

        w.Add(new LabelWidget("four free knobs for the matrix, the wheel or a host", mx + 8, Top + 96, 244, 12, EditorTheme.Left, EditorTheme.SmallFont, EditorTheme.TextDim));

        // The matrix.
        w.Add(new FrameWidget(4, MatrixTop, 620, 504, "MODULATION MATRIX"));
        w.Add(new LabelWidget("SOURCE", 30, MatrixTop + 18, 110, 11, EditorTheme.Left, EditorTheme.SmallFont));
        w.Add(new LabelWidget("TARGET", 146, MatrixTop + 18, 240, 11, EditorTheme.Left, EditorTheme.SmallFont));
        w.Add(new LabelWidget("DEPTH", 396, MatrixTop + 18, 74, 11, EditorTheme.Left, EditorTheme.SmallFont));
        w.Add(new LabelWidget("ADDING NOW", 478, MatrixTop + 18, 138, 11, EditorTheme.Center, EditorTheme.SmallFont));
        for (var slot = 0; slot < ParameterTable.ModSlotCount; slot++)
        {
            var y = MatrixTop + 38 + slot * RowStep;
            w.Add(new LabelWidget((slot + 1).ToString(inv), 8, y, 18, 14, EditorTheme.Center, EditorTheme.SmallFont));
            w.Add(new MenuWidget(engine, ParameterTable.ModSlot(slot, ModSlotParam.Source), 30, y, 110));
            w.Add(new MenuWidget(engine, ParameterTable.ModSlot(slot, ModSlotParam.Target), 146, y, 240));
            w.Add(new KnobWidget(engine, ParameterTable.ModSlot(slot, ModSlotParam.Depth), 396, y - 4, 22, showValue: false));   // the number box beside it is the readout
            w.Add(new NumberWidget(engine, ParameterTable.ModSlot(slot, ModSlotParam.Depth), 424, y + 1, 46));
            w.Add(new ModSlotMeterWidget(engine, slot, 478, y + 3, 138, 9));
        }

        // MIDI.
        var rx = 630f;
        w.Add(new FrameWidget(rx, MatrixTop, 366, 300, "MIDI"));
        w.Add(new NumberWidget(engine, ParamId.MatrixCc, rx + 80, MatrixTop + 20, 36, 12, "MATRIX CC", 72));
        w.Add(new DisplayTextWidget(() => $"now {engine.Midi.Controller(engine.Parameters.GetInt(ParamId.MatrixCc)).ToString("0.00", inv)}", rx + 122, MatrixTop + 20, 70, 12, font: EditorTheme.SmallValueFont, color: EditorTheme.TextOnDark, tooltip: "The value the matrix's CC source follows"));
        w.Add(new LabelWidget("LEARNED CONTROLLERS", rx + 8, MatrixTop + 40, 200, 12, EditorTheme.Left, EditorTheme.SmallFont, EditorTheme.TextDim));
        w.Add(new TextButtonWidget("CLEAR ALL", rx + 290, MatrixTop + 38, 68, 14, () => engine.MidiMap.Clear(), tooltip: "Forget every learned controller"));
        w.Add(new MidiMapListWidget(engine, rx + 6, MatrixTop + 56, 354, 206));
        w.Add(new LabelWidget("Learn: right-click any control, then move a knob on your controller.", rx + 8, MatrixTop + 268, 354, 12, EditorTheme.Left, EditorTheme.SmallFont, EditorTheme.TextDim));
        w.Add(new LabelWidget("The map stays with the instrument; presets leave it alone.", rx + 8, MatrixTop + 282, 354, 12, EditorTheme.Left, EditorTheme.SmallFont, EditorTheme.TextDim));

        w.Add(new FrameWidget(rx, MatrixTop + 306, 366, 86, "HOST  ·  ENGINE"));
        w.Add(new ToggleWidget(engine, ParamId.HostSync, rx + 10, MatrixTop + 326, 70, 13, "HOST SYNC"));
        w.Add(new ToggleWidget(engine, ParamId.RestartOnHostPlay, rx + 86, MatrixTop + 326, 100, 13, "RESTART ON PLAY"));
        w.Add(new LabelWidget("ENGINE RATE", rx + 10, MatrixTop + 350, 70, 13, EditorTheme.Left));
        w.Add(new ChoiceButtonsWidget(engine, ParamId.EngineRate, rx + 86, MatrixTop + 350, 70, 13));
        w.Add(new LabelWidget("(restarts the stream)", rx + 162, MatrixTop + 350, 120, 13, EditorTheme.Left, EditorTheme.SmallFont));
        w.Add(new LabelWidget("Host sync snaps lane loops to bars and sequencer steps to beats.", rx + 10, MatrixTop + 370, 350, 12, EditorTheme.Left, EditorTheme.SmallFont, EditorTheme.TextDim));

        w.Add(new FrameWidget(rx, MatrixTop + 398, 366, 106, "KEYS", inset: true));
        w.Add(new LabelWidget("Space  restart lanes    R  randomize    M  mute", rx + 10, MatrixTop + 416, 350, 12, EditorTheme.Left, EditorTheme.SmallFont));
        w.Add(new LabelWidget("Ctrl+Z / Ctrl+Y  undo / redo    Ctrl+Tab  next page", rx + 10, MatrixTop + 430, 350, 12, EditorTheme.Left, EditorTheme.SmallFont));
        w.Add(new LabelWidget("Bar: drag · wheel · double-click = default", rx + 10, MatrixTop + 448, 350, 12, EditorTheme.Left, EditorTheme.SmallFont));
        w.Add(new LabelWidget("Right-click a control: its lane, MIDI learn, Modulate with…", rx + 10, MatrixTop + 462, 350, 12, EditorTheme.Left, EditorTheme.SmallFont));
        w.Add(new LabelWidget("Lane: click the small display to arm, drag to record, release.", rx + 10, MatrixTop + 476, 350, 12, EditorTheme.Left, EditorTheme.SmallFont));

        return new PageContent(w, null);
    }
}
