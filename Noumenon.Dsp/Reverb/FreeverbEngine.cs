using System.Runtime.CompilerServices;
using Noumenon.Dsp.Shared;

namespace Noumenon.Dsp.Reverb;

/// <summary>
/// A stereo algorithmic reverb (Freeverb / Schroeder-Moorer), ported from EmptySpace: 8 parallel
/// lowpass-feedback comb filters per channel feeding 4 series all-pass diffusers, driven by a
/// mono-summed, optionally pre-delayed input. Allocation-free in <see cref="ProcessSample"/> —
/// every buffer is allocated in <see cref="Prepare"/>.
///
/// The output mix gains (dry, wet, and the width cross-mix) are click-prone, so they glide to their
/// targets over ~20 ms per sample. Feedback and damping are applied immediately: changing them
/// alters the tail going forward without a discontinuity in the current output.
/// </summary>
public sealed class FreeverbEngine : IReverbEngine
{
    private const int NumCombs = 8;
    private const int NumAllPasses = 4;

    // Freeverb delay-line tunings in samples at 44.1 kHz; scaled to the real sample rate in Prepare.
    private static readonly int[] CombTuning = [1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617];
    private static readonly int[] AllPassTuning = [556, 441, 341, 225];
    private const int StereoSpread = 23;     // right-channel delays are offset by this many samples
    private const float FixedGain = 0.015f;  // input attenuation into the tank
    private const float ScaleRoom = 0.28f;
    private const float OffsetRoom = 0.7f;   // Size 0..1 -> comb feedback 0.70..0.98
    private const float ScaleDamp = 0.4f;
    private const float AllpassFeedback = 0.5f;
    private const float MaxAllpassFeedback = 0.6f;
    private const float BassCrossoverHz = 500f;   // low/mid split for the Bass control
    private const float MaxPreDelayMs = 250f;
    private const float SmoothTimeSeconds = 0.02f;   // parameter glide time constant (~20 ms)

    private readonly CombFilter[] combL = new CombFilter[NumCombs];
    private readonly CombFilter[] combR = new CombFilter[NumCombs];
    private readonly AllpassFilter[] apL = new AllpassFilter[NumAllPasses];
    private readonly AllpassFilter[] apR = new AllpassFilter[NumAllPasses];

    private readonly PreDelayLine preDelay = new();
    private readonly OnePoleLowpass eqLpL = new(), eqLpR = new();
    private readonly OnePoleHighpass eqHpL = new(), eqHpR = new();

    private double sampleRate = 44100.0;
    private bool prepared;

    private float size = 0.5f;
    private float damping = 0.5f;
    private float bass = 0.5f;
    private float width = 1f;
    private float mix = 0.3f;
    private float preDelayMs;
    private float highCut = 1f;
    private float lowCut;
    private float diffusion = ReverbMaps.LegacyDiffusion;
    private bool freeze;

    private float feedback, damp1, damp2, inputGain;

    private float wet1, wet2, dryGain, wetGain;
    private float wet1Target, wet2Target, dryGainTarget, wetGainTarget;
    private float diffScale = 1f, diffScaleTarget = 1f;
    private float smoothCoeff = 0.001f;

    public FreeverbEngine()
    {
        for (var c = 0; c < NumCombs; c++)
        {
            combL[c] = new CombFilter();
            combR[c] = new CombFilter();
        }

        for (var a = 0; a < NumAllPasses; a++)
        {
            apL[a] = new AllpassFilter();
            apR[a] = new AllpassFilter();
        }

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

    public float ModDepth { get; set; } = 0.2f;

    public float ModRate { get; set; } = 0.5f;

    public bool ModDrift { get; set; }

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

        var bassCoeff = 1f - MathF.Exp(-2f * MathF.PI * BassCrossoverHz / (float)this.sampleRate);
        for (var c = 0; c < NumCombs; c++)
        {
            var sizeL = ScaleTuning(CombTuning[c], this.sampleRate);
            combL[c].SetSize(sizeL);
            combR[c].SetSize(sizeL + StereoSpread);
            combL[c].BassCutoff = bassCoeff;
            combR[c].BassCutoff = bassCoeff;
        }

        for (var a = 0; a < NumAllPasses; a++)
        {
            var sizeL = ScaleTuning(AllPassTuning[a], this.sampleRate);
            apL[a].SetSize(sizeL);
            apR[a].SetSize(sizeL + StereoSpread);
        }

        preDelay.SetSampleRate(this.sampleRate, MaxPreDelayMs, SmoothTimeSeconds);

        smoothCoeff = 1f - MathF.Exp(-1f / (SmoothTimeSeconds * (float)this.sampleRate));

        prepared = true;
        UpdateDerived();
        Reset();
    }

