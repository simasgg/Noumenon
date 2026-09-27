namespace Noumenon.Engine.Parameters;

public enum SectionId
{
    A = 0,
    B = 1,
}

/// <summary>The ten parameters of one oscillator slot, in <see cref="ParamId"/> order.</summary>
public enum OscParam
{
    On,
    Wave,
    Semitone,
    Fine,
    Shape,
    NoiseCutoff,
    Level,
    Trim,
    P1Route,
    P2Route,
}

/// <summary>The nine per-section parameters, in <see cref="ParamId"/> order.</summary>
public enum SectionParam
{
    Cutoff,
    Reso,
    FilterType,
    Slave,
    Level,
    P1RouteAll,
    P2RouteAll,
    P1FineRange,
    P2FineRange,
}

public enum PitchParam
{
    Value,
    Midi,
    Glide,
    BendRange,
}

public enum ResoVoiceParam
{
    Note,
    Feedback,
    Damping,
    Level,
    Pan,
}

public enum LfoParam
{
    Rate,
    Shape,
    Depth,
}

public enum ModSlotParam
{
    Source,
    Target,
    Depth,
}

/// <summary>
/// The single source of truth for every parameter: one <see cref="ParamInfo"/> row per
/// <see cref="ParamId"/>, in enum order. The developer panel, the editor, the VST2/VST3 parameter
/// lists, the preset format and the randomizer all read from here. Repeated blocks are addressed
/// arithmetically (<see cref="Osc"/>, <see cref="Section"/>, …); the static constructor verifies
/// that the rows and the enum agree, so a table/enum mismatch fails at first touch, not in a host.
/// Defaults are the init patch — the sound the instrument makes the moment it opens — so the
/// oscillator rows carry a voicing rather than neutral zeros.
/// </summary>
public static class ParameterTable
{
    public const int SectionCount = 2;
    public const int SlotsPerSection = 6;
    public const int OscParamCount = 10;
    public const int SectionParamCount = 9;
    public const int PitchFaderCount = 2;
    public const int PitchParamCount = 4;
    public const int ResoVoiceCount = 6;
    public const int ResoVoiceParamCount = 5;
    public const int LfoCount = 3;
    public const int LfoParamCount = 3;
    public const int ModSlotCount = 12;
    public const int ModSlotParamCount = 3;

    /// <summary>Choice names for <see cref="OscParam.Wave"/>; the order is the <c>Noumenon.Dsp.Oscillators.Waveform</c> enum.</summary>
    public static readonly IReadOnlyList<string> Waveforms = ["Sine", "Triangle", "Saw", "Square", "Pulse", "Bipolar Pulse", "LP Noise", "S&H Noise"];

    /// <summary>Choice names for the section and master filter modes; the order is the <c>Noumenon.Dsp.Filters.FilterType</c> enum.</summary>
    public static readonly IReadOnlyList<string> FilterTypes = ["LP", "BP", "HP"];

    public static readonly IReadOnlyList<string> SamplerModes = ["Classic", "Granular", "Stretch"];
    public static readonly IReadOnlyList<string> ModSources = ["None", "P1", "P2", "Matrix"];
    public static readonly IReadOnlyList<string> ChordSelectSources = ["None", "P1", "P2"];
    public static readonly IReadOnlyList<string> DistortionCurves = ["Tanh", "Fold", "Bit"];
    public static readonly IReadOnlyList<string> ReverbRooms = ["Room", "Chamber", "Hall", "Cathedral", "Plate", "Cloud", "Endless"];
    public static readonly IReadOnlyList<string> ReverbOrders = ["Pre", "Post"];
    public static readonly IReadOnlyList<string> LimiterSpeeds = ["Fast", "Medium", "Slow"];
    public static readonly IReadOnlyList<string> SpeedSteps = ["0.5x", "1x", "2x", "4x"];
    public static readonly IReadOnlyList<string> RandomizeScopes = ["All", "Oscillators", "Sampler", "Effects", "Lanes"];
    public static readonly IReadOnlyList<string> PlayModes = ["Free-run", "Gated"];
    public static readonly IReadOnlyList<string> Scales = ["12-TET", "Major", "Minor", "Pentatonic Major", "Pentatonic Minor", "Just Major", "Just Minor", "Whole Tone", "User"];
    public static readonly IReadOnlyList<string> RootNotes = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
    public static readonly IReadOnlyList<string> EngineRates = ["1x", "2x"];
    public static readonly IReadOnlyList<string> LfoShapes = ["Sine", "Triangle", "Saw", "Square", "S&H", "Smooth Random", "Drift"];
    public static readonly IReadOnlyList<string> MatrixSources = ["None", "P1", "P2", "LFO 1", "LFO 2", "LFO 3", "Env Follower", "Velocity", "Aftertouch", "Mod Wheel", "CC", "Macro 1", "Macro 2", "Macro 3", "Macro 4"];

