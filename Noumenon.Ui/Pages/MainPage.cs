using System.Globalization;
using Noumenon.Engine;
using Noumenon.Engine.Lanes;
using Noumenon.Engine.Parameters;
using Noumenon.Ui.Widgets;

namespace Noumenon.Ui.Pages;

/// <summary>
/// The main panel in the layout of the instrument it follows: the effect strip across the top
/// (filter, distortion, EQ, spin, resochord, reverb, volume), section A on the left and B on the
/// right with their routing rows, waveform icons, pitch readouts and level faders, the P1/P2
/// faders, the A+B / A×B crossfade and the XY scope down the centre, the sampler bottom-left,
/// the transport bottom-right, the mod-wheel box under the scope. Coordinates are logical
/// pixels on a 1000 × 664 canvas whose top 24 are the editor's own bar.
/// </summary>
internal static class MainPage
{
    private const float StripTop = 28f;
    private const float StripHeight = 110f;
    private const float SectionTop = 144f;
    private const float SectionHeight = 302f;
    private const float BottomTop = 452f;
    private const float BottomHeight = 208f;
    private const float SlotWidth = 36f;
    private const float CaptionRow = 16f;
    private const float FaderTop = 28f;
    private const float FaderLength = 48f;

    public static PageContent Build(NoumenonEngine engine, IEditorHost host)
    {
        var w = new List<Widget>();
        BuildStrip(engine, w);
        BuildSection(engine, w, SectionId.A);
        BuildSection(engine, w, SectionId.B);
        BuildCentre(engine, w);
        BuildSampler(engine, host, w);
        BuildTransport(engine, w);

        return new PageContent(w, null);
    }

