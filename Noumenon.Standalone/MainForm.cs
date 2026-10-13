using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Noumenon.Dsp.Shared;
using Noumenon.Engine;
using Noumenon.Engine.Lanes;
using Noumenon.Engine.Parameters;
using Noumenon.Engine.Presets;
using Noumenon.Engine.Recording;
using Noumenon.Engine.Scales;
using Noumenon.Standalone.Audio;
using Noumenon.Standalone.Developer;
using Noumenon.Standalone.Dialogs;
using Noumenon.Standalone.Input;
using Noumenon.Ui;
using Noumenon.Ui.Hosting;

namespace Noumenon.Standalone;

/// <summary>
/// The instrument's main window and the hub of the standalone: it owns the engine, the audio and
/// MIDI hosts and the preset library, hosts the shared <see cref="NoumenonEditor"/> as its client
/// area and serves it as <see cref="IEditorHost"/> (presets, files, status). A UI timer polls the
/// engine for the two things the audio thread cannot do itself — running the randomizer when it
/// is requested and restarting the stream when Engine Rate changes — and keeps the transport,
/// recording and party-mode state current. The Phase 5 developer panel lives on in its own window
/// behind the View menu. The menus hold what a plugin host would own: devices, bounce, record.
/// </summary>
internal sealed class MainForm : Form, IEditorHost
{
    private const int PollIntervalMs = 33;
    private const int StatusEveryPolls = 6;
    private const int ToolbarEveryPolls = 3;
    private const int SmokeCloseMs = 2500;
    private const float MeterDecay = 0.85f;

    private static readonly int[] LaneLimits = [30, 60, 120, 300];
    private static readonly int[] PartyIntervals = [10, 30, 60, 120];
    private static readonly float[] EditorScales = [1f, 1.5f, 2f];

    private readonly LaunchOptions options;
    private readonly HostSettings settings = HostSettings.Load();
    private readonly NoumenonEngine engine = new();
    private readonly AudioHost host;
    private readonly MidiHost midi;
    private readonly KeyboardMidi keyboard;
    private readonly PresetLibrary library = new();
    private readonly NoumenonEditor editor;
    private readonly Panel editorHost = new() { Dock = DockStyle.Fill, BackColor = EditorTheme.Panel, AutoScroll = true };
    private readonly Xoshiro128 seeds = new(((ulong)Environment.TickCount64 * 0x9E3779B97F4A7C15UL) ^ (ulong)DateTime.UtcNow.Ticks);
    private readonly System.Windows.Forms.Timer timer = new() { Interval = PollIntervalMs };
    private DeveloperWindow? developerWindow;

    private readonly MenuStrip menu = new();
    private readonly ToolStrip toolbar = new() { GripStyle = ToolStripGripStyle.Hidden };
    private readonly StatusStrip statusBar = new();
    private readonly ToolStripButton playButton = new("Play") { CheckOnClick = true };
    private readonly ToolStripTextBox tempoBox = new() { AutoSize = false, Width = 48, TextBoxTextAlign = HorizontalAlignment.Right };
    private readonly ToolStripButton recordButton = new("● Rec");
    private readonly ToolStripLabel recordLabel = new("");
    private readonly ToolStripButton partyButton = new("Party") { CheckOnClick = true };
    private readonly ToolStripLabel seedLabel = new("");
    private readonly ToolStripStatusLabel statusLabel = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel loadLabel = new();
    private readonly ToolStripStatusLabel inputLabel = new();
    private readonly ToolStripStatusLabel midiLabel = new();
    private readonly ToolStripStatusLabel lanesLabel = new();
    private readonly ToolStripProgressBar meterLeft = new() { AutoSize = false, Width = 70, Maximum = 1000, Style = ProgressBarStyle.Continuous };
    private readonly ToolStripProgressBar meterRight = new() { AutoSize = false, Width = 70, Maximum = 1000, Style = ProgressBarStyle.Continuous };
    private readonly ToolStripMenuItem recordItem = new("&Record") { ShortcutKeys = Keys.F9 };
    private readonly ToolStripMenuItem playItem = new("&Play (internal transport)") { ShortcutKeys = Keys.F8, CheckOnClick = true };
    private readonly ToolStripMenuItem partyItem = new("Party &Mode (randomize on a timer)") { ShortcutKeys = Keys.F10, CheckOnClick = true };
    private readonly ToolStripMenuItem keyboardItem = new("Computer &Keyboard Plays Notes") { CheckOnClick = true };
    private readonly ToolStripMenuItem midiMenu = new("&MIDI");
    private readonly ToolStripMenuItem laneLimitMenu = new("Lane &Length Limit");
    private readonly ToolStripMenuItem partyIntervalMenu = new("Party &Interval");
    private readonly ToolStripMenuItem recordFormatMenu = new("Record &Format");
    private readonly ToolStripMenuItem scaleMenu = new("Panel &Size");