    // The init patch's oscillator voicing: a low root with fifths and octaves in A, the same notes a
    // few cents off in B so the sum beats slowly and the ring-mod path adds its grit. Slot layout
    // follows the MF panel (A: sine ×3, tri, bipolar pulse, LP noise; B: sine ×2, tri, pulse,
    // bipolar pulse, S&H noise).
    private static readonly int[][] DefaultWave = [[0, 0, 0, 1, 5, 6], [0, 0, 1, 4, 5, 7]];
    private static readonly int[][] DefaultSemitone = [[-12, 0, 7, 12, 0, 0], [-12, 0, -12, 12, 19, 0]];
    private static readonly float[][] DefaultFine = [[0f, 0f, 3f, -2f, 0f, 0f], [5f, 6f, 3f, 0f, 0f, 0f]];
    private static readonly float[][] DefaultLevel = [[0.6f, 0.5f, 0.35f, 0.3f, 0.15f, 0.08f], [0.5f, 0.5f, 0.3f, 0.12f, 0.1f, 0.06f]];

    private static readonly int[] DefaultResoNote = [48, 55, 60, 64, 67, 72];
    private static readonly float[] DefaultResoPan = [-0.6f, 0.6f, -0.3f, 0.3f, -0.8f, 0.8f];

    public static readonly IReadOnlyList<ParamInfo> All = Build();

    public static int Count => All.Count;

    public static ParamInfo Get(ParamId id) => All[(int)id];

    /// <summary>Looks a row up by its enum name (<c>OscA1Level</c>) or display name (<c>Osc A1 Level</c>), case-insensitively.</summary>
    public static ParamInfo? Find(string name)
    {
        foreach (var info in All)
        {
            if (string.Equals(info.Id.ToString(), name, StringComparison.OrdinalIgnoreCase) || string.Equals(info.Name, name, StringComparison.OrdinalIgnoreCase))
                return info;
        }

        return null;
    }

    /// <summary>A fresh array of every default, indexed by <see cref="ParamId"/>.</summary>
    public static float[] CreateDefaults()
    {
        var values = new float[All.Count];
        for (var i = 0; i < values.Length; i++)
            values[i] = All[i].Default;

        return values;
    }

    public static ParamId Osc(SectionId section, int slot, OscParam param) => ParamId.OscA1On + ((int)section * SlotsPerSection + slot) * OscParamCount + (int)param;

    public static ParamId Section(SectionId section, SectionParam param) => ParamId.SectionACutoff + (int)section * SectionParamCount + (int)param;

    /// <param name="fader">0 = P1, 1 = P2.</param>
    public static ParamId Pitch(int fader, PitchParam param) => ParamId.P1Value + fader * PitchParamCount + (int)param;

    public static ParamId ResoVoice(int voice, ResoVoiceParam param) => ParamId.ResoVoice1Note + voice * ResoVoiceParamCount + (int)param;

    public static ParamId Lfo(int index, LfoParam param) => ParamId.Lfo1Rate + index * LfoParamCount + (int)param;

    public static ParamId ModSlot(int slot, ModSlotParam param) => ParamId.ModSlot1Source + slot * ModSlotParamCount + (int)param;

