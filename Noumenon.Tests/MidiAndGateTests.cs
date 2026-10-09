using Noumenon.Engine;
using Noumenon.Engine.Modulation;
using Noumenon.Engine.Parameters;

namespace Noumenon.Tests;

public class MidiAndGateTests
{
    private const int SampleRate = 48000;

    private static NoumenonEngine Make()
    {
        var engine = new NoumenonEngine();
        EngineTests.BypassEffects(engine.Parameters);
        engine.Prepare(SampleRate, 512);
        engine.Reset(1);

        return engine;
    }

    private static float[] Render(NoumenonEngine engine, double seconds)
    {
        var n = (int)(seconds * SampleRate);
        var l = new float[n];
        var r = new float[n];
        for (var pos = 0; pos < n; pos += 512)
            engine.Process(l, r, pos, Math.Min(512, n - pos));

        return l;
    }

    private static double Rms(float[] x, int start = 0)
    {
        var sum = 0.0;
        for (var i = start; i < x.Length; i++)
            sum += (double)x[i] * x[i];

        return Math.Sqrt(sum / Math.Max(1, x.Length - start));
    }

    [Fact]
    public void Input_Tracks_Notes_Bend_Wheel_And_Aftertouch()
    {
        var midi = new MidiInput();
        midi.NoteOn(60, 100);
        midi.NoteOn(64, 80);
        midi.PitchBend(0);
        midi.ControlChange(1, 127);
        midi.ChannelAftertouch(64);
        Assert.True(midi.Drain());

        Assert.Equal(64, midi.LastNote);
        Assert.Equal(80 / 127f, midi.LastVelocity);
        Assert.Equal(2, midi.HeldCount);
        Assert.Equal(-1f, midi.Bend);
        Assert.Equal(1f, midi.Wheel);
        Assert.Equal(64 / 127f, midi.Aftertouch);

        midi.NoteOff(60);
        midi.NoteOn(64, 0);   // velocity 0 is a note-off
        midi.PitchBend(16383);
        Assert.False(midi.Drain(), "no note-on arrived");
        Assert.Equal(0, midi.HeldCount);
        Assert.InRange(midi.Bend, 0.999f, 1f);
    }

    [Fact]
    public void ResoMidi_Assigns_Voices_Round_Robin()
    {
        var midi = new MidiInput();
        for (var i = 0; i < 8; i++)
            midi.NoteOn(60 + i, 100);

        midi.Drain();
        Assert.Equal(66f, midi.VoiceNote(0));   // wrapped round: the 7th note landed on voice 0
        Assert.Equal(67f, midi.VoiceNote(1));
        Assert.Equal(62f, midi.VoiceNote(2));
        Assert.Equal(65f, midi.VoiceNote(5));

        midi.ClearVoiceNotes();
        Assert.True(float.IsNaN(midi.VoiceNote(0)));
    }

    [Fact]
    public void ResoMidi_Plays_The_Resochord_From_The_Keyboard()
    {
        var engine = Make();
        engine.Parameters.Set(ParamId.ResochordMidi, true);
        engine.Midi.NoteOn(60, 100);
        engine.Midi.NoteOn(64, 100);
        Render(engine, 0.01);

        Assert.Equal(60f, engine.ResochordNote(0));
        Assert.Equal(64f, engine.ResochordNote(1));
        Assert.Equal(engine.Parameters.Get(ParamId.ResoVoice3Note), engine.ResochordNote(2));   // untouched voices keep the chord
    }

    [Fact]
    public void Gated_Mode_Opens_On_Notes_And_Closes_After_Release()
    {
        var engine = Make();
        var bank = engine.Parameters;
        bank.Set(ParamId.PlayMode, 1);
        bank.Set(ParamId.GateAttack, 0.01f);
        bank.Set(ParamId.GateRelease, 0.05f);
        engine.Reset(1);

        var silent = Render(engine, 0.2);
        Assert.True(Rms(silent) < 1e-4, $"gated mode should be silent with no note held, rms {Rms(silent)}");

        engine.Midi.NoteOn(60, 100);
        var open = Render(engine, 0.3);
        Assert.True(Rms(open, SampleRate / 10) > 0.02, "a held note should open the gate");

        engine.Midi.NoteOff(60);
        var closed = Render(engine, 0.6);
        Assert.True(Rms(closed, SampleRate / 2) < 1e-3, $"after release the sources should be silent, rms {Rms(closed, SampleRate / 2)}");
        Assert.Equal(0f, engine.GateEnvelopeValue);
    }

    [Fact]
    public void Free_Run_Ignores_Notes()
    {
        var engine = Make();
        var open = Render(engine, 0.3);
        Assert.True(Rms(open, SampleRate / 10) > 0.02);
        Assert.Equal(1f, engine.GateEnvelopeValue);
    }

    [Fact]
    public void A_Full_Queue_Drops_Rather_Than_Blocks()
    {
        var midi = new MidiInput();
        for (var i = 0; i < 1000; i++)
            midi.NoteOn(60, 100);

        midi.Drain();
        Assert.Equal(1, midi.HeldCount);
    }
}
