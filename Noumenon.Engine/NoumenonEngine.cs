using Noumenon.Dsp.Effects;
using Noumenon.Dsp.Filters;
using Noumenon.Dsp.Mix;
using Noumenon.Dsp.Modulation;
using Noumenon.Dsp.Oscillators;
using Noumenon.Dsp.Output;
using Noumenon.Dsp.Reverb;
using Noumenon.Dsp.Sampler;
using Noumenon.Dsp.Sections;
using Noumenon.Dsp.Shared;
using Noumenon.Engine.Parameters;
using Noumenon.Engine.Samples;

namespace Noumenon.Engine;

/// <summary>
/// The instrument: owns the live <see cref="Parameters"/>, the <see cref="Samples"/> map and the DSP
/// graph, and renders it. Every <see cref="ControlInterval"/> samples a control tick reads the
/// parameter bank and hands each block its new targets; the blocks glide to them per sample. The
/// tick grid runs independently of the host's block boundaries, so a render is bit-identical
/// whatever block size it is cut into. The <c>Process</c> overloads are the audio-thread hot path:
/// no allocation, no locks, no strings.
///
/// Graph: sections A and B → combiner (sum / ring-mod crossfade) → × the AM route → + the sampler
/// (or the live input through Samp↔In) → DC block → Master Filter → Distortion → EQ → Spin →
/// [scope tap] → Resochord and Reverb in the chosen Order → Volume · Width → post section (filter,
/// EQ, two-band limiter, post gain) → the safety limiter. The Order switch crossfades the two
/// routings over ~20 ms (the Resochord's input and the final output blend; the reverb's own input
/// switches instantly because everything it outputs is delayed), so a swap never clicks. With
/// Engine Rate at 2× the whole graph runs at twice the host rate between a pair of half-band
/// resamplers; the rate is read in <see cref="Prepare"/>, so a host re-prepares to change it.
/// The pitch layer and the lanes (Phase 4) come next; until then <see cref="P1"/>/<see cref="P2"/>
/// are plain inputs in semitones and <see cref="Gate"/> is the MIDI gate the hosts will drive.
/// </summary>
public sealed class NoumenonEngine
{
    public const int ControlInterval = 32;
    public const double DefaultSampleRate = 48000.0;

    private const ulong SectionSeedStride = 0x2545F4914F6CDD1DUL;
    private const float AmSmoothSeconds = 0.02f;
    private const float OrderSwitchSeconds = 0.02f;
    private const float DcCutoffHz = 5f;
    private const float SafetyReleaseSeconds = 0.2f;

    private readonly Section[] sections = [new(), new()];
    private readonly Combiner combiner = new();
    private readonly SamplePlayer sampler = new();
    private readonly EnvelopeFollower follower = new();
    private readonly Smoother amDepth = new();

    private readonly DcBlocker chainDcLeft = new();
    private readonly DcBlocker chainDcRight = new();
    private readonly MorphFilter masterFilter = new();
    private readonly Distortion distortion = new();
    private readonly ShelfEq eq = new();
    private readonly Spin spin = new();
    private readonly Resochord resochord = new();
    private readonly ReverbBlock reverb = new();
    private readonly Smoother order = new();
    private readonly OutputStage output = new();
    private readonly PostSection post = new();
    private readonly PeakLimiter safety = new();

    private readonly HalfBandUpsampler upLeft = new();
    private readonly HalfBandUpsampler upRight = new();
    private readonly HalfBandDownsampler downLeft = new();
    private readonly HalfBandDownsampler downRight = new();

    private float[] silence = new float[512];
    private double hostSampleRate = DefaultSampleRate;
    private double sampleRate = DefaultSampleRate;
    private int oversampling = 1;
    private bool prepared;
    private int samplesUntilTick;
    private int gatePending;
    private ulong seed = 1;
    private float previousReverbLeft;
    private float previousReverbRight;

    public NoumenonEngine() => Prepare(DefaultSampleRate, 512);

    public ParameterBank Parameters { get; } = new();

    public SampleMap Samples { get; } = new();

    public ScopeBuffer Scope { get; } = new();

    public double HostSampleRate => hostSampleRate;

