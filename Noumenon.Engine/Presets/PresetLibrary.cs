namespace Noumenon.Engine.Presets;

/// <summary>
/// The two preset folders every host browses: the factory bank — <c>content/presets</c> next to
/// the executable or plugin in an installed layout, or up the tree from it when running from a
/// build output — and the user's own bank in their Documents folder, where Save As defaults to,
/// shared by the standalone and the plugins so a snapshot saved in one is in the other's list. A
/// bare name on the command line is looked up in both. A host that is not the application (a
/// plugin inside a DAW, whose base directory is the DAW's) passes the folder its assembly was
/// loaded from as a starting point.
/// </summary>
public sealed class PresetLibrary
{
    private const int SearchDepth = 8;

    public PresetLibrary(params string?[] startFolders)
    {
        FactoryFolder = LocateContent(Path.Combine("content", "presets"), startFolders);
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

    public bool IsFactory(string? path) => path is not null && FactoryFolder is not null && path.StartsWith(FactoryFolder, StringComparison.OrdinalIgnoreCase);

    /// <summary>A path as given, or a preset name looked up in the factory bank then the user bank; null when neither exists.</summary>
    public static string? Resolve(string nameOrPath, params string?[] startFolders)
    {
        if (File.Exists(nameOrPath))
            return Path.GetFullPath(nameOrPath);

        var fileName = nameOrPath.EndsWith(PresetIo.Extension, StringComparison.OrdinalIgnoreCase) ? nameOrPath : nameOrPath + PresetIo.Extension;
        var library = new PresetLibrary(startFolders);
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

    /// <summary>Walks up from each start folder (then the application's base directory and the working directory) looking for <paramref name="relativePath"/>.</summary>
    public static string? LocateContent(string relativePath, params string?[] startFolders)
    {
        foreach (var start in startFolders.Concat<string?>([AppContext.BaseDirectory, Directory.GetCurrentDirectory()]))
        {
            if (string.IsNullOrEmpty(start))
                continue;

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
