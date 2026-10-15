using System.Globalization;
using NAudio.Midi;
using Noumenon.Engine;
using Noumenon.Engine.Modulation;
using Noumenon.Engine.Parameters;
using Noumenon.Engine.Presets;
using Noumenon.TestHarness;

// Offline render harness: the engine → WAV, no GUI, no DAW.
//   Usage: Noumenon.TestHarness [options]
//     --seconds <n>        length to render (default 10)
//     --rate <hz>          sample rate (default 48000)
//     --block <n>          block size the render is cut into (default 512)
//     --preset <path|name> load a .noumenon preset (a bare name is looked up in content/presets)
//     --seed <n>           randomize from this seed (default: no randomization)
//     --scope <name>       what --seed randomizes: All, Oscillators, Sampler, Effects, Lanes (default All)
//     --amount <0..1>      how far the randomizer strays from a tonal drone (default 1)
//     --p1 <st> / --p2 <st>  the P1 / P2 fader values in semitones
//     --sample <path>      load a WAV/AIFF into sample key 0 and select it
//     --input <path>       feed a WAV as the live input (the FX identity); shorter files are zero-padded
//     --sidechain <path>   feed a WAV as the sidechain (only the envelope follower hears it: Env Follower Source = Sidechain)
//     --midi <path>        play a standard MIDI file into the engine (notes, bend, controllers, aftertouch)
//     --tempo <bpm>        host tempo, playing (for Host Sync and Restart On Host Play)
//     --gate <seconds>     fire a MIDI gate at this time (repeatable)
//     --set <Name=Value>   override one parameter (enum name or display name; repeatable)
//     --at <seconds> <Name=Value>  change a parameter at that time during the render (repeatable)
//     --save-preset <path> write the prepared state as a preset before rendering
//     --preset-name <name> the name stored by --save-preset
//     --out <path>         output file (default ./noumenon_out.wav, 24-bit stereo)
//     --dump-params        print the parameter table and exit
var seconds = 10.0;
var sampleRate = 48000;
var block = 512;
string? presetPath = null;
ulong? seed = null;
var scope = RandomizeScope.All;
var amount = 1f;
float? p1 = null;
float? p2 = null;
string? samplePath = null;
string? inputPath = null;
string? sidechainPath = null;
string? midiPath = null;
double? tempo = null;
var gates = new List<double>();
var outputPath = Path.GetFullPath("noumenon_out.wav");
var overrides = new List<(string Name, float Value)>();
var timedSets = new List<(double Seconds, string Name, float Value)>();
string? savePresetPath = null;
string? presetName = null;
var dumpParams = false;

for (var i = 0; i < args.Length; i++)
{
    var a = args[i];
    var hasValue = i + 1 < args.Length;
    if (Is(a, "--seconds") && hasValue)
        seconds = double.Parse(args[++i], CultureInfo.InvariantCulture);
    else if (Is(a, "--rate") && hasValue)
        sampleRate = int.Parse(args[++i], CultureInfo.InvariantCulture);
    else if (Is(a, "--block") && hasValue)
        block = int.Parse(args[++i], CultureInfo.InvariantCulture);
    else if (Is(a, "--preset") && hasValue)
        presetPath = args[++i];
    else if (Is(a, "--seed") && hasValue)
        seed = ulong.Parse(args[++i], CultureInfo.InvariantCulture);
    else if (Is(a, "--scope") && hasValue)
        scope = Enum.Parse<RandomizeScope>(args[++i], true);
    else if (Is(a, "--amount") && hasValue)
        amount = float.Parse(args[++i], CultureInfo.InvariantCulture);
    else if (Is(a, "--p1") && hasValue)
        p1 = float.Parse(args[++i], CultureInfo.InvariantCulture);
    else if (Is(a, "--p2") && hasValue)
        p2 = float.Parse(args[++i], CultureInfo.InvariantCulture);
    else if (Is(a, "--sample") && hasValue)
        samplePath = args[++i];
    else if (Is(a, "--input") && hasValue)
        inputPath = args[++i];
    else if (Is(a, "--sidechain") && hasValue)
        sidechainPath = args[++i];
    else if (Is(a, "--midi") && hasValue)
        midiPath = args[++i];
    else if (Is(a, "--tempo") && hasValue)
        tempo = double.Parse(args[++i], CultureInfo.InvariantCulture);
    else if (Is(a, "--gate") && hasValue)
        gates.Add(double.Parse(args[++i], CultureInfo.InvariantCulture));
    else if (Is(a, "--out") && hasValue)
        outputPath = Path.GetFullPath(args[++i]);
    else if (Is(a, "--save-preset") && hasValue)
        savePresetPath = Path.GetFullPath(args[++i]);
    else if (Is(a, "--preset-name") && hasValue)
        presetName = args[++i];
    else if (Is(a, "--set") && hasValue)
    {
        var parts = args[++i].Split('=', 2);
        if (parts.Length != 2)
        {
            Console.WriteLine($"--set expects Name=Value, got '{args[i]}'.");
            return 1;
        }

        overrides.Add((parts[0], float.Parse(parts[1], CultureInfo.InvariantCulture)));
    }
    else if (Is(a, "--at") && i + 2 < args.Length)
    {
        var at = double.Parse(args[++i], CultureInfo.InvariantCulture);
        var parts = args[++i].Split('=', 2);
        if (parts.Length != 2)
        {
            Console.WriteLine($"--at expects <seconds> Name=Value, got '{args[i]}'.");
            return 1;
        }

        timedSets.Add((at, parts[0], float.Parse(parts[1], CultureInfo.InvariantCulture)));
    }
    else if (Is(a, "--dump-params"))
        dumpParams = true;
    else
    {
        Console.WriteLine($"Unknown argument '{a}'.");
        return 1;
    }
}

