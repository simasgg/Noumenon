using System.Runtime.CompilerServices;
using Noumenon.Dsp.Shared;

namespace Noumenon.Dsp.Sampler;

/// <summary>
/// MF's looping sampler in its classic mode, plus Fabrications' Samp↔In crossfade and Stereo switch.
/// One read head plays the selected <see cref="SampleData"/> through a loop region with an
/// equal-power seam crossfade, at a speed that combines the file/engine rate ratio, the pitch in
/// semitones and the signed Dir fader (negative = reverse, 0 = frozen). Every change that would
/// otherwise jump the playhead — a retrigger, a new sample, a loop range that strands the head —
/// hands the old material to a second, fading-out head while the new one fades in (~10 ms), so
/// none of them click. Speed, gains, the input crossfade and the stereo blend glide per sample.
/// <see cref="Process"/> runs on the audio thread and does not allocate; a sample switch only
/// swaps references.
/// </summary>
public sealed class SamplePlayer
{
    /// <summary>A loop region shorter than this plays nothing.</summary>
    public const int MinLoopFrames = 4;

    private const float GainSmoothSeconds = 0.02f;
    private const float SwitchFadeSeconds = 0.01f;

    private sealed class Head
    {
        public SampleData? Data;
        public double Position;
        public double RateRatio = 1.0;
        public int LoopStart;
        public int LoopEnd;
        public int Crossfade;
        public readonly Smoother Fade = new();
    }

    private readonly Head head = new();
    private readonly Head tail = new();
    private readonly Smoother speed = new();
    private readonly Smoother amp = new();
    private readonly Smoother sampleSide = new();
    private readonly Smoother inputSide = new();
    private readonly Smoother stereo = new();

    private double sampleRate = 48000.0;
    private float loopStartFraction;
    private float loopEndFraction = 1f;
    private float crossfadeMs = 10f;

    public SamplePlayer()
    {
        speed.Snap(1f);
        amp.Snap(1f);
        sampleSide.Snap(1f);
        inputSide.Snap(0f);
        stereo.Snap(1f);
        head.Fade.Snap(1f);
        tail.Fade.Snap(0f);
    }

    public SampleData? Sample => head.Data;

    /// <summary>The playhead, in frames of the current sample.</summary>
    public double Position => head.Position;

    public int LoopStartFrame => head.LoopStart;

    public int LoopEndFrame => head.LoopEnd;

    /// <summary>Selects the sample to play; a different instance than the current one is faded in over the old.</summary>
    public void SetSample(SampleData? data)
    {
        if (data is not null && data.Length < MinLoopFrames)
            data = null;

        if (ReferenceEquals(data, head.Data))
            return;

        Switch(data);
    }

    /// <summary>Restarts the current sample from the loop's start (or its end when playing backwards), click-free.</summary>
    public void Retrigger()
    {
        if (head.Data is not null)
            Switch(head.Data);
    }

    /// <summary>Loop region as fractions of the sample, and the seam crossfade in milliseconds (of sample time).</summary>
    public void SetLoop(float startFraction, float endFraction, float crossfadeMilliseconds)
    {
        loopStartFraction = DspHelper.Clamp01(startFraction);
        loopEndFraction = DspHelper.Clamp01(endFraction);
        crossfadeMs = crossfadeMilliseconds < 0f ? 0f : crossfadeMilliseconds;
        ApplyLoop(head);
        ApplyLoop(tail);
        if (head.Data is not null && (head.Position < head.LoopStart || head.Position >= head.LoopEnd))
            Switch(head.Data);
    }

    /// <summary>Pitch offset in semitones (knob + fine + P modulation + root-note correction) and the signed Dir speed.</summary>
    public void SetSpeed(float semitones, float direction) => speed.Target = MathF.Pow(2f, semitones / 12f) * direction;

    /// <summary>The product of the Amp fader, the Master knob and the slot gain.</summary>
    public void SetGain(float gain) => amp.Target = gain < 0f ? 0f : gain;

    /// <summary>0 = the sample, 1 = the live input; equal-power in between.</summary>
    public void SetInputMix(float mix)
    {
        var angle = DspHelper.Clamp01(mix) * (MathF.PI * 0.5f);
        sampleSide.Target = MathF.Cos(angle);
        inputSide.Target = MathF.Sin(angle);
    }

    /// <summary>Off sums the two channels to mono, the original MF behaviour.</summary>
    public void SetStereo(bool on) => stereo.Target = on ? 1f : 0f;

    public void Prepare(double sampleRate)
    {
        this.sampleRate = sampleRate <= 0 ? 48000.0 : sampleRate;
        speed.SetTime(this.sampleRate, GainSmoothSeconds);
        amp.SetTime(this.sampleRate, GainSmoothSeconds);
        sampleSide.SetTime(this.sampleRate, GainSmoothSeconds);
        inputSide.SetTime(this.sampleRate, GainSmoothSeconds);
        stereo.SetTime(this.sampleRate, GainSmoothSeconds);
        head.Fade.SetTime(this.sampleRate, SwitchFadeSeconds);
        tail.Fade.SetTime(this.sampleRate, SwitchFadeSeconds);
        head.RateRatio = RateRatio(head.Data);
        tail.RateRatio = RateRatio(tail.Data);
        ApplyLoop(head);
        ApplyLoop(tail);
    }

