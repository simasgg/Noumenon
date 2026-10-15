using NPlug;
using Noumenon.Engine.Parameters;

namespace Noumenon.Vst3.Parameters;

/// <summary>
/// One table parameter as a host sees it. The host speaks normalized 0–1; every conversion goes
/// through the <see cref="ParamInfo"/> row, so a logarithmic cutoff automates evenly in octaves,
/// a choice is a list the host can show as a menu, and the text a host displays for any value
/// (an automation lane's tooltip as well as the current value) is the table's own formatting with
/// the unit inside it — which is why the separate unit string stays empty. Typed text comes back
/// through <see cref="ParameterText"/>; text it cannot read is refused as an invalid argument.
/// </summary>
internal sealed class NoumenonParameter : AudioParameter
{
    public NoumenonParameter(ParamInfo info)
        : base(info.Name, units: "", id: PluginIdentity.ToVst3Id(info.Id), shortTitle: info.ShortName, stepCount: info.StepCount, defaultNormalizedValue: info.ToNormalized(info.Default), flags: FlagsOf(info))
    {
        Info = info;
    }

    public ParamInfo Info { get; }

    public override string ToString(double valueNormalized) => Info.Format(Info.FromNormalized((float)valueNormalized));

    public override double FromString(string plainValueAsString) =>
        ParameterText.TryParse(Info, plainValueAsString, out var value) ? Info.ToNormalized(value) : throw new ArgumentException($"'{plainValueAsString}' is not a value of {Info.Name}.", nameof(plainValueAsString));

    public override double ToPlain(double normalizedValue) => Info.FromNormalized((float)normalizedValue);

    public override double ToNormalized(double plainValue) => Info.ToNormalized((float)plainValue);

    private static AudioParameterFlags FlagsOf(ParamInfo info) => info.Kind == ParamKind.Choice ? AudioParameterFlags.CanAutomate | AudioParameterFlags.IsList : AudioParameterFlags.CanAutomate;
}
