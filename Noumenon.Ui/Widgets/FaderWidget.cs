using Noumenon.Engine;
using Noumenon.Engine.Lanes;
using Noumenon.Engine.Parameters;

namespace Noumenon.Ui.Widgets;

/// <summary>
/// A slider bound to one parameter, vertical or horizontal. When the parameter can carry a lane
/// it gets MF's two extras: an automation button (lit while the lane is Automated and the loop
/// replaces the fader) and the position display, which is clicked to arm — the next drag then
/// records, the display fills red while it does, and shows the loop position in green while it
/// plays. Dragging sets the lane's Pressed flag so the engine records exactly while the cap is
/// held; while a lane plays and the cap is not held, the cap follows the resolved value. The
/// wheel steps, a double-click restores the default, a right-click opens the lane menu.
/// </summary>
public sealed class FaderWidget : Widget
{
    public const float Column = 16f;

    private const float SlotThickness = 6f;
    private const float CapLong = 10f;
    private const float CapShort = 4f;
    private const float LabelHeight = 11f;
    private const float AutoButton = 8f;
    private const float Strip = 5f;
    private const float Gap = 2f;
    private const float LaneBlock = 12f;

    private readonly ParamBinding binding;
    private readonly bool vertical;
    private readonly float length;
    private readonly string? label;
    private bool dragging;
    private float grabOffset;
    private float shownValue = float.NaN;
    private int shownLane = -1;

    /// <param name="x">Left of the slot (vertical) or of the slot's start (horizontal).</param>
    /// <param name="y">Top of the slot; a caption above a vertical fader extends the widget upwards.</param>
    public FaderWidget(NoumenonEngine engine, ParamId id, float x, float y, float length, bool vertical = true, string? label = null)
    {
        binding = new ParamBinding(engine, id);
        this.vertical = vertical;
        this.length = length;
        this.label = label;
        var hasLane = binding.Lane is not null;
        if (vertical)
        {
            var height = (label is null ? 0f : LabelHeight) + length + (hasLane ? Gap + AutoButton + Gap + Strip : 0f) + 2f;
            Bounds = new RectangleF(x - (Column - SlotThickness) / 2f, y - (label is null ? 0f : LabelHeight), Column, height);
        }
        else
        {
            var width = length + (hasLane ? Gap + LaneBlock : 0f) + 2f;
            Bounds = new RectangleF(x, y - (Column - SlotThickness) / 2f, width, Column);
        }
    }

    public override string? Tooltip => $"{binding.Info.Name}: {binding.ValueText}{(binding.Lane is null ? "" : "\n" + binding.LaneText + "\nbutton: automated · display: arm · right-click: lane menu")}\ndouble-click: default";

    private RectangleF Slot => vertical
        ? new RectangleF(Bounds.X + (Column - SlotThickness) / 2f, Bounds.Y + (label is null ? 0f : LabelHeight), SlotThickness, length)
        : new RectangleF(Bounds.X, Bounds.Y + (Column - SlotThickness) / 2f, length, SlotThickness);

    private RectangleF AutoRect
    {
        get
        {
            var slot = Slot;
            return vertical
                ? new RectangleF(slot.X + slot.Width / 2f - 8f, slot.Bottom + Gap, 16f, AutoButton)
                : new RectangleF(slot.Right + Gap, Bounds.Y + 1f, LaneBlock, 8f);
        }
    }

    private RectangleF StripRect
    {
        get
        {
            var auto = AutoRect;
            return vertical
                ? new RectangleF(auto.X, auto.Bottom + Gap, 16f, Strip)
                : new RectangleF(auto.X, auto.Bottom + 2f, LaneBlock, Strip);
        }
    }

