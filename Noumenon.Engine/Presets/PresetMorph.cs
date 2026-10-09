using Noumenon.Engine.Parameters;

namespace Noumenon.Engine.Presets;

/// <summary>
/// Snapshot morphing: glides every continuous parameter from where it is to a target preset over
/// a number of seconds, in the normalized domain (so cutoffs sweep musically), and flips discrete
/// ones at the halfway point. The endpoints are prepared off the audio thread; the control tick
/// only interpolates and writes the bank, which the smoothers then follow.
/// </summary>
internal sealed class PresetMorph
{
    private float[] fromNormalized = [];
    private float[] toNormalized = [];
    private float[] fromRaw = [];
    private float[] toRaw = [];
    private double seconds;
    private double elapsed;
    private bool active;

    public bool IsActive => Volatile.Read(ref active);

    public float Progress => active ? (float)Math.Min(1.0, elapsed / seconds) : 1f;

    public void Start(float[] from, float[] to, float durationSeconds)
    {
        var all = ParameterTable.All;
        var fromN = new float[all.Count];
        var toN = new float[all.Count];
        for (var i = 0; i < all.Count; i++)
        {
            fromN[i] = all[i].ToNormalized(from[i]);
            toN[i] = all[i].ToNormalized(to[i]);
        }

        fromRaw = from;
        toRaw = to;
        fromNormalized = fromN;
        toNormalized = toN;
        seconds = Math.Max(0.001, durationSeconds);
        elapsed = 0.0;
        Volatile.Write(ref active, true);
    }

    public void Cancel() => Volatile.Write(ref active, false);

    internal void Tick(double dt, ParameterBank bank)
    {
        if (!active)
            return;

        elapsed += dt;
        var t = (float)Math.Min(1.0, elapsed / seconds);
        var all = ParameterTable.All;
        for (var i = 0; i < all.Count; i++)
        {
            var info = all[i];
            if (info.Kind == ParamKind.Trigger)
                continue;

            if (info.IsDiscrete)
            {
                bank.Set(info.Id, t < 0.5f ? fromRaw[i] : toRaw[i]);
                continue;
            }

            bank.Set(info.Id, info.FromNormalized(fromNormalized[i] + (toNormalized[i] - fromNormalized[i]) * t));
        }

        if (t >= 1f)
            Volatile.Write(ref active, false);
    }
}
