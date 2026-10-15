using System.Collections.Concurrent;
using Noumenon.Engine.Hosting;
using Noumenon.Engine.Parameters;
using Noumenon.Vst3.Parameters;

namespace Noumenon.Vst3;

/// <summary>
/// One plugin instance's session, owned by its processor: the engine and everything
/// <see cref="PluginSessionBase"/> keeps around it, plus the <see cref="HostValues"/> both
/// directions of parameter traffic consult, and the rate and block size the host set up, for the
/// editor's status line. VST3 splits a plugin into a processor and a controller that the host
/// creates separately and only introduces to each other through messages, so the processor files
/// its session here under a process-unique key and sends the key over; the controller looks the
/// session up and from then on drives the editor on the same engine. A snapshot applied to the
/// engine becomes the agreed host value of every parameter at once — the host reads the
/// controller's values back by itself after a state load, and the controller announces a panel
/// preset load with a restart.
/// </summary>
internal sealed class PluginSession : PluginSessionBase
{
    private static readonly ConcurrentDictionary<long, PluginSession> Open = new();
    private static long lastKey;

    public PluginSession(bool isEffect)
        : base(FolderOf(typeof(PluginSession)))
    {
        IsEffect = isEffect;
        Key = Interlocked.Increment(ref lastKey);
    }

    public long Key { get; }

    public bool IsEffect { get; }

    public string Name => IsEffect ? PluginIdentity.EffectName : PluginIdentity.InstrumentName;

    public HostValues HostValues { get; } = new();

    public double SampleRate { get; set; }

    public int MaxBlockSize { get; set; }

    public static PluginSession? Find(long key) => Open.TryGetValue(key, out var session) ? session : null;

    public void Register() => Open[Key] = this;

    public void Unregister() => Open.TryRemove(Key, out _);

    protected override void OnSnapshotApplied()
    {
        var bank = Engine.Parameters;
        foreach (var info in ParameterTable.All)
            HostValues.Agree(info, bank.Get(info.Id));
    }
}
