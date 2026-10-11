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
using Noumenon.Engine.Lanes;
using Noumenon.Engine.Modulation;
using Noumenon.Engine.Parameters;
using Noumenon.Engine.Presets;
using Noumenon.Engine.Samples;
using Noumenon.Engine.Scales;

namespace Noumenon.Engine;

/// <summary>
/// The instrument: owns the live <see cref="Parameters"/>, the <see cref="Samples"/> map, the
/// <see cref="Lanes"/>, the <see cref="Clock"/>, the <see cref="Midi"/> input and the DSP graph,
/// and renders it. Every <see cref="ControlInterval"/> samples a control tick builds the
/// <see cref="Resolved"/> values — the bank's static values, overridden by the mod-wheel box and
/// the playing lanes, plus the modulation matrix — computes the P1/P2 pitch faders (lane or fader,
/// MIDI follow with bend, glide) and hands each block its targets; the blocks glide per sample.
/// The tick grid ignores the host's block boundaries, so a render is bit-identical whatever block
/// size it is cut into, and every random element (noise, LFOs, the randomizer) is seeded, so the
/// same preset and seed render the same samples. The <c>Process</c> overloads are the audio-thread
/// hot path: no allocation, no locks, no strings. Presets, morphing, sample loading and the
/// randomizer run on other threads and reach the tick through the bank, lane snapshots and
/// volatile references.
///
/// Graph: sections A and B → combiner (sum / ring-mod crossfade) → × the AM route → + the sampler
/// (or the live input through Samp↔In) → × the Play Mode gate envelope → DC block → Master Filter
/// → Distortion → EQ → Spin → [scope tap] → Resochord and Reverb in the chosen Order → Volume ·
/// Width → post section (filter, EQ, two-band limiter, post gain) → the safety limiter. The Order
/// switch crossfades the two routings so a swap never clicks. With Engine Rate at 2× the whole
/// graph runs at twice the host rate between a pair of half-band resamplers; the rate is read in
/// <see cref="Prepare"/>, so a host re-prepares to change it.
/// </summary>
public sealed class NoumenonEngine
{
    public const int ControlInterval = 32;
    public const double DefaultSampleRate = 48000.0;
    public const int UserChordCount = 8;

    private const ulong SectionSeedStride = 0x2545F4914F6CDD1DUL;
    private const ulong MatrixSeedStride = 0x9E3779B97F4A7C15UL;
    private const float AmSmoothSeconds = 0.02f;
    private const float OrderSwitchSeconds = 0.02f;
    private const float DcCutoffHz = 5f;
    private const float SafetyReleaseSeconds = 0.2f;
    private const float PeakHoldSeconds = 0.3f;

    private readonly Section[] sections = [new(), new()];
    private readonly Combiner combiner = new();
    private readonly SamplePlayer sampler = new();
    private readonly EnvelopeFollower follower = new();
    private readonly Smoother amDepth = new();
    private readonly GateEnvelope gateEnvelope = new();

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

    private readonly ModMatrix matrix = new();
    private readonly PitchFader pitch1 = new();
    private readonly PitchFader pitch2 = new();
    private readonly PresetMorph morph = new();
    private readonly float[] resochordNotes = new float[Resochord.VoiceCount];
    private readonly float[] oscillatorHz = new float[ParameterTable.SectionCount * ParameterTable.SlotsPerSection];
    private readonly float[][] userChords = new float[UserChordCount][];

    private LaneSet lanes = new();
    private Scale userScale = Scale.Equal12;
    private float[] silence = new float[512];
    private double hostSampleRate = DefaultSampleRate;
    private double sampleRate = DefaultSampleRate;
    private int oversampling = 1;
    private bool prepared;
    private int samplesUntilTick;
    private int gatePending;
    private int randomizeRequested;
    private ulong seed = 1;
    private float previousReverbLeft;
    private float previousReverbRight;
    private float peakRelease;
    private float outputPeakLeft;
    private float outputPeakRight;

    public NoumenonEngine()
    {
        for (var i = 0; i < UserChordCount; i++)
            userChords[i] = (float[])ChordTable.All[1].Notes.Clone();

        Prepare(DefaultSampleRate, 512);
    }

