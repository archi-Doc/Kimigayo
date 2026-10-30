// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// SPEC 8.11.1: a bound violation is reported at the first violating effect in the implementation's own body; its Reason
// names the violation and the bound, its Note tells a definite violation from an unknown effect counted as a conflict, and its
// related locations give the effect, the conformance and the bound.
public class EffectBoundReportTest
{
    private const string Sink = "contract Sink\n    func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        effect confined\n";

    // The mutable static only the logging implementation reads (DECISIONS R5-M145).
    private const string Metrics = "group Metrics\n    public var puts: isize = 0\n    public func record() => Metrics.puts += 1\n";

    private const string Source = "contract Source\n    associate Item\n    func take(self: uniq/Self) -> Option<Self.Item>\n        effect confined\n";

    private const string Main = "public func main() => ()\n";

    [Theory]
    [InlineData(Sink + Metrics + "struct LoggingSink\n    Self is Sink\n    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        Metrics.record()\n        return .Ok(())\n", "Metrics.record()", "a mutable static access, which confined excludes", "this effect violates it", new[] { "bound", "conformance", "effect" }, "var puts: isize = 0")]
    [InlineData(Sink + "struct ConsoleSink\n    Self is Sink\n    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        Console.writeLine(\"put\")\n        return .Ok(())\n", "Console.writeLine(\"put\")", "an external operation, which confined excludes", "this effect violates it", new[] { "bound", "conformance" }, "")]
    [InlineData(Sink + "struct DeviceSink\n    Self is Sink\n    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        unsafe\n            let register = 0x40000000@raw/i32\n            *register = value\n        return .Ok(())\n", "0x40000000@raw/i32", "a pointer made from an integer, which confined excludes", "this effect violates it", new[] { "bound", "conformance" }, "")]
    public void ReportsTheViolatingEffect(string declarations, string text, string reason, string note, string[] roles, string storage)
    {
        var result = DiagnosticCorpus.Check(declarations + Main);
        var error = Assert.Single(result.Diagnostics, static x => x.Code == nameof(DiagnosticCode.IncompatibleContractImplementation_Kd));
        Assert.Equal(reason, error.Label);
        Assert.Contains(note, error.Note);
        Assert.Equal(roles, error.Related!.Select(static x => x.Role).ToArray());

        // A mutable static beside it is also the collector's StaticStorage record at its declaration, at check on every pipeline (DECISIONS
        // R5-M82, R5-M156); P42 reverts it.
        var others = result.Diagnostics.Where(static x => x.Code != nameof(DiagnosticCode.IncompatibleContractImplementation_Kd));
        Assert.Equal(
            storage.Length == 0 ? [] : [(nameof(DiagnosticCode.Unsupported_Kd), declarations.IndexOf(storage, StringComparison.Ordinal), storage.Length)],
            others.Select(static x => (x.Code, x.Span!.Value.Start, x.Span.Value.Length)));

        var c = MinimalEmissionTest.Analyze(declarations + Main);
        c.Binding.ReportDiagnostics();
        Assert.Equal(text, Assert.Single(TestDiagnostics.Of(c), static x => x.Severity == Kimi.Diagnostics.DiagnosticSeverity.Error).Text);
    }

    // The standard bounds are library declarations: their violations offer no change to them.
    [Fact]
    public void LibraryBoundsOfferNoWeakening()
    {
        var result = DiagnosticCorpus.Check("struct Writer\n    Self is BufferWriter\n    public func reserve(self: uniq/Self, minimum: isize) -> Result<WriteWindow, BufferFull>\n        Console.writeLine(\"reserve\")\n        return .Err(BufferFull.init())\n" + Main);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal("an external operation, which confined excludes", error.Label);
    }

    // SPEC 8.11.1: semantic inspection exposes the bounds each Contract declares.
    [Fact]
    public void ContractsExposeTheirDeclaredBounds()
    {
        var c = MinimalEmissionTest.Analyze(Source + "contract StableSource: Source\n    func size(self: ref/Self) -> isize\n        effect confined\n" + Main);
        var contracts = c.Kotonoha.RootKoto.NestedContainers.OfType<ContractKoto>().ToDictionary(static x => x.SymbolOf()!.Name, static x => x.SymbolOf()!.Contract!);
        Assert.Single(contracts["Source"].EffectBounds);
        Assert.Equal("size", Assert.Single(contracts["StableSource"].EffectBounds).Requirement.Name);
    }
}
