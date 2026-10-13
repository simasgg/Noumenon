using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Jacobi.Vst.Core;
using Jacobi.Vst.Plugin.Framework;
using Noumenon.Engine.Parameters;

namespace Noumenon.Vst2.Tests;

/// <summary>
/// Drives the wrapper as a host would, in process: the identity and parameter list it
/// advertises, the two-way parameter bridge, audio blocks with MIDI events cut into them, the
/// state chunk, the bypass, the real-time guard and the editor inside a foreign window.
/// </summary>
public class PluginTests
{
    private const int SampleRate = 48000;
    private const int Block = 512;

    private static NoumenonPlugin Open()
    {
        var plugin = new NoumenonPlugin();
        plugin.Parameters.Attach(plugin.Programs.ActiveProgram!.Parameters);   // what Open(host) does; there is no host here
        plugin.Audio.SampleRate = SampleRate;
        plugin.Audio.BlockSize = Block;

        return plugin;
    }

    private static float Render(NoumenonPlugin plugin, HostBuffers output, int blocks)
    {
        var peak = 0f;
        for (var i = 0; i < blocks; i++)
        {
            plugin.Audio.Process([], output.Buffers);
            peak = MathF.Max(peak, output.Peak());
        }

        return peak;
    }

    private static void Send(NoumenonPlugin plugin, int delta, params byte[] data) =>
        ((IVstMidiProcessor)plugin.Audio).Process(new VstEventCollection { new VstMidiEvent(delta, 0, 0, data, 0, 0) });

    [Fact]
    public void Advertises_The_Instrument_Identity_And_Every_Parameter()
    {
        var plugin = Open();
        Assert.Equal("Noumenon", plugin.Name);
        Assert.Equal(PluginIdentity.InstrumentId, plugin.PluginID);
        Assert.Equal(VstPluginCategory.Synth, plugin.Category);
        Assert.Equal(0, plugin.Audio.InputCount);
        Assert.Equal(2, plugin.Audio.OutputCount);
        Assert.Equal(plugin.Engine.LatencySamples, ((IVstPlugin)plugin).InitialDelay);
        Assert.True(plugin.Supports<IVstPluginAudioProcessor>());
        Assert.True(plugin.Supports<IVstMidiProcessor>());
        Assert.True(plugin.Supports<IVstPluginBypass>());
        Assert.True(plugin.Supports<IVstPluginPersistence>());
        Assert.True(plugin.Supports<IVstPluginPrograms>());
        Assert.True(plugin.Supports<IVstPluginEditor>());

        var parameters = plugin.Programs.ActiveProgram!.Parameters;
        Assert.Equal(ParameterTable.Count, parameters.Count);
        for (var i = 0; i < parameters.Count; i++)
        {
            var info = ParameterTable.All[i];
            Assert.Equal(info.ShortName, parameters[i].Info.Name);
            Assert.Equal(info.ToNormalized(info.Default), parameters[i].Value, 5);
            Assert.Equal(info.Format(info.Default), plugin.Parameters.Display(i));
        }
    }

    [Fact]
    public void Host_Writes_Reach_The_Bank_At_The_Next_Block_And_Are_Not_Echoed()
    {
        var plugin = Open();
        var parameters = plugin.Programs.ActiveProgram!.Parameters;
        var cutoff = ParameterTable.Get(ParamId.MasterFilterCutoff);
        using var output = new HostBuffers(2, Block);

        parameters[(int)ParamId.MasterFilterCutoff].Value = 0.25f;   // what the host's setParameter does
        parameters[(int)ParamId.ReverbRoom].Value = 0.3f;            // between two rooms
        Assert.NotEqual(cutoff.FromNormalized(0.25f), plugin.Engine.Parameters.Get(ParamId.MasterFilterCutoff));
        plugin.Audio.Process([], output.Buffers);

        Assert.Equal(cutoff.FromNormalized(0.25f), plugin.Engine.Parameters.Get(ParamId.MasterFilterCutoff), 2);
        Assert.Equal(ParameterTable.Get(ParamId.ReverbRoom).FromNormalized(0.3f), plugin.Engine.Parameters.Get(ParamId.ReverbRoom));
        Assert.Equal(cutoff.Format(cutoff.FromNormalized(0.25f)), plugin.Parameters.Display((int)ParamId.MasterFilterCutoff));

        // The panel's poll finds nothing to send back: the host's values still mean what the bank holds.
        Assert.Equal(0, plugin.Parameters.PushToHost(notify: true));
        Assert.Equal(0.3f, parameters[(int)ParamId.ReverbRoom].Value);
    }

