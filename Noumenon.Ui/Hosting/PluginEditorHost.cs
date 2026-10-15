using System.Runtime.InteropServices;
using System.Text.Json;
using Noumenon.Engine.Hosting;
using Noumenon.Engine.Parameters;
using Noumenon.Engine.Presets;

namespace Noumenon.Ui.Hosting;

/// <summary>
/// Hosts the shared <see cref="NoumenonEditor"/> inside a window a plugin host provides, for any
/// plugin format: the control is created on the host's UI thread, reparented onto the host's
/// handle, shown, and disposed again on close — a host opens and closes the editor many times in a
/// session, so the panel size survives reopening. While it is open a timer on that thread calls
/// <see cref="Poll"/> every 33 ms, which is where a plugin does what the standalone's poll does
/// (tell the host about bank changes, run the randomizer the engine asked for, report a latency
/// change); there is no other background thread. It is also the editor's
/// <see cref="IEditorHost"/>: presets come from the session's library, samples from a file dialog.
/// The format-specific side adds the status line, the resize request and what follows a preset
/// load or a save.
/// </summary>
public abstract partial class PluginEditorHost : IEditorHost
{
    private const int PollMs = 33;

    private readonly PluginSessionBase session;
    private NoumenonEditor? control;
    private System.Windows.Forms.Timer? timer;
    private IReadOnlyList<string> presetNames = [];
    private float scale = 1f;

    protected PluginEditorHost(PluginSessionBase session)
    {
        this.session = session;
    }

    /// <summary>The hosted control while the editor is open.</summary>
    public NoumenonEditor? Control => control;

    /// <summary>The panel scale, remembered while the editor is closed; setting it resizes an open panel.</summary>
    public float PanelScale
    {
        get => control?.PanelScale ?? scale;
        set
        {
            if (value <= 0f)
                return;

            scale = value;
            if (control is not null)
                control.PanelScale = value;
        }
    }

    /// <summary>The panel's size in physical pixels at the current scale.</summary>
    public Size PanelSize => new((int)MathF.Round(NoumenonEditor.LogicalWidth * PanelScale), (int)MathF.Round(NoumenonEditor.LogicalHeight * PanelScale));

    public IReadOnlyList<string> PresetNames => presetNames;

    public int CurrentPresetIndex => session.Library.IndexOf(session.PresetPath);

    public string CurrentPresetName => session.PresetName;

    public abstract string StatusText { get; }

    public bool KeyboardNotesActive => false;

    public void Open(nint parent)
    {
        Close();
        RefreshPresetNames();
        var editor = new NoumenonEditor(session.Engine, this) { Location = Point.Empty };
        editor.PanelScale = scale;
        editor.PanelScaleChanged += HandlePanelScaleChanged;
        editor.GestureStarted += OnGestureStarted;
        editor.GestureEnded += OnGestureEnded;
        SetParent(editor.Handle, parent);
        editor.Show();
        control = editor;
        OnOpened();
        timer = new System.Windows.Forms.Timer { Interval = PollMs };
        timer.Tick += OnTick;
        timer.Start();
    }

    public void Close()
    {
        if (timer is not null)
        {
            timer.Stop();
            timer.Dispose();
            timer = null;
        }

        if (control is null)
            return;

        OnClosing();
        control.PanelScaleChanged -= HandlePanelScaleChanged;
        control.GestureStarted -= OnGestureStarted;
        control.GestureEnded -= OnGestureEnded;
        control.Dispose();
        control = null;
    }

    public void LoadPreset(int index)
    {
        var all = session.Library.All;
        if (index < 0 || index >= all.Count)
            return;

        var preset = all[index];
        try
        {
            session.Apply(PresetIo.Load(preset.Path), preset.Path);
            session.Notice($"Loaded {preset.Name}");
            OnPresetLoaded();
        }
        catch (Exception ex) when (ex is IOException or FormatException or JsonException or UnauthorizedAccessException or InvalidOperationException)
        {
            session.Notice($"Preset not loaded: {ex.Message}");
        }
    }

    public void SavePreset()
    {
        if (control is null)
            return;

        var library = session.Library;
        var path = session.PresetPath;
        if (path is null || library.IsFactory(path))
        {
            Directory.CreateDirectory(library.UserFolder);
            using var dialog = new SaveFileDialog { Filter = $"Noumenon presets|*{PresetIo.Extension}", InitialDirectory = library.UserFolder, FileName = session.PresetName + PresetIo.Extension };
            if (dialog.ShowDialog(control) != DialogResult.OK)
                return;

            path = dialog.FileName;
        }

        var name = Path.GetFileNameWithoutExtension(path);
        try
        {
            PresetIo.Save(session.Engine.CapturePreset(name), path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            session.Notice("Preset not saved: " + ex.Message);
            return;
        }

        session.SetPreset(name, path);
        library.Rescan();
        RefreshPresetNames();
        session.Notice($"Saved {path}");
        OnPresetSaved();
    }

    public void LoadSample(int key)
    {
        if (control is null)
            return;

        using var dialog = new OpenFileDialog { Filter = MediaFoundationDecoding.FileFilter, Title = $"Load a sample into key {key}" };
        if (dialog.ShowDialog(control) == DialogResult.OK)
            LoadSampleFile(key, dialog.FileName);
    }

    public void LoadSampleFile(int key, string path)
    {
        session.Notice($"Loading {Path.GetFileName(path)}…");
        var engine = session.Engine;
        Task.Run(() =>
        {
            try
            {
                var slot = engine.Samples.Load(key, path);
                engine.Parameters.Set(ParamId.SamplerSelect, key);
                session.Notice($"Loaded {slot.Name} on key {key}");
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidDataException or UnauthorizedAccessException)
            {
                session.Notice("Sample not loaded: " + ex.Message);
            }
        });
    }

    /// <summary>UI thread, every 33 ms while the editor is open.</summary>
    protected abstract void Poll();

    /// <summary>The panel changed its scale: ask the host to fit its window to <see cref="PanelSize"/>.</summary>
    protected abstract void OnPanelScaleChanged();

    /// <summary>A preset from the bank replaced the whole snapshot.</summary>
    protected virtual void OnPresetLoaded()
    {
    }

    /// <summary>The snapshot was saved as a preset under a new name.</summary>
    protected virtual void OnPresetSaved()
    {
    }

    protected virtual void OnOpened()
    {
    }

    protected virtual void OnClosing()
    {
    }

    protected virtual void OnGestureStarted()
    {
    }

    protected virtual void OnGestureEnded()
    {
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (control is null || control.IsDisposed)
            return;

        Poll();
    }

    private void HandlePanelScaleChanged()
    {
        if (control is null)
            return;

        scale = control.PanelScale;
        OnPanelScaleChanged();
    }

    private void RefreshPresetNames()
    {
        var library = session.Library;
        presetNames = library.All.Select(preset => (library.Factory.Contains(preset) ? "★ " : "") + preset.Name).ToArray();
    }

    [LibraryImport("user32.dll")]
    private static partial nint SetParent(nint child, nint parent);
}
