using Jacobi.Vst.Plugin.Framework.Plugin;

namespace Noumenon.Vst2;

/// <summary>
/// The framework's opcode handler with five answers replaced. The state chunk is the snapshot
/// JSON (<see cref="PluginState"/>), not the framework's program serialization, whichever of
/// bank or program the host asks for; a parameter's display text is formatted from the table in
/// real units with the unit inside it, so the label stays empty; the program name is the loaded
/// preset's; and a value typed into a host's generic editor is parsed in real units or as a
/// choice name. Everything else — parameters, MIDI, the editor, suspend and resume — reaches the
/// components through the base class as usual.
/// </summary>
internal sealed class NoumenonCommands(VstPluginContext context) : VstPluginCommandsCached(context)
{
    private NoumenonPlugin Plugin => (NoumenonPlugin)PluginContext.Plugin;

    public override byte[] GetChunk(bool isPreset) => Plugin.State.Save();

    public override int SetChunk(byte[] data, bool isPreset) => Plugin.State.Load(data);

    public override string GetParameterDisplay(int index) => Plugin.Parameters.Display(index);

    public override string GetParameterLabel(int index) => "";

    public override bool String2Parameter(int index, string str) => Plugin.Parameters.Parse(index, str);

    public override string GetProgramName() => Plugin.Session.ProgramName;

    public override string GetProgramNameIndexed(int index) => index == 0 ? Plugin.Session.ProgramName : "";

    public override void SetProgramName(string name) => Plugin.Session.SetPreset(name, null);
}
