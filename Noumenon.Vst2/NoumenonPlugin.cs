using System.Runtime;
using Jacobi.Vst.Plugin.Framework;
using Jacobi.Vst.Plugin.Framework.Plugin;
using Microsoft.Extensions.DependencyInjection;
using Noumenon.Dsp.Effects;
using Noumenon.Engine;
using Noumenon.Ui.Hosting;
using Noumenon.Vst2.Editor;
using Noumenon.Vst2.Parameters;

namespace Noumenon.Vst2;

/// <summary>
/// The plugin root: one <see cref="NoumenonEngine"/>, the <see cref="PluginSession"/> the host
/// side shares, and the components the framework resolves from the DI container — the audio
/// processor (also the MIDI sink and the bypass), the parameter bridge with its single program,
/// the state chunk and the editor. They are created here, in a fixed order, and registered as
/// instances, so each can hold the others it needs. The latency the host is told is the
/// engine's, which changes with Engine Rate, so <see cref="IVstPlugin.InitialDelay"/> is
/// answered live rather than fixed at construction; the session asks the host to re-read it.
/// </summary>
internal sealed class NoumenonPlugin : VstPluginWithServices, IVstPlugin
{
    public NoumenonPlugin()
        : base(PluginIdentity.Name, PluginIdentity.Id, new VstProductInfo(PluginIdentity.Product, PluginIdentity.Vendor, PluginIdentity.Version), PluginIdentity.Category, Distortion.Latency, VstPluginCapabilities.ReceiveTimeInfo)
    {
        GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
        MediaFoundationDecoding.Register();
        Session = new PluginSession();
        Parameters = new PluginParameters(Session.Engine);
        Programs = new PluginPrograms(Parameters);
        State = new PluginState(Session, Parameters);
        Audio = new AudioProcessor(Session, Parameters);
        Editor = new PluginEditor(Session, Parameters, State, Audio);
    }

    public PluginSession Session { get; }

    public NoumenonEngine Engine => Session.Engine;

    public PluginParameters Parameters { get; }

    public PluginPrograms Programs { get; }

    public PluginState State { get; }

    public AudioProcessor Audio { get; }

    public PluginEditor Editor { get; }

    int IVstPlugin.InitialDelay => Engine.LatencySamples;

    public override void Open(IVstHost host)
    {
        base.Open(host);
        Session.Opened(host);
        if (Programs.ActiveProgram is { } program)
            Parameters.Attach(program.Parameters);
    }

    public override void Suspend()
    {
        Engine.Midi.AllNotesOff();
        base.Suspend();
    }

    public override void Resume()
    {
        base.Resume();
        Audio.Resumed();
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingletonAll(Parameters);
        services.AddSingletonAll(Programs);
        services.AddSingletonAll(State);
        services.AddSingletonAll(Audio);
        services.AddSingletonAll(Editor);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Editor.Close();

        base.Dispose(disposing);
    }
}
