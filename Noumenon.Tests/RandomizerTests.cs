using Noumenon.Engine;
using Noumenon.Engine.Parameters;

namespace Noumenon.Tests;

public class RandomizerTests
{
    private static float[] Snapshot(ParameterBank bank)
    {
        var values = new float[bank.Count];
        bank.CopyTo(values);

        return values;
    }

    [Fact]
    public void Same_Seed_Same_Patch()
    {
        var a = new ParameterBank();
        var b = new ParameterBank();
        Randomizer.Randomize(a, 1234);
        Randomizer.Randomize(b, 1234);

        Assert.Equal(Snapshot(a), Snapshot(b));
    }

    [Fact]
    public void Different_Seeds_Differ()
    {
        var a = new ParameterBank();
        var b = new ParameterBank();
        Randomizer.Randomize(a, 1);
        Randomizer.Randomize(b, 2);

        Assert.NotEqual(Snapshot(a), Snapshot(b));
    }

    [Fact]
    public void Oscillator_Scope_Leaves_The_Rest_Alone()
    {
        var bank = new ParameterBank();
        var before = Snapshot(bank);
        Randomizer.Randomize(bank, 99, RandomizeScope.Oscillators);
        var after = Snapshot(bank);

        Assert.NotEqual(before[(int)ParamId.OscA1Semitone..(int)ParamId.OscB6P2Route], after[(int)ParamId.OscA1Semitone..(int)ParamId.OscB6P2Route]);
        Assert.Equal(before[(int)ParamId.SamplerSelect..], after[(int)ParamId.SamplerSelect..]);
        Assert.Equal(before[(int)ParamId.P1Value..(int)ParamId.MixCrossfade], after[(int)ParamId.P1Value..(int)ParamId.MixCrossfade]);
    }

    [Fact]
    public void Low_Amount_Stays_Tonal_And_Keeps_Every_Slot_On()
    {
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var bank = new ParameterBank();
            Randomizer.Randomize(bank, seed, RandomizeScope.All, 0f);
            for (var s = 0; s < ParameterTable.SectionCount; s++)
            {
                for (var slot = 0; slot < ParameterTable.SlotsPerSection; slot++)
                {
                    Assert.True(bank.GetBool(ParameterTable.Osc((SectionId)s, slot, OscParam.On)));
                    Assert.InRange(bank.Get(ParameterTable.Osc((SectionId)s, slot, OscParam.Semitone)), -48f, 24f);
                    Assert.InRange(bank.Get(ParameterTable.Osc((SectionId)s, slot, OscParam.Fine)), -4f, 4f);
                }

                Assert.Equal(0, bank.GetInt(ParameterTable.Section((SectionId)s, SectionParam.FilterType)));
            }
        }
    }
}