    public ParameterBank Parameters { get; } = new();

    public ResolvedParameters Resolved { get; } = new();

    public SampleMap Samples { get; } = new();

    public LaneSet Lanes => Volatile.Read(ref lanes);

    public LaneClock Clock { get; } = new();

    public MidiInput Midi { get; } = new();

    /// <summary>The post-Drift signal for the XY display.</summary>
    public ScopeBuffer Scope { get; } = new();

    /// <summary>The two sections after their filters, A as the left channel and B as the right, for the stack displays and the A-against-B figure.</summary>
    public ScopeBuffer SectionScope { get; } = new();

    /// <summary>Parameters the randomizer must leave alone, by <see cref="ParamId"/> index; the panel's lock buttons.</summary>
    public bool[] Locks { get; } = new bool[ParameterTable.Count];

    public double HostSampleRate => hostSampleRate;

    /// <summary>The rate the graph runs at: the host rate times <see cref="Oversampling"/>.</summary>
    public double SampleRate => sampleRate;

    public int Oversampling => oversampling;

    /// <summary>Samples of delay between input and output at the host rate: the distortion's resamplers, plus the engine-rate resamplers at 2×.</summary>
    public int LatencySamples => Distortion.Latency / oversampling + (oversampling == 2 ? HalfBand.RoundTripLatency : 0);

    public ulong Seed => seed;

    /// <summary>The P1 fader as the engine hears it (lane or fader, MIDI, bend, glide), in semitones.</summary>
    public float P1 => pitch1.Value;

    public float P2 => pitch2.Value;

    public float EnvelopeFollowerValue => follower.Value;

    public float GateEnvelopeValue => gateEnvelope.Value;

    /// <summary>Where the mute fade is: 1 running, 0 silent, in between while fading either way.</summary>
    public float FadeLevel => output.FadeLevel;

    public double SamplePosition => sampler.Position;

    public bool IsMorphing => morph.IsActive;

    public float MorphProgress => morph.Progress;

    /// <summary>The scale a Scala file or the panel defined; selected by the "User" entry of the Scale parameter.</summary>
    public Scale UserScale
    {
        get => Volatile.Read(ref userScale);
        set => Volatile.Write(ref userScale, value ?? Scale.Equal12);
    }

    public Scale ActiveScale => ScaleFor(Parameters.GetInt(ParamId.Scale));

    public float ResochordNote(int voice) => Volatile.Read(ref resochordNotes[voice]);

    /// <summary>The frequency a slot's oscillator was set to by the last control tick, 0 while the slot is off.</summary>
    public float OscillatorHz(SectionId section, int slot) => Volatile.Read(ref oscillatorHz[(int)section * ParameterTable.SlotsPerSection + slot]);

    /// <summary>The output's recent peak for a meter: instant attack, about 300 ms of release.</summary>
    public float OutputPeakLeft => Volatile.Read(ref outputPeakLeft);

    public float OutputPeakRight => Volatile.Read(ref outputPeakRight);

    /// <summary>How far the post limiter is pulling its low band down right now, in dB, 0 when it is not limiting.</summary>
    public float LimiterReductionLowDb => -DspHelper.LinearToDb(post.Limiter.LowReduction);

    public float LimiterReductionHighDb => -DspHelper.LinearToDb(post.Limiter.HighReduction);

    public float[] UserChord(int slot) => (float[])Volatile.Read(ref userChords[slot]).Clone();

    public void SetUserChord(int slot, float[] notes)
    {
        if (notes.Length != Resochord.VoiceCount)
            throw new ArgumentException($"A chord has {Resochord.VoiceCount} notes.", nameof(notes));

        Volatile.Write(ref userChords[slot], (float[])notes.Clone());
    }

    /// <summary>Fabrications' Paste: stores the notes the Resochord is ringing at right now as user chord <paramref name="slot"/>.</summary>
    public void StoreChord(int slot)
    {
        var notes = new float[Resochord.VoiceCount];
        for (var i = 0; i < notes.Length; i++)
            notes[i] = ResochordNote(i);

        SetUserChord(slot, notes);
    }

