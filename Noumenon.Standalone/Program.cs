using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Runtime.InteropServices;
using Noumenon.Engine;
using Noumenon.Engine.Parameters;
using Noumenon.Engine.Presets;
using Noumenon.Engine.Recording;
using Noumenon.Standalone.Audio;
using Noumenon.Standalone.Presets;

namespace Noumenon.Standalone;

/// <summary>
/// Entry point of the standalone instrument. Normally it opens <see cref="MainForm"/> — the NAudio
/// host with the developer panel; with <c>--bounce</c> or <c>--record</c> it stays headless and
/// prints to the console it was started from. The GC runs in sustained-low-latency mode so a
/// collection never stalls the audio thread for long, and the Media Foundation decoder is
/// registered before anything can load a sample.
/// </summary>
internal static partial class Program
{
    private const int AttachParentProcess = -1;

    [STAThread]
    private static int Main(string[] args)
    {
        GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
        MediaFoundationDecoding.Register();

        var options = LaunchOptions.Parse(args);
        if (options.BouncePath is not null || options.RecordPath is not null || options.Error is not null)
        {
            AttachConsole(AttachParentProcess);
            if (options.Error is not null)
            {
                Console.Error.WriteLine(options.Error);
                return 2;
            }

            return options.BouncePath is not null ? Bounce(options) : RecordHeadless(options);
        }

        ApplicationConfiguration.Initialize();
        using var window = new MainForm(options);
        Application.Run(window);

        return 0;
    }