    private string? currentPresetPath;
    private string currentPresetName = "Init";
    private IReadOnlyList<string> presetNames = [];
    private int polls;
    private float meterL;
    private float meterR;
    private long shownMidiMessages;
    private int partyTicksLeft;
    private string? notice;
    private long noticeUntil;
    private string shortStatus = "";

    public MainForm(LaunchOptions options)
    {
        this.options = options;
        Text = "Noumenon";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        KeyPreview = true;

        if (Math.Abs(settings.LaneMaxSeconds - LaneSet.DefaultMaxSeconds) > 0.01f)
            engine.ConfigureLanes(settings.LaneMaxSeconds);

        engine.Clock.HostTempo = settings.Tempo;
        host = new AudioHost(engine, settings);
        midi = new MidiHost(engine.Midi);
        keyboard = new KeyboardMidi(engine.Midi);
        RefreshPresetNames();
        editor = new NoumenonEditor(engine, this) { Location = new Point(0, 0) };
        editor.PanelScale = EditorScales.Contains(settings.EditorScale) ? settings.EditorScale : 1f;
        editorHost.Controls.Add(editor);
        editor.PanelScaleChanged += OnEditorScaleChanged;

        BuildMenu();
        BuildToolbar();
        BuildStatusBar();
        Controls.Add(editorHost);
        Controls.Add(toolbar);
        Controls.Add(statusBar);
        Controls.Add(menu);
        MainMenuStrip = menu;
        editorHost.BringToFront();
        timer.Tick += OnTick;
    }

    // ---- IEditorHost ----

    public IReadOnlyList<string> PresetNames => presetNames;

    public int CurrentPresetIndex => library.IndexOf(currentPresetPath);

    public string CurrentPresetName => currentPresetName;

    public string StatusText => notice is not null && Environment.TickCount64 < noticeUntil ? notice : shortStatus;

    public bool KeyboardNotesActive => settings.KeyboardMidi;

    public void LoadPreset(int index)
    {
        if (index >= 0 && index < library.All.Count)
            LoadPreset(library.All[index].Path);
    }

    public void SavePreset() => SavePreset(forceDialog: false);

    public void LoadSample(int key)
    {
        using var dialog = new OpenFileDialog { Filter = MediaFoundationDecoding.FileFilter, Title = $"Load a sample into key {key}" };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            LoadSampleFile(key, dialog.FileName);
    }

