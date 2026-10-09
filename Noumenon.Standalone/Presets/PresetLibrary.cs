using Noumenon.Engine.Presets;

namespace Noumenon.Standalone.Presets;

/// <summary>
/// The two preset folders the standalone browses: the factory bank — <c>content/presets</c> next
/// to the executable in an installed layout, or up the tree from it when running from the build
/// output — and the user's own bank in their Documents folder, where Save As defaults to. A bare
/// name on the command line is looked up in both.
/// </summary>
internal sealed class PresetLibrary
{
    private const int SearchDepth = 6;

    public PresetLibrary()
    {
        FactoryFolder = LocateContent(Path.Combine("content", "presets"));
        UserFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Noumenon", "Presets");
        Rescan();
    }

    public string? FactoryFolder { get; }

    public string UserFolder { get; }

    public IReadOnlyList<PresetInfo> Factory { get; private set; } = [];

    public IReadOnlyList<PresetInfo> User { get; private set; } = [];

    public IReadOnlyList<PresetInfo> All { get; private set; } = [];

    public void Rescan()
    {
        Factory = FactoryFolder is null ? [] : PresetIo.Scan(FactoryFolder);
        User = PresetIo.Scan(UserFolder);
        All = [..Factory, ..User];
    }

    public int IndexOf(string? path)
    {
        if (path is null)
            return -1;

        for (var i = 0; i < All.Count; i++)
        {
            if (string.Equals(All[i].Path, path, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    /// <summary>A path as given, or a preset name looked up in the factory bank then the user bank; null when neither exists.</summary>
    public static string? Resolve(string nameOrPath)
    {
        if (File.Exists(nameOrPath))
            return Path.GetFullPath(nameOrPath);

        var fileName = nameOrPath.EndsWith(PresetIo.Extension, StringComparison.OrdinalIgnoreCase) ? nameOrPath : nameOrPath + PresetIo.Extension;
        var library = new PresetLibrary();
        foreach (var folder in new[] { library.FactoryFolder, library.UserFolder })
        {
            if (folder is null)
                continue;

            var candidate = Path.Combine(folder, fileName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    public static string? LocateContent(string relativePath)
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var dir = new DirectoryInfo(start);
            for (var depth = 0; depth < SearchDepth && dir is not null; depth++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, relativePath);
                if (Directory.Exists(candidate) || File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }
}
