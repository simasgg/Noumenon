using System.Globalization;
using Noumenon.Engine;
using Noumenon.Engine.Parameters;
using Noumenon.Ui.Widgets;

namespace Noumenon.Ui.Pages;

/// <summary>
/// The MOD tab: everything the main panel has no room for — the three LFOs, the envelope
/// follower, the macros, the twelve-slot modulation matrix, the Resochord voices and user chords,
/// play mode, scale and tuning, host sync and engine rate, the Phase 9 sample-mode parameters —
/// so every parameter in the table is reachable from the editor.
/// </summary>
internal static class ModPage
{
    private const float Top = 28f;

    public static PageContent Build(NoumenonEngine engine)
    {
        var inv = CultureInfo.InvariantCulture;
        var w = new List<Widget>();

        for (var i = 0; i < ParameterTable.LfoCount; i++)
        {
            var x = 4 + i * 150f;
            w.Add(new FrameWidget(x, Top, 146, 96, $"LFO {i + 1}"));
            w.Add(new KnobWidget(engine, ParameterTable.Lfo(i, LfoParam.Rate), x + 12, Top + 30, 26, "RATE"));
            w.Add(new KnobWidget(engine, ParameterTable.Lfo(i, LfoParam.Depth), x + 56, Top + 30, 26, "DEPTH"));
            w.Add(new MenuWidget(engine, ParameterTable.Lfo(i, LfoParam.Shape), x + 10, Top + 68, 128));
        }

        w.Add(new FrameWidget(454, Top, 150, 96, "ENV FOLLOWER"));
        w.Add(new KnobWidget(engine, ParamId.EnvFollowerAttack, 466, Top + 30, 26, "ATTACK"));
        w.Add(new KnobWidget(engine, ParamId.EnvFollowerRelease, 514, Top + 30, 26, "RELEASE"));
        w.Add(new DisplayTextWidget(() => "level " + engine.EnvelopeFollowerValue.ToString("0.000", inv), 464, Top + 68, 130, 13, font: EditorTheme.SmallValueFont, color: EditorTheme.TextOnDark));

        w.Add(new FrameWidget(608, Top, 200, 96, "MACROS"));
        for (var i = 0; i < 4; i++)
            w.Add(new KnobWidget(engine, ParamId.Macro1 + i, 618 + i * 48, Top + 34, 26, $"M{i + 1}"));

        w.Add(new FrameWidget(812, Top, 184, 96, "PLAY MODE"));
        w.Add(new ChoiceButtonsWidget(engine, ParamId.PlayMode, 822, Top + 20, 100, 14));
        w.Add(new LedWidget(() => engine.Midi.HeldCount > 0, 934, Top + 23, 8, "GATE"));
        w.Add(new KnobWidget(engine, ParamId.GateAttack, 826, Top + 56, 24, "ATTACK"));
        w.Add(new KnobWidget(engine, ParamId.GateRelease, 872, Top + 56, 24, "RELEASE"));
        w.Add(new DisplayTextWidget(() => engine.GateEnvelopeValue.ToString("0.00", inv), 920, Top + 60, 60, 13, font: EditorTheme.SmallValueFont, color: EditorTheme.TextOnDark));

        w.Add(new FrameWidget(4, Top + 102, 420, 360, "MODULATION MATRIX"));
        w.Add(new LabelWidget("SOURCE", 30, Top + 118, 96, 11, EditorTheme.Left, EditorTheme.SmallFont));
        w.Add(new LabelWidget("TARGET", 132, Top + 118, 200, 11, EditorTheme.Left, EditorTheme.SmallFont));
        w.Add(new LabelWidget("DEPTH", 338, Top + 118, 40, 11, EditorTheme.Left, EditorTheme.SmallFont));
        for (var slot = 0; slot < ParameterTable.ModSlotCount; slot++)
        {
            var y = Top + 132 + slot * 27f;
            w.Add(new LabelWidget((slot + 1).ToString(inv), 10, y, 18, 14, EditorTheme.Center, EditorTheme.SmallFont));
            w.Add(new MenuWidget(engine, ParameterTable.ModSlot(slot, ModSlotParam.Source), 30, y, 96));
            w.Add(new MenuWidget(engine, ParameterTable.ModSlot(slot, ModSlotParam.Target), 132, y, 200));
            w.Add(new KnobWidget(engine, ParameterTable.ModSlot(slot, ModSlotParam.Depth), 346, y - 4, 22));
            w.Add(new NumberWidget(engine, ParameterTable.ModSlot(slot, ModSlotParam.Depth), 374, y + 1, 42));
        }

        w.Add(new FrameWidget(430, Top + 102, 300, 228, "RESOCHORD VOICES"));
        w.Add(new LabelWidget("NOTE", 454, Top + 118, 40, 11, EditorTheme.Center, EditorTheme.SmallFont));
        w.Add(new LabelWidget("FB", 498, Top + 118, 30, 11, EditorTheme.Center, EditorTheme.SmallFont));
        w.Add(new LabelWidget("DAMP", 544, Top + 118, 30, 11, EditorTheme.Center, EditorTheme.SmallFont));
        w.Add(new LabelWidget("LEVEL", 590, Top + 118, 30, 11, EditorTheme.Center, EditorTheme.SmallFont));
        w.Add(new LabelWidget("PAN", 636, Top + 118, 30, 11, EditorTheme.Center, EditorTheme.SmallFont));
        w.Add(new LabelWidget("RINGS", 668, Top + 118, 56, 11, EditorTheme.Center, EditorTheme.SmallFont));
        for (var v = 0; v < ParameterTable.ResoVoiceCount; v++)
        {
            var y = Top + 132 + v * 30f;
            var voice = v;
            w.Add(new LabelWidget((v + 1).ToString(inv), 436, y, 16, 14, EditorTheme.Center, EditorTheme.SmallFont));
            w.Add(new NumberWidget(engine, ParameterTable.ResoVoice(v, ResoVoiceParam.Note), 454, y + 1, 40));
            w.Add(new KnobWidget(engine, ParameterTable.ResoVoice(v, ResoVoiceParam.Feedback), 502, y - 4, 22));
            w.Add(new KnobWidget(engine, ParameterTable.ResoVoice(v, ResoVoiceParam.Damping), 548, y - 4, 22));
            w.Add(new KnobWidget(engine, ParameterTable.ResoVoice(v, ResoVoiceParam.Level), 594, y - 4, 22));
            w.Add(new KnobWidget(engine, ParameterTable.ResoVoice(v, ResoVoiceParam.Pan), 640, y - 4, 22));
            w.Add(new DisplayTextWidget(() => RingingNote(engine, voice), 670, y + 1, 52, 12, font: EditorTheme.SmallValueFont, tooltip: "The note this voice is ringing at: MIDI, chord select, the chord list or the Note above, then the scale"));
        }

        w.Add(new FrameWidget(430, Top + 336, 300, 126, "CHORDS"));
        w.Add(new LabelWidget("CHORD", 440, Top + 352, 44, 14, EditorTheme.Left));
        w.Add(new MenuWidget(engine, ParamId.ResochordChord, 484, Top + 352, 120));
        w.Add(new LabelWidget("SELECT", 612, Top + 352, 44, 14, EditorTheme.Left));
        w.Add(new MenuWidget(engine, ParamId.ResochordChordSelect, 656, Top + 352, 64));
        w.Add(new ToggleWidget(engine, ParamId.ResochordMidi, 440, Top + 374, 70, 14, "RESO MIDI"));
        w.Add(new TextButtonWidget("FORGET VOICES", 516, Top + 374, 90, 14, engine.Midi.ClearVoiceNotes, tooltip: "Forget the notes ResoMidi assigned, so the next chord takes all six voices"));
        w.Add(new LabelWidget("STORE RINGING NOTES AS USER CHORD", 440, Top + 396, 280, 11, EditorTheme.Left, EditorTheme.SmallFont));
        for (var slot = 0; slot < NoumenonEngine.UserChordCount; slot++)
        {
            var index = slot;
            w.Add(new TextButtonWidget((slot + 1).ToString(inv), 440 + slot * 35f, Top + 410, 30, 14, () => engine.StoreChord(index), tooltip: $"Store as User {slot + 1} (chord list entry {ParameterTable.ResochordChords.Count - NoumenonEngine.UserChordCount + slot + 1})"));
        }

        w.Add(new DisplayTextWidget(() => UserChordText(engine), 440, Top + 432, 280, 13, font: EditorTheme.SmallValueFont, color: EditorTheme.TextOnDark, tooltip: "The user chord the chord list currently selects, if any"));

        w.Add(new FrameWidget(736, Top + 102, 260, 100, "SCALE  ·  TUNING  ·  HOST"));
        w.Add(new MenuWidget(engine, ParamId.Scale, 746, Top + 122, 118));
        w.Add(new MenuWidget(engine, ParamId.ScaleRoot, 870, Top + 122, 48));
        w.Add(new KnobWidget(engine, ParamId.TuningA4, 934, Top + 118, 24, "A4", labelBelow: true));
        w.Add(new ToggleWidget(engine, ParamId.HostSync, 746, Top + 146, 70, 13, "HOST SYNC"));
        w.Add(new ToggleWidget(engine, ParamId.RestartOnHostPlay, 822, Top + 146, 96, 13, "RESTART ON PLAY"));
        w.Add(new LabelWidget("ENGINE RATE", 746, Top + 168, 70, 13, EditorTheme.Left));
        w.Add(new ChoiceButtonsWidget(engine, ParamId.EngineRate, 822, Top + 168, 70, 13));
        w.Add(new LabelWidget("(restarts the stream)", 898, Top + 168, 94, 13, EditorTheme.Left, EditorTheme.SmallFont));

        w.Add(new FrameWidget(736, Top + 208, 260, 122, "SAMPLE MODES"));
        w.Add(new KnobWidget(engine, ParamId.GrainSize, 746, Top + 238, 24, "SIZE"));
        w.Add(new KnobWidget(engine, ParamId.GrainDensity, 792, Top + 238, 24, "DENSITY"));
        w.Add(new KnobWidget(engine, ParamId.GrainPosition, 840, Top + 238, 24, "POS"));
        w.Add(new KnobWidget(engine, ParamId.GrainSpray, 886, Top + 238, 24, "SPRAY"));
        w.Add(new KnobWidget(engine, ParamId.StretchAmount, 932, Top + 238, 24, "STRETCH"));
        w.Add(new LabelWidget("MODE", 746, Top + 278, 40, 14, EditorTheme.Left));
        w.Add(new MenuWidget(engine, ParamId.SamplerMode, 790, Top + 278, 100));
        w.Add(new LabelWidget("Granular and Stretch arrive in Phase 9; Classic ignores these.", 746, Top + 298, 244, 24, EditorTheme.Left, EditorTheme.SmallFont));

        w.Add(new FrameWidget(736, Top + 336, 260, 126, "KEYS", inset: true));
        w.Add(new LabelWidget("Space  restart lanes      R  randomize      M  mute", 746, Top + 354, 244, 12, EditorTheme.Left, EditorTheme.SmallFont));
        w.Add(new LabelWidget("Ctrl+Z / Ctrl+Y  undo / redo", 746, Top + 368, 244, 12, EditorTheme.Left, EditorTheme.SmallFont));
        w.Add(new LabelWidget("Fader: drag · wheel · double-click = default", 746, Top + 386, 244, 12, EditorTheme.Left, EditorTheme.SmallFont));
        w.Add(new LabelWidget("Lane: click the small display to arm, drag to record,", 746, Top + 400, 244, 12, EditorTheme.Left, EditorTheme.SmallFont));
        w.Add(new LabelWidget("release, then the button plays the loop. Right-click: menu.", 746, Top + 414, 244, 12, EditorTheme.Left, EditorTheme.SmallFont));
        w.Add(new LabelWidget("Scope: drag = X/Y phase, wheel = zoom, double-click = reset", 746, Top + 432, 244, 12, EditorTheme.Left, EditorTheme.SmallFont));

        return new PageContent(w, null);
    }

    private static string RingingNote(NoumenonEngine engine, int voice)
    {
        var note = engine.ResochordNote(voice);
        var nearest = Math.Clamp((int)MathF.Round(note), 0, 127);
        var cents = (int)MathF.Round((note - nearest) * 100f);

        return cents == 0 ? ParamInfo.NoteName(nearest) : $"{ParamInfo.NoteName(nearest)}{cents:+0;-0}";
    }

    private static string UserChordText(NoumenonEngine engine)
    {
        var chord = engine.Parameters.GetInt(ParamId.ResochordChord);
        var firstUser = ParameterTable.ResochordChords.Count - NoumenonEngine.UserChordCount;
        if (chord < firstUser)
            return "chord list: " + ParameterTable.ResochordChords[Math.Clamp(chord, 0, ParameterTable.ResochordChords.Count - 1)];

        var notes = engine.UserChord(chord - firstUser);

        return $"User {chord - firstUser + 1}: " + string.Join(" ", notes.Select(n => ParamInfo.NoteName(Math.Clamp((int)MathF.Round(n), 0, 127))));
    }
}
