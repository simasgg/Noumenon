using Noumenon.Dsp.Filters;
using Noumenon.Dsp.Mix;
using Noumenon.Dsp.Oscillators;
using Noumenon.Dsp.Output;
using Noumenon.Dsp.Sections;
using Noumenon.Dsp.Shared;
using Noumenon.Engine.Parameters;

namespace Noumenon.Engine;

/// <summary>
/// The instrument: owns the live <see cref="Parameters"/> and the DSP graph, and renders it. Every
/// <see cref="ControlInterval"/> samples a control tick reads the parameter bank and hands each
/// block its new targets (pitch from the knobs and the P routing, gains, cutoffs, mix); the blocks
/// glide to them per sample. The tick grid runs independently of the host's block boundaries, so a
/// render is bit-identical whatever block size it is cut into. <see cref="Process(float[],float[],int,int)"/>
/// is the audio-thread hot path: no allocation, no locks, no strings.
///
/// Phase 1 graph: section A and B → combiner (sum / ring-mod crossfade) → output stage. The
/// sampler, the effect chain, the pitch layer and the lanes are added by the following phases; until
/// the pitch layer exists <see cref="P1"/>/<see cref="P2"/> are plain inputs in semitones.
/// </summary>
public sealed class NoumenonEngine
{
    public const int ControlInterval = 32;
    public const double DefaultSampleRate = 48000.0;

    private const ulong SectionSeedStride = 0x2545F4914F6CDD1DUL;

    private readonly Section[] sections = [new(), new()];
    private readonly Combiner combiner = new();
    private readonly OutputStage output = new();

    private double sampleRate = DefaultSampleRate;
    private bool prepared;
    private int samplesUntilTick;
    private ulong seed = 1;

    public NoumenonEngine() => Prepare(DefaultSampleRate, 512);

    public ParameterBank Parameters { get; } = new();

    public double SampleRate => sampleRate;

    /// <summary>The seed the noise generators were last reset from.</summary>
    public ulong Seed => seed;

    /// <summary>P1 pitch input in semitones (the P1 fader after MIDI, bend and glide, once Phase 4 provides them).</summary>
    public float P1 { get; set; }

    /// <summary>P2 pitch input in semitones.</summary>
    public float P2 { get; set; }

    /// <summary>
    /// Sizes everything for a sample rate. <paramref name="maxBlockSize"/> is accepted for the
    /// host adapters' sake; the Phase 1 graph keeps no per-block scratch. Ends with a
    /// <see cref="Reset()"/>, so a prepared engine starts from a clean, deterministic state.
    /// </summary>
    public void Prepare(double sampleRate, int maxBlockSize)
    {
        this.sampleRate = sampleRate <= 0 ? DefaultSampleRate : sampleRate;
        sections[0].Prepare(this.sampleRate);
        sections[1].Prepare(this.sampleRate);
        combiner.Prepare(this.sampleRate);
        output.Prepare(this.sampleRate);
        prepared = true;
        Reset();
    }

    public void Reset() => Reset(seed);

    /// <summary>
    /// Applies the current parameters without gliding, rewinds every oscillator and reseeds the
    /// noise from <paramref name="seed"/>: two engines reset with the same parameters and seed
    /// render the same samples.
    /// </summary>
    public void Reset(ulong seed)
    {
        this.seed = seed;
        ControlTick();
        for (var s = 0; s < sections.Length; s++)
            sections[s].Reset(seed + (ulong)(s + 1) * SectionSeedStride);

        combiner.Reset();
        output.Reset();
        samplesUntilTick = 0;
    }

    public void Process(float[] left, float[] right, int count) => Process(left, right, 0, count);

    /// <summary>Renders <paramref name="count"/> stereo samples into the buffers starting at <paramref name="offset"/> (overwriting them).</summary>
    public void Process(float[] left, float[] right, int offset, int count)
    {
        var end = offset + count;
        if (!prepared)
        {
            Array.Clear(left, offset, count);
            Array.Clear(right, offset, count);
            return;
        }

        var i = offset;
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
                float l = mono, r = mono;
                output.Process(ref l, ref r);
                left[i] = l;
                right[i] = r;
                i++;
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

        output.SetVolumeDb(bank.Get(ParamId.OutputVolume));
        output.SetWidth(bank.Get(ParamId.OutputWidth));
        output.SetMute(bank.GetBool(ParamId.Mute));
    }
}