    /// <summary>Snaps every glide, drops the fading head and rewinds the playhead to the loop start.</summary>
    public void Reset()
    {
        speed.Snap();
        amp.Snap();
        sampleSide.Snap();
        inputSide.Snap();
        stereo.Snap();
        tail.Data = null;
        tail.Fade.Snap(0f);
        head.Fade.Snap(1f);
        head.Position = StartPosition(head);
    }

    /// <summary>
    /// One frame: the sample (both heads) crossfaded against the live input, folded to mono when
    /// Stereo is off, then scaled by the gain. <paramref name="dryMono"/> is the mono signal before
    /// the gain — what the envelope follower and the AM route listen to, so Amp can be at zero while
    /// the sample still shapes the oscillators.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Process(float inputLeft, float inputRight, out float left, out float right, out float dryMono)
    {
        var rate = speed.Next();
        var l = 0f;
        var r = 0f;
        if (head.Data is not null)
            Read(head, rate, ref l, ref r);

        if (tail.Data is not null)
        {
            Read(tail, rate, ref l, ref r);
            if (tail.Fade.Current == 0f && tail.Fade.Target == 0f)
                tail.Data = null;
        }

        var s = sampleSide.Next();
        var i = inputSide.Next();
        l = l * s + inputLeft * i;
        r = r * s + inputRight * i;

        var mono = 0.5f * (l + r);
        var w = stereo.Next();
        l = mono + (l - mono) * w;
        r = mono + (r - mono) * w;
        dryMono = mono;

        var g = amp.Next();
        left = l * g;
        right = r * g;
    }

    /// <summary>
    /// Reads one frame from a head and advances it. Forward play crossfades the last
    /// <c>Crossfade</c> frames of the loop into its first ones and then wraps past them, so the seam
    /// uses material from inside the loop and a loop that starts at frame 0 still crossfades;
    /// backward play mirrors that at the loop's start. The effective period is
    /// <c>LoopEnd − LoopStart − Crossfade</c>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Read(Head h, float rate, ref float left, ref float right)
    {
        var data = h.Data!;
        var fade = h.Fade.Next();
        var inc = rate * h.RateRatio;
        var p = h.Position;
        var start = h.LoopStart;
        var end = h.LoopEnd;
        var xf = h.Crossfade;

        float sl, sr;
        if (xf > 0 && inc >= 0.0 && p >= end - xf)
        {
            var t = (p - (end - xf)) / xf;
            var shadow = start + t * xf;
            var angle = (float)t * (MathF.PI * 0.5f);
            var a = MathF.Cos(angle);
            var b = MathF.Sin(angle);
            sl = a * Interpolation.Hermite(data.Left, p) + b * Interpolation.Hermite(data.Left, shadow);
            sr = data.IsStereo ? a * Interpolation.Hermite(data.Right, p) + b * Interpolation.Hermite(data.Right, shadow) : sl;
        }
        else if (xf > 0 && inc < 0.0 && p < start + xf)
        {
            var t = (start + xf - p) / xf;
            var shadow = end - t * xf;
            var angle = (float)t * (MathF.PI * 0.5f);
            var a = MathF.Cos(angle);
            var b = MathF.Sin(angle);
            sl = a * Interpolation.Hermite(data.Left, p) + b * Interpolation.Hermite(data.Left, shadow);
            sr = data.IsStereo ? a * Interpolation.Hermite(data.Right, p) + b * Interpolation.Hermite(data.Right, shadow) : sl;
        }
        else
        {
            sl = Interpolation.Hermite(data.Left, p);
            sr = data.IsStereo ? Interpolation.Hermite(data.Right, p) : sl;
        }

        left += sl * fade;
        right += sr * fade;

        p += inc;
        var span = end - start - xf;
        if (span <= 0)
        {
            p = start;
        }
        else if (p >= end)
        {
            p -= span;
            if (p >= end)
                p = start + xf;
        }
        else if (p < start)
        {
            p += span;
            if (p < start)
                p = end - xf - 1;
        }

        h.Position = p;
    }

    private void Switch(SampleData? data)
    {
        tail.Data = head.Data;
        tail.Position = head.Position;
        tail.RateRatio = head.RateRatio;
        tail.LoopStart = head.LoopStart;
        tail.LoopEnd = head.LoopEnd;
        tail.Crossfade = head.Crossfade;
        tail.Fade.Snap(head.Fade.Current);
        tail.Fade.Target = 0f;

        head.Data = data;
        head.RateRatio = RateRatio(data);
        ApplyLoop(head);
        head.Position = StartPosition(head);
        head.Fade.Snap(0f);
        head.Fade.Target = 1f;
    }

    private void ApplyLoop(Head h)
    {
        var data = h.Data;
        if (data is null)
        {
            h.LoopStart = 0;
            h.LoopEnd = 0;
            h.Crossfade = 0;
            return;
        }

        var length = data.Length;
        var start = Math.Clamp((int)(loopStartFraction * length), 0, length - MinLoopFrames);
        var end = Math.Clamp((int)(loopEndFraction * length), start + MinLoopFrames, length);
        var crossfade = (int)(crossfadeMs * data.SampleRate / 1000f);
        h.LoopStart = start;
        h.LoopEnd = end;
        h.Crossfade = Math.Min(crossfade, (end - start) / 2);
    }

    private double StartPosition(Head h)
    {
        if (h.Data is null)
            return 0.0;

        return speed.Target < 0f ? Math.Max(h.LoopStart, h.LoopEnd - h.Crossfade - 1) : h.LoopStart;
    }

    private double RateRatio(SampleData? data) => data is null ? 1.0 : data.SampleRate / sampleRate;
}
