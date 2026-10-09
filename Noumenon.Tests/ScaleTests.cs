using Noumenon.Engine.Parameters;
using Noumenon.Engine.Scales;

namespace Noumenon.Tests;

public class ScaleTests
{
    [Fact]
    public void Twelve_Tet_Is_The_Identity()
    {
        Assert.True(Scale.Equal12.IsIdentity);
        Assert.Equal(3.3f, Scale.Equal12.Quantize(3.3f, 0));
        Assert.Equal(-7.25f, Scale.Equal12.Quantize(-7.25f, 5));
    }

    [Fact]
    public void Built_In_Scales_Match_The_Parameter_List()
    {
        Assert.Equal(ParameterTable.Scales.Count - 1, Scale.BuiltIn.Count);   // the last entry is "User"
        for (var i = 0; i < Scale.BuiltIn.Count; i++)
            Assert.Equal(ParameterTable.Scales[i], Scale.BuiltIn[i].Name);
    }

    [Fact]
    public void Major_Snaps_To_Its_Degrees_In_Any_Root()
    {
        var major = Scale.BuiltIn[1];
        Assert.False(major.IsIdentity);
        Assert.Equal(5f, major.Quantize(5.6f, 0));
        Assert.Equal(12f, major.Quantize(12.4f, 0));
        Assert.Equal(-1f, major.Quantize(-1f, 0));   // B below the root is in C major
        Assert.Contains(major.Quantize(1f, 0), new[] { 0f, 2f });
        Assert.Contains(major.Quantize(3f, 2), new[] { 2f, 4f });   // D major: D# is a semitone from both D and E
        Assert.Equal(6f, major.Quantize(6.2f, 2));                  // F# is in D major
    }

    [Fact]
    public void Pentatonic_Skips_Its_Gaps()
    {
        var pentatonic = Scale.BuiltIn[3];
        Assert.Equal(7f, pentatonic.Quantize(6f, 0));
        Assert.Equal(9f, pentatonic.Quantize(10f, 0));   // 10 → 9 (1 away) rather than 12 (2 away)
    }

    [Fact]
    public void Just_Scales_Carry_Fractional_Degrees()
    {
        var just = Scale.BuiltIn[5];
        Assert.Equal(3.8631f, just.Quantize(4f, 0), 3);
        Assert.Equal(7.0196f, just.Quantize(7f, 0), 3);
    }

    [Fact]
    public void Scala_Files_Parse_Ratios_And_Cents()
    {
        const string text = """
            ! ptolemy.scl
            !
            Ptolemy's intense diatonic
             7
            !
             9/8
             5/4
             4/3
             3/2
             5/3
             15/8
             2/1
            """;

        var scale = ScalaParser.Parse(text);
        Assert.Equal("Ptolemy's intense diatonic", scale.Name);
        Assert.Equal(7, scale.DegreesCents.Count);
        Assert.Equal(1200f, scale.PeriodCents);
        Assert.Equal(0f, scale.DegreesCents[0]);
        Assert.Equal(203.91f, scale.DegreesCents[1], 1);
        Assert.Equal(386.31f, scale.DegreesCents[2], 1);
        Assert.Equal(1088.27f, scale.DegreesCents[6], 1);

        var equal = ScalaParser.Parse("Twelve\n12\n100.0\n200.0\n300.0\n400.0\n500.0\n600.0\n700.0\n800.0\n900.0\n1000.0\n1100.0\n1200.0\n");
        Assert.True(equal.IsIdentity);

        var withComments = ScalaParser.Parse("! c\nBohlen-Pierce-ish\n2\n 3/2 \n 3/1\n", "fallback");
        Assert.Equal(1901.955f, withComments.PeriodCents, 2);
        Assert.Equal(2, withComments.DegreesCents.Count);
    }

    [Theory]
    [InlineData("only one line")]
    [InlineData("desc\nnot a number\n")]
    [InlineData("desc\n3\n100.0\n200.0\n")]
    [InlineData("desc\n1\n0/0\n")]
    public void Malformed_Scala_Text_Throws(string text)
    {
        Assert.Throws<FormatException>(() => ScalaParser.Parse(text));
    }
}