if (dumpParams)
{
    DumpParameters();
    return 0;
}

var engine = new NoumenonEngine();
var bank = engine.Parameters;

if (presetPath is not null)
{
    var resolvedPath = FindPreset(presetPath);
    if (resolvedPath is null)
    {
        Console.WriteLine($"Preset '{presetPath}' not found (looked for a file and for content/presets/{presetPath}{PresetIo.Extension}).");
        return 1;
    }

    var warnings = new List<string>();
    var state = PresetIo.Load(resolvedPath);
    engine.ApplyPreset(state, loadSamples: true, warnings);
    Console.WriteLine($"Preset: {state.Name}  ({resolvedPath})");
    foreach (var warning in warnings)
        Console.WriteLine($"        warning: {warning}");
}

if (seed is { } s)
    Randomizer.Randomize(engine, s, scope, amount);

if (samplePath is not null)
{
    var slot = engine.Samples.Load(0, samplePath);
    bank.Set(ParamId.SamplerSelect, 0);
    Console.WriteLine($"Sample: {slot.Name}  ({slot.Data.Length} frames @ {slot.Data.SampleRate} Hz, {(slot.Data.IsStereo ? "stereo" : "mono")}, {slot.Data.Seconds.ToString("0.0", CultureInfo.InvariantCulture)} s) on key 0");
}

if (p1 is { } p1Value)
    bank.Set(ParamId.P1Value, p1Value);

if (p2 is { } p2Value)
    bank.Set(ParamId.P2Value, p2Value);

foreach (var (name, value) in overrides)
{
    var info = ParameterTable.Find(name);
    if (info is null)
    {
        Console.WriteLine($"Unknown parameter '{name}'. Use --dump-params to list them.");
        return 1;
    }

    bank.Set(info.Id, value);
}

var timed = new List<(long Frame, ParamId Id, float Value)>();
foreach (var (at, name, value) in timedSets)
{
    var info = ParameterTable.Find(name);
    if (info is null)
    {
        Console.WriteLine($"Unknown parameter '{name}'. Use --dump-params to list them.");
        return 1;
    }

    timed.Add(((long)(at * sampleRate), info.Id, value));
}

timed.Sort((x, y) => x.Frame.CompareTo(y.Frame));

if (tempo is { } bpm)
{
    engine.Clock.HostTempo = bpm;
    engine.Clock.HostPlaying = true;
}

engine.Prepare(sampleRate, block);
engine.Reset(seed ?? engine.Seed);

if (savePresetPath is not null)
{
    // After the reset: the first control tick has copied any pending lane snapshots in.
    var state = engine.CapturePreset(presetName ?? Path.GetFileNameWithoutExtension(savePresetPath));
    PresetIo.Save(state, savePresetPath);
    Console.WriteLine($"Saved : {savePresetPath}  ({state.Parameters.Count} parameters, {state.Lanes.Count} lanes)");
}

