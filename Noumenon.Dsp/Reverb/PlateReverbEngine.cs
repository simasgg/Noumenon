using System.Runtime.CompilerServices;
using Noumenon.Dsp.Shared;

namespace Noumenon.Dsp.Reverb;

/// <summary>
/// A stereo plate reverb after Jon Dattorro's "Effect Design, Part 1" (1997), ported from
/// EmptySpace: a mono, pre-delayed, bandwidth-limited input runs through four series diffusers
/// into a figure-8 "tank" — two cross-fed halves, each a modulated all-pass → delay → HF-damping
/// low-pass → all-pass → delay. Stereo L/R are read from a fixed set of taps inside the tank. The
/// modulated tank all-passes give the tail its slow movement; <see cref="ModDepth"/>/
/// <see cref="ModRate"/> set the LFO's excursion and speed, and <see cref="ModDrift"/> switches the
/// LFO shape from near-quadrature sines to a seeded random wander (both always advance and a
/// glided blend crossfades them, so a shape flip never steps the modulated tap).
///
/// Same parameter surface as <see cref="FreeverbEngine"/> (it implements <see cref="IReverbEngine"/>).
/// Allocation-free in <see cref="ProcessSample"/>; every buffer is sized in <see cref="Prepare"/>.
/// Delay tunings are Dattorro's (specified at 29761 Hz) scaled to the real sample rate.
/// </summary>
public sealed class PlateReverbEngine : IReverbEngine
{
    private const double RefRate = 29761.0;   // Dattorro's reference sample rate

    // Delay-line tunings (samples @ RefRate).
    private static readonly int[] InputDiffusionLen = [142, 107, 379, 277];
    private static readonly float[] InputDiffusionGain = [0.75f, 0.75f, 0.625f, 0.625f];
    private const int TankApf1L = 672, TankDelay1L = 4453, TankApf2L = 1800, TankDelay2L = 3720;
    private const int TankApf1R = 908, TankDelay1R = 4217, TankApf2R = 2656, TankDelay2R = 3163;
    private const float DecayDiffusion1 = 0.70f;
    private const float DecayDiffusion2 = 0.50f;
    private const float MaxInputDiffusionGain = 0.9f;

    // Stereo output taps (samples @ RefRate). Lines: 0-2 = right delay1/apf2/delay2, 3-5 = left.
    private static readonly int[] OutputTaps =
    [
        266, 2974, 1913, 1996, 1990, 187, 1066,   // left  out: +rd1 +rd1 -ra2 +rd2 -ld1 -la2 -ld2
        353, 3627, 1228, 2673, 2111, 335, 121     // right out: +ld1 +ld1 -la2 +ld2 -rd1 -ra2 -rd2
    ];

    private const float DecayMin = 0.40f, DecayRange = 0.54f;
    private const float TankDampAmount = 0.90f;
    private const float BandwidthDampAmount = 0.35f;
    private const float BassCrossoverHz = 500f;
    private const float BassStrength = 2f;
    private const float InputScale = 1.0f;
    private const float WetTrim = 0.6f;
    private const float MaxPreDelayMs = 250f;
    private const float SmoothTimeSeconds = 0.02f;

    // Distinct L/R xorshift32 seeds decorrelate the DRIFT wander per channel — the drift twin of the
    // sine pair's 6.2800/6.2847 detune. Reseeded in Reset so renders are deterministic.
    private const uint DriftSeedL = 0x9E3779B9u;
    private const uint DriftSeedR = 0x7F4A7C15u;

    private readonly PreDelayLine preDelay = new();
    private readonly OnePoleLowpass bandwidth = new();
    private readonly PlateAllpass[] inputDiffusers = new PlateAllpass[4];

    private readonly ModulatedAllpass apf1L = new();
    private readonly ModulatedAllpass apf1R = new();
    private readonly PlateDelay delay1L = new(), delay2L = new(), delay1R = new(), delay2R = new();
    private readonly PlateAllpass apf2L = new(), apf2R = new();
    private readonly OnePoleLowpass dampL = new(), dampR = new();
    private readonly LowShelf bassL = new(), bassR = new();
    private readonly OnePoleLowpass eqLpL = new(), eqLpR = new();
    private readonly OnePoleHighpass eqHpL = new(), eqHpR = new();

