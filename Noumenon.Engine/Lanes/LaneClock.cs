using Noumenon.Engine.Parameters;

namespace Noumenon.Engine.Lanes;

/// <summary>
/// MF's transport for the lanes: the four-step Speed (times Noumenon's continuous multiplier),
/// Restart mode (starting or ending any recording restarts every lane), the immediate Restart,
/// restart on MIDI gate and on host play, and host sync. Hosts feed it the tempo and the play
/// state; with Host Sync on and a tempo known, <see cref="BarSeconds"/> lets new recordings snap
/// to whole bars. Everything is decided once per control tick on the audio thread.
/// </summary>
public sealed class LaneClock
{
    public static readonly float[] StepMultipliers = [0.5f, 1f, 2f, 4f];

    private double hostTempo;
    private int hostPlaying;
    private int restartRequested;
    private bool wasPlaying;

    /// <summary>Host tempo in BPM, 0 when unknown.</summary>
    public double HostTempo
    {
        get => Volatile.Read(ref hostTempo);
        set => Volatile.Write(ref hostTempo, value < 0 ? 0 : value);
    }

    public bool HostPlaying
    {
        get => Volatile.Read(ref hostPlaying) != 0;
        set => Volatile.Write(ref hostPlaying, value ? 1 : 0);
    }

    /// <summary>The effective lane speed (step × multiplier) as of the last tick.</summary>
    public float Speed { get; private set; } = 1f;

    /// <summary>Seconds per 4/4 bar when host sync is on and the tempo is known, else 0.</summary>
    public float BarSeconds { get; private set; }

    public void RequestRestart() => Interlocked.Exchange(ref restartRequested, 1);

    /// <summary>Reads the transport parameters and returns whether every lane restarts this tick.</summary>
    internal bool Tick(ParameterBank bank, bool gate, bool recordingEdge)
    {
        Speed = StepMultipliers[Math.Clamp(bank.GetInt(ParamId.SpeedStep), 0, StepMultipliers.Length - 1)] * bank.Get(ParamId.SpeedMultiplier);

        var tempo = HostTempo;
        BarSeconds = bank.GetBool(ParamId.HostSync) && tempo > 0 ? (float)(4.0 * 60.0 / tempo) : 0f;

        var restart = Interlocked.Exchange(ref restartRequested, 0) != 0;
        if (bank.Get(ParamId.RestartNow) >= 0.5f)
        {
            bank.Set(ParamId.RestartNow, 0f);
            restart = true;
        }

        if (gate && bank.GetBool(ParamId.RestartOnGate))
            restart = true;

        var playing = HostPlaying;
        if (playing && !wasPlaying && bank.GetBool(ParamId.RestartOnHostPlay))
            restart = true;

        wasPlaying = playing;

        if (recordingEdge && bank.GetBool(ParamId.RestartMode))
            restart = true;

        return restart;
    }
}
