using System.Text;
using System.Text.Json;
using Jacobi.Vst.Core;
using Jacobi.Vst.Plugin.Framework;
using Noumenon.Engine.Presets;
using Noumenon.Vst2.Parameters;

namespace Noumenon.Vst2;

/// <summary>
/// The state chunk: the snapshot as the same JSON a <c>.noumenon</c> file holds, UTF-8 and
/// nothing else, so a project saved in the host and a preset saved from the panel are the same
/// thing and either plugin identity reads the other's. Loading applies the snapshot the way the
/// standalone loads a preset — parameters, lanes, chords, scale and seed at once while the audio
/// keeps running, the samples decoded on a worker — and then hands the host the parameter values
/// it should display, without automation events. <see cref="IVstPluginPersistence"/> is
/// implemented so the framework flags the plugin as chunk-based; the command overrides route
/// the host's chunk calls here directly, bypassing the framework's program serialization.
/// </summary>
internal sealed class PluginState(PluginSession session, PluginParameters parameters) : IVstPluginPersistence
{
    public byte[] Save() => Encoding.UTF8.GetBytes(PresetIo.Serialize(session.Engine.CapturePreset(session.PresetName)));

    /// <summary>Returns the bytes consumed: all of them, or 0 when the chunk is empty or not ours.</summary>
    public int Load(byte[]? data)
    {
        if (data is null || data.Length == 0)
            return 0;

        PresetState state;
        try
        {
            state = PresetIo.Deserialize(Encoding.UTF8.GetString(data));
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException or DecoderFallbackException)
        {
            session.Notice("State not loaded: " + ex.Message);
            return 0;
        }

        Apply(state, null);

        return data.Length;
    }

    /// <summary>Recalls a snapshot from any non-audio thread: a chunk, a preset file, the init patch.</summary>
    public void Apply(PresetState state, string? path)
    {
        var engine = session.Engine;
        engine.ApplyPreset(state, loadSamples: false);
        engine.Samples.ClearAll();
        session.SetPreset(state.Name, path);
        parameters.PushToHost(notify: false);
        if (state.Samples.Count == 0)
            return;

        var references = state.Samples.ToArray();
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
                session.Notice(string.Join(" · ", warnings));
        });
    }

    public bool CanLoadChunk(VstPatchChunkInfo chunkInfo) => PluginIdentity.IsOurChunk(chunkInfo.PluginID);

    public void ReadPrograms(Stream stream, VstProgramCollection programs)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        Load(buffer.ToArray());
    }

    public void WritePrograms(Stream stream, VstProgramCollection programs)
    {
        var bytes = Save();
        stream.Write(bytes, 0, bytes.Length);
    }
}
