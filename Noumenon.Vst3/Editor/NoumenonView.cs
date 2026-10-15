using System.Globalization;
using NPlug;
using Noumenon.Ui.Hosting;

namespace Noumenon.Vst3.Editor;

/// <summary>
/// The VST3 editor view the controller creates each time the host opens the editor:
/// <see cref="PluginEditorHost"/> embeds the shared panel in the host's window and serves its
/// presets and samples; this answers the plug-view calls and runs the poll on the controller —
/// the panel's edits go to the host (as one touch per mouse gesture), the randomizer runs when the
/// engine asks, a latency change is reported, and a preset loaded, a randomize or a finished
/// snapshot morph is announced as a whole new snapshot. The size is fixed at the panel's scale (1×, 1.5×, 2×); choosing another
/// scale on the panel asks the host's frame to resize. The first time a view opens in an instance,
/// the host's content scale factor (its DPI) picks the nearest panel scale; after that the user's
/// choice, saved with the project by the controller, stays.
/// </summary>
internal sealed class NoumenonView : PluginEditorHost, IAudioPluginView
{
    private readonly NoumenonController controller;
    private readonly PluginSession session;
    private IAudioPluginFrame? frame;
    private bool hostScaling;
    private bool wasMorphing;

    public NoumenonView(NoumenonController controller, PluginSession session)
        : base(session)
    {
        this.controller = controller;
        this.session = session;
        PanelScale = controller.PanelScale;
    }

    public ViewRectangle Size => new(0, 0, PanelSize.Width, PanelSize.Height);

    public override string StatusText => session.CurrentNotice ?? $"{session.Name} VST3 · {(session.SampleRate / 1000.0).ToString("0.#", CultureInfo.InvariantCulture)} kHz · {session.MaxBlockSize} smp · {session.Engine.LatencySamples} smp latency";

    public bool IsPlatformTypeSupported(AudioPluginViewPlatform platform) => platform == AudioPluginViewPlatform.Hwnd;

    public void Attached(nint parent, AudioPluginViewPlatform type) => Open(parent);

    public void Removed() => Close();

    public void OnWheel(float distance)
    {
    }

    public void OnKeyDown(ushort key, short keyCode, short modifiers)
    {
    }

    public void OnKeyUp(ushort key, short keyCode, short modifiers)
    {
    }

    public void OnSize(ViewRectangle newSize)
    {
    }

    public void OnFocus(bool state)
    {
    }

    public void SetFrame(IAudioPluginFrame frame) => this.frame = frame;

    public bool CanResize() => false;

    public bool CheckSizeConstraint(ref ViewRectangle rect)
    {
        rect = new ViewRectangle(rect.Left, rect.Top, rect.Left + PanelSize.Width, rect.Top + PanelSize.Height);

        return true;
    }

    /// <summary>The host's DPI scale; it picks the panel scale only until the user has chosen one.</summary>
    public void SetContentScaleFactor(float factor)
    {
        if (controller.PanelScaleChosen || factor <= 0f)
            return;

        var scale = factor < 1.25f ? 1f : factor < 1.75f ? 1.5f : 2f;
        controller.PanelScale = scale;
        hostScaling = true;
        try
        {
            PanelScale = scale;
        }
        finally
        {
            hostScaling = false;
        }
    }

    public bool TryFindParameter(int xPos, int yPos, out AudioParameterId parameterId)
    {
        parameterId = default;

        return false;
    }

    protected override void Poll()
    {
        // A morph moves hundreds of values at once: no edits while it glides, then one silent
        // sync and a restart when it lands, as for a preset load.
        var morphing = session.Engine.IsMorphing;
        if (!morphing && wasMorphing)
            controller.SnapshotChanged();
        else if (!morphing)
            controller.PushChanges();

        wasMorphing = morphing;
        if (session.Engine.ConsumeRandomizeRequest())
        {
            session.Randomize();
            controller.SnapshotChanged();
        }

        controller.PollLatency();
    }

    protected override void OnPanelScaleChanged()
    {
        controller.PanelScale = PanelScale;
        if (!hostScaling)
            controller.PanelScaleChosen = true;

        frame?.ResizeView(this, Size);
    }

    protected override void OnPresetLoaded() => controller.SnapshotChanged();

    /// <summary>The host closes the editor: the last changes go out and any edit a gesture left open is closed.</summary>
    protected override void OnClosing() => controller.GestureEnded();

    protected override void OnGestureStarted() => controller.GestureStarted();

    protected override void OnGestureEnded() => controller.GestureEnded();
}
