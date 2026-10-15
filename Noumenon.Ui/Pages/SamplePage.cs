using System.Globalization;
using Noumenon.Dsp.Sampler;
using Noumenon.Engine;
using Noumenon.Engine.Parameters;
using Noumenon.Ui.Widgets;

namespace Noumenon.Ui.Pages;

/// <summary>
/// The SAMPLE tab: the sampler in depth. A wide display of the selected key shows the loop and,
/// in Granular mode, every grain as it sounds, in Stretch mode the window being analysed; beneath
/// it the playback controls (mode, key, pitch, the Amp / Dir / Samp↔In bars), the Granular module
/// (size, density, position with its lane, spray, pitch spread, window, width, free or locked scan),
/// the Stretch module (amount and window length), the wavetable the oscillators' Wavetable wave
/// reads, and the list of loaded keys. The MAIN page's sampler strip is the short form of this.
/// </summary>
internal static class SamplePage
{
    private const float Top = 28f;
    private const float RowTop = 248f;
    private const float RowHeight = 150f;
    private const float LowerTop = 404f;
    private const float LowerHeight = 256f;

    public static PageContent Build(NoumenonEngine engine, IEditorHost host)
    {
        var inv = CultureInfo.InvariantCulture;
        var w = new List<Widget>();

        w.Add(new FrameWidget(4, Top, 992, 214));
        w.Add(new WaveformWidget(engine, host, 10, Top + 6, 980, 202));

        // Playback.
        var t = RowTop;
        w.Add(new FrameWidget(4, t, 330, RowHeight, "PLAYBACK"));
        w.Add(new ChoiceButtonsWidget(engine, ParamId.SamplerMode, 12, t + 18, 314, 16, ["CLASSIC", "GRANULAR", "STRETCH"]));
        w.Add(new NumberWidget(engine, ParamId.SamplerSelect, 52, t + 44, 40, 12, "KEY", 38));
        w.Add(new TextButtonWidget("LOAD…", 98, t + 44, 46, 12, () => host.LoadSample(engine.Parameters.GetInt(ParamId.SamplerSelect)), tooltip: "Load a file into the selected key"));
        w.Add(new TextButtonWidget("GATE", 148, t + 44, 40, 12, engine.Gate, tooltip: "Fire a MIDI gate: retrigger the sample, restart the lanes if set"));
        w.Add(new ToggleWidget(engine, ParamId.SamplerRetrigger, 192, t + 44, 44, 12, "RETRIG"));
        w.Add(new ToggleWidget(engine, ParamId.SamplerStereo, 240, t + 44, 44, 12, "STEREO"));
        w.Add(new LedWidget(() => engine.Midi.HeldCount > 0, 292, t + 46, 8, "GATE"));
        w.Add(new NumberWidget(engine, ParamId.SamplerPitch, 52, t + 62, 40, 12, "PITCH", 38));
        w.Add(new NumberWidget(engine, ParamId.SamplerFine, 52, t + 78, 40, 12, "FINE", 38));
        w.Add(new LabelWidget("MOD", 10, t + 94, 38, 12, EditorTheme.Right, EditorTheme.SmallFont, EditorTheme.TextDim));
        w.Add(new MenuWidget(engine, ParamId.SamplerModSource, 52, t + 94, 40, 12));
        w.Add(new NumberWidget(engine, ParamId.SamplerLoopCrossfade, 52, t + 110, 40, 12, "XFADE", 38));
        Bar(w, engine, ParamId.SamplerAmp, 136, t + 66, 70, "AMP", 30);
        Bar(w, engine, ParamId.SamplerDir, 136, t + 86, 70, "DIR", 30);
        Bar(w, engine, ParamId.SamplerInputMix, 136, t + 106, 70, "S/IN", 30);
        w.Add(new KnobWidget(engine, ParamId.SamplerMaster, 238, t + 74, 24, "MASTER"));
        w.Add(new KnobWidget(engine, ParamId.SamplerAmDepth, 284, t + 74, 24, "AM"));
        w.Add(new DisplayTextWidget(() => SampleLine(engine, inv), 12, t + 130, 314, 13, font: EditorTheme.SmallValueFont, color: EditorTheme.TextOnDark, format: EditorTheme.Left));

        // Granular.
        var gx = 340f;
        w.Add(new FrameWidget(gx, t, 360, RowHeight, "GRANULAR"));
        w.Add(new KnobWidget(engine, ParamId.GrainSize, gx + 14, t + 30, 26, "SIZE"));
        w.Add(new KnobWidget(engine, ParamId.GrainDensity, gx + 70, t + 30, 26, "DENSITY"));
        w.Add(new KnobWidget(engine, ParamId.GrainSpray, gx + 126, t + 30, 26, "SPRAY"));
        w.Add(new KnobWidget(engine, ParamId.GrainPitchSpread, gx + 182, t + 30, 26, "PITCH ±"));
        w.Add(new KnobWidget(engine, ParamId.GrainWindow, gx + 238, t + 30, 26, "WINDOW"));
        w.Add(new KnobWidget(engine, ParamId.GrainWidth, gx + 294, t + 30, 26, "WIDTH"));
        Bar(w, engine, ParamId.GrainPosition, gx + 64, t + 86, 270, "POSITION", 56);
        w.Add(new LabelWidget("SCAN", gx + 8, t + 104, 54, 14, EditorTheme.Right, EditorTheme.SmallFont, EditorTheme.TextDim));
        w.Add(new ChoiceButtonsWidget(engine, ParamId.GrainScan, gx + 64, t + 104, 110, 14, ["FREE", "LOCKED"]));
        w.Add(new DisplayTextWidget(() => engine.ActiveGrains == 1 ? "1 grain" : $"{engine.ActiveGrains} grains", gx + 184, t + 104, 74, 14, font: EditorTheme.SmallValueFont, tooltip: "Grains sounding now (32 at most)"));
        w.Add(new LabelWidget("Free: grains come from POSITION. Locked: they follow a playhead moving at DIR.", gx + 10, t + 126, 340, 12, EditorTheme.Left, EditorTheme.SmallFont, EditorTheme.TextDim));

        // Stretch.
        var sx = 706f;
        w.Add(new FrameWidget(sx, t, 290, RowHeight, "STRETCH"));
        w.Add(new KnobWidget(engine, ParamId.StretchAmount, sx + 16, t + 30, 36, "AMOUNT"));
        w.Add(new LabelWidget("WINDOW", sx + 74, t + 22, 90, 12, EditorTheme.Left, EditorTheme.SmallFont, EditorTheme.TextDim));
        w.Add(new ChoiceButtonsWidget(engine, ParamId.StretchWindow, sx + 74, t + 36, 204, 14, ["SHORT", "MEDIUM", "LONG"]));
        w.Add(new DisplayTextWidget(() => StretchLine(engine, inv), sx + 74, t + 58, 204, 13, font: EditorTheme.SmallValueFont, color: EditorTheme.TextOnDark, format: EditorTheme.Left));
        w.Add(new LabelWidget("The playhead crawls at DIR ÷ AMOUNT; DIR 0 freezes", sx + 10, t + 92, 274, 12, EditorTheme.Left, EditorTheme.SmallFont, EditorTheme.TextDim));
        w.Add(new LabelWidget("the sound. PITCH transposes without changing speed.", sx + 10, t + 106, 274, 12, EditorTheme.Left, EditorTheme.SmallFont, EditorTheme.TextDim));
        w.Add(new LabelWidget("Long windows smear more and cost more.", sx + 10, t + 120, 274, 12, EditorTheme.Left, EditorTheme.SmallFont, EditorTheme.TextDim));

        // The wavetable.
        var l = LowerTop;
        w.Add(new FrameWidget(4, l, 496, LowerHeight, "WAVETABLE"));
        w.Add(new NumberWidget(engine, ParamId.WavetableKey, 70, l + 18, 40, 12, "TABLE KEY", 58));
        w.Add(new TextButtonWidget("USE THE SELECTED KEY", 118, l + 18, 120, 12, () => engine.Parameters.Set(ParamId.WavetableKey, engine.Parameters.GetInt(ParamId.SamplerSelect)), tooltip: "Make the sampler's key the wavetable"));
        w.Add(new WavetableViewWidget(engine, 10, l + 36, 484, 188));
        w.Add(new LabelWidget("Pick Wavetable as a slot's wave (MAIN): Shape scans the frames, pitch as any slot.", 12, l + 228, 480, 12, EditorTheme.Left, EditorTheme.SmallFont, EditorTheme.TextDim));
        w.Add(new LabelWidget("A short file is one cycle; a long one gives 32 frames across the recording.", 12, l + 241, 480, 12, EditorTheme.Left, EditorTheme.SmallFont, EditorTheme.TextDim));

        // The keys.
        w.Add(new FrameWidget(506, l, 490, LowerHeight, "KEYS"));
        w.Add(new SampleKeysWidget(engine, 512, l + 18, 478, 216));
        w.Add(new LabelWidget("Drop files on the display above · double-click it to load · right-click a key for more", 512, l + 238, 478, 12, EditorTheme.Left, EditorTheme.SmallFont, EditorTheme.TextDim));

        return new PageContent(w, null);
    }