    /// <summary>The rate the graph runs at: the host rate times <see cref="Oversampling"/>.</summary>
    public double SampleRate => sampleRate;

    public int Oversampling => oversampling;

    /// <summary>Samples of delay between input and output at the host rate: the distortion's resamplers, plus the engine-rate resamplers at 2×.</summary>
    public int LatencySamples => Distortion.Latency / oversampling + (oversampling == 2 ? HalfBand.RoundTripLatency : 0);

    public ulong Seed => seed;

    public float P1 { get; set; }

    public float P2 { get; set; }

    public float EnvelopeFollowerValue => follower.Value;

    public double SamplePosition => sampler.Position;

    public float ResochordNote(int voice) => resochord.EffectiveNote(voice);

    /// <summary>
    /// A MIDI gate (note-on). Safe from any thread; the next control tick consumes it — the sampler
    /// retriggers when Sample Retrigger is on, and Phase 4 adds lane restart and the gated envelope.
    /// </summary>
    public void Gate() => Interlocked.Exchange(ref gatePending, 1);

    /// <summary>
    /// <paramref name="maxBlockSize"/> pre-sizes the input-silence buffer of the generator overloads
    /// (larger blocks still work, at the cost of one allocation); ends with a <see cref="Reset()"/>.
    /// </summary>
    public void Prepare(double sampleRate, int maxBlockSize)
    {
        hostSampleRate = sampleRate <= 0 ? DefaultSampleRate : sampleRate;
        oversampling = Parameters.GetInt(ParamId.EngineRate) == 1 ? 2 : 1;
        this.sampleRate = hostSampleRate * oversampling;

        sections[0].Prepare(this.sampleRate);
        sections[1].Prepare(this.sampleRate);
        combiner.Prepare(this.sampleRate);
        sampler.Prepare(this.sampleRate);
        follower.Prepare(this.sampleRate);
        amDepth.SetTime(this.sampleRate, AmSmoothSeconds);
        chainDcLeft.SetCutoff(this.sampleRate, DcCutoffHz);
        chainDcRight.SetCutoff(this.sampleRate, DcCutoffHz);
        masterFilter.Prepare(this.sampleRate);
        distortion.Prepare(this.sampleRate);
        eq.Prepare(this.sampleRate);
        spin.Prepare(this.sampleRate);
        resochord.Prepare(this.sampleRate);
        reverb.Prepare(this.sampleRate);
        order.SetTime(this.sampleRate, OrderSwitchSeconds);
        output.Prepare(this.sampleRate);
        post.Prepare(this.sampleRate);
        safety.Prepare(hostSampleRate, SafetyReleaseSeconds);
        EnsureSilence(maxBlockSize);
        prepared = true;
        Reset();
    }

    public void Reset() => Reset(seed);

    /// <summary>Two engines reset with the same parameters, samples and seed render identical samples.</summary>
    public void Reset(ulong seed)
    {
        this.seed = seed;
        ControlTick();
        for (var s = 0; s < sections.Length; s++)
            sections[s].Reset(seed + (ulong)(s + 1) * SectionSeedStride);

        combiner.Reset();
        sampler.Reset();
        follower.Clear();
        amDepth.Snap();
        chainDcLeft.Clear();
        chainDcRight.Clear();
        masterFilter.Reset();
        distortion.Reset();
        eq.Reset();
        spin.Reset();
        resochord.Reset();
        reverb.Reset();
        order.Snap();
        output.Reset();
        post.Reset();
        safety.Clear();
        upLeft.Clear();
        upRight.Clear();
        downLeft.Clear();
        downRight.Clear();
        Scope.Clear();
        previousReverbLeft = 0f;
        previousReverbRight = 0f;
        samplesUntilTick = 0;
    }

    public void Process(float[] left, float[] right, int count) => Process(left, right, 0, count);

    public void Process(float[] left, float[] right, int offset, int count)
    {
        EnsureSilence(count);
        Render(silence, silence, 0, left, right, offset, count);
    }

    /// <summary>Renders with a live input (the FX identity); the input may be the same buffers as the output.</summary>
    public void Process(float[] inputLeft, float[] inputRight, float[] left, float[] right, int count) => Render(inputLeft, inputRight, 0, left, right, 0, count);

