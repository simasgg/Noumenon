using System.Globalization;
using Noumenon.Dsp.Shared;
using Noumenon.Engine;
using Noumenon.Engine.Parameters;
using Noumenon.Standalone.Audio;
using Noumenon.Standalone.Input;

namespace Noumenon.Standalone.Developer;

/// <summary>
/// Read-only view of what the engine is doing that no parameter shows: the P faders as the
/// engine hears them, the envelope follower and gate envelope, the sample position, a morph in
/// progress, the lane clock and lane counts, the notes the Resochord is ringing at, and the MIDI
/// state — plus the few actions that are not parameters: storing the ringing notes as a user
/// chord, releasing every note, cancelling a morph.
/// </summary>
internal sealed class MonitorPanel : UserControl
{
    private readonly NoumenonEngine engine;
    private readonly MidiHost midi;
    private readonly KeyboardMidi keyboard;
    private readonly TableLayoutPanel table;
    private readonly Dictionary<string, Label> values = new();
    private string lastSeedText = "none yet";

    public MonitorPanel(NoumenonEngine engine, MidiHost midi, KeyboardMidi keyboard)
    {
        this.engine = engine;
        this.midi = midi;
        this.keyboard = keyboard;
        AutoScroll = true;

        table = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8), Location = new Point(0, 0) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 620));
        foreach (var name in new[] { "Engine", "P1 / P2", "Envelope follower", "Gate envelope", "Sample position", "Morph", "Lane clock", "Lanes", "Resochord notes", "MIDI input", "Last note / controller", "Held / bend / wheel / AT", "Keyboard MIDI", "Last random seed" })
            AddRow(name);

        var actions = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Width = 800 };
        actions.Controls.Add(new Label { Text = "Store ringing notes as user chord:", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
        for (var slot = 0; slot < NoumenonEngine.UserChordCount; slot++)
        {
            var index = slot;
            var button = new Button { Text = (slot + 1).ToString(CultureInfo.InvariantCulture), Width = 32 };
            button.Click += (_, _) =>
            {
                engine.StoreChord(index);
                Set("Resochord notes", ResochordText() + $"  → stored as User {index + 1}");
            };
            actions.Controls.Add(button);
        }

        var allOff = new Button { Text = "All notes off", AutoSize = true };
        allOff.Click += (_, _) =>
        {
            keyboard.ReleaseAll();
            engine.Midi.AllNotesOff();
        };
        var clearVoices = new Button { Text = "Forget ResoMidi voices", AutoSize = true };
        clearVoices.Click += (_, _) => engine.Midi.ClearVoiceNotes();
        var cancelMorph = new Button { Text = "Cancel morph", AutoSize = true };
        cancelMorph.Click += (_, _) => engine.CancelMorph();
        actions.Controls.AddRange([allOff, clearVoices, cancelMorph]);

        table.Controls.Add(new Label { Text = "Actions", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, table.RowCount);
        table.Controls.Add(actions, 1, table.RowCount);
        table.RowCount++;
        Controls.Add(table);
    }

    public void SetLastSeed(string text) => lastSeedText = text;

    public void Poll()
    {
        var inv = CultureInfo.InvariantCulture;
        var bank = engine.Parameters;
        Set("Engine", $"{engine.HostSampleRate.ToString("0", inv)} Hz host · {engine.Oversampling}× ({engine.SampleRate.ToString("0", inv)} Hz internal) · latency {engine.LatencySamples} samples · seed {engine.Seed} · scale {engine.ActiveScale.Name}");
        Set("P1 / P2", $"{engine.P1.ToString("+0.00;-0.00;0.00", inv)} st / {engine.P2.ToString("+0.00;-0.00;0.00", inv)} st  (faders {bank.Get(ParamId.P1Value).ToString("+0.0;-0.0;0.0", inv)} / {bank.Get(ParamId.P2Value).ToString("+0.0;-0.0;0.0", inv)}, MIDI {(bank.GetBool(ParamId.P1Midi) ? "on" : "off")} / {(bank.GetBool(ParamId.P2Midi) ? "on" : "off")})");
        Set("Envelope follower", Bar(engine.EnvelopeFollowerValue) + " " + engine.EnvelopeFollowerValue.ToString("0.000", inv));
        Set("Gate envelope", Bar(engine.GateEnvelopeValue) + " " + engine.GateEnvelopeValue.ToString("0.000", inv) + (bank.GetInt(ParamId.PlayMode) == 0 ? " (free-run)" : engine.Midi.HeldCount > 0 ? " (gate open)" : " (gate closed)"));
        var sample = engine.Samples.Get(bank.GetInt(ParamId.SamplerSelect));
        Set("Sample position", sample is null ? "no sample on the selected key" : $"{engine.SamplePosition.ToString("0.000", inv)} of {sample.Name} ({sample.Data.Seconds.ToString("0.00", inv)} s)");
        Set("Morph", engine.IsMorphing ? $"{(engine.MorphProgress * 100).ToString("0", inv)} %" : "idle");
        Set("Lane clock", $"speed ×{engine.Clock.Speed.ToString("0.00", inv)} · bar {(engine.Clock.BarSeconds > 0 ? engine.Clock.BarSeconds.ToString("0.00", inv) + " s" : "unsynced")} · host {(engine.Clock.HostPlaying ? "playing" : "stopped")} at {engine.Clock.HostTempo.ToString("0.#", inv)} BPM · lane limit {engine.Lanes.MaxSeconds.ToString("0", inv)} s");
        var playing = 0;
        var recording = 0;
        var armed = 0;
        var loops = 0;
        foreach (var lane in engine.Lanes.All)
        {
            if (lane.IsPlaying)
                playing++;

            if (lane.IsRecording)
                recording++;

            if (lane.Armed)
                armed++;

            if (lane.HasRecording)
                loops++;
        }

        Set("Lanes", $"{playing} playing · {recording} recording · {armed} armed · {loops} with loops · {engine.Lanes.Count} lanes");
        Set("Resochord notes", ResochordText());
        Set("MIDI input", midi.DeviceName is null ? "no port open" + (midi.Error is null ? "" : " — " + midi.Error) : $"{midi.DeviceName} · {midi.Messages} messages{(midi.Error is null ? "" : " — " + midi.Error)}");
        Set("Last note / controller", $"{(midi.LastNote >= 0 ? ParamInfo.NoteName(midi.LastNote) + " vel " + midi.LastVelocity : "—")} · {(midi.LastController >= 0 ? "CC " + midi.LastController + " = " + midi.LastControllerValue : "—")} · engine last note {(engine.Midi.LastNote >= 0 ? ParamInfo.NoteName(engine.Midi.LastNote) : "—")}");
        Set("Held / bend / wheel / AT", $"{engine.Midi.HeldCount} held · bend {engine.Midi.Bend.ToString("+0.00;-0.00;0.00", inv)} · wheel {engine.Midi.Wheel.ToString("0.00", inv)} · aftertouch {engine.Midi.Aftertouch.ToString("0.00", inv)} · CC{engine.Midi.MatrixController} {engine.Midi.Controller(engine.Midi.MatrixController).ToString("0.00", inv)}");
        Set("Keyboard MIDI", $"Z = {ParamInfo.NoteName(keyboard.BaseNote)}, Q = {ParamInfo.NoteName(keyboard.BaseNote + 12)} · {keyboard.HeldCount} keys down · minus / equals shift octaves");
        Set("Last random seed", lastSeedText);
    }

    private string ResochordText()
    {
        var inv = CultureInfo.InvariantCulture;
        var parts = new string[ParameterTable.ResoVoiceCount];
        var a4 = engine.Parameters.Get(ParamId.TuningA4);
        for (var v = 0; v < parts.Length; v++)
        {
            var note = engine.ResochordNote(v);
            parts[v] = $"{ParamInfo.NoteName(Math.Clamp((int)MathF.Round(note), 0, 127))} ({Tuning.NoteToHz(note, a4).ToString("0.0", inv)} Hz)";
        }

        return string.Join("  ", parts);
    }

    private static string Bar(float value)
    {
        var filled = Math.Clamp((int)MathF.Round(value * 20f), 0, 20);

        return "[" + new string('#', filled) + new string('.', 20 - filled) + "]";
    }

    private void AddRow(string name)
    {
        var caption = new Label { Text = name, AutoSize = true, Padding = new Padding(0, 4, 0, 4) };
        var value = new Label { Text = "", AutoSize = true, Padding = new Padding(0, 4, 0, 4), MaximumSize = new Size(620, 0) };
        values[name] = value;
        table.Controls.Add(caption, 0, table.RowCount);
        table.Controls.Add(value, 1, table.RowCount);
        table.RowCount++;
    }

    private void Set(string name, string text)
    {
        var label = values[name];
        if (label.Text != text)
            label.Text = text;
    }
}
