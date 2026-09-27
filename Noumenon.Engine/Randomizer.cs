using Noumenon.Dsp.Filters;
using Noumenon.Dsp.Oscillators;
using Noumenon.Dsp.Shared;
using Noumenon.Engine.Parameters;

namespace Noumenon.Engine;

/// <summary>What <see cref="Randomizer.Randomize"/> touches; the order is the parameter table's choice list.</summary>
public enum RandomizeScope
{
    All,
    Oscillators,
    Sampler,
    Effects,
    Lanes,
}

/// <summary>
/// Seeded patch randomization: the same seed always produces the same patch, so a happy accident
/// can be written down and recalled. <paramref name="amount"/> scales how far it strays from a
/// tonal drone — at 1 it allows MF's anything-goes chaos (wild pitches, muted slots, odd filter
/// modes), at small values it keeps every slot on a root-plus-intervals voicing with a few cents
/// of detune. Phase 1 covers the oscillator scope (slots, section filters, the combine stage); the
/// sampler, effect and lane scopes follow their blocks in Phases 2-4, and Phase 4 adds lock
/// flags and Mutate.
/// </summary>
public static class Randomizer
{
    /// <summary>Semitone offsets from the root a tonal slot may take, weighted towards unisons, octaves and fifths.</summary>
    private static readonly int[] Intervals = [-24, -12, -12, -5, 0, 0, 0, 0, 7, 7, 12, 12, 19, 24];

    private static readonly Waveform[][] PanelLayout =
    [
        [Waveform.Sine, Waveform.Sine, Waveform.Sine, Waveform.Triangle, Waveform.BipolarPulse, Waveform.NoiseLp],
        [Waveform.Sine, Waveform.Sine, Waveform.Triangle, Waveform.Pulse, Waveform.BipolarPulse, Waveform.NoiseSh],
    ];

    public static void Randomize(ParameterBank bank, ulong seed, RandomizeScope scope = RandomizeScope.All, float amount = 1f)
    {
        var rng = new Xoshiro128(seed);
        amount = DspHelper.Clamp01(amount);

        if (scope is RandomizeScope.All or RandomizeScope.Oscillators)
            RandomizeOscillators(bank, rng, amount);
    }

    private static void RandomizeOscillators(ParameterBank bank, Xoshiro128 rng, float amount)
    {
        var root = -24 + rng.NextInt(25);   // C2..C4

        for (var s = 0; s < ParameterTable.SectionCount; s++)
        {
            var section = (SectionId)s;
            for (var slot = 0; slot < ParameterTable.SlotsPerSection; slot++)
            {
                var panelWave = PanelLayout[s][slot];
                var noiseSlot = panelWave is Waveform.NoiseLp or Waveform.NoiseSh;
                Waveform wave;
                if (noiseSlot)
                    wave = rng.Chance(0.5f) ? Waveform.NoiseLp : Waveform.NoiseSh;
                else
                    wave = rng.Chance(0.3f * amount) ? (Waveform)rng.NextInt((int)Waveform.BipolarPulse + 1) : panelWave;

                int semitone;
                if (noiseSlot)
                    semitone = 0;
                else if (rng.Chance(0.35f * amount))
                    semitone = rng.NextInt(129) - 64;
                else
                    semitone = Math.Clamp(root + Intervals[rng.NextInt(Intervals.Length)], -64, 64);

                var isNoise = wave is Waveform.NoiseLp or Waveform.NoiseSh;
                bank.Set(ParameterTable.Osc(section, slot, OscParam.On), !rng.Chance(0.15f * amount));
                bank.Set(ParameterTable.Osc(section, slot, OscParam.Wave), (int)wave);
                bank.Set(ParameterTable.Osc(section, slot, OscParam.Semitone), semitone);
                bank.Set(ParameterTable.Osc(section, slot, OscParam.Fine), rng.NextBipolar() * (4f + 20f * amount));
                bank.Set(ParameterTable.Osc(section, slot, OscParam.Shape), rng.NextRange(0.2f, 0.8f));
                bank.Set(ParameterTable.Osc(section, slot, OscParam.NoiseCutoff), LogUniform(rng, 200f, 8000f));
                bank.Set(ParameterTable.Osc(section, slot, OscParam.Level), isNoise ? rng.NextRange(0f, 0.25f) : rng.NextRange(0.15f, 0.85f));
                bank.Set(ParameterTable.Osc(section, slot, OscParam.Trim), 1f);
            }

            var filterType = rng.Chance(0.2f * amount) ? (FilterType)(1 + rng.NextInt(2)) : FilterType.LowPass;
            bank.Set(ParameterTable.Section(section, SectionParam.Cutoff), LogUniform(rng, 300f, 12000f));
            bank.Set(ParameterTable.Section(section, SectionParam.Reso), rng.NextRange(0f, 0.6f * amount));
            bank.Set(ParameterTable.Section(section, SectionParam.FilterType), (int)filterType);
            bank.Set(ParameterTable.Section(section, SectionParam.Level), rng.NextRange(0.5f, 0.8f));
        }

        bank.Set(ParamId.MixCrossfade, rng.NextRange(0.2f, 0.8f));
        bank.Set(ParamId.MixSumBalance, rng.NextBipolar() * 0.5f);
        bank.Set(ParamId.MixRingLevel, rng.NextRange(0.5f, 2f));
    }

    private static float LogUniform(Xoshiro128 rng, float min, float max) => min * MathF.Pow(max / min, rng.NextFloat());
}
