using Noumenon.Dsp.Sampler;

namespace Noumenon.Tests;

/// <summary>
/// The sampler's playback contract: sample-exact at unity, loops and speeds as the panel says,
/// seams and restarts click-free, the Samp↔In crossfade equal-power, and nothing allocated while
/// samples come and go.
/// </summary>
public class SamplePlayerTests
{
    private const int SampleRate = 48000;

    private static SampleData Ramp(int frames, int rate = SampleRate)
    {
        var data = new float[frames];
        for (var i = 0; i < frames; i++)
            data[i] = i / (float)frames;

        return new SampleData("ramp", rate, data);
    }

    private static SampleData Sine(int frames, float hz, float amplitude = 0.5f, int rate = SampleRate)
    {
        var data = new float[frames];
        for (var i = 0; i < frames; i++)
            data[i] = amplitude * MathF.Sin(2f * MathF.PI * hz * i / rate);

        return new SampleData("sine", rate, data);
    }

    private static SampleData Constant(int frames, float value) => new("constant", SampleRate, Enumerable.Repeat(value, frames).ToArray());

    private static SamplePlayer Make(SampleData? data, float pitch = 0f, float dir = 1f, float loopStart = 0f, float loopEnd = 1f, float crossfadeMs = 0f, float gain = 1f, float inputMix = 0f, bool stereo = true, int rate = SampleRate)
    {
        var player = new SamplePlayer();
        player.Prepare(rate);
        player.SetSample(data);
        player.SetLoop(loopStart, loopEnd, crossfadeMs);
        player.SetSpeed(pitch, dir);
        player.SetGain(gain);
        player.SetInputMix(inputMix);
        player.SetStereo(stereo);
        player.Reset();

        return player;
    }

    private static (float[] left, float[] right) Render(SamplePlayer player, int n, float[]? inputLeft = null, float[]? inputRight = null)
    {
        var l = new float[n];
        var r = new float[n];
        for (var i = 0; i < n; i++)
        {
            player.Process(inputLeft?[i] ?? 0f, inputRight?[i] ?? 0f, out l[i], out r[i], out _);
        }

        return (l, r);
    }

    private static float MaxStep(float[] x, int from = 1)
    {
        var max = 0f;
        for (var i = Math.Max(1, from); i < x.Length; i++)
            max = MathF.Max(max, MathF.Abs(x[i] - x[i - 1]));

        return max;
    }

    [Fact]
    public void Unity_Playback_Is_Sample_Exact()
    {
        var data = Ramp(1000);
        var (l, r) = Render(Make(data), 1000);
        for (var i = 0; i < 1000; i++)
        {
            Assert.Equal(data.Left[i], l[i]);
            Assert.Equal(data.Left[i], r[i]);
        }
    }

    [Fact]
    public void Loop_Region_Repeats_Exactly()
    {
        var data = Ramp(1000);
        var player = Make(data, loopStart: 0.25f, loopEnd: 0.75f);
        Assert.Equal(250, player.LoopStartFrame);
        Assert.Equal(750, player.LoopEndFrame);

        var (l, _) = Render(player, 2000);
        for (var i = 0; i < 2000; i++)
            Assert.Equal(data.Left[250 + i % 500], l[i]);
    }

    [Fact]
    public void Reverse_At_Half_Speed_Walks_Back_Through_The_Loop()
    {
        var data = Ramp(1000);
        var (l, _) = Render(Make(data, dir: -0.5f), 1000);
        for (var k = 0; k < 499; k++)
        {
            Assert.Equal(data.Left[999 - k], l[2 * k]);
            if (k > 0)
                Assert.Equal((data.Left[999 - k] + data.Left[998 - k]) * 0.5f, l[2 * k + 1], 5);   // a straight line interpolates exactly (away from the clamped last frame)
        }
    }

    [Fact]
    public void Direction_Zero_Freezes_The_Playhead()
    {
        var data = Ramp(1000);
        var (l, _) = Render(Make(data, dir: 0f, loopStart: 0.5f), 500);
        Assert.All(l, v => Assert.Equal(data.Left[500], v));
    }

    [Fact]
    public void Pitch_Up_An_Octave_Doubles_The_Step()
    {
        var data = Ramp(1000);
        var (l, _) = Render(Make(data, pitch: 12f), 1500);
        for (var i = 0; i < 1500; i++)
            Assert.Equal(data.Left[(2 * i) % 1000], l[i]);
    }

    [Fact]
    public void A_Sample_At_Twice_The_Rate_Plays_At_Its_Own_Speed()
    {
        var data = Ramp(2000, rate: 96000);
        var (l, _) = Render(Make(data), 1000);
        for (var i = 0; i < 1000; i++)
            Assert.Equal(data.Left[2 * i], l[i]);
    }

    [Fact]
    public void Loop_Crossfade_Removes_The_Seam_Click()
    {
        var data = Sine(4800, 997f);   // 99.7 cycles: the loop seam is a hard discontinuity
        var (hard, _) = Render(Make(data), 4800 * 3);
        var (soft, _) = Render(Make(data, crossfadeMs: 20f), 4800 * 3);

        Assert.True(MaxStep(hard) > 0.3f, $"test setup: the plain seam should click, max step {MaxStep(hard)}");
        Assert.True(MaxStep(soft) < 0.15f, $"crossfaded seam still steps by {MaxStep(soft)}");
    }

