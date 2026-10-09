using Noumenon.Engine.Recording;
using Noumenon.Standalone.Audio;

namespace Noumenon.Standalone.Dialogs;

/// <summary>
/// Device setup: output API and device, WASAPI share mode, sample rate and buffer, the live
/// input, the MIDI port, the computer keyboard, the lane length limit and the record format
/// and folder. Edits a copy of the settings; the caller applies <see cref="Result"/> on OK and
/// restarts what changed.
/// </summary>
internal sealed class AudioSettingsDialog : Form
{
    private static readonly int[] Rates = [0, 44100, 48000, 88200, 96000];
    private static readonly int[] Latencies = [5, 10, 20, 30, 50, 100, 200];
    private static readonly int[] LaneLimits = [30, 60, 120, 300];

    private readonly AudioHost host;
    private readonly ComboBox apiBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox outputBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox exclusiveBox = new() { Text = "Exclusive mode (WASAPI)", AutoSize = true };
    private readonly ComboBox rateBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox latencyBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox inputBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox asioInputBox = new() { Text = "Feed ASIO inputs 1–2 to Samp↔In", AutoSize = true };
    private readonly ComboBox midiBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox keyboardBox = new() { Text = "Computer keyboard plays notes when no text box has focus", AutoSize = true };
    private readonly ComboBox laneBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox formatBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox folderBox = new();
    private readonly Button asioPanelButton = new() { Text = "ASIO control panel…", AutoSize = true };
    private IReadOnlyList<DeviceEntry> outputs = [];
    private IReadOnlyList<DeviceEntry> inputs = [];
    private IReadOnlyList<DeviceEntry> midis = [];

