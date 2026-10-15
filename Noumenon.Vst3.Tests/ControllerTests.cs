using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using NPlug;
using Noumenon.Engine.Parameters;
using Noumenon.Engine.Presets;
using Noumenon.Vst3.Editor;
using Noumenon.Vst3.Processing;

namespace Noumenon.Vst3.Tests;

/// <summary>
/// The controller as a host sees it: panel changes reach the host once, as edits, and a gesture is
/// one touch; values the host set are never sent back; a whole new snapshot is a restart, not
/// hundreds of edits; a latency change asks the host to re-read it; the processor's state becomes
/// the values the host displays; the MIDI controllers are mapped; and the editor opens inside a
/// host window, passes its widget self-test there, resizes through the host's frame and keeps its
/// scale.
/// </summary>
public class ControllerTests
{
    private static (NoumenonController Controller, PluginSession Session, RecordingHandler Handler) Open()
    {
        var session = new PluginSession(isEffect: false);
        var controller = new NoumenonController();
        controller.Attach(session);
        var handler = new RecordingHandler();
        ((IAudioController)controller).SetControllerHandler(handler);

        return (controller, session, handler);
    }

    private static int Id(ParamId id) => PluginIdentity.ToVst3Id(id);

    [Fact]
    public void A_Fresh_Controller_Shows_The_Defaults()
    {
        IAudioController controller = new NoumenonController();

        foreach (var info in ParameterTable.All)
            Assert.Equal(info.ToNormalized(info.Default), controller.GetParameterNormalized(Id(info.Id)), 6);

        Assert.Equal(0.5, controller.GetParameterNormalized(PluginIdentity.MidiControllerIdBase + PluginIdentity.PitchBendController));
    }

    [Fact]
    public void Panel_Changes_Reach_The_Host_Once_As_Edits()
    {
        var (controller, session, handler) = Open();
        session.Engine.Parameters.Set(ParamId.OutputVolume, -12f);

        Assert.Equal(1, controller.PushChanges());
        var id = Id(ParamId.OutputVolume);
        Assert.Equal([$"begin {id}", $"perform {id}", $"end {id}"], handler.Calls);
        var normalized = ParameterTable.Get(ParamId.OutputVolume).ToNormalized(-12f);
        Assert.Equal(normalized, handler.Performed[0].Value, 6);
        Assert.Equal(normalized, ((IAudioController)controller).GetParameterNormalized(id), 6);
        Assert.Equal(0, controller.PushChanges());
    }

    [Fact]
    public void A_Panel_Gesture_Is_One_Touch()
    {
        var (controller, session, handler) = Open();
        var bank = session.Engine.Parameters;
        var id = Id(ParamId.MasterFilterCutoff);

        controller.GestureStarted();
        bank.Set(ParamId.MasterFilterCutoff, 800f);
        controller.PushChanges();
        bank.Set(ParamId.MasterFilterCutoff, 600f);
        controller.PushChanges();
        bank.Set(ParamId.MasterFilterCutoff, 500f);
        controller.GestureEnded();

        Assert.Equal([$"begin {id}", $"perform {id}", $"perform {id}", $"perform {id}", $"end {id}"], handler.Calls);
        Assert.Equal(ParameterTable.Get(ParamId.MasterFilterCutoff).ToNormalized(500f), handler.Performed[^1].Value, 6);
    }

