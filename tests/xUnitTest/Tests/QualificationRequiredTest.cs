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

    // Since the MIR decides acceptance (R6-check H5), the constructor whose write Binding rejects also has A1's UninitializedPlace_Kd,
    // since the rejected write initializes nothing (PLAN G105): published directly until A6's 2g-1 (DECISIONS R5-M184; H3b T51, class 4).
    [Fact]
    public void AnOwnStoredFieldCanBeQualifiedInAConstructorBody()
    {
        const string source = "struct Item\n    var value: i32\n    public init() => value = 1\npublic func main() => ()";
        var records = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.Equal(
            [(nameof(DiagnosticCode.UninitializedPlace_Kd), "init() => value = 1"), (nameof(DiagnosticCode.QualificationRequired_Kd), "value")],
            records.Select(x => (x.Code, source.Substring(x.Span!.Value.Start, x.Span.Value.Length))));
        var record = records[1];
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

    // SPEC 9.5 (R7-N2d): a projected base stops a name as a nominal one does, also before the clause can be bound, in a clause, a
    // Constraint and behind an opening alias; each was accepted before.
    [Theory]
    [InlineData("contract HasBase\n    associate Parent\nstruct Source\n    Self is HasBase\n    associate HasBase.Parent is Q\nopen struct Q\n    public open struct N\n        public init() => ()\n    public init() => ()\npublic open struct N\n    public func far(self) -> i32 => 2\n    public init() => ()\nstruct T: Source.(HasBase).Parent\n    public init(): base() => ()\n    struct U: N\n        public init() => ()")]
    [InlineData("alias G1\ncontract HasBase\n    associate Parent\nstruct Source\n    Self is HasBase\n    associate HasBase.Parent is Q\ngroup G1\n    public open struct N\n        public init() => ()\nopen struct Q\n    public open struct N\n        public init() => ()\n    public init() => ()\nstruct T: Source.(HasBase).Parent\n    public init(): base() => ()\n    struct U: N\n        public init() => ()")]
    [InlineData("contract K<T>\ncontract HasBase\n    associate Parent\nstruct Source\n    Self is HasBase\n    associate HasBase.Parent is Q\nopen struct Q\n    public struct N\n    public init() => ()\nstruct N\nstruct T: Source.(HasBase).Parent\n    Self is K<N>\n    public init(): base() => ()")]
    [InlineData("contract K<T>\ncontract K2<T>\ncontract HasItem\n    associate Item\ncontract HasBase\n    associate Parent\nstruct Source\n    Self is HasBase\n    associate HasBase.Parent is Q\nopen struct Q: R\n    Self is HasItem\n    associate HasItem.Item is i32\n    protected open struct N\n        public init() => ()\n    public init() => ()\nopen struct R\n    private open struct N\n        public init() => ()\n    public init() => ()\n    struct T: Source.(HasBase).Parent\n        Self is K<Q.N>\n        Self is K2<T.(HasItem).Item>\n        public init(): base() => ()")]
    public void AProjectedBaseStopsANameAsANominalOneDoes(string source)
    {
        var c = CompilationTestHelper.Parse(source);
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, static x => x.Code == DiagnosticCode.QualificationRequired_Kd);
    }

    // SPEC 9.5, 18.2.1 (PLAN G632; R7-N1 c2): an unqualified Name never selects an enum Case or a member that needs a receiver, at the
    // scope stages and at the import stages 5 (document aliases) and 6 (default aliases) alike, so the stop is the use's one record, at
    // the Name, with the found member's cause. Before, the forbidden selection was made and its consumer's verdict recorded instead
    // (PrerequisiteUnavailable_Kd, NotCallable_Kd, NoApplicableOverload_Kd, InvalidAssignment_Kd, Unsupported_Kd or InternalInvariant_Kd).
    [Theory]
    [InlineData("enum Message\n    Quit\n    Write(string)\n    func bad() -> Message => Quit", "", "Quit", "Case")]
    [InlineData("enum Message\n    Quit\n    Write(string)\n    func f() -> i32\n        let n = Quit\n        let Quit = 1\n        return Quit", "", "Quit", "Case")]
    [InlineData("enum Box<T>\n    Empty\n    Full(T)\n    func wrap(x: T) -> Box<T> => Full(x@move)", "", "Full", "Case")]
    [InlineData("alias Message\nenum Message\n    Quit\n    Write(string)\nfunc f() -> Message => Quit", "", "Quit", "Case")]
    [InlineData("alias Message\nenum Message\n    Quit\n    Write(string)\nfunc f() -> Message => Write(\"a\")", "", "Write", "Case")]
    [InlineData("alias Kimi.Option<i32>\nfunc pick() -> Option<i32>\n    return Some(1)", "", "Some", "Case")]
    [InlineData("alias S\nstruct S\n    public var count: i32 = 0\n    public init() => ()\n    public func make() -> i32 => 1\n    public func read(self) -> i32 => 2\nfunc f() -> i32 => read()", "", "read", "Receiver")]
    [InlineData("alias Counter\nstruct Counter\n    public var count: i32 = 0\n    public func zero() -> i32 => 0\nfunc g() -> i32 => count", "", "count", "Receiver")]
    [InlineData("alias P\ncontract P\n    func m(self: ref/Self, x: i32) -> i32\nfunc g() -> i32 => m(1)", "", "m", "Receiver")]
    [InlineData("alias Counter\nalias Meter\nstruct Counter\n    public var count: i32 = 0\n    public func read(self: ref/Self) -> i32 => self.count\nstruct Meter\n    public var value: i32 = 0\n    public func read(self: ref/Self) -> i32 => self.value\nfunc f() -> i32 => read()", "", "read", "Receiver")]
    [InlineData("alias Message\nalias Tools\nenum Message\n    Quit\n    Write(string)\ngroup Tools\n    private func Quit() -> i32 => 1\nfunc f() -> Message => Quit", "", "Quit", "Case")]
    [InlineData("alias Message\nenum Message\n    Quit\n    Write(string)\ngroup Tools\n    public func Quit() -> i32 => 1\nfunc f() -> i32 => Quit()", "Alias=\n  \"Tools\"", "Quit", "Case")]
    [InlineData("enum Message\n    Quit\n    Write(string)\nfunc f() -> Message => Quit", "Alias=\n  \"Message\"", "Quit", "Case")]
    [InlineData("struct Counter\n    public var count: i32 = 0\nfunc g() -> i32 => count", "Alias=\n  \"Counter\"", "count", "Receiver")]
    [InlineData("alias Counter\nstruct Counter\n    public var Message: i32 = 0\nenum Message\n    Quit\n    Write(string)\nfunc f() -> Message => Message.Nope", "", "Message", "Receiver")]
    [InlineData("enum Message\n    Quit\n    Write(string)\nstruct Counter\n    var Message: i32 = 0\n    func f() -> Message => Message.Nope", "", "Message", "Receiver")]
    [InlineData("alias Counter\nstruct Counter\n    var stored: i32 = 0\n    public init() => ()\n    public computed total: i32\n        get(self: ref/Self) -> i32 => self.stored\nfunc g() -> i32 => total", "", "total", "Receiver")]
    [InlineData("enum Message\n    Quit\n    Write(string)\nfunc g() -> Message => Write(\"a\")", "Alias=\n  \"Message\"", "Write", "Case")]
    [InlineData("alias Counter\nstruct Counter\n    public var count: i32 = 0\n    public init() => ()\n    public func read(self: ref/Self) -> i32 => self.count\nfunc g() -> i32\n    let f = read\n    return 0", "", "read", "Receiver")]
    [InlineData("alias Counter\nstruct Counter\n    public var count: i32 = 0\n    public init() => ()\nfunc g() -> i32\n    count = 1\n    return 0", "", "count", "Receiver")]
    [InlineData("alias P\ncontract P\n    property value: i32 has get\nfunc g() -> i32 => value", "", "value", "Receiver")]
    [InlineData("alias Message\nenum Message\n    Quit\nfunc g() -> i32\n    let n = Quit\n    let Quit = 1\n    return Quit", "", "Quit", "Case")]
    [InlineData("alias Box<i32>\nstruct Box<T>\n    public var item: i32 = 0\n    public init() => ()\nfunc g() -> i32 => item", "", "item", "Receiver")]
    public void AnUnqualifiedNameNeverSelectsACaseOrAReceiverMember(string source, string settings, string name, string cause)
    {
        var record = Assert.Single(DiagnosticCorpus.Check(source, "Library", settings).Diagnostics);
        Assert.Equal("QualificationRequired_Kd", record.Code);
        Assert.Equal(name, source.Substring(record.Span!.Value.Start, record.Span.Value.Length));
        Assert.Equal(cause, record.Reason![3].Value);
    }

    // SPEC 9.5, 9.5.1, 18.2.2 (R7-N1 c2): an import stage's same-name declarations are checked for a conflict before 9.5's stop, Cases
    // and receiver members among them (9.2's Value role admits them), so a stage mixing them with a function or another value, or two
    // sources' Cases, is the ordinary same-name ambiguity at the Name.
    [Theory]
    [InlineData("alias Message\nalias Tools\nenum Message\n    Quit\n    Write(string)\ngroup Tools\n    public func Quit() -> i32 => 1\nfunc f() -> i32 => Quit()", "", "Quit")]
    [InlineData("alias Message\nalias Tools\nenum Message\n    Quit\n    Write(string)\ngroup Tools\n    public func Write(s: string) -> i32 => 1\nfunc f() -> i32 => Write(\"a\")", "", "Write")]
    [InlineData("alias Counter\nalias Config\nstruct Counter\n    public var count: i32 = 0\ngroup Config\n    public var count: i32 = 3\nfunc f() -> i32 => count", "", "count")]
    [InlineData("alias A\nalias B\nenum A\n    Quit\nenum B\n    Quit\nfunc f() -> A => Quit", "", "Quit")]
    [InlineData("alias Message\nalias Counter\nenum Message\n    Quit\n    Write(string)\nstruct Counter\n    public var count: i32 = 0\n    public func Quit(self: ref/Self) -> i32 => 1\nfunc f() -> i32 => Quit()", "", "Quit")]
    [InlineData("alias Counter\nalias Tools\nstruct Counter\n    public var count: i32 = 0\ngroup Tools\n    public func count() -> i32 => 1\nfunc f() -> i32 => count()", "", "count")]
    [InlineData("enum Message\n    Quit\n    Write(string)\ngroup Tools\n    public func Quit() -> i32 => 1\nfunc f() -> i32 => Quit()", "Alias=\n  \"Message\"\n  \"Tools\"", "Quit")]
    public void AnImportStageChecksItsConflictBeforeTheStop(string source, string settings, string name)
    {
        var records = DiagnosticCorpus.Check(source, "Library", settings).Diagnostics;
        Assert.Contains(records, x => x.Code == "AmbiguousBinding_Kd" && source.Substring(x.Span!.Value.Start, x.Span.Value.Length) == name);
        Assert.DoesNotContain(records, static x => x.Code == "QualificationRequired_Kd");
    }

    // SPEC 9.5, 7.4, 18.2.1 (R7-N1 c2): a stage with a selectable candidate selects; its members needing a receiver are never selected
    // (one group's, or another opened source's beside a free function), and an opened or default-aliased Type function is selectable.
    [Theory]
    [InlineData("struct S\n    func f(self: ref/Self) -> i32 => 1\n    func f(x: i32) -> i32 => x\n    func g() -> i32 => f(2)", "")]
    [InlineData("alias Message\nenum Message\n    Quit\n    public func make() -> Message => .Quit\nfunc f() -> Message => make()", "")]
    [InlineData("enum Message\n    Quit\n    Write(string)\n    public func make() -> Message => .Quit\nfunc f() -> Message => make()", "Alias=\n  \"Message\"")]
    [InlineData("alias Counter\nalias Tools\nstruct Counter\n    public var count: i32 = 0\n    public func read(self: ref/Self) -> i32 => self.count\ngroup Tools\n    public func read() -> i32 => 1\nfunc g() -> i32 => read()", "")]
    public void AStageWithASelectableCandidateSelectsIt(string source, string settings)
        => Assert.Empty(DiagnosticCorpus.Check(source, "Library", settings).Diagnostics);

    // SPEC 9.5, 7.4, 10 (R7-N1 c2): at a stage with a selectable candidate the receiver members of another opened source join the group
    // but are never selected, so a call no selectable candidate fits has no applicable overload; never 9.5's stop.
    [Theory]
    [InlineData("alias Counter\nalias Tools\nstruct Counter\n    public var count: i32 = 0\n    public func read(self: ref/Self) -> i32 => self.count\ngroup Tools\n    public func read(x: i32) -> i32 => x\nfunc g() -> i32 => read()", "read()")]
    [InlineData("alias Counter\nalias Tools\nstruct Counter\n    public var count: i32 = 0\n    public func read(self: ref/Self) -> i32 => self.count\ngroup Tools\n    public func read() -> i32 => 1\nfunc g(c: ref/Counter) -> i32 => read(c)", "read(c)")]
    public void AStageWithASelectableCandidateNeverSelectsItsReceiverMembers(string source, string call)
    {
        var record = Assert.Single(DiagnosticCorpus.Check(source, "Library").Diagnostics);
        Assert.Equal("NoApplicableOverload_Kd", record.Code);
        Assert.Equal(call, source.Substring(record.Span!.Value.Start, record.Span.Value.Length));
    }

    // SPEC 9.6, 9.5 (R7-N1 c2): a stop under 9.5 fails the value path of `X.m`, so the Type path is the sole success, at an import stage
    // as at a scope stage, wherever the Type path finds `X` (stage 6's default alias in the last row). Before, the value path selected the
    // opened member or Case and BU-117's found-both interim rejected the access with Unsupported_Kd; the invalid twin (neither path finds
    // `Nope`) keeps the stop's record at `Message`, as at stage 2.
    [Theory]
    [InlineData("alias Counter\nstruct Counter\n    public var Message: i32 = 0\nenum Message\n    Quit\n    Write(string)\nfunc f() -> Message => Message.Quit", "")]
    [InlineData("alias Kinds\nenum Kinds\n    Message\n    Other\nenum Message\n    Quit\n    Write(string)\nfunc f() -> Message => Message.Quit", "")]
    [InlineData("struct Counter\n    public var Message: i32 = 0\nenum Message\n    Quit\n    Write(string)\nfunc f() -> Message => Message.Quit", "Alias=\n  \"Counter\"")]
    [InlineData("enum Message\n    Quit\n    Write(string)\nstruct Counter\n    var Message: i32 = 0\n    func f() -> Message => Message.Quit", "")]
    [InlineData("alias Counter\nstruct Counter\n    public var Tools: i32 = 0\n    public init() => ()\ngroup Tools\n    public func make() -> i32 => 1\nfunc g() -> i32 => Tools.make()", "")]
    [InlineData("alias Counter\nstruct Counter\n    public var Tools: i32 = 0\n    public init() => ()\ngroup Lib\n    public group Tools\n        public func make() -> i32 => 1\nfunc g() -> i32 => Tools.make()", "Alias=\n  \"Lib\"")]
    public void AValuePathStoppedAtAnImportStageLeavesTheTypePath(string source, string settings)
        => Assert.Empty(DiagnosticCorpus.Check(source, "Library", settings).Diagnostics);

    // SPEC 9.5, 8.6.4 (PLAN G656): an opened Type-function requirement needs no receiver and is no Case, so 9.5 does not stop it; its
    // call is rejected after selection, by the requirement call's record (G656's unit).
    [Fact]
    public void AnOpenedTypeFunctionRequirementIsNoQualificationStop()
    {
        var records = DiagnosticCorpus.Check("alias P\ncontract P\n    func make() -> i32\nfunc g() -> i32 => make()", "Library", string.Empty).Diagnostics;
        Assert.NotEmpty(records);
        Assert.DoesNotContain(records, static x => x.Code == "QualificationRequired_Kd");
    }
}