    public void Reset()
    {
        for (var c = 0; c < NumCombs; c++)
        {
            combL[c].Clear();
            combR[c].Clear();
        }

        for (var a = 0; a < NumAllPasses; a++)
        {
            apL[a].Clear();
            apR[a].Clear();
        }

        preDelay.Clear();
        eqLpL.Clear();
        eqLpR.Clear();
        eqHpL.Clear();
        eqHpR.Clear();

        wet1 = wet1Target;
        wet2 = wet2Target;
        dryGain = dryGainTarget;
        wetGain = wetGainTarget;
        diffScale = diffScaleTarget;
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
        diffScale += (diffScaleTarget - diffScale) * smoothCoeff;

        var apFb = AllpassFeedback * diffScale;
        if (apFb > MaxAllpassFeedback)
            apFb = MaxAllpassFeedback;

        var mono = (inL + inR) * inputGain;
        var tankIn = preDelay.Process(mono);

        float outL = 0f, outR = 0f;
        for (var c = 0; c < NumCombs; c++)
        {
            outL += combL[c].Process(tankIn);
            outR += combR[c].Process(tankIn);
        }

        for (var a = 0; a < NumAllPasses; a++)
        {
            apL[a].Feedback = apFb;
            apR[a].Feedback = apFb;
            outL = apL[a].Process(outL);
            outR = apR[a].Process(outR);
        }

        var wetL = eqHpL.Process(eqLpL.Process(outL * wet1 + outR * wet2));
        var wetR = eqHpR.Process(eqLpR.Process(outR * wet1 + outL * wet2));
        outLeft = wetL * wetGain + inL * dryGain;
        outRight = wetR * wetGain + inR * dryGain;
    }

    private void UpdateDerived()
    {
        feedback = freeze ? 1f : size * ScaleRoom + OffsetRoom;
        damp1 = freeze ? 0f : damping * ScaleDamp;
        damp2 = 1f - damp1;
        inputGain = freeze ? 0f : FixedGain;

        var bassGain = freeze ? 1f : MathF.Pow(feedback, 1f / ReverbMaps.BassMultiplier(bass) - 1f);

        for (var c = 0; c < NumCombs; c++)
        {
            combL[c].Feedback = feedback;
            combL[c].Damp1 = damp1;
            combL[c].Damp2 = damp2;
            combL[c].BassGain = bassGain;
            combR[c].Feedback = feedback;
            combR[c].Damp1 = damp1;
            combR[c].Damp2 = damp2;
            combR[c].BassGain = bassGain;
        }

        var highCutCoeff = highCut >= 1f ? 1f : 1f - MathF.Exp(-2f * MathF.PI * ReverbMaps.HighCutHz(highCut) / (float)sampleRate);
        eqLpL.Coeff = highCutCoeff;
        eqLpR.Coeff = highCutCoeff;

        var lowCutCoeff = lowCut <= 0f ? 0f : 1f - MathF.Exp(-2f * MathF.PI * ReverbMaps.LowCutHz(lowCut) / (float)sampleRate);
        eqHpL.Coeff = lowCutCoeff;
        eqHpR.Coeff = lowCutCoeff;

        diffScaleTarget = ReverbMaps.DiffusionScale(diffusion);

        wet1Target = width * 0.5f + 0.5f;
        wet2Target = (1f - width) * 0.5f;

        var angle = mix * (MathF.PI * 0.5f);
        dryGainTarget = MathF.Cos(angle);
        wetGainTarget = MathF.Sin(angle);

        preDelay.SetDelaySamples((float)(preDelayMs * sampleRate / 1000.0));
    }

    private static int ScaleTuning(int tuningAt44100, double sampleRate)
    {
        var n = (int)MathF.Round((float)(tuningAt44100 * sampleRate / 44100.0));

        return n < 1 ? 1 : n;
    }
}
