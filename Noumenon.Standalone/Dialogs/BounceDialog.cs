using System.Globalization;
using Noumenon.Engine;
using Noumenon.Engine.Presets;
using Noumenon.Engine.Recording;
using Noumenon.Engine.Samples;
using Noumenon.Standalone.Audio;

namespace Noumenon.Standalone.Dialogs;

/// <summary>
/// The bounce: renders the current snapshot for a number of minutes at any rate to a WAV on a
/// worker thread, faster than real time, with progress and a cancel. The live engine keeps
/// playing; the snapshot is captured the moment Start is pressed.
/// </summary>
internal sealed class BounceDialog : Form
{
    private static readonly int[] Rates = [44100, 48000, 88200, 96000];

    private readonly NoumenonEngine engine;
    private readonly HostSettings settings;
    private readonly string presetName;
    private readonly TextBox pathBox = new() { Width = 380 };
    private readonly NumericUpDown minutesBox = new() { Minimum = 0.1m, Maximum = 240, DecimalPlaces = 1, Increment = 0.5m, Value = 1, Width = 70 };
    private readonly ComboBox rateBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly ComboBox formatBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly ProgressBar progress = new() { Width = 460, Maximum = 1000 };
    private readonly Label result = new() { AutoSize = true, MaximumSize = new Size(460, 0) };
    private readonly Button startButton = new() { Text = "Start", Width = 90 };
    private readonly Button cancelButton = new() { Text = "Close", Width = 90 };
    private CancellationTokenSource? cancellation;

    public BounceDialog(NoumenonEngine engine, HostSettings settings, int currentRate, string presetName)
    {
        this.engine = engine;
        this.settings = settings;
        this.presetName = presetName;
        Text = "Bounce to WAV";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(500, 250);

        var folder = settings.EffectiveRecordFolder;
        pathBox.Text = Path.Combine(folder, $"{SafeName(presetName)} {DateTime.Now:yyyy-MM-dd HH-mm-ss}.wav");
        foreach (var rate in Rates)
            rateBox.Items.Add(rate + " Hz");

        rateBox.SelectedIndex = Math.Max(0, Array.IndexOf(Rates, currentRate > 0 ? currentRate : 48000));
        if (rateBox.SelectedIndex < 0)
            rateBox.SelectedIndex = 1;

        formatBox.Items.AddRange(["24-bit PCM", "32-bit float"]);
        formatBox.SelectedIndex = settings.RecordFormat == WavSampleFormat.Float32 ? 1 : 0;

        var browse = new Button { Text = "Browse…", AutoSize = true };
        browse.Click += (_, _) =>
        {
            using var dialog = new SaveFileDialog { Filter = "WAV files|*.wav", FileName = Path.GetFileName(pathBox.Text), InitialDirectory = Path.GetDirectoryName(pathBox.Text) };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                pathBox.Text = dialog.FileName;
        };

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var pathRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        pathRow.Controls.AddRange([pathBox, browse]);
        var optionsRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        optionsRow.Controls.AddRange([minutesBox, Caption("minutes at"), rateBox, Caption("as"), formatBox]);
        AddRow(layout, "File", pathRow);
        AddRow(layout, "Length", optionsRow);
        AddRow(layout, "Tempo", new Label { Text = $"{settings.Tempo.ToString("0.#", CultureInfo.InvariantCulture)} BPM from the transport (Host Sync and Restart On Host Play see a playing host)", AutoSize = true, MaximumSize = new Size(380, 0) });
        AddRow(layout, "Progress", progress);
        AddRow(layout, "", result);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 44, Padding = new Padding(8) };
        buttons.Controls.AddRange([cancelButton, startButton]);
        startButton.Click += (_, _) => Start();
        cancelButton.Click += (_, _) =>
        {
            if (cancellation is { } running)
                running.Cancel();
            else
                Close();
        };

        Controls.Add(layout);
        Controls.Add(buttons);
        AcceptButton = startButton;
    }

    private static Label Caption(string text) => new() { Text = text, AutoSize = true, Padding = new Padding(4, 6, 4, 0) };

    private static void AddRow(TableLayoutPanel table, string caption, Control control)
    {
        var row = table.RowCount;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(new Label { Text = caption, AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, row);
        control.Margin = new Padding(0, 3, 0, 3);
        table.Controls.Add(control, 1, row);
        table.RowCount++;
    }

    private static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();

        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }

    private void Start()
    {
        if (cancellation is not null)
            return;

        var path = pathBox.Text.Trim();
        if (path.Length == 0)
            return;

        var seconds = (double)minutesBox.Value * 60.0;
        var rate = Rates[Math.Max(0, rateBox.SelectedIndex)];
        var format = formatBox.SelectedIndex == 1 ? WavSampleFormat.Float32 : WavSampleFormat.Pcm24;
        var state = engine.CapturePreset(presetName);
        SampleSlot[] slots = [..engine.Samples.Loaded];
        var tempo = settings.Tempo;
        var cts = new CancellationTokenSource();
        cancellation = cts;
        startButton.Enabled = false;
        cancelButton.Text = "Cancel";
        progress.Value = 0;
        result.Text = "Rendering…";
        var reporter = new Progress<float>(p => progress.Value = Math.Clamp((int)(p * 1000), 0, 1000));

        Task.Run(() => OfflineRenderer.Render(state, slots, path, seconds, rate, format, tempo, reporter, cts.Token), cts.Token).ContinueWith(task =>
        {
            cancellation = null;
            cts.Dispose();
            if (IsDisposed)
                return;

            startButton.Enabled = true;
            cancelButton.Text = "Close";
            if (task.IsCanceled)
            {
                result.Text = "Cancelled; the partial file was deleted.";
                progress.Value = 0;
            }
            else if (task.IsFaulted)
            {
                result.Text = "Failed: " + (task.Exception?.GetBaseException().Message ?? "unknown error");
            }
            else
            {
                var inv = CultureInfo.InvariantCulture;
                var r = task.Result;
                result.Text = $"Wrote {r.Path}\n{r.Seconds.ToString("0.0", inv)} s in {r.Elapsed.TotalSeconds.ToString("0.0", inv)} s ({r.RealTimeFactor.ToString("0.0", inv)}× real time), peak {r.Peak.ToString("0.000", inv)}{(r.Randomizations > 0 ? $", {r.Randomizations} randomizations" : "")}";
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (cancellation is { } running)
        {
            running.Cancel();
            e.Cancel = true;
            return;
        }

        base.OnFormClosing(e);
    }
}
