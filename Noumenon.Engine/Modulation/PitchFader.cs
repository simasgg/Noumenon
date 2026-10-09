using Noumenon.Engine.Scales;

namespace Noumenon.Engine.Modulation;

/// <summary>
/// One of MF's two pitch faders, P1 or P2, as the engine hears it: the lane-or-static fader value
/// in semitones, replaced by the last MIDI note (relative to middle C, optionally quantized to
/// the scale) plus pitch bend times the bend range when the fader's MIDI button is on, then the
/// glide knob's portamento. The result drives the oscillators through the routing rows, the
/// sampler and the Resochord through their Mod menus, and the chord-select input.
/// </summary>
internal sealed class PitchFader
{
    private float value;
    private float target;

    public float Value => value;

    public float Target => target;

    public void Snap(float semitones)
    {
        value = semitones;
        target = semitones;
    }

    public void Tick(double seconds, float faderSemitones, bool midiFollow, MidiInput midi, float bendRange, float glideSeconds, Scale scale, int root)
    {
        target = faderSemitones;
        if (midiFollow && midi.LastNote >= 0)
        {
            var note = midi.LastNote - (float)Dsp.Shared.Tuning.MiddleC;
            if (!scale.IsIdentity)
                note = scale.Quantize(note, root);

            target = note + midi.Bend * bendRange;
        }

        if (glideSeconds <= 0.0005f)
        {
            value = target;
            return;
        }

        var coeff = 1f - MathF.Exp((float)(-seconds / (glideSeconds / 3.0)));
        value += (target - value) * coeff;
        if (MathF.Abs(target - value) <= 1e-4f)
            value = target;
    }
}