    [Fact]
    public void Stereo_Switch_Sums_To_Mono()
    {
        var left = Ramp(1000).Left;
        var right = left.Select(v => 0.5f * v).ToArray();
        var data = new SampleData("stereo", SampleRate, left, right);
        Assert.True(data.IsStereo);

        var (l, r) = Render(Make(data), 1000);
        for (var i = 0; i < 1000; i++)
        {
            Assert.Equal(left[i], l[i]);
            Assert.Equal(right[i], r[i]);
        }

        var (ml, mr) = Render(Make(data, stereo: false), 1000);
        for (var i = 0; i < 1000; i++)
        {
            Assert.Equal(0.75f * left[i], ml[i], 5);
            Assert.Equal(ml[i], mr[i]);
        }
    }

    [Fact]
    public void Retrigger_Restarts_From_The_Loop_Start_Without_A_Click()
    {
        var data = Sine(4800, 1000f);   // 100 whole cycles: the loop seam itself is continuous, so any step is the retrigger's
        var player = Make(data);
        Render(player, 1000);
        player.Retrigger();
        var (l, _) = Render(player, 12000);

        Assert.True(MaxStep(l) < 0.2f, $"retrigger clicked: max step {MaxStep(l)}");
        for (var k = 8000; k < 12000; k++)
            Assert.Equal(data.Left[k % 4800], l[k]);   // the tail is gone and the new head's fade has snapped to exactly 1
    }

    [Fact]
    public void Switching_Samples_Crossfades_To_The_New_One()
    {
        var a = Sine(4800, 997f);
        var b = Sine(4800, 400f);
        var player = Make(a);
        Render(player, 1000);
        player.SetSample(b);
        var (l, _) = Render(player, 12000);

        Assert.Same(b, player.Sample);
        Assert.True(MaxStep(l) < 0.2f, $"sample switch clicked: max step {MaxStep(l)}");
        for (var k = 8000; k < 12000; k++)
            Assert.Equal(b.Left[k % 4800], l[k]);
    }

    [Fact]
    public void No_Sample_Is_Silent_But_The_Input_Still_Passes()
    {
        var (l, r) = Render(Make(null), 500);
        Assert.All(l, v => Assert.Equal(0f, v));
        Assert.All(r, v => Assert.Equal(0f, v));

        var input = Enumerable.Range(0, 500).Select(i => 0.3f * MathF.Sin(i * 0.1f)).ToArray();
        var (il, ir) = Render(Make(null, inputMix: 1f), 500, input, input);
        for (var i = 0; i < 500; i++)
        {
            Assert.Equal(input[i], il[i], 6);
            Assert.Equal(input[i], ir[i], 6);
        }
    }

    [Fact]
    public void Input_Mix_Is_An_Equal_Power_Crossfade()
    {
        var data = Constant(100, 1f);
        var ones = Enumerable.Repeat(1f, 100).ToArray();

        Assert.Equal(1f, Render(Make(data, inputMix: 0f), 100, ones, ones).left[50], 5);
        Assert.Equal(1f, Render(Make(data, inputMix: 1f), 100, ones, ones).left[50], 5);
        Assert.Equal(MathF.Sqrt(2f), Render(Make(data, inputMix: 0.5f), 100, ones, ones).left[50], 4);
    }

    [Fact]
    public void Loop_Change_That_Strands_The_Playhead_Restarts_It()
    {
        var data = Ramp(1000);
        var player = Make(data);
        Render(player, 900);
        Assert.Equal(900.0, player.Position);

        player.SetLoop(0f, 0.5f, 0f);
        Assert.Equal(0.0, player.Position);
        Render(player, 3000);
        Assert.Equal(0.0, player.Position);   // 3000 frames = six trips round a 500-frame loop
    }

    [Fact]
    public void Too_Short_A_Sample_Is_Treated_As_None()
    {
        var player = Make(new SampleData("tiny", SampleRate, new float[2]));
        Assert.Null(player.Sample);
        var (l, _) = Render(player, 100);
        Assert.All(l, v => Assert.Equal(0f, v));
    }

    [Fact]
    public void Process_Does_Not_Allocate()
    {
        var a = Sine(4800, 300f);
        var b = Sine(2400, 500f, rate: 44100);
        var player = Make(a, crossfadeMs: 15f);
        var inL = new float[512];
        var inR = new float[512];
        for (var k = 0; k < 20; k++)
            Render(player, 512, inL, inR);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var k = 0; k < 500; k++)
        {
            player.SetSample((k / 50 & 1) == 0 ? a : b);
            if (k % 37 == 0)
                player.Retrigger();

            player.SetLoop(0f, (k & 1) == 0 ? 1f : 0.6f, 15f);
            player.SetSpeed((k & 1) == 0 ? 0f : 7f, (k & 2) == 0 ? 1f : -0.5f);
            player.SetInputMix((k & 1) == 0 ? 0.2f : 0.7f);
            for (var i = 0; i < 512; i++)
                player.Process(inL[i], inR[i], out _, out _, out _);
        }

        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }
}
