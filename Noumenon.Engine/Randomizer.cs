using Noumenon.Dsp.Effects;
using Noumenon.Dsp.Filters;
using Noumenon.Dsp.Oscillators;
using Noumenon.Dsp.Reverb;
using Noumenon.Dsp.Shared;
using Noumenon.Engine.Lanes;
using Noumenon.Engine.Parameters;

namespace Noumenon.Engine;

/// <summary>What <see cref="Randomizer.Randomize(ParameterBank,ulong,RandomizeScope,float,bool[])"/> touches; the order is the parameter table's choice list.</summary>
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
/// can be written down and recalled. Amount scales how far it strays from a tonal drone — at 1 it
/// allows MF's anything-goes chaos (wild pitches, muted slots, odd filter modes), at small values
/// it keeps every slot on a root-plus-intervals voicing with a few cents of detune. Scopes pick
/// the oscillators (slots, section filters, the combine stage), the sampler, the effect chain or
/// the lanes (fresh smooth random loops on a random subset of faders); All takes everything, which
/// is MF's Randomize plus living faders. Locked parameters are never touched, and
/// <see cref="Mutate"/> nudges the current patch instead of replacing it. Runs on the UI thread —
/// it allocates — and is picked up by the engine through the parameter bank and lane snapshots.
/// </summary>
public static class Randomizer
{
    private static readonly int[] Intervals = [-24, -12, -12, -5, 0, 0, 0, 0, 7, 7, 12, 12, 19, 24];   // weighted towards unisons, octaves and fifths

    private static readonly Waveform[][] PanelLayout =
    [
        [Waveform.Sine, Waveform.Sine, Waveform.Sine, Waveform.Triangle, Waveform.BipolarPulse, Waveform.NoiseLp],
        [Waveform.Sine, Waveform.Sine, Waveform.Triangle, Waveform.Pulse, Waveform.BipolarPulse, Waveform.NoiseSh],
    ];

    private static readonly float[] Directions = [1f, 1f, 1f, -1f, -1f, 0.5f, -0.5f, 2f, -2f];

    public static void Randomize(NoumenonEngine engine, ulong seed, RandomizeScope scope = RandomizeScope.All, float amount = 1f)
    {
        Randomize(engine.Parameters, seed, scope, amount, engine.Locks);
        if (scope is RandomizeScope.All or RandomizeScope.Lanes)
            RandomizeLanes(engine.Lanes, engine.Parameters, seed ^ 0x5DEECE66DUL, amount, engine.Locks);

        engine.Clock.RequestRestart();
    }

    public static void Randomize(ParameterBank bank, ulong seed, RandomizeScope scope = RandomizeScope.All, float amount = 1f, bool[]? locks = null)
    {
        var rng = new Xoshiro128(seed);
        amount = DspHelper.Clamp01(amount);

        if (scope is RandomizeScope.All or RandomizeScope.Oscillators)
            RandomizeOscillators(bank, rng, amount, locks);

        if (scope is RandomizeScope.All or RandomizeScope.Sampler)
            RandomizeSampler(bank, rng, amount, locks);

        if (scope is RandomizeScope.All or RandomizeScope.Effects)
            RandomizeEffects(bank, rng, amount, locks);
    }

    /// <summary>Gives a random subset of the lanes a fresh smooth loop around their current value and clears the rest.</summary>
    public static void RandomizeLanes(LaneSet lanes, ParameterBank bank, ulong seed, float amount = 1f, bool[]? locks = null)
    {
        var rng = new Xoshiro128(seed);
        amount = DspHelper.Clamp01(amount);
        var probability = 0.3f + 0.4f * amount;
        foreach (var lane in lanes.All)
        {
            if (locks is not null && locks[(int)lane.Id])
                continue;

            if (!rng.Chance(probability))
            {
                lane.Clear();
                continue;
            }

            var seconds = rng.NextRange(4f, Math.Min(40f, lanes.MaxSeconds));
            var count = Math.Max(2, (int)(seconds * AutomationLane.PointsPerSecond));
            var centre = lane.Info.ToNormalized(bank.Get(lane.Id));
            var spread = 0.15f + 0.35f * amount;
            var normalized = RandomLoop(rng, count, centre, spread);
            var points = new float[count];
            for (var i = 0; i < count; i++)
                points[i] = lane.Info.FromNormalized(normalized[i]);

            lane.Load(new LaneSnapshot(points, LaneMode.Automated, 1f));
        }
    }

