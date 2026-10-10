namespace Noumenon.Ui;

/// <summary>
/// What the editor needs from whoever hosts it (the standalone now, the plugin wrappers later):
/// the preset bank and the file dialogs, which live outside the shared control because a plugin
/// host owns its own files and windows. Everything the editor does with the engine itself goes
/// through <c>NoumenonEngine</c> directly.
/// </summary>
public interface IEditorHost
{
    IReadOnlyList<string> PresetNames { get; }

    int CurrentPresetIndex { get; }

    string CurrentPresetName { get; }

    /// <summary>A short line for the top bar: the device, the load, a notice.</summary>
    string StatusText { get; }

    /// <summary>True while letter keys play notes, so the editor leaves R and M alone.</summary>
    bool KeyboardNotesActive { get; }

    void LoadPreset(int index);

    void SavePreset();

    void LoadSample(int key);

    void LoadSampleFile(int key, string path);
}
