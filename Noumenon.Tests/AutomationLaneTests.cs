using Noumenon.Dsp.Shared;
using Noumenon.Engine;
using Noumenon.Engine.Lanes;
using Noumenon.Engine.Parameters;

namespace Noumenon.Tests;

/// <summary>
/// MF's living faders: arm + drag records the live value at 50 Hz, release keeps the loop, Automated
/// plays it back at length / speed, Restart aligns it, snapshots load at the next tick, and a full
/// buffer stops the take.
/// </summary>
public class AutomationLaneTests
{
    private const double Dt = 32.0 / 48000.0;

    private static AutomationLane Make(float maxSeconds = 10f) => new(ParameterTable.Get(ParamId.OscA1Level), maxSeconds);

    /// <summary>Ticks for <paramref name="seconds"/>, feeding <paramref name="source"/>(time) as the fader value; returns the lane's output per tick with its time.</summary>
    private static List<(double Time, float Value, bool Overrides)> Run(AutomationLane lane, double seconds, Func<double, float> source, float clockSpeed = 1f, float barSeconds = 0f, double startTime = 0.0)
    {
        var output = new List<(double, float, bool)>();
        var ticks = (int)(seconds / Dt);
        for (var i = 0; i < ticks; i++)
        {
            var t = startTime + i * Dt;
            lane.Tick(Dt, clockSpeed, barSeconds, source(t), out var overrides, out var value);
            output.Add((t, value, overrides));
        }

        return output;
    }

    private static AutomationLane RecordRamp(double seconds)
    {
        var lane = Make();
        lane.Armed = true;
        lane.Pressed = true;
        Run(lane, seconds, t => (float)(t / seconds));
        lane.Pressed = false;
        lane.Tick(Dt, 1f, 0f, 0.5f, out _, out _);
        lane.Mode = LaneMode.Automated;
        lane.Armed = false;

        return lane;
    }

    [Fact]
    public void Arm_Drag_Release_Records_And_Automated_Loops_It()
    {
        var lane = RecordRamp(2.0);
        Assert.True(lane.HasRecording);
        Assert.False(lane.IsRecording);
        Assert.InRange(lane.LengthSeconds, 1.95f, 2.05f);

        var playback = Run(lane, 4.5, _ => 0.5f);
        Assert.All(playback, p => Assert.True(p.Overrides));
        foreach (var (time, value, _) in playback)
        {
            var expected = (float)(time % 2.0 / 2.0);
            if (expected < 0.05f || expected > 0.95f)
                continue;   // the seam interpolates from 1 back to 0

            Assert.True(MathF.Abs(value - expected) < 0.04f, $"t={time:F3}: {value} vs {expected}");
        }
    }

    [Fact]
    public void Loop_Period_Is_Length_Over_Speed()
    {
        var lane = RecordRamp(2.0);
        var fast = Run(lane, 3.0, _ => 0f, clockSpeed: 2f);
        foreach (var (time, value, _) in fast)
        {
            var expected = (float)(time % 1.0);
            if (expected < 0.05f || expected > 0.95f)
                continue;

            Assert.True(MathF.Abs(value - expected) < 0.04f, $"t={time:F3}: {value} vs {expected}");
        }

        lane.Restart();
        lane.SpeedMultiplier = 0.5f;
        var slow = Run(lane, 4.0, _ => 0f);
        var atThree = slow.First(p => p.Time >= 3.0);
        Assert.InRange(atThree.Value, 0.7f, 0.8f);   // 3 s at half speed = 1.5 s into a 2 s ramp
    }

    [Fact]
    public void Static_Passes_The_Fader_Through()
    {
        var lane = RecordRamp(1.0);
        lane.Mode = LaneMode.Static;
        var output = Run(lane, 0.5, t => 0.3f);
        Assert.All(output, p => Assert.False(p.Overrides));
        Assert.Equal(0.3f, lane.Value);
    }