    public void LoadSampleFile(int key, string path)
    {
        Notice($"Loading {Path.GetFileName(path)}…");
        Task.Run(() => engine.Samples.Load(key, path)).ContinueWith(task =>
        {
            if (IsDisposed)
                return;

            if (task.IsFaulted)
            {
                Notice("Sample not loaded: " + (task.Exception?.GetBaseException().Message ?? "unknown error"));
                return;
            }

            engine.Parameters.Set(ParamId.SamplerSelect, key);
            Notice($"Loaded {task.Result.Name} on key {key}");
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    // ---- Window life cycle ----

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        FitToEditor();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        StartAudio();
        if (!midi.Open(settings.MidiDevice) && midi.Error is not null)
            Notice(midi.Error);

        if (options.PresetPath is not null)
        {
            var resolved = PresetLibrary.Resolve(options.PresetPath);
            if (resolved is null)
                Notice($"Preset '{options.PresetPath}' not found.");
            else
                LoadPreset(resolved);
        }

        timer.Start();
        if (!options.Smoke)
            return;

        for (var page = 0; page < NoumenonEditor.PageCount; page++)
        {
            editor.Page = page;
            editor.Refresh();
            if (options.ScreenshotFolder is { } folder)
                SaveScreenshot(folder, page);
        }

        editor.Page = 0;
        if (options.ScreenshotFolder is { } reportFolder)
        {
            var report = editor.SelfTest();
            File.WriteAllText(Path.Combine(reportFolder, "selftest.txt"), report);
            if (!report.StartsWith("0 failures", StringComparison.Ordinal))
                Environment.ExitCode = 1;
        }
        var closer = new System.Windows.Forms.Timer { Interval = SmokeCloseMs };
        closer.Tick += (_, _) =>
        {
            closer.Stop();
            closer.Dispose();
            Close();
        };
        closer.Start();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        timer.Stop();
        keyboard.ReleaseAll();
        host.StopRecording();
        host.Dispose();
        midi.Dispose();
        settings.EditorScale = editor.PanelScale;
        settings.Save();
        developerWindow?.Dispose();
        base.OnFormClosing(e);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        keyboard.ReleaseAll();
        base.OnDeactivate(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (KeyboardMidiActive(e) && keyboard.KeyDown(e.KeyCode))
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (settings.KeyboardMidi && keyboard.KeyUp(e.KeyCode))
        {
            e.Handled = true;
            return;
        }

        base.OnKeyUp(e);
    }

    private bool KeyboardMidiActive(KeyEventArgs e)
    {
        if (!settings.KeyboardMidi || e.Control || e.Alt)
            return false;

        var control = ActiveControl;
        while (control is ContainerControl container && container.ActiveControl is not null)
            control = container.ActiveControl;

        return control is not (TextBoxBase or NumericUpDown or ComboBox { DropDownStyle: not ComboBoxStyle.DropDownList });
    }

    private void SaveScreenshot(string folder, int page)
    {
        try
        {
            Directory.CreateDirectory(folder);
            using var bitmap = editor.RenderToBitmap();
            bitmap.Save(Path.Combine(folder, $"editor-page{page + 1}.png"), System.Drawing.Imaging.ImageFormat.Png);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
        {
            Notice("Screenshot failed: " + ex.Message);
        }
    }

    private void FitToEditor()
    {
        menu.PerformLayout();
        toolbar.PerformLayout();
        statusBar.PerformLayout();
        var area = Screen.FromControl(this).WorkingArea;
        var chrome = menu.Height + toolbar.Height + statusBar.Height;
        var width = Math.Min(Math.Max(editor.Width, 760), area.Width - 24);
        var height = Math.Min(chrome + editor.Height, area.Height - 60);
        ClientSize = new Size(width, height);   // a panel larger than the screen scrolls inside its host
        editorHost.AutoScrollPosition = Point.Empty;
        editor.Location = new Point(Math.Max(0, (editorHost.ClientSize.Width - editor.Width) / 2), 0);
    }

    // Both the View menu and the editor's own size buttons end here: the editor has already resized itself.
    private void OnEditorScaleChanged()
    {
        var scale = editor.PanelScale;
        settings.EditorScale = scale;
        foreach (ToolStripMenuItem item in scaleMenu.DropDownItems)
            item.Checked = item.Tag is float f && Math.Abs(f - scale) < 0.01f;

        FitToEditor();
    }

    // ---- Building the chrome ----

    private void BuildMenu()
    {
        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.Add(Item("&New (Init Patch)", Keys.Control | Keys.N, NewPatch));
        file.DropDownItems.Add(Item("&Open Preset…", Keys.Control | Keys.O, OpenPreset));
        file.DropDownItems.Add(Item("&Save Preset", Keys.Control | Keys.S, () => SavePreset(forceDialog: false)));
        file.DropDownItems.Add(Item("Save Preset &As…", Keys.Control | Keys.Shift | Keys.S, () => SavePreset(forceDialog: true)));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Item("Load Sa&mple into Key…", Keys.Control | Keys.L, () => LoadSample(engine.Parameters.GetInt(ParamId.SamplerSelect))));
        file.DropDownItems.Add(Item("Load Scala Sca&le (.scl)…", Keys.None, LoadScale));
        file.DropDownItems.Add(Item("Reset User Scale to 12-TET", Keys.None, () =>
        {
            engine.UserScale = Engine.Scales.Scale.Equal12;
            Notice("User scale reset to 12-TET.");
        }));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Item("&Bounce to WAV…", Keys.Control | Keys.B, Bounce));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Item("E&xit", Keys.Alt | Keys.F4, Close));

        var audio = new ToolStripMenuItem("&Audio");
        audio.DropDownItems.Add(Item("Audio && MIDI &Settings…", Keys.Control | Keys.Oemcomma, ShowSettings));
        audio.DropDownItems.Add(Item("&Restart Audio", Keys.None, RestartAudio));
        audio.DropDownItems.Add(Item("Sto&p Audio", Keys.None, () =>
        {
            host.Stop();
            UpdateStatus();
        }));
        audio.DropDownItems.Add(new ToolStripSeparator());
        recordItem.Click += (_, _) => ToggleRecording();
        audio.DropDownItems.Add(recordItem);
        foreach (var format in new[] { WavSampleFormat.Pcm24, WavSampleFormat.Float32 })
        {
            var chosen = format;
            var item = new ToolStripMenuItem(format == WavSampleFormat.Pcm24 ? "24-bit PCM" : "32-bit float") { Checked = settings.RecordFormat == format };
            item.Click += (_, _) =>
            {
                settings.RecordFormat = chosen;
                foreach (ToolStripMenuItem sibling in recordFormatMenu.DropDownItems)
                    sibling.Checked = sibling == item;
            };
            recordFormatMenu.DropDownItems.Add(item);
        }

        audio.DropDownItems.Add(recordFormatMenu);
        audio.DropDownItems.Add(Item("Open Recordings &Folder", Keys.None, () => OpenFolder(settings.EffectiveRecordFolder)));
        audio.DropDownItems.Add(new ToolStripSeparator());
        audio.DropDownItems.Add(Item("ASIO &Control Panel…", Keys.None, () =>
        {
            if (host.CanShowAsioPanel)
                host.ShowAsioControlPanel();
            else
                Notice("The ASIO control panel is available while an ASIO driver is running.");
        }));

        keyboardItem.Click += (_, _) =>
        {
            settings.KeyboardMidi = keyboardItem.Checked;
            if (!settings.KeyboardMidi)
                keyboard.ReleaseAll();

            settings.Save();
        };
        midiMenu.DropDownOpening += (_, _) => FillMidiMenu();
        FillMidiMenu();

        var transport = new ToolStripMenuItem("&Transport");
        playItem.Click += (_, _) => SetPlaying(playItem.Checked);
        transport.DropDownItems.Add(playItem);
        transport.DropDownItems.Add(Item("&Tempo…", Keys.Control | Keys.T, () =>
        {
            var typed = InputDialog.Prompt(this, "Tempo", "Beats per minute (20 .. 300)", settings.Tempo.ToString("0.##", CultureInfo.InvariantCulture));
            if (typed is not null)
                ApplyTempo(typed);
        }));
        transport.DropDownItems.Add(new ToolStripSeparator());
        transport.DropDownItems.Add(Item("Rando&mize", Keys.F5, Randomize));
        transport.DropDownItems.Add(Item("&Restart Lanes", Keys.F6, () => engine.Clock.RequestRestart()));
        transport.DropDownItems.Add(Item("M&ute", Keys.F7, () => engine.Parameters.Set(ParamId.Mute, !engine.Parameters.GetBool(ParamId.Mute))));
        foreach (var limit in LaneLimits)
        {
            var seconds = limit;
            var item = new ToolStripMenuItem(limit + " s") { Checked = (int)settings.LaneMaxSeconds == limit };
            item.Click += (_, _) => SetLaneLimit(seconds);
            laneLimitMenu.DropDownItems.Add(item);
        }

        transport.DropDownItems.Add(laneLimitMenu);
        transport.DropDownItems.Add(new ToolStripSeparator());
        partyItem.Click += (_, _) => SetParty(partyItem.Checked);
        transport.DropDownItems.Add(partyItem);
        foreach (var interval in PartyIntervals)
        {
            var seconds = interval;
            var item = new ToolStripMenuItem(interval + " s") { Checked = settings.PartyIntervalSeconds == interval };
            item.Click += (_, _) =>
            {
                settings.PartyIntervalSeconds = seconds;
                partyTicksLeft = Math.Min(partyTicksLeft, PartyTicks);
                foreach (ToolStripMenuItem sibling in partyIntervalMenu.DropDownItems)
                    sibling.Checked = sibling == item;
            };
            partyIntervalMenu.DropDownItems.Add(item);
        }

        transport.DropDownItems.Add(partyIntervalMenu);

        var presets = new ToolStripMenuItem("&Presets");
        presets.DropDownItems.Add(Item("&Previous", Keys.Control | Keys.Left, () => StepPreset(-1)));
        presets.DropDownItems.Add(Item("&Next", Keys.Control | Keys.Right, () => StepPreset(1)));
        presets.DropDownItems.Add(Item("&Random Factory Preset", Keys.Control | Keys.R, RandomFactoryPreset));
        presets.DropDownItems.Add(Item("&Morph To…", Keys.Control | Keys.M, MorphTo));
        presets.DropDownItems.Add(new ToolStripSeparator());
        presets.DropDownItems.Add(Item("Re&scan Folders", Keys.None, () =>
        {
            library.Rescan();
            RefreshPresetNames();
        }));
        presets.DropDownItems.Add(Item("Open &User Preset Folder", Keys.None, () => OpenFolder(library.UserFolder)));

        var view = new ToolStripMenuItem("&View");
        foreach (var scale in EditorScales)
        {
            var chosen = scale;
            var item = new ToolStripMenuItem("×" + scale.ToString("0.#", CultureInfo.InvariantCulture)) { Tag = scale, Checked = Math.Abs(editor.PanelScale - scale) < 0.01f };
            item.Click += (_, _) => editor.PanelScale = chosen;
            scaleMenu.DropDownItems.Add(item);
        }

        view.DropDownItems.Add(scaleMenu);
        view.DropDownItems.Add(new ToolStripSeparator());
        view.DropDownItems.Add(Item("&MAIN Page", Keys.Control | Keys.D1, () => editor.Page = 0));
        view.DropDownItems.Add(Item("&POST Page", Keys.Control | Keys.D2, () => editor.Page = 1));
        view.DropDownItems.Add(Item("M&OD Page", Keys.Control | Keys.D3, () => editor.Page = 2));
        view.DropDownItems.Add(new ToolStripSeparator());
        view.DropDownItems.Add(Item("&Developer Panel…", Keys.Control | Keys.D, ShowDeveloperPanel));

        var help = new ToolStripMenuItem("&Help");
        help.DropDownItems.Add(Item("&Keys…", Keys.F1, ShowKeys));
        help.DropDownItems.Add(Item("&About", Keys.None, () => MessageBox.Show(this, $"Noumenon {Application.ProductVersion}\nA generative ambient loop instrument.\n\nSettings: {HostSettings.FilePath}\nFactory presets: {library.FactoryFolder ?? "(not found)"}\nUser presets: {library.UserFolder}", "About Noumenon")));

        menu.Items.AddRange([file, audio, midiMenu, transport, presets, view, help]);
    }

    private void FillMidiMenu()
    {
        midiMenu.DropDownItems.Clear();
        var none = new ToolStripMenuItem("(no MIDI port)") { Checked = settings.MidiDevice is null };
        none.Click += (_, _) => SelectMidi(null);
        midiMenu.DropDownItems.Add(none);
        foreach (var entry in AudioDevices.MidiInputs())
        {
            var name = entry.Id;
            var item = new ToolStripMenuItem(entry.Name) { Checked = string.Equals(settings.MidiDevice, name, StringComparison.OrdinalIgnoreCase) };
            item.Click += (_, _) => SelectMidi(name);
            midiMenu.DropDownItems.Add(item);
        }

        midiMenu.DropDownItems.Add(new ToolStripSeparator());
        keyboardItem.Checked = settings.KeyboardMidi;
        midiMenu.DropDownItems.Add(keyboardItem);
        midiMenu.DropDownItems.Add(Item("Panic (All Notes Off)", Keys.F12, () =>
        {
            keyboard.ReleaseAll();
            engine.Midi.AllNotesOff();
        }));
    }

    private void SelectMidi(string? name)
    {
        settings.MidiDevice = name;
        if (!midi.Open(name) && midi.Error is not null)
            Notice(midi.Error);

        settings.Save();
    }

    private void BuildToolbar()
    {
        playButton.Click += (_, _) => SetPlaying(playButton.Checked);
        tempoBox.Text = settings.Tempo.ToString("0.#", CultureInfo.InvariantCulture);
        tempoBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
                return;

            ApplyTempo(tempoBox.Text);
            e.SuppressKeyPress = true;
            editor.Focus();
        };
        tempoBox.Leave += (_, _) => ApplyTempo(tempoBox.Text);
        recordButton.Click += (_, _) => ToggleRecording();
        partyButton.Click += (_, _) => SetParty(partyButton.Checked);

        playButton.ToolTipText = "Internal transport: play state for Restart On Host Play and Host Sync (F8)";
        toolbar.Items.Add(playButton);
        toolbar.Items.Add(tempoBox);
        toolbar.Items.Add(new ToolStripLabel("BPM"));
        toolbar.Items.Add(new ToolStripSeparator());
        recordButton.ToolTipText = "Record the output to a WAV (F9)";
        toolbar.Items.Add(recordButton);
        toolbar.Items.Add(recordLabel);
        toolbar.Items.Add(new ToolStripSeparator());
        partyButton.ToolTipText = "Party mode: randomize every interval (F10)";
        toolbar.Items.Add(partyButton);
        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(Button("Audio…", "Audio & MIDI settings (Ctrl+,)", ShowSettings));
        toolbar.Items.Add(Button("Bounce…", "Render the snapshot to a WAV (Ctrl+B)", Bounce));
        toolbar.Items.Add(Button("Dev panel", "Every parameter as a plain control (Ctrl+D)", ShowDeveloperPanel));
        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(seedLabel);
    }