    private static IReadOnlyList<ParamInfo> Build()
    {
        var ids = Enum.GetValues<ParamId>();
        var rows = new List<ParamInfo>(ids.Length);

        var targetMax = ids.Length;   // 0 = none, k = ParamId k-1; a target list can only grow, like the enum

        for (var s = 0; s < SectionCount; s++)
        {
            var section = (SectionId)s;
            var group = section == SectionId.A ? ParamGroup.OscillatorA : ParamGroup.OscillatorB;
            for (var slot = 0; slot < SlotsPerSection; slot++)
            {
                var tag = $"{section}{slot + 1}";
                rows.Add(Toggle(Osc(section, slot, OscParam.On), group, $"Osc {tag} On", $"{tag} On", true, ParamFlags.Addition));
                rows.Add(Choice(Osc(section, slot, OscParam.Wave), group, $"Osc {tag} Wave", $"{tag} Wave", Waveforms, DefaultWave[s][slot], ParamFlags.Addition));
                rows.Add(Integer(Osc(section, slot, OscParam.Semitone), group, $"Osc {tag} Semitone", $"{tag} Semi", -64, 64, DefaultSemitone[s][slot], Units.Semitones, ParamFlags.Lane));
                rows.Add(Linear(Osc(section, slot, OscParam.Fine), group, $"Osc {tag} Fine", $"{tag} Fine", -100f, 100f, DefaultFine[s][slot], Units.Cents));
                rows.Add(Linear(Osc(section, slot, OscParam.Shape), group, $"Osc {tag} Shape", $"{tag} Shape", 0f, 1f, 0.5f, Units.None));
                rows.Add(Log(Osc(section, slot, OscParam.NoiseCutoff), group, $"Osc {tag} Noise Cutoff", $"{tag} Cut", 20f, 20000f, 2000f, Units.Hertz));
                rows.Add(Linear(Osc(section, slot, OscParam.Level), group, $"Osc {tag} Level", $"{tag} Level", 0f, 1f, DefaultLevel[s][slot], Units.None, ParamFlags.Lane));
                rows.Add(Log(Osc(section, slot, OscParam.Trim), group, $"Osc {tag} Trim", $"{tag} Trim", 0.25f, 4f, 1f, Units.Multiplier));
                rows.Add(Toggle(Osc(section, slot, OscParam.P1Route), group, $"Osc {tag} P1 Route", $"{tag} P1", true));
                rows.Add(Toggle(Osc(section, slot, OscParam.P2Route), group, $"Osc {tag} P2 Route", $"{tag} P2", true));
            }
        }

        for (var s = 0; s < SectionCount; s++)
        {
            var section = (SectionId)s;
            var group = section == SectionId.A ? ParamGroup.SectionA : ParamGroup.SectionB;
            rows.Add(Log(Section(section, SectionParam.Cutoff), group, $"Section {section} Cutoff", $"{section} Cutoff", 20f, 20000f, 8000f, Units.Hertz, ParamFlags.Lane));
            rows.Add(Linear(Section(section, SectionParam.Reso), group, $"Section {section} Reso", $"{section} Reso", 0f, 1f, 0.1f, Units.None, ParamFlags.Lane));
            rows.Add(Choice(Section(section, SectionParam.FilterType), group, $"Section {section} Filter Type", $"{section} Type", FilterTypes, 0, ParamFlags.Addition));
            rows.Add(Toggle(Section(section, SectionParam.Slave), group, $"Section {section} Slave", $"{section} Slave", false));
            rows.Add(Linear(Section(section, SectionParam.Level), group, $"Section {section} Level", $"{section} Level", 0f, 1f, 0.7f, Units.None, ParamFlags.Lane));
            rows.Add(Toggle(Section(section, SectionParam.P1RouteAll), group, $"Section {section} P1 All", $"{section} P1 All", false));
            rows.Add(Toggle(Section(section, SectionParam.P2RouteAll), group, $"Section {section} P2 All", $"{section} P2 All", false));
            rows.Add(Toggle(Section(section, SectionParam.P1FineRange), group, $"Section {section} P1 Fine Range", $"{section} P1Fine", false));
            rows.Add(Toggle(Section(section, SectionParam.P2FineRange), group, $"Section {section} P2 Fine Range", $"{section} P2Fine", false));
        }

        for (var p = 0; p < PitchFaderCount; p++)
        {
            var tag = $"P{p + 1}";
            rows.Add(Linear(Pitch(p, PitchParam.Value), ParamGroup.Pitch, $"{tag} Value", tag, -48f, 48f, 0f, Units.Semitones, ParamFlags.Lane));
            rows.Add(Toggle(Pitch(p, PitchParam.Midi), ParamGroup.Pitch, $"{tag} MIDI", $"{tag} MIDI", false));
            rows.Add(Linear(Pitch(p, PitchParam.Glide), ParamGroup.Pitch, $"{tag} Glide", $"{tag} Glide", 0f, 5f, 0.1f, Units.Seconds));
            rows.Add(Integer(Pitch(p, PitchParam.BendRange), ParamGroup.Pitch, $"{tag} Bend Range", $"{tag} Bend", 0, 24, 2, Units.Semitones));
        }

        rows.Add(Linear(ParamId.MixCrossfade, ParamGroup.Mix, "Mix Crossfade", "Xfade", 0f, 1f, 0.5f, Units.None, ParamFlags.Lane));
        rows.Add(Linear(ParamId.MixSumBalance, ParamGroup.Mix, "Mix A+B Balance", "A+B Bal", -1f, 1f, 0f, Units.None));
        rows.Add(Linear(ParamId.MixRingLevel, ParamGroup.Mix, "Mix AxB Level", "AxB Vol", 0f, 2f, 1f, Units.None));

        rows.Add(Integer(ParamId.SamplerSelect, ParamGroup.Sampler, "Sample Select", "Smp Sel", 0, 127, 0, Units.None));
        rows.Add(Choice(ParamId.SamplerMode, ParamGroup.Sampler, "Sample Mode", "Smp Mode", SamplerModes, 0, ParamFlags.Addition));
        rows.Add(Integer(ParamId.SamplerPitch, ParamGroup.Sampler, "Sample Pitch", "Smp Pit", -24, 24, 0, Units.Semitones, ParamFlags.Lane));
        rows.Add(Linear(ParamId.SamplerFine, ParamGroup.Sampler, "Sample Fine", "Smp Fine", -100f, 100f, 0f, Units.Cents));
        rows.Add(Choice(ParamId.SamplerModSource, ParamGroup.Sampler, "Sample Mod Source", "Smp Mod", ModSources, 2));
        rows.Add(Linear(ParamId.SamplerLoopStart, ParamGroup.Sampler, "Loop Start", "Loop St", 0f, 1f, 0f, Units.Percent));
        rows.Add(Linear(ParamId.SamplerLoopEnd, ParamGroup.Sampler, "Loop End", "Loop End", 0f, 1f, 1f, Units.Percent));
        rows.Add(Linear(ParamId.SamplerLoopCrossfade, ParamGroup.Sampler, "Loop Crossfade", "Loop XF", 0f, 500f, 10f, Units.Milliseconds, ParamFlags.Addition));
        rows.Add(Linear(ParamId.SamplerAmp, ParamGroup.Sampler, "Sample Amp", "Smp Amp", 0f, 1f, 0.5f, Units.None, ParamFlags.Lane));
        rows.Add(Linear(ParamId.SamplerDir, ParamGroup.Sampler, "Sample Direction", "Smp Dir", -2f, 2f, 1f, Units.Multiplier, ParamFlags.Lane));
        rows.Add(Linear(ParamId.SamplerMaster, ParamGroup.Sampler, "Sample Master", "Smp Mstr", 0f, 1f, 0.8f, Units.None));
        rows.Add(Toggle(ParamId.SamplerStereo, ParamGroup.Sampler, "Sample Stereo", "Smp Ster", true));
        rows.Add(Toggle(ParamId.SamplerRetrigger, ParamGroup.Sampler, "Sample Retrigger", "Smp Retr", false));
        rows.Add(Linear(ParamId.SamplerInputMix, ParamGroup.Sampler, "Sample/Input Mix", "Smp<>In", 0f, 1f, 0f, Units.None, ParamFlags.Lane));
        rows.Add(Linear(ParamId.SamplerAmDepth, ParamGroup.Sampler, "Sample AM Depth", "Smp AM", 0f, 1f, 0f, Units.None, ParamFlags.Addition));
        rows.Add(Log(ParamId.GrainSize, ParamGroup.Sampler, "Grain Size", "Grn Size", 5f, 500f, 80f, Units.Milliseconds, ParamFlags.Addition));
        rows.Add(Log(ParamId.GrainDensity, ParamGroup.Sampler, "Grain Density", "Grn Dens", 1f, 64f, 8f, Units.Hertz, ParamFlags.Addition));
        rows.Add(Linear(ParamId.GrainPosition, ParamGroup.Sampler, "Grain Position", "Grn Pos", 0f, 1f, 0f, Units.Percent, ParamFlags.Addition));
        rows.Add(Linear(ParamId.GrainSpray, ParamGroup.Sampler, "Grain Spray", "Grn Spry", 0f, 1f, 0.1f, Units.Percent, ParamFlags.Addition));
        rows.Add(Log(ParamId.StretchAmount, ParamGroup.Sampler, "Stretch Amount", "Stretch", 1f, 100f, 8f, Units.Multiplier, ParamFlags.Addition));

        rows.Add(Toggle(ParamId.MasterFilterOn, ParamGroup.MasterFilter, "Master Filter On", "MF On", true));
        rows.Add(Linear(ParamId.MasterFilterMorph, ParamGroup.MasterFilter, "Master Filter LP-HP", "MF LP-HP", 0f, 1f, 0f, Units.None));
        rows.Add(Log(ParamId.MasterFilterCutoff, ParamGroup.MasterFilter, "Master Filter Cutoff", "MF Cut", 20f, 20000f, 12000f, Units.Hertz, ParamFlags.Lane));
        rows.Add(Linear(ParamId.MasterFilterReso, ParamGroup.MasterFilter, "Master Filter Reso", "MF Res", 0f, 1f, 0.1f, Units.None, ParamFlags.Lane));
        rows.Add(Linear(ParamId.MasterFilterMix, ParamGroup.MasterFilter, "Master Filter Mix", "MF Mix", 0f, 1f, 1f, Units.None));

        rows.Add(Toggle(ParamId.DistortionOn, ParamGroup.Distortion, "Distortion On", "Dst On", true));
        rows.Add(Linear(ParamId.DistortionMix, ParamGroup.Distortion, "Distortion Mix", "Dst Mix", 0f, 1f, 0f, Units.None, ParamFlags.Lane));
        rows.Add(Linear(ParamId.DistortionDrive, ParamGroup.Distortion, "Distortion Drive", "Dst Drv", 0f, 1f, 0.2f, Units.None, ParamFlags.Lane));
        rows.Add(Choice(ParamId.DistortionCurve, ParamGroup.Distortion, "Distortion Curve", "Dst Crv", DistortionCurves, 0, ParamFlags.Addition));

        rows.Add(Linear(ParamId.EqHigh, ParamGroup.Eq, "EQ High", "EQ Hi", -12f, 12f, 0f, Units.Decibels));
        rows.Add(Linear(ParamId.EqLow, ParamGroup.Eq, "EQ Low", "EQ Low", -12f, 12f, 0f, Units.Decibels));

        rows.Add(Toggle(ParamId.SpinOn, ParamGroup.Spin, "Spin On", "Spin On", true));
        rows.Add(Log(ParamId.SpinTime1, ParamGroup.Spin, "Spin Time 1", "Spin T1", 0.1f, 500f, 120f, Units.Milliseconds, ParamFlags.Lane));
        rows.Add(Log(ParamId.SpinTime2, ParamGroup.Spin, "Spin Time 2", "Spin T2", 0.1f, 500f, 180f, Units.Milliseconds, ParamFlags.Lane));
        rows.Add(Linear(ParamId.SpinFine, ParamGroup.Spin, "Spin Fine", "Spin Fin", -1f, 1f, 0f, Units.Milliseconds, ParamFlags.Lane));
        rows.Add(Linear(ParamId.SpinFeedback, ParamGroup.Spin, "Spin Feedback", "Spin FB", 0f, 0.95f, 0.35f, Units.None, ParamFlags.Lane));
        rows.Add(Toggle(ParamId.SpinLink, ParamGroup.Spin, "Spin Link", "Spin Lnk", false));
        rows.Add(Linear(ParamId.SpinMix, ParamGroup.Spin, "Spin Mix", "Spin Mix", 0f, 1f, 0.3f, Units.None, ParamFlags.Addition));

        rows.Add(Toggle(ParamId.ResochordOn, ParamGroup.Resochord, "Resochord On", "Rc On", true));
        rows.Add(Integer(ParamId.ResochordChord, ParamGroup.Resochord, "Resochord Chord", "Rc Chord", 0, 63, 0, Units.None));
        rows.Add(Linear(ParamId.ResochordFeedback, ParamGroup.Resochord, "Resochord Feedback", "Rc FB", 0f, 0.99f, 0.5f, Units.None));
        rows.Add(Linear(ParamId.ResochordFeedbackFader, ParamGroup.Resochord, "Resochord Feedback Fader", "Rc FBFdr", 0f, 0.99f, 0f, Units.None, ParamFlags.Lane));
        rows.Add(Integer(ParamId.ResochordPitch, ParamGroup.Resochord, "Resochord Pitch", "Rc Pitch", -24, 24, 0, Units.Semitones));
        rows.Add(Linear(ParamId.ResochordFine, ParamGroup.Resochord, "Resochord Fine", "Rc Fine", -100f, 100f, 0f, Units.Cents));
        rows.Add(Choice(ParamId.ResochordModSource, ParamGroup.Resochord, "Resochord Mod Source", "Rc Mod", ModSources, 0));
        rows.Add(Linear(ParamId.ResochordMix, ParamGroup.Resochord, "Resochord Mix", "Rc Mix", 0f, 1f, 0.3f, Units.None, ParamFlags.Lane));
        rows.Add(Toggle(ParamId.ResochordMidi, ParamGroup.Resochord, "Resochord MIDI", "Rc MIDI", false));
        rows.Add(Choice(ParamId.ResochordChordSelect, ParamGroup.Resochord, "Resochord Chord Select", "Rc ChSel", ChordSelectSources, 0));
        for (var v = 0; v < ResoVoiceCount; v++)
        {
            var tag = $"Rc{v + 1}";
            rows.Add(Integer(ResoVoice(v, ResoVoiceParam.Note), ParamGroup.Resochord, $"Resochord Voice {v + 1} Note", $"{tag} Note", 0, 127, DefaultResoNote[v], Units.Note));
            rows.Add(Linear(ResoVoice(v, ResoVoiceParam.Feedback), ParamGroup.Resochord, $"Resochord Voice {v + 1} Feedback", $"{tag} FB", -0.99f, 0.99f, 0.9f, Units.None));
            rows.Add(Linear(ResoVoice(v, ResoVoiceParam.Damping), ParamGroup.Resochord, $"Resochord Voice {v + 1} Damping", $"{tag} Damp", 0f, 1f, 0.3f, Units.None));
            rows.Add(Linear(ResoVoice(v, ResoVoiceParam.Level), ParamGroup.Resochord, $"Resochord Voice {v + 1} Level", $"{tag} Lvl", 0f, 1f, 0.8f, Units.None));
            rows.Add(Linear(ResoVoice(v, ResoVoiceParam.Pan), ParamGroup.Resochord, $"Resochord Voice {v + 1} Pan", $"{tag} Pan", -1f, 1f, DefaultResoPan[v], Units.None));
        }

        rows.Add(Toggle(ParamId.ReverbOn, ParamGroup.Reverb, "Reverb On", "Rv On", true));
        rows.Add(Choice(ParamId.ReverbRoom, ParamGroup.Reverb, "Reverb Room", "Rv Room", ReverbRooms, 2));
        rows.Add(Linear(ParamId.ReverbMix, ParamGroup.Reverb, "Reverb Mix", "Rv Mix", 0f, 1f, 0.3f, Units.None, ParamFlags.Lane));
        rows.Add(Choice(ParamId.ReverbOrder, ParamGroup.Reverb, "Reverb Order", "Rv Order", ReverbOrders, 1));
        rows.Add(Toggle(ParamId.ReverbFreeze, ParamGroup.Reverb, "Reverb Freeze", "Rv Frz", false, ParamFlags.Addition));
        rows.Add(Linear(ParamId.ReverbSize, ParamGroup.Reverb, "Reverb Size", "Rv Size", 0f, 1f, 0.7f, Units.None, ParamFlags.Addition));
        rows.Add(Linear(ParamId.ReverbDamping, ParamGroup.Reverb, "Reverb Damping", "Rv Damp", 0f, 1f, 0.4f, Units.None, ParamFlags.Addition));

        rows.Add(Linear(ParamId.OutputVolume, ParamGroup.Output, "Volume", "Volume", Units.SilenceDb, 6f, 0f, Units.Decibels, ParamFlags.Lane));
        rows.Add(Linear(ParamId.OutputWidth, ParamGroup.Output, "Width", "Width", 0f, 2f, 1f, Units.None, ParamFlags.Lane));

        rows.Add(Linear(ParamId.ScopePhaseX, ParamGroup.Scope, "Scope X Phase", "Scp X", 0f, 50f, 0f, Units.Milliseconds));
        rows.Add(Linear(ParamId.ScopePhaseY, ParamGroup.Scope, "Scope Y Phase", "Scp Y", 0f, 50f, 5f, Units.Milliseconds));
        rows.Add(Linear(ParamId.ScopeZoom, ParamGroup.Scope, "Scope Zoom", "Scp Zoom", 0f, 4f, 1f, Units.Multiplier));

        rows.Add(Toggle(ParamId.PostFilterOn, ParamGroup.Post, "Post Filter On", "Po FltOn", false));
        rows.Add(Linear(ParamId.PostFilterMix, ParamGroup.Post, "Post Filter Mix", "Po FlMix", 0f, 1f, 1f, Units.None));
        rows.Add(Log(ParamId.PostFilterCutoff, ParamGroup.Post, "Post Filter Cutoff", "Po FlCut", 20f, 20000f, 12000f, Units.Hertz));
        rows.Add(Linear(ParamId.PostFilterMorph, ParamGroup.Post, "Post Filter LP-HP", "Po LP-HP", 0f, 1f, 0f, Units.None));
        rows.Add(Linear(ParamId.PostFilterReso, ParamGroup.Post, "Post Filter Reso", "Po FlRes", 0f, 1f, 0.1f, Units.None));
        rows.Add(Linear(ParamId.PostEqHigh, ParamGroup.Post, "Post EQ High", "Po EQ Hi", -12f, 12f, 0f, Units.Decibels));
        rows.Add(Linear(ParamId.PostEqLow, ParamGroup.Post, "Post EQ Low", "Po EQ Lo", -12f, 12f, 0f, Units.Decibels));
        rows.Add(Toggle(ParamId.PostLimiterOn, ParamGroup.Post, "Post Limiter On", "Po LimOn", true));
        rows.Add(Linear(ParamId.PostLimiterLowThreshold, ParamGroup.Post, "Post Limiter Low Threshold", "Po LoThr", -24f, 0f, -6f, Units.Decibels));
        rows.Add(Linear(ParamId.PostLimiterHighThreshold, ParamGroup.Post, "Post Limiter High Threshold", "Po HiThr", -24f, 0f, -6f, Units.Decibels));
        rows.Add(Log(ParamId.PostLimiterSplit, ParamGroup.Post, "Post Limiter Split", "Po Split", 100f, 5000f, 250f, Units.Hertz));
        rows.Add(Linear(ParamId.PostLimiterGain, ParamGroup.Post, "Post Limiter Gain", "Po Gain", 0f, 24f, 0f, Units.Decibels));
        rows.Add(Choice(ParamId.PostLimiterSpeed, ParamGroup.Post, "Post Limiter Speed", "Po Speed", LimiterSpeeds, 1));
        rows.Add(Linear(ParamId.PostVolume, ParamGroup.Post, "Post Volume", "Po Vol", Units.SilenceDb, 6f, 0f, Units.Decibels));

        rows.Add(Toggle(ParamId.RestartMode, ParamGroup.Transport, "Restart Mode", "RstMode", false));
        rows.Add(Trigger(ParamId.RestartNow, ParamGroup.Transport, "Restart Now", "Restart"));
        rows.Add(Toggle(ParamId.RestartOnGate, ParamGroup.Transport, "Restart On MIDI Gate", "RstGate", false));
        rows.Add(Toggle(ParamId.RestartOnHostPlay, ParamGroup.Transport, "Restart On Host Play", "RstPlay", false, ParamFlags.Addition));
        rows.Add(Choice(ParamId.SpeedStep, ParamGroup.Transport, "Speed Step", "SpdStep", SpeedSteps, 1));
        rows.Add(Log(ParamId.SpeedMultiplier, ParamGroup.Transport, "Speed Multiplier", "SpdMul", 0.1f, 10f, 1f, Units.Multiplier, ParamFlags.Addition));
        rows.Add(Trigger(ParamId.RandomizeNow, ParamGroup.Transport, "Randomize", "Random"));
        rows.Add(Choice(ParamId.RandomizeScope, ParamGroup.Transport, "Randomize Scope", "RndScope", RandomizeScopes, 0, ParamFlags.Addition));
        rows.Add(Linear(ParamId.RandomizeAmount, ParamGroup.Transport, "Randomize Amount", "RndAmt", 0f, 1f, 1f, Units.Percent, ParamFlags.Addition));
        rows.Add(Integer(ParamId.RandomSeed, ParamGroup.Transport, "Random Seed", "RndSeed", 0, 9999, 0, Units.None, ParamFlags.Addition));
        rows.Add(Toggle(ParamId.Mute, ParamGroup.Transport, "Mute", "Mute", false));

        rows.Add(Choice(ParamId.PlayMode, ParamGroup.PlayMode, "Play Mode", "PlayMode", PlayModes, 0, ParamFlags.Addition));
        rows.Add(Linear(ParamId.GateAttack, ParamGroup.PlayMode, "Gate Attack", "Attack", 0f, 10f, 0.5f, Units.Seconds, ParamFlags.Addition));
        rows.Add(Linear(ParamId.GateRelease, ParamGroup.PlayMode, "Gate Release", "Release", 0f, 30f, 4f, Units.Seconds, ParamFlags.Addition));

        rows.Add(Integer(ParamId.WheelTarget, ParamGroup.ModWheel, "Mod Wheel Target", "Whl Tgt", 0, targetMax, 0, Units.Target));
        rows.Add(Linear(ParamId.WheelMin, ParamGroup.ModWheel, "Mod Wheel Min", "Whl Min", 0f, 1f, 0f, Units.None));
        rows.Add(Linear(ParamId.WheelMax, ParamGroup.ModWheel, "Mod Wheel Max", "Whl Max", 0f, 1f, 1f, Units.None));

        rows.Add(Choice(ParamId.Scale, ParamGroup.Global, "Scale", "Scale", Scales, 0, ParamFlags.Addition));
        rows.Add(Choice(ParamId.ScaleRoot, ParamGroup.Global, "Scale Root", "Root", RootNotes, 0, ParamFlags.Addition));
        rows.Add(Linear(ParamId.TuningA4, ParamGroup.Global, "Tuning A4", "A4", 415f, 466f, 440f, Units.Hertz, ParamFlags.Addition));
        rows.Add(Toggle(ParamId.HostSync, ParamGroup.Global, "Host Sync", "HostSync", false, ParamFlags.Addition));
        rows.Add(Choice(ParamId.EngineRate, ParamGroup.Global, "Engine Rate", "EngRate", EngineRates, 0, ParamFlags.Addition));

        for (var i = 0; i < LfoCount; i++)
        {
            var tag = $"LFO{i + 1}";
            rows.Add(Log(Lfo(i, LfoParam.Rate), ParamGroup.ModMatrix, $"LFO {i + 1} Rate", $"{tag} Rt", 0.01f, 20f, 0.1f, Units.Hertz, ParamFlags.Addition));
            rows.Add(Choice(Lfo(i, LfoParam.Shape), ParamGroup.ModMatrix, $"LFO {i + 1} Shape", $"{tag} Shp", LfoShapes, 0, ParamFlags.Addition));
            rows.Add(Linear(Lfo(i, LfoParam.Depth), ParamGroup.ModMatrix, $"LFO {i + 1} Depth", $"{tag} Dep", 0f, 1f, 1f, Units.None, ParamFlags.Addition));
        }

        rows.Add(Log(ParamId.EnvFollowerAttack, ParamGroup.ModMatrix, "Env Follower Attack", "Env Att", 1f, 1000f, 10f, Units.Milliseconds, ParamFlags.Addition));
        rows.Add(Log(ParamId.EnvFollowerRelease, ParamGroup.ModMatrix, "Env Follower Release", "Env Rel", 1f, 5000f, 200f, Units.Milliseconds, ParamFlags.Addition));
        rows.Add(Linear(ParamId.Macro1, ParamGroup.ModMatrix, "Macro 1", "Macro 1", 0f, 1f, 0f, Units.None, ParamFlags.Addition));
        rows.Add(Linear(ParamId.Macro2, ParamGroup.ModMatrix, "Macro 2", "Macro 2", 0f, 1f, 0f, Units.None, ParamFlags.Addition));
        rows.Add(Linear(ParamId.Macro3, ParamGroup.ModMatrix, "Macro 3", "Macro 3", 0f, 1f, 0f, Units.None, ParamFlags.Addition));
        rows.Add(Linear(ParamId.Macro4, ParamGroup.ModMatrix, "Macro 4", "Macro 4", 0f, 1f, 0f, Units.None, ParamFlags.Addition));

        for (var i = 0; i < ModSlotCount; i++)
        {
            var tag = $"M{i + 1}";
            rows.Add(Choice(ModSlot(i, ModSlotParam.Source), ParamGroup.ModMatrix, $"Mod Slot {i + 1} Source", $"{tag} Src", MatrixSources, 0, ParamFlags.Addition));
            rows.Add(Integer(ModSlot(i, ModSlotParam.Target), ParamGroup.ModMatrix, $"Mod Slot {i + 1} Target", $"{tag} Tgt", 0, targetMax, 0, Units.Target, ParamFlags.Addition));
            rows.Add(Linear(ModSlot(i, ModSlotParam.Depth), ParamGroup.ModMatrix, $"Mod Slot {i + 1} Depth", $"{tag} Dep", -1f, 1f, 0f, Units.None, ParamFlags.Addition));
        }

        if (rows.Count != ids.Length)
            throw new InvalidOperationException($"ParameterTable has {rows.Count} rows but ParamId has {ids.Length} members.");

        for (var i = 0; i < rows.Count; i++)
        {
            if ((int)rows[i].Id != i)
                throw new InvalidOperationException($"ParameterTable row {i} is {rows[i].Id}, expected {(ParamId)i}: the table and the enum are out of step.");
        }

        return rows;
    }

