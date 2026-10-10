using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Noumenon.Ui.Widgets;

/// <summary>
/// The control every page is drawn on. It owns the widget list, scales a fixed logical layout by
/// an integer-ish factor, keeps the static layer (chassis, frames, labels) in a cached bitmap and
/// repaints only the rectangles of widgets that report a change from the 30 fps poll, so an
/// idle panel costs almost nothing while faders that play themselves still animate. Mouse events
/// are mapped to logical coordinates and routed to the topmost widget under the pointer, which
/// keeps capture until the button is released; a press-to-release is one undo gesture. The
/// context menus the widgets open are shown and owned here until the next one replaces them.
/// </summary>
public sealed class EditorSurface : Control
{
    public const int FrameIntervalMs = 33;

    private readonly List<Widget> widgets = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = FrameIntervalMs };
    private readonly ToolTip toolTip = new() { InitialDelay = 450, ReshowDelay = 150, AutoPopDelay = 8000 };
    private Bitmap? background;
    private float scale = 1f;
    private SizeF logicalSize = new(1000f, 664f);
    private Widget? captured;
    private Widget? hot;
    private bool gestureOpen;
    private ContextMenuStrip? menu;

    public EditorSurface()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Selectable, true);
        BackColor = EditorTheme.Panel;
        AllowDrop = true;
        timer.Tick += OnTick;
        UpdateSize();
    }

    /// <summary>The page's chassis drawing, in logical coordinates, cached with the static widgets.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Action<Graphics>? DrawStatic { get; set; }

    public event Action? GestureStarted;

    public event Action? GestureEnded;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float PanelScale
    {
        get => scale;
        set
        {
            if (value <= 0f || Math.Abs(value - scale) < 0.001f)
                return;

            scale = value;
            UpdateSize();
        }
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public SizeF LogicalSize
    {
        get => logicalSize;
        set
        {
            logicalSize = value;
            UpdateSize();
        }
    }

    public IReadOnlyList<Widget> Widgets => widgets;

    public void SetWidgets(IEnumerable<Widget> page)
    {
        foreach (var widget in widgets)
            widget.Surface = null;

        widgets.Clear();
        widgets.AddRange(page);
        foreach (var widget in widgets)
            widget.Surface = this;

        captured = null;
        SetHot(null);
        DropCache();
        Invalidate();
    }

    public void Start() => timer.Start();

    public void Stop() => timer.Stop();

    /// <summary>One poll of every widget outside the timer, before a screenshot or a self-test.</summary>
    public void UpdateAll()
    {
        foreach (var widget in widgets)
        {
            if (!widget.IsStatic && widget.Visible)
                widget.Update();
        }
    }

    public void InvalidateStatic()
    {
        DropCache();
        Invalidate();
    }

    public void InvalidateWidget(Widget widget) => Invalidate(Rectangle.Inflate(ToDevice(widget.Bounds), 3, 3));

    public void RunGesture(Action change)
    {
        GestureStarted?.Invoke();
        try
        {
            change();
        }
        finally
        {
            GestureEnded?.Invoke();
        }
    }

    /// <summary>
    /// Shows a menu built for one click and owns it from here: it is disposed when the next menu
    /// replaces it or with the surface, never from its own Closed event — WinForms raises Closed
    /// from inside the drop-down's hide path and still reparents the window (and runs the clicked
    /// item's handler) afterwards, so disposing there throws and loses the pick.
    /// </summary>
    public void ShowMenu(ContextMenuStrip menu, PointF at)
    {
        DisposeMenu();
        this.menu = menu;
        menu.Show(this, Point.Round(new PointF(at.X * scale, at.Y * scale)));
    }

    public Point ToScreen(PointF logical) => PointToScreen(Point.Round(new PointF(logical.X * scale, logical.Y * scale)));

    /// <summary>Renders the whole page into a bitmap, as a screenshot or for a smoke test.</summary>
    public Bitmap RenderToBitmap()
    {
        UpdateAll();
        var bitmap = new Bitmap(Math.Max(1, Width), Math.Max(1, Height));
        using var g = Graphics.FromImage(bitmap);
        PaintSurface(g, new Rectangle(0, 0, bitmap.Width, bitmap.Height));

        return bitmap;
    }

    protected override void OnPaint(PaintEventArgs e) => PaintSurface(e.Graphics, e.ClipRectangle);

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        var p = ToLogical(e.Location);
        var widget = HitTest(p);
        if (widget is null)
            return;

        captured = widget;
        gestureOpen = true;
        GestureStarted?.Invoke();
        widget.MouseDown(p, e.Button, e.Clicks);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = ToLogical(e.Location);
        if (captured is not null)
        {
            captured.MouseMove(p, e.Button);
            return;
        }

        SetHot(HitTest(p));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var p = ToLogical(e.Location);
        if (captured is not null)
        {
            var widget = captured;
            captured = null;
            widget.MouseUp(p, e.Button);
        }

        if (gestureOpen)
        {
            gestureOpen = false;
            GestureEnded?.Invoke();
        }

        SetHot(HitTest(p));
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        var p = ToLogical(e.Location);
        HitTest(p)?.MouseWheel(p, e.Delta);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (captured is null)
            SetHot(null);
    }

    protected override void OnDragEnter(DragEventArgs drgevent)
    {
        base.OnDragEnter(drgevent);
        drgevent.Effect = DropTargetAt(drgevent) is not null ? DragDropEffects.Copy : DragDropEffects.None;
    }

    protected override void OnDragOver(DragEventArgs drgevent)
    {
        base.OnDragOver(drgevent);
        drgevent.Effect = DropTargetAt(drgevent) is not null ? DragDropEffects.Copy : DragDropEffects.None;
    }

    protected override void OnDragDrop(DragEventArgs drgevent)
    {
        base.OnDragDrop(drgevent);
        if (DropTargetAt(drgevent) is { } target && drgevent.Data?.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
            target.DropFiles(paths);
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down or Keys.Left or Keys.Right or Keys.Space || base.IsInputKey(keyData);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeMenu();
            timer.Dispose();
            toolTip.Dispose();
            background?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void DisposeMenu()
    {
        if (menu is null)
            return;

        menu.Close();
        menu.Dispose();
        menu = null;
    }

    private IFileDropTarget? DropTargetAt(DragEventArgs e)
    {
        if (e.Data is null || !e.Data.GetDataPresent(DataFormats.FileDrop))
            return null;

        var p = ToLogical(PointToClient(new Point(e.X, e.Y)));

        return HitTest(p) as IFileDropTarget;
    }

    private void PaintSurface(Graphics g, Rectangle clip)
    {
        EnsureBackground();
        g.DrawImage(background!, clip, clip, GraphicsUnit.Pixel);
        Prepare(g);
        g.ScaleTransform(scale, scale);
        var logicalClip = ToLogical(clip);
        foreach (var widget in widgets)
        {
            if (!widget.IsStatic && widget.Visible && widget.Bounds.IntersectsWith(logicalClip))
                widget.Draw(g);
        }
    }

    private void EnsureBackground()
    {
        if (background is not null && background.Width == Width && background.Height == Height)
            return;

        background?.Dispose();
        background = new Bitmap(Math.Max(1, Width), Math.Max(1, Height));
        using var g = Graphics.FromImage(background);
        g.Clear(EditorTheme.Panel);
        Prepare(g);
        g.ScaleTransform(scale, scale);
        DrawStatic?.Invoke(g);
        foreach (var widget in widgets)
        {
            if (widget.IsStatic && widget.Visible)
                widget.Draw(g);
        }
    }

    private static void Prepare(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!Visible || widgets.Count == 0)
            return;

        foreach (var widget in widgets)
        {
            if (!widget.IsStatic && widget.Visible && widget.Update())
                InvalidateWidget(widget);
        }
    }

    private Widget? HitTest(PointF p)
    {
        for (var i = widgets.Count - 1; i >= 0; i--)
        {
            var widget = widgets[i];
            if (!widget.IsStatic && widget.Contains(p))
                return widget;
        }

        return null;
    }

    private void SetHot(Widget? widget)
    {
        if (ReferenceEquals(hot, widget))
            return;

        hot?.SetHot(false);
        hot = widget;
        hot?.SetHot(true);
        toolTip.SetToolTip(this, hot?.Tooltip ?? "");
    }

    private void DropCache()
    {
        background?.Dispose();
        background = null;
    }

    private void UpdateSize()
    {
        Size = new Size((int)MathF.Ceiling(logicalSize.Width * scale), (int)MathF.Ceiling(logicalSize.Height * scale));
        DropCache();
        Invalidate();
    }

    private PointF ToLogical(Point device) => new(device.X / scale, device.Y / scale);

    private RectangleF ToLogical(Rectangle device) => new(device.X / scale, device.Y / scale, device.Width / scale, device.Height / scale);

    private Rectangle ToDevice(RectangleF logical) => Rectangle.Round(new RectangleF(logical.X * scale, logical.Y * scale, logical.Width * scale, logical.Height * scale));
}
