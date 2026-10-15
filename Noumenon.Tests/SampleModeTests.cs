using Noumenon.Dsp.Sampler;
using Noumenon.Engine;
using Noumenon.Engine.Parameters;

namespace Noumenon.Tests;

/// <summary>
/// The sampler's Phase 9 modes: Granular and Stretch keep a sample's pitch while Dir moves their
/// playheads (and transpose with Pitch), stay near the sample's level, follow or ignore the
/// playhead as Grain Scan says, freeze at Dir 0, switch modes without a click, and inside the
/// engine stay allocation-free and block-size invariant while the stretch work is spread over the
/// control ticks.
/// </summary>
public class SampleModeTests
{
    private const int SampleRate = 48000;
    private const int FftSize = 65536;
    private const double BinHz = (double)SampleRate / FftSize;

    private static SampleData Tone(float hz, float seconds = 4f, float amplitude = 0.5f) =>
        new("tone", SampleRate, Enumerable.Range(0, (int)(seconds * SampleRate)).Select(i => amplitude * MathF.Sin(2f * MathF.PI * hz * i / SampleRate)).ToArray());

    private static SamplePlayer Make(SampleData data, SampleMode mode, float pitch = 0f, float dir = 1f)
    {
        var player = new SamplePlayer();
        player.Prepare(SampleRate);
        player.SetSample(data);
        player.SetLoop(0f, 1f, 0f);
        player.SetSpeed(pitch, dir);
        player.SetGain(1f);
        player.SetInputMix(0f);
        player.SetStereo(true);
        player.Granular.Set(80f, 20f, 0.5f, 0f, 0f, 0f, 0f, GrainScan.Free);
        player.Stretch.SetAmount(8f);
        player.Stretch.SetWindow(StretchWindow.Long);
        player.SetMode(mode);
        player.Reset(7);

        return player;
    }

    private static float[] Render(SamplePlayer player, int n)
    {
        var y = new float[n];
        for (var i = 0; i < n; i++)
        {
            if (i % 32 == 0)
                player.Tick();

            player.Process(0f, 0f, out y[i], out _, out _);
        }

        return y;
    }

    private static double Rms(float[] x, int start, int end)
    {
        var sum = 0.0;
        for (var i = start; i < end; i++)
            sum += (double)x[i] * x[i];

        return Math.Sqrt(sum / Math.Max(1, end - start));
    }

    [Theory]
    [InlineData(SampleMode.Granular, 0f, 440.0)]
    [InlineData(SampleMode.Granular, 12f, 880.0)]
    [InlineData(SampleMode.Stretch, 0f, 440.0)]
    [InlineData(SampleMode.Stretch, 7f, 659.26)]
    public void The_Modes_Keep_The_Pitch_And_Follow_The_Pitch_Knob(SampleMode mode, float pitch, double expectedHz)
    {
        var player = Make(Tone(440f), mode, pitch);
        var y = Render(player, SampleRate + FftSize)[SampleRate..];

        // Random phases turn a stretched sine into a narrow band as wide as the window's main lobe,
        // so its centre (not its loudest bin) is the pitch; nearly all the energy sits within ±3 %.
        var mags = Spectrum.MagnitudeDb(y, FftSize);
        Assert.Equal(expectedHz, Spectrum.PeakFrequency(mags, BinHz, 100, 4000), expectedHz * 0.02);
        Assert.Equal(expectedHz, Centroid(mags, expectedHz * 0.9, expectedHz * 1.1), expectedHz * 0.004);
        Assert.True(BandShare(mags, expectedHz * 0.97, expectedHz * 1.03) > 0.95, "the energy stays at the pitch");
    }

    private static double Centroid(double[] magsDb, double fromHz, double toHz)
    {
        double weighted = 0, total = 0;
        for (var k = (int)(fromHz / BinHz); k <= (int)(toHz / BinHz); k++)
        {
            var power = Math.Pow(10, magsDb[k] / 10);
            weighted += power * k * BinHz;
            total += power;
        }

        return weighted / total;
    }

    private static double BandShare(double[] magsDb, double fromHz, double toHz)
    {
        double band = 0, total = 0;
        for (var k = 1; k < magsDb.Length; k++)
        {
            var power = Math.Pow(10, magsDb[k] / 10);
            total += power;
            if (k * BinHz >= fromHz && k * BinHz <= toHz)
                band += power;
        }

        return band / total;
    }

