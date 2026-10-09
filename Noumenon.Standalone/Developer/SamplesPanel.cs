using System.Globalization;
using Noumenon.Engine;
using Noumenon.Engine.Parameters;
using Noumenon.Standalone.Audio;

namespace Noumenon.Standalone.Developer;

/// <summary>
/// The sample map page: loads a file into a key (decoding on a worker, never the UI or audio
/// thread), clears a key, selects it for the sampler, and adjusts a slot's gain and root note.
/// The list mirrors the engine's map and refreshes itself when the map changes.
/// </summary>
internal sealed class SamplesPanel : UserControl
{
    private const int RefreshEveryPolls = 10;

    private readonly NoumenonEngine engine;
    private readonly ListView list;
    private readonly NumericUpDown keyBox;
    private readonly NumericUpDown gainBox;
    private readonly NumericUpDown rootBox;
    private readonly Label status;
    private string signature = "";
    private int polls;
    private bool loading;

    public SamplesPanel(NoumenonEngine engine)
    {
        this.engine = engine;
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(4, 6, 4, 0) };
        keyBox = new NumericUpDown { Minimum = 0, Maximum = 127, Width = 52 };
        gainBox = new NumericUpDown { Minimum = 0, Maximum = 4, DecimalPlaces = 2, Increment = 0.05m, Value = 1, Width = 60 };
        rootBox = new NumericUpDown { Minimum = 0, Maximum = 127, Value = 60, Width = 52 };
        var load = new Button { Text = "Load into key…", AutoSize = true };
        var clear = new Button { Text = "Clear key", AutoSize = true };
        var select = new Button { Text = "Select key", AutoSize = true };
        var apply = new Button { Text = "Apply gain / root", AutoSize = true };
        status = new Label { AutoSize = true, Padding = new Padding(8, 5, 0, 0) };
        load.Click += (_, _) => LoadSample();
        clear.Click += (_, _) =>
        {
            engine.Samples.Clear((int)keyBox.Value);
            RefreshList(force: true);
        };
        select.Click += (_, _) => engine.Parameters.Set(ParamId.SamplerSelect, (int)keyBox.Value);
        apply.Click += (_, _) =>
        {
            engine.Samples.Adjust((int)keyBox.Value, (float)gainBox.Value, (int)rootBox.Value);
            RefreshList(force: true);
        };
        bar.Controls.AddRange([Caption("Key"), keyBox, load, clear, select, Caption("Gain"), gainBox, Caption("Root"), rootBox, apply, status]);

        list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false };
        list.Columns.Add("Key", 44);
        list.Columns.Add("Name", 180);
        list.Columns.Add("Rate", 64);
        list.Columns.Add("Length", 80);
        list.Columns.Add("Ch", 36);
        list.Columns.Add("Gain", 52);
        list.Columns.Add("Root", 52);
        list.Columns.Add("Path", 420);
        list.SelectedIndexChanged += (_, _) =>
        {
            if (list.SelectedItems.Count == 0 || list.SelectedItems[0].Tag is not int key || engine.Samples.Get(key) is not { } slot)
                return;

            keyBox.Value = key;
            gainBox.Value = (decimal)Math.Clamp(slot.Gain, 0f, 4f);
            rootBox.Value = slot.RootNote;
        };

        Controls.Add(list);
        Controls.Add(bar);
        list.BringToFront();
    }

    public void Poll()
    {
        if (++polls % RefreshEveryPolls == 0)
            RefreshList(force: false);
    }

    private static Label Caption(string text) => new() { Text = text, AutoSize = true, Padding = new Padding(6, 5, 2, 0) };

    private void LoadSample()
    {
        if (loading)
            return;

        using var dialog = new OpenFileDialog { Filter = MediaFoundationDecoding.FileFilter, Title = "Load a sample" };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        var key = (int)keyBox.Value;
        var path = dialog.FileName;
        var gain = (float)gainBox.Value;
        var root = (int)rootBox.Value;
        loading = true;
        status.Text = "Loading " + Path.GetFileName(path) + "…";
        Task.Run(() => engine.Samples.Load(key, path, gain, root)).ContinueWith(task =>
        {
            loading = false;
            if (IsDisposed)
                return;

            if (task.IsFaulted)
            {
                var message = task.Exception?.GetBaseException().Message ?? "unknown error";
                status.Text = "Failed: " + message;
                MessageBox.Show(this, message, "Sample not loaded", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var slot = task.Result;
            engine.Parameters.Set(ParamId.SamplerSelect, key);
            status.Text = $"Loaded {slot.Name} on key {key} ({slot.Data.Seconds.ToString("0.0", CultureInfo.InvariantCulture)} s)";
            RefreshList(force: true);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void RefreshList(bool force)
    {
        var inv = CultureInfo.InvariantCulture;
        var current = string.Join("|", engine.Samples.Loaded.Select(s => $"{s.Key}:{s.Name}:{s.Gain.ToString(inv)}:{s.RootNote}"));
        if (!force && current == signature)
            return;

        signature = current;
        list.BeginUpdate();
        list.Items.Clear();
        foreach (var slot in engine.Samples.Loaded)
        {
            var item = new ListViewItem(slot.Key.ToString(inv)) { Tag = slot.Key };
            item.SubItems.Add(slot.Name);
            item.SubItems.Add(slot.Data.SampleRate.ToString(inv));
            item.SubItems.Add(slot.Data.Seconds.ToString("0.00", inv) + " s");
            item.SubItems.Add(slot.Data.IsStereo ? "2" : "1");
            item.SubItems.Add(slot.Gain.ToString("0.00", inv));
            item.SubItems.Add(ParamInfo.NoteName(slot.RootNote));
            item.SubItems.Add(slot.Path ?? "(generated)");
            list.Items.Add(item);
        }

        list.EndUpdate();
    }
}
