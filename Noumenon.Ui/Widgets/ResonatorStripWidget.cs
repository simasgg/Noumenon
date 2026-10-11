using Noumenon.Engine;
using Noumenon.Engine.Parameters;

namespace Noumenon.Ui.Widgets;

/// <summary>
/// The resonator's six voices as ticks on a pitch strip that spans the chord, each at the note it is
/// ringing at right now — MIDI, chord select, the chord list or its own Note, then the scale —
/// with its name beneath and its height from the voice's level, so a chord can be read off the
/// panel without opening the voice table.
/// </summary>
public sealed class ResonatorStripWidget : Widget
{
    private const float MinSpan = 24f;
    private const float Margin = 2f;
    private const float NameHeight = 9f;

    private readonly NoumenonEngine engine;
    private readonly ParamBinding[] levels;
    private int shownHash;

    public ResonatorStripWidget(NoumenonEngine engine, float x, float y, float width, float height)
    {
        this.engine = engine;
        levels = new ParamBinding[ParameterTable.ResoVoiceCount];
        for (var v = 0; v < levels.Length; v++)
            levels[v] = new ParamBinding(engine, ParameterTable.ResoVoice(v, ResoVoiceParam.Level));

        Bounds = new RectangleF(x, y, width, height);
    }

    public override string? Tooltip => "Resonator voices: the notes ringing now";

    public override void Draw(Graphics g)
    {
        EditorDraw.Display(g, Bounds, 3f);
        var strip = new RectangleF(Bounds.X + 3f, Bounds.Y + 2f, Bounds.Width - 6f, Bounds.Height - NameHeight - 4f);
        Range(out var lowNote, out var highNote);
        using (var grid = new Pen(EditorTheme.DisplayGrid, 1f))
        {
            for (var octave = MathF.Ceiling(lowNote / 12f) * 12f; octave <= highNote; octave += 12f)
            {
                var x = strip.X + strip.Width * (octave - lowNote) / (highNote - lowNote);
                g.DrawLine(grid, x, strip.Y, x, strip.Bottom);
            }
        }

        for (var v = 0; v < levels.Length; v++)
        {
            var note = engine.ResochordNote(v);
            var x = strip.X + strip.Width * Math.Clamp((note - lowNote) / (highNote - lowNote), 0f, 1f);
            var height = strip.Height * Math.Clamp(levels[v].ResolvedNormalized, 0.08f, 1f);
            using var pen = new Pen(EditorTheme.Accent, 2f);
            g.DrawLine(pen, x, strip.Bottom, x, strip.Bottom - height);
            var nearest = Math.Clamp((int)MathF.Round(note), 0, 127);
            EditorDraw.Text(g, ParamInfo.NoteName(nearest), new RectangleF(x - 14f, strip.Bottom + 1f, 28f, NameHeight), EditorTheme.SmallFont, EditorTheme.TextDim, EditorTheme.Center);
        }
    }

    /// <summary>The strip spans the ringing notes plus a margin, at least two octaves, so a chord spreads out and a cluster still reads.</summary>
    private void Range(out float low, out float high)
    {
        low = float.MaxValue;
        high = float.MinValue;
        for (var v = 0; v < levels.Length; v++)
        {
            var note = engine.ResochordNote(v);
            low = MathF.Min(low, note);
            high = MathF.Max(high, note);
        }

        var centre = (low + high) / 2f;
        var span = MathF.Max(MinSpan, high - low + 2f * Margin);
        low = centre - span / 2f;
        high = centre + span / 2f;
    }

    public override bool Update()
    {
        var hash = 0;
        for (var v = 0; v < levels.Length; v++)
            hash = hash * 31 + (int)(engine.ResochordNote(v) * 100f) + (int)(levels[v].ResolvedNormalized * 50f) * 7;

        if (hash == shownHash)
            return false;

        shownHash = hash;

        return true;
    }
}
