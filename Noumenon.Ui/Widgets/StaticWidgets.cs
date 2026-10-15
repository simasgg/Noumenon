using Noumenon.Engine;
using Noumenon.Engine.Parameters;

namespace Noumenon.Ui.Widgets;

/// <summary>A caption baked into the static layer.</summary>
public sealed class LabelWidget : Widget
{
    private readonly string text;
    private readonly Font font;
    private readonly Color color;
    private readonly StringFormat format;

    public LabelWidget(string text, float x, float y, float width, float height, StringFormat? format = null, Font? font = null, Color? color = null)
    {
        IsStatic = true;
        this.text = text;
        this.font = font ?? EditorTheme.LabelFont;
        this.color = color ?? EditorTheme.Text;
        this.format = format ?? EditorTheme.Center;
        Bounds = new RectangleF(x, y, width, height);
    }

    public override void Draw(Graphics g) => EditorDraw.Text(g, text, Bounds, font, color, format);
}

/// <summary>A caption rotated to read bottom-to-top beside a tall fader.</summary>
public sealed class VerticalLabelWidget : Widget
{
    private readonly string text;
    private readonly Font font;

    public VerticalLabelWidget(string text, float centreX, float centreY, Font? font = null)
    {
        IsStatic = true;
        this.text = text;
        this.font = font ?? EditorTheme.LabelFont;
        Bounds = new RectangleF(centreX - 8, centreY - 60, 16, 120);
    }

    public override void Draw(Graphics g) => EditorDraw.VerticalText(g, text, new PointF(Bounds.X + 8, Bounds.Y + 60), font, EditorTheme.Text);
}

/// <summary>A module frame of the chassis with an optional title, baked into the static layer.</summary>
public sealed class FrameWidget : Widget
{
    private readonly string? title;
    private readonly bool inset;

    public FrameWidget(float x, float y, float width, float height, string? title = null, bool inset = false)
    {
        IsStatic = true;
        this.title = title;
        this.inset = inset;
        Bounds = new RectangleF(x, y, width, height);
    }

    public override void Draw(Graphics g) => EditorDraw.PanelFrame(g, Bounds, title, inset);
}

/// <summary>A dark readout showing whatever text a function returns right now, with an optional click action.</summary>
public sealed class DisplayTextWidget : Widget
{
    private readonly Func<string> text;
    private readonly Font font;
    private readonly Color color;
    private readonly StringFormat format;
    private readonly Action<PointF>? onClick;
    private readonly string? tooltip;
    private string shown = "";
    private bool down;

    public DisplayTextWidget(Func<string> text, float x, float y, float width, float height, Action<PointF>? onClick = null, string? tooltip = null, Font? font = null, Color? color = null, StringFormat? format = null)
    {
        this.text = text;
        this.onClick = onClick;
        this.tooltip = tooltip;
        this.font = font ?? EditorTheme.ValueFont;
        this.color = color ?? EditorTheme.Readout;
        this.format = format ?? EditorTheme.Center;
        Bounds = new RectangleF(x, y, width, height);
    }

    public override string? Tooltip => tooltip;

    public override void Draw(Graphics g)
    {
        EditorDraw.Display(g, Bounds);
        var inner = RectangleF.Inflate(Bounds, -3, 0);
        EditorDraw.Text(g, shown, inner, font, down ? EditorTheme.TextLight : color, format);
    }

    public override bool Update()
    {
        var current = text();
        if (current == shown)
            return false;

        shown = current;

        return true;
    }

    public override void MouseDown(PointF p, MouseButtons button, int clicks)
    {
        if (button != MouseButtons.Left || onClick is null)
            return;

        down = true;
        Invalidate();
    }

    public override void MouseUp(PointF p, MouseButtons button)
    {
        if (!down)
            return;

        down = false;
        Invalidate();
        if (Bounds.Contains(p))
            onClick?.Invoke(p);
    }
}

/// <summary>An indicator LED driven by a function, with an optional caption to its right.</summary>
public sealed class LedWidget : Widget
{
    private readonly Func<bool> on;
    private readonly Color color;
    private readonly string? text;
    private readonly float size;
    private bool shown;

    public LedWidget(Func<bool> on, float x, float y, float size, string? text = null, Color? color = null)
    {
        this.on = on;
        this.text = text;
        this.size = size;
        this.color = color ?? EditorTheme.LedOn;
        var width = text is null ? size + 4 : size + 6 + text.Length * 6f;
        Bounds = new RectangleF(x - 2, y - 2, width, size + 4);
    }

    public override void Draw(Graphics g)
    {
        EditorDraw.Led(g, new RectangleF(Bounds.X + 2, Bounds.Y + 2, size, size), shown, color);
        if (text is not null)
            EditorDraw.Text(g, text, new RectangleF(Bounds.X + size + 5, Bounds.Y, Bounds.Width - size - 5, Bounds.Height), EditorTheme.LabelFont, EditorTheme.Text, EditorTheme.Left);
    }

    public override bool Update()
    {
        var current = on();
        if (current == shown)
            return false;

        shown = current;

        return true;
    }
}

/// <summary>
/// The top bar's status line: the host's status (device, load, notices) — or, while a control
/// waits to learn a MIDI controller, an amber prompt naming it, which a click cancels. When a
/// controller has just been bound it tells the host, so the notice names what was learned.
/// </summary>
public sealed class StatusLineWidget : Widget
{
    private readonly NoumenonEngine engine;
    private readonly IEditorHost host;
    private string shown = "";
    private bool shownLearning;
    private int learned;

    public StatusLineWidget(NoumenonEngine engine, IEditorHost host, float x, float y, float width, float height)
    {
        this.engine = engine;
        this.host = host;
        learned = engine.MidiMap.LearnedCount;
        Bounds = new RectangleF(x, y, width, height);
    }

    public override string? Tooltip => shownLearning ? "Waiting for a MIDI controller · click to cancel" : null;

    public override void Draw(Graphics g)
    {
        EditorDraw.Display(g, Bounds);
        EditorDraw.Text(g, shown, RectangleF.Inflate(Bounds, -3, 0), EditorTheme.SmallFont, shownLearning ? EditorTheme.Armed : EditorTheme.TextOnDark, EditorTheme.Left);
    }

    public override bool Update()
    {
        var map = engine.MidiMap;
        var count = map.LearnedCount;
        if (count != learned)
        {
            learned = count;
            var controller = map.LastController;
            if (map.ParameterFor(controller) is { } id)
                host.Notice($"MIDI CC {controller} → {ParameterTable.Get(id).Name}");
        }

        var learning = map.Learning;
        var text = learning is { } waiting ? $"MIDI LEARN: move a controller for {ParameterTable.Get(waiting).Name}" : host.StatusText;
        if (text == shown && (learning is not null) == shownLearning)
            return false;

        shown = text;
        shownLearning = learning is not null;

        return true;
    }

    public override void MouseDown(PointF p, MouseButtons button, int clicks)
    {
        if (button == MouseButtons.Left && engine.MidiMap.Learning is not null)
        {
            engine.MidiMap.CancelLearn();
            Invalidate();
        }
    }
}
