using System.Globalization;

namespace Noumenon.Engine.Scales;

/// <summary>
/// Reads Scala <c>.scl</c> files into a <see cref="Scale"/>: comment lines start with "!", the
/// first real line is the description, the second the number of pitches, then one pitch per
/// line — cents when it contains a decimal point, otherwise a ratio ("3/2") or an integer. The
/// implicit 1/1 is the root; the last pitch is the period (usually 2/1), and every pitch inside
/// it becomes a degree.
/// </summary>
public static class ScalaParser
{
    public static Scale Parse(string text, string? fallbackName = null)
    {
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => !l.TrimStart().StartsWith('!')).ToList();
        if (lines.Count < 2)
            throw new FormatException("A Scala file needs a description line and a pitch count.");

        var description = lines[0].Trim();
        if (!int.TryParse(lines[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) || count < 0)
            throw new FormatException($"Invalid pitch count '{lines[1].Trim()}'.");

        var cents = new List<float>();
        for (var i = 0; i < count; i++)
        {
            if (2 + i >= lines.Count)
                throw new FormatException($"Expected {count} pitches, found {i}.");

            cents.Add(ParsePitch(lines[2 + i]));
        }

        var period = cents.Count > 0 ? cents[^1] : 1200f;
        if (period <= 0f)
            throw new FormatException("The period must be positive.");

        var name = string.IsNullOrWhiteSpace(description) ? fallbackName ?? "Scala" : description;

        return new Scale(name, cents.Take(Math.Max(0, cents.Count - 1)), period);
    }

    public static Scale ParseFile(string path) => Parse(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path));

    private static float ParsePitch(string line)
    {
        var token = line.Trim().Split(' ', '\t')[0];
        if (token.Length == 0)
            throw new FormatException("Empty pitch line.");

        if (token.Contains('.'))
        {
            if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var cents))
                throw new FormatException($"Invalid cents value '{token}'.");

            return cents;
        }

        var slash = token.IndexOf('/');
        if (slash >= 0)
        {
            if (!long.TryParse(token[..slash], NumberStyles.Integer, CultureInfo.InvariantCulture, out var numerator) || !long.TryParse(token[(slash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var denominator) || numerator <= 0 || denominator <= 0)
                throw new FormatException($"Invalid ratio '{token}'.");

            return (float)(1200.0 * Math.Log2((double)numerator / denominator));
        }

        if (!long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole) || whole <= 0)
            throw new FormatException($"Invalid pitch '{token}'.");

        return (float)(1200.0 * Math.Log2(whole));
    }
}
