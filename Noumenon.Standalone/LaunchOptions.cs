using System.Globalization;
using Noumenon.Engine.Recording;

namespace Noumenon.Standalone;

/// <summary>
/// The command line: nothing opens the instrument window; <c>--smoke</c> closes it by itself;
/// <c>--preset</c> loads a file or a factory name at start; <c>--bounce</c> and <c>--record</c>
/// run without a window (an offline render, or the configured device played for a while into a
/// WAV with the overrun and drop counts printed), so the Phase 5 targets can be checked from a
/// script. Parsing never throws: a bad argument lands in <see cref="Error"/>.
/// </summary>
internal sealed class LaunchOptions
{
    public bool Smoke { get; private set; }

    public string? PresetPath { get; private set; }

    public string? BouncePath { get; private set; }

    public string? RecordPath { get; private set; }

    public double Minutes { get; private set; } = 1.0;

    public double Seconds { get; private set; } = 30.0;

    public int SampleRate { get; private set; }

    public WavSampleFormat? Format { get; private set; }

    public ulong? Seed { get; private set; }

    /// <summary>Headless overrides of the saved device settings: WASAPI share mode and buffer length.</summary>
    public bool? Exclusive { get; private set; }

    public int? LatencyMs { get; private set; }

    public string? Error { get; private set; }

    public static LaunchOptions Parse(string[] args)
    {
        var options = new LaunchOptions();
        var inv = CultureInfo.InvariantCulture;
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            var hasValue = i + 1 < args.Length;
            try
            {
                if (Is(a, "--smoke"))
                    options.Smoke = true;
                else if (Is(a, "--preset") && hasValue)
                    options.PresetPath = args[++i];
                else if (Is(a, "--bounce") && hasValue)
                    options.BouncePath = args[++i];
                else if (Is(a, "--record") && hasValue)
                    options.RecordPath = args[++i];
                else if (Is(a, "--minutes") && hasValue)
                    options.Minutes = double.Parse(args[++i], inv);
                else if (Is(a, "--seconds") && hasValue)
                    options.Seconds = double.Parse(args[++i], inv);
                else if (Is(a, "--rate") && hasValue)
                    options.SampleRate = int.Parse(args[++i], inv);
                else if (Is(a, "--seed") && hasValue)
                    options.Seed = ulong.Parse(args[++i], inv);
                else if (Is(a, "--float"))
                    options.Format = WavSampleFormat.Float32;
                else if (Is(a, "--pcm24"))
                    options.Format = WavSampleFormat.Pcm24;
                else if (Is(a, "--exclusive"))
                    options.Exclusive = true;
                else if (Is(a, "--shared"))
                    options.Exclusive = false;
                else if (Is(a, "--latency") && hasValue)
                    options.LatencyMs = int.Parse(args[++i], inv);
                else
                    options.Error = $"Unknown argument '{a}'. Options: --smoke, --preset <file|name>, --bounce <wav> [--minutes n] [--rate hz] [--float|--pcm24] [--seed n], --record <wav> [--seconds n] [--rate hz] [--exclusive|--shared] [--latency ms].";
            }
            catch (FormatException)
            {
                options.Error = $"'{args[i]}' is not a valid value for {a}.";
            }
            catch (OverflowException)
            {
                options.Error = $"'{args[i]}' is out of range for {a}.";
            }

            if (options.Error is not null)
                break;
        }

        return options;
    }

    private static bool Is(string arg, string name) => arg.Equals(name, StringComparison.OrdinalIgnoreCase);
}
