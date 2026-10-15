using Jacobi.Vst.Core;
using Jacobi.Vst.Plugin.Framework;
using Noumenon.Engine.Hosting;
using Noumenon.Vst2.Parameters;

namespace Noumenon.Vst2;

/// <summary>
/// The VST2 side of a plugin instance's session; <see cref="PluginSessionBase"/> holds the engine,
/// the presets, the notices and the state. This adds the host services once the plugin is open,
/// the program name VST2 can show and the latency the host was last told, and pushes a snapshot
/// applied to the engine — a chunk, a preset — into the host's parameter objects without
/// automation events.
///
/// There is deliberately no free-running background thread here. The randomizer allocates, so it
/// cannot run on the audio thread; the editor's 33 ms poll runs it when the GUI is open, exactly
/// as the standalone's always-present UI timer does. A Randomize trigger the host automates while
/// the GUI is closed stays latched on the engine (<see cref="Engine.NoumenonEngine.ConsumeRandomizeRequest"/>)
/// and is applied the next time the editor opens — a plugin with no window has no UI thread to run
/// it on, and a per-instance managed timer is exactly the kind of cross-plugin background execution
/// that makes loading a second .NET bridge into the host process fragile.
/// </summary>
internal sealed class PluginSession : PluginSessionBase
{
    private IVstHost? host;
    private int reportedLatency;

    public PluginSession()
        : base(FolderOf(typeof(PluginSession)))
    {
        reportedLatency = Engine.LatencySamples;
    }

    public IVstHost? Host => Volatile.Read(ref host);

    /// <summary>The host-facing parameter list, set once by the plugin root.</summary>
    public PluginParameters? Parameters { get; set; }

    /// <summary>The preset name cut to what VST2 allows for a program name.</summary>
    public string ProgramName => PresetName.Length <= Constants.MaxProgramNameLength ? PresetName : PresetName[..Constants.MaxProgramNameLength];

    public void Opened(IVstHost host) => Volatile.Write(ref this.host, host);

    /// <summary>Tells the host the program name changed, from the UI thread only.</summary>
    public void TellHostDisplayChanged() => Host?.GetInstance<IVstHostShell>()?.UpdateDisplay();

    /// <summary>UI thread: when Engine Rate changed the latency, asks the host to re-read it.</summary>
    public void PollLatency()
    {
        var latency = Engine.LatencySamples;
        if (latency == reportedLatency)
            return;

        reportedLatency = latency;
        Host?.GetInstance<IVstHostSequencer>()?.UpdatePluginIO();
    }

    protected override void OnSnapshotApplied() => Parameters?.PushToHost(notify: false);
}
