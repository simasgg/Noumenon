using System.Text;
using NPlug.Validator;
using Xunit.Abstractions;

namespace Noumenon.Vst3.Tests;

/// <summary>
/// Steinberg's VST3 validator — the SDK's own conformance suite, shipped by NPlug.Validator with a
/// native proxy that hands it this assembly's factory — run over the module in process: both
/// identities through bus scans, parameter scans, MIDI mapping, units, state transitions,
/// processing at every block size and sample rate it tries, automation, flushes, silence and the
/// tail. The whole report goes to the test output.
/// </summary>
public class ValidatorTests(ITestOutputHelper output)
{
    [Fact]
    public void The_Steinberg_Validator_Passes_Both_Identities()
    {
        var log = new StringWriter(new StringBuilder());
        var errors = new StringWriter(new StringBuilder());

        var attachedBefore = NoumenonController.Attachments;
        var passed = AudioPluginValidator.Validate(() => NoumenonFactory.Create().Export(), log, errors);

        var report = log.ToString();
        output.WriteLine(report);
        output.WriteLine(errors.ToString());
        Assert.True(passed, "the validator reported failures; see the test output");
        Assert.Contains("0 tests failed", report);
        Assert.Contains("Noumenon FX", report);
        Assert.DoesNotContain("Failed to connect", errors.ToString());
        Assert.True(NoumenonController.Attachments > attachedBefore, "the host's connection should hand each controller its processor's session");
    }
}
