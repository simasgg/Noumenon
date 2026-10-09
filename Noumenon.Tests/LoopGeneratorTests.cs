using Noumenon.Dsp.Effects;
using Noumenon.Dsp.Oscillators;
using Noumenon.Engine;
using Noumenon.Engine.Lanes;
using Noumenon.Engine.Parameters;

namespace Noumenon.Tests;

/// <summary>
/// The plan's Phase 4 "done when": lanes make a render evolve and repeat, a MIDI progression drives
/// the chord select, and two renders from the same seed — lanes, LFOs and all — are byte-identical.
/// </summary>
public class LoopGeneratorTests
{
    private const int SampleRate = 48000;

    private static NoumenonEngine Make(ulong seed = 1)
    {
        var engine = new NoumenonEngine();
        engine.Prepare(SampleRate, 512);
        engine.Reset(seed);

        return engine;
    }

    private static (float[] left, float[] right) Render(NoumenonEngine engine, int n, int block = 512, Action<int>? beforeBlock = null)
    {
        var l = new float[n];
        var r = new float[n];
        for (var pos = 0; pos < n; pos += block)
        {
            beforeBlock?.Invoke(pos);
            engine.Process(l, r, pos, Math.Min(block, n - pos));
        }

        return (l, r);
    }

    private static double WindowRms(float[] x, int start, int end)
    {
        var sum = 0.0;
        var count = 0;
        for (var i = start; i < end && i < x.Length; i++)
        {
            sum += (double)x[i] * x[i];
            count++;
        }

        return count > 0 ? Math.Sqrt(sum / count) : 0;
    }

    private static void SoloSine(ParameterBank bank)
    {
        EngineTests.BypassEffects(bank);
        for (var s = 0; s < ParameterTable.SectionCount; s++)
        {
            for (var slot = 0; slot < ParameterTable.SlotsPerSection; slot++)
                bank.Set(ParameterTable.Osc((SectionId)s, slot, OscParam.On), false);
        }

        bank.Set(ParamId.OscA2On, true);
        bank.Set(ParamId.OscA2Wave, (int)Waveform.Sine);
        bank.Set(ParamId.SectionALevel, 1f);
        bank.Set(ParamId.MixCrossfade, 0f);
    }

    [Fact]
    public void Same_Seed_Renders_Identically_With_Lanes_Lfos_And_Midi()
    {
        var renders = new List<float[]>();
        foreach (var block in new[] { 512, 333 })
        {
            var engine = Make(11);
            var bank = engine.Parameters;
            Randomizer.Randomize(engine, 11, RandomizeScope.All, 0.7f);
            bank.Set(ParamId.Lfo1Rate, 0.7f);
            bank.Set(ParamId.Lfo1Shape, 5);   // Smooth Random
            bank.Set(ParamId.ModSlot1Source, 3);
            bank.Set(ParamId.ModSlot1Target, (int)ParamId.MasterFilterCutoff + 1);
            bank.Set(ParamId.ModSlot1Depth, 0.4f);
            bank.Set(ParamId.P1Midi, true);
            bank.Set(ParamId.ResochordChordSelect, 1);
            engine.Midi.NoteOn(55, 100);
            engine.Reset(11);
            var (l, _) = Render(engine, SampleRate * 3, block);
            renders.Add(l);
        }

        Assert.Equal(renders[0], renders[1]);
        Assert.True(WindowRms(renders[0], SampleRate, SampleRate * 2) > 0.01, "the patch should sound");
    }

    [Fact]
    public void A_Lane_Makes_The_Render_Evolve_And_Repeat()
    {
        var engine = Make();
        SoloSine(engine.Parameters);
        var points = new float[100];   // a 2 s loop: level rises 0.1 → 0.9 and falls back
        for (var i = 0; i < points.Length; i++)
            points[i] = 0.1f + 0.8f * (0.5f - 0.5f * MathF.Cos(2f * MathF.PI * i / points.Length));

        engine.Lanes.ForParameter(ParamId.OscA2Level)!.Load(new LaneSnapshot(points, LaneMode.Automated, 1f));
        engine.Reset(1);

        var (l, _) = Render(engine, SampleRate * 6);
        var quarter = SampleRate / 4;
        var windows = Enumerable.Range(0, 24).Select(k => WindowRms(l, k * quarter, (k + 1) * quarter)).ToArray();

        Assert.True(windows.Max() / windows.Min() > 2.0, "the level should move within a period");
        for (var k = 0; k < 16; k++)
            Assert.InRange(windows[k + 8], windows[k] * 0.85, windows[k] * 1.15);   // repeats every 2 s
    }