    [Theory]
    [InlineData(SampleMode.Granular)]
    [InlineData(SampleMode.Stretch)]
    public void The_Modes_Stay_Near_The_Samples_Level(SampleMode mode)
    {
        var tone = Tone(330f, amplitude: 0.5f);
        var classic = Render(Make(tone, SampleMode.Classic), SampleRate * 3);
        var player = Make(tone, mode);
        player.Granular.Set(80f, 20f, 0.5f, 0.5f, 0f, 0f, 0f, GrainScan.Free);   // scattered grains add in power, as a real cloud does
        var other = Render(player, SampleRate * 3);

        var reference = Rms(classic, SampleRate, SampleRate * 3);
        var level = Rms(other, SampleRate, SampleRate * 3);
        var db = 20 * Math.Log10(level / reference);
        Assert.InRange(db, -4.5, 3.0);
    }

    [Fact]
    public void A_Locked_Cloud_Follows_The_Playhead_And_A_Free_One_Stays()
    {
        var tone = Tone(220f);
        var locked = Make(tone, SampleMode.Granular, dir: 0.5f);
        locked.Granular.Set(80f, 20f, 0f, 0f, 0f, 0f, 0f, GrainScan.Locked);
        locked.Reset(1);
        Render(locked, SampleRate);
        Assert.Equal(SampleRate * 0.5, locked.DisplayPosition, 1.0);

        var free = Make(tone, SampleMode.Granular, dir: 0.5f);
        free.Granular.Set(80f, 20f, 0.25f, 0f, 0f, 0f, 0f, GrainScan.Free);
        Render(free, SampleRate);
        Assert.Equal(tone.Length * 0.25, free.DisplayPosition, 1.0);
        Assert.InRange(free.Granular.ActiveGrains, 1, Granulator.MaxGrains);
    }

    [Fact]
    public void The_Stretch_Playhead_Crawls_And_Freezes_At_Dir_Zero()
    {
        var tone = Tone(220f);
        var crawling = Make(tone, SampleMode.Stretch, dir: 1f);
        Render(crawling, SampleRate);
        Assert.Equal(SampleRate / 8.0, crawling.Stretch.Playhead, 1.0);

        var frozen = Make(tone, SampleMode.Stretch, dir: 0f);
        Render(frozen, SampleRate);
        Assert.Equal(0.0, frozen.Stretch.Playhead, 6);
        Assert.True(Rms(Render(frozen, SampleRate), 0, SampleRate) > 0.1, "a frozen spectrum still sounds");
    }

    [Theory]
    [InlineData(StretchWindow.Short, 48000, 4096)]
    [InlineData(StretchWindow.Medium, 48000, 8192)]
    [InlineData(StretchWindow.Long, 48000, 16384)]
    [InlineData(StretchWindow.Long, 96000, 32768)]
    [InlineData(StretchWindow.Short, 44100, 4096)]
    public void The_Window_Is_A_Length_In_Time(StretchWindow window, int rate, int expected)
    {
        var stretcher = new SpectralStretcher();
        stretcher.SetWindow(window);
        stretcher.Prepare(rate);

        Assert.Equal(expected, stretcher.FrameSize);
    }

    [Theory]
    [InlineData(SampleMode.Granular)]
    [InlineData(SampleMode.Stretch)]
    public void Switching_Modes_Does_Not_Click(SampleMode mode)
    {
        var player = Make(Tone(110f, amplitude: 0.5f), SampleMode.Classic);
        var y = new float[SampleRate * 2];
        for (var i = 0; i < y.Length; i++)
        {
            if (i % 32 == 0)
                player.Tick();

            if (i == SampleRate / 2)
                player.SetMode(mode);
            else if (i == SampleRate + SampleRate / 3)
                player.SetMode(SampleMode.Classic);

            player.Process(0f, 0f, out y[i], out _, out _);
        }

        // A 110 Hz sine at 0.5 moves at most 0.0072 per sample; a crossfade between two such signals stays smooth.
        var maxStep = 0f;
        for (var i = 1; i < y.Length; i++)
            maxStep = MathF.Max(maxStep, MathF.Abs(y[i] - y[i - 1]));

        Assert.True(maxStep < 0.03f, $"largest step {maxStep}");
    }

