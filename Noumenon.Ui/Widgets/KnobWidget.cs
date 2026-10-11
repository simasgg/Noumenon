using Noumenon.Engine;
using Noumenon.Engine.Parameters;

namespace Noumenon.Ui.Widgets;

/// <summary>
/// A rotary control bound to one parameter: drag up or down to turn (Shift for fine), the wheel
/// steps, a double-click restores the default, and a right-click opens the lane menu when the
/// parameter can carry a lane. The pointer shows what the panel set; the yellow arc shows what
/// the engine resolved, so a lane or the matrix moving the value is visible, and while a lane
/// plays the pointer follows it. Under the mouse or while dragged, its value floats where the
/// caption is (beneath the knob when it has none) as a badge the surface paints above every
/// other widget, so a neighbour can neither cover nor clip it; a knob with a readout beside it
/// skips the badge.
/// </summary>
public sealed class KnobWidget : Widget
{
    private const float DragRange = 160f;
    private const float LabelHeight = 11f;
    private const float BadgePad = 3f;

    private readonly NoumenonEngine engine;
    private readonly Func<ParamId> select;
    private readonly Dictionary<ParamId, ParamBinding> bindings = new();
    private readonly string? label;
    private readonly float size;
    private readonly bool labelBelow;
    private readonly bool showValue;
    private bool dragging;
    private float dragStartY;
    private float dragStartNormalized;
    private float shownPointer = float.NaN;
    private float shownArc = float.NaN;
    private int shownLane = -1;
    private ParamId shownId;
    private RectangleF drawnBadge;

    /// <param name="x">Left edge of the knob circle.</param>
    /// <param name="y">Top edge of the knob circle; a caption above it extends the widget upwards.</param>
    /// <param name="showValue">False when a readout beside the knob shows its value, so it shows no badge.</param>
    public KnobWidget(NoumenonEngine engine, ParamId id, float x, float y, float size, string? label = null, bool labelBelow = false, float labelWidth = 0f, bool showValue = true)
        : this(engine, () => id, x, y, size, label, labelBelow, labelWidth, showValue)
    {
    }

    /// <summary>A knob whose parameter depends on engine state, such as an oscillator slot's shape or noise cutoff by waveform.</summary>
    public KnobWidget(NoumenonEngine engine, Func<ParamId> select, float x, float y, float size, string? label = null, bool labelBelow = false, float labelWidth = 0f, bool showValue = true)
    {
        this.engine = engine;
        this.select = select;
        this.size = size;
        this.label = label;
        this.labelBelow = labelBelow || label is null;   // a caption-less knob's badge goes beneath the knob
        this.showValue = showValue;
        var width = Math.Max(size, labelWidth > 0f ? labelWidth : label is null ? size : size + 18f);
        var height = size + (label is null ? 0f : LabelHeight);
        Bounds = new RectangleF(x + size / 2f - width / 2f, y - (this.labelBelow ? 0f : LabelHeight), width, height);
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

    private RectangleF KnobRect => new(Bounds.X + (Bounds.Width - size) / 2f, Bounds.Y + (labelBelow ? 0f : LabelHeight), size, size);

    private RectangleF LabelRect => labelBelow ? new RectangleF(Bounds.X, KnobRect.Bottom, Bounds.Width, LabelHeight) : new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, LabelHeight);

    public override void Draw(Graphics g)
    {
        var pointer = Binding.LanePlaying && !dragging ? Binding.ResolvedNormalized : Binding.Normalized;
        EditorDraw.Knob(g, KnobRect, pointer, Binding.ResolvedNormalized, Binding.IsBipolar, Hot || dragging);

        if (label is not null && !ShowingValue)
            EditorDraw.Text(g, label, LabelRect, EditorTheme.LabelFont, EditorTheme.Text, EditorTheme.Center);

        var lane = Binding.Lane;
        if (lane is null || LaneState(lane) == 0)
            return;

        var state = LaneState(lane);
        var led = new RectangleF(KnobRect.Right - 5f, KnobRect.Bottom - 5f, 5f, 5f);
        EditorDraw.Led(g, led, true, state == 3 ? EditorTheme.Recording : state == 2 ? EditorTheme.Playing : state == 1 ? EditorTheme.Armed : EditorTheme.Loop);
    }

    private bool ShowingValue => showValue && (Hot || dragging);

    public override RectangleF OverlayBounds
    {
        get
        {
            if (!ShowingValue)
                return drawnBadge;

            var badge = BadgeRect(Binding.ValueText);

            return drawnBadge.IsEmpty ? badge : RectangleF.Union(badge, drawnBadge);
        }
    }

    public override void DrawOverlay(Graphics g)
    {
        if (!ShowingValue)
        {
            drawnBadge = RectangleF.Empty;
            return;
        }

        var text = Binding.ValueText;
        var badge = BadgeRect(text);
        EditorDraw.Badge(g, badge, text);
        drawnBadge = badge;
    }

    /// <summary>The value badge: as wide as its text, centred on the knob, in the caption's strip, kept on the panel for the knobs at its edges.</summary>
    private RectangleF BadgeRect(string text)
    {
        var width = MathF.Ceiling(EditorDraw.TextWidth(text, EditorTheme.SmallValueFont)) + 2f * BadgePad;
        var knob = KnobRect;
        var strip = LabelRect;
        var x = knob.X + knob.Width / 2f - width / 2f;
        if (Surface is { } surface)
            x = Math.Clamp(x, 1f, Math.Max(1f, surface.LogicalSize.Width - width - 1f));

        return new RectangleF(x, strip.Y, width, strip.Height);
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
