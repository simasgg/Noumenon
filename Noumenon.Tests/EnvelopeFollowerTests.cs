using Noumenon.Dsp.Modulation;

namespace Noumenon.Tests;

public class EnvelopeFollowerTests
{
    private const int SampleRate = 48000;

    private static EnvelopeFollower Make(float attackMs, float releaseMs)
    {
        var f = new EnvelopeFollower();
        f.Prepare(SampleRate);
        f.SetTimes(attackMs, releaseMs);

        return f;
    }

    [Fact]
    public void Attack_Reaches_Two_Thirds_In_One_Time_Constant()
    {
        var f = Make(10f, 200f);
        var atOneTau = 0f;
        for (var i = 0; i < SampleRate / 100; i++)
            atOneTau = f.Process(1f);

        Assert.InRange(atOneTau, 0.6f, 0.66f);

        for (var i = 0; i < SampleRate / 10; i++)
            f.Process(1f);

        Assert.InRange(f.Value, 0.99f, 1.0001f);
    }

    [Fact]
    public void Release_Decays_Back_To_Silence()
    {
        var f = Make(1f, 100f);
        for (var i = 0; i < SampleRate / 10; i++)
            f.Process(-0.8f);   // rectified: a negative input still raises the envelope

        Assert.InRange(f.Value, 0.79f, 0.8f);

        for (var i = 0; i < SampleRate; i++)
            f.Process(0f);

        Assert.True(f.Value < 1e-4f, $"envelope did not release: {f.Value}");
    }

    [Fact]
    public void Follows_A_Sine_Near_Its_Peak_With_A_Slow_Release()
    {
        var f = Make(5f, 300f);
        var value = 0f;
        for (var i = 0; i < SampleRate; i++)
            value = f.Process(0.9f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));

        Assert.InRange(value, 0.8f, 0.9f);
    }
}
