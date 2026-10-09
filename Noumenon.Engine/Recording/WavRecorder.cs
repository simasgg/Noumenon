using NAudio.Wave;

namespace Noumenon.Engine.Recording;

/// <summary>
/// Real-time capture to a WAV file that the audio thread feeds without ever waiting on a disk:
/// <see cref="Push(ReadOnlySpan{float})"/> copies the block into a <see cref="SampleRing"/> and
/// returns; a dedicated writer thread drains the ring, converts and writes, and refreshes the RIFF
/// header once a second so a crash still leaves a playable file. Only whole frames enter the ring,
/// so the channels can never slip against each other. A full ring (the disk stalled for longer
/// than the ring holds) drops frames and counts them in <see cref="FramesDropped"/> instead of
/// blocking; a write failure stops the recording and surfaces in <see cref="Error"/>. Dispose
/// stops the thread, writes what is left and finalizes the header.
/// </summary>
public sealed class WavRecorder : IDisposable
{
    private const int ChunkFrames = 4096;
    private const int FlushIntervalMs = 1000;
    private const int IdleSleepMs = 10;

    private readonly SampleRing ring;
    private readonly WaveFileWriter writer;
    private readonly Thread thread;
    private readonly int channels;
    private readonly float[] chunk;
    private readonly byte[] bytes;
    private float[] scratch;
    private long framesWritten;
    private long framesDropped;
    private Exception? error;
    private volatile bool stopping;
    private bool disposed;

    public WavRecorder(string path, int sampleRate, WavSampleFormat format, int channels = WavSamples.Channels, float ringSeconds = 4f, int maxBlockFrames = 4096)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));

        if (channels is < 1 or > 2)
            throw new ArgumentOutOfRangeException(nameof(channels), channels, "The recorder writes mono or stereo.");

        Path = System.IO.Path.GetFullPath(path);
        SampleRate = sampleRate;
        Format = format;
        this.channels = channels;
        ring = new SampleRing((int)(Math.Max(0.1f, ringSeconds) * sampleRate * channels));
        scratch = new float[Math.Max(1, maxBlockFrames) * channels];
        chunk = new float[ChunkFrames * channels];
        bytes = new byte[chunk.Length * 4];

        var folder = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);

        writer = new WaveFileWriter(Path, WavSamples.WaveFormatFor(format, sampleRate, channels));
        thread = new Thread(Run) { Name = "Noumenon WAV writer", IsBackground = true, Priority = ThreadPriority.AboveNormal };
        thread.Start();
    }

    public string Path { get; }

    public int SampleRate { get; }

    public WavSampleFormat Format { get; }

    public long FramesWritten => Volatile.Read(ref framesWritten);

    public long FramesDropped => Volatile.Read(ref framesDropped);

    public double SecondsWritten => FramesWritten / (double)SampleRate;

    /// <summary>Set by the writer thread when the file could not be written; pushes are ignored from then on.</summary>
    public Exception? Error => Volatile.Read(ref error);

    public bool IsRecording => !stopping;

    /// <summary>Audio thread: queues one interleaved block; never blocks, never allocates.</summary>
    public void Push(ReadOnlySpan<float> interleaved)
    {
        if (stopping)
            return;

        var frames = interleaved.Length / channels;
        var fit = Math.Min(ring.Free / channels, frames);
        if (fit > 0)
            ring.Write(interleaved[..(fit * channels)]);

        if (fit < frames)
            Interlocked.Add(ref framesDropped, frames - fit);
    }

    /// <summary>Audio thread: queues one block from separate channel arrays; allocates only if the block outgrows the constructor's hint.</summary>
    public void Push(float[] left, float[] right, int count)
    {
        var needed = count * channels;
        if (scratch.Length < needed)
            scratch = new float[needed];

        if (channels == 1)
        {
            Array.Copy(left, scratch, count);
        }
        else
        {
            for (int i = 0, j = 0; i < count; i++, j += 2)
            {
                scratch[j] = left[i];
                scratch[j + 1] = right[i];
            }
        }

        Push(scratch.AsSpan(0, needed));
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        stopping = true;
        thread.Join();
        writer.Dispose();
    }

    private void Run()
    {
        var lastFlush = Environment.TickCount64;
        try
        {
            while (true)
            {
                var n = ring.Read(chunk);
                if (n > 0)
                {
                    var byteCount = WavSamples.Convert(chunk.AsSpan(0, n), Format, bytes);
                    writer.Write(bytes, 0, byteCount);
                    Volatile.Write(ref framesWritten, framesWritten + n / channels);
                    var now = Environment.TickCount64;
                    if (now - lastFlush >= FlushIntervalMs)
                    {
                        writer.Flush();
                        lastFlush = now;
                    }

                    continue;
                }

                if (stopping)
                    break;

                Thread.Sleep(IdleSleepMs);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            Volatile.Write(ref error, ex);
            stopping = true;
        }
    }
}
