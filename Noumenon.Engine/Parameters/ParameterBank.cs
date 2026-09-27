namespace Noumenon.Engine.Parameters;

/// <summary>
/// The live parameter values — one <see cref="float"/> per <see cref="ParamId"/>, in real units. The
/// UI and the host write it from their threads and the audio thread reads it at control rate, so every
/// access is a <see cref="Volatile"/> read or write of a single float: no locks, no allocation, and a
/// torn value is impossible. Writes are clamped to the table's range, so the engine never sees an
/// out-of-range value.
/// </summary>
public sealed class ParameterBank
{
    private readonly float[] values = ParameterTable.CreateDefaults();

    public int Count => values.Length;

    public float Get(ParamId id) => Volatile.Read(ref values[(int)id]);

    public bool GetBool(ParamId id) => Get(id) >= 0.5f;

    public int GetInt(ParamId id) => (int)MathF.Round(Get(id));

    public void Set(ParamId id, float value) => Volatile.Write(ref values[(int)id], ParameterTable.Get(id).Clamp(value));

    public void Set(ParamId id, bool on) => Set(id, on ? 1f : 0f);

    public void Set(ParamId id, int value) => Set(id, (float)value);

    public float GetNormalized(ParamId id) => ParameterTable.Get(id).ToNormalized(Get(id));

    public void SetNormalized(ParamId id, float normalized) => Set(id, ParameterTable.Get(id).FromNormalized(normalized));

    public void ResetToDefaults()
    {
        var all = ParameterTable.All;
        for (var i = 0; i < all.Count; i++)
            Volatile.Write(ref values[i], all[i].Default);
    }

    /// <summary>Snapshot every value into <paramref name="destination"/> (sized <see cref="Count"/>).</summary>
    public void CopyTo(float[] destination)
    {
        for (var i = 0; i < values.Length; i++)
            destination[i] = Volatile.Read(ref values[i]);
    }

    /// <summary>Load every value from <paramref name="source"/> (sized <see cref="Count"/>), clamped.</summary>
    public void CopyFrom(ReadOnlySpan<float> source)
    {
        var all = ParameterTable.All;
        for (var i = 0; i < values.Length; i++)
            Volatile.Write(ref values[i], all[i].Clamp(source[i]));
    }
}
