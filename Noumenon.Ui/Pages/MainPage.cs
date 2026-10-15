using System.Globalization;
using Noumenon.Dsp.Oscillators;
using Noumenon.Engine;
using Noumenon.Engine.Lanes;
using Noumenon.Engine.Parameters;
using Noumenon.Ui.Widgets;

namespace Noumenon.Ui.Pages;

/// <summary>
/// The main panel, laid out as the signal flows: the two oscillator sections as strips (a stack
/// display over six slot cards each), the pitch faders and the sampler beneath them, the main
/// display with the combine controls in the column to their right, the effect chain as a row of
/// modules left to right into the output module, and along the bottom the lanes, the transport
/// (with the snapshot morph: a preset, a time, go), the mod-wheel box and the wordmark. The
/// sampler strip is the short form of the SAMPLE page. Coordinates are logical pixels on a 1000 × 664 canvas
/// whose top 24 are the editor's own bar.
/// </summary>
internal static class MainPage
{
    private const float Top = 28f;
    private const float SourceWidth = 762f;
    private const float SectionHeight = 112f;
    private const float PitchTop = 260f;
    private const float SampleTop = 298f;
    private const float ColumnX = 770f;
    private const float ColumnWidth = 226f;
    private const float ChainTop = 368f;
    private const float ChainHeight = 112f;
    private const float BottomTop = 486f;
    private const float BottomHeight = 174f;
    private const float CardWidth = 110f;
    private const float CardStep = 111f;
    private const float CardsX = 90f;

    // The morph time is the panel's own setting, kept for the session like the panel scale.
    private static float morphSeconds = 8f;

    public static PageContent Build(NoumenonEngine engine, IEditorHost host)
    {
        var w = new List<Widget>();
        BuildSection(engine, w, SectionId.A, Top);
        BuildSection(engine, w, SectionId.B, Top + SectionHeight + 4f);
        BuildPitch(engine, w);
        BuildSample(engine, host, w);
        BuildColumn(engine, w);
        BuildChain(engine, w);
        BuildOut(engine, w);
        BuildLanes(engine, w);
        BuildTransport(engine, host, w);
        BuildWheel(engine, w);
        BuildWordmark(engine, w);

        return new PageContent(w, null);
    }

    // ---- Sources ----

    private static void BuildSection(NoumenonEngine engine, List<Widget> w, SectionId section, float top)
    {
        w.Add(new FrameWidget(4, top, SourceWidth, SectionHeight));

        // The head: the letter, the filter, the level.
        var x0 = 10f;
        w.Add(new LabelWidget(section.ToString(), x0, top + 4, 24, 26, EditorTheme.Center, EditorTheme.HugeFont));
        w.Add(new ToggleWidget(engine, ParameterTable.Section(section, SectionParam.Slave), x0 + 28, top + 9, 44, 13, "SLAVE"));
        w.Add(new KnobWidget(engine, ParameterTable.Section(section, SectionParam.Cutoff), x0 + 2, top + 40, 22, "CUT"));
        w.Add(new KnobWidget(engine, ParameterTable.Section(section, SectionParam.Reso), x0 + 30, top + 40, 22, "RES"));
        w.Add(new ChoiceButtonsWidget(engine, ParameterTable.Section(section, SectionParam.FilterType), x0, top + 66, 72, 12));
        w.Add(new LabelWidget("LEVEL", x0, top + 82, 72, 9, EditorTheme.Left, EditorTheme.SmallFont, EditorTheme.TextDim));
        w.Add(new FaderWidget(engine, ParameterTable.Section(section, SectionParam.Level), x0, top + 97, 56, vertical: false));

        // The stack display and the pitch-routing block.
        w.Add(new StackWidget(engine, section, CardsX, top + 2, 608, 42));
        var routeX = 704f;
        w.Add(new LabelWidget("P1", routeX, top + 4, 14, 11, EditorTheme.Left, EditorTheme.SmallFont, EditorTheme.TextDim));
        w.Add(new ToggleWidget(engine, ParameterTable.Section(section, SectionParam.P1FineRange), routeX + 14, top + 4, 22, 11, "FINE"));
        w.Add(new ToggleWidget(engine, ParameterTable.Section(section, SectionParam.P1RouteAll), routeX + 38, top + 4, 22, 11, "ALL"));
        w.Add(new LabelWidget("P2", routeX, top + 18, 14, 11, EditorTheme.Left, EditorTheme.SmallFont, EditorTheme.TextDim));
        w.Add(new ToggleWidget(engine, ParameterTable.Section(section, SectionParam.P2FineRange), routeX + 14, top + 18, 22, 11, "FINE"));
        w.Add(new ToggleWidget(engine, ParameterTable.Section(section, SectionParam.P2RouteAll), routeX + 38, top + 18, 22, 11, "ALL"));
        w.Add(new LabelWidget("ROUTE", routeX, top + 32, 60, 10, EditorTheme.Center, EditorTheme.SmallFont, EditorTheme.TextDim));

        // Six slot cards.
        for (var slot = 0; slot < ParameterTable.SlotsPerSection; slot++)
            BuildCard(engine, w, section, slot, CardsX + slot * CardStep, top + 48);
    }

