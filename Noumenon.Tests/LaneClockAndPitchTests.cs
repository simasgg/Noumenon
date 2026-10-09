using Noumenon.Dsp.Effects;
using Noumenon.Engine;
using Noumenon.Engine.Lanes;
using Noumenon.Engine.Parameters;

namespace Noumenon.Tests;

public class LaneClockAndPitchTests
{
    private const int SampleRate = 48000;

    private static NoumenonEngine Make()
    {
        var engine = new NoumenonEngine();
        EngineTests.BypassEffects(engine.Parameters);
        engine.Parameters.Set(ParamId.P1Glide, 0f);
        engine.Parameters.Set(ParamId.P2Glide, 0f);
        engine.Prepare(SampleRate, 512);
        engine.Reset(1);

        return engine;
    }

    private static void Render(NoumenonEngine engine, double seconds)
    {
        var n = (int)(seconds * SampleRate);
        var l = new float[512];
        var r = new float[512];
        for (var pos = 0; pos < n; pos += 512)
            engine.Process(l, r, Math.Min(512, n - pos));
    }

    [Fact]
    public void Speed_Is_The_Step_Times_The_Multiplier()
    {
        var clock = new LaneClock();
        var bank = new ParameterBank();
        bank.Set(ParamId.SpeedStep, 3);
        bank.Set(ParamId.SpeedMultiplier, 0.5f);
        clock.Tick(bank, false, false);
        Assert.Equal(2f, clock.Speed);
    }

    [Fact]
    public void Restart_Now_Fires_Once_And_Clears_Itself()
    {
        var clock = new LaneClock();
        var bank = new ParameterBank();
        bank.Set(ParamId.RestartNow, 1f);
        Assert.True(clock.Tick(bank, false, false));
        Assert.Equal(0f, bank.Get(ParamId.RestartNow));
        Assert.False(clock.Tick(bank, false, false));

        clock.RequestRestart();
        Assert.True(clock.Tick(bank, false, false));
    }

    [Fact]
    public void Gate_Recording_Edges_And_Host_Play_Restart_Only_When_Enabled()
    {
        var clock = new LaneClock();
        var bank = new ParameterBank();
        Assert.False(clock.Tick(bank, gate: true, recordingEdge: true));

        bank.Set(ParamId.RestartOnGate, true);
        Assert.True(clock.Tick(bank, gate: true, recordingEdge: false));

        bank.Set(ParamId.RestartMode, true);
        Assert.True(clock.Tick(bank, gate: false, recordingEdge: true));

        bank.Set(ParamId.RestartOnHostPlay, true);
        clock.HostPlaying = true;
        Assert.True(clock.Tick(bank, false, false));
        Assert.False(clock.Tick(bank, false, false), "only the edge restarts");
    }

    [Fact]
    public void Host_Sync_Exposes_The_Bar_Length()
    {
        var clock = new LaneClock();
        var bank = new ParameterBank();
        clock.HostTempo = 120;
        clock.Tick(bank, false, false);
        Assert.Equal(0f, clock.BarSeconds);

        bank.Set(ParamId.HostSync, true);
        clock.Tick(bank, false, false);
        Assert.Equal(2f, clock.BarSeconds, 5);
    }

    [Fact]
    public void P1_Follows_Midi_Notes_And_Bend_When_Enabled()
    {
        var engine = Make();
        engine.Parameters.Set(ParamId.P1Value, 5f);
        Render(engine, 0.01);
        Assert.Equal(5f, engine.P1);

        engine.Parameters.Set(ParamId.P1Midi, true);
        engine.Midi.NoteOn(64, 100);
        Render(engine, 0.01);
        Assert.Equal(4f, engine.P1);   // E4 − C4
        Assert.Equal(0f, engine.P2);   // P2's MIDI button is off: it stays on its own fader

        engine.Midi.PitchBend(16383);
        Render(engine, 0.01);
        Assert.InRange(engine.P1, 5.99f, 6.01f);   // + bend range 2

        engine.Parameters.Set(ParamId.P1Midi, false);
        Render(engine, 0.01);
        Assert.Equal(5f, engine.P1);
    }

    [Fact]
    public void Glide_Takes_Its_Time()
    {
        var engine = Make();
        engine.Parameters.Set(ParamId.P1Glide, 0.5f);
        engine.Parameters.Set(ParamId.P1Value, 12f);
        Render(engine, 0.05);
        Assert.InRange(engine.P1, 0.5f, 6f);

        Render(engine, 2.0);
        Assert.InRange(engine.P1, 11.99f, 12.01f);
    }

    [Fact]
    public void Chord_Select_From_A_Midi_Note_Picks_The_Fabrications_Chord()
    {
        var engine = Make();
        engine.Parameters.Set(ParamId.ResochordChordSelect, 1);   // P1
        engine.Parameters.Set(ParamId.P1Midi, true);

        engine.Midi.NoteOn(52, 100);   // E3: C#3 = chord 1, so E3 = chord 4
        Render(engine, 0.01);
        for (var v = 0; v < Resochord.VoiceCount; v++)
            Assert.Equal(ChordTable.All[4].Notes[v], engine.ResochordNote(v));

        engine.Midi.NoteOn(48, 100);   // C3: the voice parameters
        Render(engine, 0.01);
        Assert.Equal(engine.Parameters.Get(ParamId.ResoVoice1Note), engine.ResochordNote(0));
    }

    [Fact]
    public void A_Scale_Quantizes_Midi_Pitch_And_Resochord_Notes()
    {
        var engine = Make();
        engine.Parameters.Set(ParamId.Scale, 3);   // Pentatonic Major in C
        engine.Parameters.Set(ParamId.P1Midi, true);
        engine.Midi.NoteOn(66, 100);   // F#4 → G4 (7) or E4 (4): nearest degree is 7
        Render(engine, 0.01);
        Assert.Equal(7f, engine.P1);

        engine.Parameters.Set(ParamId.ResochordChord, 3);   // Sus2 has a D (50) and a G (55): both in the scale
        Render(engine, 0.01);
        Assert.Equal(50f, engine.ResochordNote(1));
    }
}