    public override void Draw(Graphics g)
    {
        if (label is not null && vertical)
            EditorDraw.Text(g, label, new RectangleF(Bounds.X - 10f, Bounds.Y, Bounds.Width + 20f, LabelHeight), EditorTheme.LabelFont, EditorTheme.Text, EditorTheme.Center);

        EditorDraw.FaderBar(g, Slot, binding.Normalized, binding.ResolvedNormalized, binding.IsBipolar, vertical, Hot || dragging);

        var lane = binding.Lane;
        if (lane is null)
            return;

        var auto = AutoRect;
        var automated = lane.Mode == LaneMode.Automated;
        EditorDraw.Frame(g, auto, 1.5f, automated ? EditorTheme.ButtonOn : EditorTheme.Button, EditorTheme.ButtonEdge, 1f);
        EditorDraw.Led(g, new RectangleF(auto.X + auto.Width / 2f - 2f, auto.Y + auto.Height / 2f - 2f, 4f, 4f), automated, EditorTheme.LedOn);

        var strip = StripRect;
        EditorDraw.Frame(g, strip, 1f, EditorTheme.Display, lane.Armed && !lane.IsRecording ? EditorTheme.Armed : EditorTheme.ButtonEdge, 1f);
        var inner = RectangleF.Inflate(strip, -1f, -1f);
        if (lane.IsRecording)
        {
            var fraction = Math.Clamp(lane.RecordedSeconds / lane.MaxSeconds, 0f, 1f);
            using var brush = new SolidBrush(EditorTheme.Recording);
            g.FillRectangle(brush, inner.X, inner.Y, inner.Width * fraction, inner.Height);
        }
        else if (lane.IsPlaying)
        {
            var fraction = lane.LengthSeconds > 0f ? (float)Math.Clamp(lane.Phase / lane.LengthSeconds, 0.0, 1.0) : 0f;
            using var dim = new SolidBrush(EditorTheme.Loop);
            using var bright = new SolidBrush(EditorTheme.Playing);
            g.FillRectangle(dim, inner);
            g.FillRectangle(bright, inner.X + (inner.Width - 2f) * fraction, inner.Y, 2f, inner.Height);
        }
        else if (lane.HasRecording)
        {
            using var dim = new SolidBrush(EditorTheme.Loop);
            g.FillRectangle(dim, inner);
        }
    }

    public override bool Update()
    {
        var value = Displayed;
        var laneState = 0;
        if (binding.Lane is { } lane)
        {
            var phase = lane.IsPlaying && lane.LengthSeconds > 0f ? (int)(lane.Phase / lane.LengthSeconds * 64) : lane.IsRecording ? (int)(lane.RecordedSeconds * 4f) : 0;
            laneState = (lane.Mode == LaneMode.Automated ? 1 : 0) | (lane.Armed ? 2 : 0) | (lane.IsRecording ? 4 : 0) | (lane.IsPlaying ? 8 : 0) | (lane.HasRecording ? 16 : 0) | (phase << 5);
        }

        if (value == shownValue && laneState == shownLane)
            return false;

        shownValue = value;
        shownLane = laneState;

        return true;
    }

    public override void MouseDown(PointF p, MouseButtons button, int clicks)
    {
        var lane = binding.Lane;
        if (button == MouseButtons.Right)
        {
            if (lane is not null)
                ShowMenu(LaneMenu.Build(binding), p);

            return;
        }

        if (button != MouseButtons.Left)
            return;

        if (lane is not null)
        {
            if (AutoRect.Contains(p))
            {
                lane.Mode = lane.Mode == LaneMode.Automated ? LaneMode.Static : LaneMode.Automated;
                Invalidate();
                return;
            }

            if (StripRect.Contains(p))
            {
                lane.Armed = !lane.Armed;
                Invalidate();
                return;
            }
        }

        if (clicks >= 2)
        {
            binding.Reset();
            Invalidate();
            return;
        }

        var cap = CapRect(binding.Normalized);
        if (cap.Contains(p))
        {
            grabOffset = vertical ? cap.Y + cap.Height / 2f - p.Y : cap.X + cap.Width / 2f - p.X;
        }
        else
        {
            grabOffset = 0f;
            binding.Normalized = NormalizedAt(p);
        }

        dragging = true;
        if (lane is not null)
            lane.Pressed = true;

        Invalidate();
    }

    public override void MouseMove(PointF p, MouseButtons buttons)
    {
        if (!dragging)
            return;

        binding.Normalized = NormalizedAt(p);
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

    private float Displayed => binding.LanePlaying && !dragging ? binding.ResolvedNormalized : binding.Normalized;

    private RectangleF CapRect(float normalized)
    {
        var slot = Slot;
        if (vertical)
        {
            var cy = slot.Bottom - normalized * length;
            return new RectangleF(slot.X + slot.Width / 2f - CapLong / 2f, cy - CapShort / 2f, CapLong, CapShort);
        }

        var cx = slot.X + normalized * length;

        return new RectangleF(cx - CapShort / 2f, slot.Y + slot.Height / 2f - CapLong / 2f, CapShort, CapLong);
    }

    private float NormalizedAt(PointF p)
    {
        var slot = Slot;
        var n = vertical ? (slot.Bottom - (p.Y + grabOffset)) / length : (p.X + grabOffset - slot.X) / length;

        return Math.Clamp(n, 0f, 1f);
    }
}
