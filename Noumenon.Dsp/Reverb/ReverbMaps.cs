namespace Noumenon.Dsp.Reverb;

/// <summary>
/// The normalized-control mappings the two engines share (from EmptySpace's DspHelper): Bass
/// 0..1 → a log-symmetric low-band decay multiplier 0.25× .. 4× (0.5 = neutral); ModDepth 0..1 →
/// 0 .. <see cref="MaxModDepthMs"/> of tank-LFO excursion (the plate sizes its headroom from the
/// maximum); ModRate 0..1 → 0.1 .. 10 Hz, log-symmetric about 1 Hz; HighCut 0..1 → 1 .. 20 kHz
/// (log, 1 = Off) and LowCut 0..1 → 20 Hz .. 1 kHz (log, 0 = Off); Diffusion scales the engines'
/// stock diffusion gains, with <see cref="LegacyDiffusion"/> being exactly 1×.
/// </summary>
internal static class ReverbMaps
{
    public const float MaxModDepthMs = 2f;
    public const float LegacyDiffusion = 0.75f;

    public static float BassMultiplier(float bass) => MathF.Pow(4f, 2f * bass - 1f);

    public static float ModDepthMs(float depth) => depth * MaxModDepthMs;

    public static float ModRateHz(float rate) => MathF.Pow(10f, 2f * rate - 1f);

    public static float HighCutHz(float highCut) => 1000f * MathF.Pow(20f, highCut);

    public static float LowCutHz(float lowCut) => 20f * MathF.Pow(50f, lowCut);

    public static float DiffusionScale(float diffusion) => diffusion / LegacyDiffusion;
}
