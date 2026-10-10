using Noumenon.Engine;
using Noumenon.Standalone.Audio;
using Noumenon.Standalone.Input;

namespace Noumenon.Standalone.Developer;

/// <summary>
/// The Phase 5 developer panel in its own window now that the editor fills the main one: every
/// parameter as a plain control with lock and lane buttons, the sample map and the monitor.
/// Closing hides it so it reopens instantly; the main window disposes it at exit.
/// </summary>
internal sealed class DeveloperWindow : Form
{
    private readonly DeveloperPanel panel;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 33 };

    public DeveloperWindow(NoumenonEngine engine, MidiHost midi, KeyboardMidi keyboard)
    {
        Text = "Noumenon — developer panel";
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(1000, 680);
        MinimumSize = new Size(820, 400);
        ShowInTaskbar = false;
        panel = new DeveloperPanel(engine, midi, keyboard) { Dock = DockStyle.Fill };
        Controls.Add(panel);
        timer.Tick += (_, _) => panel.Poll();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible)
            timer.Start();
        else
            timer.Stop();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            timer.Dispose();

        base.Dispose(disposing);
    }
}