    private readonly int[] tap = new int[14];

    private double sampleRate = 44100.0;
    private bool prepared;
    private double excPhase;
    private double excInc;
    private float excDepth;
    private float excDepthTarget;

    private double driftPhase;
    private uint driftRandL = DriftSeedL, driftRandR = DriftSeedR;
    private float driftPrevL, driftTargetL, driftPrevR, driftTargetR;
    private float driftBlend, driftBlendTarget;

    private float size = 0.5f, damping = 0.5f, bass = 0.5f, width = 1f, mix = 0.3f, preDelayMs;
    private float modDepth = 0.2f, modRate = 0.5f;
    private bool modDrift;
    private float highCut = 1f, lowCut;
    private float diffusion = ReverbMaps.LegacyDiffusion;
    private bool freeze;

    private float decay, inputGain;

    private float wet1, wet2, dryGain, wetGain;
    private float wet1Target, wet2Target, dryGainTarget, wetGainTarget;
    private float diffScale = 1f, diffScaleTarget = 1f;
    private float smoothCoeff = 0.001f;

    public PlateReverbEngine()
    {
        for (var i = 0; i < inputDiffusers.Length; i++)
            inputDiffusers[i] = new PlateAllpass();

        Prepare(44100.0, 512);
    }

    public float Size
    {
        get => size;
        set
        {
            size = DspHelper.Clamp01(value);
            UpdateDerived();
        }
    }

    public float Damping
    {
        get => damping;
        set
        {
            damping = DspHelper.Clamp01(value);
            UpdateDerived();
        }
    }

    public float Bass
    {
        get => bass;
        set
        {
            bass = DspHelper.Clamp01(value);
            UpdateDerived();
        }
    }

    public float Width
    {
        get => width;
        set
        {
            width = DspHelper.Clamp01(value);
            UpdateDerived();
        }
    }

    public float Mix
    {
        get => mix;
        set
        {
            mix = DspHelper.Clamp01(value);
            UpdateDerived();
        }
    }

    public float PreDelayMs
    {
        get => preDelayMs;
        set
        {
            preDelayMs = value < 0f ? 0f : value > MaxPreDelayMs ? MaxPreDelayMs : value;
            UpdateDerived();
        }
    }

    public bool Freeze
    {
        get => freeze;
        set
        {
            freeze = value;
            UpdateDerived();
        }
    }

    public float ModDepth
    {
        get => modDepth;
        set
        {
            modDepth = DspHelper.Clamp01(value);
            UpdateDerived();
        }
    }

    public float ModRate
    {
        get => modRate;
        set
        {
            modRate = DspHelper.Clamp01(value);
            UpdateDerived();
        }
    }

    public bool ModDrift
    {
        get => modDrift;
        set
        {
            modDrift = value;
            UpdateDerived();
        }
    }

    public float HighCut
    {
        get => highCut;
        set
        {
            highCut = DspHelper.Clamp01(value);
            UpdateDerived();
        }
    }

    public float LowCut
    {
        get => lowCut;
        set
        {
            lowCut = DspHelper.Clamp01(value);
            UpdateDerived();
        }
    }

    public float Diffusion
    {
        get => diffusion;
        set
        {
            diffusion = DspHelper.Clamp01(value);
            UpdateDerived();
        }
    }

