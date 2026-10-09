using Noumenon.Engine.Parameters;

namespace Noumenon.Engine.Lanes;

/// <summary>
/// One <see cref="AutomationLane"/> for every lane-capable parameter, addressable by parameter.
/// Built once for a maximum recording time (the plan's configurable limit, default 60 s); the
/// engine swaps in a new set to change it, so the audio thread never sees a lane resize.
/// </summary>
public sealed class LaneSet
{
    public const float DefaultMaxSeconds = 60f;

    private readonly AutomationLane[] lanes;
    private readonly int[] indexByParameter;

    public LaneSet(float maxSeconds = DefaultMaxSeconds)
    {
        MaxSeconds = maxSeconds;
        indexByParameter = new int[ParameterTable.Count];
        Array.Fill(indexByParameter, -1);
        var list = new List<AutomationLane>();
        foreach (var info in ParameterTable.All)
        {
            if (!info.IsLaneCapable)
                continue;

            indexByParameter[info.Index] = list.Count;
            list.Add(new AutomationLane(info, maxSeconds));
        }

        lanes = list.ToArray();
    }

    public float MaxSeconds { get; }

    public int Count => lanes.Length;

    public AutomationLane this[int index] => lanes[index];

    public IReadOnlyList<AutomationLane> All => lanes;

    public AutomationLane? ForParameter(ParamId id)
    {
        var index = indexByParameter[(int)id];

        return index < 0 ? null : lanes[index];
    }

    public bool AnyPlaying
    {
        get
        {
            foreach (var lane in lanes)
            {
                if (lane.IsPlaying)
                    return true;
            }

            return false;
        }
    }

    internal void RestartAll()
    {
        foreach (var lane in lanes)
            lane.Restart();
    }

    /// <summary>Ticks every lane, writing overriding values into <paramref name="resolved"/>; returns true if any recording started or ended.</summary>
    internal bool Tick(double seconds, float clockSpeed, float barSeconds, ParameterBank bank, float[] resolved)
    {
        var edge = false;
        foreach (var lane in lanes)
        {
            var index = (int)lane.Id;
            if (lane.Tick(seconds, clockSpeed, barSeconds, bank.Get(lane.Id), out var overrides, out var value))
                edge = true;

            if (overrides)
                resolved[index] = value;
        }

        return edge;
    }
}
