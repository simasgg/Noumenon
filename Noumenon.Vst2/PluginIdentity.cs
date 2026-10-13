using Jacobi.Vst.Core;

namespace Noumenon.Vst2;

/// <summary>
/// Which of the two plugins this assembly is. The sources are compiled twice: as the instrument
/// (<c>Noumenon</c>, a Synth with two outputs that reads MIDI) and, with <c>NOUMENON_FX</c>
/// defined, as the effect (<c>Noumenon FX</c>, the same with two inputs feeding Samp↔In, because
/// FL Studio sends no audio into generators). Everything else is identical, down to the state
/// chunk, so a snapshot saved from one loads in the other and each accepts the other's chunk ID.
/// The four-character IDs are the ones noted in the licensing checklist.
/// </summary>
internal static class PluginIdentity
{
    public const string Vendor = "Noumenon";
    public const string Product = "Noumenon";
    public const int OutputCount = 2;

    /// <summary>In VST.NET's thousands notation (1100 = 1.1.0.0); 0.1.0 is 100.</summary>
    public const int Version = 100;

    public const int InstrumentId = ('N' << 24) | ('o' << 16) | ('u' << 8) | 'm';
    public const int EffectId = ('N' << 24) | ('o' << 16) | ('u' << 8) | 'F';

#if NOUMENON_FX
    public const bool IsEffect = true;
    public const string Name = "Noumenon FX";
    public const int Id = EffectId;
    public const int InputCount = 2;
    public const VstPluginCategory Category = VstPluginCategory.Effect;
#else
    public const bool IsEffect = false;
    public const string Name = "Noumenon";
    public const int Id = InstrumentId;
    public const int InputCount = 0;
    public const VstPluginCategory Category = VstPluginCategory.Synth;
#endif

    /// <summary>The IsSynth flag this identity advertises: set for the instrument, cleared for the effect so a host routes audio into it.</summary>
    public const VstPluginFlags SynthFlag = IsEffect ? VstPluginFlags.None : VstPluginFlags.IsSynth;

    public static bool IsOurChunk(int pluginId) => pluginId == InstrumentId || pluginId == EffectId;
}