    private static void BuildStrip(NoumenonEngine engine, List<Widget> w)
    {
        // FILTER
        Module(w, 4, 146, "FILTER", engine, ParamId.MasterFilterOn);
        w.Add(new KnobWidget(engine, ParamId.MasterFilterMorph, 14, StripTop + 30, 28, "LP-HP"));
        w.Add(new KnobWidget(engine, ParamId.MasterFilterMix, 116, StripTop + 34, 24, "MIX"));
        Fader(w, engine, ParamId.MasterFilterCutoff, 64, "CUT");
        Fader(w, engine, ParamId.MasterFilterReso, 96, "RES");

        // DISTORTION
        Module(w, 154, 124, "DISTORTION", engine, ParamId.DistortionOn);
        Fader(w, engine, ParamId.DistortionMix, 176, "MIX");
        Fader(w, engine, ParamId.DistortionDrive, 208, "DRIVE");
        w.Add(new LabelWidget("CURVE", 232, StripTop + CaptionRow, 42, 11));
        w.Add(new MenuWidget(engine, ParamId.DistortionCurve, 232, StripTop + 28, 42));

        // EQ
        Module(w, 282, 60, "EQ", engine, null);
        w.Add(new KnobWidget(engine, ParamId.EqHigh, 298, StripTop + 26, 24, "HI"));
        w.Add(new KnobWidget(engine, ParamId.EqLow, 298, StripTop + 66, 24, "LOW"));

        // SPIN
        Module(w, 346, 196, "SPIN", engine, ParamId.SpinOn);
        Fader(w, engine, ParamId.SpinTime1, 366, "T1");
        Fader(w, engine, ParamId.SpinTime2, 396, "T2");
        Fader(w, engine, ParamId.SpinFine, 426, "FINE");
        Fader(w, engine, ParamId.SpinFeedback, 456, "FB");
        w.Add(new ToggleWidget(engine, ParamId.SpinLink, 486, StripTop + CaptionRow + 2, 40, 13, "LINK"));
        w.Add(new KnobWidget(engine, ParamId.SpinMix, 494, StripTop + 52, 24, "MIX"));

        // RESOCHORD
        Module(w, 546, 206, "RESOCHORD", engine, ParamId.ResochordOn);
        w.Add(new MenuWidget(engine, ParamId.ResochordChord, 552, StripTop + CaptionRow + 2, 92));
        w.Add(new NumberWidget(engine, ParamId.ResochordFeedback, 588, StripTop + 38, 40, 12, "FB", 36));
        w.Add(new NumberWidget(engine, ParamId.ResochordPitch, 588, StripTop + 54, 40, 12, "PITCH", 36));
        w.Add(new NumberWidget(engine, ParamId.ResochordFine, 588, StripTop + 70, 40, 12, "FINE", 36));
        w.Add(new LabelWidget("MOD", 552, StripTop + 86, 33, 14, EditorTheme.Right));
        w.Add(new MenuWidget(engine, ParamId.ResochordModSource, 588, StripTop + 86, 40));
        Fader(w, engine, ParamId.ResochordMix, 650, "MIX");
        Fader(w, engine, ParamId.ResochordFeedbackFader, 680, "FB");
        w.Add(new ToggleWidget(engine, ParamId.ResochordMidi, 706, StripTop + CaptionRow + 2, 40, 13, "MIDI"));
        w.Add(new LabelWidget("SELECT", 706, StripTop + 40, 40, 11));
        w.Add(new MenuWidget(engine, ParamId.ResochordChordSelect, 706, StripTop + 52, 40));

        // REVERB
        Module(w, 756, 152, "REVERB", engine, ParamId.ReverbOn);
        w.Add(new MenuWidget(engine, ParamId.ReverbRoom, 762, StripTop + CaptionRow + 2, 86));
        w.Add(new LabelWidget("ORDER", 762, StripTop + 38, 40, 11, EditorTheme.Left));
        w.Add(new ChoiceButtonsWidget(engine, ParamId.ReverbOrder, 762, StripTop + 50, 62, 13));
        w.Add(new ToggleWidget(engine, ParamId.ReverbFreeze, 762, StripTop + 68, 46, 13, "FREEZE"));
        w.Add(new KnobWidget(engine, ParamId.ReverbSize, 822, StripTop + 60, 22, "SIZE"));
        w.Add(new KnobWidget(engine, ParamId.ReverbDamping, 850, StripTop + 60, 22, "DAMP"));
        Fader(w, engine, ParamId.ReverbMix, 884, "MIX");

        // VOLUME
        Module(w, 912, 84, "VOLUME", engine, null);
        w.Add(new KnobWidget(engine, ParamId.OutputVolume, 916, StripTop + 24, 32));
        w.Add(new KnobWidget(engine, ParamId.OutputWidth, 956, StripTop + 30, 24, "WIDTH", labelBelow: true));
    }

    private static void Module(List<Widget> w, float x, float width, string title, NoumenonEngine engine, ParamId? on)
    {
        w.Add(new FrameWidget(x, StripTop, width, StripHeight, null));
        if (on is { } id)
        {
            w.Add(new ToggleWidget(engine, id, x + 6, StripTop + 4, 10, 10, "", ToggleStyle.Led));
            w.Add(new LabelWidget(title, x + 18, StripTop + 2, width - 20, 13, EditorTheme.Left, EditorTheme.TitleFont));
        }
        else
        {
            w.Add(new LabelWidget(title, x + 8, StripTop + 2, width - 16, 13, EditorTheme.Left, EditorTheme.TitleFont));
        }
    }

    private static void Fader(List<Widget> w, NoumenonEngine engine, ParamId id, float slotX, string label)
    {
        w.Add(new LabelWidget(label, slotX - 12, StripTop + CaptionRow, 32, 11));
        w.Add(new FaderWidget(engine, id, slotX, StripTop + FaderTop, FaderLength));
    }