    private static int Bounce(LaunchOptions options)
    {
        var settings = HostSettings.Load();
        var engine = new NoumenonEngine();
        var name = LoadPreset(engine, options.PresetPath, out var error);
        if (error is not null)
        {
            Console.Error.WriteLine(error);
            return 3;
        }

        if (options.Seed is { } seed)
            Randomizer.Randomize(engine, seed, (RandomizeScope)engine.Parameters.GetInt(ParamId.RandomizeScope), engine.Parameters.Get(ParamId.RandomizeAmount));

        var state = engine.CapturePreset(name);
        if (options.Seed is { } s)
            state.Seed = s;

        var rate = options.SampleRate > 0 ? options.SampleRate : settings.SampleRate > 0 ? settings.SampleRate : (int)NoumenonEngine.DefaultSampleRate;
        var format = options.Format ?? settings.RecordFormat;
        Console.WriteLine($"Bounce: {name} -> {options.BouncePath}  ({options.Minutes.ToString("0.##", CultureInfo.InvariantCulture)} min @ {rate} Hz, {format})");
        var lastShown = -1;
        var progress = new SynchronousProgress(p =>
        {
            var percent = (int)(p * 100);
            if (percent / 10 != lastShown / 10)
            {
                lastShown = percent;
                Console.Write($"{percent}% ");
            }
        });

        var result = OfflineRenderer.Render(state, engine.Samples.Loaded, options.BouncePath!, options.Minutes * 60.0, rate, format, settings.Tempo, progress);
        Console.WriteLine();
        Console.WriteLine($"Wrote : {result.Path}  ({result.Frames} frames, peak {result.Peak.ToString("0.000", CultureInfo.InvariantCulture)}, {result.Randomizations} randomizations)");
        Console.WriteLine($"Time  : {result.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s, {result.RealTimeFactor.ToString("0.0", CultureInfo.InvariantCulture)}x real time");

        return 0;
    }

    private static int RecordHeadless(LaunchOptions options)
    {
        var settings = HostSettings.Load();
        var engine = new NoumenonEngine();
        var name = LoadPreset(engine, options.PresetPath, out var error);
        if (error is not null)
        {
            Console.Error.WriteLine(error);
            return 3;
        }

        if (options.Seed is { } seed)
            Randomizer.Randomize(engine, seed, (RandomizeScope)engine.Parameters.GetInt(ParamId.RandomizeScope), engine.Parameters.Get(ParamId.RandomizeAmount));

        engine.Clock.HostTempo = settings.Tempo;
        if (options.SampleRate > 0)
            settings.SampleRate = options.SampleRate;

        if (options.Exclusive is { } exclusive)
            settings.Exclusive = exclusive;

        if (options.LatencyMs is { } latency)
            settings.LatencyMs = latency;

        if (options.Format is { } format)
            settings.RecordFormat = format;

        using var host = new AudioHost(engine, settings);
        if (!host.Start())
        {
            Console.Error.WriteLine(host.LastError ?? "The audio device could not be started.");
            return 3;
        }

        Console.WriteLine($"Audio : {host.Description}  (engine latency {engine.LatencySamples} samples)");
        Console.WriteLine($"Patch : {name}");
        var recorder = host.StartRecording(options.RecordPath!);
        Console.WriteLine($"Record: {recorder.Path}  ({recorder.Format}, {options.Seconds.ToString("0.#", CultureInfo.InvariantCulture)} s)");
        var stopwatch = Stopwatch.StartNew();
        var nextReport = 10.0;
        var peak = 0f;
        var randomizations = 0UL;
        while (stopwatch.Elapsed.TotalSeconds < options.Seconds)
        {
            Thread.Sleep(100);
            host.TakePeaks(out var l, out var r);
            peak = MathF.Max(peak, MathF.Max(l, r));
            if (engine.ConsumeRandomizeRequest())
                Randomizer.Randomize(engine, (options.Seed ?? 1) + 1000 * ++randomizations, (RandomizeScope)engine.Parameters.GetInt(ParamId.RandomizeScope), engine.Parameters.Get(ParamId.RandomizeAmount));

            if (stopwatch.Elapsed.TotalSeconds >= nextReport)
            {
                Console.WriteLine($"  {stopwatch.Elapsed.TotalSeconds,6:0} s  load {host.Load * 100,5:0.0} %  overruns {host.Overruns}  dropped {recorder.FramesDropped}  input underruns {host.InputUnderruns}  written {recorder.SecondsWritten:0.0} s");
                nextReport += 10.0;
            }
        }

        var callbacks = host.Callbacks;
        var lastBlock = host.LastBlockFrames;
        var overruns = host.Overruns;
        var startupOverruns = host.StartupOverruns;
        var inputUnderruns = host.InputUnderruns;
        host.StopRecording();
        host.Stop();
        var clean = recorder.FramesDropped == 0 && overruns == 0 && recorder.Error is null;
        Console.WriteLine($"Done  : {recorder.SecondsWritten.ToString("0.0", CultureInfo.InvariantCulture)} s written, peak {peak.ToString("0.000", CultureInfo.InvariantCulture)}, {callbacks} callbacks, last block {lastBlock} frames");
        Console.WriteLine($"Health: overruns {overruns} (plus {startupOverruns} in the first {EngineProvider.WarmupCallbacks} callbacks), dropped frames {recorder.FramesDropped}, input underruns {inputUnderruns}{(recorder.Error is null ? "" : ", error: " + recorder.Error.Message)} -> {(clean ? "clean" : "NOT clean")}");

        return clean ? 0 : 1;
    }

    private static string LoadPreset(NoumenonEngine engine, string? presetPath, out string? error)
    {
        error = null;
        if (presetPath is null)
            return "Init";

        var resolved = PresetLibrary.Resolve(presetPath);
        if (resolved is null)
        {
            error = $"Preset '{presetPath}' not found.";
            return "Init";
        }

        try
        {
            var warnings = new List<string>();
            var state = PresetIo.Load(resolved);
            engine.ApplyPreset(state, loadSamples: true, warnings);
            foreach (var warning in warnings)
                Console.WriteLine($"warning: {warning}");

            return state.Name;
        }
        catch (Exception ex) when (ex is IOException or FormatException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            error = $"Preset '{presetPath}': {ex.Message}";
            return "Init";
        }
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(int processId);

    private sealed class SynchronousProgress(Action<float> report) : IProgress<float>
    {
        public void Report(float value) => report(value);
    }
}
