using System.Globalization;
using Noumenon.Dsp.Shared;
using Noumenon.Engine;
using Noumenon.Engine.Lanes;
using Noumenon.Engine.Parameters;
using Noumenon.Standalone.Dialogs;

namespace Noumenon.Standalone.Developer;

/// <summary>
/// One line of the developer panel: a lock box for the randomizer, the parameter's name, a control
/// that fits its kind (a slider for continuous and integer values, a check box, a drop-down for
/// choices and modulation targets, a button for triggers), the value in real units, and for a
/// lane-capable parameter MF's two lane buttons — Auto plays the recorded loop, Arm makes the next
/// drag record — with the lane's state beside them. The slider writes the bank while it is pressed
/// and follows the resolved value while the lane plays, so faders visibly play themselves; the poll
/// updates every control under a guard so the display never writes back into the engine. The
/// right-click menu clears a lane, fills it with a random loop or sets its speed.
/// </summary>
internal sealed class ParameterRow : UserControl
{
    public const int RowHeight = 26;
    public const int RowWidth = 800;

    private const int SliderSteps = 1000;
    private const float RandomLoopSpread = 0.35f;

    private static readonly ToolTip Tips = new() { AutoPopDelay = 12000, InitialDelay = 400 };
    private static readonly Xoshiro128 LoopSeeds = new((ulong)Environment.TickCount64 ^ 0x5DEECE66DUL);
    private static ContextMenuStrip? laneMenu;
    private static ParameterRow? menuRow;

    private readonly NoumenonEngine engine;
    private readonly ParamInfo info;
    private readonly CheckBox lockBox;
    private readonly Label nameLabel;
    private readonly Label valueLabel;
    private readonly TrackBar? slider;
    private readonly CheckBox? toggle;
    private readonly ComboBox? choice;
    private readonly CheckBox? autoButton;
    private readonly CheckBox? armButton;
    private readonly Label? laneLabel;
    private bool updating;
    private bool pressed;
    private float shownValue = float.NaN;
    private string shownText = "";
    private string shownLane = "";

