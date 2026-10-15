using System.Text;
using Noumenon.Engine.Hosting;
using Noumenon.Engine.Parameters;

namespace Noumenon.Tests;

/// <summary>
/// The format-agnostic session both plugin wrappers build on: the state is the snapshot JSON and
/// survives a round trip into a fresh instance, a state that is not one leaves the instrument as
/// it was with a notice, every applied snapshot is announced once, and the randomizer names what
/// it made.
/// </summary>
public class PluginSessionTests
{
    private sealed class Session() : PluginSessionBase(null)
    {
        public int Applied { get; private set; }

        protected override void OnSnapshotApplied() => Applied++;
    }

    [Fact]
    public void State_Round_Trips_Into_A_Fresh_Session()
    {
        var source = new Session();
        source.Engine.Parameters.Set(ParamId.OutputVolume, -9f);
        source.Engine.Parameters.Set(ParamId.ReverbRoom, 3);
        source.SetPreset("Somewhere", null);

        var bytes = source.SaveState();
        Assert.Equal((byte)'{', bytes[0]);

        var target = new Session();
        Assert.True(target.LoadState(bytes));
        Assert.Equal(-9f, target.Engine.Parameters.Get(ParamId.OutputVolume));
        Assert.Equal(3, target.Engine.Parameters.GetInt(ParamId.ReverbRoom));
        Assert.Equal("Somewhere", target.PresetName);
        Assert.Null(target.PresetPath);
        Assert.Equal(1, target.Applied);
    }

    [Fact]
    public void A_State_That_Is_Not_A_Snapshot_Changes_Nothing()
    {
        var session = new Session();
        session.Engine.Parameters.Set(ParamId.OutputVolume, -6f);

        Assert.False(session.LoadState([]));
        Assert.Null(session.CurrentNotice);
        Assert.False(session.LoadState(Encoding.UTF8.GetBytes("nonsense")));
        Assert.False(session.LoadState(Encoding.UTF8.GetBytes("{\"Version\": \"one\"}")));

        Assert.Equal(-6f, session.Engine.Parameters.Get(ParamId.OutputVolume));
        Assert.Equal(0, session.Applied);
        Assert.StartsWith("State not loaded", session.CurrentNotice);
    }

    [Fact]
    public void The_Randomizer_Names_What_It_Made()
    {
        var session = new Session();
        session.Engine.Parameters.Set(ParamId.RandomSeed, 1234);

        session.Randomize();

        Assert.Equal("random 1234", session.PresetName);
        Assert.Contains("seed 1234", session.CurrentNotice);
    }
}