    /// <summary>Nudges every unlocked continuous parameter by up to ±10 % of its range times <paramref name="amount"/>; integers step by one now and then.</summary>
    public static void Mutate(ParameterBank bank, ulong seed, float amount = 0.5f, bool[]? locks = null)
    {
        var rng = new Xoshiro128(seed);
        amount = DspHelper.Clamp01(amount);
        foreach (var info in ParameterTable.All)
        {
            if (locks is not null && locks[info.Index])
                continue;

            switch (info.Kind)
            {
                case ParamKind.Continuous:
                    bank.Set(info.Id, info.FromNormalized(DspHelper.Clamp01(info.ToNormalized(bank.Get(info.Id)) + rng.NextBipolar() * 0.1f * amount)));
                    break;
                case ParamKind.Integer when info.Unit == Units.Semitones && rng.Chance(0.2f * amount):
                    bank.Set(info.Id, bank.Get(info.Id) + (rng.Chance(0.5f) ? 1f : -1f));
                    break;
            }
        }
    }

    /// <summary>
    /// A smooth loop of <paramref name="count"/> normalized values: the sum of three sines at
    /// integer multiples of the loop, random phases and amplitudes, scaled so it stays within
    /// ±<paramref name="spread"/> of <paramref name="centre"/> and clamped to 0..1. Periodic by
    /// construction, so the seam is seamless.
    /// </summary>
    public static float[] RandomLoop(Xoshiro128 rng, int count, float centre, float spread)
    {
        var amplitudes = new float[3];
        var phases = new float[3];
        var total = 0f;
        for (var k = 0; k < 3; k++)
        {
            amplitudes[k] = rng.NextRange(0.5f, 1f) / (k + 1);
            phases[k] = rng.NextFloat() * DspHelper.TwoPi;
            total += amplitudes[k];
        }

        var points = new float[count];
        for (var i = 0; i < count; i++)
        {
            var sum = 0f;
            for (var k = 0; k < 3; k++)
                sum += amplitudes[k] * MathF.Sin(DspHelper.TwoPi * (k + 1) * i / count + phases[k]);

            points[i] = DspHelper.Clamp01(centre + spread * sum / total);
        }

        return points;
    }

