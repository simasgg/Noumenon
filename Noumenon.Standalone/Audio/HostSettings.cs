using System.Text.Json;
using System.Text.Json.Serialization;
using Noumenon.Engine.Modulation;
using Noumenon.Engine.Recording;

namespace Noumenon.Standalone.Audio;

public enum OutputApi
{
    Wasapi,
    Asio,
}

/// <summary>
/// What the standalone remembers between runs — the devices, the rate and buffer, the record
/// format and folder, the transport tempo, the lane limit, the learned MIDI controllers — as a JSON file in the user's
/// application-data folder. Loading never fails: a missing or damaged file yields the defaults,
/// which open the system's default WASAPI output at its own rate with no input and no MIDI.
/// </summary>
internal sealed class HostSettings
{
    public OutputApi Api { get; set; } = OutputApi.Wasapi;

    /// <summary>The WASAPI endpoint ID or the ASIO driver name; null means the system default (WASAPI) or the first driver (ASIO).</summary>
    public string? OutputDevice { get; set; }

    public bool Exclusive { get; set; }

    /// <summary>The sample rate to run at; 0 means the device's own rate.</summary>
    public int SampleRate { get; set; }

    /// <summary>The WASAPI buffer length; ASIO uses the driver's own buffer size.</summary>
    public int LatencyMs { get; set; } = 20;

    /// <summary>The WASAPI capture endpoint ID feeding Samp↔In, or null for no input.</summary>
    public string? InputDevice { get; set; }

    /// <summary>With ASIO: feed the driver's first two input channels to Samp↔In.</summary>
    public bool AsioInput { get; set; }

    /// <summary>The MIDI input port's product name, or null for none.</summary>
    public string? MidiDevice { get; set; }

    public bool KeyboardMidi { get; set; } = true;

    public WavSampleFormat RecordFormat { get; set; } = WavSampleFormat.Pcm24;

    public string? RecordFolder { get; set; }

    public float LaneMaxSeconds { get; set; } = 60f;

    public double Tempo { get; set; } = 120.0;

    public int PartyIntervalSeconds { get; set; } = 30;

    public string? LastPresetFolder { get; set; }

    /// <summary>The editor's panel size: 1, 1.5 or 2.</summary>
    public float EditorScale { get; set; } = 1f;

    /// <summary>The learned MIDI controllers: they belong to the player's setup, so presets never change them.</summary>
    public List<MidiMapping> MidiMap { get; set; } = [];

    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Noumenon");

    public static string FilePath => Path.Combine(Folder, "settings.json");

    [JsonIgnore]
    public string EffectiveRecordFolder => string.IsNullOrWhiteSpace(RecordFolder) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Noumenon", "Recordings") : RecordFolder;

    public static HostSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize(File.ReadAllText(FilePath), SettingsJsonContext.Default.HostSettings) ?? new HostSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }

        return new HostSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, SettingsJsonContext.Default.HostSettings));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public HostSettings Clone() => (HostSettings)MemberwiseClone();

    public void CopyFrom(HostSettings other)
    {
        Api = other.Api;
        OutputDevice = other.OutputDevice;
        Exclusive = other.Exclusive;
        SampleRate = other.SampleRate;
        LatencyMs = other.LatencyMs;
        InputDevice = other.InputDevice;
        AsioInput = other.AsioInput;
        MidiDevice = other.MidiDevice;
        KeyboardMidi = other.KeyboardMidi;
        RecordFormat = other.RecordFormat;
        RecordFolder = other.RecordFolder;
        LaneMaxSeconds = other.LaneMaxSeconds;
        Tempo = other.Tempo;
        PartyIntervalSeconds = other.PartyIntervalSeconds;
        LastPresetFolder = other.LastPresetFolder;
        EditorScale = other.EditorScale;
        MidiMap = other.MidiMap;
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(HostSettings))]
internal partial class SettingsJsonContext : JsonSerializerContext
{
}