    private static ParamInfo Linear(ParamId id, ParamGroup group, string name, string shortName, float min, float max, float defaultValue, string unit, ParamFlags flags = ParamFlags.None) =>
        new(id, group, name, shortName, ParamKind.Continuous, ParamScale.Linear, min, max, defaultValue, unit, null, flags);

    private static ParamInfo Log(ParamId id, ParamGroup group, string name, string shortName, float min, float max, float defaultValue, string unit, ParamFlags flags = ParamFlags.None) =>
        new(id, group, name, shortName, ParamKind.Continuous, ParamScale.Logarithmic, min, max, defaultValue, unit, null, flags);

    private static ParamInfo Integer(ParamId id, ParamGroup group, string name, string shortName, int min, int max, int defaultValue, string unit, ParamFlags flags = ParamFlags.None) =>
        new(id, group, name, shortName, ParamKind.Integer, ParamScale.Linear, min, max, defaultValue, unit, null, flags);

    private static ParamInfo Toggle(ParamId id, ParamGroup group, string name, string shortName, bool defaultOn, ParamFlags flags = ParamFlags.None) =>
        new(id, group, name, shortName, ParamKind.Toggle, ParamScale.Linear, 0f, 1f, defaultOn ? 1f : 0f, Units.None, null, flags);

    private static ParamInfo Choice(ParamId id, ParamGroup group, string name, string shortName, IReadOnlyList<string> choices, int defaultIndex, ParamFlags flags = ParamFlags.None) =>
        new(id, group, name, shortName, ParamKind.Choice, ParamScale.Linear, 0f, choices.Count - 1, defaultIndex, Units.None, choices, flags);

    private static ParamInfo Trigger(ParamId id, ParamGroup group, string name, string shortName, ParamFlags flags = ParamFlags.None) =>
        new(id, group, name, shortName, ParamKind.Trigger, ParamScale.Linear, 0f, 1f, 0f, Units.None, null, flags);
}