    private static void BuildSection(NoumenonEngine engine, List<Widget> w, SectionId section)
    {
        var isA = section == SectionId.A;
        var frameX = isA ? 4f : 670f;
        w.Add(new FrameWidget(frameX, SectionTop, 326, SectionHeight));
        w.Add(new LabelWidget(section.ToString(), isA ? 10 : 958, SectionTop + 2, 30, 32, EditorTheme.Center, EditorTheme.HugeFont));

        var slotsX = isA ? 58f : 728f;
        var row1 = SectionTop + 4;
        var row2 = SectionTop + 20;
        for (var slot = 0; slot < ParameterTable.SlotsPerSection; slot++)
        {
            var x0 = slotsX + slot * SlotWidth;
            w.Add(new ToggleWidget(engine, ParameterTable.Osc(section, slot, OscParam.P1Route), x0 + 2, row1, 30, 12, "ON"));
            w.Add(new ToggleWidget(engine, ParameterTable.Osc(section, slot, OscParam.P2Route), x0 + 2, row2, 30, 12, "ON"));
        }

        var fineX = isA ? 280f : 698f;
        var allX = isA ? 306f : 672f;
        w.Add(new ToggleWidget(engine, ParameterTable.Section(section, SectionParam.P1FineRange), fineX, row1, 22, 12, "FINE"));
        w.Add(new ToggleWidget(engine, ParameterTable.Section(section, SectionParam.P1RouteAll), allX, row1, 22, 12, "ALL"));
        w.Add(new ToggleWidget(engine, ParameterTable.Section(section, SectionParam.P2FineRange), fineX, row2, 22, 12, "FINE"));
        w.Add(new ToggleWidget(engine, ParameterTable.Section(section, SectionParam.P2RouteAll), allX, row2, 22, 12, "ALL"));
        w.Add(new LabelWidget("P1", isA ? 40 : 946, row1, 16, 12, EditorTheme.Center, EditorTheme.SmallFont));
        w.Add(new LabelWidget("P2", isA ? 40 : 946, row2, 16, 12, EditorTheme.Center, EditorTheme.SmallFont));

        var columnX = isA ? 10f : 952f;
        w.Add(new KnobWidget(engine, ParameterTable.Section(section, SectionParam.Cutoff), columnX + 4, SectionTop + 50, 28, "CUTOFF"));
        w.Add(new ToggleWidget(engine, ParameterTable.Section(section, SectionParam.Slave), columnX, SectionTop + 86, 36, 13, "SLAVE"));
        w.Add(new KnobWidget(engine, ParameterTable.Section(section, SectionParam.Reso), columnX + 4, SectionTop + 116, 28, "RESO"));
        w.Add(new ChoiceButtonsWidget(engine, ParameterTable.Section(section, SectionParam.FilterType), columnX, SectionTop + 150, 36, 44, null, vertical: true));

        for (var slot = 0; slot < ParameterTable.SlotsPerSection; slot++)
        {
            var x0 = slotsX + slot * SlotWidth;
            var index = slot;
            w.Add(new WaveIconWidget(engine, section, slot, x0 + 1, SectionTop + 40, 32, 18));
            w.Add(new KnobWidget(engine, () => IsNoise(engine, section, index) ? ParameterTable.Osc(section, index, OscParam.NoiseCutoff) : ParameterTable.Osc(section, index, OscParam.Shape), x0 + 7, SectionTop + 63, 20));
            w.Add(new NumberWidget(engine, ParameterTable.Osc(section, slot, OscParam.Semitone), x0 + 1, SectionTop + 88, 32));
            w.Add(new NumberWidget(engine, ParameterTable.Osc(section, slot, OscParam.Fine), x0 + 1, SectionTop + 102, 32));
            w.Add(new NumberWidget(engine, ParameterTable.Osc(section, slot, OscParam.Trim), x0 + 1, SectionTop + 116, 32));
            w.Add(new FaderWidget(engine, ParameterTable.Osc(section, slot, OscParam.Level), x0 + 13, SectionTop + 134, 140));
        }

        var levelX = isA ? 300f : 686f;
        w.Add(new VerticalLabelWidget($"LEVEL {section}", isA ? 288 : 706, SectionTop + 150));
        w.Add(new FaderWidget(engine, ParameterTable.Section(section, SectionParam.Level), levelX, SectionTop + 42, 230));
    }

