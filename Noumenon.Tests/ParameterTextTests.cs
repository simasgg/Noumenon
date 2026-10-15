using Noumenon.Engine.Parameters;

namespace Noumenon.Tests;

/// <summary>
/// Typed parameter text, as both plugin formats read it: units, choices, switches, notes and
/// targets, and every row's own display text reading back to a value that displays the same.
/// </summary>
public class ParameterTextTests
{
    private static float Parse(ParamId id, string text)
    {
        Assert.True(ParameterText.TryParse(ParameterTable.Get(id), text, out var value), $"'{text}' should parse for {id}");

        return value;
    }

    [Fact]
    public void Numbers_Read_Their_Unit()
    {
        Assert.Equal(1500f, Parse(ParamId.MasterFilterCutoff, "1.5 kHz"), 2);
        Assert.Equal(120f, Parse(ParamId.MasterFilterCutoff, "120 Hz"), 2);
        Assert.Equal(0.5f, Parse(ParamId.SamplerLoopEnd, "50 %"), 4);
        Assert.Equal(-12f, Parse(ParamId.OutputVolume, "-12.0 dB"), 4);
        Assert.Equal(Units.SilenceDb, Parse(ParamId.OutputVolume, "-inf dB"));
        Assert.Equal(1.25f, Parse(ParamId.OutputVolume, "1,25"), 4);
        Assert.False(ParameterText.TryParse(ParameterTable.Get(ParamId.OutputVolume), "loud", out _));
        Assert.False(ParameterText.TryParse(ParameterTable.Get(ParamId.OutputVolume), "  ", out _));
    }

    [Fact]
    public void Choices_Switches_Notes_And_Targets_Read_By_Name()
    {
        Assert.Equal(1f, Parse(ParamId.ReverbRoom, ParameterTable.ReverbRooms[1].ToUpperInvariant()));
        Assert.Equal(2f, Parse(ParamId.ReverbRoom, "2"));
        Assert.False(ParameterText.TryParse(ParameterTable.Get(ParamId.ReverbRoom), "Cathedral of Glass", out _));
        Assert.Equal(1f, Parse(ParamId.Mute, "On"));
        Assert.Equal(0f, Parse(ParamId.Mute, "off"));
        Assert.Equal(1f, Parse(ParamId.RandomizeNow, "Fire"));
        Assert.False(ParameterText.TryParse(ParameterTable.Get(ParamId.Mute), "sideways", out _));

        var note = ParameterTable.ResoVoice(0, ResoVoiceParam.Note);
        Assert.Equal(60f, Parse(note, "C4"));
        Assert.Equal(49f, Parse(note, "c#3"));
        Assert.Equal(64f, Parse(note, "64"));

        Assert.Equal(0f, Parse(ParamId.WheelTarget, "None"));
        Assert.Equal((float)((int)ParamId.MasterFilterCutoff + 1), Parse(ParamId.WheelTarget, ParameterTable.Get(ParamId.MasterFilterCutoff).Name));
    }

    [Fact]
    public void Every_Rows_Display_Text_Reads_Back_To_Its_Value()
    {
        foreach (var info in ParameterTable.All)
        {
            foreach (var value in new[] { info.Min, info.Default, info.Max, info.FromNormalized(0.37f) })
            {
                var text = info.Format(value);
                Assert.True(ParameterText.TryParse(info, text, out var parsed), $"{info.Id}: '{text}' does not parse");
                Assert.True(info.Format(parsed) == text, $"{info.Id}: '{text}' reads back as {parsed}, which shows as '{info.Format(parsed)}'");
            }
        }
    }
}