Console.WriteLine(seed is { } shown ? $"Patch : seed {shown} ({scope}), amount {amount.ToString("0.00", CultureInfo.InvariantCulture)}" : presetPath is null ? "Patch : init (table defaults)" : "Patch : preset");
DescribePatch(engine);

var total = (int)(seconds * sampleRate);
var left = new float[total];
var right = new float[total];
float[]? inputLeft = null;
float[]? inputRight = null;
if (inputPath is not null)
{
    var input = WavFile.Read(inputPath);
    if (input.SampleRate != sampleRate)
        Console.WriteLine($"Input : note: {inputPath} is {input.SampleRate} Hz, fed as-is at {sampleRate} Hz");

    inputLeft = new float[total];
    inputRight = new float[total];
    var frames = Math.Min(total, input.Length);
    Array.Copy(input.Left, inputLeft, frames);
    Array.Copy(input.Right, inputRight, frames);
    Console.WriteLine($"Input : {inputPath}  ({input.Length} frames, {(frames < total ? "zero-padded" : "truncated")} to {total})");
}

float[]? sidechainLeft = null;
float[]? sidechainRight = null;
if (sidechainPath is not null)
{
    var sidechain = WavFile.Read(sidechainPath);
    sidechainLeft = new float[total];
    sidechainRight = new float[total];
    var frames = Math.Min(total, sidechain.Length);
    Array.Copy(sidechain.Left, sidechainLeft, frames);
    Array.Copy(sidechain.Right, sidechainRight, frames);
    Console.WriteLine($"Side  : {sidechainPath}  ({sidechain.Length} frames, {(frames < total ? "zero-padded" : "truncated")} to {total})");
}

var midiEvents = midiPath is null ? [] : ReadMidi(midiPath, sampleRate);
if (midiPath is not null)
    Console.WriteLine($"MIDI  : {midiPath}  ({midiEvents.Count} events, last at {(midiEvents.Count > 0 ? midiEvents[^1].Frame / (double)sampleRate : 0).ToString("0.00", CultureInfo.InvariantCulture)} s)");

var silence = new float[total];
var gateFrames = gates.Select(g => (long)(g * sampleRate)).OrderBy(g => g).ToList();
var nextGate = 0;
var nextTimed = 0;
var nextMidi = 0;
var randomizeCount = 0UL;
for (var pos = 0; pos < total; pos += block)
{
    var count = Math.Min(block, total - pos);
    while (nextGate < gateFrames.Count && gateFrames[nextGate] < pos + count)
    {
        engine.Gate();
        nextGate++;
    }

    while (nextTimed < timed.Count && timed[nextTimed].Frame < pos + count)
    {
        bank.Set(timed[nextTimed].Id, timed[nextTimed].Value);
        nextTimed++;
    }

    while (nextMidi < midiEvents.Count && midiEvents[nextMidi].Frame < pos + count)
    {
        midiEvents[nextMidi].Apply(engine.Midi);
        nextMidi++;
    }

    if (engine.ConsumeRandomizeRequest())
    {
        Randomizer.Randomize(engine, (seed ?? 1) + 1000 * ++randomizeCount, (RandomizeScope)bank.GetInt(ParamId.RandomizeScope), bank.Get(ParamId.RandomizeAmount));
        Console.WriteLine($"Random: randomized at {(pos / (double)sampleRate).ToString("0.00", CultureInfo.InvariantCulture)} s");
    }

    if (sidechainLeft is not null && sidechainRight is not null)
        engine.Process(inputLeft ?? silence, inputRight ?? silence, sidechainLeft, sidechainRight, left, right, pos, count);
    else if (inputLeft is not null && inputRight is not null)
        engine.Process(inputLeft, inputRight, left, right, pos, count);
    else
        engine.Process(left, right, pos, count);
}

if (gates.Count > 0)
    Console.WriteLine($"Gates : {string.Join(", ", gates.Select(g => g.ToString("0.00", CultureInfo.InvariantCulture) + " s"))}");

if (timedSets.Count > 0)
    Console.WriteLine($"Timed : {string.Join(", ", timedSets.Select(t => $"{t.Seconds.ToString("0.00", CultureInfo.InvariantCulture)} s {t.Name}={t.Value.ToString(CultureInfo.InvariantCulture)}"))}");

