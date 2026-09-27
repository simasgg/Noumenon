namespace Noumenon.Standalone;

/// <summary>
/// The instrument's main window. Phase 5 fills it with the audio/MIDI device setup, the transport
/// and the developer panel; Phase 6 hosts the shared editor from <c>Noumenon.Ui</c> in it.
/// </summary>
internal sealed class MainForm : Form
{
    private readonly int autoCloseMs;

    public MainForm(int autoCloseMs = 0)
    {
        this.autoCloseMs = autoCloseMs;

        Text = "Noumenon";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1000, 640);
        MinimumSize = new Size(640, 400);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (autoCloseMs <= 0)
            return;

        var timer = new System.Windows.Forms.Timer { Interval = autoCloseMs };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            Close();
        };
        timer.Start();
    }
}
