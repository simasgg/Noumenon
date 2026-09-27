namespace Noumenon.Dsp.Reverb;

/// <summary>
/// One of the Reverb block's rooms: which engine it runs on and the character settings it pins.
/// Size and Damping stay live parameters; everything here is what makes a room sound like itself.
/// </summary>
public sealed record ReverbRoom(string Name, ReverbMode Mode, float PreDelayMs, float Diffusion, float Bass, float ModDepth, float ModRate, bool ModDrift, float HighCut, float LowCut, bool Freeze);

/// <summary>
/// MF's six room types — our own voicings over the two engines. The order is the Reverb Room
/// parameter's choice list; append only. "Endless" is not a room but the Reverb Freeze parameter:
/// a frozen tank closes its input, so a room that started frozen would never hear anything.
/// </summary>
public static class ReverbRooms
{
    public static readonly IReadOnlyList<ReverbRoom> All =
    [
        //   name         engine               pre   diff   bass   mdep   mrate  drift  hicut  locut  freeze
        new("Room",      ReverbMode.Freeverb, 5f,   0.75f, 0.45f, 0.20f, 0.50f, false, 0.70f, 0.10f, false),
        new("Chamber",   ReverbMode.Freeverb, 12f,  0.85f, 0.55f, 0.20f, 0.50f, false, 0.55f, 0.20f, false),
        new("Hall",      ReverbMode.Plate,    25f,  0.80f, 0.60f, 0.25f, 0.45f, false, 0.50f, 0.20f, false),
        new("Cathedral", ReverbMode.Plate,    45f,  0.85f, 0.65f, 0.30f, 0.35f, true,  0.40f, 0.25f, false),
        new("Plate",     ReverbMode.Plate,    0f,   0.75f, 0.50f, 0.20f, 0.50f, false, 1.00f, 0.00f, false),
        new("Cloud",     ReverbMode.Plate,    60f,  1.00f, 0.55f, 0.50f, 0.30f, true,  0.35f, 0.30f, false),
    ];

    public static readonly IReadOnlyList<string> Names = All.Select(r => r.Name).ToArray();

    public static int Count => All.Count;
}
