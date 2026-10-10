using Noumenon.Engine;
using Noumenon.Engine.Lanes;
using Noumenon.Engine.Parameters;

namespace Noumenon.Ui.Widgets;

/// <summary>
/// One element of the owner-drawn panel: a logical rectangle at 1× that the surface scales,
/// a draw routine, an optional per-frame <see cref="Update"/> that says whether it changed,
/// and mouse handlers in logical coordinates. Static widgets (labels, frames) are rendered once
/// into the surface's cached background; everything else is redrawn only when it reports a
/// change or is invalidated. A widget never touches the engine from any thread but the UI's.
/// </summary>
public abstract class Widget
{
    public RectangleF Bounds { get; set; }

    public bool Visible { get; set; } = true;

    public bool IsStatic { get; protected init; }

    public EditorSurface? Surface { get; internal set; }

    public bool Hot { get; private set; }

    public virtual string? Tooltip => null;

    public virtual void Draw(Graphics g)
    {
    }

    /// <summary>UI timer: returns true when the drawn state changed since the last call.</summary>
    public virtual bool Update() => false;

    public virtual void MouseDown(PointF p, MouseButtons button, int clicks)
    {
    }

    public virtual void MouseMove(PointF p, MouseButtons buttons)
    {
    }

    public virtual void MouseUp(PointF p, MouseButtons button)
    {
    }

    public virtual void MouseWheel(PointF p, int delta)
    {
    }

    public bool Contains(PointF p) => Visible && Bounds.Contains(p);

    internal void SetHot(bool hot)
    {
        if (Hot == hot)
            return;

        Hot = hot;
        Invalidate();
    }

    protected void Invalidate() => Surface?.InvalidateWidget(this);

    /// <summary>Runs a change as one undoable gesture (menu picks and wheel steps happen outside a mouse drag).</summary>
    protected void Commit(Action change)
    {
        if (Surface is { } surface)
            surface.RunGesture(change);
        else
            change();
    }

    protected void ShowMenu(ContextMenuStrip menu, PointF at) => Surface?.ShowMenu(menu, at);

    protected static bool ShiftHeld => (Control.ModifierKeys & Keys.Shift) != 0;
}

/// <summary>A widget that accepts files dropped on it (the sample display).</summary>
public interface IFileDropTarget
{
    void DropFiles(string[] paths);
}

/// <summary>
/// A widget's link to one parameter: the bank value the panel sets, the resolved value the
/// engine plays (a lane or the matrix can move it), both as real units and normalized, and the
/// parameter's lane when it has one.
/// </summary>
public sealed class ParamBinding
{
    public ParamBinding(NoumenonEngine engine, ParamId id)
    {
        Engine = engine;
        Id = id;
        Info = ParameterTable.Get(id);
    }

    public NoumenonEngine Engine { get; }

    public ParamId Id { get; }

    public ParamInfo Info { get; }

    public float Value
    {
        get => Engine.Parameters.Get(Id);
        set => Engine.Parameters.Set(Id, value);
    }

    public float Resolved => Engine.Resolved.Get(Id);

    public float Normalized
    {
        get => Info.ToNormalized(Value);
        set => Value = Info.FromNormalized(Math.Clamp(value, 0f, 1f));
    }

    public float ResolvedNormalized => Info.ToNormalized(Resolved);

    public bool IsBipolar => Info.Min < 0f && Info.Max > 0f;

    public AutomationLane? Lane => Info.IsLaneCapable ? Engine.Lanes.ForParameter(Id) : null;

    public bool LanePlaying => Lane is { IsPlaying: true };

    public string ValueText => Info.Format(Value);

    public string ResolvedText => Info.Format(Resolved);

    public void Reset() => Value = Info.Default;

    /// <summary>One wheel notch: a step for discrete parameters, two percent of the range otherwise (a tenth of that with Shift).</summary>
    public void Step(int notches)
    {
        if (Info.IsDiscrete)
            Value += notches;
        else
            Normalized += notches * (ShiftHeldNow ? 0.002f : 0.02f);
    }

    public string LaneText
    {
        get
        {
            var lane = Lane;
            if (lane is null)
                return "";

            if (lane.IsRecording)
                return $"recording {lane.RecordedSeconds:0.0} s";

            if (lane.IsPlaying)
                return $"loop {lane.Phase:0.0}/{lane.LengthSeconds:0.0} s ×{lane.SpeedMultiplier:0.##}";

            return (lane.HasRecording ? $"loop {lane.LengthSeconds:0.0} s (static)" : "no loop") + (lane.Armed ? ", armed" : "");
        }
    }

    private static bool ShiftHeldNow => (Control.ModifierKeys & Keys.Shift) != 0;
}
