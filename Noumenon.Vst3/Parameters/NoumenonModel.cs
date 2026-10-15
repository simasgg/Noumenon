using NPlug;
using Noumenon.Engine.Parameters;

namespace Noumenon.Vst3.Parameters;

/// <summary>
/// The parameter list both the processor and the controller build (NPlug gives each its own copy):
/// every <see cref="ParameterTable"/> row, grouped into one unit per panel section so a host's
/// generic editor and automation menus read like the panel, then the hidden parameters the host's
/// MIDI controllers are mapped onto — a VST3 host turns CC, channel aftertouch and pitch bend into
/// parameter changes on whatever parameter the plugin names for them. Pitch bend rests at the
/// centre. The engine's bank stays the truth; these objects only carry what the host reads and
/// writes.
/// </summary>
internal sealed class NoumenonModel : AudioProcessorModel
{
    public NoumenonModel()
        : base("Noumenon")
    {
        var table = ParameterTable.All;
        var units = new Dictionary<ParamGroup, AudioUnit>();
        var parameters = new NoumenonParameter[table.Count];
        foreach (var info in table)
        {
            if (!units.TryGetValue(info.Group, out var unit))
            {
                unit = AddUnit(new AudioUnit(ParameterTable.GroupName(info.Group)));
                units.Add(info.Group, unit);
            }

            parameters[info.Index] = unit.AddParameter(new NoumenonParameter(info));
        }

        var midi = AddUnit(new AudioUnit("MIDI Controllers"));
        var controllers = new AudioParameter[PluginIdentity.MidiControllerCount];
        for (var controller = 0; controller < controllers.Length; controller++)
        {
            var name = controller switch
            {
                PluginIdentity.AftertouchController => "Channel Aftertouch",
                PluginIdentity.PitchBendController => "Pitch Bend",
                _ => $"MIDI CC {controller}",
            };

            controllers[controller] = midi.AddParameter(new AudioParameter(name, id: PluginIdentity.MidiControllerIdBase + controller, defaultNormalizedValue: controller == PluginIdentity.PitchBendController ? 0.5 : 0.0, flags: AudioParameterFlags.IsHidden));
        }

        TableParameters = parameters;
        MidiControllers = controllers;
    }

    /// <summary>Indexed by <see cref="ParamId"/>.</summary>
    public IReadOnlyList<NoumenonParameter> TableParameters { get; }

    /// <summary>Indexed by VST3 controller number (0–127 CC, 128 aftertouch, 129 pitch bend).</summary>
    public IReadOnlyList<AudioParameter> MidiControllers { get; }

    /// <summary>
    /// Puts every value back to its default after <see cref="AudioProcessorModel.Initialize"/>, which
    /// (NPlug 0.5) points each parameter at its zero-filled slot before copying the value into it, so
    /// every parameter would otherwise start at 0.
    /// </summary>
    public void ResetValues()
    {
        foreach (var parameter in TableParameters)
            parameter.NormalizedValue = parameter.DefaultNormalizedValue;

        foreach (var controller in MidiControllers)
            controller.NormalizedValue = controller.DefaultNormalizedValue;
    }
}