    public ParameterRow(NoumenonEngine engine, ParamInfo info)
    {
        this.engine = engine;
        this.info = info;
        Height = RowHeight;
        Width = RowWidth;
        Margin = new Padding(0);

        lockBox = new CheckBox { Left = 4, Top = 5, Width = 16, Height = 16 };
        lockBox.CheckedChanged += (_, _) =>
        {
            if (!updating)
                engine.Locks[info.Index] = lockBox.Checked;
        };
        Tips.SetToolTip(lockBox, "Lock: the randomizer leaves this parameter alone");

        nameLabel = new Label { Left = 24, Top = 5, Width = 186, Height = 16, Text = info.Name, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        var range = info.Kind == ParamKind.Choice ? string.Join(" / ", info.Choices) : $"{info.Format(info.Min)} .. {info.Format(info.Max)}, default {info.Format(info.Default)}";
        Tips.SetToolTip(nameLabel, $"{info.Id} (#{info.Index}) · {info.Kind}\n{range}{(info.IsLaneCapable ? "\nlane-capable: Arm, then drag to record; Auto plays the loop" : "")}{(info.IsAddition ? "\nNoumenon addition" : "")}");
        nameLabel.MouseUp += OnRowMouseUp;
        MouseUp += OnRowMouseUp;

        Control control;
        if (info.Unit == Units.Target)
        {
            choice = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            choice.Items.Add("None");
            foreach (var target in ParameterTable.All)
                choice.Items.Add(target.Name);

            choice.SelectedIndexChanged += OnChoiceChanged;
            control = choice;
        }
        else
        {
            switch (info.Kind)
            {
                case ParamKind.Toggle:
                    toggle = new CheckBox { Text = "On", Width = 60, Height = 20 };
                    toggle.CheckedChanged += (_, _) =>
                    {
                        if (!updating)
                            engine.Parameters.Set(info.Id, toggle.Checked);
                    };
                    control = toggle;
                    break;
                case ParamKind.Choice:
                    choice = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
                    foreach (var name in info.Choices)
                        choice.Items.Add(name);

                    choice.SelectedIndexChanged += OnChoiceChanged;
                    control = choice;
                    break;
                case ParamKind.Trigger:
                    var fire = new Button { Text = "Fire", Width = 80, Height = 22 };
                    fire.Click += (_, _) => engine.Parameters.Set(info.Id, 1f);
                    control = fire;
                    break;
                default:
                    slider = new TrackBar { TickStyle = TickStyle.None, AutoSize = false, Height = 24, SmallChange = 1 };
                    if (info.Kind == ParamKind.Integer)
                    {
                        slider.Minimum = (int)info.Min;
                        slider.Maximum = (int)info.Max;
                        slider.LargeChange = Math.Max(1, (int)((info.Max - info.Min) / 20));
                    }
                    else
                    {
                        slider.Minimum = 0;
                        slider.Maximum = SliderSteps;
                        slider.LargeChange = SliderSteps / 20;
                    }

                    slider.ValueChanged += (_, _) =>
                    {
                        if (!updating)
                            engine.Parameters.Set(info.Id, FromSlider(slider.Value));
                    };
                    slider.MouseDown += (_, _) => SetPressed(true);
                    slider.MouseUp += (_, _) => SetPressed(false);
                    slider.MouseCaptureChanged += (_, _) =>
                    {
                        if (!slider.Capture)
                            SetPressed(false);
                    };
                    control = slider;
                    break;
            }
        }

        control.Left = 214;
        control.Top = control is TrackBar ? 1 : 3;
        if (control is not CheckBox and not Button)
            control.Width = 236;

        valueLabel = new Label { Left = 454, Top = 5, Width = 100, Height = 16, TextAlign = ContentAlignment.MiddleRight };
        if (slider is not null)
        {
            valueLabel.Cursor = Cursors.Hand;
            valueLabel.DoubleClick += (_, _) => TypeValue();
            Tips.SetToolTip(valueLabel, "Double-click to type a value");
        }

        Controls.AddRange([lockBox, nameLabel, control, valueLabel]);

        if (info.IsLaneCapable)
        {
            autoButton = new CheckBox { Appearance = Appearance.Button, Text = "Auto", Left = 560, Top = 2, Width = 48, Height = 22, TextAlign = ContentAlignment.MiddleCenter };
            armButton = new CheckBox { Appearance = Appearance.Button, Text = "Arm", Left = 610, Top = 2, Width = 42, Height = 22, TextAlign = ContentAlignment.MiddleCenter };
            laneLabel = new Label { Left = 656, Top = 5, Width = 140, Height = 16, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            Tips.SetToolTip(autoButton, "Automated: the lane's loop replaces the fader");
            Tips.SetToolTip(armButton, "Armed: the next drag of the fader records a loop");
            autoButton.CheckedChanged += (_, _) =>
            {
                if (!updating && Lane is { } lane)
                    lane.Mode = autoButton.Checked ? LaneMode.Automated : LaneMode.Static;
            };
            armButton.CheckedChanged += (_, _) =>
            {
                if (!updating && Lane is { } lane)
                    lane.Armed = armButton.Checked;
            };
            laneLabel.MouseUp += OnRowMouseUp;
            Controls.AddRange([autoButton, armButton, laneLabel]);
        }
    }

    public ParamInfo Info => info;

    private AutomationLane? Lane => engine.Lanes.ForParameter(info.Id);

    /// <summary>UI timer: reflects the bank, the resolved value, the locks and the lane without writing anything back.</summary>
    public void Poll()
    {
        var bank = engine.Parameters;
        var bankValue = bank.Get(info.Id);
        var resolvedValue = engine.Resolved.Get(info.Id);
        var lane = Lane;
        var playing = lane is { IsPlaying: true };
        updating = true;
        try
        {
            var locked = engine.Locks[info.Index];
            if (lockBox.Checked != locked)
                lockBox.Checked = locked;

            if (!pressed)
            {
                var shown = playing ? resolvedValue : bankValue;
                if (shown != shownValue)
                {
                    shownValue = shown;
                    Show(shown);
                }
            }

            var text = info.Format(resolvedValue);
            if (!playing && MathF.Abs(resolvedValue - bankValue) > 1e-5f)
                text += " ~";   // the mod wheel box or the matrix is moving it

            if (text != shownText)
            {
                shownText = text;
                valueLabel.Text = text;
            }

            if (lane is not null && autoButton is not null && armButton is not null && laneLabel is not null)
            {
                var automated = lane.Mode == LaneMode.Automated;
                if (autoButton.Checked != automated)
                    autoButton.Checked = automated;

                if (armButton.Checked != lane.Armed)
                    armButton.Checked = lane.Armed;

                var laneText = LaneText(lane);
                if (laneText != shownLane)
                {
                    shownLane = laneText;
                    laneLabel.Text = laneText;
                }
            }
        }
        finally
        {
            updating = false;
        }
    }

    private static string LaneText(AutomationLane lane)
    {
        var inv = CultureInfo.InvariantCulture;
        string text;
        if (lane.IsRecording)
            text = $"● REC {lane.RecordedSeconds.ToString("0.0", inv)} s";
        else if (lane.IsPlaying)
            text = $"▶ {lane.Phase.ToString("0.0", inv)} / {lane.LengthSeconds.ToString("0.0", inv)} s ×{lane.SpeedMultiplier.ToString("0.00", inv)}";
        else if (lane.HasRecording)
            text = $"{lane.LengthSeconds.ToString("0.0", inv)} s loop";
        else
            text = "no loop";

        return lane.Armed && !lane.IsRecording ? text + " · armed" : text;
    }

    private void Show(float value)
    {
        if (slider is not null)
            slider.Value = Math.Clamp(ToSlider(value), slider.Minimum, slider.Maximum);
        else if (toggle is not null)
            toggle.Checked = value >= 0.5f;
        else if (choice is not null)
            choice.SelectedIndex = Math.Clamp((int)MathF.Round(value), 0, choice.Items.Count - 1);
    }

    private int ToSlider(float value) => info.Kind == ParamKind.Integer ? (int)MathF.Round(value) : (int)MathF.Round(info.ToNormalized(value) * SliderSteps);

    private float FromSlider(int position) => info.Kind == ParamKind.Integer ? position : info.FromNormalized(position / (float)SliderSteps);

    private void OnChoiceChanged(object? sender, EventArgs e)
    {
        if (!updating && choice is not null && choice.SelectedIndex >= 0)
            engine.Parameters.Set(info.Id, choice.SelectedIndex);
    }

    private void SetPressed(bool down)
    {
        if (pressed == down)
            return;

        pressed = down;
        if (Lane is { } lane)
            lane.Pressed = down;
    }

    private void TypeValue()
    {
        var inv = CultureInfo.InvariantCulture;
        var current = engine.Parameters.Get(info.Id);
        var typed = InputDialog.Prompt(this, info.Name, $"Value{(info.Unit.Length > 0 ? " in " + info.Unit : "")}: {info.Format(info.Min)} .. {info.Format(info.Max)}", current.ToString(inv));
        if (typed is null)
            return;

        var token = typed.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (float.TryParse(token, NumberStyles.Float, inv, out var value) || float.TryParse(token, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
            engine.Parameters.Set(info.Id, value);
        else
            MessageBox.Show(this, $"'{typed}' is not a number.", info.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void OnRowMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right || !info.IsLaneCapable)
            return;

        menuRow = this;
        laneMenu ??= BuildLaneMenu();
        laneMenu.Show(Cursor.Position);
    }

    private static ContextMenuStrip BuildLaneMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Clear lane", null, (_, _) => menuRow?.Lane?.Clear());
        menu.Items.Add(new ToolStripSeparator());
        foreach (var seconds in new[] { 4f, 8f, 16f, 32f })
        {
            var length = seconds;
            menu.Items.Add($"Random loop, {length:0} s", null, (_, _) => menuRow?.RandomLoop(length));
        }

        menu.Items.Add(new ToolStripSeparator());
        foreach (var multiplier in new[] { 0.25f, 0.5f, 1f, 2f, 4f })
        {
            var speed = multiplier;
            menu.Items.Add($"Speed ×{speed:0.##}", null, (_, _) =>
            {
                if (menuRow?.Lane is { } lane)
                    lane.SpeedMultiplier = speed;
            });
        }

        return menu;
    }

    private void RandomLoop(float seconds)
    {
        if (Lane is not { } lane)
            return;

        var count = Math.Clamp((int)(seconds * AutomationLane.PointsPerSecond), 2, lane.Capacity);
        var centre = info.ToNormalized(engine.Parameters.Get(info.Id));
        var normalized = Randomizer.RandomLoop(LoopSeeds, count, centre, RandomLoopSpread);
        var points = new float[count];
        for (var i = 0; i < count; i++)
            points[i] = info.FromNormalized(normalized[i]);

        lane.Load(new LaneSnapshot(points, LaneMode.Automated, lane.SpeedMultiplier));
    }
}