    private void BuildStatusBar()
    {
        statusBar.Items.AddRange([statusLabel, loadLabel, inputLabel, midiLabel, lanesLabel, new ToolStripStatusLabel("L"), meterLeft, new ToolStripStatusLabel("R"), meterRight]);
        statusBar.SizingGrip = false;
    }

    private static ToolStripMenuItem Item(string text, Keys shortcut, Action action)
    {
        var item = new ToolStripMenuItem(text) { ShortcutKeys = shortcut };
        item.Click += (_, _) => action();

        return item;
    }

    private static ToolStripButton Button(string text, string tip, Action action)
    {
        var button = new ToolStripButton(text) { ToolTipText = tip, DisplayStyle = ToolStripItemDisplayStyle.Text };
        button.Click += (_, _) => action();

        return button;
    }

    private void ShowDeveloperPanel()
    {
        developerWindow ??= new DeveloperWindow(engine, midi, keyboard) { Location = new Point(Left + 40, Top + 40) };
        developerWindow.Show(this);
        developerWindow.Activate();
    }

    // ---- The poll ----

    private void OnTick(object? sender, EventArgs e)
    {
        polls++;
        host.TakePeaks(out var l, out var r);
        meterL = MathF.Max(l, meterL * MeterDecay);
        meterR = MathF.Max(r, meterR * MeterDecay);
        meterLeft.Value = (int)(Math.Clamp(meterL, 0f, 1f) * 1000);
        meterRight.Value = (int)(Math.Clamp(meterR, 0f, 1f) * 1000);

        if (engine.ConsumeRandomizeRequest())
            Randomize();

        if (host.NeedsRestartForEngineRate)
        {
            Notice($"Engine rate {ParameterTable.EngineRates[engine.Parameters.GetInt(ParamId.EngineRate)]}: restarting the stream.");
            RestartAudio();
        }

        if (partyItem.Checked)
        {
            if (--partyTicksLeft <= 0)
            {
                Randomize();
                partyTicksLeft = PartyTicks;
            }

            partyButton.Text = $"Party {(partyTicksLeft * PollIntervalMs + 999) / 1000} s";
        }

        if (polls % ToolbarEveryPolls == 0)
            SyncToolbar();

        if (polls % StatusEveryPolls == 0)
            UpdateStatus();
    }

