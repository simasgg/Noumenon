using System.Globalization;

namespace Noumenon.Engine.Parameters;

/// <summary>How a parameter's value behaves and how hosts should present it.</summary>
public enum ParamKind
{
    /// <summary>A real value between <see cref="ParamInfo.Min"/> and <see cref="ParamInfo.Max"/>.</summary>
    Continuous,

    /// <summary>A whole number between Min and Max (a map key, a MIDI note, a seed).</summary>
    Integer,

    /// <summary>0 = off, 1 = on.</summary>
    Toggle,

    /// <summary>One entry of <see cref="ParamInfo.Choices"/>; the value is the entry's index.</summary>
    Choice,

    /// <summary>Momentary: writing 1 fires the action once and the engine writes it back to 0.</summary>
    Trigger,
}

/// <summary>How a <see cref="ParamKind.Continuous"/> value maps onto the host's normalized 0-1 range.</summary>
public enum ParamScale
{
    Linear,

    /// <summary>Equal ratios per step (frequencies, times, gain multipliers). Needs Min &gt; 0.</summary>
    Logarithmic,
}

/// <summary>The panel section a parameter belongs to; the developer panel and the editor group by it.</summary>
public enum ParamGroup
{
    OscillatorA,
    OscillatorB,
    SectionA,
    SectionB,
    Pitch,
    Mix,
    Sampler,
    MasterFilter,
    Distortion,
    Eq,
    Spin,
    Resochord,
    Reverb,
    Output,
    Scope,
    Post,
    Transport,
    PlayMode,
    ModWheel,
    Global,
    ModMatrix,
}

[Flags]
public enum ParamFlags
{
    None = 0,

    /// <summary>Can carry a recorded/random automation lane (every MF "automatable fader" has this).</summary>
    Lane = 1,

    /// <summary>A Noumenon addition: not in Metaphysical Function or Fabrications.</summary>
    Addition = 2,
}

/// <summary>
/// One row of <see cref="ParameterTable"/>: everything every layer needs to know about a parameter
/// — its identity, panel names (VST2 caps host-facing names at 8 characters, hence
/// <see cref="ShortName"/>), range, default, kind and the normalized 0-1 mapping the plugin wrappers
/// use. Values are stored and exchanged in real units (Hz, semitones, dB, …); only the host boundary
/// speaks normalized.
/// </summary>
public sealed class ParamInfo
{
    public const int MaxShortNameLength = 8;

    public ParamInfo(ParamId id, ParamGroup group, string name, string shortName, ParamKind kind, ParamScale scale, float min, float max, float defaultValue, string unit, IReadOnlyList<string>? choices, ParamFlags flags)
    {
        if (shortName.Length > MaxShortNameLength)
            throw new ArgumentException($"{id}: short name '{shortName}' exceeds {MaxShortNameLength} characters.", nameof(shortName));

        if (max < min)
            throw new ArgumentException($"{id}: max {max} is below min {min}.", nameof(max));

        if (scale == ParamScale.Logarithmic && min <= 0f)
            throw new ArgumentException($"{id}: a logarithmic range needs a positive minimum.", nameof(min));

        if (kind == ParamKind.Choice && (choices is null || choices.Count < 2 || max != choices.Count - 1))
            throw new ArgumentException($"{id}: a choice parameter needs at least two choices and max = count - 1.", nameof(choices));

        if (defaultValue < min || defaultValue > max)
            throw new ArgumentException($"{id}: default {defaultValue} is outside [{min}, {max}].", nameof(defaultValue));

        Id = id;
        Group = group;
        Name = name;
        ShortName = shortName;
        Kind = kind;
        Scale = scale;
        Min = min;
        Max = max;
        Default = defaultValue;
        Unit = unit;
        Choices = choices ?? [];
        Flags = flags;
    }

    public ParamId Id { get; }
    public int Index => (int)Id;
    public ParamGroup Group { get; }
    public string Name { get; }
    public string ShortName { get; }
    public ParamKind Kind { get; }
    public ParamScale Scale { get; }
    public float Min { get; }
    public float Max { get; }
    public float Default { get; }
    public string Unit { get; }
    public IReadOnlyList<string> Choices { get; }
    public ParamFlags Flags { get; }