    private static void RandomizeOscillators(ParameterBank bank, Xoshiro128 rng, float amount, bool[]? locks)
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
                Set(bank, locks, ParameterTable.Osc(section, slot, OscParam.On), rng.Chance(0.15f * amount) ? 0f : 1f);
                Set(bank, locks, ParameterTable.Osc(section, slot, OscParam.Wave), (int)wave);
                Set(bank, locks, ParameterTable.Osc(section, slot, OscParam.Semitone), semitone);
                Set(bank, locks, ParameterTable.Osc(section, slot, OscParam.Fine), rng.NextBipolar() * (4f + 20f * amount));
                Set(bank, locks, ParameterTable.Osc(section, slot, OscParam.Shape), rng.NextRange(0.2f, 0.8f));
                Set(bank, locks, ParameterTable.Osc(section, slot, OscParam.NoiseCutoff), LogUniform(rng, 200f, 8000f));
                Set(bank, locks, ParameterTable.Osc(section, slot, OscParam.Level), isNoise ? rng.NextRange(0f, 0.25f) : rng.NextRange(0.15f, 0.85f));
                Set(bank, locks, ParameterTable.Osc(section, slot, OscParam.Trim), 1f);
            }

            var filterType = rng.Chance(0.2f * amount) ? (FilterType)(1 + rng.NextInt(2)) : FilterType.LowPass;
            Set(bank, locks, ParameterTable.Section(section, SectionParam.Cutoff), LogUniform(rng, 300f, 12000f));
            Set(bank, locks, ParameterTable.Section(section, SectionParam.Reso), rng.NextRange(0f, 0.6f * amount));
            Set(bank, locks, ParameterTable.Section(section, SectionParam.FilterType), (int)filterType);
            Set(bank, locks, ParameterTable.Section(section, SectionParam.Level), rng.NextRange(0.5f, 0.8f));
        }

        Set(bank, locks, ParamId.MixCrossfade, rng.NextRange(0.2f, 0.8f));
        Set(bank, locks, ParamId.MixSumBalance, rng.NextBipolar() * 0.5f);
        Set(bank, locks, ParamId.MixRingLevel, rng.NextRange(0.5f, 2f));
    }

    private static void RandomizeSampler(ParameterBank bank, Xoshiro128 rng, float amount, bool[]? locks)
    {
        Set(bank, locks, ParamId.SamplerPitch, rng.Chance(amount) ? rng.NextInt(25) - 12 : 0);
        Set(bank, locks, ParamId.SamplerFine, rng.NextBipolar() * 20f * amount);
        Set(bank, locks, ParamId.SamplerDir, Directions[rng.NextInt(Directions.Length)]);
        var start = rng.NextRange(0f, 0.6f * amount);
        Set(bank, locks, ParamId.SamplerLoopStart, start);
        Set(bank, locks, ParamId.SamplerLoopEnd, rng.NextRange(start + 0.2f, 1f));
        Set(bank, locks, ParamId.SamplerLoopCrossfade, LogUniform(rng, 5f, 300f));
        Set(bank, locks, ParamId.SamplerAmp, rng.NextRange(0.2f, 0.8f));
        Set(bank, locks, ParamId.SamplerAmDepth, rng.NextRange(0f, 0.5f * amount));
    }

    private static void RandomizeEffects(ParameterBank bank, Xoshiro128 rng, float amount, bool[]? locks)
    {
        Set(bank, locks, ParamId.MasterFilterCutoff, LogUniform(rng, 300f, 16000f));
        Set(bank, locks, ParamId.MasterFilterMorph, rng.Chance(0.3f * amount) ? rng.NextFloat() : 0f);
        Set(bank, locks, ParamId.MasterFilterReso, rng.NextRange(0f, 0.6f * amount));

        Set(bank, locks, ParamId.DistortionMix, rng.NextRange(0f, 0.6f * amount));
        Set(bank, locks, ParamId.DistortionDrive, rng.NextRange(0.1f, 0.8f));
        if (rng.Chance(0.5f * amount))
            Set(bank, locks, ParamId.DistortionCurve, rng.NextInt(3));

        Set(bank, locks, ParamId.EqLow, rng.NextBipolar() * 6f * amount);
        Set(bank, locks, ParamId.EqHigh, rng.NextBipolar() * 6f * amount);

        Set(bank, locks, ParamId.SpinTime1, LogUniform(rng, 5f, 500f));
        Set(bank, locks, ParamId.SpinTime2, LogUniform(rng, 5f, 500f));
        Set(bank, locks, ParamId.SpinFine, rng.NextBipolar() * amount);
        Set(bank, locks, ParamId.SpinFeedback, rng.NextRange(0f, 0.7f));
        Set(bank, locks, ParamId.SpinLink, rng.Chance(0.3f) ? 1f : 0f);
        Set(bank, locks, ParamId.SpinMix, rng.Chance(0.7f) ? 1f : rng.NextRange(0.3f, 1f));

        Set(bank, locks, ParamId.ResochordChord, rng.NextInt(ChordTable.Count));
        Set(bank, locks, ParamId.ResochordFeedback, rng.NextRange(0.4f, 0.95f));
        Set(bank, locks, ParamId.ResochordMix, rng.NextRange(0f, 0.7f * amount));
        Set(bank, locks, ParamId.ResochordPitch, rng.Chance(0.3f * amount) ? rng.NextInt(25) - 12 : 0);

        Set(bank, locks, ParamId.ReverbRoom, rng.NextInt(ReverbRooms.Count));
        Set(bank, locks, ParamId.ReverbMix, rng.NextRange(0.1f, 0.6f));
        Set(bank, locks, ParamId.ReverbSize, rng.NextRange(0.3f, 1f));
        Set(bank, locks, ParamId.ReverbDamping, rng.NextRange(0.1f, 0.8f));
        Set(bank, locks, ParamId.ReverbOrder, rng.Chance(0.3f) ? 0f : 1f);
    }

    private static void Set(ParameterBank bank, bool[]? locks, ParamId id, float value)
    {
        if (locks is not null && locks[(int)id])
            return;

        bank.Set(id, value);
    }

    private static float LogUniform(Xoshiro128 rng, float min, float max) => min * MathF.Pow(max / min, rng.NextFloat());
}
