using System.Globalization;
using Jacobi.Vst.Core;
using Jacobi.Vst.Plugin.Framework;
using Noumenon.Ui.Hosting;
using Noumenon.Vst2.Parameters;

namespace Noumenon.Vst2.Editor;

/// <summary>
/// The VST2 editor: <see cref="PluginEditorHost"/> embeds the shared panel in the window the host
/// provides and serves its presets and samples; this answers the editor opcodes, tells the host
/// about every bank change from the 33 ms poll (with an automation notify, so a host records the
/// panel's moves), runs the randomizer when the engine asks, reports a latency change, and asks
/// the host to resize its window when the panel's scale changes.
/// </summary>
internal sealed class PluginEditor : PluginEditorHost, IVstPluginEditor
{
    private readonly PluginSession session;
    private readonly PluginParameters parameters;
    private readonly AudioProcessor audio;

    public PluginEditor(PluginSession session, PluginParameters parameters, AudioProcessor audio)
        : base(session)
    {
        this.session = session;
        this.parameters = parameters;
        this.audio = audio;
    }

    public Rectangle Bounds => new(Point.Empty, PanelSize);

    public VstKnobMode KnobMode { get; set; } = VstKnobMode.LinearMode;

    public override string StatusText => session.CurrentNotice ?? $"{PluginIdentity.Name} VST2 · {(audio.SampleRate / 1000f).ToString("0.#", CultureInfo.InvariantCulture)} kHz · {audio.BlockSize} smp · {session.Engine.LatencySamples} smp latency";

    public void ProcessIdle()
    {
    }

    public bool KeyDown(byte ascii, VstVirtualKey virtualKey, VstModifierKeys modifiers) => false;

    public bool KeyUp(byte ascii, VstVirtualKey virtualKey, VstModifierKeys modifiers) => false;

    protected override void OnOpened() => parameters.UseAutomation(session.Host?.GetInstance<IVstHostAutomation>());

    protected override void Poll()
    {
        parameters.PushToHost(notify: true);
        if (session.Engine.ConsumeRandomizeRequest())
        {
            session.Randomize();
            session.TellHostDisplayChanged();
        }

        session.PollLatency();
    }

    protected override void OnPanelScaleChanged() => session.Host?.GetInstance<IVstHostShell>()?.SizeWindow(Bounds.Width, Bounds.Height);

    protected override void OnPresetLoaded() => session.TellHostDisplayChanged();

    protected override void OnPresetSaved() => session.TellHostDisplayChanged();
}
