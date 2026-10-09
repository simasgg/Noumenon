using NAudio.Wave;

namespace Noumenon.Standalone.Audio;

/// <summary>
/// The ASIO driver's input channels as the live input. NAudio raises <c>AudioAvailable</c> with
/// the input buffers right before it asks the provider for the same block, on the same thread, so
/// the tap converts the block into its own arrays and the next <see cref="Pull"/> hands them over
/// with no ring and no extra latency. A sample type NAudio cannot convert disables the tap.
/// </summary>
internal sealed class AsioInputTap : IInputSource
{
    private float[] interleaved = [];
    private float[] left = [];
    private float[] right = [];
    private int channels;
    private int frames;
    private bool failed;

    public string? Error { get; private set; }

    /// <summary>Off the audio thread, after the driver's buffer size is known.</summary>
    public void Configure(int framesPerBuffer, int inputChannels)
    {
        channels = Math.Max(1, inputChannels);
        interleaved = new float[framesPerBuffer * channels];
        left = new float[framesPerBuffer];
        right = new float[framesPerBuffer];
        frames = 0;
    }

    /// <summary>ASIO thread: converts the block NAudio just received.</summary>
    public void Capture(AsioAudioAvailableEventArgs e)
    {
        if (failed)
            return;

        var count = e.SamplesPerBuffer;
        var inputs = e.InputBuffers.Length;
        if (inputs < 1 || count > left.Length || count * inputs > interleaved.Length)
        {
            frames = 0;
            return;
        }

        try
        {
            e.GetAsInterleavedSamples(interleaved);
        }
        catch (NotImplementedException ex)
        {
            failed = true;
            Error = "ASIO input sample type is not supported: " + ex.Message;
            frames = 0;
            return;
        }

        for (var i = 0; i < count; i++)
        {
            left[i] = interleaved[i * inputs];
            right[i] = inputs > 1 ? interleaved[i * inputs + 1] : left[i];
        }

        frames = count;
    }

    public bool Pull(float[] destinationLeft, float[] destinationRight, int count)
    {
        if (failed || frames != count)
            return false;

        Array.Copy(left, destinationLeft, count);
        Array.Copy(right, destinationRight, count);
        frames = 0;

        return true;
    }
}
