namespace Noumenon.Engine.Parameters;

/// <summary>
/// What the engine actually uses each control tick: the bank's static values with the mod-wheel
/// box, the lanes and the modulation matrix applied. The audio thread rewrites it every tick;
/// the panel reads it to draw faders that play themselves and knobs that a modulator is moving.
/// </summary>
public sealed class ResolvedParameters
{
    private readonly float[] values = ParameterTable.CreateDefaults();

    internal float[] Raw => values;

    public float Get(ParamId id) => Volatile.Read(ref values[(int)id]);

    public bool GetBool(ParamId id) => Get(id) >= 0.5f;

    public int GetInt(ParamId id) => (int)MathF.Round(Get(id));
}
