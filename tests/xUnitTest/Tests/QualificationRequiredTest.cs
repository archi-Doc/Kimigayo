// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class QualificationRequiredTest
{
    [Theory]
    [InlineData("group Outer\n    func marker() -> i32 => 1\n    open struct Base\n        public func marker() -> i32 => 25\n    struct Derived: Base\n        func read() -> i32 => marker()", "marker", "Self.")]
    [InlineData("open struct Base\n    protected var count: i32 = 1\nstruct Derived: Base\n    func read(self: ref/Self) -> i32 => count", "count", "self.")]
    [InlineData("struct Node\nopen struct Base\n    public struct Node\nstruct Derived: Base\n    func read(value: Node) => ()", "Node", "Self.")]
    [InlineData("struct Item\n    var count: i32 = 1\n    func read(self: ref/Self) -> i32 => count", "count", "self.")]
    [InlineData("struct Item\n    let id: i32 = 1\n    drop\n        if id == 1 => Console.writeLine(\"first\")", "id", "self.")]
    [InlineData("struct View<T> {source}\n    let value: ref/T during source\n    func f(self: ref/Self) -> ref/T during self.source => value", "value", "self.")]
    public void AContainerStopsUnqualifiedLookup(string declarations, string name, string qualifier)
    {
        var source = declarations + "\npublic func main() => ()";
        var record = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal("QualificationRequired_Kd", record.Code);
        Assert.Equal(name, source.Substring(record.Span!.Value.Start, record.Span.Value.Length));
        Assert.Contains(record.Repairs!, x => x.Kind == "Repair.Qualify" && x.Edits.Single().Text == qualifier);
        var repair = record.Repairs!.First(x => x.Edits.Single().Text == qualifier);
        Assert.Empty(DiagnosticCorpus.Check(UnnecessaryUnsafeBlockTest.Apply(source, repair.Edits)).Diagnostics);
    }

    [Fact]
    public void ExplicitQualificationAndLexicalLocalsRemainValid()
    {
        const string source = "group Outer\n    func marker() -> i32 => 1\n    open struct Base\n        public func marker() -> i32 => 25\n    struct Derived: Base\n        func read() -> i32 => Self.marker() + ::Outer.marker()\n        func local() -> i32\n            let marker = 4\n            return marker\npublic func main() => ()";
        Assert.Empty(DiagnosticCorpus.Check(source).Diagnostics);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
    }

    [Theory]
    [InlineData("open struct Base<T>\n    public func marker() -> i32 => 1\nstruct Derived<U>: Base<U>\n    func read() -> i32 => marker()", "Self.")]
    [InlineData("open struct Base\n    public func marker() -> i32 => 1\nstruct Derived: Base\n    struct Nested\n        func read() -> i32 => marker()", "Derived.")]
    [InlineData("open struct Base\n    public var count: i32 = 1\nopen struct Middle: Base\nstruct Derived: Middle\n    func read(self) -> i32 => count", "self.")]
    public void GenericTransitiveAndEnclosingContainersKeepTheLookupBoundary(string declarations, string qualifier)
    {
        var source = declarations + "\npublic func main() => ()";
        var record = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal("QualificationRequired_Kd", record.Code);
        var repair = Assert.Single(record.Repairs!, x => x.Edits.Single().Text == qualifier);
        Assert.Empty(DiagnosticCorpus.Check(UnnecessaryUnsafeBlockTest.Apply(source, repair.Edits)).Diagnostics);
    }

    [Fact]
    public void OuterDeclarationsAreExplicitAlternativesOnly()
    {
        const string source = "group Outer\n    func marker() -> i32 => 1\n    open struct Base\n        public func marker() -> i32 => 25\n    struct Derived: Base\n        func read() -> i32 => marker()\npublic func main() => ()";
        var output = DiagnosticCorpus.Check(source);
        var record = Assert.Single(output.Diagnostics);
        Assert.Equal("QualificationRequired_Kd", record.Code);
        Assert.Equal(["Self.", "::Outer."], record.Repairs!.Select(static x => x.Edits.Single().Text));
        Assert.Equal(["marker", "Derived", "Value", "Inherited", "false", "Base"], record.Reason!.Select(static x => x.Value));
        Assert.Equal(["base", "declaration"], record.Related!.Select(static x => x.Role).Order(StringComparer.Ordinal));
        foreach (var repair in record.Repairs!)
        {
            Assert.Empty(repair.Verified);
            Assert.Equal([RepairCondition.UsageLegality, RepairCondition.Selection], repair.Required.Select(static x => x.Condition));
            Assert.Empty(DiagnosticCorpus.Check(UnnecessaryUnsafeBlockTest.Apply(source, repair.Edits)).Diagnostics);
        }

        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(new(output.Diagnostics, output.Sources), string.Empty);
        Assert.Contains(record.Message, console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(output.Sources[record.Source].Path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(output, [identity], identity, related)[identity]);
            Assert.Equal((record.Display!.Range, record.Code), (sent.Range, sent.Code));
        }
    }

    [Theory]
    [InlineData("group Outer\n    func marker() -> i32 => 1\n    open struct Base\n        private func marker() -> i32 => 25\n    struct Derived: Base\n        func read() -> i32 => marker()")]
    [InlineData("struct Base\nopen struct Parent<T>\nstruct Derived<T>: Parent<Base>")]
    public void InaccessibleBaseNamesAndBaseClauseArgumentsDoNotStopLookup(string declarations)
        => Assert.Empty(DiagnosticCorpus.Check(declarations + "\npublic func main() => ()").Diagnostics);

    [Fact]
    public void AStoppedTypePathDoesNotRejectAValidValuePath()
    {
        const string source = "struct Value\n    public var item: i32 = 1\nopen struct Base\n    public struct Name\nstruct Derived: Base\n    func read(Name: ref/Value) -> i32 => Name.item\npublic func main() => ()";
        Assert.Empty(DiagnosticCorpus.Check(source).Diagnostics);
    }

    [Fact]
    public void ATypeRepairHasNoUsageConditionAndRefutedArityOffersNoRepair()
    {
        const string declaration = "open struct Base\n    public struct Item<T>\nstruct Derived: Base\n    func read(x: Item<i32>) => ()\npublic func main() => ()";
        var record = Assert.Single(DiagnosticCorpus.Check(declaration).Diagnostics);
        var repair = Assert.Single(record.Repairs!);
        Assert.Equal([RepairCondition.Selection], repair.Verified);
        Assert.Empty(repair.Required);
        var wrongArity = Assert.Single(DiagnosticCorpus.Check(declaration.Replace("Item<i32>", "Item<i32, bool>", StringComparison.Ordinal)).Diagnostics);
        Assert.Equal("QualificationRequired_Kd", wrongArity.Code);
        Assert.Null(wrongArity.Repairs);
    }

    [Fact]
    public void ADeclarationFragmentSharesTheBaseBoundary()
    {
        var c = MinimalEmissionTest.Analyze("open struct Base\n    public func marker() -> i32 => 1\nstruct Derived: Base");
        c.Kotonoha.AddSource(new SourceDocument("fragment.kimi", "struct Derived\n    func read() -> i32 => marker()"));
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, static x => x.Code == DiagnosticCode.QualificationRequired_Kd);
        Assert.False(CompilationTestHelper.Reload(c).Bind().IsComplete);
    }

    [Fact]
    public void AConditionalMemberReservesItsNameBeforeConditionChecking()
    {
        const string source = "contract C\nopen struct Base<T>\n    Self is C when T is Copy\n        public func marker() -> i32 => 1\nstruct Derived<T>: Base<T>\n    func read() -> i32 => marker()\npublic func main() => ()";
        var record = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal("QualificationRequired_Kd", record.Code);
    }

    [Fact]
    public void ABaseDependencyCycleTerminatesWithARejection()
        => Assert.NotEmpty(DiagnosticCorpus.Check("struct S: S.N\n    open struct N: S\npublic func main() => ()").Diagnostics);

    [Fact]
    public void AnOwnStoredFieldCanBeQualifiedInAConstructorBody()
    {
        const string source = "struct Item\n    var value: i32\n    public init() => value = 1\npublic func main() => ()";
        var record = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(["value", "Item", "Value", "Receiver", "true"], record.Reason!.Select(static x => x.Value));
        Assert.Equal("declaration", Assert.Single(record.Related!).Role);
        var repair = Assert.Single(record.Repairs!);
        Assert.Equal("self.", repair.Edits.Single().Text);
        Assert.Empty(DiagnosticCorpus.Check(UnnecessaryUnsafeBlockTest.Apply(source, repair.Edits)).Diagnostics);
    }

    [Fact]
    public void ExplicitRepairsPreserveTheChosenMemberAtRuntime()
        => ScalarEmissionTest.EmitFixture("QualificationSelectedMember", "group Outer\n    public func marker() -> i32 => 1\n    public open struct Base\n        public func marker() -> i32 => 25\n    public struct Derived: Base\n        public func read() -> i32 => Self.marker() + ::Outer.marker()\nrequire Outer.Derived.read() == 26 else => $abort(\"selection\")", string.Empty);

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ValidAndRejectedLookupsReuseTheirStorage()
    {
        foreach (var qualifier in new[] { string.Empty, "Self." })
        {
            var c = MinimalEmissionTest.Analyze("open struct Base\n    public func marker() -> i32 => 1\nstruct Derived: Base\n    func read() -> i32 => " + qualifier + "marker()");
            var valid = true;
            Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete == (qualifier.Length != 0)));
            Assert.True(valid);
            Assert.Equal(qualifier.Length != 0, CompilationTestHelper.Reload(c).Bind().IsComplete);
        }
    }
}
