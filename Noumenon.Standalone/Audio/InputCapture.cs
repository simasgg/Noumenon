using System.Buffers.Binary;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Noumenon.Engine.Recording;

namespace Noumenon.Standalone.Audio;

/// <summary>
/// A WASAPI capture endpoint feeding Samp↔In. The capture runs on NAudio's own thread in the
/// device's mix format; each chunk is decoded to stereo float, resampled with linear
/// interpolation if the capture rate differs from the engine's, and written to a
/// <see cref="SampleRing"/> that the audio callback pulls whole blocks from. The two devices run
/// on separate clocks, so the ring primes itself with about one buffer of lead before it starts
/// delivering, zero-fills and re-primes on an underrun, and drops on an overflow — drift costs a
/// rare blip on the input, never a glitch on the output.
/// </summary>
internal sealed class InputCapture : IInputSource, IDisposable
{
    private static readonly Guid PcmSubtype = new("00000001-0000-0010-8000-00aa00389b71");
    private static readonly Guid FloatSubtype = new("00000003-0000-0010-8000-00aa00389b71");

    private readonly WasapiCapture capture;
    private readonly SampleRing ring;
    private readonly int sourceRate;
    private readonly int sourceChannels;
    private readonly int bytesPerSample;
    private readonly bool isFloat;
    private readonly int targetRate;
    private readonly int primeSamples;
    private readonly float[] pull;
    private float[] decoded = new float[8192];
    private float[] resampled = new float[8192];
    private double resamplePhase = -1.0;
    private float previousLeft;
    private float previousRight;
    private bool primed;
    private long underruns;
    private long dropped;
    private string? error;

    public InputCapture(MMDevice device, int targetRate, int maxFrames, int latencyMs = 20)
    {
        capture = new WasapiCapture(device, true, latencyMs);
        var format = capture.WaveFormat;
        sourceRate = format.SampleRate;
        sourceChannels = Math.Max(1, format.Channels);
        bytesPerSample = Math.Max(1, format.BitsPerSample / 8);
        isFloat = IsFloat(format);
        this.targetRate = targetRate;
        ring = new SampleRing(targetRate * 2);
        primeSamples = (targetRate * latencyMs / 1000 + Math.Max(64, maxFrames)) * 2;
        pull = new float[Math.Max(64, maxFrames) * 2];
        Description = $"{device.FriendlyName} ({sourceRate} Hz, {sourceChannels} ch, {(isFloat ? "float" : format.BitsPerSample + "-bit")})";
        capture.DataAvailable += OnData;
        capture.RecordingStopped += OnStopped;
    }

    public string Description { get; }

    public long Underruns => Volatile.Read(ref underruns);

    public long Dropped => Volatile.Read(ref dropped);

    public string? Error => Volatile.Read(ref error);

    public void Start() => capture.StartRecording();

    public bool Pull(float[] left, float[] right, int frames)
    {
        var needed = frames * 2;
        if (needed > pull.Length)
            return false;

        if (!primed)
        {
            if (ring.Count < primeSamples)
                return false;

            primed = true;
        }

        var got = ring.Read(pull.AsSpan(0, needed));
        if (got < needed)
        {
            Array.Clear(pull, got, needed - got);
            primed = false;
            Volatile.Write(ref underruns, underruns + 1);
        }

        for (int i = 0, j = 0; i < frames; i++, j += 2)
        {
            left[i] = pull[j];
            right[i] = pull[j + 1];
        }

        return true;
    }

    public void Dispose()
    {
        capture.DataAvailable -= OnData;
        capture.RecordingStopped -= OnStopped;
        try
        {
            capture.StopRecording();
        }
        catch (Exception ex) when (AudioDevices.IsDeviceFailure(ex))
        {
        }

        capture.Dispose();
    }

    private static bool IsFloat(WaveFormat format)
    {
        if (format.Encoding == WaveFormatEncoding.IeeeFloat)
            return true;

        if (format is WaveFormatExtensible extensible)
            return extensible.SubFormat == FloatSubtype || (extensible.SubFormat != PcmSubtype && format.BitsPerSample == 32);

        return false;
    }

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
            Volatile.Write(ref error, e.Exception.Message);
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        var frameBytes = bytesPerSample * sourceChannels;
        var frames = e.BytesRecorded / frameBytes;
        if (frames <= 0)
            return;

        if (decoded.Length < frames * 2)
            decoded = new float[frames * 2];

        Decode(e.Buffer, frames);
        ReadOnlySpan<float> output;
        if (sourceRate == targetRate)
        {
            output = decoded.AsSpan(0, frames * 2);
        }
        else
        {
            var produced = Resample(frames);
            output = resampled.AsSpan(0, produced * 2);
        }

        var fit = Math.Min(ring.Free / 2 * 2, output.Length);
        if (fit > 0)
            ring.Write(output[..fit]);

        if (fit < output.Length)
            Volatile.Write(ref dropped, dropped + (output.Length - fit) / 2);
    }

    private void Decode(byte[] buffer, int frames)
    {
        var frameBytes = bytesPerSample * sourceChannels;
        var rightOffset = sourceChannels > 1 ? bytesPerSample : 0;
        for (int f = 0, p = 0, j = 0; f < frames; f++, p += frameBytes, j += 2)
        {
            decoded[j] = ReadSample(buffer, p);
            decoded[j + 1] = ReadSample(buffer, p + rightOffset);
        }
    }

    private float ReadSample(byte[] buffer, int offset)
    {
        var span = buffer.AsSpan(offset, bytesPerSample);
        if (isFloat && bytesPerSample == 4)
            return BinaryPrimitives.ReadSingleLittleEndian(span);

        return bytesPerSample switch
        {
            1 => (buffer[offset] - 128) / 128f,
            2 => BinaryPrimitives.ReadInt16LittleEndian(span) / 32768f,
            3 => (buffer[offset] | (buffer[offset + 1] << 8) | ((sbyte)buffer[offset + 2] << 16)) / 8388608f,
            4 => BinaryPrimitives.ReadInt32LittleEndian(span) / 2147483648f,
            _ => 0f,
        };
    }

    /// <summary>Linear interpolation from the capture rate to the engine rate, continuous across chunks; returns the frames produced.</summary>
    private int Resample(int frames)
    {
        var step = sourceRate / (double)targetRate;
        var expected = (int)(frames / step) + 2;
        if (resampled.Length < expected * 2)
            resampled = new float[expected * 2];

        var produced = 0;
        var p = resamplePhase;
        while (p < frames - 1)
        {
            var k = (int)Math.Floor(p);
            var frac = (float)(p - k);
            float aL, aR;
            if (k < 0)
            {
                aL = previousLeft;
                aR = previousRight;
            }
            else
            {
                aL = decoded[k * 2];
                aR = decoded[k * 2 + 1];
            }

            var bL = decoded[(k + 1) * 2];
            var bR = decoded[(k + 1) * 2 + 1];
            resampled[produced * 2] = aL + (bL - aL) * frac;
            resampled[produced * 2 + 1] = aR + (bR - aR) * frac;
            produced++;
            p += step;
        }

        previousLeft = decoded[(frames - 1) * 2];
        previousRight = decoded[(frames - 1) * 2 + 1];
        resamplePhase = p - frames;

        return produced;
    }
}