    public void Process(float[] inputLeft, float[] inputRight, float[] left, float[] right, int offset, int count) => Render(inputLeft, inputRight, offset, left, right, offset, count);

    private void Render(float[] inputLeft, float[] inputRight, int inputOffset, float[] left, float[] right, int offset, int count)
    {
        var end = offset + count;
        if (!prepared)
        {
            Array.Clear(left, offset, count);
            Array.Clear(right, offset, count);
            return;
        }

        var j = inputOffset;
        for (var i = offset; i < end; i++, j++)
        {
            float l, r;
            if (oversampling == 2)
            {
                upLeft.Process(inputLeft[j], out var inL0, out var inL1);
                upRight.Process(inputRight[j], out var inR0, out var inR1);
                RenderSample(inL0, inR0, out var l0, out var r0);
                RenderSample(inL1, inR1, out var l1, out var r1);
                l = downLeft.Process(l0, l1);
                r = downRight.Process(r0, r1);
            }
            else
            {
                RenderSample(inputLeft[j], inputRight[j], out l, out r);
            }

            safety.Process(ref l, ref r);
            left[i] = l;
            right[i] = r;
        }
    }

    private void RenderSample(float inputLeft, float inputRight, out float left, out float right)
    {
        if (samplesUntilTick <= 0)
        {
            ControlTick();
            samplesUntilTick = ControlInterval;
        }

        samplesUntilTick--;

        var mono = combiner.Process(sections[0].Process(), sections[1].Process());
        sampler.Process(inputLeft, inputRight, out var sampleLeft, out var sampleRight, out var dry);
        var envelope = follower.Process(dry);
        var depth = amDepth.Next();
        var am = 1f - depth + depth * (envelope > 1f ? 1f : envelope);

        var l = chainDcLeft.Process(mono * am + sampleLeft);
        var r = chainDcRight.Process(mono * am + sampleRight);
        masterFilter.Process(ref l, ref r);
        distortion.Process(ref l, ref r);
        eq.Process(ref l, ref r);
        spin.Process(ref l, ref r);
        Scope.Write(l, r);

        // Order: 0 = Post (Resochord then Reverb), 1 = Pre (Reverb then Resochord), crossfaded.
        var p = order.Next();
        var resoL = l + (previousReverbLeft - l) * p;
        var resoR = r + (previousReverbRight - r) * p;
        resochord.Process(ref resoL, ref resoR);
        var revL = resoL + (l - resoL) * p;
        var revR = resoR + (r - resoR) * p;
        reverb.Process(ref revL, ref revR);
        previousReverbLeft = revL;
        previousReverbRight = revR;
        l = revL + (resoL - revL) * p;
        r = revR + (resoR - revR) * p;

        output.Process(ref l, ref r);
        post.Process(ref l, ref r);
        left = l;
        right = r;
    }

