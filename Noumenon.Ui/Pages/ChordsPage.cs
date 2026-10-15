using System.Globalization;
using Noumenon.Engine;
using Noumenon.Engine.Parameters;
using Noumenon.Ui.Widgets;

namespace Noumenon.Ui.Pages;

/// <summary>
/// The CHORDS tab: harmony and how notes play. The resonator's six voices (note, feedback,
/// damping, level, pan, and the note each rings at right now), the chord list with its user
/// memories — filled by hand from the ringing notes or all at once from the scale — the scale,
/// root and A4, the play mode's gate envelope, and the chord sequencer: up to eight steps, each a
/// degree of the scale, stepping on the lane clock, its chord built in the scale with the chosen
/// type, voicing and octave, and its root optionally driving P1 or P2.
/// </summary>
internal static class ChordsPage
{
    private const float Top = 28f;
    private const float UpperHeight = 234f;
    private const float SequencerTop = 268f;
    private const float CellWidth = 116f;
    private const float CellStep = 122f;

    public static PageContent Build(NoumenonEngine engine)
    {
        var inv = CultureInfo.InvariantCulture;
        var w = new List<Widget>();

        // The resonator's voices.
        w.Add(new FrameWidget(4, Top, 400, UpperHeight, "RESONATOR VOICES"));
        w.Add(new LabelWidget("NOTE", 30, Top + 18, 40, 11, EditorTheme.Center, EditorTheme.SmallFont));
        w.Add(new LabelWidget("FB", 86, Top + 18, 30, 11, EditorTheme.Center, EditorTheme.SmallFont));
        w.Add(new LabelWidget("DAMP", 142, Top + 18, 30, 11, EditorTheme.Center, EditorTheme.SmallFont));
        w.Add(new LabelWidget("LEVEL", 198, Top + 18, 30, 11, EditorTheme.Center, EditorTheme.SmallFont));
        w.Add(new LabelWidget("PAN", 254, Top + 18, 30, 11, EditorTheme.Center, EditorTheme.SmallFont));
        w.Add(new LabelWidget("RINGS", 300, Top + 18, 92, 11, EditorTheme.Center, EditorTheme.SmallFont));
        for (var v = 0; v < ParameterTable.ResoVoiceCount; v++)
        {
            var y = Top + 34 + v * 33f;   // leaves the knobs' value strip between the rows
            var voice = v;
            w.Add(new LabelWidget((v + 1).ToString(inv), 10, y, 16, 14, EditorTheme.Center, EditorTheme.SmallFont));
            w.Add(new NumberWidget(engine, ParameterTable.ResoVoice(v, ResoVoiceParam.Note), 30, y + 1, 40));
            w.Add(new KnobWidget(engine, ParameterTable.ResoVoice(v, ResoVoiceParam.Feedback), 90, y - 4, 22));
            w.Add(new KnobWidget(engine, ParameterTable.ResoVoice(v, ResoVoiceParam.Damping), 146, y - 4, 22));
            w.Add(new KnobWidget(engine, ParameterTable.ResoVoice(v, ResoVoiceParam.Level), 202, y - 4, 22));
            w.Add(new KnobWidget(engine, ParameterTable.ResoVoice(v, ResoVoiceParam.Pan), 258, y - 4, 22));
            w.Add(new DisplayTextWidget(() => Name(engine.ResochordNote(voice)), 304, y + 1, 84, 12, font: EditorTheme.SmallValueFont, tooltip: "The note this voice is ringing at: MIDI, the sequencer, chord select, the chord list or the Note, then the scale"));
        }

        // The chord list and the user memories.
        var cx = 410f;
        w.Add(new FrameWidget(cx, Top, 300, UpperHeight, "CHORDS"));
        w.Add(new LabelWidget("CHORD", cx + 10, Top + 20, 44, 14, EditorTheme.Left));
        w.Add(new MenuWidget(engine, ParamId.ResochordChord, cx + 54, Top + 20, 120));
        w.Add(new LabelWidget("SELECT", cx + 182, Top + 20, 44, 14, EditorTheme.Left));
        w.Add(new MenuWidget(engine, ParamId.ResochordChordSelect, cx + 226, Top + 20, 64));
        w.Add(new ToggleWidget(engine, ParamId.ResochordMidi, cx + 10, Top + 42, 70, 14, "RESO MIDI"));
        w.Add(new TextButtonWidget("FORGET VOICES", cx + 86, Top + 42, 90, 14, engine.Midi.ClearVoiceNotes, tooltip: "Forget the notes ResoMidi assigned, so the next chord takes all six voices"));
        w.Add(new LabelWidget("STORE THE RINGING NOTES AS USER CHORD", cx + 10, Top + 66, 280, 11, EditorTheme.Left, EditorTheme.SmallFont));
        for (var slot = 0; slot < NoumenonEngine.UserChordCount; slot++)
        {
            var index = slot;
            w.Add(new TextButtonWidget((slot + 1).ToString(inv), cx + 10 + slot * 35f, Top + 80, 30, 14, () => engine.StoreChord(index), tooltip: $"Store as User {slot + 1} (chord list entry {ParameterTable.ResochordChords.Count - NoumenonEngine.UserChordCount + slot + 1})"));
        }

        w.Add(new DisplayTextWidget(() => UserChordText(engine), cx + 10, Top + 102, 280, 13, font: EditorTheme.SmallValueFont, color: EditorTheme.TextOnDark, tooltip: "The user chord the chord list currently selects, if any"));
        w.Add(new LabelWidget("FROM THE SCALE", cx + 10, Top + 128, 280, 11, EditorTheme.Left, EditorTheme.SmallFont));
        w.Add(new TextButtonWidget("FILL USER CHORDS WITH THE KEY'S CHORDS", cx + 10, Top + 142, 280, 16, engine.FillUserChordsFromScale, tooltip: "User 1–7 become the chords on degrees I–VII of the scale (User 8: I an octave up), with the sequencer's type, voicing and octave"));
        w.Add(new LabelWidget("Then CHORD picks them, or SELECT P1/P2: keys C#3… play", cx + 10, Top + 164, 280, 12, EditorTheme.Left, EditorTheme.SmallFont, EditorTheme.TextDim));
        w.Add(new LabelWidget("chords 1, 2, … of the list (the user ones follow the presets).", cx + 10, Top + 178, 280, 12, EditorTheme.Left, EditorTheme.SmallFont, EditorTheme.TextDim));
        w.Add(new ResonatorStripWidget(engine, cx + 10, Top + 196, 280, 30));

        // Scale and tuning.
        var sx = 716f;
        w.Add(new FrameWidget(sx, Top, 280, 110, "SCALE  ·  TUNING"));
        w.Add(new MenuWidget(engine, ParamId.Scale, sx + 10, Top + 22, 118));
        w.Add(new MenuWidget(engine, ParamId.ScaleRoot, sx + 134, Top + 22, 48));
        w.Add(new KnobWidget(engine, ParamId.TuningA4, sx + 214, Top + 18, 26, "A4", labelBelow: true));
        w.Add(new DisplayTextWidget(() => $"{engine.ActiveScale.Name} on {ParameterTable.RootNotes[Math.Clamp(engine.Parameters.GetInt(ParamId.ScaleRoot), 0, 11)]} · {engine.ActiveScale.DegreesCents.Count} degrees", sx + 10, Top + 46, 172, 13, font: EditorTheme.SmallValueFont, color: EditorTheme.TextOnDark, format: EditorTheme.Left));
        w.Add(new LabelWidget("The scale quantizes the oscillators, P1/P2's MIDI notes", sx + 10, Top + 68, 264, 12, EditorTheme.Left, EditorTheme.SmallFont, EditorTheme.TextDim));
        w.Add(new LabelWidget("and the resonator, and is the sequencer's harmony.", sx + 10, Top + 82, 264, 12, EditorTheme.Left, EditorTheme.SmallFont, EditorTheme.TextDim));

        // Play mode.
        w.Add(new FrameWidget(sx, Top + 116, 280, 118, "PLAY MODE"));
        w.Add(new ChoiceButtonsWidget(engine, ParamId.PlayMode, sx + 10, Top + 136, 120, 14));
        w.Add(new LedWidget(() => engine.Midi.HeldCount > 0, sx + 142, Top + 139, 8, "GATE"));
        w.Add(new KnobWidget(engine, ParamId.GateAttack, sx + 16, Top + 176, 26, "ATTACK"));
        w.Add(new KnobWidget(engine, ParamId.GateRelease, sx + 70, Top + 176, 26, "RELEASE"));
        w.Add(new DisplayTextWidget(() => "envelope " + engine.GateEnvelopeValue.ToString("0.00", inv), sx + 124, Top + 184, 100, 13, font: EditorTheme.SmallValueFont, color: EditorTheme.TextOnDark));
        w.Add(new LabelWidget("Gated: notes open the sound, release closes it.", sx + 10, Top + 214, 264, 12, EditorTheme.Left, EditorTheme.SmallFont, EditorTheme.TextDim));

        // The chord sequencer.
        var t = SequencerTop;
        w.Add(new FrameWidget(4, t, 992, 392, null));
        w.Add(new ToggleWidget(engine, ParamId.ChordSeqOn, 12, t + 6, 10, 10, "", ToggleStyle.Led));
        w.Add(new LabelWidget("CHORD SEQUENCER", 26, t + 4, 160, 13, EditorTheme.Left, EditorTheme.TitleFont, EditorTheme.TextDim));
        w.Add(new NumberWidget(engine, ParamId.ChordSeqSteps, 64, t + 30, 30, 12, "STEPS", 50));
        w.Add(new KnobWidget(engine, ParamId.ChordSeqStepTime, 112, t + 26, 26, "STEP TIME", labelBelow: true, labelWidth: 60));
        w.Add(new LabelWidget("TYPE", 166, t + 30, 30, 12, EditorTheme.Right, EditorTheme.SmallFont, EditorTheme.TextDim));
        w.Add(new MenuWidget(engine, ParamId.ChordSeqType, 200, t + 30, 82, 12));
        w.Add(new LabelWidget("VOICING", 290, t + 30, 44, 12, EditorTheme.Right, EditorTheme.SmallFont, EditorTheme.TextDim));
        w.Add(new ChoiceButtonsWidget(engine, ParamId.ChordSeqVoicing, 338, t + 29, 150, 14, ["CLOSE", "OPEN", "WIDE"]));
        w.Add(new NumberWidget(engine, ParamId.ChordSeqOctave, 546, t + 30, 30, 12, "OCTAVE", 50));
        w.Add(new LabelWidget("ROOT TO", 584, t + 30, 46, 12, EditorTheme.Right, EditorTheme.SmallFont, EditorTheme.TextDim));
        w.Add(new ChoiceButtonsWidget(engine, ParamId.ChordSeqRoot, 634, t + 29, 120, 14, ["—", "P1", "P2"]));
        w.Add(new DisplayTextWidget(() => SequencerLine(engine, inv), 764, t + 29, 224, 14, font: EditorTheme.SmallValueFont, color: EditorTheme.TextOnDark, tooltip: "Where the sequencer is"));

        for (var step = 0; step < ParameterTable.ChordSeqStepCount; step++)
        {
            var x = 12 + step * CellStep;
            w.Add(new LabelWidget($"STEP {step + 1}", x, t + 70, 50, 12, EditorTheme.Left, EditorTheme.SmallFont, EditorTheme.TextDim));
            w.Add(new MenuWidget(engine, ParameterTable.ChordSeqStep(step), x + 54, t + 69, CellWidth - 54, 13));
            w.Add(new ChordCellWidget(engine, step, x, t + 88, CellWidth, 70));
        }

        w.Add(new LabelWidget("Each step is a degree of the scale (Hold keeps the chord before it). Steps last STEP TIME at the lane speed, whole beats with Host Sync,", 12, t + 172, 976, 12, EditorTheme.Left, EditorTheme.SmallFont, EditorTheme.TextDim));
        w.Add(new LabelWidget("and go back to step 1 with every lane restart. While it runs its chord replaces the chord list; ResoMidi still wins per voice.", 12, t + 186, 976, 12, EditorTheme.Left, EditorTheme.SmallFont, EditorTheme.TextDim));
        w.Add(new LabelWidget("RINGING NOW", 12, t + 212, 120, 12, EditorTheme.Left, EditorTheme.SmallFont, EditorTheme.TextDim));
        w.Add(new ResonatorStripWidget(engine, 12, t + 228, 976, 60));
        w.Add(new DisplayTextWidget(() => RingingLine(engine), 12, t + 296, 976, 14, font: EditorTheme.SmallValueFont, color: EditorTheme.TextOnDark, format: EditorTheme.Left));
        w.Add(new LabelWidget("Try: Scale Minor, root A, TYPE Seventh, VOICING Open, ROOT TO P1 with the oscillators routed to P1 — the drone follows the progression.", 12, t + 322, 976, 12, EditorTheme.Left, EditorTheme.SmallFont, EditorTheme.TextDim));
        w.Add(new LabelWidget("The resonator needs MIX up (MAIN) to be heard; feed it something with sustain.", 12, t + 336, 976, 12, EditorTheme.Left, EditorTheme.SmallFont, EditorTheme.TextDim));

        return new PageContent(w, null);
    }

    private static string SequencerLine(NoumenonEngine engine, CultureInfo inv)
    {
        if (!engine.Parameters.GetBool(ParamId.ChordSeqOn))
            return "off";

        var steps = engine.Parameters.GetInt(ParamId.ChordSeqSteps);

        return $"step {engine.Sequencer.Step + 1} of {steps} · {(engine.Sequencer.Progress * 100f).ToString("0", inv)} %";
    }

    private static string RingingLine(NoumenonEngine engine)
    {
        var names = new string[ParameterTable.ResoVoiceCount];
        for (var v = 0; v < names.Length; v++)
            names[v] = Name(engine.ResochordNote(v));

        return string.Join("   ", names);
    }

    private static string Name(float note)
    {
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

        return $"User {chord - firstUser + 1}: " + string.Join(" ", notes.Select(Name));
    }
}
