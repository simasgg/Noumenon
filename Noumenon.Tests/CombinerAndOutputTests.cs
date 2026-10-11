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
    public void Mute_Fades_Out_Over_Its_Time_And_Back_In()
    {
        var o = MakeOutput();
        o.SetFade(0.5f);
        var i = 0;
        for (; i < SampleRate / 10; i++)
            Run(o, ref i);

        // Out: silent at the end of the fade, a gentle slope on the way (no step bigger than the glide allows).
        o.SetMute(true);
        var maxStep = 0f;
        var prev = Run(o, ref i);
        var halfway = 0f;
        for (var end = i + SampleRate / 2; i < end; i++)
        {
            var l = Run(o, ref i);
            maxStep = MathF.Max(maxStep, MathF.Abs(l - prev));
            prev = l;
            if (i == end - SampleRate / 4)
                halfway = o.FadeLevel;
        }

        Assert.Equal(0f, o.FadeLevel);
        Assert.True(MathF.Abs(halfway - 0.5f) < 0.01f, $"halfway through the fade the level was {halfway}");
        Assert.True(maxStep < 0.03f, $"the fade stepped by {maxStep}");
        var silent = Run(o, ref i);
        Assert.Equal(0f, silent);

        // In: back at unity after the same time, and the signal is the input again.
        o.SetMute(false);
        for (var end = i + SampleRate / 2; i < end; i++)
            Run(o, ref i);

        Assert.Equal(1f, o.FadeLevel);
        var l2 = 0.5f;
        var r2 = 0.5f;
        o.Process(ref l2, ref r2);
        Assert.True(MathF.Abs(l2 - 0.5f) < 0.01f, $"after the fade in the output was {l2}");
    }

    [Fact]
    public void Mute_With_No_Fade_Takes_The_Glide_Time()
    {
        var o = MakeOutput();
        o.SetFade(0f);
        var i = 0;
        for (; i < 100; i++)
            Run(o, ref i);

        o.SetMute(true);
        var first = Run(o, ref i);
        Assert.True(MathF.Abs(first) > 0.1f, $"the first sample after a mute was {first}: a mute glides, it never jumps");
        for (var end = i + SampleRate / 40; i < end; i++)
            Run(o, ref i);

        Assert.Equal(0f, o.FadeLevel);
        Assert.Equal(0f, Run(o, ref i));
    }

    [Fact]
    public void Pause_Is_A_Second_Switch_Into_The_Same_Fade()
    {
        var o = MakeOutput();
        o.SetFade(0.5f);
        var i = 0;
        o.SetPause(true);
        for (var end = i + SampleRate / 4; i < end; i++)
            Run(o, ref i);

        Assert.True(MathF.Abs(o.FadeLevel - 0.5f) < 0.01f, $"a quarter second into a half-second fade the level was {o.FadeLevel}");
        o.SetMute(true);
        o.SetPause(false);   // still muted: the fade keeps going out
        for (var end = i + SampleRate / 4; i < end; i++)
            Run(o, ref i);

        Assert.Equal(0f, o.FadeLevel);
        o.SetMute(false);
        for (var end = i + SampleRate / 2; i < end; i++)
            Run(o, ref i);

        Assert.Equal(1f, o.FadeLevel);
    }

    private static float Run(OutputStage o, ref int i)
    {
        var l = 0.5f * MathF.Sin(i * 0.05f);
        var r = l;
        o.Process(ref l, ref r);

        return l;
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