    private static void BuildCard(NoumenonEngine engine, List<Widget> w, SectionId section, int slot, float x, float y)
    {
        var index = slot;
        w.Add(new FrameWidget(x, y, CardWidth, 60, null, inset: true));
        w.Add(new WaveIconWidget(engine, section, slot, x + 3, y + 3, 34, 14));
        w.Add(new NumberWidget(engine, ParameterTable.Osc(section, slot, OscParam.Semitone), x + 40, y + 4, 32));
        w.Add(new NumberWidget(engine, ParameterTable.Osc(section, slot, OscParam.Fine), x + 74, y + 4, 32));
        w.Add(new FaderWidget(engine, ParameterTable.Osc(section, slot, OscParam.Level), x + 4, y + 27, 40, vertical: false));
        w.Add(new KnobWidget(engine, () => IsNoise(engine, section, index) ? ParameterTable.Osc(section, index, OscParam.NoiseCutoff) : ParameterTable.Osc(section, index, OscParam.Shape), x + 62, y + 21, 18));
        w.Add(new NumberWidget(engine, ParameterTable.Osc(section, slot, OscParam.Trim), x + 82, y + 24, 26));
        w.Add(new ToggleWidget(engine, ParameterTable.Osc(section, slot, OscParam.P1Route), x + 4, y + 43, 24, 12, "P1"));
        w.Add(new ToggleWidget(engine, ParameterTable.Osc(section, slot, OscParam.P2Route), x + 30, y + 43, 24, 12, "P2"));
        w.Add(new LabelWidget($"{section}{slot + 1}", x + 58, y + 43, 48, 12, EditorTheme.Right, EditorTheme.SmallFont, EditorTheme.TextDim));
    }

    private static bool IsNoise(NoumenonEngine engine, SectionId section, int slot) => engine.Parameters.GetInt(ParameterTable.Osc(section, slot, OscParam.Wave)) is (int)Waveform.NoiseLp or (int)Waveform.NoiseSh;

    private static void BuildPitch(NoumenonEngine engine, List<Widget> w)
    {
        var inv = CultureInfo.InvariantCulture;
        w.Add(new FrameWidget(4, PitchTop, SourceWidth, 34));
        for (var p = 0; p < ParameterTable.PitchFaderCount; p++)
        {
            var x0 = 10f + p * 380f;
            var y = PitchTop + 6;
            var fader = p;
            w.Add(new LabelWidget($"P{p + 1}", x0, y, 18, 13, EditorTheme.Center, EditorTheme.TitleFont));
            w.Add(new FaderWidget(engine, ParameterTable.Pitch(p, PitchParam.Value), x0 + 22, y + 5, 150, vertical: false));
            w.Add(new DisplayTextWidget(() => (fader == 0 ? engine.P1 : engine.P2).ToString("+0.0;-0.0;0.0", inv), x0 + 192, y, 44, 13, tooltip: $"P{p + 1} as the engine hears it: lane or fader, MIDI, bend, glide (semitones)"));
            w.Add(new ToggleWidget(engine, ParameterTable.Pitch(p, PitchParam.Midi), x0 + 240, y, 34, 13, "MIDI"));
            w.Add(new KnobWidget(engine, ParameterTable.Pitch(p, PitchParam.Glide), x0 + 282, y - 3, 18, "GLIDE", labelBelow: true, labelWidth: 34));
            w.Add(new NumberWidget(engine, ParameterTable.Pitch(p, PitchParam.BendRange), x0 + 316, y, 28));
            w.Add(new LabelWidget("BEND", x0 + 312, y + 15, 36, 11, EditorTheme.Center, EditorTheme.LabelFont, EditorTheme.Text));
        }
    }