    [Fact]
    public void Panel_Changes_Reach_The_Host_Once_And_Typed_Values_Parse()
    {
        var plugin = Open();
        var bank = plugin.Engine.Parameters;
        var parameters = plugin.Programs.ActiveProgram!.Parameters;
        using var output = new HostBuffers(2, Block);

        bank.Set(ParamId.OutputVolume, -12f);
        Assert.Equal(1, plugin.Parameters.PushToHost(notify: true));
        Assert.Equal(ParameterTable.Get(ParamId.OutputVolume).ToNormalized(-12f), parameters[(int)ParamId.OutputVolume].Value, 5);
        Assert.Equal(0, plugin.Parameters.PushToHost(notify: true));
        plugin.Audio.Process([], output.Buffers);   // nor does the next block's pull undo it
        Assert.Equal(-12f, bank.Get(ParamId.OutputVolume), 3);
        Assert.Equal("-12.0 dB", plugin.Parameters.Display((int)ParamId.OutputVolume));

        Assert.True(plugin.Parameters.Parse((int)ParamId.ReverbRoom, ParameterTable.ReverbRooms[1]));
        Assert.Equal(1, bank.GetInt(ParamId.ReverbRoom));
        Assert.True(plugin.Parameters.Parse((int)ParamId.MasterFilterCutoff, "1.5 kHz"));
        Assert.Equal(1500f, bank.Get(ParamId.MasterFilterCutoff), 1);
        Assert.True(plugin.Parameters.Parse((int)ParamId.Mute, "on"));
        Assert.True(bank.GetBool(ParamId.Mute));
        Assert.False(plugin.Parameters.Parse((int)ParamId.Mute, "sideways"));
        Assert.Equal(ParameterTable.Get(ParamId.MasterFilterCutoff).ToNormalized(1500f), parameters[(int)ParamId.MasterFilterCutoff].Value, 5);
    }

    [Fact]
    public void Renders_Sound_And_Cuts_Blocks_At_Midi_Events()
    {
        var plugin = Open();
        var bank = plugin.Engine.Parameters;
        bank.Set(ParamId.P1Midi, true);
        bank.Set(ParamId.P1Glide, 0f);
        using var output = new HostBuffers(2, Block);

        Assert.True(Render(plugin, output, 20) > 0.01f, "the init patch should sound from the first blocks");

        Send(plugin, 256, 0x90, 72, 100);
        Assert.Equal(1, plugin.Audio.PendingEventCount);
        plugin.Audio.Process([], output.Buffers);
        Assert.Equal(0, plugin.Audio.PendingEventCount);
        Render(plugin, output, 10);
        Assert.Equal(72, plugin.Engine.Midi.LastNote);
        Assert.InRange(plugin.Engine.P1, 11.5f, 12.5f);   // note 72 is twelve semitones above middle C

        Send(plugin, 0, 0xE0, 0x7F, 0x7F);   // bend fully up, range 2
        Render(plugin, output, 10);
        Assert.InRange(plugin.Engine.P1, 13.5f, 14.5f);

        Send(plugin, 0, 0xB0, 1, 127);       // the mod wheel
        Render(plugin, output, 2);
        Assert.Equal(1f, plugin.Engine.Midi.Wheel);

        Send(plugin, 0, 0x80, 72, 0);
        Render(plugin, output, 2);
        Assert.Equal(0, plugin.Engine.Midi.HeldCount);
    }

    [Fact]
    public void State_Chunk_Round_Trips_Through_A_Fresh_Plugin()
    {
        var source = Open();
        source.Engine.Parameters.Set(ParamId.OutputVolume, -9f);
        source.Engine.Parameters.Set(ParamId.ReverbRoom, 3);
        source.Engine.SetUserChord(1, [40f, 47f, 52f, 59f, 64f, 71f]);
        source.Session.SetPreset("Chunky", null);

        var bytes = source.State.Save();
        Assert.Equal((byte)'{', bytes[0]);

        var target = Open();
        Assert.Equal(bytes.Length, target.State.Load(bytes));
        Assert.Equal(-9f, target.Engine.Parameters.Get(ParamId.OutputVolume));
        Assert.Equal(3, target.Engine.Parameters.GetInt(ParamId.ReverbRoom));
        Assert.Equal([40f, 47f, 52f, 59f, 64f, 71f], target.Engine.UserChord(1));
        Assert.Equal("Chunky", target.Session.PresetName);
        Assert.Equal("Chunky", target.Session.ProgramName);

        // The host's parameter objects already show the loaded values, without waiting for the editor's poll.
        Assert.Equal(ParameterTable.Get(ParamId.OutputVolume).ToNormalized(-9f), target.Programs.ActiveProgram!.Parameters[(int)ParamId.OutputVolume].Value, 5);
        Assert.Equal(0, target.Parameters.PushToHost(notify: true));

        Assert.Equal(0, target.State.Load([]));
        Assert.Equal(0, target.State.Load("nonsense"u8.ToArray()));
        Assert.Equal(-9f, target.Engine.Parameters.Get(ParamId.OutputVolume));
        Assert.True(target.State.CanLoadChunk(new VstPatchChunkInfo(1, PluginIdentity.EffectId, PluginIdentity.Version, 0)));
        Assert.True(target.State.CanLoadChunk(new VstPatchChunkInfo(1, PluginIdentity.InstrumentId, PluginIdentity.Version, 0)));
        Assert.False(target.State.CanLoadChunk(new VstPatchChunkInfo(1, 0x45735276, PluginIdentity.Version, 0)));
    }