    public AudioSettingsDialog(HostSettings current, AudioHost host)
    {
        this.host = host;
        Result = current.Clone();
        Text = "Audio & MIDI settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(560, 470);

        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12), AutoScroll = false };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        apiBox.Items.AddRange(["WASAPI", "ASIO"]);
        apiBox.SelectedIndex = current.Api == OutputApi.Asio ? 1 : 0;
        apiBox.SelectedIndexChanged += (_, _) => FillOutputs();

        foreach (var rate in Rates)
            rateBox.Items.Add(rate == 0 ? "Device default" : rate + " Hz");

        rateBox.SelectedIndex = Math.Max(0, Array.IndexOf(Rates, current.SampleRate));

        foreach (var latency in Latencies)
            latencyBox.Items.Add(latency + " ms");

        latencyBox.SelectedIndex = NearestIndex(Latencies, current.LatencyMs);
        exclusiveBox.Checked = current.Exclusive;
        asioInputBox.Checked = current.AsioInput;
        keyboardBox.Checked = current.KeyboardMidi;

        foreach (var limit in LaneLimits)
            laneBox.Items.Add(limit + " s");

        laneBox.SelectedIndex = NearestIndex(LaneLimits, (int)current.LaneMaxSeconds);
        formatBox.Items.AddRange(["24-bit PCM", "32-bit float"]);
        formatBox.SelectedIndex = current.RecordFormat == WavSampleFormat.Float32 ? 1 : 0;
        folderBox.Text = current.RecordFolder ?? "";

        inputs = AudioDevices.WasapiInputs();
        inputBox.Items.Add("(none)");
        foreach (var entry in inputs)
            inputBox.Items.Add(entry.Name);

        inputBox.SelectedIndex = 1 + IndexOf(inputs, current.InputDevice);
        if (inputBox.SelectedIndex < 0)
            inputBox.SelectedIndex = 0;

        midis = AudioDevices.MidiInputs();
        midiBox.Items.Add("(none)");
        foreach (var entry in midis)
            midiBox.Items.Add(entry.Name);

        midiBox.SelectedIndex = 1 + IndexOf(midis, current.MidiDevice);
        if (midiBox.SelectedIndex < 0)
            midiBox.SelectedIndex = 0;

        asioPanelButton.Enabled = host.CanShowAsioPanel;
        asioPanelButton.Click += (_, _) => host.ShowAsioControlPanel();

        var browse = new Button { Text = "Browse…", AutoSize = true };
        browse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { Description = "Recordings folder", InitialDirectory = folderBox.Text };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                folderBox.Text = dialog.SelectedPath;
        };
        var folderRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0) };
        folderBox.Width = 290;
        folderRow.Controls.AddRange([folderBox, browse]);

        AddRow(table, "Output API", apiBox);
        AddRow(table, "Output device", outputBox);
        AddRow(table, "", exclusiveBox);
        AddRow(table, "Sample rate", rateBox);
        AddRow(table, "Buffer (WASAPI)", latencyBox);
        AddRow(table, "", asioPanelButton);
        AddRow(table, "Live input (WASAPI)", inputBox);
        AddRow(table, "", asioInputBox);
        AddRow(table, "MIDI input", midiBox);
        AddRow(table, "", keyboardBox);
        AddRow(table, "Lane length limit", laneBox);
        AddRow(table, "Record format", formatBox);
        AddRow(table, "Record folder", folderRow);
        AddRow(table, "", new Label { Text = "Leave the folder empty for Documents\\Noumenon\\Recordings. Changing the lane limit clears every lane.", AutoSize = true, MaximumSize = new Size(380, 0), ForeColor = SystemColors.GrayText });

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 44, Padding = new Padding(8) };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 84 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 84 };
        buttons.Controls.AddRange([cancel, ok]);
        AcceptButton = ok;
        CancelButton = cancel;
        ok.Click += (_, _) => ReadBack();

        Controls.Add(table);
        Controls.Add(buttons);
        FillOutputs();
    }

    public HostSettings Result { get; }

    private static void AddRow(TableLayoutPanel table, string caption, Control control)
    {
        var row = table.RowCount;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(new Label { Text = caption, AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, row);
        if (control is ComboBox)
            control.Width = 360;

        control.Margin = new Padding(0, 3, 0, 3);
        table.Controls.Add(control, 1, row);
        table.RowCount++;
    }

    private static int IndexOf(IReadOnlyList<DeviceEntry> entries, string? id)
    {
        if (id is null)
            return -1;

        for (var i = 0; i < entries.Count; i++)
        {
            if (string.Equals(entries[i].Id, id, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static int NearestIndex(int[] values, int value)
    {
        var best = 0;
        for (var i = 1; i < values.Length; i++)
        {
            if (Math.Abs(values[i] - value) < Math.Abs(values[best] - value))
                best = i;
        }

        return best;
    }

    private void FillOutputs()
    {
        var asio = apiBox.SelectedIndex == 1;
        outputs = asio ? AudioDevices.AsioDrivers() : AudioDevices.WasapiOutputs();
        outputBox.Items.Clear();
        if (!asio)
            outputBox.Items.Add("(system default)");

        foreach (var entry in outputs)
            outputBox.Items.Add(entry.Name);

        var wanted = Result.Api == (asio ? OutputApi.Asio : OutputApi.Wasapi) ? Result.OutputDevice : null;
        var index = IndexOf(outputs, wanted);
        outputBox.SelectedIndex = asio ? Math.Max(0, index) : index + 1;
        if (outputBox.Items.Count == 0)
            outputBox.Items.Add(asio ? "(no ASIO drivers installed)" : "(no output devices)");

        if (outputBox.SelectedIndex < 0 && outputBox.Items.Count > 0)
            outputBox.SelectedIndex = 0;

        exclusiveBox.Enabled = !asio;
        latencyBox.Enabled = !asio;
        inputBox.Enabled = !asio;
        asioInputBox.Enabled = asio;
    }

    private void ReadBack()
    {
        var asio = apiBox.SelectedIndex == 1;
        Result.Api = asio ? OutputApi.Asio : OutputApi.Wasapi;
        var outputIndex = asio ? outputBox.SelectedIndex : outputBox.SelectedIndex - 1;
        Result.OutputDevice = outputIndex >= 0 && outputIndex < outputs.Count ? outputs[outputIndex].Id : null;
        Result.Exclusive = exclusiveBox.Checked;
        Result.SampleRate = Rates[Math.Max(0, rateBox.SelectedIndex)];
        Result.LatencyMs = Latencies[Math.Max(0, latencyBox.SelectedIndex)];
        var inputIndex = inputBox.SelectedIndex - 1;
        Result.InputDevice = inputIndex >= 0 && inputIndex < inputs.Count ? inputs[inputIndex].Id : null;
        Result.AsioInput = asioInputBox.Checked;
        var midiIndex = midiBox.SelectedIndex - 1;
        Result.MidiDevice = midiIndex >= 0 && midiIndex < midis.Count ? midis[midiIndex].Id : null;
        Result.KeyboardMidi = keyboardBox.Checked;
        Result.LaneMaxSeconds = LaneLimits[Math.Max(0, laneBox.SelectedIndex)];
        Result.RecordFormat = formatBox.SelectedIndex == 1 ? WavSampleFormat.Float32 : WavSampleFormat.Pcm24;
        Result.RecordFolder = string.IsNullOrWhiteSpace(folderBox.Text) ? null : folderBox.Text.Trim();
    }
}