    private static void BuildSample(NoumenonEngine engine, IEditorHost host, List<Widget> w)
    {
        var y0 = SampleTop + 2;
        w.Add(new FrameWidget(4, SampleTop, SourceWidth, 64));
        w.Add(new WaveformWidget(engine, host, 10, y0 + 2, 290, 58));

        w.Add(new NumberWidget(engine, ParamId.SamplerSelect, 344, y0 + 4, 40, 12, "SELECT", 38));
        w.Add(new NumberWidget(engine, ParamId.SamplerPitch, 344, y0 + 19, 40, 12, "PITCH", 38));
        w.Add(new NumberWidget(engine, ParamId.SamplerFine, 344, y0 + 34, 40, 12, "FINE", 38));
        w.Add(new LabelWidget("MOD", 306, y0 + 49, 34, 12, EditorTheme.Right, EditorTheme.SmallFont, EditorTheme.TextDim));
        w.Add(new MenuWidget(engine, ParamId.SamplerModSource, 344, y0 + 49, 40, 12));

        w.Add(new LabelWidget("MODE", 392, y0 + 4, 30, 12, EditorTheme.Right, EditorTheme.SmallFont, EditorTheme.TextDim));
        w.Add(new MenuWidget(engine, ParamId.SamplerMode, 426, y0 + 4, 56, 12));
        w.Add(new NumberWidget(engine, ParamId.SamplerLoopCrossfade, 426, y0 + 19, 50, 12, "XFADE", 34));
        w.Add(new ToggleWidget(engine, ParamId.SamplerRetrigger, 392, y0 + 35, 40, 12, "RETRIG"));
        w.Add(new ToggleWidget(engine, ParamId.SamplerStereo, 436, y0 + 35, 40, 12, "STEREO"));
        w.Add(new TextButtonWidget("LOAD…", 392, y0 + 49, 40, 12, () => host.LoadSample(engine.Parameters.GetInt(ParamId.SamplerSelect)), tooltip: "Load a file into the selected key"));
        w.Add(new TextButtonWidget("GATE", 436, y0 + 49, 40, 12, engine.Gate, tooltip: "Fire a MIDI gate: retrigger the sample, restart the lanes if set"));

        Bar(w, engine, ParamId.SamplerAmp, 512, y0 + 9, 56, "AMP", 24);
        Bar(w, engine, ParamId.SamplerDir, 512, y0 + 27, 56, "DIR", 24);
        Bar(w, engine, ParamId.SamplerInputMix, 512, y0 + 45, 56, "S/IN", 24);
        w.Add(new KnobWidget(engine, ParamId.SamplerMaster, 598, y0 + 16, 24, "MASTER"));
        w.Add(new KnobWidget(engine, ParamId.SamplerAmDepth, 644, y0 + 16, 24, "AM"));
        w.Add(new LedWidget(() => engine.Midi.HeldCount > 0, 690, y0 + 24, 8, "GATE"));
    }

    // ---- The display and the combine ----

    private static void BuildColumn(NoumenonEngine engine, List<Widget> w)
    {
        var inv = CultureInfo.InvariantCulture;
        w.Add(new FrameWidget(ColumnX, Top, ColumnWidth, 334));
        w.Add(new ScopeWidget(engine, ColumnX + 4, Top + 4, ColumnWidth - 8, ColumnWidth - 8));
        w.Add(new ChoiceButtonsWidget(engine, ParamId.ScopeMode, ColumnX + 4, Top + ColumnWidth, ColumnWidth - 8, 14, ["OUT", "A × B", "SPECTRUM"]));

        var y = Top + ColumnWidth + 22;
        w.Add(new LabelWidget("COMBINE", ColumnX + 8, y, 100, 12, EditorTheme.Left, EditorTheme.TitleFont, EditorTheme.TextDim));
        w.Add(new KnobWidget(engine, ParamId.MixSumBalance, ColumnX + 8, y + 30, 22, "A+B"));
        w.Add(new FaderWidget(engine, ParamId.MixCrossfade, ColumnX + 40, y + 36, 140, vertical: false));
        w.Add(new KnobWidget(engine, ParamId.MixRingLevel, ColumnX + ColumnWidth - 30, y + 30, 22, "A×B"));
        w.Add(new DisplayTextWidget(() => $"X {engine.Parameters.Get(ParamId.ScopePhaseX).ToString("0.0", inv)} ms  Y {engine.Parameters.Get(ParamId.ScopePhaseY).ToString("0.0", inv)} ms  zoom {engine.Parameters.Get(ParamId.ScopeZoom).ToString("0.00", inv)}×", ColumnX + 8, y + 66, 180, 13, font: EditorTheme.SmallValueFont, color: EditorTheme.TextOnDark));
        w.Add(new KnobWidget(engine, ParamId.ScopeZoom, ColumnX + ColumnWidth - 28, y + 63, 18, "ZOOM", labelWidth: 36));
    }

