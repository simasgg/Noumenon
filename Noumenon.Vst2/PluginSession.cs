using System.Globalization;
using Jacobi.Vst.Core;
using Jacobi.Vst.Plugin.Framework;
using Noumenon.Dsp.Shared;
using Noumenon.Engine;
using Noumenon.Engine.Parameters;
using Noumenon.Engine.Presets;

namespace Noumenon.Vst2;

/// <summary>
/// What the host side of the plugin shares across its components: the engine, the host services
/// once the plugin is open, the preset library (the factory bank next to the plugin or up the
/// tree, the user bank in Documents), the name of the loaded snapshot, a notice line for the
/// editor's bar, the seeds the randomizer draws from and the latency the host was last told.
///
/// There is deliberately no free-running background thread here. The randomizer allocates, so it
/// cannot run on the audio thread; the editor's 33 ms poll runs it when the GUI is open, exactly
/// as the standalone's always-present UI timer does. A Randomize trigger the host automates while
/// the GUI is closed stays latched on the engine (<see cref="NoumenonEngine.ConsumeRandomizeRequest"/>)
/// and is applied the next time the editor opens — a plugin with no window has no UI thread to run
/// it on, and a per-instance managed timer is exactly the kind of cross-plugin background execution
/// that makes loading a second .NET bridge into the host process fragile.
/// </summary>
internal sealed class PluginSession
{
    private const int NoticeMs = 8000;

    private readonly Xoshiro128 seeds = new(((ulong)Environment.TickCount64 * 0x9E3779B97F4A7C15UL) ^ (ulong)DateTime.UtcNow.Ticks);
    private IVstHost? host;
    private string? notice;
    private long noticeUntil;
    private int reportedLatency;

    public PluginSession()
    {
        Engine = new NoumenonEngine();
        Library = new PresetLibrary(AssemblyFolder);
        reportedLatency = Engine.LatencySamples;
    }

    /// <summary>The folder the plugin was loaded from (the deploy folder), or null when the assembly has no location.</summary>
    public static string? AssemblyFolder
    {
        get
        {
            var location = typeof(PluginSession).Assembly.Location;

            return string.IsNullOrEmpty(location) ? null : Path.GetDirectoryName(location);
        }
    }

    public NoumenonEngine Engine { get; }

    public PresetLibrary Library { get; }

    public IVstHost? Host => Volatile.Read(ref host);

    public string PresetName { get; private set; } = "Init";

    public string? PresetPath { get; private set; }

    /// <summary>The preset name cut to what VST2 allows for a program name.</summary>
    public string ProgramName => PresetName.Length <= Constants.MaxProgramNameLength ? PresetName : PresetName[..Constants.MaxProgramNameLength];

    /// <summary>The current notice while it is fresh, else null.</summary>
    public string? CurrentNotice => Volatile.Read(ref notice) is { } text && Environment.TickCount64 < Volatile.Read(ref noticeUntil) ? text : null;

    public void Opened(IVstHost host) => Volatile.Write(ref this.host, host);

    public void Notice(string text)
    {
        Volatile.Write(ref noticeUntil, Environment.TickCount64 + NoticeMs);
        Volatile.Write(ref notice, text);
    }

    public void SetPreset(string name, string? path)
    {
        PresetName = string.IsNullOrWhiteSpace(name) ? "Init" : name;
        PresetPath = path;
    }

    /// <summary>Tells the host the program name changed, from the UI thread only.</summary>
    public void TellHostDisplayChanged() => Host?.GetInstance<IVstHostShell>()?.UpdateDisplay();

    /// <summary>Runs the randomizer with the Random Seed parameter or a fresh seed, as the standalone does.</summary>
    public void Randomize()
    {
        var bank = Engine.Parameters;
        var fixedSeed = bank.GetInt(ParamId.RandomSeed);
        var seed = fixedSeed != 0 ? (ulong)fixedSeed : seeds.Next();
        Randomizer.Randomize(Engine, seed, (RandomizeScope)bank.GetInt(ParamId.RandomizeScope), bank.Get(ParamId.RandomizeAmount));
        SetPreset($"random {seed}", null);
        var scope = ParameterTable.RandomizeScopes[Math.Clamp(bank.GetInt(ParamId.RandomizeScope), 0, ParameterTable.RandomizeScopes.Count - 1)];
        Notice($"Randomized: seed {seed}, {scope}, {(bank.Get(ParamId.RandomizeAmount) * 100).ToString("0", CultureInfo.InvariantCulture)} %");
    }

    /// <summary>UI thread: when Engine Rate changed the latency, asks the host to re-read it.</summary>
    public void PollLatency()
    {
        var latency = Engine.LatencySamples;
        if (latency == reportedLatency)
            return;

        reportedLatency = latency;
        Host?.GetInstance<IVstHostSequencer>()?.UpdatePluginIO();
    }
}