    private static NoumenonEngine MakeEngine(SampleMode mode)
    {
        var engine = new NoumenonEngine();
        var bank = engine.Parameters;
        engine.Samples.Set(0, Tone(220f, seconds: 2f));
        bank.Set(ParamId.SamplerSelect, 0);
        bank.Set(ParamId.SamplerMode, (int)mode);
        bank.Set(ParamId.SamplerAmp, 0.8f);
        bank.Set(ParamId.GrainSpray, 0.3f);
        bank.Set(ParamId.GrainPitchSpread, 2f);
        bank.Set(ParamId.StretchWindow, (int)StretchWindow.Long);
        engine.Prepare(SampleRate, 512);
        engine.Reset(5);

        return engine;
    }

    [Theory]
    [InlineData(SampleMode.Granular)]
    [InlineData(SampleMode.Stretch)]
    public void The_Engine_Stays_Allocation_Free_In_Every_Mode(SampleMode mode)
    {
        var engine = MakeEngine(mode);
        var l = new float[512];
        var r = new float[512];

        void Run()
        {
            for (var k = 0; k < 600; k++)
            {
                engine.Parameters.Set(ParamId.GrainPosition, (k & 7) / 8f);
                engine.Parameters.Set(ParamId.StretchWindow, (k / 100) % 3);
                engine.Parameters.Set(ParamId.SamplerMode, (k / 150) % 3);
                engine.Process(l, r, l.Length);
            }
        }

        Run();   // every path once before measuring: a first call may load or compile something
        var before = GC.GetAllocatedBytesForCurrentThread();
        Run();

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void Every_Phase_9_Feature_At_Once_Allocates_Nothing()
    {
        var engine = MakeEngine(SampleMode.Granular);
        var bank = engine.Parameters;
        bank.Set(ParamId.OscA1Wave, (int)Noumenon.Dsp.Oscillators.Waveform.Wavetable);
        bank.Set(ParamId.OscA2Wave, (int)Noumenon.Dsp.Oscillators.Waveform.Sub);
        bank.Set(ParamId.ChordSeqOn, true);
        bank.Set(ParamId.ChordSeqStepTime, 0.25f);
        bank.Set(ParamId.ChordSeqRoot, 1);
        bank.Set(ParamId.Scale, 3);
        bank.Set(ParamId.EnvFollowerSource, 3);
        bank.Set(ParamId.ModSlot1Source, 6);
        bank.Set(ParamId.ModSlot1Target, (int)ParamId.GrainPosition + 1);
        bank.Set(ParamId.ModSlot1Depth, 0.5f);
        bank.Set(ParamId.ModSlot2Source, 10);
        bank.Set(ParamId.ModSlot2Target, (int)ParamId.StretchAmount + 1);
        bank.Set(ParamId.ModSlot2Depth, 0.3f);
        engine.MidiMap.Assign(21, ParamId.GrainSize);
        engine.MidiMap.Assign(22, ParamId.SamplerMode);
        var silence = new float[512];
        var sidechain = Enumerable.Range(0, 512).Select(i => 0.3f * MathF.Sin(i * 0.05f)).ToArray();
        var l = new float[512];
        var r = new float[512];

        void Run()
        {
            for (var k = 0; k < 800; k++)
            {
                engine.Midi.ControlChange(21, k % 128);
                if (k % 100 == 0)
                    engine.Midi.ControlChange(22, (k / 100) % 3 * 63);

                if (k % 37 == 0)
                    engine.Midi.NoteOn(48 + k % 24, 90);

                engine.Process(silence, silence, sidechain, sidechain, l, r, 0, 512);
            }
        }

        Run();
        var before = GC.GetAllocatedBytesForCurrentThread();
        Run();

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(l.All(float.IsFinite));
    }

    [Theory]
    [InlineData(SampleMode.Granular)]
    [InlineData(SampleMode.Stretch)]
    public void The_Engine_Renders_The_Same_Whatever_The_Block_Size(SampleMode mode)
    {
        const int n = SampleRate * 2;
        var a = MakeEngine(mode);
        var oneL = new float[n];
        var oneR = new float[n];
        a.Process(oneL, oneR, 0, n);

        var b = MakeEngine(mode);
        var blkL = new float[n];
        var blkR = new float[n];
        int[] blocks = [1, 32, 512, 7, 333, 1024, 17, 31, 64];
        int pos = 0, bi = 0;
        while (pos < n)
        {
            var len = Math.Min(blocks[bi++ % blocks.Length], n - pos);
            b.Process(blkL, blkR, pos, len);
            pos += len;
        }

        Assert.True(Rms(oneL, n / 2, n) > 0.01, "the mode should sound");
        for (var i = 0; i < n; i++)
            Assert.True(oneL[i] == blkL[i] && oneR[i] == blkR[i], $"differs at {i}");
    }
}
