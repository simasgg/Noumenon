namespace Noumenon.Ui;

/// <summary>
/// Undo for the panel: one entry per gesture (a press-to-release, a menu pick, a wheel step)
/// holding the whole parameter bank before and after, since a gesture may move several values
/// (a lane menu, a reset). Entries are cheap — a few hundred floats — so the stack keeps the
/// last few hundred and forgets the oldest.
/// </summary>
public sealed class UndoStack
{
    private const int Capacity = 300;

    private readonly List<(float[] Before, float[] After)> undo = new();
    private readonly List<(float[] Before, float[] After)> redo = new();

    public bool CanUndo => undo.Count > 0;

    public bool CanRedo => redo.Count > 0;

    public void Push(float[] before, float[] after)
    {
        undo.Add((before, after));
        if (undo.Count > Capacity)
            undo.RemoveAt(0);

        redo.Clear();
    }

    /// <summary>The bank to restore, or null when there is nothing to undo.</summary>
    public float[]? Undo()
    {
        if (undo.Count == 0)
            return null;

        var entry = undo[^1];
        undo.RemoveAt(undo.Count - 1);
        redo.Add(entry);

        return entry.Before;
    }

    public float[]? Redo()
    {
        if (redo.Count == 0)
            return null;

        var entry = redo[^1];
        redo.RemoveAt(redo.Count - 1);
        undo.Add(entry);

        return entry.After;
    }

    public static bool Differ(float[] a, float[] b)
    {
        for (var i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
                return true;
        }

        return false;
    }
}
