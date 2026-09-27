using System.Runtime.CompilerServices;
using Noumenon.Dsp.Reverb;
using Noumenon.Dsp.Shared;

namespace Noumenon.Dsp.Effects;

/// <summary>
/// MF's Resochord: six parallel feedback combs tuned to the notes of a chord, so whatever passes
/// through rings at those pitches. Each voice has a bipolar feedback trim, a damping low-pass in
/// its loop, a level and a pan; the global feedback (knob + fader, ≤ 0.99) scales them all and a
/// soft clip in every loop keeps a resonance from running away. The comb length is the period of
/// the note minus the damping filter's phase delay at that note, so a voice rings within a couple
/// of cents of its pitch at any damping. Chord changes and pitch modulation glide the lengths (a
/// pitch slide, no click). Chord index 0 means the six voice Note parameters as set; any other
/// index is a <see cref="ChordTable"/> preset. Mono in (the sum), stereo out through the pans;
/// idle when off or at mix 0. Setters run every control tick and are no-ops when nothing
/// changed; retuning is done once, lazily, on the next sample.
/// </summary>
public sealed class Resochord
{
    public const int VoiceCount = ChordTable.VoiceCount;
    public const float MaxFeedback = 0.99f;

    private const float MinHz = 20f;
    private const float MaxDelaySeconds = 1f / MinHz + 0.01f;
    private const float SmoothSeconds = 0.02f;
    private const float BrightestDampingHz = 20000f;
    private const float DarkestDampingHz = 500f;
    private const float VoiceSumGain = 0.4f;
    private const float SoftClipKnee = 0.5f;

    private sealed class Voice
    {
        public readonly InterpolatedDelay Delay = new();
        public readonly OnePoleLowpass Damping = new();
        public readonly Smoother Feedback = new();
        public readonly Smoother Level = new();
        public readonly Smoother PanLeft = new();
        public readonly Smoother PanRight = new();
        public float Note = 60f;
        public float FeedbackTrim = 0.9f;
        public float DampingAmount = float.NaN;
        public float LastLevel = float.NaN;
        public float LastPan = float.NaN;
        public float DampingCoeff = 1f;
    }

    private readonly Voice[] voices = new Voice[VoiceCount];
    private readonly BypassMix mix = new(0.3f);

    private double sampleRate = 48000.0;
    private float globalFeedback = 0.7f;
    private float pitchOffset;
    private float a4Hz = Tuning.DefaultA4Hz;
    private int chordIndex;
    private bool tuningDirty = true;
    private bool wasIdle = true;

    public Resochord()
    {
        for (var i = 0; i < VoiceCount; i++)
        {
            var voice = new Voice();
            voice.Level.Snap(0.8f);
            voice.PanLeft.Snap(0.7071f);
            voice.PanRight.Snap(0.7071f);
            voice.Feedback.Snap(0.63f);
            voices[i] = voice;
            SetVoiceDamping(voice, 0.3f);
        }
    }

    public void SetEnabled(bool on) => mix.SetEnabled(on);

    public void SetMix(float value) => mix.SetMix(value);

    public void SetGlobalFeedback(float knob, float fader)
    {
        var total = DspHelper.Clamp(knob + fader, 0f, MaxFeedback);
        if (total == globalFeedback)
            return;

        globalFeedback = total;
        for (var i = 0; i < VoiceCount; i++)
            UpdateVoiceFeedback(voices[i]);
    }

    public void SetPitchOffset(float semitones)
    {
        if (semitones == pitchOffset)
            return;

        pitchOffset = semitones;
        tuningDirty = true;
    }

    public void SetA4(float hz)
    {
        if (hz == a4Hz)
            return;

        a4Hz = hz;
        tuningDirty = true;
    }

    public void SetChord(int index)
    {
        var clamped = Math.Clamp(index, 0, ChordTable.Count - 1);
        if (clamped == chordIndex)
            return;

        chordIndex = clamped;
        tuningDirty = true;
    }

    public void SetVoice(int index, float note, float feedbackTrim, float damping, float level, float pan)
    {
        var voice = voices[index];
        if (note != voice.Note)
        {
            voice.Note = note;
            tuningDirty = true;
        }

        var trim = DspHelper.Clamp(feedbackTrim, -MaxFeedback, MaxFeedback);
        if (trim != voice.FeedbackTrim)
        {
            voice.FeedbackTrim = trim;
            UpdateVoiceFeedback(voice);
        }

        var damp = DspHelper.Clamp01(damping);
        if (damp != voice.DampingAmount)
        {
            SetVoiceDamping(voice, damp);
            tuningDirty = true;
        }

        if (level != voice.LastLevel)
        {
            voice.LastLevel = level;
            voice.Level.Target = level < 0f ? 0f : level;
        }

        if (pan != voice.LastPan)
        {
            voice.LastPan = pan;
            var angle = (DspHelper.Clamp(pan, -1f, 1f) + 1f) * 0.5f * (MathF.PI * 0.5f);
            voice.PanLeft.Target = MathF.Cos(angle);
            voice.PanRight.Target = MathF.Sin(angle);
        }
    }