    [Fact]
    public void Restart_Rewinds_The_Loop()
    {
        var lane = RecordRamp(2.0);
        Run(lane, 1.3, _ => 0f);
        Assert.InRange(lane.Phase, 1.25, 1.35);

        lane.Restart();
        Assert.Equal(0.0, lane.Phase);
        lane.Tick(Dt, 1f, 0f, 0f, out _, out var value);
        Assert.True(value < 0.01f);
    }

    [Fact]
    public void A_Snapshot_Loads_At_The_Next_Tick()
    {
        var lane = Make();
        lane.Load(new LaneSnapshot([0.1f, 0.9f], LaneMode.Automated, 2f));
        Assert.False(lane.HasRecording);

        lane.Tick(Dt, 1f, 0f, 0f, out var overrides, out var value);
        Assert.True(lane.HasRecording);
        Assert.Equal(2, lane.PointCount);
        Assert.Equal(LaneMode.Automated, lane.Mode);
        Assert.Equal(2f, lane.SpeedMultiplier);
        Assert.True(overrides);
        Assert.Equal(0.1f, value, 4);   // the first tick after a load reads point 0
    }

    [Fact]
    public void Clear_Makes_The_Lane_Static_And_Empty()
    {
        var lane = RecordRamp(1.0);
        lane.Clear();
        lane.Tick(Dt, 1f, 0f, 0.4f, out var overrides, out _);
        Assert.False(overrides);
        Assert.False(lane.HasRecording);
        Assert.Equal(LaneMode.Static, lane.Mode);
    }

    [Fact]
    public void Recording_Stops_When_The_Buffer_Is_Full()
    {
        var lane = Make(maxSeconds: 1f);
        lane.Armed = true;
        lane.Pressed = true;
        Run(lane, 3.0, t => (float)t);
        Assert.False(lane.IsRecording);
        Assert.Equal(AutomationLane.PointsPerSecond, lane.PointCount);
        Assert.True(lane.Armed, "MF keeps the fader armed after a take");
    }

    [Fact]
    public void Host_Sync_Stretches_Loops_To_Whole_Bars()
    {
        Assert.Equal(1f, AutomationLane.SyncStretchFor(2.3f, 0f));
        Assert.Equal(2.3f / 2f, AutomationLane.SyncStretchFor(2.3f, 2f), 5);
        Assert.Equal(3.5f / 4f, AutomationLane.SyncStretchFor(3.5f, 2f), 5);
        Assert.Equal(0.4f / 2f, AutomationLane.SyncStretchFor(0.4f, 2f), 5);   // never shorter than one bar
    }

    [Fact]
    public void Random_Loops_Are_Smooth_Seamless_And_Bounded()
    {
        var points = Randomizer.RandomLoop(new Xoshiro128(3), 200, 0.5f, 0.3f);
        Assert.Equal(200, points.Length);
        Assert.All(points, p => Assert.InRange(p, 0.2f, 0.8f));
        Assert.True(MathF.Abs(points[0] - points[^1]) < 0.03f, "the loop should close on itself");
        for (var i = 1; i < points.Length; i++)
            Assert.True(MathF.Abs(points[i] - points[i - 1]) < 0.03f, $"step {i} too large: {points[i] - points[i - 1]}");

        Assert.True(points.Max() - points.Min() > 0.1f, "the loop should actually move");
    }

    [Fact]
    public void A_Lane_Set_Has_One_Lane_Per_Lane_Capable_Parameter()
    {
        var lanes = new LaneSet(5f);
        Assert.Equal(ParameterTable.All.Count(p => p.IsLaneCapable), lanes.Count);
        Assert.NotNull(lanes.ForParameter(ParamId.OscB6Level));
        Assert.Null(lanes.ForParameter(ParamId.OscA1Wave));
        Assert.Equal(5f, lanes.MaxSeconds);
        Assert.Equal(5f, lanes[0].MaxSeconds);
    }
}
