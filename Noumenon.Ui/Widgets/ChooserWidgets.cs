using System.Globalization;

namespace Noumenon.Ui.Widgets;

/// <summary>
/// A readout that picks one entry from a list the host owns (the preset bank, for a morph target):
/// click opens the list, the wheel steps through it. The pick is the panel's own state, not a
/// parameter; it is kept by index and shown by name, and reads "choose…" until something is picked
/// or when the list has shrunk past it.
/// </summary>
public sealed class ListChoiceWidget : Widget
{
    private readonly Func<IReadOnlyList<string>> entries;
    private readonly string? tooltip;
    private string shown = "";

    public ListChoiceWidget(Func<IReadOnlyList<string>> entries, float x, float y, float width, float height, string? tooltip = null)
    {
        this.entries = entries;
        this.tooltip = tooltip;
        Bounds = new RectangleF(x, y, width, height);
    }

    /// <summary>The picked entry, or -1.</summary>
    public int Selected { get; set; } = -1;

    public override string? Tooltip => tooltip;

    private string Text
    {
        get
        {
            var list = entries();
            return Selected >= 0 && Selected < list.Count ? list[Selected] : "choose…";
        }
    }

    public override void Draw(Graphics g)
    {
        EditorDraw.Display(g, Bounds);
        EditorDraw.Text(g, shown, new RectangleF(Bounds.X + 3f, Bounds.Y, Bounds.Width - 14f, Bounds.Height), EditorTheme.SmallValueFont, Hot ? EditorTheme.TextLight : EditorTheme.Readout, EditorTheme.Left);
        EditorDraw.Chevron(g, new RectangleF(Bounds.Right - 10f, Bounds.Y + Bounds.Height / 2f - 2f, 6f, 4f), EditorTheme.TraceDim);
    }

    public override bool Update()
    {
        var text = Text;
        if (text == shown)
            return false;

        shown = text;

        return true;
    }

    public override void MouseDown(PointF p, MouseButtons button, int clicks)
    {
        if (button != MouseButtons.Left)
            return;

        var list = entries();
        if (list.Count == 0)
            return;

        var menu = new ContextMenuStrip { ShowCheckMargin = true, ShowImageMargin = false };
        for (var i = 0; i < list.Count; i++)
        {
            var index = i;
            var item = new ToolStripMenuItem(list[i]) { Checked = index == Selected };
            item.Click += (_, _) =>
            {
                Selected = index;
                Invalidate();
            };
            menu.Items.Add(item);
        }

        ShowMenu(menu, new PointF(Bounds.X, Bounds.Bottom));
    }

    public override void MouseWheel(PointF p, int delta)
    {
        var count = entries().Count;
        if (count == 0)
            return;

        Selected = Selected < 0 ? 0 : (Selected + (delta > 0 ? count - 1 : 1)) % count;
        Invalidate();
    }
}

/// <summary>
/// A number box over a value the panel itself holds (a morph's time), not a parameter: drag up or
/// down, wheel, double-click for the default. The range is walked in equal ratios, so seconds
/// from half a second to minutes move evenly.
/// </summary>
public sealed class ValueBoxWidget : Widget
{
    private const float DragPixelsForRange = 240f;

    private readonly Func<float> get;
    private readonly Action<float> set;
    private readonly float min;
    private readonly float max;
    private readonly float defaultValue;
    private readonly string format;
    private readonly string suffix;
    private readonly string? tooltip;
    private bool dragging;
    private float dragStartY;
    private float dragStartPosition;
    private string shown = "";

    public ValueBoxWidget(Func<float> get, Action<float> set, float min, float max, float defaultValue, string format, string suffix, float x, float y, float width, float height, string? tooltip = null)
    {
        this.get = get;
        this.set = set;
        this.min = min;
        this.max = max;
        this.defaultValue = defaultValue;
        this.format = format;
        this.suffix = suffix;
        this.tooltip = tooltip;
        Bounds = new RectangleF(x, y, width, height);
    }

    public override string? Tooltip => tooltip;

    private string Text => get().ToString(format, CultureInfo.InvariantCulture) + suffix;

    public override void Draw(Graphics g)
    {
        EditorDraw.Display(g, Bounds);
        EditorDraw.Text(g, shown, RectangleF.Inflate(Bounds, -2f, 0f), EditorTheme.SmallValueFont, Hot || dragging ? EditorTheme.TextLight : EditorTheme.Readout, EditorTheme.Center);
    }

    public override bool Update()
    {
        var text = Text;
        if (text == shown)
            return false;

        shown = text;

        return true;
    }

    public override void MouseDown(PointF p, MouseButtons button, int clicks)
    {
        if (button != MouseButtons.Left)
            return;

        if (clicks >= 2)
        {
            set(defaultValue);
            Invalidate();
            return;
        }

        dragging = true;
        dragStartY = p.Y;
        dragStartPosition = Position(get());
    }

    public override void MouseMove(PointF p, MouseButtons buttons)
    {
        if (!dragging)
            return;

        set(FromPosition(dragStartPosition + (dragStartY - p.Y) / DragPixelsForRange));
        Invalidate();
    }

    public override void MouseUp(PointF p, MouseButtons button)
    {
        dragging = false;
        Invalidate();
    }

    public override void MouseWheel(PointF p, int delta)
    {
        set(FromPosition(Position(get()) + (delta > 0 ? 0.02f : -0.02f)));
        Invalidate();
    }

    private float Position(float value) => MathF.Log(Math.Clamp(value, min, max) / min) / MathF.Log(max / min);

    private float FromPosition(float position) => min * MathF.Pow(max / min, Math.Clamp(position, 0f, 1f));
}
