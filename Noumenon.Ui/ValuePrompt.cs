using System.Globalization;
using Noumenon.Engine.Parameters;

namespace Noumenon.Ui;

/// <summary>Types an exact value for a parameter (a double-click on a readout); null when cancelled or not a number.</summary>
public static class ValuePrompt
{
    public static float? Show(IWin32Window? owner, ParamInfo info, float current)
    {
        using var form = new Form
        {
            Text = info.Name,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = owner is null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent,
            ClientSize = new Size(320, 108),
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
        };

        var caption = new Label { Text = $"{info.Format(info.Min)} .. {info.Format(info.Max)}{(info.Unit.Length > 0 ? ", in " + info.Unit : "")}", Left = 12, Top = 12, Width = 296, Height = 18 };
        var box = new TextBox { Left = 12, Top = 36, Width = 296, Text = current.ToString(CultureInfo.InvariantCulture) };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 146, Top = 72, Width = 78 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 230, Top = 72, Width = 78 };
        form.Controls.AddRange([caption, box, ok, cancel]);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        box.SelectAll();

        var result = owner is null ? form.ShowDialog() : form.ShowDialog(owner);
        if (result != DialogResult.OK)
            return null;

        var token = box.Text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || float.TryParse(token, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
            return value;

        return null;
    }
}
