using Noumenon.Dsp.Modulation;
using Noumenon.Engine.Parameters;

namespace Noumenon.Engine.Modulation;

/// <summary>
/// The general modulation matrix (a Noumenon addition; MF's routing rows, Mod menus and the
/// mod-wheel box are fixed slots the engine wires directly). Twelve slots each add
/// depth × source to a target parameter in its normalized 0–1 domain, so a cutoff modulates
/// musically and a level linearly, on top of whatever the lane or the fader gave it. Sources are
/// filled once per tick in the order of the parameter table's list: none, P1, P2 (as ±1 over
/// ±48 semitones), the three LFOs (times their depth), the envelope follower, MIDI velocity,
/// aftertouch, the mod wheel, the controller the Matrix CC parameter names, and the four macros.
/// The panel reads the sources back to draw them.
/// </summary>
internal sealed class ModMatrix
{
    public const int SourceCount = 15;
    public const int SourceP1 = 1;
    public const int SourceP2 = 2;
    public const int SourceLfo1 = 3;
    public const int SourceEnvelope = 6;
    public const int SourceVelocity = 7;
    public const int SourceAftertouch = 8;
    public const int SourceWheel = 9;
    public const int SourceController = 10;
    public const int SourceMacro1 = 11;

    private readonly float[] sources = new float[SourceCount];

    public Lfo[] Lfos { get; } = [new(), new(), new()];

    public float Source(int index) => (uint)index < SourceCount ? Volatile.Read(ref sources[index]) : 0f;

    public void Reset(ulong seed)
    {
        for (var i = 0; i < Lfos.Length; i++)
            Lfos[i].Reset(seed + (ulong)(i + 1) * 0x9E3779B97F4A7C15UL);

        Array.Clear(sources);
    }

    /// <summary>Advances the LFOs and gathers every source for this tick.</summary>
    public void Gather(double seconds, float[] resolved, float p1, float p2, float envelope, MidiInput midi)
    {
        sources[SourceP1] = Math.Clamp(p1 / 48f, -1f, 1f);
        sources[SourceP2] = Math.Clamp(p2 / 48f, -1f, 1f);
        for (var i = 0; i < Lfos.Length; i++)
        {
            var lfo = Lfos[i];
            lfo.RateHz = resolved[(int)ParameterTable.Lfo(i, LfoParam.Rate)];
            lfo.Shape = (LfoShape)(int)resolved[(int)ParameterTable.Lfo(i, LfoParam.Shape)];
            sources[SourceLfo1 + i] = lfo.Advance(seconds) * resolved[(int)ParameterTable.Lfo(i, LfoParam.Depth)];
        }

        sources[SourceEnvelope] = Math.Clamp(envelope, 0f, 1f);
        sources[SourceVelocity] = midi.LastVelocity;
        sources[SourceAftertouch] = midi.Aftertouch;
        sources[SourceWheel] = midi.Wheel;
        sources[SourceController] = midi.Controller((int)resolved[(int)ParamId.MatrixCc]);
        sources[SourceMacro1] = resolved[(int)ParamId.Macro1];
        sources[SourceMacro1 + 1] = resolved[(int)ParamId.Macro2];
        sources[SourceMacro1 + 2] = resolved[(int)ParamId.Macro3];
        sources[SourceMacro1 + 3] = resolved[(int)ParamId.Macro4];
    }

    /// <summary>Applies every active slot to <paramref name="resolved"/>.</summary>
    public void Apply(float[] resolved)
    {
        var all = ParameterTable.All;
        for (var slot = 0; slot < ParameterTable.ModSlotCount; slot++)
        {
            var source = (int)resolved[(int)ParameterTable.ModSlot(slot, ModSlotParam.Source)];
            var target = (int)resolved[(int)ParameterTable.ModSlot(slot, ModSlotParam.Target)];
            var depth = resolved[(int)ParameterTable.ModSlot(slot, ModSlotParam.Depth)];
            if (source <= 0 || source >= SourceCount || target <= 0 || target > all.Count || depth == 0f)
                continue;

            var info = all[target - 1];
            if (info.Kind == ParamKind.Trigger)
                continue;

            var normalized = info.ToNormalized(resolved[target - 1]) + depth * sources[source];
            resolved[target - 1] = info.FromNormalized(normalized < 0f ? 0f : normalized > 1f ? 1f : normalized);
        }
    }
}
