using Jacobi.Vst.Core;
using Jacobi.Vst.Core.Plugin;
using Jacobi.Vst.Plugin.Framework;
using Jacobi.Vst.Plugin.Framework.Plugin;

namespace Noumenon.Vst2;

/// <summary>
/// The public type VST.NET's bridge scans for and instantiates. It hands the framework the plugin
/// root and swaps in <see cref="NoumenonCommands"/>, which answers the opcodes the framework's
/// program model would get wrong for an instrument whose state is a whole snapshot rather than a
/// list of parameter values. It also pins the <c>IsSynth</c> flag by identity: the framework sets
/// it for any plugin that receives MIDI, but both identities do, and an effect that advertises
/// <c>IsSynth</c> is placed as a generator — FL Studio then feeds it no audio, which is the one
/// thing the FX identity exists to get. So the instrument keeps the flag and the effect clears it.
/// </summary>
public sealed class PluginCommandStub : StdPluginCommandStub
{
    protected override IVstPlugin CreatePluginInstance() => new NoumenonPlugin();

    protected override IVstPluginCommands24 CreatePluginCommands(VstPluginContext pluginCtx) => new NoumenonCommands(pluginCtx);

    protected override VstPluginInfo CreatePluginInfo(IVstPlugin plugin)
    {
        var info = base.CreatePluginInfo(plugin);
        info.Flags = (info.Flags & ~VstPluginFlags.IsSynth) | PluginIdentity.SynthFlag;

        return info;
    }
}
