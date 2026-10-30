// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class VirtualEffectObligationTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false, "State.count", true)]
    [InlineData(true, "State.count", true)]
    [InlineData(false, "1", false)]
    [InlineData(true, "1", false)]
    public void UnusedOriginalAndOverrideBodiesMustSatisfyTheSlotBound(bool derived, string body, bool fails)
    {
        var source = "group State\n    public var count: i32 = 0\nopen struct Base\n    public virtual func read(self: objref/Self) -> i32\n        effect confined\n        return " + (derived ? "1" : body) + "\n" + (derived ? "struct Derived : Base\n    override func read(self: objref/Self) -> i32 => " + body + "\n" : string.Empty) + "()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Equal(fails, c.Binding.Issues.Any(x => x.Code == DiagnosticCode.UnsatisfiedEffectBound_Kd));
        Assert.False(c.Generate(TextWriter.Null));
        if (!fails)
        {
            // The unrelated mutable group storage is still outside generation (P42): the collector's StaticStorage record at check, on every
            // pipeline (DECISIONS R5-M82, R5-M156); P42 reverts it.
            Assert.Equal("Unsupported_Kd public var count: i32 = 0", MinimalEmissionTest.Problems(c));
        }
    }

    // SPEC 8.11.3: an invalid item in the slot's leading region supplies no bound; the slot's guarantee is its valid clause
    // alone, which the body's Console output then violates (PLAN G219, v2).
    [Theory]
    [InlineData("effect Runner.run confined\n", "InvalidEffectBound_Kd", "effect Runner.run confined", false)]
    [InlineData("effect Runner.run confined\n        effect confined\n", "InvalidEffectBound_Kd", "effect Runner.run confined", true)]
    [InlineData("effect bogus\n        effect confined\n", "ExpectedSyntax_Kd", "bogus", true)]
    public void AnInvalidSlotItemSuppliesNoBound(string items, string code, string at, bool bounded)
    {
        var source = "contract Runner\n    func run(self: ref/Self) -> i32\n" +
            "open struct Base\n    public virtual func score(self: objref/Self) -> i32\n        " + items +
            "        Console.writeLine(\"x\")\n        return 1\npublic func main() => ()\n";
        var records = DiagnosticCorpus.Check(source).Diagnostics;
        var item = Assert.Single(records, x => x.Code == code);
        Assert.Equal(source.IndexOf(at, StringComparison.Ordinal), item.Span!.Value.Start);
        Assert.Equal(bounded ? 2 : 1, records.Length);
        if (bounded)
        {
            var violation = Assert.Single(records, static x => x.Code == "UnsatisfiedEffectBound_Kd");
            var bound = Assert.Single(violation.Related!, static x => x.Role == "bound");
            Assert.Equal(source.IndexOf("effect confined", StringComparison.Ordinal), bound.Span!.Value.Start);
        }
    }

    [Fact]
    public void EachImplementationIsCheckedIndependently()
    {
        const string Source = "group State\n    public var count: i32 = 0\nopen struct Base\n    public virtual func read(self: objref/Self) -> i32\n        effect confined\n        return State.count\nstruct Derived : Base\n    override func read(self: objref/Self) -> i32 => State.count\n()";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.Equal(2, c.Binding.Issues.Count(x => x.Code == DiagnosticCode.UnsatisfiedEffectBound_Kd));
    }

    [Theory]
    [InlineData("self.read()", false)]
    [InlineData("self.other()", true)]
    public void BodyVerificationUsesPublicGuaranteesForRecursiveCalls(string body, bool fails)
    {
        var source = "open struct Base\n    public virtual func other(self: objref/Self) -> i32 => 1\n    public virtual func read(self: objref/Self) -> i32\n        effect confined\n        return " + body + "\n()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Equal(fails, c.Binding.Issues.Any(x => x.Code == DiagnosticCode.UnsatisfiedEffectBound_Kd));
    }

    [Fact]
    public void FailedCalleeResolutionRemainsTheCause()
    {
        var c = MinimalEmissionTest.Analyze("open struct Base\n    public virtual func read(self: objref/Self) -> i32\n        effect confined\n        return absent()\nlet independent = missing");
        c.Binding.ReportDiagnostics();
        Assert.DoesNotContain(TestDiagnostics.Of(c), x => x.Code == "UnsatisfiedEffectBound_Kd");
        Assert.Contains(TestDiagnostics.Of(c), x => x.Code == "UnresolvedBinding_Kd" && x.Text == "absent");
        Assert.Contains(TestDiagnostics.Of(c), x => x.Code == "UnresolvedBinding_Kd" && x.Text == "missing");
    }

    [Fact]
    public void DefaultEvaluationBelongsToTheCallRatherThanTheImplementationBody()
    {
        const string Source = "group State\n    public var count: i32 = 0\nopen struct Base\n    public virtual func read(self: objref/Self, n: i32 = State.count) -> i32\n        effect confined\n        return n\nstruct Derived : Base\n    override func read(self: objref/Self, n: i32) -> i32 => n\ncontract Reader\n    func run(self: ref/Self, value: objref/Base) -> i32\n        effect confined\nstruct Probe\n    Self is Reader\n    public func run(self: ref/Self, value: objref/Base) -> i32 => value.read()\n()";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code == DiagnosticCode.UnsatisfiedEffectBound_Kd);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
    }

    [Fact]
    public void EditingTheBodyRevokesItsFailureRecord()
    {
        const string Source = "group State\n    public var count: i32 = 0\nopen struct Base\n    public virtual func read(self: objref/Self) -> i32\n        effect confined\n        return 1\nstruct Derived : Base\n    override func read(self: objref/Self) -> i32 => State.count\n()";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.UnsatisfiedEffectBound_Kd);
        var function = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.IsOverride);
        var donor = MinimalEmissionTest.Analyze(Source.Replace("=> State.count", "=> 2", StringComparison.Ordinal));
        var replacement = Assert.Single(KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.IsOverride);
        var body = ParseTestHelper.Detach(replacement.ExpressionBody!, new CodeBlockKoto(function.CodeContext));
        Assert.True(KotoHelper.Replace(function, function.ExpressionBody!, body));
        c.Bind();
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code == DiagnosticCode.UnsatisfiedEffectBound_Kd);
    }

    [Fact]
    public void TransitiveFailureRelatesTheOriginalSlotAndItsBoundInCliAndLsp()
    {
        const string Source = "group State\n    public var count: i32 = 0\n    public func inspect() -> i32 => State.count\nopen struct Base\n    public virtual func read(self: objref/Self) -> i32\n        effect confined\n        return 1\nstruct Derived : Base\n    override func read(self: objref/Self) -> i32 => State.inspect()\nlet independent = missing";
        var path = Path.GetFullPath("virtual-effect-obligation.kimi");
        var c = MinimalEmissionTest.Analyze(Source, path);
        c.Binding.ReportDiagnostics();
        var shown = Assert.Single(TestDiagnostics.Of(c), x => x.Code == "UnsatisfiedEffectBound_Kd");
        Assert.Equal("State.inspect()", shown.Text);
        Assert.Equal("confined: a mutable static access", shown.Label);
        Assert.Contains(TestDiagnostics.Of(c), x => x.Code == "UnresolvedBinding_Kd" && x.Text == "missing");
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var record = Assert.Single(result.Diagnostics, x => x.Code == shown.Code);
        Assert.Equal("The operation does not satisfy a required effect bound", record.Message);
        Assert.Contains(record.Related!, x => x.Role == "declaration" && x.Label == "the original virtual slot");
        Assert.Contains(record.Related!, x => x.Role == "bound");
        Assert.Contains(record.Related!, x => x.Role == "effect");
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        Assert.Contains("confined: a mutable static access", console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity], x => x.Code == record.Code);
            Assert.Equal(record.Display!.Range, sent.Range);
            Assert.Contains("including unused declarations", sent.Message, StringComparison.Ordinal);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("1")]
    [InlineData("State.count")]
    public void WarmObligationChecksReuseTheirEvidence(string body)
    {
        var c = MinimalEmissionTest.Analyze("group State\n    public var count: i32 = 0\nopen struct Base\n    public virtual func read(self: objref/Self) -> i32\n        effect confined\n        return 1\nstruct Derived : Base\n    override func read(self: objref/Self) -> i32 => " + body + "\n()");
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Bind()));
    }
}
