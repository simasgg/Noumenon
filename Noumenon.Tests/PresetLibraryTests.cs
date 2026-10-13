using Noumenon.Engine.Presets;

namespace Noumenon.Tests;

public class PresetLibraryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "noumenon-library-" + Guid.NewGuid().ToString("N"));

    public PresetLibraryTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Factory_Content_Is_Found_Up_The_Tree_From_A_Plugin_Deploy_Folder()
    {
        var factory = Path.Combine(root, "content", "presets");
        Directory.CreateDirectory(factory);
        PresetIo.Save(new PresetState { Name = "Alpha" }, Path.Combine(factory, "Alpha" + PresetIo.Extension));
        var deploy = Path.Combine(root, "Noumenon.Vst2", "bin", "x64", "Debug", "net10.0-windows", "deploy");
        Directory.CreateDirectory(deploy);

        var library = new PresetLibrary(deploy);

        Assert.Equal(factory, library.FactoryFolder);
        Assert.Contains(library.Factory, preset => preset.Name == "Alpha");
        Assert.True(library.IsFactory(library.Factory[0].Path));
        Assert.False(library.IsFactory(Path.Combine(library.UserFolder, "Mine" + PresetIo.Extension)));
        Assert.Equal(Path.Combine(factory, "Alpha" + PresetIo.Extension), PresetLibrary.Resolve("Alpha", deploy));
        Assert.Null(PresetLibrary.Resolve("Nope", deploy));
    }
}
