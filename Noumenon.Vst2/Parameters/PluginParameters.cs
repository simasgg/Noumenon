using System.Globalization;
using Jacobi.Vst.Plugin.Framework;
using Noumenon.Engine;
using Noumenon.Engine.Parameters;

namespace Noumenon.Vst2.Parameters;

/// <summary>
/// The host-facing parameter list, one entry per <see cref="ParamId"/> in enum order, generated
/// from <see cref="ParameterTable"/>. The host speaks normalized 0–1 and the bank speaks real
/// units, so every value crosses here through <see cref="ParamInfo.ToNormalized"/> and
/// <see cref="ParamInfo.FromNormalized"/>: a logarithmic cutoff automates evenly in octaves and
/// a choice snaps to its entries. Traffic runs both ways. The audio thread pulls what the host
/// wrote into the framework's parameter objects into the bank at the start of each block; the
/// editor's poll pushes what the panel, a preset, the randomizer or the engine itself changed in
/// the bank back to those objects and notifies the host so automation records. One array of the
/// normalized values both sides last agreed on keeps them from echoing each other, and a value
/// the host set is never "corrected" back to it as long as it still means the same bank value.
/// </summary>
internal sealed class PluginParameters
{
    private const float Epsilon = 1e-5f;

    private readonly NoumenonEngine engine;
    private readonly float[] hostValues;
    private VstParameterCollection? active;
    private IVstHostAutomation? automation;

    public PluginParameters(NoumenonEngine engine)
    {
        this.engine = engine;
        var all = ParameterTable.All;
        hostValues = new float[all.Count];
        var categories = new Dictionary<ParamGroup, VstParameterCategory>();
        for (var i = 0; i < all.Count; i++)
        {
            var p = all[i];
            if (!categories.TryGetValue(p.Group, out var category))
            {
                category = new VstParameterCategory { Name = CategoryName(p.Group) };
                categories[p.Group] = category;
                Categories.Add(category);
            }

            var info = new VstParameterInfo
            {
                Category = category,
                CanBeAutomated = true,
                Name = p.ShortName,
                Label = "",
                ShortLabel = "",
                DefaultValue = p.ToNormalized(p.Default),
                IsSwitch = p.Kind is ParamKind.Toggle or ParamKind.Trigger,
            };

            if (p.IsDiscrete)
            {
                info.MinInteger = (int)p.Min;
                info.MaxInteger = (int)p.Max;
                info.StepInteger = 1;
                info.LargeStepInteger = Math.Max(1, (int)(p.Max - p.Min) / 10);
            }
            else
            {
                info.StepFloat = 0.01f;
                info.SmallStepFloat = 0.001f;
                info.LargeStepFloat = 0.1f;
            }

            Infos.Add(info);
            hostValues[i] = info.DefaultValue;
        }
    }

    public VstParameterCategoryCollection Categories { get; } = [];

    public VstParameterInfoCollection Infos { get; } = [];

    /// <summary>The number of parameters the host sees; the same as the table's.</summary>
    public int Count => hostValues.Length;

    /// <summary>
    /// Binds the program the framework activated: its parameter objects are what the host reads
    /// and writes from now on. The bank is the truth at that moment, so it is pushed into them
    /// (a state chunk may have arrived before the program existed) without automation events.
    /// </summary>
    public void Attach(VstParameterCollection parameters)
    {
        for (var i = 0; i < hostValues.Length && i < parameters.Count; i++)
            hostValues[i] = parameters[i].Value;

        Volatile.Write(ref active, parameters);
        PushToHost(notify: false);
    }

    public void UseAutomation(IVstHostAutomation? hostAutomation) => automation = hostAutomation;

    /// <summary>Audio thread, once per block: what the host set since the last block goes into the bank. Allocation-free.</summary>
    public void PullFromHost()
    {
        var parameters = Volatile.Read(ref active);
        if (parameters is null)
            return;

        var all = ParameterTable.All;
        var bank = engine.Parameters;
        for (var i = 0; i < hostValues.Length; i++)
        {
            var v = parameters[i].Value;
            if (v == hostValues[i])
                continue;

            hostValues[i] = v;
            bank.Set(all[i].Id, all[i].FromNormalized(v));
        }
    }

