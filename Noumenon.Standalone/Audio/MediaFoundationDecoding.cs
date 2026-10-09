using System.Runtime.InteropServices;
using NAudio.Wave;
using Noumenon.Dsp.Sampler;
using Noumenon.Engine.Samples;

namespace Noumenon.Standalone.Audio;

/// <summary>
/// Plugs Windows' Media Foundation into the engine's sample decoder, so MP3, AAC/M4A, WMA and
/// (on Windows 10 and later) FLAC files load as samples next to the WAV and AIFF the engine reads
/// itself. The decoder is tried for any extension the built-in readers decline and passes by
/// returning null when Media Foundation cannot open the file.
/// </summary>
internal static class MediaFoundationDecoding
{
    public const string FileFilter = "Audio files|*.wav;*.wave;*.aif;*.aiff;*.aifc;*.mp3;*.m4a;*.aac;*.mp4;*.wma;*.flac;*.ogg;*.opus|All files|*.*";

    private static bool registered;

    public static void Register()
    {
        if (registered)
            return;

        registered = true;
        SampleDecoder.Register(TryDecode);
    }

    private static SampleData? TryDecode(string path)
    {
        try
        {
            using var reader = new MediaFoundationReader(path);

            return SampleDecoder.ReadAll(reader, Path.GetFileNameWithoutExtension(path));
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or NotSupportedException or IOException or InvalidDataException or ArgumentException)
        {
            return null;
        }
    }
}