    private int PartyTicks => Math.Max(1, settings.PartyIntervalSeconds * 1000 / PollIntervalMs);

    private void SyncToolbar()
    {
        var playing = engine.Clock.HostPlaying;
        if (playButton.Checked != playing)
            playButton.Checked = playing;

        if (playItem.Checked != playing)
            playItem.Checked = playing;

        var recorder = host.Recorder;
        var recording = recorder is not null;
        if (recordButton.Checked != recording)
            recordButton.Checked = recording;

        if (recordItem.Checked != recording)
            recordItem.Checked = recording;

        recordLabel.Text = recorder is null ? "" : $"{TimeSpan.FromSeconds(recorder.SecondsWritten):mm\\:ss}{(recorder.FramesDropped > 0 ? " · dropped " + recorder.FramesDropped : "")}{(recorder.Error is null ? "" : " · " + recorder.Error.Message)}";
        if (recorder?.Error is not null)
            ToggleRecording();
    }

    private void UpdateStatus()
    {
        var inv = CultureInfo.InvariantCulture;
        var now = Environment.TickCount64;
        statusLabel.Text = notice is not null && now < noticeUntil ? notice : host.Description + (host.LastError is null ? "" : " — " + host.LastError);
        loadLabel.Text = host.IsRunning
            ? $"load {(host.Load * 100).ToString("0", inv)} % · overruns {host.Overruns} · {host.LastBlockFrames} frames · latency {engine.LatencySamples} smp + {host.DeviceLatencyMs} ms"
            : "audio off";
        inputLabel.Text = host.InputDescription is null ? "no input" : $"in: {host.InputDescription}{(host.InputUnderruns > 0 ? " · " + host.InputUnderruns + " underruns" : "")}{(host.InputError is null ? "" : " · " + host.InputError)}";
        var messages = midi.Messages;
        midiLabel.Text = $"MIDI: {midi.DeviceName ?? (settings.KeyboardMidi ? "keyboard" : "off")}{(messages != shownMidiMessages ? " ●" : "")}";
        shownMidiMessages = messages;

        var playing = 0;
        var recording = 0;
        foreach (var lane in engine.Lanes.All)
        {
            if (lane.IsPlaying)
                playing++;

            if (lane.IsRecording)
                recording++;
        }

        lanesLabel.Text = $"lanes: {playing} playing{(recording > 0 ? ", " + recording + " recording" : "")}";
        shortStatus = host.IsRunning
            ? $"{(settings.Api == OutputApi.Asio ? "ASIO" : settings.Exclusive ? "WASAPI excl" : "WASAPI")} {host.SampleRate / 1000.0:0.#} kHz · load {(host.Load * 100).ToString("0", inv)} % · ovr {host.Overruns}{(host.Recorder is { } rec ? " · REC " + TimeSpan.FromSeconds(rec.SecondsWritten).ToString("mm\\:ss") : "")}"
            : "audio off" + (host.LastError is null ? "" : ": " + host.LastError);
    }

