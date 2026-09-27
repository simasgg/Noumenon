using Noumenon.Dsp.Filters;
using Noumenon.Dsp.Oscillators;
using Noumenon.Engine;
using Noumenon.Engine.Parameters;

namespace Noumenon.Tests;

/// <summary>
/// The parameter table is the contract every layer builds on, so these pin it down: one row per
/// enum member in enum order, unique host-safe names, sane ranges, an exact normalized round trip,
/// and the arithmetic block layout the engine addresses slots and sections with.
/// </summary>
public class ParameterTableTests
{
    [Fact]
    public void Rows_Match_Enum_In_Order()
    {
        var ids = Enum.GetValues<ParamId>();
        Assert.Equal(ids.Length, ParameterTable.Count);
        for (var i = 0; i < ids.Length; i++)
            Assert.Equal(ids[i], ParameterTable.All[i].Id);
    }

    [Fact]
    public void Names_Are_Unique_And_Short_Names_Fit_Vst2()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var shortNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in ParameterTable.All)
        {
            Assert.True(names.Add(p.Name), $"duplicate name '{p.Name}'");
            Assert.True(shortNames.Add(p.ShortName), $"duplicate short name '{p.ShortName}' ({p.Id})");
            Assert.True(p.ShortName.Length <= ParamInfo.MaxShortNameLength, $"{p.Id}: short name '{p.ShortName}' too long");
        }
    }

    [Fact]
    public void Defaults_Are_In_Range_And_Discrete_Defaults_Are_Whole()
    {
        foreach (var p in ParameterTable.All)
        {
            Assert.InRange(p.Default, p.Min, p.Max);
            if (p.IsDiscrete)
                Assert.Equal(MathF.Round(p.Default), p.Default);
        }
    }

    [Fact]
    public void Normalized_Round_Trip_Is_Exact_Enough()
    {
        foreach (var p in ParameterTable.All)
        {
            foreach (var value in new[] { p.Min, p.Default, p.Max })
            {
                var back = p.FromNormalized(p.ToNormalized(value));
                var tolerance = p.IsDiscrete ? 0f : 1e-4f * MathF.Max(1f, MathF.Abs(value));
                Assert.True(MathF.Abs(back - value) <= tolerance, $"{p.Id}: {value} -> {p.ToNormalized(value)} -> {back}");
            }

            Assert.Equal(0f, p.ToNormalized(p.Min));
            Assert.Equal(1f, p.ToNormalized(p.Max), 5);
        }
    }

    [Fact]
    public void Block_Arithmetic_Matches_The_Enum_Names()
    {
        Assert.Equal(ParamId.OscA1On, ParameterTable.Osc(SectionId.A, 0, OscParam.On));
        Assert.Equal(ParamId.OscA6P2Route, ParameterTable.Osc(SectionId.A, 5, OscParam.P2Route));
        Assert.Equal(ParamId.OscB1Wave, ParameterTable.Osc(SectionId.B, 0, OscParam.Wave));
        Assert.Equal(ParamId.OscB6Level, ParameterTable.Osc(SectionId.B, 5, OscParam.Level));
        Assert.Equal(ParamId.SectionACutoff, ParameterTable.Section(SectionId.A, SectionParam.Cutoff));
        Assert.Equal(ParamId.SectionBP2FineRange, ParameterTable.Section(SectionId.B, SectionParam.P2FineRange));
        Assert.Equal(ParamId.P1Value, ParameterTable.Pitch(0, PitchParam.Value));
        Assert.Equal(ParamId.P2BendRange, ParameterTable.Pitch(1, PitchParam.BendRange));
        Assert.Equal(ParamId.ResoVoice1Note, ParameterTable.ResoVoice(0, ResoVoiceParam.Note));
        Assert.Equal(ParamId.ResoVoice6Pan, ParameterTable.ResoVoice(5, ResoVoiceParam.Pan));
        Assert.Equal(ParamId.Lfo3Depth, ParameterTable.Lfo(2, LfoParam.Depth));
        Assert.Equal(ParamId.ModSlot1Source, ParameterTable.ModSlot(0, ModSlotParam.Source));
        Assert.Equal(ParamId.ModSlot12Depth, ParameterTable.ModSlot(11, ModSlotParam.Depth));
        Assert.Equal(ParameterTable.OscParamCount, Enum.GetValues<OscParam>().Length);
        Assert.Equal(ParameterTable.SectionParamCount, Enum.GetValues<SectionParam>().Length);
    }

    [Fact]
    public void Choice_Lists_Match_The_Dsp_Enums()
    {
        Assert.Equal(Enum.GetValues<Waveform>().Length, ParameterTable.Waveforms.Count);
        Assert.Equal("Sine", ParameterTable.Waveforms[(int)Waveform.Sine]);
        Assert.Equal("Saw", ParameterTable.Waveforms[(int)Waveform.Saw]);
        Assert.Equal("Bipolar Pulse", ParameterTable.Waveforms[(int)Waveform.BipolarPulse]);
        Assert.Equal("S&H Noise", ParameterTable.Waveforms[(int)Waveform.NoiseSh]);

        Assert.Equal(Enum.GetValues<FilterType>().Length, ParameterTable.FilterTypes.Count);
        Assert.Equal("HP", ParameterTable.FilterTypes[(int)FilterType.HighPass]);

        var scopes = Enum.GetValues<RandomizeScope>();
        Assert.Equal(scopes.Length, ParameterTable.RandomizeScopes.Count);
        for (var i = 0; i < scopes.Length; i++)
            Assert.Equal(scopes[i].ToString(), ParameterTable.RandomizeScopes[i]);
    }

    [Fact]
    public void Formatting_Reads_Like_A_Panel()
    {
        Assert.Equal("2.00 kHz", ParameterTable.Get(ParamId.OscA1NoiseCutoff).Format(2000f));
        Assert.Equal("440.0 Hz", ParameterTable.Get(ParamId.TuningA4).Format(440f));
        Assert.Equal("-inf dB", ParameterTable.Get(ParamId.OutputVolume).Format(-60f));
        Assert.Equal("+3.0 dB", ParameterTable.Get(ParamId.OutputVolume).Format(3f));
        Assert.Equal("+7 st", ParameterTable.Get(ParamId.OscA1Semitone).Format(7f));
        Assert.Equal("-25 ct", ParameterTable.Get(ParamId.OscA1Fine).Format(-25f));
        Assert.Equal("C4", ParameterTable.Get(ParamId.ResoVoice1Note).Format(60f));
        Assert.Equal("Saw", ParameterTable.Get(ParamId.OscA1Wave).Format(2f));
        Assert.Equal("On", ParameterTable.Get(ParamId.Mute).Format(1f));
        Assert.Equal("None", ParameterTable.Get(ParamId.WheelTarget).Format(0f));
        Assert.Equal("Osc A1 On", ParameterTable.Get(ParamId.WheelTarget).Format(1f));
        Assert.Equal("50 %", ParameterTable.Get(ParamId.SamplerLoopStart).Format(0.5f));
    }

    [Fact]
    public void Find_Accepts_Enum_And_Display_Names()
    {
        Assert.Equal(ParamId.OscA1Level, ParameterTable.Find("OscA1Level")!.Id);
        Assert.Equal(ParamId.OscA1Level, ParameterTable.Find("osc a1 level")!.Id);
        Assert.Null(ParameterTable.Find("no such parameter"));
    }

    [Fact]
    public void Bank_Clamps_And_Rounds_On_Write()
    {
        var bank = new ParameterBank();
        Assert.Equal(ParameterTable.Count, bank.Count);

        bank.Set(ParamId.OscA1Semitone, 3.4f);
        Assert.Equal(3f, bank.Get(ParamId.OscA1Semitone));

        bank.Set(ParamId.OscA1Level, 2f);
        Assert.Equal(1f, bank.Get(ParamId.OscA1Level));

        bank.Set(ParamId.OscA1Wave, 99);
        Assert.Equal((int)Waveform.NoiseSh, bank.GetInt(ParamId.OscA1Wave));

        bank.SetNormalized(ParamId.SectionACutoff, 1f);
        Assert.Equal(20000f, bank.Get(ParamId.SectionACutoff), 1);

        bank.Set(ParamId.Mute, true);
        Assert.True(bank.GetBool(ParamId.Mute));

        bank.ResetToDefaults();
        Assert.Equal(ParameterTable.Get(ParamId.OscA1Level).Default, bank.Get(ParamId.OscA1Level));
        Assert.False(bank.GetBool(ParamId.Mute));
    }

    [Fact]
    public void Every_MF_Automatable_Fader_Is_Lane_Capable()
    {
        var lanes = ParameterTable.All.Count(p => p.IsLaneCapable);
        Assert.True(lanes >= 32, $"expected at least MF's 32 automatable faders, found {lanes}");
        Assert.True(ParameterTable.Get(ParamId.OscB6Level).IsLaneCapable);
        Assert.True(ParameterTable.Get(ParamId.P1Value).IsLaneCapable);
        Assert.True(ParameterTable.Get(ParamId.SpinTime1).IsLaneCapable);
        Assert.False(ParameterTable.Get(ParamId.OscA1Wave).IsLaneCapable);
    }
}
