using NAudio.Wave;
using Noumenon.Dsp.Sampler;

namespace Noumenon.Engine.Samples;

/// <summary>
/// Turns an audio file into <see cref="SampleData"/>. WAV and AIFF are decoded here with NAudio.Core
/// (any PCM depth or 32-bit float, any channel count — the first two channels are kept). Formats
/// that need a platform decoder (MP3/AAC through Media Foundation, FLAC) are plugged in by the
/// Windows hosts through <see cref="Register"/>, so this assembly stays host-agnostic. Never called
/// on the audio thread.
/// </summary>
public static class SampleDecoder
{
    private const int ChunkFrames = 16384;

    private static readonly List<Func<string, SampleData?>> Extra = [];
    private static readonly Lock ExtraLock = new();

    /// <summary>Adds a decoder for formats the built-in readers do not handle; it returns null to pass.</summary>
    public static void Register(Func<string, SampleData?> decoder)
    {
        lock (ExtraLock)
        {
            Extra.Add(decoder);
        }
    }

    public static SampleData Decode(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Sample file not found.", path);

        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path).ToLowerInvariant();
        WaveStream? stream = extension switch
        {
            ".wav" or ".wave" => new WaveFileReader(path),
            ".aif" or ".aiff" or ".aifc" => new AiffFileReader(path),
            _ => null,
        };

        if (stream is not null)
        {
            using (stream)
            {
                return ReadAll(stream, name);
            }
        }

        Func<string, SampleData?>[] decoders;
        lock (ExtraLock)
        {
            decoders = Extra.ToArray();
        }

        foreach (var decoder in decoders)
        {
            if (decoder(path) is { } data)
                return data;
        }

        throw new NotSupportedException($"No decoder for '{extension}' files ({path}).");
    }

    public static SampleData ReadAll(WaveStream stream, string name)
    {
        var format = stream.WaveFormat;
        var channels = format.Channels;
        if (channels < 1)
            throw new InvalidDataException("The stream has no channels.");

        var provider = stream.ToSampleProvider();
        var expectedFrames = format.BlockAlign > 0 ? (int)Math.Min(int.MaxValue, stream.Length / format.BlockAlign) : 0;
        var left = new List<float>(expectedFrames);
        var right = channels > 1 ? new List<float>(expectedFrames) : null;
        var chunk = new float[ChunkFrames * channels];
        int read;
        while ((read = provider.Read(chunk, 0, chunk.Length)) > 0)
        {
            for (var i = 0; i + channels <= read; i += channels)
            {
                left.Add(chunk[i]);
                right?.Add(chunk[i + 1]);
            }
        }

        return new SampleData(name, format.SampleRate, left.ToArray(), right?.ToArray());
    }
}