    public void Prepare(double sampleRate, int maxBlockSize)
    {
        this.sampleRate = sampleRate <= 0 ? 44100.0 : sampleRate;

        var maxExcursionSamples = ReverbMaps.MaxModDepthMs * (float)this.sampleRate / 1000f;
        var excursion = (int)(2f * maxExcursionSamples) + 2;

        for (var i = 0; i < inputDiffusers.Length; i++)
        {
            var len = Scale(InputDiffusionLen[i]);
            inputDiffusers[i].SetSize(len, len);
        }

        apf1L.SetSize(Scale(TankApf1L), excursion);
        apf1R.SetSize(Scale(TankApf1R), excursion);
        apf1L.Gain = DecayDiffusion1;
        apf1R.Gain = DecayDiffusion1;
        apf2L.SetSize(Scale(TankApf2L), Scale(TankApf2L));
        apf2R.SetSize(Scale(TankApf2R), Scale(TankApf2R));
        apf2L.Gain = DecayDiffusion2;
        apf2R.Gain = DecayDiffusion2;

        delay1L.SetSize(Scale(TankDelay1L), Scale(TankDelay1L));
        delay2L.SetSize(Scale(TankDelay2L), Scale(TankDelay2L));
        delay1R.SetSize(Scale(TankDelay1R), Scale(TankDelay1R));
        delay2R.SetSize(Scale(TankDelay2R), Scale(TankDelay2R));

        for (var i = 0; i < tap.Length; i++)
            tap[i] = Scale(OutputTaps[i]);

        preDelay.SetSampleRate(this.sampleRate, MaxPreDelayMs, SmoothTimeSeconds);
        smoothCoeff = 1f - MathF.Exp(-1f / (SmoothTimeSeconds * (float)this.sampleRate));

        var bassCoeff = 1f - MathF.Exp(-2f * MathF.PI * BassCrossoverHz / (float)this.sampleRate);
        bassL.Cutoff = bassCoeff;
        bassR.Cutoff = bassCoeff;

        prepared = true;
        UpdateDerived();
        Reset();
    }

    public void Reset()
    {
        preDelay.Clear();
        bandwidth.Clear();
        for (var i = 0; i < inputDiffusers.Length; i++)
            inputDiffusers[i].Clear();

        apf1L.Clear();
        apf1R.Clear();
        apf2L.Clear();
        apf2R.Clear();
        delay1L.Clear();
        delay2L.Clear();
        delay1R.Clear();
        delay2R.Clear();
        dampL.Clear();
        dampR.Clear();
        bassL.Clear();
        bassR.Clear();
        eqLpL.Clear();
        eqLpR.Clear();
        eqHpL.Clear();
        eqHpR.Clear();
        excPhase = 0.0;

        driftPhase = 0.0;
        driftRandL = DriftSeedL;
        driftRandR = DriftSeedR;
        driftPrevL = NextDriftTarget(ref driftRandL);
        driftTargetL = NextDriftTarget(ref driftRandL);
        driftPrevR = NextDriftTarget(ref driftRandR);
        driftTargetR = NextDriftTarget(ref driftRandR);

        wet1 = wet1Target;
        wet2 = wet2Target;
        dryGain = dryGainTarget;
        wetGain = wetGainTarget;
        excDepth = excDepthTarget;
        diffScale = diffScaleTarget;
        driftBlend = driftBlendTarget;
    }

    public void Process(float[] left, float[] right, int count) => Process(left, right, 0, count);

