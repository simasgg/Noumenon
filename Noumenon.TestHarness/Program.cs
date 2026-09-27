using System.Globalization;
using Noumenon.Engine;
using Noumenon.Engine.Parameters;
using Noumenon.TestHarness;

// Offline render harness: the engine → WAV, no GUI, no DAW.
//   Usage: Noumenon.TestHarness [options]
//     --seconds <n>        length to render (default 10)
//     --rate <hz>          sample rate (default 48000)
//     --block <n>          block size the render is cut into (default 512)
//     --seed <n>           randomize the oscillator patch from this seed (default: the init patch)
//     --amount <0..1>      how far the randomizer strays from a tonal drone (default 1)
//     --p1 <st> / --p2 <st>  pitch inputs in semitones (default 0)
//     --sample <path>      load a WAV/AIFF into sample key 0 and select it
//     --input <path>       feed a WAV as the live input (the FX identity); shorter files are zero-padded
//     --gate <seconds>     fire a MIDI gate at this time (repeatable)
//     --set <Name=Value>   override one parameter (enum name or display name; repeatable)
//     --at <seconds> <Name=Value>  change a parameter at that time during the render (repeatable)
//     --out <path>         output file (default ./noumenon_out.wav, 24-bit stereo)
//     --dump-params        print the parameter table and exit
var seconds = 10.0;
var sampleRate = 48000;
var block = 512;
ulong? seed = null;
var amount = 1f;
var p1 = 0f;
var p2 = 0f;
string? samplePath = null;
string? inputPath = null;
var gates = new List<double>();
var outputPath = Path.GetFullPath("noumenon_out.wav");
var overrides = new List<(string Name, float Value)>();
var timedSets = new List<(double Seconds, string Name, float Value)>();
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
    else if (Is(a, "--seed") && hasValue)
        seed = ulong.Parse(args[++i], CultureInfo.InvariantCulture);
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
    else if (Is(a, "--gate") && hasValue)
        gates.Add(double.Parse(args[++i], CultureInfo.InvariantCulture));
    else if (Is(a, "--out") && hasValue)
        outputPath = Path.GetFullPath(args[++i]);
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
if (seed is { } s)
    Randomizer.Randomize(bank, s, RandomizeScope.All, amount);

if (samplePath is not null)
{
    var slot = engine.Samples.Load(0, samplePath);
    bank.Set(ParamId.SamplerSelect, 0);
    Console.WriteLine($"Sample: {slot.Name}  ({slot.Data.Length} frames @ {slot.Data.SampleRate} Hz, {(slot.Data.IsStereo ? "stereo" : "mono")}, {slot.Data.Seconds.ToString("0.0", CultureInfo.InvariantCulture)} s) on key 0");
}

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

engine.P1 = p1;
engine.P2 = p2;
engine.Prepare(sampleRate, block);
engine.Reset(seed ?? 1);

Console.WriteLine(seed is { } shown ? $"Patch : seed {shown}, amount {amount.ToString("0.00", CultureInfo.InvariantCulture)}" : "Patch : init (table defaults)");
DescribePatch(bank);

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

var gateFrames = gates.Select(g => (long)(g * sampleRate)).OrderBy(g => g).ToList();
var nextGate = 0;
var nextTimed = 0;
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

    if (inputLeft is not null && inputRight is not null)
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

static void DescribePatch(ParameterBank bank)
{
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
    Console.WriteLine($"  Sampler: key {bank.GetInt(ParamId.SamplerSelect)}, pitch {bank.GetInt(ParamId.SamplerPitch):+0;-0;0} st, dir {bank.Get(ParamId.SamplerDir).ToString("0.00", inv)}, loop {bank.Get(ParamId.SamplerLoopStart).ToString("0.00", inv)}..{bank.Get(ParamId.SamplerLoopEnd).ToString("0.00", inv)} xf {bank.Get(ParamId.SamplerLoopCrossfade).ToString("0", inv)} ms, amp {bank.Get(ParamId.SamplerAmp).ToString("0.00", inv)}, master {bank.Get(ParamId.SamplerMaster).ToString("0.00", inv)}, samp<>in {bank.Get(ParamId.SamplerInputMix).ToString("0.00", inv)}, AM {bank.Get(ParamId.SamplerAmDepth).ToString("0.00", inv)}, retrigger {(bank.GetBool(ParamId.SamplerRetrigger) ? "on" : "off")}");
    Console.WriteLine($"  Chain: filter {OnOff(bank, ParamId.MasterFilterOn)} {ParameterTable.Get(ParamId.MasterFilterCutoff).Format(bank.Get(ParamId.MasterFilterCutoff))} morph {bank.Get(ParamId.MasterFilterMorph).ToString("0.00", inv)} | dist {OnOff(bank, ParamId.DistortionOn)} mix {bank.Get(ParamId.DistortionMix).ToString("0.00", inv)} {ParameterTable.DistortionCurves[bank.GetInt(ParamId.DistortionCurve)]} | eq {bank.Get(ParamId.EqLow).ToString("+0.0;-0.0", inv)}/{bank.Get(ParamId.EqHigh).ToString("+0.0;-0.0", inv)} dB | spin {OnOff(bank, ParamId.SpinOn)} {bank.Get(ParamId.SpinTime1).ToString("0", inv)}/{bank.Get(ParamId.SpinTime2).ToString("0", inv)} ms fb {bank.Get(ParamId.SpinFeedback).ToString("0.00", inv)} mix {bank.Get(ParamId.SpinMix).ToString("0.00", inv)}");
    Console.WriteLine($"  Chain: resochord {OnOff(bank, ParamId.ResochordOn)} {ParameterTable.Get(ParamId.ResochordChord).Format(bank.Get(ParamId.ResochordChord))} fb {bank.Get(ParamId.ResochordFeedback).ToString("0.00", inv)}+{bank.Get(ParamId.ResochordFeedbackFader).ToString("0.00", inv)} mix {bank.Get(ParamId.ResochordMix).ToString("0.00", inv)} | reverb {OnOff(bank, ParamId.ReverbOn)} {ParameterTable.ReverbRooms[bank.GetInt(ParamId.ReverbRoom)]} size {bank.Get(ParamId.ReverbSize).ToString("0.00", inv)} mix {bank.Get(ParamId.ReverbMix).ToString("0.00", inv)} freeze {OnOff(bank, ParamId.ReverbFreeze)} order {ParameterTable.ReverbOrders[bank.GetInt(ParamId.ReverbOrder)]} | post limiter {OnOff(bank, ParamId.PostLimiterOn)} gain {bank.Get(ParamId.PostLimiterGain).ToString("0", inv)} dB");
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