    /// <summary>
    /// UI thread: what changed in the bank since the host last heard goes to the host's parameter
    /// objects; with <paramref name="notify"/> the host is told too, which it does not want while
    /// it is loading state itself. Returns how many parameters were pushed.
    /// </summary>
    public int PushToHost(bool notify)
    {
        var parameters = Volatile.Read(ref active);
        if (parameters is null)
            return 0;

        var pushed = 0;
        var all = ParameterTable.All;
        var bank = engine.Parameters;
        for (var i = 0; i < hostValues.Length; i++)
        {
            var p = all[i];
            var value = bank.Get(p.Id);
            var h = hostValues[i];
            if (p.IsDiscrete ? p.FromNormalized(h) == value : MathF.Abs(p.ToNormalized(value) - h) <= Epsilon)
                continue;

            var n = p.ToNormalized(value);
            hostValues[i] = n;
            var parameter = parameters[i];
            parameter.Value = n;
            pushed++;
            if (notify)
                automation?.NotifyParameterValueChanged(parameter);
        }

        return pushed;
    }

    /// <summary>The display text for a parameter, from the host's own value when the program is live so it matches what the host just set.</summary>
    public string Display(int index)
    {
        if ((uint)index >= (uint)hostValues.Length)
            return "";

        var p = ParameterTable.All[index];
        var parameters = Volatile.Read(ref active);
        var real = parameters is null ? engine.Parameters.Get(p.Id) : p.FromNormalized(parameters[index].Value);

        return p.Format(real);
    }

    /// <summary>A value typed into a host's generic editor: a number in real units (a unit suffix is ignored) or a choice name.</summary>
    public bool Parse(int index, string? text)
    {
        if ((uint)index >= (uint)hostValues.Length || string.IsNullOrWhiteSpace(text))
            return false;

        var p = ParameterTable.All[index];
        var trimmed = text.Trim();
        float value;
        if (p.Kind == ParamKind.Choice)
        {
            var choice = -1;
            for (var i = 0; i < p.Choices.Count; i++)
            {
                if (string.Equals(p.Choices[i], trimmed, StringComparison.OrdinalIgnoreCase))
                    choice = i;
            }

            if (choice < 0 && !int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out choice))
                return false;

            value = choice;
        }
        else if (p.Kind is ParamKind.Toggle or ParamKind.Trigger)
        {
            if (trimmed.Equals("on", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("fire", StringComparison.OrdinalIgnoreCase) || trimmed == "1")
                value = 1f;
            else if (trimmed.Equals("off", StringComparison.OrdinalIgnoreCase) || trimmed == "0" || trimmed == "-")
                value = 0f;
            else
                return false;
        }
        else
        {
            var end = 0;
            while (end < trimmed.Length && (char.IsDigit(trimmed[end]) || trimmed[end] is '-' or '+' or '.' or ',' || (end > 0 && trimmed[end] is 'e' or 'E')))
                end++;

            if (end == 0 || !float.TryParse(trimmed[..end].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return false;

            if (p.Unit == Units.Hertz && trimmed.EndsWith("kHz", StringComparison.OrdinalIgnoreCase))
                value *= 1000f;
            else if (p.Unit == Units.Percent)
                value /= 100f;
        }

        engine.Parameters.Set(p.Id, value);
        PushToHost(notify: false);

        return true;
    }

    private static string CategoryName(ParamGroup group) => group switch
    {
        ParamGroup.OscillatorA => "Oscillators A",
        ParamGroup.OscillatorB => "Oscillators B",
        ParamGroup.SectionA => "Section A",
        ParamGroup.SectionB => "Section B",
        ParamGroup.Pitch => "Pitch",
        ParamGroup.Mix => "Mix",
        ParamGroup.Sampler => "Sampler",
        ParamGroup.MasterFilter => "Master Filter",
        ParamGroup.Distortion => "Distortion",
        ParamGroup.Eq => "EQ",
        ParamGroup.Spin => "Drift",
        ParamGroup.Resochord => "Resonator",
        ParamGroup.Reverb => "Reverb",
        ParamGroup.Output => "Output",
        ParamGroup.Scope => "Scope",
        ParamGroup.Post => "Post",
        ParamGroup.Transport => "Transport",
        ParamGroup.PlayMode => "Play Mode",
        ParamGroup.ModWheel => "Mod Wheel",
        ParamGroup.Global => "Global",
        ParamGroup.ModMatrix => "Mod Matrix",
        _ => group.ToString(),
    };
}
