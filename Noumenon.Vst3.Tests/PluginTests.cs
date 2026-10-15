using NPlug;
using Noumenon.Engine.Parameters;
using Noumenon.Engine.Presets;
using Noumenon.Vst3.Parameters;
using Noumenon.Vst3.Processing;

namespace Noumenon.Vst3.Tests;

/// <summary>
/// The module as a host scans and drives it: one factory with both identities and their shared
/// controller, the parameter list with permanent IDs, units and text both ways, the latency, tail
/// and context the processors ask for, state that crosses between the identities (a standalone
/// preset file is one), and a process call through native bus buffers that renders, sets the
/// silence flags and allocates nothing.
/// </summary>
public class PluginTests
{
    private const int SampleRate = 48000;
    private const int Block = 512;

    [Fact]
    public void The_Factory_Exports_Both_Identities_And_One_Controller()
    {
        var factory = NoumenonFactory.Create();
        var infos = factory.PluginClassInfos;
        Assert.Equal(3, infos.Count);

        var instrument = Assert.IsType<AudioProcessorClassInfo>(infos[0]);
        Assert.Equal(PluginIdentity.InstrumentClassId, instrument.ClassId);
        Assert.Equal("Noumenon", instrument.Name);
        Assert.Equal(AudioProcessorCategory.InstrumentSynthesizer, instrument.Category);

        var effect = Assert.IsType<AudioProcessorClassInfo>(infos[1]);
        Assert.Equal(PluginIdentity.EffectClassId, effect.ClassId);
        Assert.Equal("Noumenon FX", effect.Name);
        Assert.Equal(AudioProcessorCategory.Effect, effect.Category);

        Assert.Equal(PluginIdentity.ControllerClassId, Assert.IsType<AudioControllerClassInfo>(infos[2]).ClassId);
        Assert.All(infos, info => Assert.Equal(PluginIdentity.Vendor, info.Vendor));

        Assert.IsType<InstrumentProcessor>(factory.CreateInstance(PluginIdentity.InstrumentClassId));
        var fx = Assert.IsType<EffectProcessor>(factory.CreateInstance(PluginIdentity.EffectClassId));
        Assert.Equal(PluginIdentity.ControllerClassId, fx.ControllerClassId);
        Assert.IsType<NoumenonController>(factory.CreateInstance(PluginIdentity.ControllerClassId));
        Assert.NotNull(AudioPluginFactoryExporter.Instance);
    }

    [Fact]
    public void Every_Table_Parameter_Is_Listed_Under_Its_Permanent_Id()
    {
        var model = new NoumenonModel();
        model.Initialize();
        model.ResetValues();
        Assert.Equal(ParameterTable.Count + PluginIdentity.MidiControllerCount, model.ParameterCount);

        foreach (var info in ParameterTable.All)
        {
            var parameter = model.GetParameterById(info.Index + 1);
            Assert.Same(model.TableParameters[info.Index], parameter);
            Assert.Equal(info.Name, parameter.Title);
            Assert.Equal(info.ShortName, parameter.ShortTitle);
            Assert.Equal(info.StepCount, parameter.StepCount);
            Assert.Equal(info.ToNormalized(info.Default), parameter.DefaultNormalizedValue, 6);
            Assert.Equal(parameter.DefaultNormalizedValue, parameter.NormalizedValue, 6);
            Assert.Equal(ParameterTable.GroupName(info.Group), parameter.Unit!.Name);
            Assert.Equal(info.Kind == ParamKind.Choice, (parameter.Flags & AudioParameterFlags.IsList) != 0);
            Assert.True((parameter.Flags & AudioParameterFlags.CanAutomate) != 0);
        }

        var bend = model.MidiControllers[PluginIdentity.PitchBendController];
        Assert.Equal(PluginIdentity.MidiControllerIdBase + PluginIdentity.PitchBendController, bend.Id.Value);
        Assert.Equal(0.5, bend.NormalizedValue);
        Assert.All(model.MidiControllers, controller => Assert.Equal(AudioParameterFlags.IsHidden, controller.Flags));
    }

    [Fact]
    public void Parameter_Text_Is_The_Tables_Both_Ways()
    {
        var model = new NoumenonModel();
        model.Initialize();
        var cutoff = model.TableParameters[(int)ParamId.MasterFilterCutoff];
        var room = model.TableParameters[(int)ParamId.ReverbRoom];
        var mute = model.TableParameters[(int)ParamId.Mute];
        var volume = model.TableParameters[(int)ParamId.OutputVolume];

        Assert.Equal(cutoff.Info.Format(cutoff.Info.FromNormalized(0.3f)), cutoff.ToString(0.3));
        Assert.Equal(cutoff.Info.ToNormalized(1500f), cutoff.FromString("1.5 kHz"), 5);
        Assert.Equal(room.Info.ToNormalized(1f), room.FromString(ParameterTable.ReverbRooms[1]), 6);
        Assert.Equal(ParameterTable.ReverbRooms[1], room.ToString(room.Info.ToNormalized(1f)));
        Assert.Equal(1.0, mute.FromString("on"));
        Assert.Equal("-12.0 dB", volume.ToString(volume.Info.ToNormalized(-12f)));
        Assert.Equal(-12f, (float)volume.ToPlain(volume.FromString("-12.0 dB")), 3);
        Assert.Throws<ArgumentException>(() => mute.FromString("sideways"));
    }

