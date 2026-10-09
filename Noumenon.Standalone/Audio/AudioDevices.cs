using System.Runtime.InteropServices;
using NAudio;
using NAudio.CoreAudioApi;
using NAudio.Midi;
using NAudio.Wave;

namespace Noumenon.Standalone.Audio;

internal sealed record DeviceEntry(string Id, string Name);

/// <summary>
/// Enumerates what the host can open — WASAPI render and capture endpoints by their stable IDs,
/// ASIO drivers by name, MIDI inputs by product name — and resolves a saved choice back to a
/// device, falling back to the system default where there is one. Enumeration talks to COM and
/// winmm, so it belongs on the UI thread, never in a callback; a machine without devices yields
/// empty lists, not exceptions.
/// </summary>
internal static class AudioDevices
{
    public static IReadOnlyList<DeviceEntry> WasapiOutputs() => Endpoints(DataFlow.Render);

    public static IReadOnlyList<DeviceEntry> WasapiInputs() => Endpoints(DataFlow.Capture);

    public static IReadOnlyList<DeviceEntry> AsioDrivers()
    {
        try
        {
            return AsioOut.GetDriverNames().Select(name => new DeviceEntry(name, name)).ToArray();
        }
        catch (Exception ex) when (IsDeviceFailure(ex))
        {
            return [];
        }
    }

    public static IReadOnlyList<DeviceEntry> MidiInputs()
    {
        var list = new List<DeviceEntry>();
        try
        {
            for (var i = 0; i < MidiIn.NumberOfDevices; i++)
            {
                var name = MidiIn.DeviceInfo(i).ProductName;
                list.Add(new DeviceEntry(name, name));
            }
        }
        catch (Exception ex) when (IsDeviceFailure(ex))
        {
        }

        return list;
    }

    public static int MidiIndex(string? productName)
    {
        if (productName is null)
            return -1;

        try
        {
            for (var i = 0; i < MidiIn.NumberOfDevices; i++)
            {
                if (string.Equals(MidiIn.DeviceInfo(i).ProductName, productName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
        }
        catch (Exception ex) when (IsDeviceFailure(ex))
        {
        }

        return -1;
    }

    public static MMDevice? FindEndpoint(DataFlow flow, string? id, bool fallbackToDefault)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            if (id is not null)
            {
                try
                {
                    var device = enumerator.GetDevice(id);
                    if (device.State == DeviceState.Active && device.DataFlow == flow)
                        return device;
                }
                catch (COMException)
                {
                }
            }

            if (!fallbackToDefault || !enumerator.HasDefaultAudioEndpoint(flow, Role.Multimedia))
                return null;

            return enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia);
        }
        catch (Exception ex) when (IsDeviceFailure(ex))
        {
            return null;
        }
    }

    public static bool IsDeviceFailure(Exception ex) =>
        ex is COMException or MmException or InvalidOperationException or NotSupportedException or ArgumentException or DllNotFoundException or EntryPointNotFoundException or NotImplementedException;

    private static IReadOnlyList<DeviceEntry> Endpoints(DataFlow flow)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active).Select(device => new DeviceEntry(device.ID, device.FriendlyName)).ToArray();
        }
        catch (Exception ex) when (IsDeviceFailure(ex))
        {
            return [];
        }
    }
}
