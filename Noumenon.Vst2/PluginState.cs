using Jacobi.Vst.Core;
using Jacobi.Vst.Plugin.Framework;

namespace Noumenon.Vst2;

/// <summary>
/// The state chunk: the session's snapshot JSON (<see cref="Engine.Hosting.PluginSessionBase.SaveState"/>),
/// whichever of bank or program the host asks for, so a project saved in the host and a preset
/// saved from the panel are the same thing and either plugin identity reads the other's.
/// <see cref="IVstPluginPersistence"/> is implemented so the framework flags the plugin as
/// chunk-based; the command overrides route the host's chunk calls here directly, bypassing the
/// framework's program serialization.
/// </summary>
internal sealed class PluginState(PluginSession session) : IVstPluginPersistence
{
    public byte[] Save() => session.SaveState();

    /// <summary>Returns the bytes consumed: all of them, or 0 when the chunk is empty or not ours.</summary>
    public int Load(byte[]? data) => data is { Length: > 0 } && session.LoadState(data) ? data.Length : 0;

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
