using Noumenon.Engine;
using Noumenon.Engine.Parameters;
using Noumenon.Engine.Scales;

namespace Noumenon.Tests;

/// <summary>
/// The scale-aware chord generator and the chord sequencer: chords are stacked in steps of the
/// active scale (12-TET borrowing major), voicings spread them as documented, the sequencer steps
/// on the lane clock (speed, host-synced beats, restarts, Hold), drives the Resonator and can hand
/// its root to a P fader, and the panel's fill puts the key's chords in the user memories.
/// </summary>
public class ChordSequencerTests
{
    private const int SampleRate = 48000;

    private static float[] Chord(Scale scale, int root, int degree, ChordType type, ChordVoicing voicing = ChordVoicing.Close, int octave = 3)
    {
        var notes = new float[ChordGenerator.VoiceCount];
        ChordGenerator.Generate(scale, root, degree, type, voicing, octave, notes);

        return notes;
    }

    private static Scale BuiltIn(string name) => Scale.BuiltIn.First(s => s.Name == name);

    [Fact]
    public void A_Major_Triad_On_The_Tonic_In_Close_Voicing()
    {
        Assert.Equal([48f, 52f, 55f, 60f, 64f, 67f], Chord(BuiltIn("Major"), 0, 0, ChordType.Triad));
    }

    [Fact]
    public void The_Fifth_Degree_Seventh_Is_A_Dominant_Seventh()
    {
        Assert.Equal([55f, 59f, 62f, 65f, 67f, 71f], Chord(BuiltIn("Major"), 0, 4, ChordType.Seventh));
    }

    [Fact]
    public void The_Scale_Decides_Major_Or_Minor()
    {
        var major = BuiltIn("Major");
        Assert.Equal([50f, 53f, 57f, 62f, 65f, 69f], Chord(major, 0, 1, ChordType.Triad));   // ii = D minor
        Assert.Equal([57f, 60f, 64f, 69f, 72f, 76f], Chord(BuiltIn("Minor"), 9, 0, ChordType.Triad));   // A minor: i = A C E
    }

    [Fact]
    public void Twelve_Tet_Borrows_The_Major_Scale()
    {
        Assert.Equal(Chord(BuiltIn("Major"), 2, 3, ChordType.Seventh), Chord(Scale.Equal12, 2, 3, ChordType.Seventh));
    }

    [Fact]
    public void Open_And_Wide_Voicings_Spread_The_Chord()
    {
        var major = BuiltIn("Major");
        Assert.Equal([36f, 48f, 55f, 64f, 72f, 79f], Chord(major, 0, 0, ChordType.Triad, ChordVoicing.Open));
        Assert.Equal([36f, 43f, 64f, 72f, 79f, 88f], Chord(major, 0, 0, ChordType.Triad, ChordVoicing.Wide));
    }

    [Fact]
    public void Degrees_Past_The_Scale_Wrap_Into_The_Next_Octave()
    {
        var pentatonic = BuiltIn("Pentatonic Major");   // C D E G A
        Assert.Equal(60f, Chord(pentatonic, 0, 5, ChordType.Triad)[0]);   // degree VI of five is C an octave up
        Assert.Equal(60f, ChordGenerator.RootNote(pentatonic, 0, 5, 3));
    }

    [Fact]
    public void Just_Scales_Give_Fractional_Notes()
    {
        var notes = Chord(BuiltIn("Just Major"), 0, 0, ChordType.Triad);
        Assert.Equal(48f + 3.8631f, notes[1], 3);
        Assert.Equal(48f + 7.0196f, notes[2], 3);
    }

