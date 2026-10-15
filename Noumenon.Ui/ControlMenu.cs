using Noumenon.Dsp.Shared;
using Noumenon.Engine;
using Noumenon.Engine.Lanes;
using Noumenon.Engine.Parameters;
using Noumenon.Ui.Widgets;

namespace Noumenon.Ui;

/// <summary>
/// The right-click menu of every control bound to a parameter. A lane-capable one gets its lane:
/// the mode and arm state, clearing the loop, a smooth random loop of a chosen length around the
/// current value, the playback speed. Every one gets MIDI learn (the next controller moved is
/// bound to it; a bound one shows its CC and can forget it) and the modulation matrix's quick
/// route — "Modulate with" puts a source into the first free slot aimed at this parameter at a
/// quarter depth, and the slots already aimed at it can be removed — and finally its default.
/// Built fresh on each click and owned by the surface that shows it; matrix changes run through
/// the widget's commit so each is one undo step.
/// </summary>
public static class ControlMenu
{
    public const float QuickDepth = 0.25f;

    private static readonly float[] LoopSeconds = [4f, 8f, 16f, 32f];
    private static readonly float[] Speeds = [0.25f, 0.5f, 1f, 2f, 4f];
    private static readonly Xoshiro128 Seeds = new((ulong)Environment.TickCount64 ^ 0x5DEECE66DUL);

    private const float RandomSpread = 0.35f;

    /// <param name="commit">Runs a change as one undoable gesture (the widget's own <c>Commit</c>).</param>
    public static ContextMenuStrip Build(ParamBinding binding, Action<Action> commit)
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

        AddMidiItems(menu, binding);
        if (binding.Info.Kind != ParamKind.Trigger)
            AddModulationItems(menu, binding, commit);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add($"Reset to {binding.Info.Format(binding.Info.Default)}", null, (_, _) => commit(binding.Reset));

        return menu;
    }

    /// <summary>The matrix slots aimed at a parameter, by slot index.</summary>
    public static IEnumerable<int> SlotsTargeting(NoumenonEngine engine, ParamId id)
    {
        var target = (int)id + 1;
        for (var slot = 0; slot < ParameterTable.ModSlotCount; slot++)
        {
            if (engine.Parameters.GetInt(ParameterTable.ModSlot(slot, ModSlotParam.Target)) == target && engine.Parameters.GetInt(ParameterTable.ModSlot(slot, ModSlotParam.Source)) > 0)
                yield return slot;
        }
    }

    private static void AddMidiItems(ContextMenuStrip menu, ParamBinding binding)
    {
        var map = binding.Engine.MidiMap;
        var id = binding.Id;
        if (map.Learning == id)
        {
            menu.Items.Add("Cancel MIDI learn", null, (_, _) => map.CancelLearn());
        }
        else
        {
            var learn = new ToolStripMenuItem("Learn MIDI CC (move a controller next)");
            learn.Click += (_, _) => map.BeginLearn(id);
            menu.Items.Add(learn);
        }

        var controller = map.ControllerFor(id);
        if (controller >= 0)
            menu.Items.Add($"Forget MIDI CC {controller}", null, (_, _) => map.Forget(id));
    }

    private static void AddModulationItems(ContextMenuStrip menu, ParamBinding binding, Action<Action> commit)
    {
        var engine = binding.Engine;
        var bank = engine.Parameters;
        var free = -1;
        for (var slot = 0; slot < ParameterTable.ModSlotCount && free < 0; slot++)
        {
            if (bank.GetInt(ParameterTable.ModSlot(slot, ModSlotParam.Source)) == 0)
                free = slot;
        }

        var modulate = new ToolStripMenuItem(free >= 0 ? "Modulate with" : "Modulate with (the matrix is full)") { Enabled = free >= 0 };
        if (free >= 0)
        {
            for (var source = 1; source < ParameterTable.MatrixSources.Count; source++)
            {
                var chosen = source;
                modulate.DropDownItems.Add(ParameterTable.MatrixSources[source], null, (_, _) => commit(() =>
                {
                    bank.Set(ParameterTable.ModSlot(free, ModSlotParam.Source), chosen);
                    bank.Set(ParameterTable.ModSlot(free, ModSlotParam.Target), (int)binding.Id + 1);
                    bank.Set(ParameterTable.ModSlot(free, ModSlotParam.Depth), QuickDepth);
                }));
            }
        }

        menu.Items.Add(modulate);
        var slots = SlotsTargeting(engine, binding.Id).ToArray();
        if (slots.Length == 0)
            return;

        var names = string.Join(", ", slots.Select(s => ParameterTable.MatrixSources[bank.GetInt(ParameterTable.ModSlot(s, ModSlotParam.Source))]));
        menu.Items.Add($"Remove modulation ({names})", null, (_, _) => commit(() =>
        {
            foreach (var slot in slots)
            {
                bank.Set(ParameterTable.ModSlot(slot, ModSlotParam.Source), 0);
                bank.Set(ParameterTable.ModSlot(slot, ModSlotParam.Target), 0);
                bank.Set(ParameterTable.ModSlot(slot, ModSlotParam.Depth), 0f);
            }
        }));
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
