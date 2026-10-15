namespace Noumenon.Engine.Modulation;

/// <summary>What the envelope follower listens to; the order is the parameter table's list.</summary>
public enum FollowerSource
{
    /// <summary>The sampler after Samp↔In, before Amp: the sample, the live input or a blend (MF's behaviour, the default).</summary>
    SampleAndInput,

    /// <summary>The sample alone, whatever Samp↔In does.</summary>
    Sample,

    /// <summary>The live input (the effect's input bus, the standalone's capture), heard or not.</summary>
    Input,

    /// <summary>The sidechain bus a host routes in, separate from the main input.</summary>
    Sidechain,

    /// <summary>The two oscillator sections after the combine stage.</summary>
    Sections,

    /// <summary>The instrument's own output, one sample late: the patch follows itself.</summary>
    Output,
}
