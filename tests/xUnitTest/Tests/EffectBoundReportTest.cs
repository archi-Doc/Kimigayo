// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// SPEC 8.11.1: a bound violation refuses the implementation. Since the MIR decides acceptance (H5, DECISIONS R5-M185), A4 reports it at
// the conformance's `Self is C` clause with no Label, Note or related location (R6-check H3b T41). Binding's record was at the first
// violating effect in the implementation's own body, its Reason named the violation and the bound, its Note told a definite violation
// from an unknown effect counted as a conflict, and its related locations gave the effect, the conformance and the bound; those facts
// return when A4 publishes them (H0a 4.1's Effect* rows).
public class EffectBoundReportTest
{
    private const string Sink = "contract Sink\n    func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        effect confined\n";

    // The mutable static only the logging implementation reads (DECISIONS R5-M145).
    private const string Metrics = "group Metrics\n    public var puts: isize = 0\n    public func record() => Metrics.puts += 1\n";

    private const string Source = "contract Source\n    associate Item\n    func take(self: uniq/Self) -> Option<Self.Item>\n        effect confined\n";

    private const string Main = "public func main() => ()\n";

    [Theory]
    [InlineData(Sink + Metrics + "struct LoggingSink\n    Self is Sink\n    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        Metrics.record()\n        return .Ok(())\n", "var puts: isize = 0")]
    [InlineData(Sink + "struct ConsoleSink\n    Self is Sink\n    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        Console.writeLine(\"put\")\n        return .Ok(())\n", "")]
    [InlineData(Sink + "struct DeviceSink\n    Self is Sink\n    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        unsafe\n            let register = 0x40000000@raw/i32\n            *register = value\n        return .Ok(())\n", "")]
    public void ReportsTheViolatingEffect(string declarations, string storage)
    {
        var result = DiagnosticCorpus.Check(declarations + Main);
        var error = Assert.Single(result.Diagnostics, static x => x.Code == nameof(DiagnosticCode.IncompatibleContractImplementation_Kd));
        Assert.Equal(MirTargetSide.At("Self is Sink").Resolve(declarations), error.Span!.Value);
        Assert.Null(error.Label);
        Assert.Null(error.Note);
        Assert.Null(error.Related);

        // A mutable static beside it is also the collector's StaticStorage record at its declaration, at check (DECISIONS
        // R5-M82, R5-M156); P42 reverts it.
        var others = result.Diagnostics.Where(static x => x.Code != nameof(DiagnosticCode.IncompatibleContractImplementation_Kd));
        Assert.Equal(
            storage.Length == 0 ? [] : [(nameof(DiagnosticCode.Unsupported_Kd), declarations.IndexOf(storage, StringComparison.Ordinal), storage.Length)],
            others.Select(static x => (x.Code, x.Span!.Value.Start, x.Span.Value.Length)));

        (DiagnosticCode, SyntaxLocation)[] storages = storage.Length == 0 ? [] : [(DiagnosticCode.Unsupported_Kd, MirTargetSide.At(storage))];
        EffectBoundImplementationTest.AssertBound(MinimalEmissionTest.Analyze(declarations + Main), valid: false, "Self is Sink", storages);
    }

    // The standard bounds are library declarations: their violations offer no change to them.
    [Fact]
    public void LibraryBoundsOfferNoWeakening()
    {
        const string Source = "struct Writer\n    Self is BufferWriter\n    public func reserve(self: uniq/Self, minimum: isize) -> Result<WriteWindow, BufferFull>\n        Console.writeLine(\"reserve\")\n        return .Err(BufferFull.init())\n" + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(Source).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.IncompatibleContractImplementation_Kd), error.Code);
        Assert.Equal(MirTargetSide.At("Self is BufferWriter").Resolve(Source), error.Span!.Value);
        Assert.Null(error.Label);
        Assert.Null(error.Note);
        Assert.Null(error.Related);
        Assert.Null(error.Repairs);
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