    /// <summary>Replaces the lane set for a new maximum recording time; existing recordings are dropped.</summary>
    public void ConfigureLanes(float maxSeconds) => Volatile.Write(ref lanes, new LaneSet(maxSeconds));

    /// <summary>
    /// A MIDI gate (note-on) without a note. Safe from any thread; the next control tick consumes it —
    /// the sampler retriggers when Sample Retrigger is on and the lanes restart when Restart On Gate is.
    /// </summary>
    public void Gate() => Interlocked.Exchange(ref gatePending, 1);

    /// <summary>The Randomize trigger (parameter or panel) is queued here; the UI thread runs the randomizer when this returns true.</summary>
    public bool ConsumeRandomizeRequest() => Interlocked.Exchange(ref randomizeRequested, 0) != 0;

    public void RequestRandomize() => Interlocked.Exchange(ref randomizeRequested, 1);

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
        gateEnvelope.Prepare(this.sampleRate);
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
        peakRelease = MathF.Exp(-1f / (PeakHoldSeconds * (float)this.sampleRate));
        post.Prepare(this.sampleRate);
        safety.Prepare(hostSampleRate, SafetyReleaseSeconds);
        EnsureSilence(maxBlockSize);
        prepared = true;
        Reset();
    }

    public void Reset() => Reset(seed);

    /// <summary>Two engines reset with the same parameters, lanes, samples and seed render identical samples.</summary>
    public void Reset(ulong seed)
    {
        this.seed = seed;
        matrix.Reset(seed + MatrixSeedStride);
        Lanes.RestartAll();
        ControlTick();
        for (var s = 0; s < sections.Length; s++)
            sections[s].Reset(seed + (ulong)(s + 1) * SectionSeedStride);

        combiner.Reset();
        sampler.Reset();
        follower.Clear();
        amDepth.Snap();
        gateEnvelope.Reset();
        pitch1.Snap(pitch1.Target);
        pitch2.Snap(pitch2.Target);
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
        matrix.Reset(seed + MatrixSeedStride);
        Lanes.RestartAll();
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

    /// <summary>Everything a snapshot holds, read from the UI thread (a lane still recording contributes what it has so far).</summary>
    public PresetState CapturePreset(string name)
    {
        var state = new PresetState { Name = name, Seed = seed };
        foreach (var info in ParameterTable.All)
            state.Parameters[info.Id.ToString()] = Parameters.Get(info.Id);

        foreach (var lane in Lanes.All)
        {
            if (!lane.HasRecording && lane.Mode == LaneMode.Static && lane.SpeedMultiplier == 1f)
                continue;

            var snapshot = lane.Capture();
            state.Lanes.Add(new LaneState { Parameter = lane.Id.ToString(), Mode = snapshot.Mode.ToString(), SpeedMultiplier = snapshot.SpeedMultiplier, Points = snapshot.Points });
        }

        for (var i = 0; i < UserChordCount; i++)
            state.UserChords.Add(UserChord(i));

        foreach (var slot in Samples.Loaded)
        {
            if (slot.Path is not null)
                state.Samples.Add(new SampleReference { Key = slot.Key, Path = slot.Path, Gain = slot.Gain, RootNote = slot.RootNote });
        }

        var scale = UserScale;
        if (!ReferenceEquals(scale, Scale.Equal12))
            state.UserScale = new ScaleState { Name = scale.Name, DegreesCents = scale.DegreesCents.ToArray(), PeriodCents = scale.PeriodCents };

        for (var i = 0; i < Locks.Length; i++)
        {
            if (Locks[i])
                state.Locked.Add(ParameterTable.All[i].Id.ToString());
        }

        return state;
    }

    /// <summary>
    /// Recalls a snapshot from the UI thread: parameters (missing ones take their defaults), lanes,
    /// chords, samples (missing files are reported in <paramref name="warnings"/>), the scale and
    /// the locks. The audio keeps running — values glide, and the lanes restart like MF's Restart.
    /// </summary>
    public void ApplyPreset(PresetState state, bool loadSamples = true, ICollection<string>? warnings = null)
    {
        foreach (var info in ParameterTable.All)
            Parameters.Set(info.Id, state.Parameters.TryGetValue(info.Id.ToString(), out var value) ? value : info.Default);

        var laneSet = Lanes;
        var byParameter = state.Lanes.ToDictionary(l => l.Parameter, l => l, StringComparer.OrdinalIgnoreCase);
        foreach (var lane in laneSet.All)
        {
            if (byParameter.TryGetValue(lane.Id.ToString(), out var laneState))
                lane.Load(new LaneSnapshot(laneState.Points, Enum.TryParse<LaneMode>(laneState.Mode, true, out var mode) ? mode : LaneMode.Static, laneState.SpeedMultiplier));
            else
                lane.Clear();
        }

        for (var i = 0; i < UserChordCount; i++)
        {
            if (i < state.UserChords.Count && state.UserChords[i].Length == Resochord.VoiceCount)
                SetUserChord(i, state.UserChords[i]);
        }

        if (loadSamples)
        {
            Samples.ClearAll();
            foreach (var reference in state.Samples)
            {
                try
                {
                    Samples.Load(reference.Key, reference.Path, reference.Gain, reference.RootNote);
                }
                catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidDataException or UnauthorizedAccessException)
                {
                    warnings?.Add($"Sample {reference.Key} ({reference.Path}): {ex.Message}");
                }
            }
        }

        UserScale = state.UserScale is { } scale ? new Scale(scale.Name, scale.DegreesCents, scale.PeriodCents) : Scale.Equal12;
        seed = state.Seed;

        Array.Clear(Locks);
        foreach (var name in state.Locked)
        {
            if (Enum.TryParse<ParamId>(name, true, out var id))
                Locks[(int)id] = true;
        }

        Midi.ClearVoiceNotes();
        Clock.RequestRestart();
    }

    /// <summary>Glides every parameter from its current value to <paramref name="target"/>'s over <paramref name="seconds"/>; lanes and samples are not morphed.</summary>
    public void Morph(PresetState target, float seconds)
    {
        var from = new float[ParameterTable.Count];
        Parameters.CopyTo(from);
        var to = ParameterTable.CreateDefaults();
        foreach (var info in ParameterTable.All)
        {
            if (target.Parameters.TryGetValue(info.Id.ToString(), out var value))
                to[info.Index] = info.Clamp(value);
        }

        morph.Start(from, to, seconds);
    }

    public void CancelMorph() => morph.Cancel();

    private Scale ScaleFor(int index) => index >= 0 && index < Scale.BuiltIn.Count ? Scale.BuiltIn[index] : UserScale;

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

        var a = sections[0].Process();
        var b = sections[1].Process();
        SectionScope.Write(a, b);
        var mono = combiner.Process(a, b);
        sampler.Process(inputLeft, inputRight, out var sampleLeft, out var sampleRight, out var dry);
        var envelope = follower.Process(dry);
        var depth = amDepth.Next();
        var am = 1f - depth + depth * (envelope > 1f ? 1f : envelope);
        var gateGain = gateEnvelope.Next();

        var l = chainDcLeft.Process((mono * am + sampleLeft) * gateGain);
        var r = chainDcRight.Process((mono * am + sampleRight) * gateGain);
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
        var peakL = l < 0f ? -l : l;
        var peakR = r < 0f ? -r : r;
        outputPeakLeft = peakL > outputPeakLeft ? peakL : DspHelper.Undenormalize(outputPeakLeft * peakRelease);
        outputPeakRight = peakR > outputPeakRight ? peakR : DspHelper.Undenormalize(outputPeakRight * peakRelease);
        left = l;
        right = r;
    }

    /// <summary>Builds the resolved values and sets every block's targets. Runs on the audio thread; allocation-free.</summary>
    private void ControlTick()
    {
        var bank = Parameters;
        var resolved = Resolved.Raw;
        var dt = ControlInterval / sampleRate;
        var laneSet = Volatile.Read(ref lanes);

        float R(ParamId id) => resolved[(int)id];
        bool RB(ParamId id) => resolved[(int)id] >= 0.5f;
        int RI(ParamId id) => (int)MathF.Round(resolved[(int)id]);

        // MIDI, the gate, and the triggers the engine cannot run itself.
        var gate = Interlocked.Exchange(ref gatePending, 0) != 0;
        if (Midi.Drain())
            gate = true;

        if (bank.Get(ParamId.RandomizeNow) >= 0.5f)
        {
            bank.Set(ParamId.RandomizeNow, 0f);
            Interlocked.Exchange(ref randomizeRequested, 1);
        }

        morph.Tick(dt, bank);

        // Static values, then the mod-wheel box: Fabrications' wheel drives one fader between Min and Max.
        bank.CopyTo(resolved);
        var wheelTarget = bank.GetInt(ParamId.WheelTarget);
        if (wheelTarget > 0 && wheelTarget <= ParameterTable.Count)
        {
            var info = ParameterTable.All[wheelTarget - 1];
            if (info.Kind != ParamKind.Trigger)
            {
                var lo = R(ParamId.WheelMin);
                var hi = R(ParamId.WheelMax);
                resolved[wheelTarget - 1] = info.FromNormalized(lo + (hi - lo) * Midi.Wheel);
            }
        }

        // The lanes override their faders; the clock decides whether they all restart.
        var recordingEdge = laneSet.Tick(dt, Clock.Speed, Clock.BarSeconds, bank, resolved);
        if (Clock.Tick(bank, gate, recordingEdge))
            laneSet.RestartAll();

        // The pitch layer.
        var scale = ScaleFor(RI(ParamId.Scale));
        var root = RI(ParamId.ScaleRoot);
        pitch1.Tick(dt, R(ParamId.P1Value), RB(ParamId.P1Midi), Midi, R(ParamId.P1BendRange), R(ParamId.P1Glide), scale, root);
        pitch2.Tick(dt, R(ParamId.P2Value), RB(ParamId.P2Midi), Midi, R(ParamId.P2BendRange), R(ParamId.P2Glide), scale, root);
        var p1 = pitch1.Value;
        var p2 = pitch2.Value;
        resolved[(int)ParamId.P1Value] = p1;
        resolved[(int)ParamId.P2Value] = p2;

        // The matrix, on top of everything above.
        matrix.Gather(dt, resolved, p1, p2, follower.Value, Midi);
        matrix.Apply(resolved);

        gateEnvelope.SetFreeRun(RI(ParamId.PlayMode) == 0);
        gateEnvelope.SetTimes(R(ParamId.GateAttack), R(ParamId.GateRelease));
        gateEnvelope.SetGate(Midi.HeldCount > 0);

        var a4 = R(ParamId.TuningA4);
        var masterCutoff = R(ParamId.MasterFilterCutoff);

        for (var s = 0; s < ParameterTable.SectionCount; s++)
        {
            var id = (SectionId)s;
            var section = sections[s];
            var routeAll1 = RB(ParameterTable.Section(id, SectionParam.P1RouteAll));
            var routeAll2 = RB(ParameterTable.Section(id, SectionParam.P2RouteAll));
            var fine1 = RB(ParameterTable.Section(id, SectionParam.P1FineRange));
            var fine2 = RB(ParameterTable.Section(id, SectionParam.P2FineRange));

            for (var slot = 0; slot < ParameterTable.SlotsPerSection; slot++)
            {
                var osc = section.Slot(slot);
                var on = RB(ParameterTable.Osc(id, slot, OscParam.On));
                section.SetSlotGain(slot, on ? R(ParameterTable.Osc(id, slot, OscParam.Level)) * R(ParameterTable.Osc(id, slot, OscParam.Trim)) : 0f);
                if (!on)
                {
                    Volatile.Write(ref oscillatorHz[s * ParameterTable.SlotsPerSection + slot], 0f);
                    continue;
                }

                osc.Waveform = (Waveform)RI(ParameterTable.Osc(id, slot, OscParam.Wave));
                var route1 = routeAll1 || RB(ParameterTable.Osc(id, slot, OscParam.P1Route));
                var route2 = routeAll2 || RB(ParameterTable.Osc(id, slot, OscParam.P2Route));
                var semitone = R(ParameterTable.Osc(id, slot, OscParam.Semitone));
                if (!scale.IsIdentity)
                    semitone = scale.Quantize(semitone, root);

                var fine = R(ParameterTable.Osc(id, slot, OscParam.Fine));
                var hz = Tuning.OscillatorHz(semitone, fine, p1, p2, route1, route2, fine1, fine2, a4);
                osc.SetFrequency(hz);
                Volatile.Write(ref oscillatorHz[s * ParameterTable.SlotsPerSection + slot], hz);
                osc.SetShape(R(ParameterTable.Osc(id, slot, OscParam.Shape)));
                osc.SetNoiseCutoff(R(ParameterTable.Osc(id, slot, OscParam.NoiseCutoff)));
            }

            var cutoff = RB(ParameterTable.Section(id, SectionParam.Slave)) ? masterCutoff : R(ParameterTable.Section(id, SectionParam.Cutoff));
            section.SetFilter(cutoff, R(ParameterTable.Section(id, SectionParam.Reso)), (FilterType)RI(ParameterTable.Section(id, SectionParam.FilterType)));
            section.SetLevel(R(ParameterTable.Section(id, SectionParam.Level)));
        }

        combiner.SetCrossfade(R(ParamId.MixCrossfade));
        combiner.SetBalance(R(ParamId.MixSumBalance));
        combiner.SetRingLevel(R(ParamId.MixRingLevel));

        // Sampler: the selected key's slot (a reference the loader publishes), pitch = knob + fine
        // + the chosen P fader + the slot's root-note correction, Dir as signed speed.
        var sample = Samples.Get(RI(ParamId.SamplerSelect));
        sampler.SetSample(sample?.Data);
        var sampleModSource = RI(ParamId.SamplerModSource);
        var samplePitchMod = sampleModSource == 1 ? p1 : sampleModSource == 2 ? p2 : 0f;
        var rootCorrection = sample is null ? 0f : Tuning.MiddleC - sample.RootNote;
        sampler.SetSpeed(R(ParamId.SamplerPitch) + R(ParamId.SamplerFine) / 100f + samplePitchMod + rootCorrection, R(ParamId.SamplerDir));
        sampler.SetLoop(R(ParamId.SamplerLoopStart), R(ParamId.SamplerLoopEnd), R(ParamId.SamplerLoopCrossfade));
        sampler.SetGain(R(ParamId.SamplerAmp) * R(ParamId.SamplerMaster) * (sample?.Gain ?? 1f));
        sampler.SetInputMix(R(ParamId.SamplerInputMix));
        sampler.SetStereo(RB(ParamId.SamplerStereo));
        if (gate && RB(ParamId.SamplerRetrigger))
            sampler.Retrigger();

        follower.SetTimes(R(ParamId.EnvFollowerAttack), R(ParamId.EnvFollowerRelease));
        amDepth.Target = R(ParamId.SamplerAmDepth);

        // The effect chain.
        masterFilter.SetEnabled(RB(ParamId.MasterFilterOn));
        masterFilter.SetMix(R(ParamId.MasterFilterMix));
        masterFilter.SetCutoff(masterCutoff);
        masterFilter.SetResonance(R(ParamId.MasterFilterReso));
        masterFilter.SetMorph(R(ParamId.MasterFilterMorph));

        distortion.SetEnabled(RB(ParamId.DistortionOn));
        distortion.SetMix(R(ParamId.DistortionMix));
        distortion.SetDrive(R(ParamId.DistortionDrive));
        distortion.Curve = (DistortionCurve)RI(ParamId.DistortionCurve);

        eq.SetLow(R(ParamId.EqLow));
        eq.SetHigh(R(ParamId.EqHigh));

        spin.SetEnabled(RB(ParamId.SpinOn));
        spin.SetMix(R(ParamId.SpinMix));
        spin.SetTimes(R(ParamId.SpinTime1), R(ParamId.SpinTime2), R(ParamId.SpinFine), RB(ParamId.SpinLink));
        spin.SetFeedback(R(ParamId.SpinFeedback));

        // Resochord: the six notes come from ResoMidi's round-robin, the P-driven chord select, the
        // chord list (presets or user memories) or the voice parameters, then the scale.
        resochord.SetEnabled(RB(ParamId.ResochordOn));
        resochord.SetMix(R(ParamId.ResochordMix));
        resochord.SetGlobalFeedback(R(ParamId.ResochordFeedback), R(ParamId.ResochordFeedbackFader));
        var resoModSource = RI(ParamId.ResochordModSource);
        var resoPitchMod = resoModSource == 1 ? p1 : resoModSource == 2 ? p2 : 0f;
        resochord.SetPitchOffset(R(ParamId.ResochordPitch) + R(ParamId.ResochordFine) / 100f + resoPitchMod);
        resochord.SetA4(a4);
        resochord.SetChord(0);
        var chordSelect = RI(ParamId.ResochordChordSelect);
        var chordIndex = chordSelect == 0 ? RI(ParamId.ResochordChord) : ChordTable.IndexFromPitch(chordSelect == 1 ? p1 : p2);
        var resoMidi = RB(ParamId.ResochordMidi);
        for (var v = 0; v < Resochord.VoiceCount; v++)
        {
            float note;
            var midiNote = resoMidi ? Midi.VoiceNote(v) : float.NaN;
            if (!float.IsNaN(midiNote))
                note = midiNote;
            else if (chordIndex <= 0)
                note = R(ParameterTable.ResoVoice(v, ResoVoiceParam.Note));
            else if (chordIndex < ChordTable.Count)
                note = ChordTable.All[chordIndex].Notes[v];
            else
                note = Volatile.Read(ref userChords[Math.Min(chordIndex - ChordTable.Count, UserChordCount - 1)])[v];

            if (!scale.IsIdentity)
                note = Tuning.MiddleC + scale.Quantize(note - Tuning.MiddleC, root);

            Volatile.Write(ref resochordNotes[v], note);
            resochord.SetVoice(
                v,
                note,
                R(ParameterTable.ResoVoice(v, ResoVoiceParam.Feedback)),
                R(ParameterTable.ResoVoice(v, ResoVoiceParam.Damping)),
                R(ParameterTable.ResoVoice(v, ResoVoiceParam.Level)),
                R(ParameterTable.ResoVoice(v, ResoVoiceParam.Pan)));
        }

        reverb.SetEnabled(RB(ParamId.ReverbOn));
        reverb.SetMix(R(ParamId.ReverbMix));
        reverb.SetRoom(RI(ParamId.ReverbRoom));
        reverb.SetSize(R(ParamId.ReverbSize));
        reverb.SetDamping(R(ParamId.ReverbDamping));
        reverb.SetFreeze(RB(ParamId.ReverbFreeze));
        order.Target = RI(ParamId.ReverbOrder) == 0 ? 1f : 0f;

        output.SetVolumeDb(R(ParamId.OutputVolume));
        output.SetWidth(R(ParamId.OutputWidth));
        output.SetMute(RB(ParamId.Mute));
        output.SetPause(RB(ParamId.Pause));
        output.SetFade(R(ParamId.PauseFade));

        post.Filter.SetEnabled(RB(ParamId.PostFilterOn));
        post.Filter.SetMix(R(ParamId.PostFilterMix));
        post.Filter.SetCutoff(R(ParamId.PostFilterCutoff));
        post.Filter.SetResonance(R(ParamId.PostFilterReso));
        post.Filter.SetMorph(R(ParamId.PostFilterMorph));
        post.Eq.SetLow(R(ParamId.PostEqLow));
        post.Eq.SetHigh(R(ParamId.PostEqHigh));
        post.Limiter.SetEnabled(RB(ParamId.PostLimiterOn));
        post.Limiter.SetGainDb(R(ParamId.PostLimiterGain));
        post.Limiter.SetThresholdsDb(R(ParamId.PostLimiterLowThreshold), R(ParamId.PostLimiterHighThreshold));
        post.Limiter.SetSplit(R(ParamId.PostLimiterSplit));
        post.Limiter.SetSpeed((LimiterSpeed)RI(ParamId.PostLimiterSpeed));
        post.SetVolumeDb(R(ParamId.PostVolume));
    }

    private void EnsureSilence(int count)
    {
        if (silence.Length < count)
            silence = new float[count];
    }
}
