using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Jacobi.Vst.Core;
using Jacobi.Vst.Plugin.Framework;
using Noumenon.Engine.Parameters;
using Noumenon.Engine.Presets;
using Noumenon.Ui;
using Noumenon.Ui.Hosting;
using Noumenon.Vst2.Parameters;

namespace Noumenon.Vst2.Editor;

/// <summary>
/// Hosts the shared <see cref="NoumenonEditor"/> inside the window the host provides: the
/// control is created on the host's UI thread, reparented onto the host's handle, shown, and
/// disposed again on close — a host opens and closes the editor many times in a session, so the
/// panel size is remembered across openings. While it is open a timer on that thread does what
/// the standalone's poll does: pushes bank changes to the host, runs the randomizer when the
/// engine asks and reports a latency change. It is also the editor's <see cref="IEditorHost"/>:
/// presets come from the shared library, samples from a file dialog, and the status line says
/// what the host is running at. A panel-size change asks the host to resize its window.
/// </summary>
internal sealed partial class PluginEditor : IVstPluginEditor, IEditorHost
{
    private const int PollMs = 33;

    private readonly PluginSession session;
    private readonly PluginParameters parameters;
    private readonly PluginState state;
    private readonly AudioProcessor audio;
    private NoumenonEditor? control;
    private System.Windows.Forms.Timer? timer;
    private IReadOnlyList<string> presetNames = [];
    private float scale = 1f;

    public PluginEditor(PluginSession session, PluginParameters parameters, PluginState state, AudioProcessor audio)
    {
        this.session = session;
        this.parameters = parameters;
        this.state = state;
        this.audio = audio;
    }

    // ---- IVstPluginEditor ----

    public Rectangle Bounds => new(0, 0, (int)MathF.Round(NoumenonEditor.LogicalWidth * scale), (int)MathF.Round(NoumenonEditor.LogicalHeight * scale));

    public VstKnobMode KnobMode { get; set; } = VstKnobMode.LinearMode;

    /// <summary>The hosted control while the editor is open.</summary>
    public NoumenonEditor? Control => control;

    public void Open(IntPtr hWnd)
    {
        Close();
        RefreshPresetNames();
        var editor = new NoumenonEditor(session.Engine, this) { Location = Point.Empty };
        editor.PanelScale = scale;
        editor.PanelScaleChanged += OnPanelScaleChanged;
        SetParent(editor.Handle, hWnd);
        editor.Show();
        control = editor;
        parameters.UseAutomation(session.Host?.GetInstance<IVstHostAutomation>());
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

        control.PanelScaleChanged -= OnPanelScaleChanged;
        control.Dispose();
        control = null;
    }

    public void ProcessIdle()
    {
    }

    public bool KeyDown(byte ascii, VstVirtualKey virtualKey, VstModifierKeys modifiers) => false;

    public bool KeyUp(byte ascii, VstVirtualKey virtualKey, VstModifierKeys modifiers) => false;

    // ---- IEditorHost ----

    public IReadOnlyList<string> PresetNames => presetNames;

    public int CurrentPresetIndex => session.Library.IndexOf(session.PresetPath);

    public string CurrentPresetName => session.PresetName;

    public string StatusText => session.CurrentNotice ?? $"{PluginIdentity.Name} VST2 · {(audio.SampleRate / 1000f).ToString("0.#", CultureInfo.InvariantCulture)} kHz · {audio.BlockSize} smp · {session.Engine.LatencySamples} smp latency";

    public bool KeyboardNotesActive => false;

    public void LoadPreset(int index)
    {
        var all = session.Library.All;
        if (index < 0 || index >= all.Count)
            return;

        var preset = all[index];
        try
        {
            state.Apply(PresetIo.Load(preset.Path), preset.Path);
            session.Notice($"Loaded {preset.Name}");
            session.TellHostDisplayChanged();
        }
        catch (Exception ex) when (ex is IOException or FormatException or JsonException or UnauthorizedAccessException)
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
        session.TellHostDisplayChanged();
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

    // ---- The poll ----

    private void OnTick(object? sender, EventArgs e)
    {
        if (control is null || control.IsDisposed)
            return;

        parameters.PushToHost(notify: true);
        if (session.Engine.ConsumeRandomizeRequest())
        {
            session.Randomize();
            session.TellHostDisplayChanged();
        }

        session.PollLatency();
    }

    private void OnPanelScaleChanged()
    {
        if (control is null)
            return;

        scale = control.PanelScale;
        session.Host?.GetInstance<IVstHostShell>()?.SizeWindow(Bounds.Width, Bounds.Height);
    }

    private void RefreshPresetNames()
    {
        var library = session.Library;
        presetNames = library.All.Select(preset => (library.Factory.Contains(preset) ? "★ " : "") + preset.Name).ToArray();
    }

    [LibraryImport("user32.dll")]
    private static partial nint SetParent(nint child, nint parent);
}
