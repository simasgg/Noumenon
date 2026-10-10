using Noumenon.Engine;
using Noumenon.Engine.Parameters;

namespace Noumenon.Ui.Widgets;

/// <summary>MF's SPEED box: the lane clock's step as one to three arrows (a dim one for half speed); click the left or right half, or wheel, to step.</summary>
public sealed class SpeedWidget : Widget
{
    private readonly ParamBinding binding;
    private int shown = -1;

    public SpeedWidget(NoumenonEngine engine, float x, float y, float width, float height)
    {
        binding = new ParamBinding(engine, ParamId.SpeedStep);
        Bounds = new RectangleF(x, y, width, height);
    }

    public override string? Tooltip => $"Lane speed: {binding.ValueText}\nclick left / right half or wheel to step";

    public override void Draw(Graphics g)
    {
        EditorDraw.Display(g, Bounds);
        var step = (int)MathF.Round(binding.Value);
        var arrows = Math.Max(1, step);
        var arrowWidth = 7f;
        var total = arrows * (arrowWidth + 2f) - 2f;
        var x = Bounds.X + Bounds.Width / 2f - total / 2f;
        var color = step == 0 ? EditorTheme.TraceDim : EditorTheme.Trace;
        for (var i = 0; i < arrows; i++)
        {
            EditorDraw.Triangle(g, new RectangleF(x, Bounds.Y + 3f, arrowWidth, Bounds.Height - 6f), true, color);
            x += arrowWidth + 2f;
        }
    }

    public override bool Update()
    {
        var step = (int)MathF.Round(binding.Value);
        if (step == shown)
            return false;

        shown = step;

        return true;
    }

    public override void MouseDown(PointF p, MouseButtons button, int clicks)
    {
        if (button != MouseButtons.Left)
            return;

        binding.Value += p.X < Bounds.X + Bounds.Width / 2f ? -1 : 1;
        Invalidate();
    }

    public override void MouseWheel(PointF p, int delta)
    {
        Commit(() => binding.Step(delta > 0 ? 1 : -1));
        Invalidate();
    }
}