    [Fact]
    public void Bypass_Silences_The_Instrument()
    {
        // The init patch's Drift is a through-delay at Mix 1, so the first ~120 ms are the delay
        // filling; 20 blocks of 512 at 48 kHz (~213 ms) clears it, as the render test relies on too.
        var plugin = Open();
        using var output = new HostBuffers(2, Block);
        Assert.True(Render(plugin, output, 20) > 0.01f);

        plugin.Audio.Bypass = true;
        Array.Fill(output.Arrays[0], 1f);
        Array.Fill(output.Arrays[1], 1f);
        plugin.Audio.Process([], output.Buffers);
        Assert.Equal(0f, output.Peak());

        plugin.Audio.Bypass = false;
        Assert.True(Render(plugin, output, 20) > 0.01f);
    }

    [Fact]
    public void Engine_Rate_Change_Re_Prepares_Between_Blocks_And_Changes_The_Latency()
    {
        var plugin = Open();
        using var output = new HostBuffers(2, Block);
        Render(plugin, output, 2);
        var latency = plugin.Engine.LatencySamples;

        plugin.Engine.Parameters.Set(ParamId.EngineRate, 1);
        Render(plugin, output, 2);
        Assert.Equal(2, plugin.Engine.Oversampling);
        Assert.NotEqual(latency, plugin.Engine.LatencySamples);
        Assert.Equal(plugin.Engine.LatencySamples, ((IVstPlugin)plugin).InitialDelay);
        Assert.True(Render(plugin, output, 10) > 0.01f);
    }

    [Fact]
    public void Process_Does_Not_Allocate_In_Steady_State()
    {
        var plugin = Open();
        var midi = (IVstMidiProcessor)plugin.Audio;
        using var output = new HostBuffers(2, Block);
        var events = new VstEventCollection
        {
            new VstMidiEvent(100, 0, 0, [0x90, 60, 90], 0, 0),
            new VstMidiEvent(300, 0, 0, [0x80, 60, 0], 0, 0),
        };

        for (var i = 0; i < 20; i++)
        {
            midi.Process(events);
            plugin.Audio.Process([], output.Buffers);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 50; i++)
        {
            midi.Process(events);
            plugin.Audio.Process([], output.Buffers);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void Editor_Opens_Inside_A_Host_Window_And_Keeps_Its_Size()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var plugin = Open();
                var editor = plugin.Editor;
                using var window = new Form { ClientSize = editor.Bounds.Size, StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000), ShowInTaskbar = false };
                window.Show();
                editor.Open(window.Handle);
                Pump(300);

                var control = editor.Control;
                Assert.NotNull(control);
                Assert.True(control.IsHandleCreated);
                Assert.Equal(window.Handle, GetParent(control.Handle));
                Assert.Equal(new Size(1000, 664), editor.Bounds.Size);

                control.PanelScale = 1.5f;
                Assert.Equal(new Size(1500, 996), editor.Bounds.Size);

                plugin.Engine.Parameters.Set(ParamId.Mute, true);   // the poll pushes a panel change to the host's parameter
                Pump(200);
                Assert.Equal(1f, plugin.Programs.ActiveProgram!.Parameters[(int)ParamId.Mute].Value);

                editor.Close();
                Assert.Null(editor.Control);
                editor.Open(window.Handle);
                Pump(100);
                Assert.Equal(new Size(1500, 996), editor.Bounds.Size);   // the panel size survives reopening
                editor.Close();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Pump(int milliseconds)
    {
        var end = Environment.TickCount64 + milliseconds;
        while (Environment.TickCount64 < end)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    [DllImport("user32.dll")]
    private static extern nint GetParent(nint hWnd);
}
