namespace Noumenon.Dsp.Oscillators;

/// <summary>
/// The waves an oscillator slot can produce. The order is the parameter table's choice list, so
/// append only. MF's fixed slot layout uses Sine, Triangle, Pulse, BipolarPulse and the two noises;
/// Saw, Square, Wavetable and Sub are additions.
/// </summary>
public enum Waveform
{
    Sine,

    /// <summary>Shape moves the peak: 0.5 is symmetric, towards 0 or 1 it leans into a ramp.</summary>
    Triangle,

    Saw,

    /// <summary>A pulse at fixed 50 % width; Shape is ignored.</summary>
    Square,

    /// <summary>Shape is the pulse width (the duty cycle).</summary>
    Pulse,

    /// <summary>
    /// A positive pulse at the start of the cycle and a negative one half a cycle later, zero
    /// between them (Reaktor's Bi-Pulse). Shape is the pulse width; at 1 it becomes a square.
    /// </summary>
    BipolarPulse,

    /// <summary>White noise through a one-pole low-pass at the slot's noise cutoff (MF section A's noise).</summary>
    NoiseLp,

    /// <summary>Sample-and-hold noise stepping at the slot's noise cutoff rate: pitched, gritty (MF section B's second noise mode).</summary>
    NoiseSh,

    /// <summary>A band-limited wavetable cut from a sample (the Wavetable Key); Shape scans its frames. Silent until the table exists.</summary>
    Wavetable,

    /// <summary>
    /// A sine with its sub-octave, the two locked to one phase (the sub is a divider: its cycle
    /// spans two of the slot's). Shape crossfades them at equal power: 0 the slot's pitch only,
    /// 0.5 both, 1 the octave below only.
    /// </summary>
    Sub,
}
