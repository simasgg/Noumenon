using System.Runtime.CompilerServices;
using NPlug;
using Noumenon.Vst3.Processing;

namespace Noumenon.Vst3;

/// <summary>
/// The module's plugin factory: the instrument and the effect as two processor classes
/// (subcategories Instrument|Synth and Fx — the effect must be an Fx, or FL Studio routes no audio
/// into it) sharing one controller class. NPlug's native proxy asks the assembly for the factory
/// through the export its build adds, which returns <see cref="AudioPluginFactoryExporter.Instance"/>;
/// the module initializer sets that the moment the runtime loads this assembly.
/// </summary>
internal static class NoumenonFactory
{
    public static AudioPluginFactory Create()
    {
        var factory = new AudioPluginFactory(new AudioPluginFactoryInfo(PluginIdentity.Vendor, PluginIdentity.Url, PluginIdentity.Email));
        factory.RegisterPlugin<InstrumentProcessor>(new AudioProcessorClassInfo(PluginIdentity.InstrumentClassId, PluginIdentity.InstrumentName, AudioProcessorCategory.InstrumentSynthesizer) { Vendor = PluginIdentity.Vendor, Version = PluginIdentity.Version });
        factory.RegisterPlugin<EffectProcessor>(new AudioProcessorClassInfo(PluginIdentity.EffectClassId, PluginIdentity.EffectName, AudioProcessorCategory.Effect) { Vendor = PluginIdentity.Vendor, Version = PluginIdentity.Version });
        factory.RegisterPlugin<NoumenonController>(new AudioControllerClassInfo(PluginIdentity.ControllerClassId, PluginIdentity.ControllerName) { Vendor = PluginIdentity.Vendor, Version = PluginIdentity.Version });

        return factory;
    }

    [ModuleInitializer]
    internal static void Export() => AudioPluginFactoryExporter.Instance = Create();
}
