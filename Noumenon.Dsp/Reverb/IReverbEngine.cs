namespace Noumenon.Dsp.Reverb;

/// <summary>
/// The common surface of the two reverb algorithms ported from EmptySpace (Freeverb and the
/// Dattorro plate), so <see cref="ReverbBlock"/> can swap them behind one API. The thirteen sound
/// parameters mean the same thing for both; <see cref="ModDepth"/>/<see cref="ModRate"/>/
/// <see cref="ModDrift"/> drive only the plate's tank LFO (Freeverb stores them without effect).
/// <see cref="HighCut"/>/<see cref="LowCut"/> are one-pole cuts on the wet output whose default
/// endpoints (1 / 0) are exact bypasses. <see cref="ProcessSample"/> is the audio-thread hot path
/// and must be allocation-free — size all buffers in <see cref="Prepare"/>.
/// </summary>
public interface IReverbEngine
{
    float Size { get; set; }
    float Damping { get; set; }
    float Bass { get; set; }
    float Width { get; set; }
    float Mix { get; set; }
    float PreDelayMs { get; set; }
    bool Freeze { get; set; }
    float ModDepth { get; set; }
    float ModRate { get; set; }
    bool ModDrift { get; set; }
    float HighCut { get; set; }
    float LowCut { get; set; }
    float Diffusion { get; set; }

    void Prepare(double sampleRate, int maxBlockSize);
    void Reset();
    void ProcessSample(float inLeft, float inRight, out float outLeft, out float outRight);
    void Process(float[] left, float[] right, int count);
    void Process(float[] left, float[] right, int offset, int count);
}
