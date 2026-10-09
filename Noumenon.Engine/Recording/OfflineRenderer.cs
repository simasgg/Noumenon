using System.Diagnostics;
using NAudio.Wave;
using Noumenon.Engine.Parameters;
using Noumenon.Engine.Presets;
using Noumenon.Engine.Samples;

namespace Noumenon.Engine.Recording;

public sealed record OfflineRenderResult(string Path, long Frames, int SampleRate, float Peak, TimeSpan Elapsed, int Randomizations)
{
    public double Seconds => Frames / (double)SampleRate;

    /// <summary>How many seconds of audio were rendered per second of wall-clock time.</summary>
    public double RealTimeFactor => Elapsed.TotalSeconds > 0 ? Seconds / Elapsed.TotalSeconds : 0;
}

/// <summary>
/// The bounce: renders a snapshot to a WAV faster than real time on whatever thread calls it,
/// with a fresh engine so the live one keeps playing untouched. Samples are shared by reference
/// (a decoded sample is immutable), nothing is re-decoded. The render takes the same deterministic
/// path as the harness — the preset's seed, the lanes restarted at zero, the Randomize trigger
/// honoured with a seed derived from the preset's — so a bounce is reproducible and identical
/// whatever block size it is cut into. Cancelling deletes the partial file.
/// </summary>
public static class OfflineRenderer
{
    public const int DefaultBlockSize = 4096;

    public static OfflineRenderResult Render(PresetState state, IEnumerable<SampleSlot> samples, string path, double seconds, int sampleRate, WavSampleFormat format, double hostTempo = 0, IProgress<float>? progress = null, CancellationToken cancellation = default, int blockSize = DefaultBlockSize)
    {
        if (seconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(seconds));

        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));

        blockSize = Math.Max(1, blockSize);
        var engine = new NoumenonEngine();
        engine.ApplyPreset(state, loadSamples: false);
        foreach (var slot in samples)
            engine.Samples.Set(slot.Key, slot.Data, slot.Gain, slot.RootNote, slot.Path);

        engine.Clock.HostTempo = hostTempo;
        engine.Clock.HostPlaying = hostTempo > 0;
        engine.Prepare(sampleRate, blockSize);
        engine.Reset(state.Seed);

        var fullPath = Path.GetFullPath(path);
        var folder = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);

        var total = (long)(seconds * sampleRate);
        var left = new float[blockSize];
        var right = new float[blockSize];
        var interleaved = new float[blockSize * WavSamples.Channels];
        var bytes = new byte[interleaved.Length * 4];
        var peak = 0f;
        var randomizations = 0;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var writer = new WaveFileWriter(fullPath, WavSamples.WaveFormatFor(format, sampleRate));
            var position = 0L;
            var nextReport = 0L;
            var reportStep = Math.Max(1, total / 200);
            while (position < total)
            {
                cancellation.ThrowIfCancellationRequested();
                var count = (int)Math.Min(blockSize, total - position);
                if (engine.ConsumeRandomizeRequest())
                {
                    randomizations++;
                    Randomizer.Randomize(engine, state.Seed + 1000UL * (ulong)randomizations, (RandomizeScope)engine.Parameters.GetInt(ParamId.RandomizeScope), engine.Parameters.Get(ParamId.RandomizeAmount));
                }

                engine.Process(left, right, count);
                for (int i = 0, j = 0; i < count; i++, j += 2)
                {
                    var l = left[i];
                    var r = right[i];
                    interleaved[j] = l;
                    interleaved[j + 1] = r;
                    var a = MathF.Abs(l);
                    if (a > peak)
                        peak = a;

                    a = MathF.Abs(r);
                    if (a > peak)
                        peak = a;
                }

                writer.Write(bytes, 0, WavSamples.Convert(interleaved.AsSpan(0, count * 2), format, bytes));
                position += count;
                if (progress is not null && position >= nextReport)
                {
                    progress.Report((float)(position / (double)total));
                    nextReport = position + reportStep;
                }
            }
        }
        catch (OperationCanceledException)
        {
            TryDelete(fullPath);
            throw;
        }

        stopwatch.Stop();
        progress?.Report(1f);

        return new OfflineRenderResult(fullPath, total, sampleRate, peak, stopwatch.Elapsed, randomizations);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
