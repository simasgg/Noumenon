using NAudio.CoreAudioApi;
using NAudio.Wave;
using Noumenon.Engine;
using Noumenon.Engine.Parameters;
using Noumenon.Engine.Recording;

namespace Noumenon.Standalone.Audio;

/// <summary>
/// The standalone's audio I/O around one <see cref="NoumenonEngine"/>: opens the chosen output
/// (WASAPI shared or exclusive, or ASIO), prepares the engine for the rate the stream actually
/// runs at and lets the device pull blocks from <see cref="EngineProvider"/>. The live input
/// comes from a WASAPI capture endpoint on its own thread or from the ASIO driver's inputs in the
/// same callback; without one the engine hears silence. Every change of device, rate or engine
/// rate goes through <see cref="Start"/> again — stop, re-prepare, start — so nothing re-prepares
/// while the callback can run; a recording in progress ends when the stream stops, because the
/// file's rate could change. Failures never throw out of <see cref="Start"/>: it returns false
/// and leaves the reason in <see cref="LastError"/>.
/// </summary>
internal sealed class AudioHost : IDisposable
{
    private const int MinLatencyMs = 3;
    private const int MaxLatencyMs = 500;
    private const int DefaultAsioRate = 48000;

    private readonly NoumenonEngine engine;
    private readonly HostSettings settings;
    private readonly EngineProvider provider;
    private IWavePlayer? player;
    private AsioOut? asio;
    private AsioInputTap? asioInput;
    private InputCapture? capture;
    private WavRecorder? recorder;
    private string? lastError;

    public AudioHost(NoumenonEngine engine, HostSettings settings)
    {
        this.engine = engine;
        this.settings = settings;
        provider = new EngineProvider(engine);
    }

    public NoumenonEngine Engine => engine;

    public bool IsRunning => player is { PlaybackState: PlaybackState.Playing };

    public string Description { get; private set; } = "Audio stopped";

    public int SampleRate { get; private set; }

    /// <summary>The device's own buffering: the WASAPI buffer length or the ASIO buffer, in milliseconds.</summary>
    public int DeviceLatencyMs { get; private set; }

    public string? InputDescription => capture?.Description ?? (asioInput is not null ? "ASIO inputs 1-2" : null);

    public long InputUnderruns => capture?.Underruns ?? 0;

    public string? InputError => capture?.Error ?? asioInput?.Error;

    public double Load => provider.Load;

    public long Overruns => provider.Overruns;

    public long StartupOverruns => provider.StartupOverruns;

    public long Callbacks => provider.Callbacks;

    public int LastBlockFrames => provider.LastBlockFrames;

    public WavRecorder? Recorder => Volatile.Read(ref recorder);

    public string? LastError => Volatile.Read(ref lastError);

    public bool CanShowAsioPanel => asio is not null;

    public void TakePeaks(out float left, out float right) => provider.TakePeaks(out left, out right);

    public bool Start()
    {
        Stop();
        Volatile.Write(ref lastError, null);
        try
        {
            if (settings.Api == OutputApi.Asio)
                StartAsio();
            else
                StartWasapi();

            return true;
        }
        catch (Exception ex) when (AudioDevices.IsDeviceFailure(ex))
        {
            Stop();
            Volatile.Write(ref lastError, ex.Message);
            Description = "Audio failed: " + ex.Message;

            return false;
        }
    }

    public void Stop()
    {
        StopRecording();
        if (player is not null)
        {
            player.PlaybackStopped -= OnPlaybackStopped;
            if (asio is not null)
                asio.AudioAvailable -= OnAsioAudio;

            try
            {
                player.Stop();
                player.Dispose();
            }
            catch (Exception ex) when (AudioDevices.IsDeviceFailure(ex))
            {
            }

            player = null;
            asio = null;
            asioInput = null;
        }

        provider.Input = null;
        capture?.Dispose();
        capture = null;
        Description = "Audio stopped";
    }

    /// <summary>True when the Engine Rate parameter no longer matches what the stream was prepared for.</summary>
    public bool NeedsRestartForEngineRate => IsRunning && (engine.Parameters.GetInt(ParamId.EngineRate) == 1 ? 2 : 1) != engine.Oversampling;

    public void ShowAsioControlPanel() => asio?.ShowControlPanel();

