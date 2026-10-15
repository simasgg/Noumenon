using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text;
using NPlug;

namespace Noumenon.Vst3;

/// <summary>
/// Works around two bugs in NPlug 0.5.0.380's message support that its public API cannot avoid;
/// both sit on the path the processor and the controller use to find each other.
///
/// Sending: NPlug turns a managed string into the native one a host needs — every message ID and
/// attribute ID — through a per-host cache whose lookup inserts the key and then adds it a second
/// time, which throws (reported to the host as an invalid argument, so its <c>connect</c> of the
/// two fails), and it would never have copied the characters anyway. Seeding that cache with
/// correctly encoded strings for the IDs this plugin uses lets the lookup succeed before it reaches
/// the faulty branch; NPlug frees them with the rest when it terminates the component.
///
/// Disposing: <see cref="AudioMessage.Dispose"/> releases the message and then the attribute list
/// it borrowed from it — <c>IMessage::getAttributes</c> hands out no reference — so the list is
/// released after the message has already freed it, a use-after-free that crashes a host now and
/// then. A sent message is therefore never disposed through NPlug: <see cref="Release"/> releases
/// the native message exactly once.
///
/// Both reach NPlug internals (a private cache, an internal pointer) by reflection and fall back to
/// doing nothing if a later NPlug changes them — the connection then fails or a message leaks, which
/// the tests' validator run shows. The third NPlug 0.5 quirk, parameters starting at 0 instead of
/// their defaults, is handled in <see cref="Parameters.NoumenonModel.ResetValues"/>.
/// </summary>
internal static class NPlugWorkarounds
{
    private const string StringCacheField = "_managedToNativeUTF8";
    private const string NativeMessageField = "NativeContext";

    private static readonly NativeMessageReader? ReadNativeMessage = CreateNativeMessageReader();

    private delegate nint NativeMessageReader(ref AudioMessage message);

    public static unsafe bool SeedStrings(AudioHostApplication host, params string[] ids)
    {
        if (host.GetType().GetField(StringCacheField, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(host) is not Dictionary<string, IntPtr> cache)
            return false;

        lock (cache)
        {
            foreach (var id in ids)
            {
                if (cache.TryGetValue(id, out var existing) && existing != IntPtr.Zero)
                    continue;

                var length = Encoding.UTF8.GetByteCount(id);
                var native = (byte*)NativeMemory.Alloc((nuint)(length + 1));
                Encoding.UTF8.GetBytes(id, new Span<byte>(native, length));
                native[length] = 0;
                cache[id] = (IntPtr)native;
            }
        }

        return true;
    }

    /// <summary>Releases a message this plugin created, once (IUnknown's release, the third vtable slot); use it instead of <see cref="AudioMessage.Dispose"/>.</summary>
    public static unsafe void Release(ref AudioMessage message)
    {
        var native = ReadNativeMessage?.Invoke(ref message) ?? 0;
        if (native == 0)
            return;

        var vtable = *(nint**)native;
        ((delegate* unmanaged<nint, uint>)vtable[2])(native);
    }

    private static NativeMessageReader? CreateNativeMessageReader()
    {
        var field = typeof(AudioMessage).GetField(NativeMessageField, BindingFlags.NonPublic | BindingFlags.Instance);
        if (field is null || field.FieldType != typeof(IntPtr))
            return null;

        var method = new DynamicMethod("ReadNativeMessage", typeof(nint), [typeof(AudioMessage).MakeByRefType()], typeof(NPlugWorkarounds).Module, skipVisibility: true);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, field);
        il.Emit(OpCodes.Ret);

        return method.CreateDelegate<NativeMessageReader>();
    }
}
