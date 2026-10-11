using System.Globalization;
using Noumenon.Engine;
using Noumenon.Engine.Parameters;
using Noumenon.Ui.Widgets;

namespace Noumenon.Ui.Pages;

/// <summary>The POST tab: the Fabrications-style post section after Volume · Width — filter, EQ, the two-band limiter and the post gain — with the output pair repeated for convenience.</summary>
internal static class PostPage
{
    private const float Top = 28f;

    public static PageContent Build(NoumenonEngine engine)
    {
        var inv = CultureInfo.InvariantCulture;
        var w = new List<Widget>();

        w.Add(new FrameWidget(4, Top, 250, 130, null));
        w.Add(new ToggleWidget(engine, ParamId.PostFilterOn, 10, Top + 4, 10, 10, "", ToggleStyle.Led));
        w.Add(new LabelWidget("POST FILTER", 22, Top + 2, 120, 13, EditorTheme.Left, EditorTheme.TitleFont));
        w.Add(new KnobWidget(engine, ParamId.PostFilterMorph, 16, Top + 40, 32, "LP-HP"));
        w.Add(new LabelWidget("CUT", 70, Top + 18, 28, 11));
        w.Add(new FaderWidget(engine, ParamId.PostFilterCutoff, 80, Top + 30, 70));
        w.Add(new LabelWidget("RES", 104, Top + 18, 28, 11));
        w.Add(new FaderWidget(engine, ParamId.PostFilterReso, 114, Top + 30, 70));
        w.Add(new KnobWidget(engine, ParamId.PostFilterMix, 150, Top + 40, 32, "MIX"));

        w.Add(new FrameWidget(260, Top, 130, 130, "POST EQ"));
        w.Add(new KnobWidget(engine, ParamId.PostEqHigh, 272, Top + 40, 32, "HI"));
        w.Add(new KnobWidget(engine, ParamId.PostEqLow, 328, Top + 40, 32, "LOW"));

        w.Add(new FrameWidget(396, Top, 380, 130, null));
        w.Add(new ToggleWidget(engine, ParamId.PostLimiterOn, 402, Top + 4, 10, 10, "", ToggleStyle.Led));
        w.Add(new LabelWidget("TWO-BAND LIMITER", 414, Top + 2, 160, 13, EditorTheme.Left, EditorTheme.TitleFont));
        w.Add(new LabelWidget("LOW", 404, Top + 18, 30, 11));
        w.Add(new FaderWidget(engine, ParamId.PostLimiterLowThreshold, 415, Top + 30, 70));
        w.Add(new LabelWidget("HIGH", 438, Top + 18, 30, 11));
        w.Add(new FaderWidget(engine, ParamId.PostLimiterHighThreshold, 449, Top + 30, 70));
        w.Add(new KnobWidget(engine, ParamId.PostLimiterSplit, 484, Top + 40, 32, "SPLIT"));
        w.Add(new KnobWidget(engine, ParamId.PostLimiterGain, 540, Top + 40, 32, "GAIN"));
        w.Add(new LabelWidget("RELEASE", 596, Top + 22, 160, 11, EditorTheme.Left));
        w.Add(new ChoiceButtonsWidget(engine, ParamId.PostLimiterSpeed, 596, Top + 36, 170, 14));
        w.Add(new LabelWidget("LOW / HIGH: thresholds per band", 596, Top + 60, 176, 12, EditorTheme.Left, EditorTheme.SmallFont));
        w.Add(new LabelWidget("SPLIT: the crossover frequency", 596, Top + 74, 176, 12, EditorTheme.Left, EditorTheme.SmallFont));
        w.Add(new MeterWidget(() => engine.LimiterReductionLowDb, 596, Top + 94, 170, 8, MeterScale.Reduction, "LO"));
        w.Add(new MeterWidget(() => engine.LimiterReductionHighDb, 596, Top + 106, 170, 8, MeterScale.Reduction, "HI"));

        w.Add(new FrameWidget(782, Top, 214, 130, "POST VOLUME  ·  OUTPUT"));
        w.Add(new KnobWidget(engine, ParamId.PostVolume, 796, Top + 40, 36, "POST"));
        w.Add(new KnobWidget(engine, ParamId.OutputVolume, 864, Top + 40, 36, "VOLUME"));
        w.Add(new KnobWidget(engine, ParamId.OutputWidth, 932, Top + 40, 36, "WIDTH"));

        w.Add(new FrameWidget(4, Top + 140, 992, 60, null, inset: true));
        w.Add(new LabelWidget("Signal path:  sections → combine → × AM → + sampler → gate → DC → master filter → distortion → EQ → drift → [scope] → resonator / reverb → volume · width → post filter → post EQ → limiter → post volume → safety limiter", 14, Top + 146, 972, 16, EditorTheme.Left, EditorTheme.SmallFont));
        w.Add(new DisplayTextWidget(() => $"engine {engine.HostSampleRate.ToString("0", inv)} Hz × {engine.Oversampling}  ·  latency {engine.LatencySamples} samples  ·  scale {engine.ActiveScale.Name}  ·  seed {engine.Seed}", 14, Top + 170, 972, 16, font: EditorTheme.SmallValueFont, color: EditorTheme.TextOnDark, format: EditorTheme.Left));

        return new PageContent(w, null);
    }
}