Console.WriteLine($"Engine: {engine.Oversampling}x ({engine.SampleRate} Hz internal), latency {engine.LatencySamples} samples");

Analyze(left, right, sampleRate);

new WavFile { SampleRate = sampleRate, Left = left, Right = right }.Write(outputPath);
Console.WriteLine($"Output: {outputPath}  ({total} frames @ {sampleRate} Hz, {seconds.ToString("0.#", CultureInfo.InvariantCulture)} s)");
return 0;

static bool Is(string arg, string name) => arg.Equals(name, StringComparison.OrdinalIgnoreCase);

static string? FindPreset(string nameOrPath)
{
    if (File.Exists(nameOrPath))
        return Path.GetFullPath(nameOrPath);

    var fileName = nameOrPath.EndsWith(PresetIo.Extension, StringComparison.OrdinalIgnoreCase) ? nameOrPath : nameOrPath + PresetIo.Extension;
    foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        var dir = new DirectoryInfo(start);
        for (var depth = 0; depth < 6 && dir is not null; depth++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "content", "presets", fileName);
            if (File.Exists(candidate))
                return candidate;
        }
    }

    return null;
}

static List<(long Frame, Action<MidiInput> Apply)> ReadMidi(string path, int sampleRate)
{
    var midi = new MidiFile(path, false);
    var ticksPerQuarter = midi.DeltaTicksPerQuarterNote;
    var tempos = new List<TempoEvent>();
    for (var track = 0; track < midi.Tracks; track++)
        tempos.AddRange(midi.Events[track].OfType<TempoEvent>());

    tempos.Sort((x, y) => x.AbsoluteTime.CompareTo(y.AbsoluteTime));

    double TicksToSeconds(long ticks)
    {
        var elapsed = 0.0;
        var lastTick = 0L;
        var microsecondsPerQuarter = 500000.0;
        foreach (var tempoEvent in tempos)
        {
            if (tempoEvent.AbsoluteTime >= ticks)
                break;

            elapsed += (tempoEvent.AbsoluteTime - lastTick) / (double)ticksPerQuarter * microsecondsPerQuarter / 1e6;
            lastTick = tempoEvent.AbsoluteTime;
            microsecondsPerQuarter = tempoEvent.MicrosecondsPerQuarterNote;
        }

        return elapsed + (ticks - lastTick) / (double)ticksPerQuarter * microsecondsPerQuarter / 1e6;
    }

    var events = new List<(long Frame, Action<MidiInput> Apply)>();
    for (var track = 0; track < midi.Tracks; track++)
    {
        foreach (var midiEvent in midi.Events[track])
        {
            var frame = (long)(TicksToSeconds(midiEvent.AbsoluteTime) * sampleRate);
            switch (midiEvent)
            {
                case NoteOnEvent { Velocity: > 0 } on:
                    events.Add((frame, m => m.NoteOn(on.NoteNumber, on.Velocity)));
                    break;
                case NoteEvent off when off.CommandCode == MidiCommandCode.NoteOff || off is NoteOnEvent:
                    events.Add((frame, m => m.NoteOff(off.NoteNumber)));
                    break;
                case PitchWheelChangeEvent bend:
                    events.Add((frame, m => m.PitchBend(bend.Pitch)));
                    break;
                case ControlChangeEvent cc:
                    events.Add((frame, m => m.ControlChange((int)cc.Controller, cc.ControllerValue)));
                    break;
                case ChannelAfterTouchEvent pressure:
                    events.Add((frame, m => m.ChannelAftertouch(pressure.AfterTouchPressure)));
                    break;
            }
        }
    }

    return events.OrderBy(e => e.Frame).ToList();
}

