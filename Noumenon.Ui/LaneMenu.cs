using Noumenon.Dsp.Shared;
using Noumenon.Engine;
using Noumenon.Engine.Lanes;
using Noumenon.Ui.Widgets;

namespace Noumenon.Ui;

/// <summary>
/// The right-click menu of every lane-capable control: the lane's mode and arm state, clearing
/// the loop, filling it with a smooth random loop of a chosen length around the current value,
/// the playback speed, and the value's default. Built fresh on each click and owned by the surface that shows it.
/// </summary>
public static class LaneMenu
{
    private static readonly float[] LoopSeconds = [4f, 8f, 16f, 32f];
    private static readonly float[] Speeds = [0.25f, 0.5f, 1f, 2f, 4f];
    private static readonly Xoshiro128 Seeds = new((ulong)Environment.TickCount64 ^ 0x5DEECE66DUL);

    private const float RandomSpread = 0.35f;

    public static ContextMenuStrip Build(ParamBinding binding)
    {
        var menu = new ContextMenuStrip { ShowCheckMargin = true, ShowImageMargin = false };
        var lane = binding.Lane;
        menu.Items.Add(new ToolStripMenuItem(binding.Info.Name) { Enabled = false });
        if (lane is not null)
        {
            var automated = new ToolStripMenuItem("Automated (play the loop)") { Checked = lane.Mode == LaneMode.Automated };
            automated.Click += (_, _) => lane.Mode = lane.Mode == LaneMode.Automated ? LaneMode.Static : LaneMode.Automated;
            var armed = new ToolStripMenuItem("Armed (next drag records)") { Checked = lane.Armed };
            armed.Click += (_, _) => lane.Armed = !lane.Armed;
            menu.Items.Add(automated);
            menu.Items.Add(armed);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Clear loop", null, (_, _) => lane.Clear());
            foreach (var seconds in LoopSeconds)
            {
                var length = seconds;
                menu.Items.Add($"Random loop, {length:0} s", null, (_, _) => RandomLoop(binding, lane, length));
            }

            menu.Items.Add(new ToolStripSeparator());
            foreach (var speed in Speeds)
            {
                var multiplier = speed;
                var item = new ToolStripMenuItem($"Speed ×{multiplier:0.##}") { Checked = MathF.Abs(lane.SpeedMultiplier - multiplier) < 0.001f };
                item.Click += (_, _) => lane.SpeedMultiplier = multiplier;
                menu.Items.Add(item);
            }

            menu.Items.Add(new ToolStripSeparator());
        }

        menu.Items.Add($"Reset to {binding.Info.Format(binding.Info.Default)}", null, (_, _) => binding.Reset());

        return menu;
    }

    private static void RandomLoop(ParamBinding binding, AutomationLane lane, float seconds)
    {
        var count = Math.Clamp((int)(seconds * AutomationLane.PointsPerSecond), 2, lane.Capacity);
        var centre = binding.Info.ToNormalized(binding.Value);
        var normalized = Randomizer.RandomLoop(Seeds, count, centre, RandomSpread);
        var points = new float[count];
        for (var i = 0; i < count; i++)
            points[i] = binding.Info.FromNormalized(normalized[i]);

        lane.Load(new LaneSnapshot(points, LaneMode.Automated, lane.SpeedMultiplier));
    }
}
