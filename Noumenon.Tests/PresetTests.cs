using Noumenon.Engine;
using Noumenon.Engine.Lanes;
using Noumenon.Engine.Parameters;
using Noumenon.Engine.Presets;
using Noumenon.Engine.Scales;

namespace Noumenon.Tests;

public class PresetTests : IDisposable
{
    private const int SampleRate = 48000;

    private readonly string dir = Path.Combine(Path.GetTempPath(), "noumenon-presets-" + Guid.NewGuid().ToString("N"));

    public PresetTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(dir, true);
        }
        catch (IOException)
        {
        }
    }

    private static NoumenonEngine Make()
    {
        var engine = new NoumenonEngine();
        engine.Prepare(SampleRate, 512);
        engine.Reset(1);

        return engine;
    }

    private static void RenderBlocks(NoumenonEngine engine, int blocks)
    {
        var l = new float[512];
        var r = new float[512];
        for (var i = 0; i < blocks; i++)
            engine.Process(l, r, l.Length);
    }

    [Fact]
    public void A_Snapshot_Survives_The_Json_Round_Trip()
    {
        var source = Make();
        Randomizer.Randomize(source, 5, RandomizeScope.All, 0.8f);
        source.Locks[(int)ParamId.OscA3Level] = true;
        source.SetUserChord(2, [40f, 47f, 52f, 59f, 64f, 71f]);
        source.UserScale = ScalaParser.Parse("Test\n2\n 3/2\n 2/1\n");
        source.Parameters.Set(ParamId.Scale, 8);   // User
        source.Reset(5);
        RenderBlocks(source, 4);   // the lanes take their snapshots in

        var captured = source.CapturePreset("round trip");
        var json = PresetIo.Serialize(captured);
        var loaded = PresetIo.Deserialize(json);

        Assert.Equal(PresetState.CurrentVersion, loaded.Version);
        Assert.Equal("round trip", loaded.Name);
        Assert.Equal(ParameterTable.Count, loaded.Parameters.Count);
        Assert.True(loaded.Lanes.Count > 0, "the randomizer should have automated some lanes");
        Assert.Contains("OscA3Level", loaded.Locked);
        Assert.Equal([40f, 47f, 52f, 59f, 64f, 71f], loaded.UserChords[2]);
        Assert.Equal("Test", loaded.UserScale!.Name);

        var target = Make();
        target.ApplyPreset(loaded);
        RenderBlocks(target, 4);
        var again = target.CapturePreset("round trip");

        foreach (var info in ParameterTable.All)
            Assert.Equal(captured.Parameters[info.Id.ToString()], again.Parameters[info.Id.ToString()]);

        Assert.Equal(captured.Lanes.Count, again.Lanes.Count);
        foreach (var (a, b) in captured.Lanes.Zip(again.Lanes))
        {
            Assert.Equal(a.Parameter, b.Parameter);
            Assert.Equal(a.Mode, b.Mode);
            Assert.Equal(a.Points, b.Points);
        }

        Assert.Equal(captured.UserChords[2], again.UserChords[2]);
        Assert.True(target.Locks[(int)ParamId.OscA3Level]);
        Assert.Equal(701.955f, target.UserScale.DegreesCents[1], 2);
        Assert.Equal(5UL, target.Seed);
    }

    [Fact]
    public void Missing_Parameters_Take_Their_Defaults_And_Unknown_Ones_Are_Ignored()
    {
        var state = new PresetState();
        state.Parameters["OscA1Level"] = 0.1f;
        state.Parameters["NoSuchParameter"] = 42f;

        var engine = Make();
        engine.Parameters.Set(ParamId.OscB1Level, 0.99f);
        engine.ApplyPreset(state);

        Assert.Equal(0.1f, engine.Parameters.Get(ParamId.OscA1Level));
        Assert.Equal(ParameterTable.Get(ParamId.OscB1Level).Default, engine.Parameters.Get(ParamId.OscB1Level));
    }

    [Fact]
    public void Files_Save_Load_And_Scan()
    {
        var engine = Make();
        var pathB = Path.Combine(dir, "Beta" + PresetIo.Extension);
        var pathA = Path.Combine(dir, "Alpha" + PresetIo.Extension);
        PresetIo.Save(engine.CapturePreset("Beta"), pathB);
        PresetIo.Save(engine.CapturePreset(""), pathA);
        File.WriteAllText(Path.Combine(dir, "notes.txt"), "not a preset");

        var found = PresetIo.Scan(dir);
        Assert.Equal(["Alpha", "Beta"], found.Select(p => p.Name).ToArray());
        Assert.Equal("Alpha", PresetIo.Load(pathA).Name);   // an empty name falls back to the file name
        Assert.Empty(PresetIo.Scan(Path.Combine(dir, "missing")));
    }

    [Fact]
    public void A_Newer_Version_Is_Refused_And_An_Old_One_Is_Migrated()
    {
        Assert.Throws<FormatException>(() => PresetIo.Deserialize("{\"Version\": 99}"));

        var old = PresetIo.Deserialize("{\"Name\": \"ancient\", \"Parameters\": {\"OscA1Level\": 0.25}}");
        Assert.Equal(1, old.Version);
        Assert.Equal(0.25f, old.Parameters["OscA1Level"]);
    }

    [Fact]
    public void Morph_Glides_To_The_Target_Through_A_Log_Midpoint()
    {
        var engine = Make();
        var target = engine.CapturePreset("target");
        target.Parameters["OscA1Level"] = 1f;         // from 0.6
        target.Parameters["SectionACutoff"] = 800f;   // from 8000 (log)
        target.Parameters["OscA1Wave"] = 2f;          // Saw, from Sine (discrete: flips at the midpoint)

        engine.Morph(target, 1f);
        Assert.True(engine.IsMorphing);

        RenderBlocks(engine, (int)(0.5 * SampleRate / 512));
        Assert.InRange(engine.Parameters.Get(ParamId.OscA1Level), 0.76f, 0.84f);
        Assert.InRange(engine.Parameters.Get(ParamId.SectionACutoff), 2200f, 2900f);   // geometric midpoint ≈ 2530

        RenderBlocks(engine, (int)(0.7 * SampleRate / 512));
        Assert.False(engine.IsMorphing);
        Assert.Equal(1f, engine.Parameters.Get(ParamId.OscA1Level), 3);
        Assert.Equal(800f, engine.Parameters.Get(ParamId.SectionACutoff), 0);
        Assert.Equal(2, engine.Parameters.GetInt(ParamId.OscA1Wave));
    }
}
