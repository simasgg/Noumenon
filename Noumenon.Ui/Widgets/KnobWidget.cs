using Noumenon.Engine;
using Noumenon.Engine.Parameters;

namespace Noumenon.Ui.Widgets;

/// <summary>
/// A rotary control bound to one parameter: drag up or down to turn (Shift for fine), the wheel
/// steps, a double-click restores the default, and a right-click opens the lane menu when the
/// parameter can carry a lane. The pointer shows what the panel set; the yellow arc shows what
/// the engine resolved, so a lane or the matrix moving the value is visible, and while a lane
/// plays the pointer follows it. The caption turns into the value while the mouse is over it.
/// </summary>
public sealed class KnobWidget : Widget
{
    private const float DragRange = 160f;
    private const float LabelHeight = 11f;

    private readonly NoumenonEngine engine;
    private readonly Func<ParamId> select;
    private readonly Dictionary<ParamId, ParamBinding> bindings = new();
    private readonly string? label;
    private readonly float size;
    private readonly bool labelBelow;
    private bool dragging;
    private float dragStartY;
    private float dragStartNormalized;
    private float shownPointer = float.NaN;
    private float shownArc = float.NaN;
    private int shownLane = -1;
    private ParamId shownId;

    /// <param name="x">Left edge of the knob circle.</param>
    /// <param name="y">Top edge of the knob circle; a caption above it extends the widget upwards.</param>
    public KnobWidget(NoumenonEngine engine, ParamId id, float x, float y, float size, string? label = null, bool labelBelow = false, float labelWidth = 0f)
        : this(engine, () => id, x, y, size, label, labelBelow, labelWidth)
    {
    }

    /// <summary>A knob whose parameter depends on engine state, such as an oscillator slot's shape or noise cutoff by waveform.</summary>
    public KnobWidget(NoumenonEngine engine, Func<ParamId> select, float x, float y, float size, string? label = null, bool labelBelow = false, float labelWidth = 0f)
    {
        this.engine = engine;
        this.select = select;
        this.size = size;
        this.label = label;
        this.labelBelow = labelBelow;
        var width = Math.Max(size, labelWidth > 0f ? labelWidth : size + 18f);
        var height = size + (label is null ? 0f : LabelHeight);
        Bounds = new RectangleF(x + size / 2f - width / 2f, y - (label is null || labelBelow ? 0f : LabelHeight), width, height);
    }

    private ParamBinding Binding
    {
        get
        {
            var id = select();
            if (!bindings.TryGetValue(id, out var bound))
            {
                bound = new ParamBinding(engine, id);
                bindings[id] = bound;
            }

            return bound;
        }
    }

    public override string? Tooltip => $"{Binding.Info.Name}: {Binding.ValueText}{(Binding.Lane is null ? "" : "\n" + Binding.LaneText + "\nright-click: lane menu")}\ndouble-click: default";

    private RectangleF KnobRect => new(Bounds.X + (Bounds.Width - size) / 2f, Bounds.Y + (label is null || labelBelow ? 0f : LabelHeight), size, size);

    private RectangleF LabelRect => labelBelow ? new RectangleF(Bounds.X, KnobRect.Bottom, Bounds.Width, LabelHeight) : new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, LabelHeight);

    public override void Draw(Graphics g)
    {
        var pointer = Binding.LanePlaying && !dragging ? Binding.ResolvedNormalized : Binding.Normalized;
        EditorDraw.Knob(g, KnobRect, pointer, Binding.ResolvedNormalized, Binding.IsBipolar, Hot || dragging);

        var showValue = Hot || dragging;
        if (label is not null || showValue)
            EditorDraw.Text(g, showValue ? Binding.ValueText : label!, LabelRect, showValue ? EditorTheme.SmallValueFont : EditorTheme.LabelFont, EditorTheme.Text, EditorTheme.Center);

        var lane = Binding.Lane;
        if (lane is null || LaneState(lane) == 0)
            return;

        var state = LaneState(lane);
        var led = new RectangleF(KnobRect.Right - 5f, KnobRect.Bottom - 5f, 5f, 5f);
        EditorDraw.Led(g, led, true, state == 3 ? EditorTheme.Recording : state == 2 ? EditorTheme.Playing : state == 1 ? EditorTheme.Armed : EditorTheme.Loop);
    }

    public override bool Update()
    {
        var bound = Binding;
        var pointer = bound.LanePlaying && !dragging ? bound.ResolvedNormalized : bound.Normalized;
        var arc = bound.ResolvedNormalized;
        var laneState = bound.Lane is { } lane ? LaneState(lane) : 0;
        if (pointer == shownPointer && arc == shownArc && laneState == shownLane && bound.Id == shownId)
            return false;

        shownPointer = pointer;
        shownArc = arc;
        shownLane = laneState;
        shownId = bound.Id;

        return true;
    }

    public override void MouseDown(PointF p, MouseButtons button, int clicks)
    {
        if (button == MouseButtons.Right)
        {
            if (Binding.Lane is not null)
                ShowMenu(LaneMenu.Build(Binding), p);

            return;
        }

        if (button != MouseButtons.Left)
            return;

        if (clicks >= 2)
        {
            Binding.Reset();
            Invalidate();
            return;
        }

        dragging = true;
        dragStartY = p.Y;
        dragStartNormalized = Binding.Normalized;
        if (Binding.Lane is { } lane)
            lane.Pressed = true;

        Invalidate();
    }

    public override void MouseMove(PointF p, MouseButtons buttons)
    {
        if (!dragging)
            return;

        var delta = (dragStartY - p.Y) / DragRange * (ShiftHeld ? 0.1f : 1f);
        Binding.Normalized = dragStartNormalized + delta;
        Invalidate();
    }

    public override void MouseUp(PointF p, MouseButtons button)
    {
        if (!dragging)
            return;

        dragging = false;
        if (Binding.Lane is { } lane)
            lane.Pressed = false;

        Invalidate();
    }

    public override void MouseWheel(PointF p, int delta)
    {
        Commit(() => Binding.Step(delta > 0 ? 1 : -1));
        Invalidate();
    }

    /// <summary>0 nothing to show, 1 armed, 2 playing, 3 recording, 4 holds a loop but is static.</summary>
    private static int LaneState(Engine.Lanes.AutomationLane lane)
    {
        if (lane.IsRecording)
            return 3;

        if (lane.IsPlaying)
            return 2;

        if (lane.Armed)
            return 1;

        return lane.HasRecording ? 4 : 0;
    }
}
