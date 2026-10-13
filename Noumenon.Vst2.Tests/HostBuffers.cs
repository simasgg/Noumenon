using System.Runtime.InteropServices;
using Jacobi.Vst.Core;

namespace Noumenon.Vst2.Tests;

/// <summary>Pinned float arrays wrapped as the host's audio buffers, so a test can call the processor exactly as a host does.</summary>
internal sealed unsafe class HostBuffers : IDisposable
{
    private readonly GCHandle[] handles;

    public HostBuffers(int channels, int frames)
    {
        Arrays = new float[channels][];
        Buffers = new VstAudioBuffer[channels];
        handles = new GCHandle[channels];
        for (var c = 0; c < channels; c++)
        {
            Arrays[c] = new float[frames];
            handles[c] = GCHandle.Alloc(Arrays[c], GCHandleType.Pinned);
            Buffers[c] = new VstAudioBuffer((float*)handles[c].AddrOfPinnedObject(), frames, canWrite: true);
        }
    }

    public float[][] Arrays { get; }

    public VstAudioBuffer[] Buffers { get; }

    public float Peak()
    {
        var peak = 0f;
        foreach (var channel in Arrays)
        {
            foreach (var sample in channel)
                peak = MathF.Max(peak, MathF.Abs(sample));
        }

        return peak;
    }

    public void Dispose()
    {
        foreach (var handle in handles)
            handle.Free();
    }
}
