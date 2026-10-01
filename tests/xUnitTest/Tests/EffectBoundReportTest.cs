// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// SPEC 8.4.10.6: a bound violation is reported at the first violating effect in the implementation's own body; its Reason
// names the violation and the bound, its Note tells a definite violation from an unknown effect counted as a conflict, and its
// related locations give the effect, the conformance and the bound.
public class EffectBoundReportTest
{
    private const string Sink = "contract Sink\n    func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        effect confined\n" +
        "group Metrics\n    public var puts: isize = 0\n    public func record() => Metrics.puts += 1\n";

    private const string Source = "contract Source\n    associate Item\n    func take(self: uniq/Self) -> Option<Self.Item>\n        effect preserves results\n";

    private const string Main = "public func main() => ()\n";

    [Theory]
    [InlineData(Sink + "struct LoggingSink\n    Self is Sink\n    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        Metrics.record()\n        return .Ok(())\n", "Metrics.record()", "a mutable static access, which confined excludes", "this effect violates it", new[] { "bound", "conformance", "effect" })]
    [InlineData(Sink + "struct ConsoleSink\n    Self is Sink\n    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        Console.writeLine(\"put\")\n        return .Ok(())\n", "Console.writeLine(\"put\")", "an external operation, which confined excludes", "this effect violates it", new[] { "bound", "conformance" })]
    [InlineData(Source + "struct Merge<J>\n    J is Iterator\n    Self is Source\n    associate Source.Item is J.Item\n    var left: J\n    var right: J\n    public func take(self: uniq/Self) -> Option<J.Item>\n        match self.left.next()\n            .Some(let item) => return .Some(item@move)\n            .None => return self.right.next()\n", "self.left.next()", "a requirement call with unknown effects, which preserves results excludes", "another Field names the stored Type", new[] { "bound", "conformance" })]
    public void ReportsTheViolatingEffect(string declarations, string text, string reason, string note, string[] roles)
    {
        var result = DiagnosticCorpus.Check(declarations + Main);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.IncompatibleContractImplementation_Kd), error.Code);
        Assert.Equal(reason, error.Label);
        Assert.Contains(note, error.Note);
        Assert.Contains("declare no bound", error.Advice);
        Assert.Equal(roles, error.Related!.Select(static x => x.Role).ToArray());

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
        Assert.DoesNotContain("declare no bound", error.Advice);
    }

    // SPEC 8.4.10.6: semantic inspection exposes the bounds each Contract declares.
    [Fact]
    public void ContractsExposeTheirDeclaredBounds()
    {
        var c = MinimalEmissionTest.Analyze(Source + "contract StableSource: Source\n    func size(self: ref/Self) -> isize\n        effect confined\n" + Main);
        var contracts = c.Kotonoha.RootKoto.NestedContainers.OfType<ContractKoto>().ToDictionary(static x => x.BoundSymbol!.Name, static x => x.BoundSymbol!.Contract!);
        Assert.Equal(EffectBoundKind.PreservesResults, Assert.Single(contracts["Source"].EffectBounds).Bound);
        var own = Assert.Single(contracts["StableSource"].EffectBounds);
        Assert.Equal(("size", EffectBoundKind.Confined), (own.Requirement.Name, own.Bound));
    }
}