    private static bool IsNoise(NoumenonEngine engine, SectionId section, int slot) => engine.Parameters.GetInt(ParameterTable.Osc(section, slot, OscParam.Wave)) >= 6;

    private static void BuildCentre(NoumenonEngine engine, List<Widget> w)
    {
        var inv = CultureInfo.InvariantCulture;
        for (var p = 0; p < ParameterTable.PitchFaderCount; p++)
        {
            var y = SectionTop + 6 + p * 26;
            var fader = p;
            w.Add(new LabelWidget($"P{p + 1}", 338, y, 18, 13, EditorTheme.Center, EditorTheme.TitleFont));
            w.Add(new FaderWidget(engine, ParameterTable.Pitch(p, PitchParam.Value), 358, y + 2, 150, vertical: false));
            w.Add(new DisplayTextWidget(() => (fader == 0 ? engine.P1 : engine.P2).ToString("+0.0;-0.0;0.0", inv), 528, y, 44, 13, tooltip: $"P{p + 1} as the engine hears it: lane or fader, MIDI, bend, glide (semitones)"));
            w.Add(new ToggleWidget(engine, ParameterTable.Pitch(p, PitchParam.Midi), 578, y, 34, 13, "MIDI"));
            w.Add(new KnobWidget(engine, ParameterTable.Pitch(p, PitchParam.Glide), 616, y - 4, 20));
            w.Add(new NumberWidget(engine, ParameterTable.Pitch(p, PitchParam.BendRange), 640, y, 26));
        }

        w.Add(new LabelWidget("GLIDE", 608, SectionTop + 58, 36, 11, EditorTheme.Center, EditorTheme.SmallFont));
        w.Add(new LabelWidget("BEND", 638, SectionTop + 58, 30, 11, EditorTheme.Center, EditorTheme.SmallFont));

        w.Add(new KnobWidget(engine, ParamId.MixSumBalance, 346, SectionTop + 64, 26, "A+B", labelBelow: true));
        w.Add(new FaderWidget(engine, ParamId.MixCrossfade, 386, SectionTop + 74, 194, vertical: false));
        w.Add(new KnobWidget(engine, ParamId.MixRingLevel, 612, SectionTop + 64, 26, "A×B", labelBelow: true));

        w.Add(new ScopeWidget(engine, 340, SectionTop + 108, 280, 300));
        w.Add(new DisplayTextWidget(() => $"X {engine.Parameters.Get(ParamId.ScopePhaseX).ToString("0.0", inv)} ms   Y {engine.Parameters.Get(ParamId.ScopePhaseY).ToString("0.0", inv)} ms   zoom {engine.Parameters.Get(ParamId.ScopeZoom).ToString("0.00", inv)}×", 340, SectionTop + 412, 280, 13, font: EditorTheme.SmallValueFont, color: EditorTheme.TextOnDark));
        w.Add(new FaderWidget(engine, ParamId.ScopeZoom, 642, SectionTop + 116, 240));
        w.Add(new LabelWidget("ZOOM", 626, SectionTop + 362, 52, 11));

        w.Add(new FrameWidget(340, 574, 280, 82, "MOD WHEEL", inset: true));
        w.Add(new LabelWidget("TARGET", 350, 591, 60, 11, EditorTheme.Left));
        w.Add(new MenuWidget(engine, ParamId.WheelTarget, 350, 603, 150));
        w.Add(new KnobWidget(engine, ParamId.WheelMin, 526, 600, 22, "MIN"));
        w.Add(new KnobWidget(engine, ParamId.WheelMax, 566, 600, 22, "MAX"));
        w.Add(new DisplayTextWidget(() => $"wheel {engine.Midi.Wheel.ToString("0.00", inv)}  bend {engine.Midi.Bend.ToString("+0.00;-0.00;0.00", inv)}  held {engine.Midi.HeldCount}", 350, 626, 200, 13, font: EditorTheme.SmallValueFont, color: EditorTheme.TextOnDark));
        w.Add(new LedWidget(() => engine.Midi.HeldCount > 0, 566, 629, 8, "GATE"));
    }

