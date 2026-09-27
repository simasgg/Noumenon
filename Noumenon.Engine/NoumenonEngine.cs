using Noumenon.Dsp.Filters;
using Noumenon.Dsp.Mix;
using Noumenon.Dsp.Modulation;
using Noumenon.Dsp.Oscillators;
using Noumenon.Dsp.Output;
using Noumenon.Dsp.Sampler;
using Noumenon.Dsp.Sections;
using Noumenon.Dsp.Shared;
using Noumenon.Engine.Parameters;
using Noumenon.Engine.Samples;

namespace Noumenon.Engine;

/// <summary>
/// The instrument: owns the live <see cref="Parameters"/>, the <see cref="Samples"/> map and the DSP
/// graph, and renders it. Every <see cref="ControlInterval"/> samples a control tick reads the
/// parameter bank and hands each block its new targets (pitch from the knobs and the P routing,
/// gains, cutoffs, mix, the sampler's speed and loop); the blocks glide to them per sample. The tick
/// grid runs independently of the host's block boundaries, so a render is bit-identical whatever
/// block size it is cut into. The <c>Process</c> overloads are the audio-thread hot path: no
/// allocation, no locks, no strings.
///
/// Graph so far: section A and B → combiner (sum / ring-mod crossfade) → × the AM route → + the
/// sampler (or the live input through Samp↔In) → output stage. The effect chain (Phase 3), the pitch
/// layer and the lanes (Phase 4) come next; until then <see cref="P1"/>/<see cref="P2"/> are plain
/// inputs in semitones and <see cref="Gate"/> is the MIDI gate the hosts will drive.
/// </summary>
public sealed class NoumenonEngine
{
    public const int ControlInterval = 32;
    public const double DefaultSampleRate = 48000.0;

    private const ulong SectionSeedStride = 0x2545F4914F6CDD1DUL;
    private const float AmSmoothSeconds = 0.02f;

    private readonly Section[] sections = [new(), new()];
    private readonly Combiner combiner = new();
    private readonly SamplePlayer sampler = new();
    private readonly EnvelopeFollower follower = new();
    private readonly Smoother amDepth = new();
    private readonly OutputStage output = new();

    private float[] silence = new float[512];
    private double sampleRate = DefaultSampleRate;
    private bool prepared;
    private int samplesUntilTick;
    private int gatePending;
    private ulong seed = 1;

    public NoumenonEngine() => Prepare(DefaultSampleRate, 512);

    public ParameterBank Parameters { get; } = new();

    public SampleMap Samples { get; } = new();

    public double SampleRate => sampleRate;

    /// <summary>The seed the noise generators were last reset from.</summary>
    public ulong Seed => seed;

    /// <summary>P1 pitch input in semitones (the P1 fader after MIDI, bend and glide, once Phase 4 provides them).</summary>
    public float P1 { get; set; }

    /// <summary>P2 pitch input in semitones.</summary>
    public float P2 { get; set; }

    /// <summary>The envelope follower's current level (the sampler / input signal), a modulation source.</summary>
    public float EnvelopeFollowerValue => follower.Value;

    /// <summary>The sampler's playhead in frames of the selected sample, for the waveform display.</summary>
    public double SamplePosition => sampler.Position;

    /// <summary>
    /// A MIDI gate (note-on). Safe from any thread; the next control tick consumes it — the sampler
    /// retriggers when Sample Retrigger is on, and Phase 4 adds lane restart and the gated envelope.
    /// </summary>
    public void Gate() => Interlocked.Exchange(ref gatePending, 1);

    /// <summary>
    /// Sizes everything for a sample rate and the largest block a host will send (the input-silence
    /// buffer for the generator overloads; larger blocks still work, at the cost of one allocation).
    /// Ends with a <see cref="Reset()"/>, so a prepared engine starts from a clean, deterministic state.
    /// </summary>
    public void Prepare(double sampleRate, int maxBlockSize)
    {
        this.sampleRate = sampleRate <= 0 ? DefaultSampleRate : sampleRate;
        sections[0].Prepare(this.sampleRate);
        sections[1].Prepare(this.sampleRate);
        combiner.Prepare(this.sampleRate);
        sampler.Prepare(this.sampleRate);
        follower.Prepare(this.sampleRate);
        amDepth.SetTime(this.sampleRate, AmSmoothSeconds);
        output.Prepare(this.sampleRate);
        EnsureSilence(maxBlockSize);
        prepared = true;
        Reset();
    }

