using System.Text;
using System.Text.Json;
using NPlug;
using NPlug.IO;
using Noumenon.Engine.Parameters;
using Noumenon.Engine.Presets;
using Noumenon.Vst3.Editor;
using Noumenon.Vst3.Parameters;

namespace Noumenon.Vst3;

/// <summary>
/// The VST3 edit controller, one class for both identities. It publishes the parameter list
/// (<see cref="NoumenonModel"/>), maps every MIDI controller, aftertouch and pitch bend onto the
/// hidden parameters the processor turns back into MIDI, reads the processor's state into the
/// values the host displays, and keeps its own small state — the panel scale — with the project.
/// Once the host has connected it to its processor it holds that instance's session, which is
/// what the editor runs on.
///
/// The bank is the truth and the panel writes it directly, so the controller's job while the
/// editor is open is to tell the host: the view's 33 ms poll calls <see cref="PushChanges"/>,
/// which sends every bank value that no longer means what the host last agreed to (see
/// <see cref="HostValues"/>) as a begin/perform/end edit — kept open for the length of a panel
/// gesture so a host records it as one touch. A snapshot loaded from the panel, or the randomizer,
/// changes everything at once and is announced with a parameter restart instead, without edits.
/// A latency change (Engine Rate) asks the host to re-read the processor's latency. All of it
/// runs on the host's UI thread.
/// </summary>
internal sealed class NoumenonController : AudioController<NoumenonModel>
{
    private const uint StateMagic = 0x4E6F7556;   // "NouV"

    private static long attachments;

    private readonly HashSet<int> openEdits = [];
    private PluginSession? session;
    private bool gestureOpen;
    private int reportedLatency = -1;

    public NoumenonController()
    {
        Model.ResetValues();
        var controllers = Model.MidiControllers;
        for (var controller = 0; controller < controllers.Count; controller++)
            SetMidiCCMapping((AudioMidiControllerNumber)controller, controllers[controller]);
    }

    /// <summary>How many controllers in this process have found their processor's session (diagnostics for the tests).</summary>
    public static long Attachments => Interlocked.Read(ref attachments);

    /// <summary>The editor's scale, remembered while it is closed and saved with the project.</summary>
    public float PanelScale { get; set; } = 1f;

    /// <summary>Whether the user picked <see cref="PanelScale"/> on the panel; until then the host's DPI scale chooses it.</summary>
    public bool PanelScaleChosen { get; set; }

    /// <summary>Connects to a session directly; the host's connection does this through <see cref="SessionMessages"/>.</summary>
    public void Attach(PluginSession attached)
    {
        session = attached;
        reportedLatency = attached.Engine.LatencySamples;
        Interlocked.Increment(ref attachments);
    }

    /// <summary>Sends every bank change the host has not agreed to; returns how many parameters were sent.</summary>
    public int PushChanges()
    {
        if (session is null)
            return 0;

        var handler = Handler;
        var bank = session.Engine.Parameters;
        var hostValues = session.HostValues;
        var parameters = Model.TableParameters;
        var now = Environment.TickCount64;
        var pushed = 0;
        foreach (var info in ParameterTable.All)
        {
            if (!hostValues.TryPush(info, bank.Get(info.Id), now, out var normalized))
                continue;

            var parameter = parameters[info.Index];
            parameter.NormalizedValue = normalized;
            pushed++;
            if (handler is null)
                continue;

            var id = parameter.Id;
            if (!gestureOpen)
            {
                handler.BeginEdit(id);
                handler.PerformEdit(id, normalized);
                handler.EndEdit(id);
                continue;
            }

            if (openEdits.Add(id.Value))
                handler.BeginEdit(id);

            handler.PerformEdit(id, normalized);
        }

        return pushed;
    }

    public void GestureStarted() => gestureOpen = true;

    /// <summary>Sends the gesture's last values and closes every edit it opened.</summary>
    public void GestureEnded()
    {
        PushChanges();
        gestureOpen = false;
        if (Handler is { } handler)
        {
            foreach (var id in openEdits)
                handler.EndEdit(id);
        }

        openEdits.Clear();
    }