    private static NoumenonEngine MakeEngine()
    {
        var engine = new NoumenonEngine();
        var bank = engine.Parameters;
        EngineTests.BypassEffects(bank);
        bank.Set(ParamId.ChordSeqOn, true);
        bank.Set(ParamId.ChordSeqSteps, 4);
        bank.Set(ParamId.ChordSeqStepTime, 1f);
        bank.Set(ParamId.ChordSeqVoicing, (int)ChordVoicing.Close);
        bank.Set(ParamId.Scale, 1);   // Major
        bank.Set(ParamId.P1Glide, 0f);
        engine.Prepare(SampleRate, 512);
        engine.Reset(1);

        return engine;
    }

    private static void Run(NoumenonEngine engine, double seconds)
    {
        var l = new float[512];
        var r = new float[512];
        for (var done = 0; done < seconds * SampleRate; done += 512)
            engine.Process(l, r, 512);
    }

    [Fact]
    public void The_Sequencer_Steps_Through_Its_Degrees_On_The_Lane_Clock()
    {
        var engine = MakeEngine();
        Run(engine, 0.5);
        Assert.Equal(0, engine.Sequencer.Step);
        Assert.Equal(48f, engine.ResochordNote(0));   // I: C

        Run(engine, 1.0);
        Assert.Equal(1, engine.Sequencer.Step);
        Assert.Equal(57f, engine.ResochordNote(0));   // vi: A

        engine.Parameters.Set(ParamId.SpeedStep, 3);   // 4× clock: a step every quarter second
        Run(engine, 0.3);
        Assert.Equal(2, engine.Sequencer.Step);

        engine.Clock.RequestRestart();
        Run(engine, 0.05);
        Assert.Equal(0, engine.Sequencer.Step);
    }

    [Fact]
    public void Hold_Keeps_The_Previous_Chord()
    {
        var engine = MakeEngine();
        engine.Parameters.Set(ParamId.ChordSeqStep2, 0);   // Hold
        Run(engine, 1.5);
        Assert.Equal(1, engine.Sequencer.Step);
        Assert.Equal(48f, engine.ResochordNote(0));
    }

    [Fact]
    public void Host_Sync_Snaps_A_Step_To_Whole_Beats()
    {
        var engine = MakeEngine();
        engine.Parameters.Set(ParamId.HostSync, true);
        engine.Parameters.Set(ParamId.ChordSeqStepTime, 1.2f);   // at 120 BPM: two beats, one second
        engine.Clock.HostTempo = 120;
        Run(engine, 0.9);
        Assert.Equal(0, engine.Sequencer.Step);
        Run(engine, 0.2);
        Assert.Equal(1, engine.Sequencer.Step);
    }

    [Fact]
    public void The_Root_Can_Drive_P1()
    {
        var engine = MakeEngine();
        engine.Parameters.Set(ParamId.ChordSeqRoot, 1);
        Run(engine, 1.5);   // step 2: vi, A3 = 57
        Assert.Equal(57f - 60f, engine.P1, 3);
    }

    [Fact]
    public void Off_Leaves_The_Chord_List_In_Charge()
    {
        var engine = MakeEngine();
        engine.Parameters.Set(ParamId.ChordSeqOn, false);
        engine.Parameters.Set(ParamId.ResochordChord, 0);
        Run(engine, 0.2);
        Assert.Equal(engine.Parameters.Get(ParamId.ResoVoice1Note), engine.ResochordNote(0));
    }

    [Fact]
    public void Fill_Puts_The_Keys_Chords_In_The_User_Memories()
    {
        var engine = new NoumenonEngine();
        engine.Parameters.Set(ParamId.Scale, 1);
        engine.Parameters.Set(ParamId.ChordSeqVoicing, (int)ChordVoicing.Close);
        engine.FillUserChordsFromScale();

        Assert.Equal([48f, 52f, 55f, 60f, 64f, 67f], engine.UserChord(0));
        Assert.Equal([55f, 59f, 62f, 67f, 71f, 74f], engine.UserChord(4));
        Assert.Equal([60f, 64f, 67f, 72f, 76f, 79f], engine.UserChord(7));   // I an octave up
    }
}