    // ---- The chain ----

    private static void BuildChain(NoumenonEngine engine, List<Widget> w)
    {
        var t = ChainTop;

        Module(w, engine, 4, 124, "FILTER", ParamId.MasterFilterOn);
        w.Add(new KnobWidget(engine, ParamId.MasterFilterMorph, 14, t + 30, 26, "LP-HP"));
        w.Add(new KnobWidget(engine, ParamId.MasterFilterMix, 16, t + 76, 22, "MIX"));
        Bar(w, engine, ParamId.MasterFilterCutoff, 60, t + 32, 50, "CUT", 22);
        Bar(w, engine, ParamId.MasterFilterReso, 60, t + 56, 50, "RES", 22);

        Module(w, engine, 132, 112, "DIST", ParamId.DistortionOn);
        Bar(w, engine, ParamId.DistortionMix, 180, t + 30, 46, "MIX", 36);
        Bar(w, engine, ParamId.DistortionDrive, 180, t + 52, 46, "DRIVE", 36);
        w.Add(new LabelWidget("CURVE", 140, t + 72, 36, 12, EditorTheme.Right, EditorTheme.SmallFont, EditorTheme.TextDim));
        w.Add(new MenuWidget(engine, ParamId.DistortionCurve, 180, t + 72, 52, 12));

        Module(w, engine, 248, 66, "EQ", null);
        w.Add(new KnobWidget(engine, ParamId.EqHigh, 270, t + 30, 22, "HI"));
        w.Add(new KnobWidget(engine, ParamId.EqLow, 270, t + 70, 22, "LOW"));

        Module(w, engine, 318, 166, "DRIFT", ParamId.SpinOn);
        Bar(w, engine, ParamId.SpinTime1, 362, t + 26, 56, "T1", 30);
        Bar(w, engine, ParamId.SpinTime2, 362, t + 44, 56, "T2", 30);
        Bar(w, engine, ParamId.SpinFine, 362, t + 62, 56, "FINE", 30);
        Bar(w, engine, ParamId.SpinFeedback, 362, t + 80, 56, "FB", 30);
        w.Add(new ToggleWidget(engine, ParamId.SpinLink, 444, t + 3, 34, 12, "LINK"));
        w.Add(new KnobWidget(engine, ParamId.SpinMix, 452, t + 40, 22, "MIX"));

        Module(w, engine, 488, 184, "RESONATOR", ParamId.ResochordOn);
        w.Add(new ToggleWidget(engine, ParamId.ResochordMidi, 630, t + 3, 36, 12, "MIDI"));
        w.Add(new MenuWidget(engine, ParamId.ResochordChord, 496, t + 18, 100, 12));
        w.Add(new LabelWidget("SEL", 600, t + 18, 28, 12, EditorTheme.Right, EditorTheme.SmallFont, EditorTheme.TextDim));
        w.Add(new MenuWidget(engine, ParamId.ResochordChordSelect, 632, t + 18, 34, 12));
        w.Add(new NumberWidget(engine, ParamId.ResochordFeedback, 528, t + 34, 34, 12, "FB", 30));
        w.Add(new NumberWidget(engine, ParamId.ResochordPitch, 600, t + 34, 34, 12, "PITCH", 36));
        w.Add(new NumberWidget(engine, ParamId.ResochordFine, 528, t + 50, 34, 12, "FINE", 30));
        w.Add(new LabelWidget("MOD", 564, t + 50, 32, 12, EditorTheme.Right, EditorTheme.SmallFont, EditorTheme.TextDim));
        w.Add(new MenuWidget(engine, ParamId.ResochordModSource, 600, t + 50, 34, 12));
        Bar(w, engine, ParamId.ResochordMix, 528, t + 72, 40, "MIX", 30);
        Bar(w, engine, ParamId.ResochordFeedbackFader, 600, t + 72, 40, "FB", 30);
        w.Add(new ResonatorStripWidget(engine, 496, t + 86, 170, 22));

        Module(w, engine, 676, 150, "REVERB", ParamId.ReverbOn);
        w.Add(new MenuWidget(engine, ParamId.ReverbRoom, 684, t + 18, 84, 12));
        w.Add(new ToggleWidget(engine, ParamId.ReverbFreeze, 772, t + 18, 48, 12, "FREEZE"));
        w.Add(new LabelWidget("ORDER", 684, t + 34, 36, 12, EditorTheme.Left, EditorTheme.SmallFont, EditorTheme.TextDim));
        w.Add(new ChoiceButtonsWidget(engine, ParamId.ReverbOrder, 722, t + 34, 60, 12));
        w.Add(new KnobWidget(engine, ParamId.ReverbSize, 690, t + 62, 22, "SIZE"));
        w.Add(new KnobWidget(engine, ParamId.ReverbDamping, 728, t + 62, 22, "DAMP"));
        w.Add(new KnobWidget(engine, ParamId.ReverbMix, 766, t + 62, 22, "MIX"));
        w.Add(new DisplayTextWidget(() => engine.Parameters.GetInt(ParamId.ReverbOrder) == 0 ? "reverb → resonator" : "resonator → reverb", 684, t + 92, 134, 13, font: EditorTheme.SmallValueFont, color: EditorTheme.TextOnDark, tooltip: "The order of the two: Pre puts the reverb first"));
    }

