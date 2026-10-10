using Noumenon.Engine;
using Noumenon.Engine.Parameters;

namespace Noumenon.Ui.Widgets;

public enum ToggleStyle
{
    Button,
    Led,
}

/// <summary>A push button or LED bound to a toggle parameter; MF's ON, FINE, ALL, LINK and MIDI buttons.</summary>
public sealed class ToggleWidget : Widget
{
    private readonly ParamBinding binding;
    private readonly string text;
    private readonly ToggleStyle style;
    private bool down;
    private bool shown;

    public ToggleWidget(NoumenonEngine engine, ParamId id, float x, float y, float width, float height, string text, ToggleStyle style = ToggleStyle.Button)
    {
        binding = new ParamBinding(engine, id);
        this.text = text;
        this.style = style;
        Bounds = new RectangleF(x, y, width, height);
    }

    public override string? Tooltip => $"{binding.Info.Name}: {binding.ValueText}";

    public override void Draw(Graphics g)
    {
        var on = binding.Value >= 0.5f;
        if (style == ToggleStyle.Button)
        {
            EditorDraw.ButtonFace(g, Bounds, down, on, text);
            return;
        }

        var led = new RectangleF(Bounds.X, Bounds.Y + (Bounds.Height - 7f) / 2f, 7f, 7f);
        EditorDraw.Led(g, led, on, EditorTheme.LedOn);
        EditorDraw.Text(g, text, new RectangleF(led.Right + 3f, Bounds.Y, Bounds.Width - 10f, Bounds.Height), EditorTheme.LabelFont, EditorTheme.Text, EditorTheme.Left);
    }

    public override bool Update()
    {
        var on = binding.Value >= 0.5f;
        if (on == shown)
            return false;

        shown = on;

        return true;
    }

    public override void MouseDown(PointF p, MouseButtons button, int clicks)
    {
        if (button != MouseButtons.Left)
            return;

        down = true;
        Invalidate();
    }

    public override void MouseUp(PointF p, MouseButtons button)
    {
        if (!down)
            return;

        down = false;
        if (Bounds.Contains(p))
            binding.Value = binding.Value >= 0.5f ? 0f : 1f;

        Invalidate();
    }
}

/// <summary>A momentary button that runs an action on release; <c>lit</c> can light it from engine state.</summary>
public sealed class TextButtonWidget : Widget
{
    private readonly string text;
    private readonly Action onClick;
    private readonly Func<bool>? lit;
    private readonly string? tooltip;
    private readonly Font font;
    private bool down;
    private bool shownLit;

    public TextButtonWidget(string text, float x, float y, float width, float height, Action onClick, Func<bool>? lit = null, string? tooltip = null, Font? font = null)
    {
        this.text = text;
        this.onClick = onClick;
        this.lit = lit;
        this.tooltip = tooltip;
        this.font = font ?? EditorTheme.SmallFont;
        Bounds = new RectangleF(x, y, width, height);
    }

    public override string? Tooltip => tooltip;

    public override void Draw(Graphics g) => EditorDraw.ButtonFace(g, Bounds, down, lit?.Invoke() ?? false, text, font);

    public override bool Update()
    {
        var current = lit?.Invoke() ?? false;
        if (current == shownLit)
            return false;

        shownLit = current;

        return true;
    }

    public override void MouseDown(PointF p, MouseButtons button, int clicks)
    {
        if (button != MouseButtons.Left)
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
            onClick();
    }
}

/// <summary>One button per choice of a choice parameter, the current one lit; MF's PRE / POST.</summary>
public sealed class ChoiceButtonsWidget : Widget
{
    private readonly ParamBinding binding;
    private readonly string[] labels;
    private readonly bool vertical;
    private int shown = -1;
    private int downIndex = -1;

    public ChoiceButtonsWidget(NoumenonEngine engine, ParamId id, float x, float y, float width, float height, string[]? labels = null, bool vertical = false)
    {
        binding = new ParamBinding(engine, id);
        this.labels = labels ?? binding.Info.Choices.ToArray();
        this.vertical = vertical;
        Bounds = new RectangleF(x, y, width, height);
    }

    public override string? Tooltip => $"{binding.Info.Name}: {binding.ValueText}";

    public override void Draw(Graphics g)
    {
        var current = (int)MathF.Round(binding.Value);
        for (var i = 0; i < labels.Length; i++)
            EditorDraw.ButtonFace(g, Cell(i), downIndex == i, current == i, labels[i]);
    }

    public override bool Update()
    {
        var current = (int)MathF.Round(binding.Value);
        if (current == shown)
            return false;

        shown = current;

        return true;
    }

    public override void MouseDown(PointF p, MouseButtons button, int clicks)
    {
        if (button != MouseButtons.Left)
            return;

        downIndex = IndexAt(p);
        Invalidate();
    }

    public override void MouseUp(PointF p, MouseButtons button)
    {
        if (downIndex < 0)
            return;

        var index = IndexAt(p);
        if (index == downIndex)
            binding.Value = index;

        downIndex = -1;
        Invalidate();
    }

    public override void MouseWheel(PointF p, int delta)
    {
        Commit(() => binding.Step(delta > 0 ? 1 : -1));
        Invalidate();
    }

    private RectangleF Cell(int index)
    {
        var count = labels.Length;
        if (vertical)
        {
            var h = (Bounds.Height - (count - 1) * 2f) / count;
            return new RectangleF(Bounds.X, Bounds.Y + index * (h + 2f), Bounds.Width, h);
        }

        var w = (Bounds.Width - (count - 1) * 2f) / count;

        return new RectangleF(Bounds.X + index * (w + 2f), Bounds.Y, w, Bounds.Height);
    }

    private int IndexAt(PointF p)
    {
        for (var i = 0; i < labels.Length; i++)
        {
            if (Cell(i).Contains(p))
                return i;
        }

        return -1;
    }
}