    public void Process(float[] left, float[] right, int offset, int count)
    {
        if (!prepared)
            return;

        var end = offset + count;
        for (var i = offset; i < end; i++)
            ProcessSample(left[i], right[i], out left[i], out right[i]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ProcessSample(float inL, float inR, out float outLeft, out float outRight)
    {
        dryGain += (dryGainTarget - dryGain) * smoothCoeff;
        wetGain += (wetGainTarget - wetGain) * smoothCoeff;
        wet1 += (wet1Target - wet1) * smoothCoeff;
        wet2 += (wet2Target - wet2) * smoothCoeff;
        excDepth += (excDepthTarget - excDepth) * smoothCoeff;
        driftBlend += (driftBlendTarget - driftBlend) * smoothCoeff;
        diffScale += (diffScaleTarget - diffScale) * smoothCoeff;

        for (var d = 0; d < inputDiffusers.Length; d++)
        {
            var g = InputDiffusionGain[d] * diffScale;
            inputDiffusers[d].Gain = g > MaxInputDiffusionGain ? MaxInputDiffusionGain : g;
        }

        var pre = bandwidth.Process(preDelay.Process((inL + inR) * inputGain));
        var split = inputDiffusers[3].Process(inputDiffusers[2].Process(inputDiffusers[1].Process(inputDiffusers[0].Process(pre))));

        var sineL = 1f + (float)Math.Cos(excPhase * 6.2800);
        var sineR = 1f + (float)Math.Sin(excPhase * 6.2847);
        excPhase += excInc;
        driftPhase += excInc;

        if (driftPhase >= 1.0)
        {
            driftPhase -= 1.0;
            driftPrevL = driftTargetL;
            driftTargetL = NextDriftTarget(ref driftRandL);
            driftPrevR = driftTargetR;
            driftTargetR = NextDriftTarget(ref driftRandR);
        }

        var driftT = (float)driftPhase;

        var driftL = driftPrevL + (driftTargetL - driftPrevL) * driftT;
        var driftR = driftPrevR + (driftTargetR - driftPrevR) * driftT;
        var excL = excDepth * (sineL + driftBlend * (driftL - sineL));
        var excR = excDepth * (sineR + driftBlend * (driftR - sineR));

        var aL = apf1L.Process(split + decay * delay2R.Tap(delay2R.Length), excL);
        var lpL = bassL.Process(dampL.Process(delay1L.Process(aL)));
        var a2L = apf2L.Process(decay * lpL);
        delay2L.Process(a2L);

        var aR = apf1R.Process(split + decay * delay2L.Tap(delay2L.Length), excR);
        var lpR = bassR.Process(dampR.Process(delay1R.Process(aR)));
        var a2R = apf2R.Process(decay * lpR);
        delay2R.Process(a2R);

        var lo = delay1R.Tap(tap[0]) + delay1R.Tap(tap[1]) - apf2R.Tap(tap[2]) + delay2R.Tap(tap[3]) - delay1L.Tap(tap[4]) - apf2L.Tap(tap[5]) - delay2L.Tap(tap[6]);
        var ro = delay1L.Tap(tap[7]) + delay1L.Tap(tap[8]) - apf2L.Tap(tap[9]) + delay2L.Tap(tap[10]) - delay1R.Tap(tap[11]) - apf2R.Tap(tap[12]) - delay2R.Tap(tap[13]);

        var wetLraw = lo * WetTrim;
        var wetRraw = ro * WetTrim;

        var wetL = eqHpL.Process(eqLpL.Process(wetLraw * wet1 + wetRraw * wet2));
        var wetR = eqHpR.Process(eqLpR.Process(wetRraw * wet1 + wetLraw * wet2));
        outLeft = wetL * wetGain + inL * dryGain;
        outRight = wetR * wetGain + inR * dryGain;
    }

    private void UpdateDerived()
    {
        decay = freeze ? 1f : DecayMin + size * DecayRange;
        inputGain = freeze ? 0f : InputScale;

        var tankOpen = freeze ? 1f : 1f - damping * TankDampAmount;
        dampL.Coeff = tankOpen;
        dampR.Coeff = tankOpen;
        bandwidth.Coeff = 1f - damping * BandwidthDampAmount;

        var bassGain = freeze ? 1f : MathF.Pow(decay, BassStrength * (1f / ReverbMaps.BassMultiplier(bass) - 1f));
        bassL.Gain = bassGain;
        bassR.Gain = bassGain;

        var highCutCoeff = highCut >= 1f ? 1f : 1f - MathF.Exp(-2f * MathF.PI * ReverbMaps.HighCutHz(highCut) / (float)sampleRate);
        eqLpL.Coeff = highCutCoeff;
        eqLpR.Coeff = highCutCoeff;

        var lowCutCoeff = lowCut <= 0f ? 0f : 1f - MathF.Exp(-2f * MathF.PI * ReverbMaps.LowCutHz(lowCut) / (float)sampleRate);
        eqHpL.Coeff = lowCutCoeff;
        eqHpR.Coeff = lowCutCoeff;

        excDepthTarget = ReverbMaps.ModDepthMs(modDepth) * (float)sampleRate / 1000f;
        excInc = ReverbMaps.ModRateHz(modRate) / sampleRate;
        driftBlendTarget = modDrift ? 1f : 0f;
        diffScaleTarget = ReverbMaps.DiffusionScale(diffusion);

        wet1Target = width * 0.5f + 0.5f;
        wet2Target = (1f - width) * 0.5f;

        var angle = mix * (MathF.PI * 0.5f);
        dryGainTarget = MathF.Cos(angle);
        wetGainTarget = MathF.Sin(angle);

        preDelay.SetDelaySamples((float)(preDelayMs * sampleRate / 1000.0));
    }

    private int Scale(int tuningAtRef)
    {
        var n = (int)MathF.Round((float)(tuningAtRef * sampleRate / RefRate));

        return n < 1 ? 1 : n;
    }

    private static float NextDriftTarget(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;

        return (state >> 8) * (2f / 16777216f);
    }
}
