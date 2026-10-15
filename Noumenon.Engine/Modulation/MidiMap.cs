using System.Numerics;
using Noumenon.Engine.Parameters;

namespace Noumenon.Engine.Modulation;

/// <summary>One learned controller: CC number → parameter, as presets and settings store it.</summary>
public sealed class MidiMapping
{
    public int Controller { get; set; }

    public string Parameter { get; set; } = "";
}

/// <summary>
/// MIDI learn: which controller moves which parameter. The panel arms a parameter
/// (<see cref="BeginLearn"/>); the next controller that arrives is bound to it — a parameter has
/// at most one controller and a controller at most one parameter, so learning again moves the
/// binding — and from then on every value that controller sends is written to the parameter's
/// bank value through its normalized range, at the control tick, so the knob moves, undo and
/// presets see it, and a plugin reports it to its host like any panel edit. Only arriving messages
/// move anything: binding a controller or loading a map never jumps a parameter. Channel-mode
/// controllers (120 and up) are never learned. The map is instrument setup rather than sound —
/// the standalone keeps it in its settings and a plugin in the host's project state, and loading a
/// preset leaves it alone. The audio thread binds and applies; the UI thread arms, forgets and
/// replaces, each a single-int write.
/// </summary>
public sealed class MidiMap
{
    public const int ControllerCount = 128;
    public const int FirstModeController = 120;

    private readonly int[] targets = new int[ControllerCount];
    private int learning;
    private int learned;
    private int lastController = -1;
    private int version;

    /// <summary>Changes whenever a binding is made or removed, so a display can cache what it looked up.</summary>
    public int Version => Volatile.Read(ref version);

    /// <summary>The parameter waiting for a controller, or null.</summary>
    public ParamId? Learning
    {
        get
        {
            var target = Volatile.Read(ref learning);

            return target > 0 ? (ParamId)(target - 1) : null;
        }
    }

    /// <summary>Counts the bindings the audio thread has made, so the panel can tell when the one it armed has landed.</summary>
    public int LearnedCount => Volatile.Read(ref learned);

    /// <summary>The controller bound most recently.</summary>
    public int LastController => Volatile.Read(ref lastController);

    public void BeginLearn(ParamId id) => Volatile.Write(ref learning, (int)id + 1);

    public void CancelLearn() => Volatile.Write(ref learning, 0);

    /// <summary>The controller bound to a parameter, or -1.</summary>
    public int ControllerFor(ParamId id)
    {
        var target = (int)id + 1;
        for (var cc = 0; cc < ControllerCount; cc++)
        {
            if (Volatile.Read(ref targets[cc]) == target)
                return cc;
        }

        return -1;
    }

    /// <summary>The parameter a controller moves, or null.</summary>
    public ParamId? ParameterFor(int controller)
    {
        if ((uint)controller >= ControllerCount)
            return null;

        var target = Volatile.Read(ref targets[controller]);

        return target > 0 ? (ParamId)(target - 1) : null;
    }

    public void Assign(int controller, ParamId id)
    {
        if ((uint)controller >= FirstModeController)
            return;

        Forget(id);
        Volatile.Write(ref targets[controller], (int)id + 1);
        Interlocked.Increment(ref version);
    }

    public void Forget(ParamId id)
    {
        var target = (int)id + 1;
        for (var cc = 0; cc < ControllerCount; cc++)
        {
            if (Volatile.Read(ref targets[cc]) == target)
                Volatile.Write(ref targets[cc], 0);
        }

        Interlocked.Increment(ref version);
    }

    public void Clear()
    {
        for (var cc = 0; cc < ControllerCount; cc++)
            Volatile.Write(ref targets[cc], 0);

        CancelLearn();
        Interlocked.Increment(ref version);
    }

    /// <summary>Every binding, by controller number, for the panel and for saving.</summary>
    public List<MidiMapping> Capture()
    {
        var list = new List<MidiMapping>();
        for (var cc = 0; cc < ControllerCount; cc++)
        {
            var target = Volatile.Read(ref targets[cc]);
            if (target > 0 && target <= ParameterTable.Count)
                list.Add(new MidiMapping { Controller = cc, Parameter = ((ParamId)(target - 1)).ToString() });
        }

        return list;
    }

    /// <summary>Replaces every binding; entries naming an unknown parameter or a channel-mode controller are skipped.</summary>
    public void Load(IEnumerable<MidiMapping>? mappings)
    {
        Clear();
        if (mappings is null)
            return;

        foreach (var mapping in mappings)
        {
            if ((uint)mapping.Controller < FirstModeController && Enum.TryParse<ParamId>(mapping.Parameter, true, out var id) && (int)id < ParameterTable.Count)
                Assign(mapping.Controller, id);
        }
    }

    /// <summary>Audio thread, after the MIDI drain: binds an armed parameter and applies every controller that arrived.</summary>
    internal void Apply(MidiInput midi, ParameterBank bank)
    {
        var low = midi.ArrivedLow;
        var high = midi.ArrivedHigh;
        if ((low | high) == 0UL)
            return;

        while (low != 0UL)
        {
            var cc = BitOperations.TrailingZeroCount(low);
            low &= low - 1;
            Arrived(cc, midi, bank);
        }

        while (high != 0UL)
        {
            var cc = 64 + BitOperations.TrailingZeroCount(high);
            high &= high - 1;
            Arrived(cc, midi, bank);
        }
    }

    private void Arrived(int controller, MidiInput midi, ParameterBank bank)
    {
        if (controller >= FirstModeController)
            return;

        var armed = Volatile.Read(ref learning);
        if (armed > 0)
        {
            var id = (ParamId)(armed - 1);
            Assign(controller, id);
            Volatile.Write(ref learning, 0);
            Volatile.Write(ref lastController, controller);
            Interlocked.Increment(ref learned);
        }

        var target = Volatile.Read(ref targets[controller]);
        if (target <= 0 || target > ParameterTable.Count)
            return;

        var info = ParameterTable.All[target - 1];
        if (info.Kind == ParamKind.Trigger)
        {
            if (midi.Controller(controller) >= 0.5f)
                bank.Set(info.Id, 1f);

            return;
        }

        bank.Set(info.Id, info.FromNormalized(midi.Controller(controller)));
    }
}
