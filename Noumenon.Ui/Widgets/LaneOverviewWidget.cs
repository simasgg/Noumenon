using Noumenon.Engine;
using Noumenon.Engine.Lanes;

namespace Noumenon.Ui.Widgets;

/// <summary>
/// Every lane that holds a loop, one row each: the parameter's short name, the loop as a
/// sparkline, the playhead while it plays, and a dot for its state (accent playing, amber armed,
/// red recording, dim static). Playing lanes come first. Click a row to switch it between
/// Automated and Static, right-click for the lane menu, so the faders that play themselves can
/// be managed in one place instead of hunted across the panel.
/// </summary>
public sealed class LaneOverviewWidget : Widget
{
    private const float RowHeight = 14f;
    private const float NameWidth = 64f;
    private const float DotSize = 6f;

    private readonly NoumenonEngine engine;
    private readonly List<AutomationLane> rows = new();
    private float[] points = new float[2];
    private int shownHash;

    public LaneOverviewWidget(NoumenonEngine engine, float x, float y, float width, float height)
    {
        this.engine = engine;
        Bounds = new RectangleF(x, y, width, height);
    }

    public override string? Tooltip => "Lanes with loops · click: automated / static · right-click: lane menu";

    private int Capacity => Math.Max(1, (int)(Bounds.Height / RowHeight));

    public override void Draw(Graphics g)
    {
        EditorDraw.Display(g, Bounds, 3f);
        Collect();
        if (rows.Count == 0)
        {
            EditorDraw.Text(g, "no loops yet — arm a fader's strip and drag it", Bounds, EditorTheme.SmallFont, EditorTheme.TextDim, EditorTheme.Center);
            return;
        }

        var capacity = Capacity;
        var shownRows = Math.Min(rows.Count, rows.Count > capacity ? capacity - 1 : capacity);
        for (var i = 0; i < shownRows; i++)
            DrawRow(g, rows[i], RowRect(i));

        if (rows.Count > shownRows)
            EditorDraw.Text(g, $"+{rows.Count - shownRows} more", RowRect(shownRows), EditorTheme.SmallFont, EditorTheme.TextDim, EditorTheme.Center);
    }

    public override bool Update()
    {
        Collect();
        var hash = rows.Count;
        foreach (var lane in rows)
        {
            var phase = lane.IsPlaying && lane.LengthSeconds > 0f ? (int)(lane.Phase / lane.LengthSeconds * 256) : (int)(lane.RecordedSeconds * 8f);
            hash = hash * 31 + (int)lane.Id * 7 + (lane.IsPlaying ? 1 : 0) + (lane.Armed ? 2 : 0) + (lane.IsRecording ? 4 : 0) + lane.PointCount * 8 + (phase << 12);
        }

        if (hash == shownHash)
            return false;

        shownHash = hash;

        return true;
    }

    public override void MouseDown(PointF p, MouseButtons button, int clicks)
    {
        Collect();
        var index = (int)((p.Y - Bounds.Y) / RowHeight);
        if (index < 0 || index >= rows.Count || index >= Capacity)
            return;

        var lane = rows[index];
        if (button == MouseButtons.Right)
        {
            ShowMenu(ControlMenu.Build(new ParamBinding(engine, lane.Id), Commit), p);
            return;
        }

        if (button != MouseButtons.Left)
            return;

        lane.Mode = lane.Mode == LaneMode.Automated ? LaneMode.Static : LaneMode.Automated;
        Invalidate();
    }

    private void Collect()
    {
        rows.Clear();
        foreach (var lane in engine.Lanes.All)
        {
            if (lane.HasRecording || lane.IsRecording)
                rows.Add(lane);
        }

        rows.Sort((a, b) => Rank(b).CompareTo(Rank(a)));
    }

    private static int Rank(AutomationLane lane) => lane.IsRecording ? 3 : lane.IsPlaying ? 2 : lane.Armed ? 1 : 0;

    private RectangleF RowRect(int index) => new(Bounds.X + 2f, Bounds.Y + 2f + index * RowHeight, Bounds.Width - 4f, RowHeight);

    private void DrawRow(Graphics g, AutomationLane lane, RectangleF row)
    {
        var state = lane.IsRecording ? EditorTheme.Recording : lane.IsPlaying ? EditorTheme.Playing : lane.Armed ? EditorTheme.Armed : EditorTheme.TextDim;
        EditorDraw.Text(g, lane.Info.ShortName, new RectangleF(row.X + 2f, row.Y, NameWidth - 4f, row.Height), EditorTheme.SmallFont, lane.IsPlaying ? EditorTheme.Text : EditorTheme.TextDim, EditorTheme.Left);
        var plot = new RectangleF(row.X + NameWidth, row.Y + 2f, row.Width - NameWidth - DotSize - 8f, row.Height - 4f);
        if (lane.Capacity > points.Length)
            points = new float[lane.Capacity];

        var n = lane.CopyPoints(points);
        if (n >= 2)
        {
            var info = lane.Info;
            using var pen = new Pen(lane.IsPlaying ? EditorTheme.Accent : EditorTheme.AccentDark, 1f);
            var previous = PointF.Empty;
            var columns = Math.Max(1, (int)plot.Width);
            for (var c = 0; c <= columns; c++)
            {
                var index = Math.Min(n - 1, (int)((long)n * c / Math.Max(1, columns)));
                var y = plot.Bottom - plot.Height * info.ToNormalized(points[index]);
                var point = new PointF(plot.X + c, y);
                if (c > 0)
                    g.DrawLine(pen, previous, point);

                previous = point;
            }
        }

        if (lane.IsPlaying && lane.LengthSeconds > 0f)
        {
            var fraction = (float)Math.Clamp(lane.Phase / lane.LengthSeconds, 0.0, 1.0);
            using var head = new Pen(EditorTheme.TextLight, 1f);
            var x = plot.X + plot.Width * fraction;
            g.DrawLine(head, x, plot.Y - 1f, x, plot.Bottom + 1f);
        }
        else if (lane.IsRecording)
        {
            var fraction = Math.Clamp(lane.RecordedSeconds / lane.MaxSeconds, 0f, 1f);
            using var brush = new SolidBrush(Color.FromArgb(90, EditorTheme.Recording));
            g.FillRectangle(brush, plot.X, plot.Y, plot.Width * fraction, plot.Height);
        }

        EditorDraw.Led(g, new RectangleF(row.Right - DotSize - 2f, row.Y + (row.Height - DotSize) / 2f, DotSize, DotSize), true, state);
    }
}
