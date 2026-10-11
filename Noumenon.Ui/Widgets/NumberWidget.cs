using Noumenon.Engine;
using Noumenon.Engine.Parameters;

namespace Noumenon.Ui.Widgets;

/// <summary>
/// A numeric readout bound to one parameter, as MF shows pitches and fine offsets: drag up or
/// down to change it (whole steps for integers, Shift for fine), the wheel steps, a double-click
/// restores the default and the right-click menu types an exact value. Shows the resolved value,
/// so a lane on a pitch readout is visible, and takes a caption to its left when asked.
/// </summary>
public sealed class NumberWidget : Widget
{
    private const float DragPixelsPerStep = 3f;
    private const float DragPixelsForRange = 300f;

    private readonly ParamBinding binding;
    private readonly string? label;
    private readonly float labelWidth;
    private bool dragging;
    private float dragStartY;
    private float dragStartValue;
    private string shown = "";

    public NumberWidget(NoumenonEngine engine, ParamId id, float x, float y, float width, float height = 12f, string? label = null, float labelWidth = 0f)
    {
        binding = new ParamBinding(engine, id);
        this.label = label;
        this.labelWidth = label is null ? 0f : labelWidth;
        Bounds = new RectangleF(x - this.labelWidth, y, width + this.labelWidth, height);
    }

    public override string? Tooltip => $"{binding.Info.Name}: {binding.ValueText}{(binding.Lane is null ? "" : "\n" + binding.LaneText)}\ndrag: change · wheel: step · double-click: default\nright-click: {(binding.Lane is null ? "type a value" : "lane menu, type a value")}";

    private RectangleF Box => new(Bounds.X + labelWidth, Bounds.Y, Bounds.Width - labelWidth, Bounds.Height);

    public override void Draw(Graphics g)
    {
        if (label is not null)
            EditorDraw.Text(g, label, new RectangleF(Bounds.X, Bounds.Y, labelWidth - 3f, Bounds.Height), EditorTheme.LabelFont, EditorTheme.Text, EditorTheme.Right);

        var box = Box;
        EditorDraw.Display(g, box);
        EditorDraw.Text(g, binding.ResolvedText, RectangleF.Inflate(box, -2f, 0f), EditorTheme.SmallValueFont, Hot || dragging ? EditorTheme.TextLight : EditorTheme.Readout, EditorTheme.Center);
    }

    public override bool Update()
    {
        var text = binding.ResolvedText;
        if (text == shown)
            return false;

        shown = text;

        return true;
    }

    public override void MouseDown(PointF p, MouseButtons button, int clicks)
    {
        if (button == MouseButtons.Right)
        {
            var menu = LaneMenu.Build(binding);
            menu.Items.Insert(menu.Items.Count - 1, new ToolStripMenuItem("Type a value…", null, (_, _) => TypeValue()));
            ShowMenu(menu, p);
            return;
        }

        if (button != MouseButtons.Left)
            return;

        if (clicks >= 2)
        {
            binding.Reset();
            Invalidate();
            return;
        }

        dragging = true;
        dragStartY = p.Y;
        dragStartValue = binding.Value;
        if (binding.Lane is { } lane)
            lane.Pressed = true;

        Invalidate();
    }

    public override void MouseMove(PointF p, MouseButtons buttons)
    {
        if (!dragging)
            return;

        var pixels = dragStartY - p.Y;
        var info = binding.Info;
        binding.Value = info.IsDiscrete
            ? dragStartValue + (int)(pixels / DragPixelsPerStep)
            : dragStartValue + pixels / DragPixelsForRange * (info.Max - info.Min) * (ShiftHeld ? 0.1f : 1f);
        Invalidate();
    }

    public override void MouseUp(PointF p, MouseButtons button)
    {
        if (!dragging)
            return;

        dragging = false;
        if (binding.Lane is { } lane)
            lane.Pressed = false;

        Invalidate();
    }

    public override void MouseWheel(PointF p, int delta)
    {
        Commit(() => binding.Step(delta > 0 ? 1 : -1));
        Invalidate();
    }

    private void TypeValue()
    {
        var typed = ValuePrompt.Show(Surface, binding.Info, binding.Value);
        if (typed is { } value)
            Commit(() => binding.Value = value);

        Invalidate();
    }
}
