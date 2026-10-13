using Jacobi.Vst.Plugin.Framework;
using Jacobi.Vst.Plugin.Framework.Plugin;

namespace Noumenon.Vst2.Parameters;

/// <summary>
/// A single program. The state a host saves and restores is a whole snapshot — lanes, chords,
/// samples, scale, seed — which VST2's parameter-only programs cannot carry, so presets stay the
/// editor's business and the chunk is the state; the program exists because the framework
/// serves the active program's parameters to the host. Its name is answered from the session
/// (the loaded preset) by <see cref="NoumenonCommands"/>.
/// </summary>
internal sealed class PluginPrograms(PluginParameters parameters) : VstPluginPrograms
{
    public override VstProgram? ActiveProgram
    {
        get => base.ActiveProgram;
        set
        {
            base.ActiveProgram = value;
            if (value is not null)
                parameters.Attach(value.Parameters);
        }
    }

    protected override VstProgramCollection CreateProgramCollection()
    {
        var program = new VstProgram(parameters.Categories) { Name = "Snapshot" };
        foreach (var info in parameters.Infos)
            program.Parameters.Add(new VstParameter(info));

        return [program];
    }
}
