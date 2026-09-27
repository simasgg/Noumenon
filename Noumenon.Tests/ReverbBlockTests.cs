using Noumenon.Dsp.Reverb;
using Noumenon.Dsp.Shared;

namespace Noumenon.Tests;

public class ReverbBlockTests
{
    private const int SampleRate = 48000;

    private static ReverbBlock Make(int room = 2, float mix = 1f, float size = 0.7f, float damping = 0.4f, bool freeze = false, bool enabled = true)
    {
        var r = new ReverbBlock();
        r.Prepare(SampleRate);
        r.SetEnabled(enabled);
        r.SetMix(mix);
        r.SetRoom(room);
        r.SetSize(size);
        r.SetDamping(damping);
        r.SetFreeze(freeze);
        r.Reset();

        return r;
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

    private static float[] Impulse(ReverbBlock r, int n)
    {
        var y = new float[n];
        for (var i = 0; i < n; i++)
        {
            var l = i == 0 ? 0.5f : 0f;
            var right = l;
            r.Process(ref l, ref right);
            Assert.True(float.IsFinite(l) && float.IsFinite(right), $"non-finite at {i}");
            y[i] = l;
        }

        return y;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Every_Room_Rings_Finite_And_Decays(int room)
    {
        Assert.Equal(6, ReverbRooms.Count);
        var y = Impulse(Make(room), SampleRate * 4);
        var early = WindowRms(y, SampleRate / 4, SampleRate * 3 / 4);
        var late = WindowRms(y, SampleRate * 7 / 2, SampleRate * 4);

        Assert.True(early > 0, $"{ReverbRooms.All[room].Name} produced no tail");
        Assert.True(late < early * 0.5, $"{ReverbRooms.All[room].Name} did not decay (early {early}, late {late})");
    }

    [Fact]
    public void Switching_Rooms_Across_Engines_Is_Click_Free()
    {
        var r = Make(room: 2, mix: 0.5f);
        var y = new float[SampleRate * 3 / 2];
        var maxBefore = 0f;
        var maxAfter = 0f;
        for (var i = 0; i < y.Length; i++)
        {
            if (i == SampleRate)
                r.SetRoom(1);   // Hall (plate) → Chamber (Freeverb)

            var l = 0.3f * MathF.Sin(DspHelper.TwoPi * 200f * i / SampleRate);
            var right = l;
            r.Process(ref l, ref right);
            y[i] = l;
            if (i == 0)
                continue;

            var step = MathF.Abs(y[i] - y[i - 1]);
            if (i >= SampleRate / 2 && i < SampleRate)
                maxBefore = MathF.Max(maxBefore, step);
            else if (i >= SampleRate && i < SampleRate + SampleRate / 5)
                maxAfter = MathF.Max(maxAfter, step);
        }

        Assert.Equal("Chamber", r.Room.Name);
        Assert.True(maxAfter <= maxBefore * 2f + 0.02f, $"room switch stepped by {maxAfter} (baseline {maxBefore})");
    }

    [Fact]
    public void Freeze_Holds_The_Tail()
    {
        var r = Make(room: 2);
        var y = new float[SampleRate * 4];
        for (var i = 0; i < y.Length; i++)
        {
            if (i == SampleRate / 2)
                r.SetFreeze(true);

            var l = i < SampleRate / 2 ? 0.3f * MathF.Sin(DspHelper.TwoPi * 300f * i / SampleRate) : 0f;
            var right = l;
            r.Process(ref l, ref right);
            y[i] = l;
        }

        var held = WindowRms(y, SampleRate, SampleRate * 3 / 2);
        var later = WindowRms(y, SampleRate * 7 / 2, SampleRate * 4);
        Assert.True(held > 0.01, "no tail to hold");
        Assert.InRange(later, held * 0.3, held * 3.0);
    }

    [Fact]
    public void Mix_Zero_And_Off_Are_Exact_Dry()
    {
        foreach (var r in new[] { Make(mix: 0f), Make(enabled: false) })
        {
            var rng = new Xoshiro128(8);
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
    public void Process_Does_Not_Allocate_Across_Room_Switches()
    {
        var r = Make();
        for (var k = 0; k < 20; k++)
            Impulse(r, 512);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var k = 0; k < 600; k++)
        {
            if (k % 100 == 0)
                r.SetRoom(k / 100);

            r.SetSize((k & 1) == 0 ? 0.5f : 0.8f);
            r.SetFreeze(k % 300 == 150);
            for (var i = 0; i < 512; i++)
            {
                var l = 0.1f;
                var right = -0.1f;
                r.Process(ref l, ref right);
            }
        }

        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }
}