    private static void Bar(List<Widget> w, NoumenonEngine engine, ParamId id, float x, float y, float length, string label, float labelWidth)
    {
        w.Add(new LabelWidget(label, x - labelWidth - 2f, y - 4f, labelWidth, 13f, EditorTheme.Right, EditorTheme.SmallFont, EditorTheme.TextDim));
        w.Add(new FaderWidget(engine, id, x, y, length, vertical: false));
    }

    private static string SampleLine(NoumenonEngine engine, CultureInfo inv)
    {
        var slot = engine.Samples.Get(engine.Parameters.GetInt(ParamId.SamplerSelect));
        if (slot is null)
            return "no sample on this key";

        var seconds = engine.SamplePosition / slot.Data.SampleRate;

        return $"{slot.Name} · at {seconds.ToString("0.00", inv)} s of {slot.Data.Seconds.ToString("0.00", inv)} s";
    }

    private static string StretchLine(NoumenonEngine engine, CultureInfo inv)
    {
        var points = SpectralStretcher.FrameSizeFor((StretchWindow)engine.Parameters.GetInt(ParamId.StretchWindow), engine.SampleRate);
        var ms = points / engine.SampleRate * 1000.0;

        return $"{ms.ToString("0", inv)} ms window ({points} points)";
    }
}
