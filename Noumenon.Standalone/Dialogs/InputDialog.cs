namespace Noumenon.Standalone.Dialogs;

/// <summary>A one-line text prompt for typing an exact value or a name; returns null when cancelled.</summary>
internal static class InputDialog
{
    public static string? Prompt(IWin32Window owner, string title, string label, string initial)
    {
        using var form = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(380, 112),
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
        };

        var caption = new Label { Text = label, Left = 12, Top = 12, Width = 356, Height = 18, AutoEllipsis = true };
        var box = new TextBox { Left = 12, Top = 36, Width = 356, Text = initial };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 206, Top = 74, Width = 78 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 290, Top = 74, Width = 78 };
        form.Controls.AddRange([caption, box, ok, cancel]);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        box.SelectAll();

        return form.ShowDialog(owner) == DialogResult.OK ? box.Text : null;
    }
}