    public bool IsLaneCapable => (Flags & ParamFlags.Lane) != 0;
    public bool IsAddition => (Flags & ParamFlags.Addition) != 0;
    public bool IsDiscrete => Kind is ParamKind.Integer or ParamKind.Toggle or ParamKind.Choice or ParamKind.Trigger;

    /// <summary>The number of steps a discrete parameter has minus one (VST3's stepCount); 0 for continuous.</summary>
    public int StepCount => IsDiscrete ? (int)(Max - Min) : 0;

    public float Clamp(float value)
    {
        if (float.IsNaN(value))
            return Default;

        var v = value < Min ? Min : value > Max ? Max : value;

        return IsDiscrete ? MathF.Round(v) : v;
    }

    public float ToNormalized(float value)
    {
        var v = Clamp(value);
        if (Max <= Min)
            return 0f;

        if (Kind == ParamKind.Continuous && Scale == ParamScale.Logarithmic)
            return MathF.Log(v / Min) / MathF.Log(Max / Min);

        return (v - Min) / (Max - Min);
    }

    public float FromNormalized(float normalized)
    {
        var n = float.IsNaN(normalized) ? 0f : normalized < 0f ? 0f : normalized > 1f ? 1f : normalized;
        if (Kind == ParamKind.Continuous && Scale == ParamScale.Logarithmic)
            return Min * MathF.Pow(Max / Min, n);

        return Clamp(Min + n * (Max - Min));
    }

    /// <summary>The value as the panel and hosts display it, e.g. "12.0 kHz", "+7 st", "On", "Saw".</summary>
    public string Format(float value)
    {
        var v = Clamp(value);
        var inv = CultureInfo.InvariantCulture;
        switch (Kind)
        {
            case ParamKind.Toggle:
                return v >= 0.5f ? "On" : "Off";
            case ParamKind.Trigger:
                return v >= 0.5f ? "Fire" : "-";
            case ParamKind.Choice:
                return Choices[(int)v];
        }

        switch (Unit)
        {
            case Units.Hertz:
                return v >= 1000f ? (v / 1000f).ToString("0.00", inv) + " kHz" : v.ToString("0.0", inv) + " Hz";
            case Units.Milliseconds:
                return v.ToString("0.0", inv) + " ms";
            case Units.Seconds:
                return v.ToString("0.00", inv) + " s";
            case Units.Decibels:
                return v <= Min && Min <= Units.SilenceDb ? "-inf dB" : v.ToString("+0.0;-0.0;0.0", inv) + " dB";
            case Units.Semitones:
                return Kind == ParamKind.Integer ? v.ToString("+0;-0;0", inv) + " st" : v.ToString("+0.00;-0.00;0.00", inv) + " st";
            case Units.Cents:
                return v.ToString("+0;-0;0", inv) + " ct";
            case Units.Multiplier:
                return v.ToString("0.00", inv) + "x";
            case Units.Percent:
                return (v * 100f).ToString("0", inv) + " %";
            case Units.Note:
                return NoteName((int)v);
            case Units.Target:
                return v <= 0f ? "None" : ParameterTable.Get((ParamId)((int)v - 1)).Name;
            default:
                return Kind == ParamKind.Integer ? v.ToString("0", inv) : v.ToString("0.00", inv);
        }
    }

    private static readonly string[] NoteNames = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    /// <summary>MIDI note number → name with the octave convention where 60 = C4.</summary>
    public static string NoteName(int midiNote)
    {
        var octave = midiNote / 12 - 1;

        return NoteNames[midiNote % 12] + octave.ToString(CultureInfo.InvariantCulture);
    }
}

/// <summary>The unit strings <see cref="ParamInfo.Unit"/> uses; <see cref="ParamInfo.Format"/> switches on them.</summary>
public static class Units
{
    public const string None = "";
    public const string Hertz = "Hz";
    public const string Milliseconds = "ms";
    public const string Seconds = "s";
    public const string Decibels = "dB";
    public const string Semitones = "st";
    public const string Cents = "ct";
    public const string Multiplier = "x";
    public const string Percent = "%";
    public const string Note = "note";
    public const string Target = "target";

    /// <summary>A decibel range whose minimum is at or below this reads "-inf" at the bottom and means silence.</summary>
    public const float SilenceDb = -60f;
}
