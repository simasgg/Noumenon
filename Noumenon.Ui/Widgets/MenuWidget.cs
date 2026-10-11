using Noumenon.Engine;
using Noumenon.Engine.Parameters;

namespace Noumenon.Ui.Widgets;

/// <summary>
/// A drop-down readout for a choice parameter (chords, rooms, sources) or a modulation target.
/// Click opens the list; the wheel steps through it. Targets are grouped by panel section so the
/// three hundred parameters stay navigable.
/// </summary>
public sealed class MenuWidget : Widget
{
    private readonly ParamBinding binding;
    private string shown = "";
    private bool down;

    public MenuWidget(NoumenonEngine engine, ParamId id, float x, float y, float width, float height = 14f)
    {
        binding = new ParamBinding(engine, id);
        Bounds = new RectangleF(x, y, width, height);
    }

    public override string? Tooltip => $"{binding.Info.Name}: {binding.ValueText}\nclick: list · wheel: step";

    public override void Draw(Graphics g)
    {
        EditorDraw.Display(g, Bounds);
        EditorDraw.Text(g, binding.ValueText, new RectangleF(Bounds.X + 3f, Bounds.Y, Bounds.Width - 14f, Bounds.Height), EditorTheme.SmallValueFont, down ? EditorTheme.TextLight : EditorTheme.Readout, EditorTheme.Left);
        EditorDraw.Chevron(g, new RectangleF(Bounds.Right - 10f, Bounds.Y + Bounds.Height / 2f - 2f, 6f, 4f), EditorTheme.TraceDim);
    }

    public override bool Update()
    {
        var text = binding.ValueText;
        if (text == shown)
            return false;

        shown = text;

        return true;
    }

    public override void MouseDown(PointF p, MouseButtons button, int clicks)
    {
        if (button != MouseButtons.Left)
            return;

        down = true;
        Invalidate();
        ShowMenu(BuildMenu(), new PointF(Bounds.X, Bounds.Bottom));
    }

    public override void MouseUp(PointF p, MouseButtons button)
    {
        down = false;
        Invalidate();
    }

    public override void MouseWheel(PointF p, int delta)
    {
        Commit(() => binding.Step(delta > 0 ? -1 : 1));
        Invalidate();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip { ShowCheckMargin = true, ShowImageMargin = false };
        var current = (int)MathF.Round(binding.Value);
        if (binding.Info.Unit == Units.Target)
        {
            var none = new ToolStripMenuItem("None") { Checked = current == 0 };
            none.Click += (_, _) => Commit(() => binding.Value = 0f);
            menu.Items.Add(none);
            foreach (var group in ParameterTable.All.GroupBy(info => info.Group))
            {
                var submenu = new ToolStripMenuItem(GroupTitle(group.Key));
                foreach (var info in group)
                {
                    if (info.Kind == ParamKind.Trigger)
                        continue;

                    var target = info.Index + 1;
                    var item = new ToolStripMenuItem(info.Name) { Checked = current == target };
                    item.Click += (_, _) => Commit(() => binding.Value = target);
                    submenu.DropDownItems.Add(item);
                }

                menu.Items.Add(submenu);
            }

            return menu;
        }

        for (var i = 0; i < binding.Info.Choices.Count; i++)
        {
            var index = i;
            var item = new ToolStripMenuItem(binding.Info.Choices[i]) { Checked = current == i };
            item.Click += (_, _) => Commit(() => binding.Value = index);
            menu.Items.Add(item);
        }

        return menu;
    }

    private static string GroupTitle(ParamGroup group) => group switch
    {
        ParamGroup.OscillatorA => "Oscillators A",
        ParamGroup.OscillatorB => "Oscillators B",
        ParamGroup.SectionA => "Section A",
        ParamGroup.SectionB => "Section B",
        ParamGroup.MasterFilter => "Master filter",
        ParamGroup.Spin => "Drift",
        ParamGroup.Resochord => "Resonator",
        ParamGroup.ModWheel => "Mod wheel",
        ParamGroup.PlayMode => "Play mode",
        ParamGroup.ModMatrix => "Modulation",
        _ => group.ToString(),
    };
}
