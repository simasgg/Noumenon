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
//     --set <Name=Value>   override one parameter (enum name or display name; repeatable)
//     --out <path>         output file (default ./noumenon_out.wav, 24-bit stereo)
//     --dump-params        print the parameter table and exit
var seconds = 10.0;
var sampleRate = 48000;
var block = 512;
ulong? seed = null;
var amount = 1f;
var p1 = 0f;
var p2 = 0f;
var outputPath = Path.GetFullPath("noumenon_out.wav");
var overrides = new List<(string Name, float Value)>();
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

engine.P1 = p1;
engine.P2 = p2;
engine.Prepare(sampleRate, block);
engine.Reset(seed ?? 1);

Console.WriteLine(seed is { } shown ? $"Patch : seed {shown}, amount {amount.ToString("0.00", CultureInfo.InvariantCulture)}" : "Patch : init (table defaults)");
DescribePatch(bank);

var total = (int)(seconds * sampleRate);
var left = new float[total];
var right = new float[total];
for (var pos = 0; pos < total; pos += block)
    engine.Process(left, right, pos, Math.Min(block, total - pos));

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
}

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
