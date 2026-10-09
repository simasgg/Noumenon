using Noumenon.Engine.Presets;

namespace Noumenon.Standalone.Dialogs;

/// <summary>Picks a preset from the factory and user banks and a duration, for the snapshot morph.</summary>
internal sealed class PresetPickerDialog : Form
{
    private readonly ListBox list = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly NumericUpDown secondsBox = new() { Minimum = 0.5m, Maximum = 600, DecimalPlaces = 1, Increment = 1, Value = 8, Width = 70 };
    private readonly IReadOnlyList<PresetInfo> presets;

    public PresetPickerDialog(IReadOnlyList<PresetInfo> presets, string title)
    {
        this.presets = presets;
        Text = title;
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(360, 420);

        foreach (var preset in presets)
            list.Items.Add(preset.Name);

        if (list.Items.Count > 0)
            list.SelectedIndex = 0;

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 44, Padding = new Padding(8) };
        var ok = new Button { Text = "Morph", DialogResult = DialogResult.OK, Width = 84 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 84 };
        bottom.Controls.AddRange([cancel, ok, secondsBox, new Label { Text = "over seconds:", AutoSize = true, Padding = new Padding(0, 6, 4, 0) }]);
        list.DoubleClick += (_, _) =>
        {
            if (list.SelectedIndex >= 0)
                DialogResult = DialogResult.OK;
        };

        Controls.Add(list);
        Controls.Add(bottom);
        list.BringToFront();
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public PresetInfo? Selected => list.SelectedIndex >= 0 && list.SelectedIndex < presets.Count ? presets[list.SelectedIndex] : null;

    public float Seconds => (float)secondsBox.Value;
}