    private static void BuildSampler(NoumenonEngine engine, IEditorHost host, List<Widget> w)
    {
        w.Add(new FrameWidget(4, BottomTop, 326, BottomHeight, "SAMPLER"));
        w.Add(new WaveformWidget(engine, host, 12, BottomTop + 18, 200, 54));
        w.Add(new KnobWidget(engine, ParamId.SamplerMaster, 14, BottomTop + 96, 26, "MASTER"));
        w.Add(new LabelWidget("AMP", 56, BottomTop + 85, 20, 11));
        w.Add(new FaderWidget(engine, ParamId.SamplerAmp, 62, BottomTop + 96, 60));
        w.Add(new LabelWidget("DIR", 86, BottomTop + 85, 20, 11));
        w.Add(new FaderWidget(engine, ParamId.SamplerDir, 92, BottomTop + 96, 60));
        w.Add(new LabelWidget("SMP/IN", 110, BottomTop + 85, 32, 11));
        w.Add(new FaderWidget(engine, ParamId.SamplerInputMix, 122, BottomTop + 96, 60));
        w.Add(new KnobWidget(engine, ParamId.SamplerAmDepth, 152, BottomTop + 96, 22, "AM"));
        w.Add(new LedWidget(() => engine.Midi.HeldCount > 0, 150, BottomTop + 150, 8, "GATE"));

        w.Add(new NumberWidget(engine, ParamId.SamplerSelect, 262, BottomTop + 18, 44, 12, "SELECT", 46));
        w.Add(new NumberWidget(engine, ParamId.SamplerPitch, 262, BottomTop + 34, 44, 12, "PITCH", 46));
        w.Add(new NumberWidget(engine, ParamId.SamplerFine, 262, BottomTop + 50, 44, 12, "FINE", 46));
        w.Add(new LabelWidget("MOD", 214, BottomTop + 66, 44, 14, EditorTheme.Right));
        w.Add(new MenuWidget(engine, ParamId.SamplerModSource, 262, BottomTop + 66, 44));
        w.Add(new LabelWidget("MODE", 214, BottomTop + 84, 44, 14, EditorTheme.Right));
        w.Add(new MenuWidget(engine, ParamId.SamplerMode, 262, BottomTop + 84, 44));
        w.Add(new NumberWidget(engine, ParamId.SamplerLoopCrossfade, 262, BottomTop + 102, 44, 12, "XFADE", 46));
        w.Add(new ToggleWidget(engine, ParamId.SamplerRetrigger, 218, BottomTop + 122, 44, 13, "RETRIG"));
        w.Add(new ToggleWidget(engine, ParamId.SamplerStereo, 266, BottomTop + 122, 44, 13, "STEREO"));
        w.Add(new TextButtonWidget("LOAD…", 218, BottomTop + 142, 44, 13, () => host.LoadSample(engine.Parameters.GetInt(ParamId.SamplerSelect)), tooltip: "Load a file into the selected key"));
        w.Add(new TextButtonWidget("GATE", 266, BottomTop + 142, 44, 13, engine.Gate, tooltip: "Fire a MIDI gate: retrigger the sample, restart the lanes if set"));
    }

