using Noumenon.Dsp.Mix;
using Noumenon.Dsp.Output;

namespace Noumenon.Tests;

public class CombinerAndOutputTests
{
    private const int SampleRate = 48000;

    private static Combiner MakeCombiner(float crossfade, float balance = 0f, float ring = 1f)
    {
        var c = new Combiner();
        c.Prepare(SampleRate);
        c.SetCrossfade(crossfade);
        c.SetBalance(balance);
        c.SetRingLevel(ring);
        c.Reset();

        return c;
    }

    [Fact]
    public void Crossfade_Zero_Is_The_Sum()
    {
        Assert.Equal(0.7f, MakeCombiner(0f).Process(0.4f, 0.3f), 5);
    }

    [Fact]
    public void Crossfade_One_Is_The_Ring_Mod_With_Makeup()
    {
        Assert.Equal(0.4f * 0.3f * Combiner.RingMakeup, MakeCombiner(1f).Process(0.4f, 0.3f), 5);
        Assert.Equal(0.4f * 0.3f * Combiner.RingMakeup * 2f, MakeCombiner(1f, ring: 2f).Process(0.4f, 0.3f), 5);
    }

    [Fact]
    public void Balance_Tilts_The_Summed_Path()
    {
        Assert.Equal(0.4f, MakeCombiner(0f, balance: -1f).Process(0.4f, 0.3f), 5);
        Assert.Equal(0.3f, MakeCombiner(0f, balance: 1f).Process(0.4f, 0.3f), 5);
        Assert.Equal(0.4f + 0.15f, MakeCombiner(0f, balance: -0.5f).Process(0.4f, 0.3f), 5);
    }

    [Fact]
    public void Crossfade_Change_Glides()
    {
        var c = MakeCombiner(0f);
        c.SetCrossfade(1f);
        var first = c.Process(0.4f, 0.3f);
        for (var i = 0; i < SampleRate; i++)
            c.Process(0.4f, 0.3f);

        var settled = c.Process(0.4f, 0.3f);
        Assert.True(first > 0.6f, "the first sample after the change should still be almost the sum");
        Assert.Equal(0.4f * 0.3f * Combiner.RingMakeup, settled, 5);
    }

    private static OutputStage MakeOutput(float volumeDb = 0f, float width = 1f, bool mute = false)
    {
        var o = new OutputStage();
        o.Prepare(SampleRate);
        o.SetVolumeDb(volumeDb);
        o.SetWidth(width);
        o.SetMute(mute);
        o.Reset();

        return o;
    }

    [Fact]
    public void Safety_Limiter_Never_Lets_A_Sample_Over_The_Ceiling()
    {
        var limiter = new PeakLimiter();
        limiter.Prepare(SampleRate, 0.2f);
        var peak = 0f;
        for (var i = 0; i < SampleRate; i++)
        {
            var l = 3f * MathF.Sin(i * 0.05f);
            var r = -2.5f * MathF.Sin(i * 0.031f);
            limiter.Process(ref l, ref r);
            peak = MathF.Max(peak, MathF.Max(MathF.Abs(l), MathF.Abs(r)));
        }

        Assert.True(peak <= 0.981f, $"peak {peak}");
        Assert.True(peak > 0.9f, "the limiter should still let the signal reach the ceiling");
    }

    [Fact]
    public void Mute_Fades_To_Exact_Silence()
    {
        var o = MakeOutput();
        var maxStep = 0f;
        var prev = 0f;
        var i = 0;
        for (; i < SampleRate / 4; i++)
        {
            var l = 0.5f * MathF.Sin(i * 0.05f);
            var r = l;
            o.Process(ref l, ref r);
            prev = l;
        }

        o.SetMute(true);
        var last = 1f;
        for (var end = i + SampleRate / 2; i < end; i++)
        {
            var l = 0.5f * MathF.Sin(i * 0.05f);   // the input keeps its phase; only the gain changes
            var r = l;
            o.Process(ref l, ref r);
            maxStep = MathF.Max(maxStep, MathF.Abs(l - prev));
            prev = l;
            last = l;
        }

        Assert.Equal(0f, last);
        Assert.True(maxStep < 0.03f, $"mute clicked: max step {maxStep}");
    }

    [Fact]
    public void Volume_At_Silence_Is_Zero_And_Dc_Is_Removed()
    {
        var silent = MakeOutput(volumeDb: -60f);
        var l = 0.5f;
        var r = 0.5f;
        silent.Process(ref l, ref r);
        Assert.Equal(0f, l);

        var o = MakeOutput();
        var y = 0f;
        for (var i = 0; i < SampleRate; i++)
        {
            l = 0.5f;
            r = 0.5f;
            o.Process(ref l, ref r);
            y = l;
        }

        Assert.True(MathF.Abs(y) < 0.01f, $"DC leaked through: {y}");
    }

    [Fact]
    public void Width_Zero_Folds_To_Mono_And_Two_Doubles_The_Side()
    {
        var mono = MakeOutput(width: 0f);
        var l = 0.6f;
        var r = -0.6f;
        mono.Process(ref l, ref r);
        Assert.Equal(l, r, 5);

        var wide = MakeOutput(width: 2f);
        l = 0.3f;
        r = 0.1f;
        wide.Process(ref l, ref r);
        Assert.Equal(0.4f, l, 5);   // mid 0.2 + side 0.1·2
        Assert.Equal(0.0f, r, 5);
    }
}
