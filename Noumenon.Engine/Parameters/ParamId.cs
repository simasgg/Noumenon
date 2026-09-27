namespace Noumenon.Engine.Parameters;

/// <summary>
/// Every parameter the instrument exposes, in a fixed order. The numeric value is the parameter's
/// identity everywhere: the index into <see cref="ParameterBank"/>'s live values, the VST2 parameter
/// index and the VST3 parameter ID, and the key presets are matched against. <b>Never reorder or
/// remove a member — only append at the end</b>, or every saved project and preset silently shifts.
/// Repeated blocks (twelve oscillator slots, two sections, two pitch faders, six Resochord voices,
/// three LFOs, twelve matrix slots) are laid out contiguously so <see cref="ParameterTable"/> can
/// address them arithmetically; a test pins that layout to these names.
/// </summary>
public enum ParamId
{
    // ---- Oscillators: section A slots 1-6, then section B slots 1-6, ten parameters each ----
    OscA1On, OscA1Wave, OscA1Semitone, OscA1Fine, OscA1Shape, OscA1NoiseCutoff, OscA1Level, OscA1Trim, OscA1P1Route, OscA1P2Route,
    OscA2On, OscA2Wave, OscA2Semitone, OscA2Fine, OscA2Shape, OscA2NoiseCutoff, OscA2Level, OscA2Trim, OscA2P1Route, OscA2P2Route,
    OscA3On, OscA3Wave, OscA3Semitone, OscA3Fine, OscA3Shape, OscA3NoiseCutoff, OscA3Level, OscA3Trim, OscA3P1Route, OscA3P2Route,
    OscA4On, OscA4Wave, OscA4Semitone, OscA4Fine, OscA4Shape, OscA4NoiseCutoff, OscA4Level, OscA4Trim, OscA4P1Route, OscA4P2Route,
    OscA5On, OscA5Wave, OscA5Semitone, OscA5Fine, OscA5Shape, OscA5NoiseCutoff, OscA5Level, OscA5Trim, OscA5P1Route, OscA5P2Route,
    OscA6On, OscA6Wave, OscA6Semitone, OscA6Fine, OscA6Shape, OscA6NoiseCutoff, OscA6Level, OscA6Trim, OscA6P1Route, OscA6P2Route,
    OscB1On, OscB1Wave, OscB1Semitone, OscB1Fine, OscB1Shape, OscB1NoiseCutoff, OscB1Level, OscB1Trim, OscB1P1Route, OscB1P2Route,
    OscB2On, OscB2Wave, OscB2Semitone, OscB2Fine, OscB2Shape, OscB2NoiseCutoff, OscB2Level, OscB2Trim, OscB2P1Route, OscB2P2Route,
    OscB3On, OscB3Wave, OscB3Semitone, OscB3Fine, OscB3Shape, OscB3NoiseCutoff, OscB3Level, OscB3Trim, OscB3P1Route, OscB3P2Route,
    OscB4On, OscB4Wave, OscB4Semitone, OscB4Fine, OscB4Shape, OscB4NoiseCutoff, OscB4Level, OscB4Trim, OscB4P1Route, OscB4P2Route,
    OscB5On, OscB5Wave, OscB5Semitone, OscB5Fine, OscB5Shape, OscB5NoiseCutoff, OscB5Level, OscB5Trim, OscB5P1Route, OscB5P2Route,
    OscB6On, OscB6Wave, OscB6Semitone, OscB6Fine, OscB6Shape, OscB6NoiseCutoff, OscB6Level, OscB6Trim, OscB6P1Route, OscB6P2Route,

    // ---- Sections A and B: the 12 dB filter, the section level, and the routing-row extras ----
    SectionACutoff, SectionAReso, SectionAFilterType, SectionASlave, SectionALevel, SectionAP1RouteAll, SectionAP2RouteAll, SectionAP1FineRange, SectionAP2FineRange,
    SectionBCutoff, SectionBReso, SectionBFilterType, SectionBSlave, SectionBLevel, SectionBP1RouteAll, SectionBP2RouteAll, SectionBP1FineRange, SectionBP2FineRange,

    // ---- Pitch faders P1 and P2 ----
    P1Value, P1Midi, P1Glide, P1BendRange,
    P2Value, P2Midi, P2Glide, P2BendRange,

    // ---- Section combine: A+B / A×B ----
    MixCrossfade, MixSumBalance, MixRingLevel,