    public float EffectiveNote(int index) => chordIndex > 0 ? ChordTable.All[chordIndex].Notes[index] : voices[index].Note;

    public void Prepare(double sampleRate)
    {
        this.sampleRate = sampleRate <= 0 ? 48000.0 : sampleRate;
        for (var i = 0; i < VoiceCount; i++)
        {
            var voice = voices[i];
            voice.Delay.Prepare(this.sampleRate, MaxDelaySeconds, SmoothSeconds);
            voice.Feedback.SetTime(this.sampleRate, SmoothSeconds);
            voice.Level.SetTime(this.sampleRate, SmoothSeconds);
            voice.PanLeft.SetTime(this.sampleRate, SmoothSeconds);
            voice.PanRight.SetTime(this.sampleRate, SmoothSeconds);
            SetVoiceDamping(voice, voice.DampingAmount);
        }

        mix.Prepare(this.sampleRate);
        UpdateTuning();
        for (var i = 0; i < VoiceCount; i++)
            voices[i].Delay.SnapDelay();
    }

    public void Reset()
    {
        if (tuningDirty)
            UpdateTuning();

        for (var i = 0; i < VoiceCount; i++)
        {
            var voice = voices[i];
            voice.Delay.Clear();
            voice.Damping.Clear();
            voice.Feedback.Snap();
            voice.Level.Snap();
            voice.PanLeft.Snap();
            voice.PanRight.Snap();
        }

        mix.Snap();
        wasIdle = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Process(ref float l, ref float r)
    {
        if (mix.IsIdle)
        {
            wasIdle = true;
            return;
        }

        if (tuningDirty)
            UpdateTuning();

        if (wasIdle)
        {
            for (var i = 0; i < VoiceCount; i++)
            {
                voices[i].Delay.Clear();
                voices[i].Damping.Clear();
            }

            wasIdle = false;
        }

        var input = 0.5f * (l + r);
        var sumL = 0f;
        var sumR = 0f;
        for (var i = 0; i < VoiceCount; i++)
        {
            var voice = voices[i];
            var delayed = voice.Delay.Read();
            voice.Delay.Write(input + SoftClip(voice.Damping.Process(delayed) * voice.Feedback.Next()));
            var output = delayed * voice.Level.Next();
            sumL += output * voice.PanLeft.Next();
            sumR += output * voice.PanRight.Next();
        }

        var w = mix.Next();
        l += (sumL * VoiceSumGain - l) * w;
        r += (sumR * VoiceSumGain - r) * w;
    }

    /// <summary>Linear below the knee, tanh above it: transparent at working levels, a hard ceiling near 1.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float SoftClip(float x)
    {
        var magnitude = MathF.Abs(x);
        if (magnitude <= SoftClipKnee)
            return x;

        var shaped = SoftClipKnee + (1f - SoftClipKnee) * MathF.Tanh((magnitude - SoftClipKnee) / (1f - SoftClipKnee));

        return x < 0f ? -shaped : shaped;
    }

    private void UpdateVoiceFeedback(Voice voice) => voice.Feedback.Target = DspHelper.Clamp(globalFeedback * voice.FeedbackTrim, -MaxFeedback, MaxFeedback);

    private void SetVoiceDamping(Voice voice, float amount)
    {
        voice.DampingAmount = amount;
        var hz = BrightestDampingHz * MathF.Pow(DarkestDampingHz / BrightestDampingHz, amount);
        voice.DampingCoeff = DspHelper.OnePoleCoeff(sampleRate, hz);
        voice.Damping.Coeff = voice.DampingCoeff;
    }

    private void UpdateTuning()
    {
        tuningDirty = false;
        for (var i = 0; i < VoiceCount; i++)
        {
            var voice = voices[i];
            var note = EffectiveNote(i) + pitchOffset;
            var hz = DspHelper.Clamp(Tuning.NoteToHz(note, a4Hz), MinHz, (float)(sampleRate * 0.25));
            var period = (float)(sampleRate / hz);
            voice.Delay.SetDelaySamples(period - DampingPhaseDelay(voice.DampingCoeff, (float)(DspHelper.TwoPi * hz / sampleRate)));
        }
    }

    /// <summary>Phase delay in samples of the one-pole damping low-pass <c>a / (1 − (1−a)z⁻¹)</c> at the voice's own frequency.</summary>
    private static float DampingPhaseDelay(float coeff, float omega)
    {
        if (coeff >= 1f)
            return 0f;

        var b = 1f - coeff;
        var phase = MathF.Atan2(b * MathF.Sin(omega), 1f - b * MathF.Cos(omega));

        return phase / omega;
    }
}