    [Fact]
    public void Values_The_Host_Set_Are_Not_Sent_Back()
    {
        var (controller, session, handler) = Open();
        var renderer = new BlockRenderer(session);
        renderer.Prepare(48000, 512);
        renderer.AddParameter(0, Id(ParamId.MasterFilterCutoff), 0.25);
        renderer.AddParameter(0, Id(ParamId.ReverbRoom), 0.3);   // between two rooms: the bank snaps to one
        renderer.Flush();

        Assert.Equal(ParameterTable.Get(ParamId.MasterFilterCutoff).FromNormalized(0.25f), session.Engine.Parameters.Get(ParamId.MasterFilterCutoff), 2);
        Assert.Equal(0, controller.PushChanges());
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public void A_New_Snapshot_Is_A_Restart_Not_Edits()
    {
        var (controller, session, handler) = Open();
        var state = session.Engine.CapturePreset("Elsewhere");
        state.Parameters[nameof(ParamId.OutputVolume)] = -9f;
        state.Parameters[nameof(ParamId.ReverbRoom)] = 3f;

        session.Apply(state, null);
        controller.SnapshotChanged();

        Assert.Contains(AudioRestartFlags.ParamValuesChanged, handler.Restarts);
        Assert.True(handler.Dirty);
        Assert.Empty(handler.Calls);
        Assert.Equal(0, controller.PushChanges());
        Assert.Equal(ParameterTable.Get(ParamId.OutputVolume).ToNormalized(-9f), ((IAudioController)controller).GetParameterNormalized(Id(ParamId.OutputVolume)), 6);
        Assert.Equal("Elsewhere", session.PresetName);
    }

    [Fact]
    public void A_Latency_Change_Asks_The_Host_To_Re_Read_It()
    {
        var (controller, session, handler) = Open();
        var renderer = new BlockRenderer(session);
        renderer.Prepare(48000, 512);
        controller.PollLatency();
        Assert.Empty(handler.Restarts);

        session.Engine.Parameters.Set(ParamId.EngineRate, 1);
        renderer.Render([], [], new float[512], new float[512], 512);
        controller.PollLatency();
        controller.PollLatency();

        Assert.Equal([AudioRestartFlags.LatencyChanged], handler.Restarts);
    }

    [Fact]
    public void The_Processors_State_Becomes_The_Values_The_Host_Displays()
    {
        IAudioProcessor processor = new InstrumentProcessor();
        var source = ((InstrumentProcessor)processor).Session;
        source.Engine.Parameters.Set(ParamId.OutputVolume, -9f);
        source.Engine.Parameters.Set(ParamId.ReverbRoom, 3);
        using var stream = new MemoryStream();
        processor.GetState(stream);

        IAudioController controller = new NoumenonController();
        stream.Position = 0;
        controller.SetComponentState(stream);

        Assert.Equal(ParameterTable.Get(ParamId.OutputVolume).ToNormalized(-9f), controller.GetParameterNormalized(Id(ParamId.OutputVolume)), 6);
        Assert.Equal(ParameterTable.Get(ParamId.ReverbRoom).ToNormalized(3f), controller.GetParameterNormalized(Id(ParamId.ReverbRoom)), 6);
        Assert.Equal(ParameterTable.Get(ParamId.MasterFilterCutoff).ToNormalized(ParameterTable.Get(ParamId.MasterFilterCutoff).Default), controller.GetParameterNormalized(Id(ParamId.MasterFilterCutoff)), 6);
    }

    [Fact]
    public void The_Controllers_Own_State_Keeps_The_Panel_Scale()
    {
        var source = new NoumenonController { PanelScale = 1.5f, PanelScaleChosen = true };
        using var stream = new MemoryStream();
        ((IAudioController)source).GetState(stream);

        var target = new NoumenonController();
        stream.Position = 0;
        ((IAudioController)target).SetState(stream);
        Assert.Equal(1.5f, target.PanelScale);
        Assert.True(target.PanelScaleChosen);

        var untouched = new NoumenonController();
        ((IAudioController)untouched).SetState(new MemoryStream([1, 2, 3]));
        Assert.Equal(1f, untouched.PanelScale);
    }

    [Fact]
    public void Midi_Controllers_Map_To_The_Hidden_Parameters()
    {
        IAudioControllerMidiMapping mapping = new NoumenonController();

        Assert.True(mapping.TryGetMidiControllerAssignment(0, 0, AudioMidiControllerNumber.PitchBend, out var bend));
        Assert.Equal(PluginIdentity.MidiControllerIdBase + PluginIdentity.PitchBendController, bend.Value);
        Assert.True(mapping.TryGetMidiControllerAssignment(0, 5, AudioMidiControllerNumber.ModWheel, out var wheel));
        Assert.Equal(PluginIdentity.MidiControllerIdBase + 1, wheel.Value);
        Assert.True(mapping.TryGetMidiControllerAssignment(0, 0, AudioMidiControllerNumber.AfterTouch, out _));
        Assert.False(mapping.TryGetMidiControllerAssignment(0, 0, AudioMidiControllerNumber.ProgramChange, out _));
    }

    [Fact]
    public void The_Editor_Opens_In_A_Host_Window_Resizes_Through_The_Frame_And_Keeps_Its_Scale()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var (controller, session, handler) = Open();
                var frame = new RecordingFrame();
                var view = (NoumenonView)((IAudioController)controller).CreateView()!;
                view.SetFrame(frame);
                view.SetContentScaleFactor(1f);
                Assert.True(view.IsPlatformTypeSupported(AudioPluginViewPlatform.Hwnd));
                Assert.Equal(new ViewRectangle(0, 0, 1000, 664), view.Size);

                using var window = new Form { ClientSize = new Size(1000, 664), StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000), ShowInTaskbar = false };
                window.Show();
                view.Attached(window.Handle, AudioPluginViewPlatform.Hwnd);
                Pump(300);

                var control = view.Control;
                Assert.NotNull(control);
                Assert.True(control.IsHandleCreated);
                Assert.Equal(window.Handle, GetParent(control.Handle));

                control.PanelScale = 1.5f;   // the panel's own ×1.5 button
                Assert.Equal(new ViewRectangle(0, 0, 1500, 996), frame.Resizes[^1]);
                Assert.Equal(1.5f, controller.PanelScale);
                Assert.True(controller.PanelScaleChosen);

                session.Engine.Parameters.Set(ParamId.Mute, true);   // the poll pushes a panel change to the host
                Pump(200);
                Assert.Contains($"perform {Id(ParamId.Mute)}", handler.Calls);

                var selfTest = control.SelfTest();   // every widget driven through its gestures, which become host touches
                Assert.StartsWith("0 failures", selfTest);
                Assert.Equal(handler.Calls.Count(call => call.StartsWith("begin", StringComparison.Ordinal)), handler.Calls.Count(call => call.StartsWith("end", StringComparison.Ordinal)));

                view.Removed();
                Assert.Null(view.Control);

                var reopened = (NoumenonView)((IAudioController)controller).CreateView()!;
                reopened.SetContentScaleFactor(2f);   // the user chose a scale: the host's DPI no longer decides
                Assert.Equal(new ViewRectangle(0, 0, 1500, 996), reopened.Size);
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

    [Fact]
    public void The_Hosts_Dpi_Picks_The_First_Panel_Scale()
    {
        var (controller, _, _) = Open();
        var view = (NoumenonView)((IAudioController)controller).CreateView()!;

        view.SetContentScaleFactor(1.5f);

        Assert.Equal(new ViewRectangle(0, 0, 1500, 996), view.Size);
        Assert.Equal(1.5f, controller.PanelScale);
        Assert.False(controller.PanelScaleChosen);
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
