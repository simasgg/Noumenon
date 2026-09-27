using System.Runtime.CompilerServices;

namespace Noumenon.Dsp.Filters;

/// <summary>
/// The 127-tap Blackman-windowed-sinc half-band prototype shared by the 2× resamplers. Every other
/// tap is zero except the centre (0.5), so the polyphase forms below need only the
/// <see cref="Coefficients"/> of the 64 even taps plus one delayed copy of the input. Normalized to
/// exact DC unity; stopband around −74 dB, flat to roughly 0.4 × the lower rate.
/// </summary>
public static class HalfBand
{
    /// <summary>Half the prototype length: the centre index, an odd number so the outermost taps are non-zero.</summary>
    public const int Centre = 63;

    public const int Taps = 2 * Centre + 1;

    /// <summary>The even-tap coefficients h[2i], i = 0..Centre; symmetric.</summary>
    public static readonly float[] Coefficients = Build();

    /// <summary>Delay of the pure-copy phase, in samples of the lower rate.</summary>
    public const int CopyDelay = (Centre - 1) / 2;

    /// <summary>Group delay of an up-then-down round trip, in samples of the lower rate.</summary>
    public const int RoundTripLatency = Centre;

    private static float[] Build()
    {
        var count = Centre + 1;
        var c = new double[count];
        var sum = 0.0;
        for (var i = 0; i < count; i++)
        {
            var k = 2 * i;
            var j = k - Centre;   // odd offset from the centre
            var sinc = Math.Sin(Math.PI * j / 2.0) / (Math.PI * j);
            var window = 0.42 - 0.5 * Math.Cos(2.0 * Math.PI * k / (Taps - 1)) + 0.08 * Math.Cos(4.0 * Math.PI * k / (Taps - 1));
            c[i] = sinc * window;
            sum += c[i];
        }

        var scale = 0.5 / sum;   // the even taps carry half the DC gain, the centre tap the other half
        var result = new float[count];
        for (var i = 0; i < count; i++)
            result[i] = (float)(c[i] * scale);

        return result;
    }
}

/// <summary>
/// 1 → 2 upsampler: for each input sample it produces the half-sample interpolation (the even
/// polyphase branch, 64 multiplies) and a straight delayed copy. Both phases sit 31.5 input samples
/// behind the input.
/// </summary>
public sealed class HalfBandUpsampler
{
    private readonly float[] history = new float[HalfBand.Centre + 1];
    private int index;

    public void Clear()
    {
        Array.Clear(history);
        index = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Process(float input, out float first, out float second)
    {
        var length = history.Length;
        history[index] = input;

        var coefficients = HalfBand.Coefficients;
        var acc = 0f;
        var pos = index;
        for (var i = 0; i < length; i++)
        {
            acc += coefficients[i] * history[pos];
            if (--pos < 0)
                pos += length;
        }

        var copy = index - HalfBand.CopyDelay;
        if (copy < 0)
            copy += length;

        first = 2f * acc;
        second = history[copy];

        if (++index >= length)
            index = 0;
    }
}

/// <summary>
/// 2 → 1 downsampler: consumes a pair of high-rate samples and returns one low-rate sample, the
/// half-band filtered value 63 high-rate samples back (the even-branch convolution plus the
/// centre tap on the odd branch).
/// </summary>
public sealed class HalfBandDownsampler
{
    private readonly float[] even = new float[HalfBand.Centre + 1];
    private readonly float[] odd = new float[(HalfBand.Centre + 1) / 2 + 1];
    private int evenIndex;
    private int oddIndex;

    public void Clear()
    {
        Array.Clear(even);
        Array.Clear(odd);
        evenIndex = 0;
        oddIndex = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Process(float first, float second)
    {
        var length = even.Length;
        even[evenIndex] = first;
        odd[oddIndex] = second;

        var coefficients = HalfBand.Coefficients;
        var acc = 0f;
        var pos = evenIndex;
        for (var i = 0; i < length; i++)
        {
            acc += coefficients[i] * even[pos];
            if (--pos < 0)
                pos += length;
        }

        var centre = oddIndex - (HalfBand.Centre + 1) / 2;
        if (centre < 0)
            centre += odd.Length;

        acc += 0.5f * odd[centre];

        if (++evenIndex >= length)
            evenIndex = 0;

        if (++oddIndex >= odd.Length)
            oddIndex = 0;

        return acc;
    }
}
