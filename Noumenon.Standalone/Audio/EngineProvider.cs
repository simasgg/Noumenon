using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.Wave;
using Noumenon.Engine;
using Noumenon.Engine.Recording;

namespace Noumenon.Standalone.Audio;

/// <summary>A source of live input for the callback: fills whole blocks of both channels, or declines and leaves them to be cleared.</summary>
internal interface IInputSource
{
    bool Pull(float[] left, float[] right, int frames);
}

/// <summary>
/// The pull callback the output device drives — the audio thread. It pulls the live input, runs
/// the allocation-free engine, interleaves into the device buffer, meters, feeds the recorder's
/// ring and measures how much of its real-time budget it used. Buffers are sized when the host
/// configures it and grow only if a device asks for a bigger block than announced (once, before
/// steady state). The first callback raises the thread's priority and joins the "Pro Audio"
/// MMCSS class, which NAudio's own playback thread does not do by itself.
/// </summary>
internal sealed partial class EngineProvider : IWaveProvider
{
    /// <summary>Callbacks at the start of a stream whose overruns are reported separately: the JIT and the device settle there.</summary>
    public const int WarmupCallbacks = 64;

    private const int BytesPerFrame = 2 * sizeof(float);
    private const double LoadSmoothing = 0.05;
    private const int WarmBlocks = 8;
    private const int WarmFrames = 512;

    private readonly NoumenonEngine engine;
    private float[] inputLeft = new float[4096];
    private float[] inputRight = new float[4096];
    private float[] outputLeft = new float[4096];
    private float[] outputRight = new float[4096];
    private IInputSource? input;
    private WavRecorder? recorder;
    private double ticksPerFrame = Stopwatch.Frequency / NoumenonEngine.DefaultSampleRate;
    private double load;
    private float peakLeft;
    private float peakRight;
    private long overruns;
    private long startupOverruns;
    private long callbacks;
    private int lastBlock;
    private bool boosted;

    public EngineProvider(NoumenonEngine engine) => this.engine = engine;

    public WaveFormat WaveFormat { get; private set; } = WaveFormat.CreateIeeeFloatWaveFormat((int)NoumenonEngine.DefaultSampleRate, 2);

    public int MaxFrames => outputLeft.Length;

    public IInputSource? Input
    {
        get => Volatile.Read(ref input);
        set => Volatile.Write(ref input, value);
    }

    public WavRecorder? Recorder
    {
        get => Volatile.Read(ref recorder);
        set => Volatile.Write(ref recorder, value);
    }

    /// <summary>The fraction of its real-time budget the callback used, smoothed over recent blocks.</summary>
    public double Load => Volatile.Read(ref load);

    /// <summary>Callbacks after the first <see cref="WarmupCallbacks"/> that took longer than the audio they produced.</summary>
    public long Overruns => Volatile.Read(ref overruns);

    /// <summary>Overruns within the first <see cref="WarmupCallbacks"/> callbacks of the stream.</summary>
    public long StartupOverruns => Volatile.Read(ref startupOverruns);

    public long Callbacks => Volatile.Read(ref callbacks);

    public int LastBlockFrames => Volatile.Read(ref lastBlock);

    /// <summary>The highest absolute sample per channel since the previous call, which resets it.</summary>
    public void TakePeaks(out float left, out float right)
    {
        left = Interlocked.Exchange(ref peakLeft, 0f);
        right = Interlocked.Exchange(ref peakRight, 0f);
    }

    /// <summary>Off the audio thread, before the device starts.</summary>
    public void Configure(int sampleRate, int maxFrames)
    {
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);
        EnsureCapacity(maxFrames);
        ticksPerFrame = Stopwatch.Frequency / (double)sampleRate;
        load = 0;
        overruns = 0;
        startupOverruns = 0;
        callbacks = 0;
        lastBlock = 0;
        boosted = false;
    }

    /// <summary>
    /// Runs the whole callback path a few times on the calling thread so the JIT has compiled it
    /// before the first real callback; call it before the engine is re-prepared, which resets
    /// whatever state this advanced.
    /// </summary>
    public void Warm()
    {
        EnsureCapacity(WarmFrames);
        var scratch = new byte[WarmFrames * BytesPerFrame];
        var samples = MemoryMarshal.Cast<byte, float>(scratch.AsSpan());
        for (var block = 0; block < WarmBlocks; block++)
        {
            Array.Clear(inputLeft, 0, WarmFrames);
            Array.Clear(inputRight, 0, WarmFrames);
            engine.Process(inputLeft, inputRight, outputLeft, outputRight, WarmFrames);
            for (int i = 0, j = 0; i < WarmFrames; i++, j += 2)
            {
                samples[j] = outputLeft[i];
                samples[j + 1] = outputRight[i];
            }
        }
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        var frames = count / BytesPerFrame;
        if (frames <= 0)
            return 0;

        if (!boosted)
            Boost();

        var start = Stopwatch.GetTimestamp();
        EnsureCapacity(frames);
        var source = Input;
        if (source is null || !source.Pull(inputLeft, inputRight, frames))
        {
            Array.Clear(inputLeft, 0, frames);
            Array.Clear(inputRight, 0, frames);
        }

        engine.Process(inputLeft, inputRight, outputLeft, outputRight, frames);

        var samples = MemoryMarshal.Cast<byte, float>(buffer.AsSpan(offset, frames * BytesPerFrame));
        var pl = 0f;
        var pr = 0f;
        for (int i = 0, j = 0; i < frames; i++, j += 2)
        {
            var l = outputLeft[i];
            var r = outputRight[i];
            samples[j] = l;
            samples[j + 1] = r;
            var a = l < 0f ? -l : l;
            if (a > pl)
                pl = a;

            a = r < 0f ? -r : r;
            if (a > pr)
                pr = a;
        }

        Recorder?.Push(samples);
        if (pl > Volatile.Read(ref peakLeft))
            Volatile.Write(ref peakLeft, pl);

        if (pr > Volatile.Read(ref peakRight))
            Volatile.Write(ref peakRight, pr);

        var elapsed = Stopwatch.GetTimestamp() - start;
        var budget = frames * ticksPerFrame;
        Volatile.Write(ref load, load + (elapsed / budget - load) * LoadSmoothing);
        if (elapsed > budget)
        {
            if (callbacks < WarmupCallbacks)
                Volatile.Write(ref startupOverruns, startupOverruns + 1);
            else
                Volatile.Write(ref overruns, overruns + 1);
        }

        Volatile.Write(ref callbacks, callbacks + 1);
        Volatile.Write(ref lastBlock, frames);

        return frames * BytesPerFrame;
    }

    private void EnsureCapacity(int frames)
    {
        if (outputLeft.Length >= frames)
            return;

        inputLeft = new float[frames];
        inputRight = new float[frames];
        outputLeft = new float[frames];
        outputRight = new float[frames];
    }

    private void Boost()
    {
        boosted = true;
        try
        {
            Thread.CurrentThread.Priority = ThreadPriority.Highest;
        }
        catch (ThreadStateException)
        {
        }

        try
        {
            var index = 0u;
            AvSetMmThreadCharacteristics("Pro Audio", ref index);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    [LibraryImport("avrt.dll", EntryPoint = "AvSetMmThreadCharacteristicsW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint AvSetMmThreadCharacteristics(string taskName, ref uint taskIndex);
}
