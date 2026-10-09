using System.Text.Json;
using System.Text.Json.Nodes;

namespace Noumenon.Engine.Presets;

/// <summary>
/// Reads and writes <c>.noumenon</c> files (JSON, source-generated serializer) and scans a folder
/// for a preset bank. Loading goes through a JSON node first so older versions can be migrated
/// field by field before deserializing.
/// </summary>
public static class PresetIo
{
    public const string Extension = ".noumenon";

    public static string Serialize(PresetState state) => JsonSerializer.Serialize(state, PresetJsonContext.Default.PresetState);

    public static PresetState Deserialize(string json)
    {
        var node = JsonNode.Parse(json) as JsonObject ?? throw new FormatException("A preset must be a JSON object.");
        var version = node["Version"]?.GetValue<int>() ?? 0;
        if (version > PresetState.CurrentVersion)
            throw new FormatException($"Preset version {version} is newer than this build understands ({PresetState.CurrentVersion}).");

        Migrate(node, version);

        return node.Deserialize(PresetJsonContext.Default.PresetState) ?? throw new FormatException("The preset could not be read.");
    }

    public static void Save(PresetState state, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
        File.WriteAllText(path, Serialize(state));
    }

    public static PresetState Load(string path)
    {
        var state = Deserialize(File.ReadAllText(path));
        if (string.IsNullOrWhiteSpace(state.Name))
            state.Name = Path.GetFileNameWithoutExtension(path);

        return state;
    }

    /// <summary>The presets in a folder (non-recursive), by name.</summary>
    public static IReadOnlyList<PresetInfo> Scan(string folder)
    {
        if (!Directory.Exists(folder))
            return [];

        return Directory.EnumerateFiles(folder, "*" + Extension)
            .Select(path => new PresetInfo(Path.GetFileNameWithoutExtension(path), path))
            .OrderBy(info => info.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>Upgrades an older preset in place; version 0 (no field) is treated as the first format.</summary>
    private static void Migrate(JsonObject node, int version)
    {
        if (version < 1)
            node["Version"] = 1;
    }
}

public sealed record PresetInfo(string Name, string Path);
