using NAudio.Dsp;

namespace Noumenon.Ui.Analysis;

/// <summary>
/// A magnitude spectrum for the displays: a Hann-windowed 2048-point FFT of the newest samples
/// of a scope ring, in dB per bin where 0 dB is a full-scale sine, computed on the UI thread
/// thirty times a second. Every buffer is allocated once, and the log-frequency mapping the
/// displays share lives here so a mark and a spectrum peak land on the same pixel.
/// </summary>
public sealed class SpectrumAnalyzer
{
    public const int Size = 2048;
    public const float MinHz = 30f;
    public const float MaxHz = 8000f;
    public const float FloorDb = -72f;
    public const float CeilingDb = 12f;

    private const int Bits = 11;
    private const float FullScaleGain = 4f;   // NAudio scales a forward FFT by 1/n and the Hann window halves a tone's bin: a full-scale sine lands at 1/4

    private readonly Complex[] data = new Complex[Size];
    private readonly float[] window = new float[Size];

    public SpectrumAnalyzer()
    {
        for (var i = 0; i < Size; i++)
            window[i] = (float)FastFourierTransform.HannWindow(i, Size);
    }

    /// <summary>dB per bin, bin i at i × rate / <see cref="Size"/>; valid after <see cref="Compute"/>.</summary>
    public float[] Db { get; } = new float[Size / 2];

    /// <summary>The newest <see cref="Size"/> of the <paramref name="count"/> samples (zero-padded when fewer).</summary>
    public void Compute(float[] samples, int count)
    {
        var start = Math.Max(0, count - Size);
        for (var i = 0; i < Size; i++)
        {
            var k = start + i;
            data[i].X = k < count ? samples[k] * window[i] : 0f;
            data[i].Y = 0f;
        }

        FastFourierTransform.FFT(true, Bits, data);
        for (var i = 0; i < Size / 2; i++)
        {
            var re = data[i].X;
            var im = data[i].Y;
            var magnitude = MathF.Sqrt(re * re + im * im) * FullScaleGain;
            Db[i] = magnitude > 1e-6f ? 20f * MathF.Log10(magnitude) : -120f;
        }
    }

    /// <summary>0 at <see cref="MinHz"/>, 1 at <see cref="MaxHz"/>, logarithmic in between (clamped).</summary>
    public static float Position(float hz)
    {
        if (hz <= MinHz)
            return 0f;

        return Math.Min(1f, MathF.Log2(hz / MinHz) / MathF.Log2(MaxHz / MinHz));
    }

    public static float HzAt(float position) => MinHz * MathF.Pow(MaxHz / MinHz, Math.Clamp(position, 0f, 1f));

    /// <summary>0 at the floor, 1 at the ceiling (clamped).</summary>
    public static float Level(float db) => Math.Clamp((db - FloorDb) / (CeilingDb - FloorDb), 0f, 1f);
}
