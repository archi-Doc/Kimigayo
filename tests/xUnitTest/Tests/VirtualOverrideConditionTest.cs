// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class VirtualOverrideConditionTest(ITestOutputHelper output)
{
    private const string Helper = "group Operations\n    public func duplicate<V>(value: ref/V) -> V\n        V is Copy\n        return value@follow\n";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnOverrideBodyInheritsTheOriginalConditionalPremise(bool block)
    {
        var original = block
            ? "contract Marker\nopen struct Base<T>\n    Self is Marker when T is Copy\n        public virtual func copy(self: objref/Self, value: ref/T) -> T => Operations.duplicate(value)\n"
            : "open struct Base<T>\n    public virtual func copy(self: objref/Self, value: ref/T) -> T\n        T is Copy\n        return Operations.duplicate(value)\n";
        var c = MinimalEmissionTest.Analyze(Helper + original + "struct Derived<U> : Base<U>\n    override func copy(self: objref/Self, value: ref/U) -> U => Operations.duplicate(value)\n()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AContainingConditionMustFollowFromTheOriginalContract(bool inherited)
    {
        var condition = inherited ? "        T is Copy\n" : string.Empty;
        var c = MinimalEmissionTest.Analyze("contract Marker\nopen struct Base<T>\n    public virtual func read(self: objref/Self) -> i32\n" + condition + "        return 1\nstruct Derived<U> : Base<U>\n    Self is Marker when U is Copy\n        override func read(self: objref/Self) -> i32 => 2\n()");
        if (inherited)
        {
            Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        }
        else
        {
            c.Binding.ReportDiagnostics();
            Assert.Contains(TestDiagnostics.Of(c), x => x.Code == "UnprovenOverrideCondition_Kd");
        }
    }

    [Fact]
    public void EnclosingDerivedConstraintsCanEstablishTheCondition()
    {
        var c = MinimalEmissionTest.Analyze("contract Marker\nopen struct Base<T>\n    public virtual func read(self: objref/Self) -> i32 => 1\nstruct Derived<U> : Base<U>\n    U is Copy\n    Self is Marker when U is Copy\n        override func read(self: objref/Self) -> i32 => 2\n()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
    }

    [Fact]
    public void InheritedPremisesFollowTheCompleteBoundBasePath()
    {
        var c = MinimalEmissionTest.Analyze(Helper + "open struct Base<T>\n    public virtual func copy(self: objref/Self, value: ref/T) -> T\n        T is Copy\n        return Operations.duplicate(value)\nopen struct Middle<V> : Base<V>\nstruct Derived<U> : Middle<U>\n    override func copy(self: objref/Self, value: ref/U) -> U => Operations.duplicate(value)\n()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
    }

    [Fact]
    public void DifferentConditionsDoNotAllowDuplicateImplementations()
    {
        var c = MinimalEmissionTest.Analyze("contract Marker\ncontract Another\nopen struct Base<T>\n    public virtual func read(self: objref/Self) -> i32 => 1\nstruct Derived<U> : Base<U>\n    Self is Marker when U is Copy\n        override func read(self: objref/Self) -> i32 => 2\n    Self is Another when U is Owned\n        override func read(self: objref/Self) -> i32 => 3\n()");
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.DuplicateOverride_Kd);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.UnprovenOverrideCondition_Kd);
    }

    // SPEC 6.4.3 (the owner's O-370): an override of a slot whose substituted condition is Refuted (`string is Copy`) is one declaration
    // error at the override, not a contradictory premise of its body.
    [Fact]
    public void AClosedInapplicableSlotsOverrideIsOneDeclarationError()
    {
        var c = MinimalEmissionTest.Analyze("open struct Base<T>\n    public virtual func read(self: objref/Self) -> i32\n        T is Copy\n        return 1\nstruct Derived : Base<string>\n    override func read(self: objref/Self) -> i32 => 2\n()");
        Assert.Equal([DiagnosticCode.InvalidConstraint_Kd], c.Binding.Issues.Select(static x => x.Code));
    }

    // R7-E2 E2-5a (B's O8, M1; labelled from SPEC first): an override of a slot whose substituted condition is Refuted (6.4.3) that also
    // breaks the slot's contract (6.4.2's third step, an external name or the result Type) fails two independent requirements, so each is
    // its own record at the override (SPEC 23.1.2; dev kept the first only, the contract's facts unread). Twins: the same contract break
    // where the condition holds (`i32 is Copy`) gives its one record; the condition alone is the row above.
    [Theory]
    [InlineData("x1", "string", "renamed: i32) -> i32 => renamed", "InvalidConstraint_Kd: * | OverrideContractMismatch_Kd: *")]
    [InlineData("x2", "string", "value: i32) -> i64 => 2", "InvalidConstraint_Kd: * | OverrideContractMismatch_Kd: *")]
    [InlineData("x1t", "i32", "renamed: i32) -> i32 => renamed", "OverrideContractMismatch_Kd: *")]
    [InlineData("x2t", "i32", "value: i32) -> i64 => 2", "OverrideContractMismatch_Kd: *")]
    public void AnInapplicableSlotsOverrideThatBreaksItsContractKeepsBothRecords(string form, string argument, string tail, string records)
        => CompilationTestHelper.AssertBindingRecordsAtLines(form, $"open struct Base<T>\n    public virtual func read(self: objref/Self, value: i32) -> i32\n        T is Copy\n        return value\nstruct Derived : Base<{argument}>\n    override func read(self: objref/Self, {tail}\npublic func main() => ()", records);

    [Fact]
    public void EditingAnOriginalPremiseRevokesTheInheritedProof()
    {
        const string Source = "contract Marker\nopen struct Base<T>\n    public virtual func read(self: objref/Self) -> i32\n        T is Copy\n        return 1\nstruct Derived<U> : Base<U>\n    Self is Marker when U is Copy\n        override func read(self: objref/Self) -> i32 => 2\n()";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var donor = MinimalEmissionTest.Analyze(Source.Replace("T is Copy", "T is Owned", StringComparison.Ordinal));
        var original = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.IsVirtual);
        var changed = KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.IsVirtual);
        var replacement = ParseTestHelper.Detach(changed.TypeConstraints[0], new CodeBlockKoto(original.CodeContext));
        Assert.True(KotoHelper.Replace(original, original.TypeConstraints[0], replacement));
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.UnprovenOverrideCondition_Kd);
        c.Binding.ReportDiagnostics();
        var record = Assert.Single(c.Diagnostics.Finalize().Diagnostics, x => x.Code == "UnprovenOverrideCondition_Kd");
        Assert.Equal(new DiagnosticValue("proof", DiagnosticValueKind.Enumeration, "Unknown"), record.Reason![2]);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void InheritedPremisesReuseTheirStorage()
    {
        var c = MinimalEmissionTest.Analyze(Helper + "contract Marker\nopen struct Base<T>\n    public virtual func copy(self: objref/Self, value: ref/T) -> T\n        T is Copy\n        return Operations.duplicate(value)\nstruct Derived<U> : Base<U>\n    Self is Marker when U is Copy\n        override func copy(self: objref/Self, value: ref/U) -> U => Operations.duplicate(value)\n()");
        var complete = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => complete &= c.Bind().IsComplete));
        Assert.True(complete, MinimalEmissionTest.Describe(c));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConditionDiagnosticsIdentifyTheUnprovenClauseInCliAndLsp(bool refuted)
    {
        const string Source = "contract Marker\nopen struct Base<T>\n    public virtual func read(self: objref/Self) -> i32 => 1\nstruct Derived<U> : Base<U>\n    Self is Marker when U is Copy\n        override func read(self: objref/Self) -> i32 => 2\nlet independent = missing";
        var source = refuted ? Source.Replace("struct Derived<U> : Base<U>\n", "struct Derived<U> : Base<U>\n    U is not Copy\n", StringComparison.Ordinal) : Source;
        var path = Path.GetFullPath("virtual-override-condition.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        c.Binding.ReportDiagnostics();
        if (refuted)
        {
            // R7-C3 C3-1b (F's C2 ruling 297 (b), DECISIONS Q13): Derived's `U is not Copy` against the block's condition `U is Copy` is the
            // block's own premise error (SPEC 8.2.3, 8.8.1; as O-296 (a)'s l2): InvalidConstraint_Kd at the conditional, and dev's
            // UnprovenOverrideCondition_Kd at `U is Copy` rests on it.
            Assert.DoesNotContain(TestDiagnostics.Of(c), x => x.Code == "UnprovenOverrideCondition_Kd");
            Assert.Contains(TestDiagnostics.Of(c), x => x.Code == "InvalidConstraint_Kd" && x.Text is { } text && text.StartsWith("Self is Marker when U is Copy", StringComparison.Ordinal));
            Assert.Contains(TestDiagnostics.Of(c), x => x.Code == "UnresolvedBinding_Kd" && x.Text == "missing");
            return;
        }

        var shown = Assert.Single(TestDiagnostics.Of(c), x => x.Code == "UnprovenOverrideCondition_Kd");
        Assert.Equal("U is Copy", shown.Text);
        Assert.Contains(TestDiagnostics.Of(c), x => x.Code == "UnresolvedBinding_Kd" && x.Text == "missing");
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var record = Assert.Single(result.Diagnostics, x => x.Code == shown.Code);
        Assert.Equal(DiagnosticCategory.Proof, record.Category);
        Assert.Equal(
            [new("condition", DiagnosticValueKind.Text, "U is Copy"), new("cause", DiagnosticValueKind.Enumeration, "Condition"), new("proof", DiagnosticValueKind.Enumeration, "Unknown")],
            record.Reason!);
        Assert.Contains(record.Related!, x => x.Role == "declaration" && x.Label == "original virtual slot");
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        Assert.Contains("original virtual slot", console.Text, StringComparison.Ordinal);
        // D3, Q13 M3: the removed Note restated the clause and its Unknown proof (the record's reason above); the CLI shows the clause as the
        // label. A Refuted override condition now meets the block's premise error first (ruling 297 (b)); no row pins its Refuted CLI/LSP record.
        Assert.Contains("^^^^^^^^^ U is Copy", console.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("is not implied", console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity], x => x.Code == record.Code);
            Assert.Equal(record.Display!.Range, sent.Range);
            Assert.Contains(record.Label!, sent.Message, StringComparison.Ordinal); // The clause, in the LSP message too.
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }
}