    private static void BuildOut(NoumenonEngine engine, List<Widget> w)
    {
        var t = ChainTop;
        Module(w, engine, 834, 162, "OUT", null);
        w.Add(new KnobWidget(engine, ParamId.OutputVolume, 842, t + 30, 28, "VOLUME"));
        w.Add(new KnobWidget(engine, ParamId.OutputWidth, 884, t + 33, 22, "WIDTH"));
        w.Add(new MeterWidget(() => engine.OutputPeakLeft, 914, t + 16, 76, 8, MeterScale.Peak, "L"));
        w.Add(new MeterWidget(() => engine.OutputPeakRight, 914, t + 26, 76, 8, MeterScale.Peak, "R"));
        w.Add(new MeterWidget(() => engine.LimiterReductionLowDb, 914, t + 40, 76, 8, MeterScale.Reduction, "LO"));
        w.Add(new MeterWidget(() => engine.LimiterReductionHighDb, 914, t + 50, 76, 8, MeterScale.Reduction, "HI"));
        w.Add(new LabelWidget("LIMITER", 914, t + 60, 76, 10, EditorTheme.Left, EditorTheme.SmallFont, EditorTheme.TextDim));
        w.Add(new ToggleWidget(engine, ParamId.Mute, 842, t + 76, 48, 14, "MUTE"));
        w.Add(new KnobWidget(engine, ParamId.PauseFade, 906, t + 78, 18, "FADE"));
    }

    // ---- The bottom strip ----

    private static void BuildLanes(NoumenonEngine engine, List<Widget> w)
    {
        var t = BottomTop;
        w.Add(new FrameWidget(4, t, 440, BottomHeight, "LANES"));
        w.Add(new TextButtonWidget("RESTART", 12, t + 18, 56, 14, engine.Clock.RequestRestart, tooltip: "Restart every lane now (Space)"));
        w.Add(new ToggleWidget(engine, ParamId.RestartMode, 72, t + 18, 42, 14, "MODE"));
        w.Add(new ToggleWidget(engine, ParamId.RestartOnGate, 118, t + 18, 54, 14, "ON GATE"));
        w.Add(new LabelWidget("SPEED", 178, t + 18, 40, 14, EditorTheme.Right, EditorTheme.SmallFont, EditorTheme.TextDim));
        w.Add(new SpeedWidget(engine, 222, t + 18, 40, 14));
        w.Add(new KnobWidget(engine, ParamId.SpeedMultiplier, 271, t + 16, 16, "MULT", labelBelow: true, labelWidth: 34));
        w.Add(new TextButtonWidget("AUTO ALL", 300, t + 18, 44, 14, () => SetAllLanes(engine, LaneMode.Automated), tooltip: "Play every recorded loop"));
        w.Add(new TextButtonWidget("STATIC ALL", 348, t + 18, 46, 14, () => SetAllLanes(engine, LaneMode.Static), tooltip: "Stop every loop; faders keep their set values"));
        w.Add(new TextButtonWidget("CLEAR ALL", 398, t + 18, 40, 14, () => ClearLanes(engine), tooltip: "Forget every recorded loop"));
        w.Add(new LaneOverviewWidget(engine, 12, t + 44, 424, 114));
        w.Add(new DisplayTextWidget(() => LaneSummary(engine), 12, t + 159, 424, 12, font: EditorTheme.SmallValueFont, color: EditorTheme.TextOnDark, tooltip: "Lanes: faders that play themselves"));
    }

