using System.Text.Json.Serialization;

namespace Noumenon.Engine.Presets;

/// <summary>
/// Everything a snapshot holds, as plain data for JSON: parameter values keyed by
/// <c>ParamId</c> name (a missing key falls back to the table default, so presets survive new
/// parameters), the lanes' recordings and settings, the user chord memories, the sample
/// references, the seed, the user scale and the randomizer locks. <see cref="Version"/> is
/// bumped when the shape changes and <see cref="PresetIo"/> migrates older files.
/// </summary>
public sealed class PresetState
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public string Name { get; set; } = "";

    public string? Description { get; set; }

    public Dictionary<string, float> Parameters { get; set; } = new();

    public List<LaneState> Lanes { get; set; } = [];

    public List<float[]> UserChords { get; set; } = [];

    public List<SampleReference> Samples { get; set; } = [];

    public ulong Seed { get; set; } = 1;

    public ScaleState? UserScale { get; set; }

    public List<string> Locked { get; set; } = [];
}

public sealed class LaneState
{
    public string Parameter { get; set; } = "";

    public string Mode { get; set; } = "Static";

    public float SpeedMultiplier { get; set; } = 1f;

    public float[] Points { get; set; } = [];
}

public sealed class SampleReference
{
    public int Key { get; set; }

    public string Path { get; set; } = "";

    public float Gain { get; set; } = 1f;

    public int RootNote { get; set; } = 60;
}

public sealed class ScaleState
{
    public string Name { get; set; } = "";

    public float[] DegreesCents { get; set; } = [];

    public float PeriodCents { get; set; } = 1200f;
}

[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PresetState))]
internal partial class PresetJsonContext : JsonSerializerContext
{
}