    private void Notice(string text)
    {
        notice = text;
        noticeUntil = Environment.TickCount64 + 8000;
        statusLabel.Text = text;
    }

    // ---- Audio, MIDI, transport ----

    private void StartAudio()
    {
        if (!host.Start())
            Notice(host.LastError ?? "The audio device could not be started.");

        UpdateStatus();
    }

    private void RestartAudio()
    {
        var wasRecording = host.Recorder is not null;
        StartAudio();
        if (wasRecording)
            Notice("The recording was stopped because the stream restarted.");
    }

    private void ShowSettings()
    {
        using var dialog = new AudioSettingsDialog(settings, host);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        var laneLimitChanged = Math.Abs(dialog.Result.LaneMaxSeconds - settings.LaneMaxSeconds) > 0.01f;
        var midiChanged = !string.Equals(dialog.Result.MidiDevice, settings.MidiDevice, StringComparison.OrdinalIgnoreCase);
        settings.CopyFrom(dialog.Result);
        settings.Save();
        foreach (ToolStripMenuItem item in recordFormatMenu.DropDownItems)
            item.Checked = item.Text == (settings.RecordFormat == WavSampleFormat.Pcm24 ? "24-bit PCM" : "32-bit float");

        foreach (ToolStripMenuItem item in laneLimitMenu.DropDownItems)
            item.Checked = item.Text == (int)settings.LaneMaxSeconds + " s";

        keyboardItem.Checked = settings.KeyboardMidi;
        RestartAudio();
        if (midiChanged)
            SelectMidi(settings.MidiDevice);

        if (laneLimitChanged)
            engine.ConfigureLanes(settings.LaneMaxSeconds);
    }

