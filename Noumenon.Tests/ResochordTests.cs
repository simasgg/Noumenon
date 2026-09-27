using Noumenon.Dsp.Effects;
using Noumenon.Dsp.Shared;

namespace Noumenon.Tests;

public class ResochordTests
{
    private const int SampleRate = 48000;
    private const int FftSize = 131072;
    private const double BinHz = (double)SampleRate / FftSize;

    private static Resochord Make(float damping = 0.3f, float globalFeedback = 0.99f, float mix = 1f, bool enabled = true, int chord = 0, float soloNote = float.NaN)
    {
        var r = new Resochord();
        r.Prepare(SampleRate);
        r.SetEnabled(enabled);
        r.SetMix(mix);
        r.SetGlobalFeedback(globalFeedback, 0f);
        r.SetChord(chord);
        for (var v = 0; v < Resochord.VoiceCount; v++)
        {
            var solo = !float.IsNaN(soloNote);
            r.SetVoice(v, solo ? soloNote : 48f + 4f * v, 0.95f, damping, solo && v > 0 ? 0f : 1f, 0f);
        }

        r.Reset();

        return r;
    }

    private static float[] Ring(Resochord r, int n, float impulse = 0.3f)
    {
        var y = new float[n];
        for (var i = 0; i < n; i++)
        {
            var l = i == 0 ? impulse : 0f;
            var right = l;
            r.Process(ref l, ref right);
            y[i] = l;
        }

        return y;
    }

    private static double Cents(double hz, double reference) => 1200.0 * Math.Log2(hz / reference);

    [Theory]
    [InlineData(0f)]
    [InlineData(0.5f)]
    [InlineData(0.9f)]
    public void A3_Rings_At_220Hz_Within_Two_Cents(float damping)
    {
        var y = Ring(Make(damping: damping, soloNote: 57f), FftSize);
        var peak = Spectrum.PeakFrequency(Spectrum.MagnitudeDb(y, FftSize), BinHz, 200, 240);
        var cents = Cents(peak, 220.0);

        Assert.True(Math.Abs(cents) <= 2.0, $"damping {damping}: rang at {peak:F3} Hz, {cents:F2} cents off");
    }

    [Fact]
    public void The_Major_Chord_Rings_At_All_Six_Notes()
    {
        var y = Ring(Make(chord: 1), FftSize);
        var mags = Spectrum.MagnitudeDb(y, FftSize);
        foreach (var note in ChordTable.All[1].Notes)
        {
            var hz = Tuning.NoteToHz(note);
            var peak = Spectrum.PeakFrequency(mags, BinHz, hz * 0.97, hz * 1.03);
            Assert.True(Math.Abs(Cents(peak, hz)) <= 10.0, $"note {note}: expected {hz:F2} Hz, peak at {peak:F2} Hz");
        }
    }

    [Fact]
    public void Chord_Table_Is_Well_Formed_And_Select_Maps_Like_Fabrications()
    {
        Assert.Equal("Voices", ChordTable.All[0].Name);
        foreach (var chord in ChordTable.All)
        {
            Assert.Equal(ChordTable.VoiceCount, chord.Notes.Length);
            Assert.All(chord.Notes, n => Assert.InRange(n, 24f, 108f));
        }

        Assert.Equal(0, ChordTable.IndexFromPitch(-12f));
        Assert.Equal(0, ChordTable.IndexFromPitch(-48f));
        Assert.Equal(1, ChordTable.IndexFromPitch(-11f));
        Assert.Equal(12, ChordTable.IndexFromPitch(0f));
        Assert.Equal(ChordTable.Count - 1, ChordTable.IndexFromPitch(100f));
    }

    [Fact]
    public void Effective_Notes_Follow_The_Chord_Or_The_Voices()
    {
        var r = Make(chord: 0);
        Assert.Equal(48f, r.EffectiveNote(0));
        r.SetChord(5);   // Fifths
        Assert.Equal(ChordTable.All[5].Notes[3], r.EffectiveNote(3));
    }

    [Fact]
    public void Mix_Zero_And_Off_Are_Exact_Dry()
    {
        foreach (var r in new[] { Make(mix: 0f), Make(enabled: false) })
        {
            var rng = new Xoshiro128(6);
            for (var i = 0; i < 2048; i++)
            {
                var l = rng.NextBipolar();
                var right = rng.NextBipolar();
                var inL = l;
                var inR = right;
                r.Process(ref l, ref right);
                Assert.Equal(inL, l);
                Assert.Equal(inR, right);
            }
        }
    }

    [Fact]
    public void Full_Feedback_With_A_Hot_Input_Stays_Bounded()
    {
        var r = Make(damping: 0f, globalFeedback: 0.99f);
        var rng = new Xoshiro128(11);
        var peak = 0f;
        for (var i = 0; i < SampleRate * 2; i++)
        {
            var l = rng.NextBipolar();
            var right = rng.NextBipolar();
            r.Process(ref l, ref right);
            Assert.True(float.IsFinite(l) && float.IsFinite(right), $"non-finite at {i}");
            peak = MathF.Max(peak, MathF.Max(MathF.Abs(l), MathF.Abs(right)));
        }

        Assert.True(peak < 5f, $"peak {peak}");
    }

    [Fact]
    public void Process_Does_Not_Allocate_Across_Chord_And_Pitch_Changes()
    {
        var r = Make();
        for (var k = 0; k < 20; k++)
            Ring(r, 512);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var k = 0; k < 400; k++)
        {
            r.SetChord(k % ChordTable.Count);
            r.SetPitchOffset((k & 1) == 0 ? 0f : 7f);
            r.SetGlobalFeedback(0.5f + 0.4f * (k & 1), 0f);
            r.SetVoice(0, 48f + (k & 3), 0.9f, 0.3f + 0.1f * (k & 1), 0.8f, (k & 1) == 0 ? -0.5f : 0.5f);
            for (var i = 0; i < 512; i++)
            {
                var l = 0.1f;
                var right = 0.1f;
                r.Process(ref l, ref right);
            }
        }

        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }
}
