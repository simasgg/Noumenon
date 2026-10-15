namespace Noumenon.Vst3;

/// <summary>
/// The two messages the processor and the controller exchange through the host's connection: the
/// processor announces its session key as soon as it is connected, and the controller says hello
/// when it is connected, which makes the processor announce the key again — a host may connect
/// the two in either order, or deliver a message before the other side is listening.
/// </summary>
internal static class SessionMessages
{
    public const string Session = "Noumenon.Session";
    public const string Hello = "Noumenon.Hello";
    public const string KeyAttribute = "Key";

    /// <summary>Every string the two sides hand to NPlug as a message or attribute ID (see <see cref="NPlugWorkarounds"/>).</summary>
    public static readonly string[] Ids = [Session, Hello, KeyAttribute];
}
