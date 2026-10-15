using System.ComponentModel;
using Noumenon.Engine;
using Noumenon.Engine.Parameters;
using Noumenon.Ui.Pages;
using Noumenon.Ui.Widgets;

namespace Noumenon.Ui;

/// <summary>
/// The shared editor control: a fixed 1000 × 664 logical panel the standalone and the plugin
/// wrappers host, scaled 1×, 1.5× or 2×. Its own bar across the top holds the preset name and
/// bank, save, undo and redo, the MAIN / POST / MOD tabs and the scale; the pages hold every
/// parameter. It talks to the engine directly and to the host only for presets and files. Each
/// mouse gesture becomes one undo entry of the whole bank; Space restarts the lanes, R randomizes,
/// M mutes unless the host is using letters as piano keys.
/// </summary>
public sealed class NoumenonEditor : UserControl
{
    public const int LogicalWidth = 1000;
    public const int LogicalHeight = 664;
    public const int PageCount = 3;

    private static readonly string[] PageNames = ["MAIN", "POST", "MOD"];
    private static readonly float[] Scales = [1f, 1.5f, 2f];

    private readonly NoumenonEngine engine;
    private readonly IEditorHost host;
    private readonly EditorSurface surface = new();
    private readonly PageContent[] pages;
    private readonly List<Widget> bar = new();
    private readonly UndoStack undo = new();
    private float[]? gestureBefore;
    private int page;

    public NoumenonEditor(NoumenonEngine engine, IEditorHost host)
    {
        this.engine = engine;
        this.host = host;
        pages = [MainPage.Build(engine, host), PostPage.Build(engine), ModPage.Build(engine)];
        BuildBar();
        surface.LogicalSize = new SizeF(LogicalWidth, LogicalHeight);
        surface.GestureStarted += OnGestureStarted;
        surface.GestureEnded += OnGestureEnded;
        Controls.Add(surface);
        BackColor = EditorTheme.Panel;
        AutoSize = false;
        Size = surface.Size;
        ShowPage(0);
    }

    /// <summary>Raised after the control has resized itself for a new scale, so the host can refit its window.</summary>
    public event Action? PanelScaleChanged;

    /// <summary>Raised when a mouse gesture (press to release) begins, before it changes anything; a plugin host opens its edits here.</summary>
    public event Action? GestureStarted;