    /// <summary>
    /// After the panel replaced the whole snapshot (a preset, the randomizer): every value the
    /// host displays follows the bank silently, and the host is asked to re-read them all and told
    /// the project changed.
    /// </summary>
    public void SnapshotChanged()
    {
        if (session is null)
            return;

        var bank = session.Engine.Parameters;
        var parameters = Model.TableParameters;
        foreach (var info in ParameterTable.All)
        {
            var value = bank.Get(info.Id);
            session.HostValues.Agree(info, value);
            parameters[info.Index].NormalizedValue = info.ToNormalized(value);
        }

        if (Handler is not { } handler)
            return;

        handler.RestartComponent(AudioRestartFlags.ParamValuesChanged);
        if (handler.IsAdvancedEditSupported)
            handler.SetDirty(true);
    }

    /// <summary>Asks the host to re-read the latency when Engine Rate has changed it.</summary>
    public void PollLatency()
    {
        if (session is null)
            return;

        var latency = session.Engine.LatencySamples;
        if (latency == reportedLatency)
            return;

        reportedLatency = latency;
        Handler?.RestartComponent(AudioRestartFlags.LatencyChanged);
    }

    protected override bool Initialize(AudioHostApplication host)
    {
        NPlugWorkarounds.SeedStrings(host, SessionMessages.Ids);

        return true;
    }

    protected override IAudioPluginView? CreateView() => session is null ? null : new NoumenonView(this, session);

    protected override void Terminate() => session = null;

    protected override void OnConnect(IAudioConnectionPoint connectionPoint)
    {
        if (Host is not { } host || !host.TryCreateMessage(SessionMessages.Hello, out var message))
            return;

        try
        {
            message.Id = SessionMessages.Hello;
            connectionPoint.Notify(message);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
        }
        finally
        {
            NPlugWorkarounds.Release(ref message);
        }
    }

    protected override void OnMessage(AudioMessage message)
    {
        try
        {
            if (message.Id == SessionMessages.Session && message.Attributes.TryGetInt64(SessionMessages.KeyAttribute, out var key) && PluginSession.Find(key) is { } found)
                Attach(found);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
        }
    }

    /// <summary>The host changed a value or read it back; the processor receives the same change, so nothing else is needed (and the base class would ask for an edit).</summary>
    protected override void OnParameterValueChanged(AudioParameter parameter, bool parameterValueChangedFromHost)
    {
    }

    /// <summary>The processor's state, the snapshot JSON: the values the host displays follow it (a parameter it lacks takes its default).</summary>
    protected override void RestoreComponentState(PortableBinaryReader reader)
    {
        using var buffer = new MemoryStream();
        reader.Stream.CopyTo(buffer);
        if (buffer.Length == 0)
            return;

        PresetState state;
        try
        {
            state = PresetIo.Deserialize(Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length));
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException or InvalidOperationException)
        {
            return;
        }

        var parameters = Model.TableParameters;
        foreach (var info in ParameterTable.All)
        {
            var value = state.Parameters.TryGetValue(info.Id.ToString(), out var stored) ? stored : info.Default;
            parameters[info.Index].NormalizedValue = info.ToNormalized(value);
        }
    }

    protected override void SaveState(PortableBinaryWriter writer)
    {
        writer.WriteUInt32(StateMagic);
        writer.WriteFloat32(PanelScale);
        writer.WriteBool(PanelScaleChosen);
    }

    protected override void RestoreState(PortableBinaryReader reader)
    {
        if (reader.Stream.Length - reader.Stream.Position < 9 || reader.ReadUInt32() != StateMagic)
            return;

        var scale = reader.ReadFloat32();
        var chosen = reader.ReadBool();
        if (scale is < 0.5f or > 4f)
            return;

        PanelScale = scale;
        PanelScaleChosen = chosen;
    }
}
