using System.Globalization;
using System.Text;
using System.Text.Json;
using Noumenon.Dsp.Shared;
using Noumenon.Engine.Parameters;
using Noumenon.Engine.Presets;

namespace Noumenon.Engine.Hosting;

/// <summary>
/// What a plugin instance keeps around its engine, whatever the plugin format: the preset library
/// (the factory bank next to the plugin or up the tree from it, the user bank in Documents), the
/// name and path of the loaded snapshot, a notice line for the editor's bar, the state a host
/// saves with its project and the randomizer's seeds. The state is the snapshot as the same JSON
/// a <c>.noumenon</c> file holds, UTF-8 and nothing else, so a project saved in any host, a panel
/// preset and the other plugin format's state are the same thing. Loading one applies it the way
/// the standalone loads a preset — parameters, lanes, chords, scale and seed at once while the
/// audio keeps running, the samples decoded on a worker — plus the learned MIDI controllers,
/// which belong to the project (a preset picked on the panel keeps them); and then calls
/// <see cref="OnSnapshotApplied"/>, where the format brings the host's view of the parameters up
/// to date without reporting them as edits. Everything here runs on the host's UI thread except
/// the sample decoding.
/// </summary>
public abstract class PluginSessionBase
{
    private const int NoticeMs = 8000;

    private readonly Xoshiro128 seeds = new(((ulong)Environment.TickCount64 * 0x9E3779B97F4A7C15UL) ^ (ulong)DateTime.UtcNow.Ticks);
    private string? notice;
    private long noticeUntil;

    /// <summary><paramref name="assemblyFolder"/> is where the plugin was loaded from, the start of the factory bank search (the host's base directory is the DAW's).</summary>
    protected PluginSessionBase(string? assemblyFolder)
    {
        Engine = new NoumenonEngine();
        Library = new PresetLibrary(assemblyFolder);
    }

    public NoumenonEngine Engine { get; }

    public PresetLibrary Library { get; }

    public string PresetName { get; private set; } = "Init";

    public string? PresetPath { get; private set; }

    /// <summary>The current notice while it is fresh, else null.</summary>
    public string? CurrentNotice => Volatile.Read(ref notice) is { } text && Environment.TickCount64 < Volatile.Read(ref noticeUntil) ? text : null;

    /// <summary>The folder an assembly was loaded from, or null when it has no location.</summary>
    public static string? FolderOf(Type type)
    {
        var location = type.Assembly.Location;

        return string.IsNullOrEmpty(location) ? null : Path.GetDirectoryName(location);
    }

    /// <summary>Any thread: shows a line in the editor's bar for a few seconds.</summary>
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

    public byte[] SaveState() => Encoding.UTF8.GetBytes(PresetIo.Serialize(Engine.CapturePreset(PresetName)));

    /// <summary>Applies a saved state; false, with a notice, when the data is empty or not a snapshot, and the instrument keeps what it had.</summary>
    public bool LoadState(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return false;

        PresetState state;
        try
        {
            state = PresetIo.Deserialize(Encoding.UTF8.GetString(data));
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException or InvalidOperationException)
        {
            Notice("State not loaded: " + ex.Message);
            return false;
        }

        Engine.ApplyMidiMap(state.MidiMap);
        Apply(state, null);

        return true;
    }

    /// <summary>Recalls a snapshot — a host's state, a preset file, the init patch — with the samples it references decoded on a worker.</summary>
    public void Apply(PresetState state, string? path)
    {
        Engine.ApplyPreset(state, loadSamples: false);
        Engine.Samples.ClearAll();
        SetPreset(state.Name, path);
        OnSnapshotApplied();
        if (state.Samples.Count == 0)
            return;

        var references = state.Samples.ToArray();
        var engine = Engine;
        Task.Run(() =>
        {
            var warnings = new List<string>();
            foreach (var reference in references)
            {
                try
                {
                    engine.Samples.Load(reference.Key, reference.Path, reference.Gain, reference.RootNote);
                }
                catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidDataException or UnauthorizedAccessException)
                {
                    warnings.Add($"Sample {reference.Key} ({Path.GetFileName(reference.Path)}): {ex.Message}");
                }
            }

            if (warnings.Count > 0)
                Notice(string.Join(" · ", warnings));
        });
    }

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

    /// <summary>UI thread, right after <see cref="Apply"/> has written the bank: bring the host's parameter values up to date, silently.</summary>
    protected virtual void OnSnapshotApplied()
    {
    }
}
