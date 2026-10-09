using System.Runtime.InteropServices;
using NAudio.Wave;

namespace Noumenon.Engine.Recording;

public enum WavSampleFormat
{
    Pcm24,
    Float32,
}

/// <summary>
/// Float-to-file conversion shared by the live recorder and the offline bounce: interleaved
/// samples become little-endian 24-bit PCM (rounded, clipped at full scale, NaN muted) or raw
/// 32-bit IEEE float in a caller-owned byte buffer, so neither path allocates per block. NAudio's
/// own per-sample writer allocates for every 24-bit sample, which a twenty-minute recording
/// cannot afford.
/// </summary>
public static class WavSamples
{
    public const int Channels = 2;

    public static int BytesPerSample(WavSampleFormat format) => format == WavSampleFormat.Pcm24 ? 3 : 4;

    public static WaveFormat WaveFormatFor(WavSampleFormat format, int sampleRate, int channels = Channels) =>
        format == WavSampleFormat.Pcm24 ? new WaveFormat(sampleRate, 24, channels) : WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);

    /// <summary>Converts <paramref name="samples"/> into <paramref name="destination"/> and returns the number of bytes written.</summary>
    public static int Convert(ReadOnlySpan<float> samples, WavSampleFormat format, Span<byte> destination)
    {
        if (format == WavSampleFormat.Float32)
        {
            var bytes = MemoryMarshal.AsBytes(samples);
            bytes.CopyTo(destination);

            return bytes.Length;
        }

        var p = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            var v = samples[i];
            if (float.IsNaN(v))
                v = 0f;
            else if (v > 1f)
                v = 1f;
            else if (v < -1f)
                v = -1f;

            var s = (int)MathF.Round(v * 8388607f);
            destination[p] = (byte)s;
            destination[p + 1] = (byte)(s >> 8);
            destination[p + 2] = (byte)(s >> 16);
            p += 3;
        }

        return p;
    }
}