    private void ControlTick()
    {
        var bank = Parameters;
        var a4 = bank.Get(ParamId.TuningA4);
        var masterCutoff = bank.Get(ParamId.MasterFilterCutoff);
        var p1 = P1;
        var p2 = P2;
        var gate = Interlocked.Exchange(ref gatePending, 0) != 0;

        for (var s = 0; s < ParameterTable.SectionCount; s++)
        {
            var id = (SectionId)s;
            var section = sections[s];
            var routeAll1 = bank.GetBool(ParameterTable.Section(id, SectionParam.P1RouteAll));
            var routeAll2 = bank.GetBool(ParameterTable.Section(id, SectionParam.P2RouteAll));
            var fine1 = bank.GetBool(ParameterTable.Section(id, SectionParam.P1FineRange));
            var fine2 = bank.GetBool(ParameterTable.Section(id, SectionParam.P2FineRange));

            for (var slot = 0; slot < ParameterTable.SlotsPerSection; slot++)
            {
                var osc = section.Slot(slot);
                var on = bank.GetBool(ParameterTable.Osc(id, slot, OscParam.On));
                section.SetSlotGain(slot, on ? bank.Get(ParameterTable.Osc(id, slot, OscParam.Level)) * bank.Get(ParameterTable.Osc(id, slot, OscParam.Trim)) : 0f);
                if (!on)
                    continue;

                osc.Waveform = (Waveform)bank.GetInt(ParameterTable.Osc(id, slot, OscParam.Wave));
                var route1 = routeAll1 || bank.GetBool(ParameterTable.Osc(id, slot, OscParam.P1Route));
                var route2 = routeAll2 || bank.GetBool(ParameterTable.Osc(id, slot, OscParam.P2Route));
                var semitone = bank.Get(ParameterTable.Osc(id, slot, OscParam.Semitone));
                var fine = bank.Get(ParameterTable.Osc(id, slot, OscParam.Fine));
                osc.SetFrequency(Tuning.OscillatorHz(semitone, fine, p1, p2, route1, route2, fine1, fine2, a4));
                osc.SetShape(bank.Get(ParameterTable.Osc(id, slot, OscParam.Shape)));
                osc.SetNoiseCutoff(bank.Get(ParameterTable.Osc(id, slot, OscParam.NoiseCutoff)));
            }

            var cutoff = bank.GetBool(ParameterTable.Section(id, SectionParam.Slave)) ? masterCutoff : bank.Get(ParameterTable.Section(id, SectionParam.Cutoff));
            section.SetFilter(cutoff, bank.Get(ParameterTable.Section(id, SectionParam.Reso)), (FilterType)bank.GetInt(ParameterTable.Section(id, SectionParam.FilterType)));
            section.SetLevel(bank.Get(ParameterTable.Section(id, SectionParam.Level)));
        }

        combiner.SetCrossfade(bank.Get(ParamId.MixCrossfade));
        combiner.SetBalance(bank.Get(ParamId.MixSumBalance));
        combiner.SetRingLevel(bank.Get(ParamId.MixRingLevel));

        // Sampler: the selected key's slot (a reference the loader publishes), pitch = knob + fine
        // + the chosen P fader + the slot's root-note correction, Dir as signed speed.
        var sample = Samples.Get(bank.GetInt(ParamId.SamplerSelect));
        sampler.SetSample(sample?.Data);
        var sampleModSource = bank.GetInt(ParamId.SamplerModSource);
        var samplePitchMod = sampleModSource == 1 ? p1 : sampleModSource == 2 ? p2 : 0f;
        var rootCorrection = sample is null ? 0f : Tuning.MiddleC - sample.RootNote;
        sampler.SetSpeed(bank.Get(ParamId.SamplerPitch) + bank.Get(ParamId.SamplerFine) / 100f + samplePitchMod + rootCorrection, bank.Get(ParamId.SamplerDir));
        sampler.SetLoop(bank.Get(ParamId.SamplerLoopStart), bank.Get(ParamId.SamplerLoopEnd), bank.Get(ParamId.SamplerLoopCrossfade));
        sampler.SetGain(bank.Get(ParamId.SamplerAmp) * bank.Get(ParamId.SamplerMaster) * (sample?.Gain ?? 1f));
        sampler.SetInputMix(bank.Get(ParamId.SamplerInputMix));
        sampler.SetStereo(bank.GetBool(ParamId.SamplerStereo));
        if (gate && bank.GetBool(ParamId.SamplerRetrigger))
            sampler.Retrigger();

        follower.SetTimes(bank.Get(ParamId.EnvFollowerAttack), bank.Get(ParamId.EnvFollowerRelease));
        amDepth.Target = bank.Get(ParamId.SamplerAmDepth);

        // The effect chain.
        masterFilter.SetEnabled(bank.GetBool(ParamId.MasterFilterOn));
        masterFilter.SetMix(bank.Get(ParamId.MasterFilterMix));
        masterFilter.SetCutoff(masterCutoff);
        masterFilter.SetResonance(bank.Get(ParamId.MasterFilterReso));
        masterFilter.SetMorph(bank.Get(ParamId.MasterFilterMorph));

        distortion.SetEnabled(bank.GetBool(ParamId.DistortionOn));
        distortion.SetMix(bank.Get(ParamId.DistortionMix));
        distortion.SetDrive(bank.Get(ParamId.DistortionDrive));
        distortion.Curve = (DistortionCurve)bank.GetInt(ParamId.DistortionCurve);

        eq.SetLow(bank.Get(ParamId.EqLow));
        eq.SetHigh(bank.Get(ParamId.EqHigh));

        spin.SetEnabled(bank.GetBool(ParamId.SpinOn));
        spin.SetMix(bank.Get(ParamId.SpinMix));
        spin.SetTimes(bank.Get(ParamId.SpinTime1), bank.Get(ParamId.SpinTime2), bank.Get(ParamId.SpinFine), bank.GetBool(ParamId.SpinLink));
        spin.SetFeedback(bank.Get(ParamId.SpinFeedback));

        resochord.SetEnabled(bank.GetBool(ParamId.ResochordOn));
        resochord.SetMix(bank.Get(ParamId.ResochordMix));
        resochord.SetGlobalFeedback(bank.Get(ParamId.ResochordFeedback), bank.Get(ParamId.ResochordFeedbackFader));
        var resoModSource = bank.GetInt(ParamId.ResochordModSource);
        var resoPitchMod = resoModSource == 1 ? p1 : resoModSource == 2 ? p2 : 0f;
        resochord.SetPitchOffset(bank.Get(ParamId.ResochordPitch) + bank.Get(ParamId.ResochordFine) / 100f + resoPitchMod);
        resochord.SetA4(a4);
        var chordSelect = bank.GetInt(ParamId.ResochordChordSelect);
        resochord.SetChord(chordSelect == 0 ? bank.GetInt(ParamId.ResochordChord) : ChordTable.IndexFromPitch(chordSelect == 1 ? p1 : p2));
        for (var v = 0; v < ParameterTable.ResoVoiceCount; v++)
        {
            resochord.SetVoice(
                v,
                bank.Get(ParameterTable.ResoVoice(v, ResoVoiceParam.Note)),
                bank.Get(ParameterTable.ResoVoice(v, ResoVoiceParam.Feedback)),
                bank.Get(ParameterTable.ResoVoice(v, ResoVoiceParam.Damping)),
                bank.Get(ParameterTable.ResoVoice(v, ResoVoiceParam.Level)),
                bank.Get(ParameterTable.ResoVoice(v, ResoVoiceParam.Pan)));
        }

        reverb.SetEnabled(bank.GetBool(ParamId.ReverbOn));
        reverb.SetMix(bank.Get(ParamId.ReverbMix));
        reverb.SetRoom(bank.GetInt(ParamId.ReverbRoom));
        reverb.SetSize(bank.Get(ParamId.ReverbSize));
        reverb.SetDamping(bank.Get(ParamId.ReverbDamping));
        reverb.SetFreeze(bank.GetBool(ParamId.ReverbFreeze));
        order.Target = bank.GetInt(ParamId.ReverbOrder) == 0 ? 1f : 0f;

        output.SetVolumeDb(bank.Get(ParamId.OutputVolume));
        output.SetWidth(bank.Get(ParamId.OutputWidth));
        output.SetMute(bank.GetBool(ParamId.Mute));

        post.Filter.SetEnabled(bank.GetBool(ParamId.PostFilterOn));
        post.Filter.SetMix(bank.Get(ParamId.PostFilterMix));
        post.Filter.SetCutoff(bank.Get(ParamId.PostFilterCutoff));
        post.Filter.SetResonance(bank.Get(ParamId.PostFilterReso));
        post.Filter.SetMorph(bank.Get(ParamId.PostFilterMorph));
        post.Eq.SetLow(bank.Get(ParamId.PostEqLow));
        post.Eq.SetHigh(bank.Get(ParamId.PostEqHigh));
        post.Limiter.SetEnabled(bank.GetBool(ParamId.PostLimiterOn));
        post.Limiter.SetGainDb(bank.Get(ParamId.PostLimiterGain));
        post.Limiter.SetThresholdsDb(bank.Get(ParamId.PostLimiterLowThreshold), bank.Get(ParamId.PostLimiterHighThreshold));
        post.Limiter.SetSplit(bank.Get(ParamId.PostLimiterSplit));
        post.Limiter.SetSpeed((LimiterSpeed)bank.GetInt(ParamId.PostLimiterSpeed));
        post.SetVolumeDb(bank.Get(ParamId.PostVolume));
    }

    private void EnsureSilence(int count)
    {
        if (silence.Length < count)
            silence = new float[count];
    }
}
