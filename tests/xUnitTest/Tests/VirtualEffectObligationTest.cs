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
    // The unrelated mutable group storage is still outside generation (P42): the collector's StaticStorage record at check
    // (DECISIONS R5-M82, R5-M156); P42 reverts it.
    private const string Storage = "Unsupported_Kd public var count: i32 = 0";

    // SPEC 8.11.3 on MIR (H0b §5.4): A4 records the slot's bound at each implementation that breaks it, and Binding reports no effect issue.
    [Theory]
    [InlineData(false, "State.count", true)]
    [InlineData(true, "State.count", true)]
    [InlineData(false, "1", false)]
    [InlineData(true, "1", false)]
    public void UnusedOriginalAndOverrideBodiesMustSatisfyTheSlotBound(bool derived, string body, bool fails)
    {
        var source = "group State\n    public var count: i32 = 0\nopen struct Base\n    public virtual func read(self: objref/Self) -> i32\n        effect confined\n        return " + (derived ? "1" : body) + "\n" + (derived ? "struct Derived : Base\n    override func read(self: objref/Self) -> i32 => " + body + "\n" : string.Empty) + "()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.DoesNotContain(c.Binding.Issues, static x => x.Code == DiagnosticCode.UnsatisfiedEffectBound_Kd);
        Assert.False(c.Generate(TextWriter.Null));
        var slot = derived ? "override func read(self: objref/Self) -> i32 => " + body : "public virtual func read(self: objref/Self) -> i32";
        Assert.Equal((fails ? "UnsatisfiedEffectBound_Kd " + slot + " | " : string.Empty) + Storage, MinimalEmissionTest.Problems(c));
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
            // A4's record at the virtual function (H0b §5.4) has no label, Note or related location yet (H0a §4.1): Binding's bound role
            // at the valid `effect confined` is lost until A4 publishes it.
            var violation = Assert.Single(records, static x => x.Code == "UnsatisfiedEffectBound_Kd");
            Assert.Equal(source.IndexOf("score(self", StringComparison.Ordinal), violation.Span!.Value.Start);
            Assert.Null(violation.Label);
            Assert.Null(violation.Note);
            Assert.Null(violation.Related);
        }
    }

    [Fact]
    public void EachImplementationIsCheckedIndependently()
    {
        const string Source = "group State\n    public var count: i32 = 0\nopen struct Base\n    public virtual func read(self: objref/Self) -> i32\n        effect confined\n        return State.count\nstruct Derived : Base\n    override func read(self: objref/Self) -> i32 => State.count\n()";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.DoesNotContain(c.Binding.Issues, static x => x.Code == DiagnosticCode.UnsatisfiedEffectBound_Kd);
        Assert.Equal(
            "UnsatisfiedEffectBound_Kd public virtual func read(self: objref/Self) -> i32 | " +
            "UnsatisfiedEffectBound_Kd override func read(self: objref/Self) -> i32 => State.count | " + Storage,
            MinimalEmissionTest.Problems(c));
    }

    [Theory]
    [InlineData("self.read()", false)]
    [InlineData("self.other()", true)]
    public void BodyVerificationUsesPublicGuaranteesForRecursiveCalls(string body, bool fails)
    {
        var source = "open struct Base\n    public virtual func other(self: objref/Self) -> i32 => 1\n    public virtual func read(self: objref/Self) -> i32\n        effect confined\n        return " + body + "\n()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.DoesNotContain(c.Binding.Issues, static x => x.Code == DiagnosticCode.UnsatisfiedEffectBound_Kd);
        Assert.Equal(fails ? "UnsatisfiedEffectBound_Kd public virtual func read(self: objref/Self) -> i32" : string.Empty, MinimalEmissionTest.Problems(c));
        Assert.Equal(!fails, c.Accepts);
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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.DoesNotContain(c.Binding.Issues, static x => x.Code is DiagnosticCode.UnsatisfiedEffectBound_Kd or DiagnosticCode.IncompatibleContractImplementation_Kd);

        // A4 charges the default `State.count` to Probe's call, at its `Self is Reader`, and to neither implementation.
        Assert.Equal("IncompatibleContractImplementation_Kd Self is Reader | " + Storage, MinimalEmissionTest.Problems(c));
    }

    [Fact]
    public void EditingTheBodyRevokesItsFailureRecord()
    {
        const string Source = "group State\n    public var count: i32 = 0\nopen struct Base\n    public virtual func read(self: objref/Self) -> i32\n        effect confined\n        return 1\nstruct Derived : Base\n    override func read(self: objref/Self) -> i32 => State.count\n()";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.DoesNotContain(c.Binding.Issues, static x => x.Code == DiagnosticCode.UnsatisfiedEffectBound_Kd);
        Assert.Equal("UnsatisfiedEffectBound_Kd override func read(self: objref/Self) -> i32 => State.count | " + Storage, MinimalEmissionTest.Problems(c));
        var function = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.IsOverride);
        var donor = MinimalEmissionTest.Analyze(Source.Replace("=> State.count", "=> 2", StringComparison.Ordinal));
        var replacement = Assert.Single(KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.IsOverride);
        var body = ParseTestHelper.Detach(replacement.ExpressionBody!, new CodeBlockKoto(function.CodeContext));
        Assert.True(KotoHelper.Replace(function, function.ExpressionBody!, body));
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        c.Analyze();
        Assert.DoesNotContain(c.Binding.Issues, static x => x.Code == DiagnosticCode.UnsatisfiedEffectBound_Kd);
        Assert.Equal(Storage, MinimalEmissionTest.Problems(c));
    }

    // A4's record at the override (H0b §5.4) has no label, Note or related location yet (H0a §4.1): Binding's transitive call location
    // `State.inspect()`, its "confined: a mutable static access" label, the "including unused declarations" text and the original slot's,
    // bound and effect roles are lost until A4 publishes them.
    [Fact]
    public void TransitiveFailuresReachCliAndLspWithoutEvidence()
    {
        const string Source = "group State\n    public var count: i32 = 0\n    public func inspect() -> i32 => State.count\nopen struct Base\n    public virtual func read(self: objref/Self) -> i32\n        effect confined\n        return 1\nstruct Derived : Base\n    override func read(self: objref/Self) -> i32 => State.inspect()\nlet independent = missing";
        var path = Path.GetFullPath("virtual-effect-obligation.kimi");
        var c = MinimalEmissionTest.Analyze(Source, path);
        c.Binding.ReportDiagnostics();
        c.ReportAnalysis();
        var shown = Assert.Single(TestDiagnostics.Of(c), x => x.Code == "UnsatisfiedEffectBound_Kd");
        Assert.Equal("read(self: objref/Self) -> i32 => State.inspect()", shown.Text);
        Assert.Null(shown.Label);
        Assert.Null(shown.Note);
        Assert.Contains(TestDiagnostics.Of(c), x => x.Code == "UnresolvedBinding_Kd" && x.Text == "missing");
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var record = Assert.Single(result.Diagnostics, x => x.Code == shown.Code);
        Assert.Equal("The operation does not satisfy a required effect bound", record.Message);
        Assert.Null(record.Related);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        Assert.Contains(record.Message, console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity], x => x.Code == record.Code);
            Assert.Equal(record.Display!.Range, sent.Range);
            Assert.Equal(record.Message, sent.Message);
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