static void DescribePatch(NoumenonEngine engine)
{
    var bank = engine.Parameters;
    var inv = CultureInfo.InvariantCulture;
    for (var s = 0; s < ParameterTable.SectionCount; s++)
    {
        var section = (SectionId)s;
        Console.WriteLine($"  Section {section}: cutoff {ParameterTable.Get(ParameterTable.Section(section, SectionParam.Cutoff)).Format(bank.Get(ParameterTable.Section(section, SectionParam.Cutoff)))}, reso {bank.Get(ParameterTable.Section(section, SectionParam.Reso)).ToString("0.00", inv)}, {ParameterTable.FilterTypes[bank.GetInt(ParameterTable.Section(section, SectionParam.FilterType))]}, level {bank.Get(ParameterTable.Section(section, SectionParam.Level)).ToString("0.00", inv)}");
        for (var slot = 0; slot < ParameterTable.SlotsPerSection; slot++)
        {
            var on = bank.GetBool(ParameterTable.Osc(section, slot, OscParam.On));
            var wave = ParameterTable.Waveforms[bank.GetInt(ParameterTable.Osc(section, slot, OscParam.Wave))];
            var semitone = bank.GetInt(ParameterTable.Osc(section, slot, OscParam.Semitone));
            var fine = bank.Get(ParameterTable.Osc(section, slot, OscParam.Fine));
            var level = bank.Get(ParameterTable.Osc(section, slot, OscParam.Level));
            Console.WriteLine($"    {section}{slot + 1}: {(on ? "on " : "off")} {wave,-13} {semitone,+4:+0;-0;0} st {fine,+5:+0;-0;0} ct  level {level.ToString("0.00", inv)}");
        }
    }

    Console.WriteLine($"  Mix: crossfade {bank.Get(ParamId.MixCrossfade).ToString("0.00", inv)}, balance {bank.Get(ParamId.MixSumBalance).ToString("+0.00;-0.00;0.00", inv)}, ring {bank.Get(ParamId.MixRingLevel).ToString("0.00", inv)}");
    Console.WriteLine($"  P1 {bank.Get(ParamId.P1Value).ToString("+0.0;-0.0;0.0", inv)} st (MIDI {OnOff(bank, ParamId.P1Midi)}, glide {bank.Get(ParamId.P1Glide).ToString("0.00", inv)} s), P2 {bank.Get(ParamId.P2Value).ToString("+0.0;-0.0;0.0", inv)} st (MIDI {OnOff(bank, ParamId.P2Midi)}), play mode {ParameterTable.PlayModes[bank.GetInt(ParamId.PlayMode)]}, scale {ParameterTable.Scales[bank.GetInt(ParamId.Scale)]} in {ParameterTable.RootNotes[bank.GetInt(ParamId.ScaleRoot)]}");
    Console.WriteLine($"  Sampler: key {bank.GetInt(ParamId.SamplerSelect)}, pitch {bank.GetInt(ParamId.SamplerPitch):+0;-0;0} st, dir {bank.Get(ParamId.SamplerDir).ToString("0.00", inv)}, loop {bank.Get(ParamId.SamplerLoopStart).ToString("0.00", inv)}..{bank.Get(ParamId.SamplerLoopEnd).ToString("0.00", inv)} xf {bank.Get(ParamId.SamplerLoopCrossfade).ToString("0", inv)} ms, amp {bank.Get(ParamId.SamplerAmp).ToString("0.00", inv)}, master {bank.Get(ParamId.SamplerMaster).ToString("0.00", inv)}, samp<>in {bank.Get(ParamId.SamplerInputMix).ToString("0.00", inv)}, AM {bank.Get(ParamId.SamplerAmDepth).ToString("0.00", inv)}, retrigger {OnOff(bank, ParamId.SamplerRetrigger)}");
    Console.WriteLine($"  Chain: filter {OnOff(bank, ParamId.MasterFilterOn)} {ParameterTable.Get(ParamId.MasterFilterCutoff).Format(bank.Get(ParamId.MasterFilterCutoff))} morph {bank.Get(ParamId.MasterFilterMorph).ToString("0.00", inv)} | dist {OnOff(bank, ParamId.DistortionOn)} mix {bank.Get(ParamId.DistortionMix).ToString("0.00", inv)} {ParameterTable.DistortionCurves[bank.GetInt(ParamId.DistortionCurve)]} | eq {bank.Get(ParamId.EqLow).ToString("+0.0;-0.0", inv)}/{bank.Get(ParamId.EqHigh).ToString("+0.0;-0.0", inv)} dB | spin {OnOff(bank, ParamId.SpinOn)} {bank.Get(ParamId.SpinTime1).ToString("0", inv)}/{bank.Get(ParamId.SpinTime2).ToString("0", inv)} ms fb {bank.Get(ParamId.SpinFeedback).ToString("0.00", inv)} mix {bank.Get(ParamId.SpinMix).ToString("0.00", inv)}");
    Console.WriteLine($"  Chain: resochord {OnOff(bank, ParamId.ResochordOn)} {ParameterTable.Get(ParamId.ResochordChord).Format(bank.Get(ParamId.ResochordChord))} fb {bank.Get(ParamId.ResochordFeedback).ToString("0.00", inv)}+{bank.Get(ParamId.ResochordFeedbackFader).ToString("0.00", inv)} mix {bank.Get(ParamId.ResochordMix).ToString("0.00", inv)} | reverb {OnOff(bank, ParamId.ReverbOn)} {ParameterTable.ReverbRooms[bank.GetInt(ParamId.ReverbRoom)]} size {bank.Get(ParamId.ReverbSize).ToString("0.00", inv)} mix {bank.Get(ParamId.ReverbMix).ToString("0.00", inv)} freeze {OnOff(bank, ParamId.ReverbFreeze)} order {ParameterTable.ReverbOrders[bank.GetInt(ParamId.ReverbOrder)]} | post limiter {OnOff(bank, ParamId.PostLimiterOn)} gain {bank.Get(ParamId.PostLimiterGain).ToString("0", inv)} dB");

    var playing = engine.Lanes.All.Where(l => l.IsPlaying).ToList();
    Console.WriteLine($"  Lanes: {playing.Count} of {engine.Lanes.Count} automated{(playing.Count > 0 ? " — " + string.Join(", ", playing.Take(8).Select(l => $"{l.Info.ShortName} {l.LengthSeconds.ToString("0.0", inv)} s")) + (playing.Count > 8 ? ", …" : "") : "")}; speed step {ParameterTable.SpeedSteps[bank.GetInt(ParamId.SpeedStep)]} × {bank.Get(ParamId.SpeedMultiplier).ToString("0.00", inv)}, restart mode {OnOff(bank, ParamId.RestartMode)}");
}