    // ---- Sampler / live input ----
    SamplerSelect, SamplerMode, SamplerPitch, SamplerFine, SamplerModSource,
    SamplerLoopStart, SamplerLoopEnd, SamplerLoopCrossfade,
    SamplerAmp, SamplerDir, SamplerMaster, SamplerStereo, SamplerRetrigger, SamplerInputMix, SamplerAmDepth,
    GrainSize, GrainDensity, GrainPosition, GrainSpray, StretchAmount,

    // ---- Effect chain ----
    MasterFilterOn, MasterFilterMorph, MasterFilterCutoff, MasterFilterReso, MasterFilterMix,
    DistortionOn, DistortionMix, DistortionDrive, DistortionCurve,
    EqHigh, EqLow,
    SpinOn, SpinTime1, SpinTime2, SpinFine, SpinFeedback, SpinLink, SpinMix,
    ResochordOn, ResochordChord, ResochordFeedback, ResochordFeedbackFader, ResochordPitch, ResochordFine, ResochordModSource, ResochordMix, ResochordMidi, ResochordChordSelect,
    ResoVoice1Note, ResoVoice1Feedback, ResoVoice1Damping, ResoVoice1Level, ResoVoice1Pan,
    ResoVoice2Note, ResoVoice2Feedback, ResoVoice2Damping, ResoVoice2Level, ResoVoice2Pan,
    ResoVoice3Note, ResoVoice3Feedback, ResoVoice3Damping, ResoVoice3Level, ResoVoice3Pan,
    ResoVoice4Note, ResoVoice4Feedback, ResoVoice4Damping, ResoVoice4Level, ResoVoice4Pan,
    ResoVoice5Note, ResoVoice5Feedback, ResoVoice5Damping, ResoVoice5Level, ResoVoice5Pan,
    ResoVoice6Note, ResoVoice6Feedback, ResoVoice6Damping, ResoVoice6Level, ResoVoice6Pan,
    ReverbOn, ReverbRoom, ReverbMix, ReverbOrder, ReverbFreeze, ReverbSize, ReverbDamping,
    OutputVolume, OutputWidth,
    ScopePhaseX, ScopePhaseY, ScopeZoom,

    // ---- Fabrications post section ----
    PostFilterOn, PostFilterMix, PostFilterCutoff, PostFilterMorph, PostFilterReso,
    PostEqHigh, PostEqLow,
    PostLimiterOn, PostLimiterLowThreshold, PostLimiterHighThreshold, PostLimiterSplit, PostLimiterGain, PostLimiterSpeed,
    PostVolume,

    // ---- Transport, play mode, mod wheel, global ----
    RestartMode, RestartNow, RestartOnGate, RestartOnHostPlay, SpeedStep, SpeedMultiplier,
    RandomizeNow, RandomizeScope, RandomizeAmount, RandomSeed, Mute,
    PlayMode, GateAttack, GateRelease,
    WheelTarget, WheelMin, WheelMax,
    Scale, ScaleRoot, TuningA4, HostSync, EngineRate,

    // ---- Modulation matrix ----
    Lfo1Rate, Lfo1Shape, Lfo1Depth,
    Lfo2Rate, Lfo2Shape, Lfo2Depth,
    Lfo3Rate, Lfo3Shape, Lfo3Depth,
    EnvFollowerAttack, EnvFollowerRelease,
    Macro1, Macro2, Macro3, Macro4,
    ModSlot1Source, ModSlot1Target, ModSlot1Depth,
    ModSlot2Source, ModSlot2Target, ModSlot2Depth,
    ModSlot3Source, ModSlot3Target, ModSlot3Depth,
    ModSlot4Source, ModSlot4Target, ModSlot4Depth,
    ModSlot5Source, ModSlot5Target, ModSlot5Depth,
    ModSlot6Source, ModSlot6Target, ModSlot6Depth,
    ModSlot7Source, ModSlot7Target, ModSlot7Depth,
    ModSlot8Source, ModSlot8Target, ModSlot8Depth,
    ModSlot9Source, ModSlot9Target, ModSlot9Depth,
    ModSlot10Source, ModSlot10Target, ModSlot10Depth,
    ModSlot11Source, ModSlot11Target, ModSlot11Depth,
    ModSlot12Source, ModSlot12Target, ModSlot12Depth,
}