    [Fact]
    public void Processors_Report_The_Engines_Latency_An_Infinite_Tail_And_What_Context_They_Need()
    {
        var processor = new InstrumentProcessor();
        IAudioProcessor host = processor;

        Assert.Equal((uint)processor.Session.Engine.LatencySamples, host.LatencySamples);
        Assert.Equal(uint.MaxValue, host.TailSamples);
        Assert.True(host.ProcessContextRequirementFlags.HasFlag(AudioProcessContextRequirementFlags.NeedTempo));
        Assert.True(host.ProcessContextRequirementFlags.HasFlag(AudioProcessContextRequirementFlags.NeedTransportState));
        Assert.True(host.CanProcessSampleSize(AudioSampleSize.Float32));
        Assert.False(host.CanProcessSampleSize(AudioSampleSize.Float64));

        processor.Session.Engine.Parameters.Set(ParamId.EngineRate, 1);
        processor.Renderer.Prepare(SampleRate, Block);
        Assert.Equal((uint)processor.Session.Engine.LatencySamples, host.LatencySamples);
    }

    [Fact]
    public void State_Crosses_Between_The_Identities()
    {
        var instrument = new InstrumentProcessor();
        instrument.Session.Engine.Parameters.Set(ParamId.OutputVolume, -9f);
        instrument.Session.Engine.SetUserChord(1, [40f, 47f, 52f, 59f, 64f, 71f]);
        instrument.Session.SetPreset("Crossing", null);
        using var stream = new MemoryStream();
        ((IAudioProcessor)instrument).GetState(stream);
        Assert.Equal((byte)'{', stream.ToArray()[0]);

        var effect = new EffectProcessor();
        stream.Position = 0;
        ((IAudioProcessor)effect).SetState(stream);

        Assert.Equal(-9f, effect.Session.Engine.Parameters.Get(ParamId.OutputVolume));
        Assert.Equal([40f, 47f, 52f, 59f, 64f, 71f], effect.Session.Engine.UserChord(1));
        Assert.Equal("Crossing", effect.Session.PresetName);
        Assert.Equal(ParameterTable.Get(ParamId.OutputVolume).ToNormalized(-9f), effect.Session.HostValues.Agreed(ParamId.OutputVolume), 6);

        ((IAudioProcessor)effect).SetState(new MemoryStream("nonsense"u8.ToArray()));
        Assert.Equal(-9f, effect.Session.Engine.Parameters.Get(ParamId.OutputVolume));
        Assert.NotNull(effect.Session.CurrentNotice);
    }

    [Fact]
    public void A_Preset_File_From_The_Standalone_Is_A_Valid_State()
    {
        var path = PresetLibrary.Resolve("Cathedral Chords", AppContext.BaseDirectory);
        Assert.NotNull(path);
        var preset = PresetIo.Load(path);
        var processor = new EffectProcessor();

        ((IAudioProcessor)processor).SetState(new MemoryStream(File.ReadAllBytes(path)));

        var bank = processor.Session.Engine.Parameters;
        foreach (var (name, value) in preset.Parameters)
        {
            var info = ParameterTable.Find(name)!;
            Assert.Equal(info.Clamp(value), bank.Get(info.Id));
        }

        Assert.Equal(preset.Name, processor.Session.PresetName);
    }

    [Fact]
    public unsafe void Process_Renders_Into_The_Hosts_Buffers_And_Flags_Silence_Honestly()
    {
        var processor = new InstrumentProcessor();
        IAudioProcessor host = processor;
        host.SetupProcessing(new AudioProcessSetupData(AudioProcessMode.Realtime, AudioSampleSize.Float32, Block, SampleRate));
        using var output = new NativeBus(2, Block);
        var input = new AudioBusData(0, null, default, default);
        var outputs = new AudioBusData(1, output.Buffers, default, default);

        var peak = 0f;
        for (var i = 0; i < 20; i++)
        {
            var data = new AudioProcessData(IntPtr.Zero, AudioProcessMode.Realtime, AudioSampleSize.Float32, Block, in input, in outputs);
            host.Process(in data);
            peak = MathF.Max(peak, output.Peak());
        }

        Assert.True(peak > 0.01f, "the init patch should sound through the host's buffers");
        Assert.Equal(0UL, output.SilenceFlags);

        processor.Session.Engine.Parameters.Set(ParamId.Mute, true);
        for (var i = 0; i < 20; i++)
        {
            var data = new AudioProcessData(IntPtr.Zero, AudioProcessMode.Realtime, AudioSampleSize.Float32, Block, in input, in outputs);
            host.Process(in data);
        }

        Assert.Equal(0f, output.Peak(), 4);
        Assert.Equal(3UL, output.SilenceFlags);

        var flush = new AudioProcessData(IntPtr.Zero, AudioProcessMode.Realtime, AudioSampleSize.Float32, 0, in input, in input);
        host.Process(in flush);   // no buses at all, as a host flushing parameters sends it
    }

    [Fact]
    public unsafe void Process_Does_Not_Allocate_In_Steady_State()
    {
        var processor = new EffectProcessor();
        IAudioProcessor host = processor;
        host.SetupProcessing(new AudioProcessSetupData(AudioProcessMode.Realtime, AudioSampleSize.Float32, Block, SampleRate));
        using var inputBus = new NativeBus(2, Block);
        using var outputBus = new NativeBus(2, Block);
        var inputs = new AudioBusData(1, inputBus.Buffers, default, default);
        var outputs = new AudioBusData(1, outputBus.Buffers, default, default);

        var before = 0L;
        for (var i = 0; i < 70; i++)
        {
            if (i == 20)
                before = GC.GetAllocatedBytesForCurrentThread();

            var data = new AudioProcessData(IntPtr.Zero, AudioProcessMode.Realtime, AudioSampleSize.Float32, Block, in inputs, in outputs);
            host.Process(in data);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