    public WavRecorder StartRecording(string path)
    {
        StopRecording();
        var rate = SampleRate > 0 ? SampleRate : (int)NoumenonEngine.DefaultSampleRate;
        var created = new WavRecorder(path, rate, settings.RecordFormat, maxBlockFrames: provider.MaxFrames);
        Volatile.Write(ref recorder, created);
        provider.Recorder = created;

        return created;
    }

    public WavRecorder? StopRecording()
    {
        var current = Interlocked.Exchange(ref recorder, null);
        if (current is null)
            return null;

        provider.Recorder = null;
        current.Dispose();

        return current;
    }

    public void Dispose() => Stop();

    private void StartWasapi()
    {
        var device = AudioDevices.FindEndpoint(DataFlow.Render, settings.OutputDevice, fallbackToDefault: true) ?? throw new InvalidOperationException("No WASAPI output device is available.");
        var mixRate = device.AudioClient.MixFormat.SampleRate;
        var rate = settings.SampleRate > 0 ? settings.SampleRate : mixRate;
        var latency = Math.Clamp(settings.LatencyMs, MinLatencyMs, MaxLatencyMs);
        var shareMode = settings.Exclusive ? AudioClientShareMode.Exclusive : AudioClientShareMode.Shared;
        var blockFrames = Math.Max(64, (int)(rate * latency / 1000.0));
        PrepareEngine(rate, blockFrames * 2 + 1024);

        var output = new WasapiOut(device, shareMode, true, latency);
        output.Init(provider);
        if (settings.InputDevice is not null)
        {
            var inputDevice = AudioDevices.FindEndpoint(DataFlow.Capture, settings.InputDevice, fallbackToDefault: false);
            if (inputDevice is not null)
            {
                capture = new InputCapture(inputDevice, rate, blockFrames, latency);
                provider.Input = capture;
                capture.Start();
            }
        }

        output.PlaybackStopped += OnPlaybackStopped;
        output.Play();
        player = output;
        SampleRate = rate;
        DeviceLatencyMs = latency;
        Description = $"WASAPI {(settings.Exclusive ? "exclusive" : "shared")} · {device.FriendlyName} · {rate} Hz · {latency} ms{(rate != mixRate && !settings.Exclusive ? " (resampled from " + mixRate + " Hz)" : "")}";
    }

    private void StartAsio()
    {
        var name = settings.OutputDevice ?? AudioDevices.AsioDrivers().FirstOrDefault()?.Id ?? throw new InvalidOperationException("No ASIO driver is installed.");
        var output = new AsioOut(name);
        var rate = settings.SampleRate > 0 ? settings.SampleRate : DefaultAsioRate;
        if (!output.IsSampleRateSupported(rate))
        {
            var alternative = new[] { 48000, 44100, 96000, 88200 }.FirstOrDefault(output.IsSampleRateSupported);
            if (alternative == 0)
            {
                output.Dispose();
                throw new NotSupportedException($"ASIO driver '{name}' supports none of the usual sample rates.");
            }

            rate = alternative;
        }

        var inputs = settings.AsioInput ? Math.Min(2, output.DriverInputChannelCount) : 0;
        PrepareEngine(rate, 8192);
        if (inputs > 0)
        {
            output.InitRecordAndPlayback(provider, inputs, rate);
            asioInput = new AsioInputTap();
            asioInput.Configure(output.FramesPerBuffer, inputs);
            provider.Input = asioInput;
            output.AudioAvailable += OnAsioAudio;
        }
        else
        {
            output.Init(provider);
        }

        output.PlaybackStopped += OnPlaybackStopped;
        output.Play();
        player = output;
        asio = output;
        SampleRate = rate;
        DeviceLatencyMs = output.PlaybackLatency;
        Description = $"ASIO · {name} · {rate} Hz · {output.FramesPerBuffer} samples{(inputs > 0 ? " · " + inputs + " in" : "")}";
    }

    private void PrepareEngine(int rate, int maxFrames)
    {
        provider.Configure(rate, maxFrames);
        provider.Warm();   // JIT the callback path now; the Prepare below resets what it advanced
        engine.Prepare(rate, maxFrames);
    }

    private void OnAsioAudio(object? sender, AsioAudioAvailableEventArgs e) => asioInput?.Capture(e);

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is null)
            return;

        Volatile.Write(ref lastError, e.Exception.Message);
        Description = "Audio stopped: " + e.Exception.Message;
    }
}