    private static void BuildTransport(NoumenonEngine engine, IEditorHost host, List<Widget> w)
    {
        var inv = CultureInfo.InvariantCulture;
        var t = BottomTop;
        w.Add(new FrameWidget(448, t, 300, BottomHeight, "TRANSPORT"));
        w.Add(new TextButtonWidget("RANDOMIZE", 456, t + 18, 72, 14, engine.RequestRandomize, tooltip: "Randomize with the scope and amount (R)"));
        w.Add(new MenuWidget(engine, ParamId.RandomizeScope, 532, t + 18, 76, 14));
        w.Add(new KnobWidget(engine, ParamId.RandomizeAmount, 616, t + 16, 20, "AMOUNT", labelBelow: true));
        w.Add(new NumberWidget(engine, ParamId.RandomSeed, 690, t + 19, 46, 12, "SEED", 34));
        w.Add(new DisplayTextWidget(() => PlayState(engine, inv), 456, t + 50, 140, 14, tooltip: "The play state: free run, host play, fading, muted"));
        w.Add(new DisplayTextWidget(() => $"{engine.HostSampleRate.ToString("0", inv)} Hz × {engine.Oversampling}  ·  {engine.LatencySamples} smp  ·  {engine.ActiveScale.Name}", 456, t + 70, 284, 13, font: EditorTheme.SmallValueFont, color: EditorTheme.TextOnDark));

        // Snapshot morph: pick a preset, a time, go — every parameter glides there; lanes and samples stay.
        var y = t + 92;
        w.Add(new LabelWidget("MORPH", 456, y, 34, 14, EditorTheme.Left, EditorTheme.SmallFont, EditorTheme.TextDim));
        var target = new ListChoiceWidget(() => host.PresetNames, 490, y, 132, 14, "The preset to morph to: every parameter glides there over the time beside it; lanes and samples stay");
        w.Add(target);
        w.Add(new ValueBoxWidget(() => morphSeconds, value => morphSeconds = value, 0.5f, 120f, 8f, "0.#", " s", 626, y, 40, 14, "Morph time: drag or wheel, double-click for 8 s"));
        w.Add(new TextButtonWidget("GO", 670, y, 32, 14, () => StartMorph(host, target.Selected), () => engine.IsMorphing, "Start the morph (lit while it runs)"));
        w.Add(new TextButtonWidget("STOP", 706, y, 34, 14, engine.CancelMorph, tooltip: "Stop the morph where it is"));
        w.Add(new MeterWidget(() => engine.IsMorphing ? engine.MorphProgress : 0f, 456, y + 19, 284, 5, MeterScale.Fraction));
        w.Add(new LabelWidget("Space restart · R randomize · M mute · Ctrl+Z undo", 456, t + 124, 284, 12, EditorTheme.Left, EditorTheme.SmallFont, EditorTheme.TextDim));
        w.Add(new LabelWidget("Right-click a control: lane, MIDI learn, modulate", 456, t + 138, 284, 12, EditorTheme.Left, EditorTheme.SmallFont, EditorTheme.TextDim));
        w.Add(new LabelWidget("Bar: click the strip to arm, drag to record, release to play", 456, t + 152, 284, 12, EditorTheme.Left, EditorTheme.SmallFont, EditorTheme.TextDim));
    }

    private static void StartMorph(IEditorHost host, int preset)
    {
        if (preset < 0)
            host.Notice("Pick a preset to morph to first.");
        else
            host.MorphToPreset(preset, morphSeconds);
    }

