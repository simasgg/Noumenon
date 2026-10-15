using Noumenon.Engine.Parameters;

namespace Noumenon.Engine.Lanes;

/// <summary>
/// The chord sequencer: up to eight steps, each a degree of the scale or Hold (the previous chord
/// rings on), advancing on the lane clock. A step lasts Step Time scaled by the clock's speed (the
/// Speed step times the multiplier, like every lane) and, while Host Sync knows a tempo, snapped
/// to a whole number of beats; every lane restart — Restart, a gate with Restart On Gate, host
/// play — sends it back to step one, so the progression stays in phase with the loops. The engine
/// builds the step's chord with <c>ChordGenerator</c> for the Resonator and can hand its root to
/// P1 or P2. Runs on the audio thread at the control tick; allocation-free.
/// </summary>
public sealed class ChordSequencer
{
    private double elapsed;
    private double length = 1.0;
    private int step;
    private int degree;
    private bool started;

    /// <summary>The step playing now, 0-based.</summary>
    public int Step => Volatile.Read(ref step);

    /// <summary>The degree sounding now, 0 = I; a Hold step keeps the one before it.</summary>
    public int Degree => Volatile.Read(ref degree);

    /// <summary>How far the current step has run, 0–1.</summary>
    public float Progress => (float)Math.Clamp(Volatile.Read(ref elapsed) / Math.Max(1e-6, Volatile.Read(ref length)), 0.0, 1.0);

    internal void Restart()
    {
        Volatile.Write(ref elapsed, 0.0);
        Volatile.Write(ref step, 0);
        started = false;
    }

    /// <summary>One control tick: advances by <paramref name="seconds"/> at the clock's speed and returns the degree to sound.</summary>
    internal int Tick(double seconds, float speed, float barSeconds, float[] resolved)
    {
        var steps = Math.Clamp((int)MathF.Round(resolved[(int)ParamId.ChordSeqSteps]), 1, ParameterTable.ChordSeqStepCount);
        var stepSeconds = (double)resolved[(int)ParamId.ChordSeqStepTime];
        if (barSeconds > 0f)
        {
            var beat = barSeconds / 4.0;
            stepSeconds = Math.Max(1.0, Math.Round(stepSeconds / beat)) * beat;
        }

        Volatile.Write(ref length, Math.Max(0.01, stepSeconds));
        var current = step >= steps ? 0 : step;
        if (!started)
        {
            started = true;
            Enter(current, resolved);
        }

        var time = elapsed + seconds * Math.Max(0f, speed);
        while (time >= length)
        {
            time -= length;
            current = (current + 1) % steps;
            Enter(current, resolved);
        }

        if (current != step)
            Volatile.Write(ref step, current);

        Volatile.Write(ref elapsed, time);

        return degree;
    }

    private void Enter(int index, float[] resolved)
    {
        Volatile.Write(ref step, index);
        var value = (int)MathF.Round(resolved[(int)ParameterTable.ChordSeqStep(index)]);
        if (value > 0)
            Volatile.Write(ref degree, value - 1);
    }
}
