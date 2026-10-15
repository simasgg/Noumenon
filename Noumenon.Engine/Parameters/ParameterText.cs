using System.Globalization;

namespace Noumenon.Engine.Parameters;

/// <summary>
/// Reads text a person typed for a parameter — into a host's generic editor or an automation
/// lane's value box — back into real units, as the inverse of <see cref="ParamInfo.Format"/>: a
/// number with an optional unit (a "kHz" multiplies by 1000, a percent divides by 100, "-inf"
/// is the bottom of a silence-capable dB range), a choice by name or index, a switch as on/off, a
/// note by name ("C#3") and a modulation target by parameter name. Both plugin formats parse
/// through here, so typed values mean the same thing in every host.
/// </summary>
public static class ParameterText
{
    private static readonly string[] NoteNames = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    public static bool TryParse(ParamInfo info, string? text, out float value)
    {
        value = 0f;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();
        switch (info.Kind)
        {
            case ParamKind.Choice:
                return TryParseChoice(info, trimmed, out value);
            case ParamKind.Toggle or ParamKind.Trigger:
                return TryParseSwitch(trimmed, out value);
        }

        if (info.Unit == Units.Note && TryParseNote(trimmed, out value))
            return true;

        if (info.Unit == Units.Target)
            return TryParseTarget(trimmed, out value);

        if (info.Unit == Units.Decibels && trimmed.StartsWith("-inf", StringComparison.OrdinalIgnoreCase))
        {
            value = info.Min;
            return true;
        }

        var end = 0;
        while (end < trimmed.Length && (char.IsDigit(trimmed[end]) || trimmed[end] is '-' or '+' or '.' or ',' || (end > 0 && trimmed[end] is 'e' or 'E')))
            end++;

        if (end == 0 || !float.TryParse(trimmed[..end].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return false;

        if (info.Unit == Units.Hertz && trimmed.EndsWith("kHz", StringComparison.OrdinalIgnoreCase))
            value *= 1000f;
        else if (info.Unit == Units.Percent)
            value /= 100f;

        return true;
    }

    private static bool TryParseChoice(ParamInfo info, string text, out float value)
    {
        for (var i = 0; i < info.Choices.Count; i++)
        {
            if (string.Equals(info.Choices[i], text, StringComparison.OrdinalIgnoreCase))
            {
                value = i;
                return true;
            }
        }

        var parsed = int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index);
        value = index;

        return parsed;
    }

    private static bool TryParseSwitch(string text, out float value)
    {
        if (text.Equals("on", StringComparison.OrdinalIgnoreCase) || text.Equals("fire", StringComparison.OrdinalIgnoreCase) || text == "1")
        {
            value = 1f;
            return true;
        }

        value = 0f;

        return text.Equals("off", StringComparison.OrdinalIgnoreCase) || text == "0" || text == "-";
    }

    /// <summary>A note name with an octave where 60 = C4, the convention <see cref="ParamInfo.NoteName"/> writes.</summary>
    private static bool TryParseNote(string text, out float value)
    {
        value = 0f;
        var sharp = text.Length > 1 && text[1] == '#';
        var nameLength = sharp ? 2 : 1;
        var pitchClass = Array.FindIndex(NoteNames, name => string.Equals(name, text[..Math.Min(nameLength, text.Length)], StringComparison.OrdinalIgnoreCase));
        if (pitchClass < 0 || !int.TryParse(text[nameLength..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var octave))
            return false;

        value = (octave + 1) * 12 + pitchClass;

        return true;
    }

    /// <summary>A modulation target: "None" or a parameter's name, stored as its index + 1.</summary>
    private static bool TryParseTarget(string text, out float value)
    {
        value = 0f;
        if (text.Equals("None", StringComparison.OrdinalIgnoreCase))
            return true;

        if (ParameterTable.Find(text) is { } target)
        {
            value = target.Index + 1;
            return true;
        }

        var parsed = int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var stored);
        value = stored;

        return parsed;
    }
}