    [Fact]
    public void A_Midi_Progression_Drives_The_Resochord_Chords()
    {
        var engine = Make();
        var bank = engine.Parameters;
        bank.Set(ParamId.P1Midi, true);
        bank.Set(ParamId.P1Glide, 0f);
        bank.Set(ParamId.ResochordChordSelect, 1);
        bank.Set(ParamId.ResochordMix, 0.5f);
        engine.Reset(1);

        var progression = new[] { (Second: 0.0, Note: 48, Chord: 0), (Second: 1.0, Note: 52, Chord: 4), (Second: 2.0, Note: 55, Chord: 7), (Second: 3.0, Note: 60, Chord: 12) };
        var seen = new List<int>();
        var expectedChord = -1;
        var (l, _) = Render(engine, SampleRate * 4, beforeBlock: pos =>
        {
            foreach (var step in progression)
            {
                var frame = (int)(step.Second * SampleRate);
                if (frame >= pos && frame < pos + 512)
                {
                    engine.Midi.NoteOn(step.Note, 100);
                    expectedChord = step.Chord;
                }
            }

            if (pos % (SampleRate / 2) < 512 && expectedChord >= 0 && pos > 0)
            {
                var notes = expectedChord == 0 ? Enumerable.Range(0, 6).Select(v => bank.Get(ParameterTable.ResoVoice(v, ResoVoiceParam.Note))).ToArray() : ChordTable.All[expectedChord].Notes;
                for (var v = 0; v < Resochord.VoiceCount; v++)
                    Assert.Equal(notes[v], engine.ResochordNote(v));

                seen.Add(expectedChord);
            }
        });

        Assert.Contains(4, seen);
        Assert.Contains(7, seen);
        Assert.Contains(12, seen);
        Assert.All(l, v => Assert.True(float.IsFinite(v)));
        Assert.True(WindowRms(l, SampleRate * 3, SampleRate * 4) > 0.01);
    }

    [Fact]
    public void Restart_Aligns_Every_Lane()
    {
        var engine = Make();
        SoloSine(engine.Parameters);
        var lanes = engine.Lanes;
        lanes.ForParameter(ParamId.OscA2Level)!.Load(new LaneSnapshot([0.2f, 0.8f, 0.5f], LaneMode.Automated, 1f));
        lanes.ForParameter(ParamId.SectionACutoff)!.Load(new LaneSnapshot([500f, 5000f, 2000f, 900f, 300f], LaneMode.Automated, 1f));
        Render(engine, SampleRate);
        Assert.True(lanes.ForParameter(ParamId.OscA2Level)!.Phase > 0.01);

        engine.Parameters.Set(ParamId.RestartNow, 1f);
        Render(engine, 512);
        Assert.True(lanes.ForParameter(ParamId.OscA2Level)!.Phase < 0.02);
        Assert.True(lanes.ForParameter(ParamId.SectionACutoff)!.Phase < 0.02);
        Assert.Equal(0f, engine.Parameters.Get(ParamId.RestartNow));
    }

    [Fact]
    public void The_Randomizer_Lanes_Scope_Automates_Some_Faders_And_Respects_Locks()
    {
        var engine = Make();
        engine.Locks[(int)ParamId.OscA1Level] = true;
        Randomizer.Randomize(engine, 21, RandomizeScope.Lanes);
        Render(engine, 512);

        var playing = engine.Lanes.All.Count(l => l.IsPlaying);
        Assert.InRange(playing, 5, engine.Lanes.Count - 5);
        Assert.False(engine.Lanes.ForParameter(ParamId.OscA1Level)!.IsPlaying, "a locked fader keeps its lane");
        Assert.Equal(ParameterTable.Get(ParamId.OscA1Level).Default, engine.Parameters.Get(ParamId.OscA1Level));
        foreach (var lane in engine.Lanes.All.Where(l => l.IsPlaying))
            Assert.InRange(lane.LengthSeconds, 4f, 40f);
    }

    [Fact]
    public void Mutate_Nudges_Without_Replacing()
    {
        var bank = new ParameterBank();
        var before = new float[bank.Count];
        bank.CopyTo(before);
        Randomizer.Mutate(bank, 9, 0.5f);

        var changed = 0;
        foreach (var info in ParameterTable.All.Where(p => p.Kind == ParamKind.Continuous))
        {
            var delta = MathF.Abs(info.ToNormalized(bank.Get(info.Id)) - info.ToNormalized(before[info.Index]));
            Assert.True(delta <= 0.051f, $"{info.Id} moved by {delta}");
            if (delta > 0f)
                changed++;
        }

        Assert.True(changed > 100, $"only {changed} parameters moved");
        Assert.Equal(before[(int)ParamId.OscA1Wave], bank.Get(ParamId.OscA1Wave));   // choices are left alone
    }
}
