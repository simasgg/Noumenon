using Noumenon.Dsp.Sampler;

namespace Noumenon.Engine.Samples;

/// <summary>
/// One entry of the <see cref="SampleMap"/>: the decoded audio plus the per-slot settings the plan
/// gives every sample (gain and root note; the loop range is a snapshot parameter, not a slot
/// property). Immutable — changing a setting replaces the slot, so the audio thread always sees a
/// consistent pair of data and settings.
/// </summary>
/// <param name="Key">The map key (0–127) the Sample Select parameter addresses.</param>
/// <param name="Path">Where the file came from, or null for generated data.</param>
/// <param name="Gain">Linear gain applied on top of the Amp fader and Master knob.</param>
/// <param name="RootNote">The MIDI note at which the sample plays unchanged; 60 leaves the pitch alone.</param>
public sealed record SampleSlot(int Key, string Name, string? Path, SampleData Data, float Gain, int RootNote);