    /// <summary>Raised when a gesture ends, after its last change; a plugin host closes its edits here.</summary>
    public event Action? GestureEnded;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float PanelScale
    {
        get => surface.PanelScale;
        set
        {
            if (value <= 0f || Math.Abs(value - surface.PanelScale) < 0.001f)
                return;

            surface.PanelScale = value;
            Size = surface.Size;
            PanelScaleChanged?.Invoke();
        }
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Page
    {
        get => page;
        set => ShowPage(value);
    }

    public bool CanUndo => undo.CanUndo;

    public bool CanRedo => undo.CanRedo;

    public void Undo() => Restore(undo.Undo());

    public void Redo() => Restore(undo.Redo());

    public Bitmap RenderToBitmap() => surface.RenderToBitmap();

    /// <summary>Drives every widget's mouse handlers on every page, draws each once and restores the bank; the report's first line counts the failures.</summary>
    public string SelfTest()
    {
        var failures = new List<string>();
        var before = Snapshot();
        var exercised = 0;
        for (var p = 0; p < PageCount; p++)
        {
            ShowPage(p);
            surface.UpdateAll();
            foreach (var widget in pages[p].Widgets.Concat(bar))
            {
                if (widget.IsStatic)
                    continue;

                var centre = new PointF(widget.Bounds.X + widget.Bounds.Width / 2f, widget.Bounds.Y + widget.Bounds.Height / 2f);
                var moved = new PointF(centre.X + 5f, centre.Y - 8f);
                try
                {
                    switch (widget)
                    {
                        case KnobWidget or FaderWidget or NumberWidget or ScopeWidget or SpeedWidget or ChoiceButtonsWidget or ToggleWidget or StackWidget or LaneOverviewWidget or MeterWidget or ResonatorStripWidget:
                            widget.MouseDown(centre, MouseButtons.Left, 1);
                            widget.MouseMove(moved, MouseButtons.Left);
                            widget.MouseUp(moved, MouseButtons.Left);
                            widget.MouseWheel(centre, 120);
                            widget.MouseWheel(centre, -120);
                            exercised++;
                            break;
                        case WaveIconWidget:
                            widget.MouseWheel(centre, 120);
                            widget.MouseWheel(centre, -120);
                            exercised++;
                            break;
                    }

                    widget.Update();
                    using var bitmap = new Bitmap(4, 4);
                    using var g = Graphics.FromImage(bitmap);
                    widget.Draw(g);
                    widget.DrawOverlay(g);
                }
                catch (Exception ex)
                {
                    failures.Add($"{widget.GetType().Name} at {widget.Bounds}: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        engine.Parameters.CopyFrom(before);
        ShowPage(0);

        return $"{failures.Count} failures, {exercised} widgets exercised" + (failures.Count > 0 ? Environment.NewLine + string.Join(Environment.NewLine, failures) : "");
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        surface.Start();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        surface.Stop();
        base.OnHandleDestroyed(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Space:
                engine.Clock.RequestRestart();
                return true;
            case Keys.R when !host.KeyboardNotesActive:
                engine.RequestRandomize();
                return true;
            case Keys.M when !host.KeyboardNotesActive:
                engine.Parameters.Set(ParamId.Mute, !engine.Parameters.GetBool(ParamId.Mute));
                return true;
            case Keys.Control | Keys.Z:
                Undo();
                return true;
            case Keys.Control | Keys.Y:
                Redo();
                return true;
            case Keys.Control | Keys.Tab:
                ShowPage((page + 1) % PageCount);
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void BuildBar()
    {
        bar.Add(new DisplayTextWidget(() => host.CurrentPresetName, 6, 4, 220, 16, ShowPresetMenu, "Preset: click for the bank", EditorTheme.ValueFont, EditorTheme.Readout, EditorTheme.Left));
        bar.Add(new TextButtonWidget("◀", 230, 4, 16, 16, () => StepPreset(-1), tooltip: "Previous preset"));
        bar.Add(new TextButtonWidget("▶", 248, 4, 16, 16, () => StepPreset(1), tooltip: "Next preset"));
        bar.Add(new TextButtonWidget("SAVE", 270, 4, 40, 16, host.SavePreset, tooltip: "Save the snapshot as a preset"));
        bar.Add(new TextButtonWidget("UNDO", 318, 4, 40, 16, Undo, () => undo.CanUndo, "Undo the last gesture (Ctrl+Z)"));
        bar.Add(new TextButtonWidget("REDO", 360, 4, 40, 16, Redo, () => undo.CanRedo, "Redo (Ctrl+Y)"));
        for (var i = 0; i < PageCount; i++)
        {
            var index = i;
            bar.Add(new TextButtonWidget(PageNames[i], 470 + i * 50, 4, 48, 16, () => ShowPage(index), () => page == index, $"{PageNames[i]} page (Ctrl+Tab cycles)"));
        }

        var x = 660f;
        foreach (var factor in Scales)
        {
            var chosen = factor;
            var width = factor == 1.5f ? 34f : 28f;
            bar.Add(new TextButtonWidget("×" + factor.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture), x, 4, width, 16, () => PanelScale = chosen, () => Math.Abs(PanelScale - chosen) < 0.01f, "Panel size"));
            x += width + 2;
        }

        bar.Add(new DisplayTextWidget(() => host.StatusText, 764, 4, 230, 16, null, null, EditorTheme.SmallFont, EditorTheme.TextOnDark, EditorTheme.Left));
    }

    private void ShowPage(int index)
    {
        page = Math.Clamp(index, 0, PageCount - 1);
        var content = pages[page];
        surface.DrawStatic = g =>
        {
            DrawBar(g);
            content.DrawStatic?.Invoke(g);
        };
        surface.SetWidgets(bar.Concat(content.Widgets));
    }

    private static void DrawBar(Graphics g)
    {
        using var brush = new SolidBrush(EditorTheme.PanelDark);
        g.FillRectangle(brush, 0, 0, LogicalWidth, 24);
        using var line = new Pen(EditorTheme.Frame, 1f);
        g.DrawLine(line, 0, 24.5f, LogicalWidth, 24.5f);
    }

    private void ShowPresetMenu(PointF at)
    {
        var names = host.PresetNames;
        if (names.Count == 0)
            return;

        var menu = new ContextMenuStrip { ShowCheckMargin = true, ShowImageMargin = false };
        for (var i = 0; i < names.Count; i++)
        {
            var index = i;
            var item = new ToolStripMenuItem(names[i]) { Checked = host.CurrentPresetIndex == i };
            item.Click += (_, _) => host.LoadPreset(index);
            menu.Items.Add(item);
        }

        surface.ShowMenu(menu, new PointF(6f, 22f));
    }

    private void StepPreset(int step)
    {
        var count = host.PresetNames.Count;
        if (count == 0)
            return;

        var current = host.CurrentPresetIndex;
        var next = current < 0 ? (step > 0 ? 0 : count - 1) : (current + step + count) % count;
        host.LoadPreset(next);
    }

    private void OnGestureStarted()
    {
        gestureBefore = Snapshot();
        GestureStarted?.Invoke();
    }

    private void OnGestureEnded()
    {
        if (gestureBefore is null)
            return;

        var after = Snapshot();
        if (UndoStack.Differ(gestureBefore, after))
            undo.Push(gestureBefore, after);

        gestureBefore = null;
        GestureEnded?.Invoke();
    }

    private float[] Snapshot()
    {
        var values = new float[ParameterTable.Count];
        engine.Parameters.CopyTo(values);

        return values;
    }

    private void Restore(float[]? values)
    {
        if (values is null)
            return;

        engine.Parameters.CopyFrom(values);
        engine.Parameters.Set(ParamId.RandomizeNow, 0f);
        engine.Parameters.Set(ParamId.RestartNow, 0f);
    }
}