    private static void BuildWheel(NoumenonEngine engine, List<Widget> w)
    {
        var inv = CultureInfo.InvariantCulture;
        var t = BottomTop;
        w.Add(new FrameWidget(752, t, 244, 80, "MOD WHEEL"));
        w.Add(new LabelWidget("TARGET", 760, t + 18, 44, 12, EditorTheme.Left, EditorTheme.SmallFont, EditorTheme.TextDim));
        w.Add(new MenuWidget(engine, ParamId.WheelTarget, 804, t + 18, 120, 12));
        w.Add(new KnobWidget(engine, ParamId.WheelMin, 932, t + 16, 20, "MIN", labelBelow: true));
        w.Add(new KnobWidget(engine, ParamId.WheelMax, 964, t + 16, 20, "MAX", labelBelow: true));
        w.Add(new DisplayTextWidget(() => $"wheel {engine.Midi.Wheel.ToString("0.00", inv)}  bend {engine.Midi.Bend.ToString("+0.00;-0.00;0.00", inv)}  held {engine.Midi.HeldCount}", 760, t + 52, 180, 13, font: EditorTheme.SmallValueFont, color: EditorTheme.TextOnDark));
        w.Add(new LedWidget(() => engine.Midi.HeldCount > 0, 950, t + 55, 8, "GATE"));
    }

    private static void BuildWordmark(NoumenonEngine engine, List<Widget> w)
    {
        var inv = CultureInfo.InvariantCulture;
        var t = BottomTop + 84;
        w.Add(new FrameWidget(752, t, 244, 90, null, inset: true));
        w.Add(new LabelWidget("NOUMENON", 752, t + 12, 244, 24, EditorTheme.Center, EditorTheme.BadgeFont));
        w.Add(new LabelWidget("generative ambient loops", 752, t + 38, 244, 12, EditorTheme.Center, EditorTheme.SmallFont, EditorTheme.TextDim));
        w.Add(new DisplayTextWidget(() => PlayState(engine, inv), 764, t + 60, 220, 14, font: EditorTheme.SmallValueFont));
    }

    // ---- Helpers ----

    private static void Module(List<Widget> w, NoumenonEngine engine, float x, float width, string title, ParamId? on)
    {
        w.Add(new FrameWidget(x, ChainTop, width, ChainHeight));
        if (on is { } id)
        {
            w.Add(new ToggleWidget(engine, id, x + 6, ChainTop + 4, 10, 10, "", ToggleStyle.Led));
            w.Add(new LabelWidget(title, x + 18, ChainTop + 2, width - 20, 13, EditorTheme.Left, EditorTheme.TitleFont, EditorTheme.TextDim));
        }
        else
        {
            w.Add(new LabelWidget(title, x + 8, ChainTop + 2, width - 16, 13, EditorTheme.Left, EditorTheme.TitleFont, EditorTheme.TextDim));
        }
    }

    /// <summary>A horizontal bar with its caption to the left; <paramref name="y"/> is the top of the bar's track.</summary>
    private static void Bar(List<Widget> w, NoumenonEngine engine, ParamId id, float x, float y, float length, string label, float labelWidth)
    {
        w.Add(new LabelWidget(label, x - labelWidth - 2f, y - 4f, labelWidth, 13f, EditorTheme.Right, EditorTheme.SmallFont, EditorTheme.TextDim));
        w.Add(new FaderWidget(engine, id, x, y, length, vertical: false));
    }

    private static string PlayState(NoumenonEngine engine, CultureInfo inv) =>
        engine.FadeLevel <= 0f ? "MUTED"
        : engine.FadeLevel < 1f ? "FADING"
        : engine.Clock.HostPlaying ? "PLAYING " + engine.Clock.HostTempo.ToString("0.#", inv) + " BPM"
        : "FREE RUN";

    private static string LaneSummary(NoumenonEngine engine)
    {
        var playing = 0;
        var recording = 0;
        var armed = 0;
        foreach (var lane in engine.Lanes.All)
        {
            if (lane.IsPlaying)
                playing++;

            if (lane.IsRecording)
                recording++;

            if (lane.Armed)
                armed++;
        }

        return $"{playing} lanes playing · {recording} rec · {armed} armed";
    }

    private static void SetAllLanes(NoumenonEngine engine, LaneMode mode)
    {
        foreach (var lane in engine.Lanes.All)
        {
            if (lane.HasRecording || mode == LaneMode.Static)
                lane.Mode = mode;
        }
    }

    private static void ClearLanes(NoumenonEngine engine)
    {
        foreach (var lane in engine.Lanes.All)
            lane.Clear();
    }
}
