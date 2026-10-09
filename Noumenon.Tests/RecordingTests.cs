using Noumenon.Dsp.Sampler;
using Noumenon.Engine;
using Noumenon.Engine.Parameters;
using Noumenon.Engine.Recording;
using Noumenon.Engine.Samples;

namespace Noumenon.Tests;

/// <summary>
/// The standalone's recording services, which the audio thread feeds: the ring never blocks or
/// allocates and keeps order across its wrap, the recorder writes exactly what was pushed in both
/// formats without touching the GC on the pushing thread, and the offline bounce renders the same
/// samples as a direct render of the same snapshot, deterministically and faster than real time.
/// </summary>
public class RecordingTests : IDisposable
{
    private const int SampleRate = 48000;

    private readonly string dir = Path.Combine(Path.GetTempPath(), "noumenon-recording-" + Guid.NewGuid().ToString("N"));

    public RecordingTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(dir, true);
        }
        catch (IOException)
        {
        }
    }

    private string TempPath(string name) => Path.Combine(dir, name);

    private static float[] Ramp(int count, int start = 0)
    {
        var values = new float[count];
        for (var i = 0; i < count; i++)
            values[i] = (start + i) * 1e-4f;

        return values;
    }

    [Fact]
    public void Ring_Keeps_Order_Across_The_Wrap_And_Reports_What_Did_Not_Fit()
    {
        var ring = new SampleRing(100);   // rounds up to 128
        Assert.Equal(128, ring.Capacity);

        var destination = new float[128];
        var produced = ring.Write(Ramp(40));   // keep 40 queued so every block straddles a different place
        var consumed = 0;
        for (var round = 0; round < 20; round++)
        {
            var written = ring.Write(Ramp(37, produced));
            Assert.Equal(37, written);
            produced += written;

            var read = ring.Read(destination.AsSpan(0, 37));
            Assert.Equal(37, read);
            for (var i = 0; i < read; i++)
                Assert.Equal((consumed + i) * 1e-4f, destination[i]);

            consumed += read;
            Assert.Equal(40, ring.Count);
        }

        var overflow = ring.Write(Ramp(200, produced));
        Assert.Equal(88, overflow);   // only the free 128 - 40 fit; the rest is dropped, not grown into
        Assert.Equal(128, ring.Count);
        Assert.Equal(0, ring.Free);
        Assert.Equal(0, ring.Write(Ramp(1)));

        var drained = ring.Read(destination);
        Assert.Equal(128, drained);
        for (var i = 0; i < drained; i++)
            Assert.Equal((consumed + i) * 1e-4f, destination[i]);

        ring.Write(Ramp(5));
        ring.Clear();
        Assert.Equal(0, ring.Count);
        Assert.Equal(0, ring.Read(destination));
    }

    [Fact]
    public void Ring_Write_And_Read_Do_Not_Allocate()
    {
        var ring = new SampleRing(8192);
        var block = Ramp(512);
        var destination = new float[512];
        ring.Write(block);
        ring.Read(destination);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            ring.Write(block);
            ring.Read(destination);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Theory]
    [InlineData(WavSampleFormat.Float32)]
    [InlineData(WavSampleFormat.Pcm24)]
    public void Recorder_Writes_Exactly_What_Was_Pushed(WavSampleFormat format)
    {
        var path = TempPath($"take-{format}.wav");
        const int frames = SampleRate * 2;
        var left = new float[frames];
        var right = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            left[i] = 0.8f * MathF.Sin(2f * MathF.PI * 220f * i / SampleRate);
            right[i] = 0.5f * MathF.Sin(2f * MathF.PI * 330f * i / SampleRate);
        }

        using (var recorder = new WavRecorder(path, SampleRate, format, maxBlockFrames: 512))
        {
            var blockL = new float[512];
            var blockR = new float[512];
            for (var pos = 0; pos < frames; pos += 512)
            {
                var n = Math.Min(512, frames - pos);
                Array.Copy(left, pos, blockL, 0, n);
                Array.Copy(right, pos, blockR, 0, n);
                recorder.Push(blockL, blockR, n);
                if ((pos / 512) % 16 == 0)
                    Thread.Sleep(1);   // let the writer interleave with the pushes, as it does live
            }

            Assert.Equal(0, recorder.FramesDropped);
        }

        var data = SampleDecoder.Decode(path);
        Assert.Equal(SampleRate, data.SampleRate);
        Assert.Equal(frames, data.Length);
        Assert.True(data.IsStereo);
        var tolerance = format == WavSampleFormat.Float32 ? 0f : 2.5f / 8388607f;   // 24-bit: half a step of rounding plus NAudio's /8388608 read-back
        for (var i = 0; i < frames; i++)
        {
            Assert.True(MathF.Abs(data.Left[i] - left[i]) <= tolerance, $"L differs at {i}: {data.Left[i]} vs {left[i]}");
            Assert.True(MathF.Abs(data.Right[i] - right[i]) <= tolerance, $"R differs at {i}: {data.Right[i]} vs {right[i]}");
        }
    }

    [Fact]
    public void Recorder_Push_Does_Not_Allocate_On_The_Audio_Thread()
    {
        var path = TempPath("alloc.wav");
        var interleaved = Ramp(1024);
        var left = Ramp(512);
        var right = Ramp(512);
        using var recorder = new WavRecorder(path, SampleRate, WavSampleFormat.Pcm24, ringSeconds: 8f, maxBlockFrames: 512);
        recorder.Push(interleaved);
        recorder.Push(left, right, 512);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 200; i++)
        {
            recorder.Push(interleaved);
            recorder.Push(left, right, 512);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void Recorder_Clips_And_Mutes_NaN_In_24_Bit()
    {
        var bytes = new byte[12];
        var written = WavSamples.Convert([1.7f, -3f, float.NaN, 0.5f], WavSampleFormat.Pcm24, bytes);

        Assert.Equal(12, written);
        Assert.Equal(0x7FFFFF, bytes[0] | (bytes[1] << 8) | (bytes[2] << 16));
        Assert.Equal(-0x7FFFFF, (bytes[3] | (bytes[4] << 8) | ((sbyte)bytes[5] << 16)));
        Assert.Equal(0, bytes[6] | (bytes[7] << 8) | (bytes[8] << 16));
        Assert.Equal((int)MathF.Round(0.5f * 8388607f), bytes[9] | (bytes[10] << 8) | (bytes[11] << 16));
    }

    [Fact]
    public void Bounce_Matches_A_Direct_Render_Of_The_Same_Snapshot()
    {
        var source = new NoumenonEngine();
        Randomizer.Randomize(source, 11, RandomizeScope.All, 0.7f);
        source.Samples.Set(3, new SampleData("tone", 44100, Ramp(44100)));
        source.Parameters.Set(ParamId.SamplerSelect, 3);
        source.Parameters.Set(ParamId.SamplerAmp, 0.6f);
        source.Prepare(SampleRate, 512);
        source.Reset(11);
        var l = new float[512];
        var r = new float[512];
        source.Process(l, r, 512);   // the lanes take their snapshots in
        var state = source.CapturePreset("bounce");

        var path = TempPath("bounce.wav");
        var reports = new List<float>();
        var result = OfflineRenderer.Render(state, source.Samples.Loaded, path, 1.5, SampleRate, WavSampleFormat.Float32, progress: new SynchronousProgress(reports), blockSize: 1000);

        var reference = new NoumenonEngine();
        reference.ApplyPreset(state, loadSamples: false);
        foreach (var slot in source.Samples.Loaded)
            reference.Samples.Set(slot.Key, slot.Data, slot.Gain, slot.RootNote, slot.Path);

        reference.Prepare(SampleRate, 512);
        reference.Reset(state.Seed);
        var frames = (int)(1.5 * SampleRate);
        var refL = new float[frames];
        var refR = new float[frames];
        for (var pos = 0; pos < frames; pos += 512)
            reference.Process(refL, refR, pos, Math.Min(512, frames - pos));

        var data = SampleDecoder.Decode(path);
        Assert.Equal(frames, result.Frames);
        Assert.Equal(frames, data.Length);
        Assert.Equal(refL, data.Left);
        Assert.Equal(refR, data.Right);
        Assert.True(result.Peak > 0.01f);
        Assert.Equal(1f, reports[^1]);
        Assert.True(reports.Count > 10, "progress is reported along the way");
        Assert.Equal(0, result.Randomizations);
    }

    [Fact]
    public void Bounce_Honours_The_Randomize_Trigger_And_Runs_Faster_Than_Real_Time()
    {
        var engine = new NoumenonEngine();
        engine.Parameters.Set(ParamId.RandomizeNow, 1f);
        var state = engine.CapturePreset("trigger");

        var result = OfflineRenderer.Render(state, [], TempPath("fast.wav"), 4.0, SampleRate, WavSampleFormat.Pcm24);

        Assert.Equal(1, result.Randomizations);
        Assert.Equal(4.0, result.Seconds, 3);
        Assert.True(result.RealTimeFactor > 1.5, $"a bounce should outrun real time, got {result.RealTimeFactor:F2}x");
    }

    [Fact]
    public void Bounce_Cancelled_Leaves_No_File()
    {
        var state = new NoumenonEngine().CapturePreset("cancel");
        var path = TempPath("cancelled.wav");
        using var cancellation = new CancellationTokenSource();
        var progress = new SynchronousProgress([]) { OnReport = p => cancellation.Cancel() };

        Assert.Throws<OperationCanceledException>(() => OfflineRenderer.Render(state, [], path, 10.0, SampleRate, WavSampleFormat.Pcm24, progress: progress, cancellation: cancellation.Token));
        Assert.False(File.Exists(path));
    }

    private sealed class SynchronousProgress(List<float> reports) : IProgress<float>
    {
        public Action<float>? OnReport { get; init; }

        public void Report(float value)
        {
            reports.Add(value);
            OnReport?.Invoke(value);
        }
    }
}