static string OnOff(ParameterBank bank, ParamId id) => bank.GetBool(id) ? "on" : "off";

static void DumpParameters()
{
    Console.WriteLine($"{"#",4}  {"Id",-28} {"Short",-8} {"Kind",-10} {"Range",-24} {"Default",-12} Flags");
    foreach (var p in ParameterTable.All)
    {
        var range = p.Kind == ParamKind.Choice ? string.Join("/", p.Choices) : $"{p.Format(p.Min)} .. {p.Format(p.Max)}";
        if (range.Length > 24)
            range = range[..21] + "...";

        Console.WriteLine($"{p.Index,4}  {p.Id,-28} {p.ShortName,-8} {p.Kind,-10} {range,-24} {p.Format(p.Default),-12} {(p.Flags == ParamFlags.None ? "-" : p.Flags.ToString())}");
    }

    Console.WriteLine($"{ParameterTable.Count} parameters, {ParameterTable.All.Count(p => p.IsLaneCapable)} lane-capable, {ParameterTable.All.Count(p => p.IsAddition)} additions.");
}

static void Analyze(float[] l, float[] r, int sampleRate)
{
    var peak = 0f;
    double sumSq = 0;
    var finite = true;
    for (var i = 0; i < l.Length; i++)
    {
        float a = MathF.Abs(l[i]), b = MathF.Abs(r[i]);
        if (a > peak)
            peak = a;

        if (b > peak)
            peak = b;

        sumSq += (double)l[i] * l[i] + (double)r[i] * r[i];
        if (!float.IsFinite(l[i]) || !float.IsFinite(r[i]))
            finite = false;
    }

    var rms = Math.Sqrt(sumSq / (2.0 * Math.Max(1, l.Length)));
    Console.WriteLine($"Stats : peak={peak:F4}  rms={rms:F5}  finite={finite}");

    var win = sampleRate;
    Console.Write("Level : ");
    for (var start = 0; start < l.Length; start += win)
    {
        var end = Math.Min(start + win, l.Length);
        var s = 0.0;
        var c = 0;
        for (var i = start; i < end; i++)
        {
            s += (double)l[i] * l[i] + (double)r[i] * r[i];
            c += 2;
        }

        Console.Write($"{(c > 0 ? Math.Sqrt(s / c) : 0):F3} ");
    }

    Console.WriteLine("(1 s RMS windows)");
}
