using Noumenon.Engine;
using Noumenon.Engine.Parameters;
using Noumenon.Standalone.Audio;
using Noumenon.Standalone.Input;

namespace Noumenon.Standalone.Developer;

/// <summary>
/// The Phase 5 developer panel: every parameter in <see cref="ParameterTable"/> as a
/// <see cref="ParameterRow"/>, grouped by panel section onto a handful of tabs, plus a Samples
/// page for the sample map and a Monitor page for what the engine is doing. Ugly but complete —
/// everything is playable and lane-recordable from here before the real editor exists, and the
/// editor keeps this panel behind a menu afterwards. Pages are built the first time they are
/// shown so the window opens quickly, and only the visible page is polled.
/// </summary>
internal sealed class DeveloperPanel : UserControl
{
    private static readonly (string Title, ParamGroup[] Groups)[] Pages =
    [
        ("Osc A", [ParamGroup.OscillatorA]),
        ("Osc B", [ParamGroup.OscillatorB]),
        ("Sections", [ParamGroup.SectionA, ParamGroup.SectionB]),
        ("Pitch & Mix", [ParamGroup.Pitch, ParamGroup.Mix, ParamGroup.PlayMode, ParamGroup.ModWheel]),
        ("Sampler", [ParamGroup.Sampler]),
        ("Chain", [ParamGroup.MasterFilter, ParamGroup.Distortion, ParamGroup.Eq, ParamGroup.Spin]),
        ("Resochord & Reverb", [ParamGroup.Resochord, ParamGroup.Reverb]),
        ("Output & Post", [ParamGroup.Output, ParamGroup.Scope, ParamGroup.Post]),
        ("Transport & Global", [ParamGroup.Transport, ParamGroup.Global]),
        ("Mod Matrix", [ParamGroup.ModMatrix]),
    ];

    private readonly NoumenonEngine engine;
    private readonly TabControl tabs = new() { Dock = DockStyle.Fill };
    private readonly Dictionary<TabPage, ParamGroup[]> groupsByPage = new();
    private readonly Dictionary<TabPage, List<ParameterRow>> rowsByPage = new();
    private readonly TabPage samplesPage;
    private readonly TabPage monitorPage;
    private readonly SamplesPanel samples;
    private readonly MonitorPanel monitor;

    public DeveloperPanel(NoumenonEngine engine, MidiHost midi, KeyboardMidi keyboard)
    {
        this.engine = engine;
        foreach (var (title, groups) in Pages)
        {
            var page = new TabPage(title);
            groupsByPage[page] = groups;
            tabs.TabPages.Add(page);
        }

        samples = new SamplesPanel(engine) { Dock = DockStyle.Fill };
        samplesPage = new TabPage("Samples");
        samplesPage.Controls.Add(samples);
        tabs.TabPages.Add(samplesPage);

        monitor = new MonitorPanel(engine, midi, keyboard) { Dock = DockStyle.Fill };
        monitorPage = new TabPage("Monitor");
        monitorPage.Controls.Add(monitor);
        tabs.TabPages.Add(monitorPage);

        tabs.Selected += (_, e) =>
        {
            if (e.TabPage is not null)
                EnsureBuilt(e.TabPage);
        };
        Controls.Add(tabs);
        EnsureBuilt(tabs.TabPages[0]);
    }

    public MonitorPanel Monitor => monitor;

    public int PageCount => tabs.TabPages.Count;

    public void Poll()
    {
        var page = tabs.SelectedTab;
        if (page is null)
            return;

        if (page == samplesPage)
            samples.Poll();
        else if (page == monitorPage)
            monitor.Poll();
        else if (rowsByPage.TryGetValue(page, out var rows))
        {
            foreach (var row in rows)
                row.Poll();
        }
    }

    public void ShowPage(int index)
    {
        if (index >= 0 && index < tabs.TabPages.Count)
            tabs.SelectedIndex = index;
    }

    private void EnsureBuilt(TabPage page)
    {
        if (rowsByPage.ContainsKey(page) || !groupsByPage.TryGetValue(page, out var groups))
            return;

        var rows = new List<ParameterRow>();
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        flow.SuspendLayout();
        var headerFont = new Font(Font, FontStyle.Bold);
        foreach (var group in groups)
        {
            var members = ParameterTable.All.Where(info => info.Group == group).ToList();
            flow.Controls.Add(new Label { Text = $"{Title(group)}  ({members.Count})", Font = headerFont, Width = ParameterRow.RowWidth, Height = 20, Margin = new Padding(4, 10, 0, 2), TextAlign = ContentAlignment.MiddleLeft });
            foreach (var info in members)
            {
                var row = new ParameterRow(engine, info);
                rows.Add(row);
                flow.Controls.Add(row);
            }
        }

        flow.ResumeLayout();
        page.Controls.Add(flow);
        rowsByPage[page] = rows;
    }

    private static string Title(ParamGroup group) => group switch
    {
        ParamGroup.OscillatorA => "Oscillators, section A",
        ParamGroup.OscillatorB => "Oscillators, section B",
        ParamGroup.SectionA => "Section A filter, level and routing",
        ParamGroup.SectionB => "Section B filter, level and routing",
        ParamGroup.Pitch => "P1 / P2 pitch faders",
        ParamGroup.Mix => "A+B / A×B combine",
        ParamGroup.Sampler => "Sampler and live input",
        ParamGroup.MasterFilter => "Master filter",
        ParamGroup.Distortion => "Distortion",
        ParamGroup.Eq => "EQ",
        ParamGroup.Spin => "Spin",
        ParamGroup.Resochord => "Resochord",
        ParamGroup.Reverb => "Reverb",
        ParamGroup.Output => "Output",
        ParamGroup.Scope => "XY scope",
        ParamGroup.Post => "Post section",
        ParamGroup.Transport => "Transport, randomizer, mute",
        ParamGroup.PlayMode => "Play mode (gate envelope)",
        ParamGroup.ModWheel => "Mod wheel box",
        ParamGroup.Global => "Global: scale, tuning, host sync, engine rate",
        ParamGroup.ModMatrix => "LFOs, envelope follower, macros, matrix slots",
        _ => group.ToString(),
    };
}
