using System.Runtime.CompilerServices;
using Noumenon.Dsp.Shared;

namespace Noumenon.Dsp.Reverb;

/// <summary>
/// MF's Reverb: a room selector over the two ported engines, with Size, Damping, Freeze
/// ("Endless") and Mix. A room pins the engine and its character (<see cref="ReverbRooms"/>);
/// switching to a room on the other engine crossfades to a freshly cleared engine over ~40 ms —
/// both run only while the crossfade lasts, so the block costs one reverb, not two. Idle when
/// off or at mix 0, and cleared when it wakes up so no old tail reappears. Setters are called
/// every control tick and only touch the engines when a value actually changes.
/// </summary>
public sealed class ReverbBlock
{
    private const float SwitchSeconds = 0.04f;

    private readonly PlateReverbEngine plate = new();
    private readonly FreeverbEngine freeverb = new();
    private readonly Smoother crossfade = new();
    private readonly BypassMix mix = new(0.3f);

    private IReverbEngine active;
    private IReverbEngine? fading;
    private int roomIndex = 2;
    private float size = 0.7f;
    private float damping = 0.4f;
    private bool freeze;
    private bool wasIdle = true;

    public ReverbBlock()
    {
        active = plate;
        crossfade.Snap(1f);
        plate.Mix = 1f;
        plate.Width = 1f;
        freeverb.Mix = 1f;
        freeverb.Width = 1f;
        plate.Size = size;
        freeverb.Size = size;
        plate.Damping = damping;
        freeverb.Damping = damping;
        ApplyRoom();
    }

    public ReverbRoom Room => ReverbRooms.All[roomIndex];

    public void SetEnabled(bool on) => mix.SetEnabled(on);

    public void SetMix(float value) => mix.SetMix(value);

    public void SetSize(float value)
    {
        if (value == size)
            return;

        size = value;
        plate.Size = value;
        freeverb.Size = value;
    }

    public void SetDamping(float value)
    {
        if (value == damping)
            return;

        damping = value;
        plate.Damping = value;
        freeverb.Damping = value;
    }

    public void SetFreeze(bool on)
    {
        if (on == freeze)
            return;

        freeze = on;
        ApplyFreeze();
    }

    public void SetRoom(int index)
    {
        index = Math.Clamp(index, 0, ReverbRooms.Count - 1);
        if (index == roomIndex)
            return;

        roomIndex = index;
        ApplyRoom();
        var wanted = Room.Mode == ReverbMode.Plate ? (IReverbEngine)plate : freeverb;
        if (ReferenceEquals(wanted, active))
            return;

        // Hand the tail to the old engine and start the new one clean.
        fading?.Reset();
        fading = active;
        wanted.Reset();
        active = wanted;
        crossfade.Snap(0f);
        crossfade.Target = 1f;
    }

    public void Prepare(double sampleRate)
    {
        plate.Prepare(sampleRate, 512);
        freeverb.Prepare(sampleRate, 512);
        crossfade.SetTime(sampleRate, SwitchSeconds);
        mix.Prepare(sampleRate);
    }

    public void Reset()
    {
        plate.Reset();
        freeverb.Reset();
        fading = null;
        crossfade.Snap(1f);
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

        if (wasIdle)
        {
            active.Reset();
            fading = null;
            crossfade.Snap(1f);
            wasIdle = false;
        }

        active.ProcessSample(l, r, out var wetL, out var wetR);
        if (fading is not null)
        {
            fading.ProcessSample(l, r, out var oldL, out var oldR);
            var t = crossfade.Next();
            wetL = oldL + (wetL - oldL) * t;
            wetR = oldR + (wetR - oldR) * t;
            if (t >= 1f)
            {
                fading.Reset();
                fading = null;
            }
        }

        var w = mix.Next();
        l += (wetL - l) * w;
        r += (wetR - r) * w;
    }

    private void ApplyRoom()
    {
        var room = Room;
        ApplyRoom(plate, room);
        ApplyRoom(freeverb, room);
        ApplyFreeze();
    }

    private static void ApplyRoom(IReverbEngine engine, ReverbRoom room)
    {
        engine.PreDelayMs = room.PreDelayMs;
        engine.Diffusion = room.Diffusion;
        engine.Bass = room.Bass;
        engine.ModDepth = room.ModDepth;
        engine.ModRate = room.ModRate;
        engine.ModDrift = room.ModDrift;
        engine.HighCut = room.HighCut;
        engine.LowCut = room.LowCut;
    }

    private void ApplyFreeze()
    {
        var on = freeze || Room.Freeze;
        plate.Freeze = on;
        freeverb.Freeze = on;
    }
}
