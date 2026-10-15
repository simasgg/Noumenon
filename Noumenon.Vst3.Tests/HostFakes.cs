using System.Runtime.InteropServices;
using NPlug;

namespace Noumenon.Vst3.Tests;

/// <summary>A host's component handler that records what the controller tells it.</summary>
internal sealed class RecordingHandler : IAudioControllerHandler
{
    public List<string> Calls { get; } = [];

    public List<(int Id, double Value)> Performed { get; } = [];

    public List<AudioRestartFlags> Restarts { get; } = [];

    public bool Dirty { get; private set; }

    public bool IsAdvancedEditSupported => true;

    public bool IsCreateContextMenuSupported => false;

    public bool IsRequestBusActivationSupported => false;

    public bool IsProgressSupported => false;

    public bool IsUnitAndProgramListSupported => false;

    public void BeginEdit(AudioParameterId id) => Calls.Add($"begin {id.Value}");

    public void PerformEdit(AudioParameterId id, double valueNormalized)
    {
        Calls.Add($"perform {id.Value}");
        Performed.Add((id.Value, valueNormalized));
    }

    public void EndEdit(AudioParameterId id) => Calls.Add($"end {id.Value}");

    public void RestartComponent(AudioRestartFlags flags) => Restarts.Add(flags);

    public void SetDirty(bool state) => Dirty = state;

    public void RequestOpenEditor(string name)
    {
    }

    public void StartGroupEdit()
    {
    }

    public void FinishGroupEdit()
    {
    }

    public IAudioContextMenu CreateContextMenu(IAudioPluginView plugView, AudioParameterId paramID) => throw new NotSupportedException();

    public void RequestBusActivation(BusMediaType type, BusDirection dir, int index, bool state)
    {
    }

    public AudioProgressId StartProgress(AudioProgressType type, string? optionalDescription) => default;

    public void UpdateProgress(AudioProgressId id, double normValue)
    {
    }

    public void FinishProgress(AudioProgressId id)
    {
    }

    public void NotifyUnitSelection(AudioUnitId unitId)
    {
    }

    public void NotifyProgramListChange(AudioProgramListId listId, int programIndex)
    {
    }
}

/// <summary>A host's plug frame that records the sizes the view asks for.</summary>
internal sealed class RecordingFrame : IAudioPluginFrame
{
    public List<ViewRectangle> Resizes { get; } = [];

    public void ResizeView(IAudioPluginView view, ViewRectangle newSize) => Resizes.Add(newSize);
}

/// <summary>
/// One audio bus in native memory, laid out as VST3's AudioBusBuffers (channel count, silence
/// flags, channel pointers), so a test can hand the processor exactly what a host does.
/// </summary>
internal sealed unsafe class NativeBus : IDisposable
{
    private readonly byte* bus;
    private readonly float** channels;
    private readonly int channelCount;
    private readonly int frames;

    public NativeBus(int channelCount, int frames)
    {
        this.channelCount = channelCount;
        this.frames = frames;
        bus = (byte*)NativeMemory.AllocZeroed(24);
        channels = (float**)NativeMemory.AllocZeroed((nuint)(channelCount * sizeof(float*)));
        for (var c = 0; c < channelCount; c++)
            channels[c] = (float*)NativeMemory.AllocZeroed((nuint)(frames * sizeof(float)));

        *(int*)bus = channelCount;
        *(float***)(bus + 16) = channels;
    }

    public AudioBusBuffers* Buffers => (AudioBusBuffers*)bus;

    public ulong SilenceFlags
    {
        get => *(ulong*)(bus + 8);
        set => *(ulong*)(bus + 8) = value;
    }

    public Span<float> Channel(int channel) => new(channels[channel], frames);

    public float Peak()
    {
        var peak = 0f;
        for (var c = 0; c < channelCount; c++)
        {
            foreach (var sample in Channel(c))
                peak = MathF.Max(peak, MathF.Abs(sample));
        }

        return peak;
    }

    public void Dispose()
    {
        for (var c = 0; c < channelCount; c++)
            NativeMemory.Free(channels[c]);

        NativeMemory.Free(channels);
        NativeMemory.Free(bus);
    }
}
