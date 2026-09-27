namespace Noumenon.Standalone;

/// <summary>
/// Entry point of the standalone instrument. Phase 5 turns this into the NAudio host (device
/// selection, MIDI in, transport, record and bounce); until then it opens the empty main window so
/// the solution has a runnable Windows target. <c>--smoke</c> closes the window by itself after a
/// moment, so the app can be launched from a script to prove it starts.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var autoCloseMs = Array.IndexOf(args, "--smoke") >= 0 ? 1500 : 0;
        using var window = new MainForm(autoCloseMs);
        Application.Run(window);
    }
}