    public void Reset() => Reset(seed);

    /// <summary>
    /// Applies the current parameters without gliding, rewinds every oscillator and the sampler and
    /// reseeds the noise from <paramref name="seed"/>: two engines reset with the same parameters,
    /// samples and seed render the same samples.
    /// </summary>
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
        output.Reset();
        samplesUntilTick = 0;
    }

    /// <summary>Renders <paramref name="count"/> stereo frames with no live input (the instrument identity).</summary>
    public void Process(float[] left, float[] right, int count) => Process(left, right, 0, count);

    /// <summary>Renders into the buffers starting at <paramref name="offset"/> (overwriting them), with no live input.</summary>
    public void Process(float[] left, float[] right, int offset, int count)
    {
        EnsureSilence(count);
        Render(silence, silence, 0, left, right, offset, count);
    }

    /// <summary>Renders with a live input (the FX identity); the input may be the same buffers as the output.</summary>
    public void Process(float[] inputLeft, float[] inputRight, float[] left, float[] right, int count) => Render(inputLeft, inputRight, 0, left, right, 0, count);

    /// <summary>Renders with a live input, reading and writing from <paramref name="offset"/>.</summary>
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

        var i = offset;
        var j = inputOffset;
        while (i < end)
        {
            if (samplesUntilTick <= 0)
            {
                ControlTick();
                samplesUntilTick = ControlInterval;
            }

            var run = Math.Min(samplesUntilTick, end - i);
            for (var n = 0; n < run; n++)
            {
                var mono = combiner.Process(sections[0].Process(), sections[1].Process());
                sampler.Process(inputLeft[j], inputRight[j], out var sampleLeft, out var sampleRight, out var dry);
                var envelope = follower.Process(dry);
                var depth = amDepth.Next();
                var am = 1f - depth + depth * (envelope > 1f ? 1f : envelope);
                var l = mono * am + sampleLeft;
                var r = mono * am + sampleRight;
                output.Process(ref l, ref r);
                left[i] = l;
                right[i] = r;
                i++;
                j++;
            }

            samplesUntilTick -= run;
        }
    }

    /// <summary>Reads the parameter bank and sets every block's targets. Runs on the audio thread; allocation-free.</summary>
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
        var modSource = bank.GetInt(ParamId.SamplerModSource);
        var pitchMod = modSource == 1 ? p1 : modSource == 2 ? p2 : 0f;
        var rootCorrection = sample is null ? 0f : Tuning.MiddleC - sample.RootNote;
        sampler.SetSpeed(bank.Get(ParamId.SamplerPitch) + bank.Get(ParamId.SamplerFine) / 100f + pitchMod + rootCorrection, bank.Get(ParamId.SamplerDir));
        sampler.SetLoop(bank.Get(ParamId.SamplerLoopStart), bank.Get(ParamId.SamplerLoopEnd), bank.Get(ParamId.SamplerLoopCrossfade));
        sampler.SetGain(bank.Get(ParamId.SamplerAmp) * bank.Get(ParamId.SamplerMaster) * (sample?.Gain ?? 1f));
        sampler.SetInputMix(bank.Get(ParamId.SamplerInputMix));
        sampler.SetStereo(bank.GetBool(ParamId.SamplerStereo));
        if (gate && bank.GetBool(ParamId.SamplerRetrigger))
            sampler.Retrigger();

        follower.SetTimes(bank.Get(ParamId.EnvFollowerAttack), bank.Get(ParamId.EnvFollowerRelease));
        amDepth.Target = bank.Get(ParamId.SamplerAmDepth);

        output.SetVolumeDb(bank.Get(ParamId.OutputVolume));
        output.SetWidth(bank.Get(ParamId.OutputWidth));
        output.SetMute(bank.GetBool(ParamId.Mute));
    }

    private void EnsureSilence(int count)
    {
        if (silence.Length < count)
            silence = new float[count];
    }
}