    private void SetLaneLimit(int seconds)
    {
        var hasLoops = engine.Lanes.All.Any(lane => lane.HasRecording);
        if (hasLoops && MessageBox.Show(this, "Changing the lane length limit clears every lane recording. Continue?", "Lane length limit", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;

        settings.LaneMaxSeconds = seconds;
        engine.ConfigureLanes(seconds);
        foreach (ToolStripMenuItem item in laneLimitMenu.DropDownItems)
            item.Checked = item.Text == seconds + " s";

        settings.Save();
    }

    private void SetPlaying(bool playing)
    {
        engine.Clock.HostPlaying = playing;
        playButton.Checked = playing;
        playItem.Checked = playing;
    }

    private void ApplyTempo(string text)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var tempo) && !double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out tempo))
        {
            tempoBox.Text = settings.Tempo.ToString("0.#", CultureInfo.InvariantCulture);
            return;
        }

        tempo = Math.Clamp(tempo, 20.0, 300.0);
        settings.Tempo = tempo;
        engine.Clock.HostTempo = tempo;
        tempoBox.Text = tempo.ToString("0.#", CultureInfo.InvariantCulture);
    }

    private void SetParty(bool on)
    {
        partyItem.Checked = on;
        partyButton.Checked = on;
        partyTicksLeft = on ? PartyTicks : 0;
        if (!on)
            partyButton.Text = "Party";
    }

    private void ToggleRecording()
    {
        if (host.Recorder is { } running)
        {
            var finished = host.StopRecording() ?? running;
            Notice($"Recorded {finished.SecondsWritten.ToString("0.0", CultureInfo.InvariantCulture)} s to {finished.Path}{(finished.FramesDropped > 0 ? $" — {finished.FramesDropped} frames dropped" : "")}{(finished.Error is null ? "" : " — " + finished.Error.Message)}");
            return;
        }

        if (!host.IsRunning)
        {
            Notice("Start the audio stream before recording.");
            return;
        }

        try
        {
            var folder = settings.EffectiveRecordFolder;
            Directory.CreateDirectory(folder);
            var recorder = host.StartRecording(Path.Combine(folder, $"Noumenon {DateTime.Now:yyyy-MM-dd HH-mm-ss}.wav"));
            Notice($"Recording to {recorder.Path}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Notice("Recording failed: " + ex.Message);
        }
    }

    private void Randomize()
    {
        var bank = engine.Parameters;
        var fixedSeed = bank.GetInt(ParamId.RandomSeed);
        var seed = fixedSeed != 0 ? (ulong)fixedSeed : seeds.Next();
        Randomizer.Randomize(engine, seed, (RandomizeScope)bank.GetInt(ParamId.RandomizeScope), bank.Get(ParamId.RandomizeAmount));
        seedLabel.Text = $"seed {seed}";
        currentPresetPath = null;
        currentPresetName = $"random {seed}";
        UpdateTitle();
        Notice($"Randomized: seed {seed}, {ParameterTable.RandomizeScopes[Math.Clamp(bank.GetInt(ParamId.RandomizeScope), 0, ParameterTable.RandomizeScopes.Count - 1)]}, {(bank.Get(ParamId.RandomizeAmount) * 100).ToString("0", CultureInfo.InvariantCulture)} % — type the seed into Random Seed to recall it");
    }

    // ---- Presets ----

    private void RefreshPresetNames() => presetNames = library.All.Select(preset => (library.Factory.Contains(preset) ? "★ " : "") + preset.Name).ToArray();

    private void StepPreset(int step)
    {
        if (library.All.Count == 0)
            return;

        var index = library.IndexOf(currentPresetPath);
        index = index < 0 ? (step > 0 ? 0 : library.All.Count - 1) : (index + step + library.All.Count) % library.All.Count;
        LoadPreset(library.All[index].Path);
    }

    private void RandomFactoryPreset()
    {
        var bank = library.Factory.Count > 0 ? library.Factory : library.All;
        if (bank.Count == 0)
        {
            Notice("No presets found.");
            return;
        }

        LoadPreset(bank[seeds.NextInt(bank.Count)].Path);
    }

    private void NewPatch()
    {
        engine.ApplyPreset(new PresetState { Name = "Init", Seed = engine.Seed });
        currentPresetPath = null;
        currentPresetName = "Init";
        UpdateTitle();
    }

    private void OpenPreset()
    {
        using var dialog = new OpenFileDialog { Filter = $"Noumenon presets|*{PresetIo.Extension}|All files|*.*", InitialDirectory = settings.LastPresetFolder ?? library.FactoryFolder ?? library.UserFolder };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        settings.LastPresetFolder = Path.GetDirectoryName(dialog.FileName);
        LoadPreset(dialog.FileName);
    }

    private void LoadPreset(string path)
    {
        PresetState state;
        try
        {
            state = PresetIo.Load(path);
        }
        catch (Exception ex) when (ex is IOException or FormatException or JsonException or UnauthorizedAccessException)
        {
            Notice($"Preset not loaded: {ex.Message}");
            return;
        }

        engine.ApplyPreset(state, loadSamples: false);
        engine.Samples.ClearAll();
        currentPresetPath = path;
        currentPresetName = state.Name;
        UpdateTitle();
        Notice($"Loaded {state.Name}");
        if (state.Samples.Count == 0)
            return;

        var references = state.Samples.ToArray();
        Task.Run(() =>
        {
            var warnings = new List<string>();
            foreach (var reference in references)
            {
                try
                {
                    engine.Samples.Load(reference.Key, reference.Path, reference.Gain, reference.RootNote);
                }
                catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidDataException or UnauthorizedAccessException)
                {
                    warnings.Add($"Sample {reference.Key} ({reference.Path}): {ex.Message}");
                }
            }

            return warnings;
        }).ContinueWith(task =>
        {
            if (IsDisposed || task.Result.Count == 0)
                return;

            Notice(string.Join(" · ", task.Result));
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void SavePreset(bool forceDialog)
    {
        var path = currentPresetPath;
        var isFactory = library.IsFactory(path);
        if (forceDialog || path is null || isFactory)
        {
            Directory.CreateDirectory(library.UserFolder);
            using var dialog = new SaveFileDialog { Filter = $"Noumenon presets|*{PresetIo.Extension}", InitialDirectory = settings.LastPresetFolder ?? library.UserFolder, FileName = currentPresetName + PresetIo.Extension };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            path = dialog.FileName;
            settings.LastPresetFolder = Path.GetDirectoryName(path);
        }

        var name = Path.GetFileNameWithoutExtension(path);
        try
        {
            PresetIo.Save(engine.CapturePreset(name), path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Notice("Preset not saved: " + ex.Message);
            return;
        }

        currentPresetPath = path;
        currentPresetName = name;
        library.Rescan();
        RefreshPresetNames();
        UpdateTitle();
        Notice($"Saved {path}");
    }

    private void MorphTo()
    {
        if (library.All.Count == 0)
        {
            Notice("No presets to morph to.");
            return;
        }

        using var picker = new PresetPickerDialog(library.All, "Morph to preset");
        if (picker.ShowDialog(this) != DialogResult.OK || picker.Selected is not { } target)
            return;

        try
        {
            engine.Morph(PresetIo.Load(target.Path), picker.Seconds);
            Notice($"Morphing to {target.Name} over {picker.Seconds.ToString("0.#", CultureInfo.InvariantCulture)} s (lanes and samples stay).");
        }
        catch (Exception ex) when (ex is IOException or FormatException or JsonException or UnauthorizedAccessException)
        {
            Notice($"Preset not loaded: {ex.Message}");
        }
    }

    private void LoadScale()
    {
        using var dialog = new OpenFileDialog { Filter = "Scala scales|*.scl|All files|*.*", Title = "Load a Scala scale as the User scale" };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            var scale = ScalaParser.ParseFile(dialog.FileName);
            engine.UserScale = scale;
            engine.Parameters.Set(ParamId.Scale, ParameterTable.Scales.Count - 1);
            Notice($"User scale: {scale.Name} ({scale.DegreesCents.Count} degrees per {scale.PeriodCents.ToString("0.#", CultureInfo.InvariantCulture)} cents), selected.");
        }
        catch (Exception ex) when (ex is IOException or FormatException or UnauthorizedAccessException)
        {
            Notice("Scale not loaded: " + ex.Message);
        }
    }

    private void Bounce()
    {
        using var dialog = new BounceDialog(engine, settings, host.SampleRate, currentPresetName);
        dialog.ShowDialog(this);
    }

    private void ShowKeys()
    {
        MessageBox.Show(this,
            "Panel: Space restart lanes · R randomize · M mute (R and M play notes while the computer keyboard is a MIDI keyboard) · Ctrl+Z / Ctrl+Y undo / redo · Ctrl+Tab next page\n\n" +
            "F5 Randomize · F6 Restart lanes · F7 Mute · F8 Play / stop the internal transport · F9 Record · F10 Party mode · F12 All notes off\n\n" +
            "Ctrl+N Init · Ctrl+O / Ctrl+S Open / save preset · Ctrl+Left / Right previous / next preset · Ctrl+R random factory preset · Ctrl+M morph · Ctrl+B bounce · Ctrl+L load sample · Ctrl+T tempo · Ctrl+, audio settings · Ctrl+D developer panel · Ctrl+1/2/3 pages\n\n" +
            "Computer keyboard (when on): Z–M one octave with S D G H J as sharps, Q–P the next octave, minus / equals shift octaves.\n\n" +
            "Lanes: click the small display under a fader to arm it, drag the fader to record, release; the button above the display plays the loop. Right-click a fader or knob for its lane menu.",
            "Keys");
    }

    private void UpdateTitle() => Text = $"Noumenon — {currentPresetName}";

    private void OpenFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Notice(ex.Message);
        }
    }
}
