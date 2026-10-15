using Noumenon.Engine.Parameters;

namespace Noumenon.Vst3;

/// <summary>
/// The VST3 module's fixed facts. One module carries both plugins: the instrument
/// (<c>Noumenon</c>, a synth with a stereo output and a MIDI input) and the effect
/// (<c>Noumenon FX</c>, the same with a stereo input feeding Samp↔In, because FL Studio sends no
/// audio into instruments). They share one controller class, one parameter list and one state
/// format, so a project saved with either loads in the other and in the VST2 plugins. The class
/// IDs are permanent: a host stores them in its projects.
///
/// Parameter IDs are permanent too. A table parameter's VST3 ID is its <see cref="ParamId"/> plus
/// one — <see cref="ParamId"/> is append-only, and NPlug reads an ID of 0 as "assign one for me" —
/// and the hidden parameters the host's MIDI controllers are mapped onto (VST3 delivers CC,
/// channel aftertouch and pitch bend as parameter changes, not as events) sit in their own range
/// far above the table, one per controller number.
/// </summary>
internal static class PluginIdentity
{
    public const string Vendor = "Noumenon";
    public const string Url = "";
    public const string Email = "";
    public const string InstrumentName = "Noumenon";
    public const string EffectName = "Noumenon FX";
    public const string ControllerName = "Noumenon Controller";

    public const int ParameterIdOffset = 1;

    /// <summary>The first MIDI-controller parameter: + 0…127 for the CCs, + 128 channel aftertouch, + 129 pitch bend (VST3's controller numbering).</summary>
    public const int MidiControllerIdBase = 0x10000;

    public const int MidiControllerCount = 130;
    public const int AftertouchController = 128;
    public const int PitchBendController = 129;

    public static readonly Version Version = new(0, 1, 0);

    public static readonly Guid InstrumentClassId = new("2968021f-fbe2-4d49-8fd1-1b6820fcad3d");
    public static readonly Guid EffectClassId = new("97b9a08b-f53e-4a95-b403-77164727b89e");
    public static readonly Guid ControllerClassId = new("55df26f4-bada-481c-9f30-3d6ef7992380");

    public static int ToVst3Id(ParamId id) => (int)id + ParameterIdOffset;

    /// <summary>The table parameter behind a VST3 ID, or false for an ID outside the table (a MIDI controller, an unknown one).</summary>
    public static bool TryGetParamId(int vst3Id, out ParamId id)
    {
        var index = vst3Id - ParameterIdOffset;
        id = (ParamId)index;

        return (uint)index < (uint)ParameterTable.Count;
    }

    /// <summary>The MIDI controller number behind a VST3 ID, or -1.</summary>
    public static int MidiControllerOf(int vst3Id)
    {
        var controller = vst3Id - MidiControllerIdBase;

        return (uint)controller < MidiControllerCount ? controller : -1;
    }
}