    private static void BuildTransport(NoumenonEngine engine, List<Widget> w)
    {
        var inv = CultureInfo.InvariantCulture;
        w.Add(new FrameWidget(670, BottomTop, 326, BottomHeight, "TRANSPORT"));
        w.Add(new TextButtonWidget("RESTART", 684, BottomTop + 20, 64, 16, engine.Clock.RequestRestart, tooltip: "Restart every lane now (Space)"));
        w.Add(new ToggleWidget(engine, ParamId.RestartMode, 754, BottomTop + 20, 44, 16, "MODE"));
        w.Add(new ToggleWidget(engine, ParamId.RestartOnGate, 804, BottomTop + 20, 52, 16, "ON GATE"));
        w.Add(new LabelWidget("SPEED", 882, BottomTop + 10, 44, 11));
        w.Add(new SpeedWidget(engine, 882, BottomTop + 22, 44, 14));
        w.Add(new KnobWidget(engine, ParamId.SpeedMultiplier, 934, BottomTop + 18, 20));

        w.Add(new TextButtonWidget("RANDOMIZE", 684, BottomTop + 48, 72, 16, engine.RequestRandomize, tooltip: "Randomize with the scope and amount (R)"));
        w.Add(new MenuWidget(engine, ParamId.RandomizeScope, 762, BottomTop + 49, 70));
        w.Add(new KnobWidget(engine, ParamId.RandomizeAmount, 842, BottomTop + 46, 22, "AMOUNT", labelBelow: true));
        w.Add(new NumberWidget(engine, ParamId.RandomSeed, 714, BottomTop + 72, 42, 12, "SEED", 30));
        w.Add(new ToggleWidget(engine, ParamId.Mute, 882, BottomTop + 48, 44, 16, "MUTE"));
        w.Add(new ToggleWidget(engine, ParamId.Pause, 882, BottomTop + 70, 44, 16, "PAUSE"));
        w.Add(new KnobWidget(engine, ParamId.PauseFade, 934, BottomTop + 74, 20, "FADE"));

        w.Add(new DisplayTextWidget(() => LaneSummary(engine), 684, BottomTop + 96, 172, 13, font: EditorTheme.SmallValueFont, color: EditorTheme.TextOnDark, tooltip: "Lanes: faders that play themselves"));
        w.Add(new TextButtonWidget("AUTO ALL", 684, BottomTop + 116, 54, 14, () => SetAllLanes(engine, LaneMode.Automated), tooltip: "Play every recorded loop"));
        w.Add(new TextButtonWidget("STATIC ALL", 742, BottomTop + 116, 58, 14, () => SetAllLanes(engine, LaneMode.Static), tooltip: "Stop every loop; faders keep their set values"));
        w.Add(new TextButtonWidget("CLEAR ALL", 804, BottomTop + 116, 52, 14, () => ClearLanes(engine), tooltip: "Forget every recorded loop"));
        w.Add(new DisplayTextWidget(() => $"{engine.HostSampleRate.ToString("0", inv)} Hz × {engine.Oversampling}  ·  {engine.LatencySamples} smp  ·  {engine.ActiveScale.Name}", 684, BottomTop + 140, 172, 13, font: EditorTheme.SmallValueFont, color: EditorTheme.TextOnDark));

        w.Add(new FrameWidget(866, BottomTop + 96, 122, 100, null, inset: true));
        w.Add(new LabelWidget("NOUMENON", 866, BottomTop + 112, 122, 24, EditorTheme.Center, EditorTheme.BadgeFont));
        w.Add(new LabelWidget("generative ambient loops", 866, BottomTop + 138, 122, 12, EditorTheme.Center, EditorTheme.SmallFont));
        w.Add(new DisplayTextWidget(() => engine.Parameters.Get(ParamId.Mute) >= 0.5f ? "MUTED" : engine.PauseLevel <= 0f ? "PAUSED" : engine.PauseLevel < 1f ? "FADING" : engine.Clock.HostPlaying ? "PLAYING " + engine.Clock.HostTempo.ToString("0.#", inv) + " BPM" : "FREE RUN", 876, BottomTop + 160, 102, 14, font: EditorTheme.SmallValueFont));
    }

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
