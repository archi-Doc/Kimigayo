// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 7.3.1, 9.4.1, 9.5: the functions of one Name acquire corresponding parameters of overlapping Types in one mode.</summary>
public sealed class ParameterShapeTest(ITestOutputHelper output)
{
    private const string Types = "struct Node\n    public var number: i32 = 0\nstruct Value\n    Self is Copy\n    public var number: i32 = 0\nstruct Score\n    Self is Copy\n    public var points: i32 = 0\n";
    private const string Game = "func bumped(score: Score) -> Score\n    var result = score\n    result.points += 1\n    return result\nfunc bump(score: uniq/Score) -> () => score.points += 10\nstruct Game\n    public var score: Score = Score.init()\n";

    [Theory]
    [InlineData("func bump(score: Score) -> Score => score\nfunc bump(score: uniq/Score) -> () => ()", "uniq/Score", "Exclusive", "ByValue", "position 0 of an unbound call", "Value(Score)", false)]
    [InlineData("func inspect(value: ref/i32) -> () => ()\nfunc inspect(value: uniq/i32) -> () => ()", "uniq/i32", "Exclusive", "Shared", "position 0 of an unbound call", "Value(i32)", false)]
    [InlineData("func select(value: Node ! flag: bool) -> () => ()\nfunc select(value: ref/Node ! flag: i32) -> () => ()", "ref/Node", "Shared", "ByValue", "position 0 of an unbound call", "Value(Node)", false)]
    [InlineData("func g<T>(value: T) -> () => ()\nfunc g(value: uniq/Node) -> () => ()", "uniq/Node", "Exclusive", "ByValue", "position 0 of an unbound call", "Value(Node)", true)]
    [InlineData("func route(value: obj/Node) -> () => ()\nfunc route(value: objref/Node) -> () => ()", "objref/Node", "Shared", "ByValue", "position 0 of an unbound call", "Object(Node)", false)]
    [InlineData("struct Api<A, B>\n    public func inspect(value: Array<A>) -> () => ()\n    public func inspect(value: ref/Array<B>) -> () => ()", "ref/Array<B>", "Shared", "ByValue", "position 0 of an unbound call", "Value(Array<B>)", true)]
    [InlineData("func fill<length N>(value: [(N + 1) of i32], witness: [N of i32]) -> () => ()\nfunc fill<length M>(value: ref/[2 of i32], witness: [M of i32]) -> () => ()", "ref/[2 of i32]", "Shared", "ByValue", "position 0 of an unbound call", "Value([2 of i32])", true)]
    [InlineData("struct Point\n    public func inspect(self: ref/Self) -> () => ()\n    public func inspect(value: Self) -> () => ()", "Self", "ByValue", "Shared", "position 0 of an unbound call", "Value(Point)", false)]
    [InlineData("func choose<T>(value: T, items: ref/Array<T>) -> () => ()\nfunc choose<T>(value: Node, items: ref/Array<T>) -> () => ()", "Node", "ByValue", "Shared", "position 0 of an unbound call", "Value(Node)", true)]
    [InlineData("struct Box<A>\n    public func f(value: A) -> () => ()\n    public func f(value: Node) -> () => ()", "Node", "ByValue", "Shared", "position 0 of an unbound call", "Value(Node)", true)]
    [InlineData("func tag(! value: Value, count: i32) -> () => ()\nfunc tag(! count: i32, value: ref/Value) -> () => ()", "ref/Value", "Shared", "ByValue", "the external name value", "Value(Value)", false)]
    [InlineData("struct Point\n    public var x: i32 = 0\n    public func scale(self: uniq/Self, by: i32) -> () => ()\n    public func scale(self: uniq/Self, by: ref/i32) -> () => ()", "ref/i32", "Shared", "ByValue", "position 0 of a bound call", "Value(i32)", false)]
    [InlineData("contract Api\n    func f(self, value: Node) -> ()\n    func f(self, value: ref/Node) -> ()", "ref/Node", "Shared", "ByValue", "position 0 of a bound call", "Value(Node)", false)]
    public void TheLaterDeclarationIsRejectedAtItsParameter(string source, string parameter, string mode, string other, string correspondence, string key, bool conservative)
    {
        var c = Analyze(Types + source);
        var error = Assert.Single(Errors(c));
        Assert.Equal(nameof(DiagnosticCode.ParameterShapeMismatch_Kd), error.Code);
        Assert.Equal(parameter, error.Text);
        Assert.Equal($"{correspondence} is acquired {mode} here and {other} there", error.Label);
        Assert.Equal(conservative, error.Note is not null);
        Assert.Contains("sorted/sort", error.Advice, StringComparison.Ordinal);
        var result = c.Diagnostics.Finalize();
        var record = Assert.Single(result.Diagnostics, static x => x.Severity == DiagnosticSeverity.Error);
        Assert.Equal(DiagnosticCategory.Language, record.Category);
        Assert.Equal([("mode", mode), ("other", other), ("correspondence", correspondence), ("key", key)], record.Reason!.Select(static x => (x.Name, x.Value)));
        var related = Assert.Single(record.Related!);
        Assert.Equal("declaration", related.Role);
        Assert.Contains(correspondence, related.Label, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Diagnostics, static x => x.Code == nameof(DiagnosticCode.PrerequisiteUnavailable_Kd));
        output.WriteLine(record.Message + "\n" + related.Label);
    }

    // SPEC 7.3.1: one record per corresponding parameter of the later declaration; several overlapping earlier declarations are its parties.
    [Fact]
    public void EachConflictingParameterIsOneRecordWithEveryOverlappingDeclaration()
    {
        var c = Analyze(Types + "func pair(left: Value, right: Value) -> () => ()\nfunc pair(left: Value, right: ref/Value) -> () => ()\nfunc pair(left: ref/Value, right: uniq/Value) -> () => ()");
        var errors = Errors(c);
        Assert.Equal(["ref/Value", "ref/Value", "uniq/Value"], errors.Select(static x => x.Text));
        var records = c.Diagnostics.Finalize().Diagnostics.Where(static x => x.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.Equal([1, 2, 2], records.Select(static x => x.Related!.Length));
    }

    [Theory]
    [InlineData("func f(text: ref/string) -> () => ()\nfunc f(text: i32) -> () => ()")]
    [InlineData("func f(value: Node) -> () => ()\nfunc f<T>(value: T) -> () => ()")]
    [InlineData("struct Left\nstruct Right\nfunc split(state: uniq/Left) -> () => ()\nfunc split(state: uniq/Right) -> () => ()")]
    [InlineData("func show(value: Node) -> () => ()\nfunc show(value: objref/Node) -> () => ()")]
    [InlineData("func f<T>(value: T ! tag: i32) -> () => ()\nfunc f<s/U>(value: s/U ! tag: i64) -> () => ()")]
    [InlineData("struct Container<T>\n    public func add(self: uniq/Self, value: T) -> () => ()\n    public func add(self: uniq/Self, value: T, at: isize) -> () => ()")]
    [InlineData("func fill(buffer: [2 of i32]) -> () => ()\nfunc fill(buffer: ref/[3 of i32]) -> () => ()")]
    [InlineData("func take(range: Range<i32, i32>) -> () => ()\nfunc take(range: ref/Range<i64, i64>) -> () => ()")]
    [InlineData("struct Point\n    public func inspect(self: ref/Self) -> () => ()\n    public func inspect(self: ref/Self, by: i32) -> () => ()")]
    [InlineData("contract Api\n    func f(self, value: Node) -> ()\n    func f(self, value: Node, count: i32) -> ()")]
    public void PairsWithOneModeOrDisjointTypesAreAccepted(string source)
    {
        var c = Analyze(Types + source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Empty(Errors(c));
    }

    // SPEC 7.3.1, 15.1.5: a forgotten @uniq is an error at the only candidate; no by-value candidate takes the call.
    [Theory]
    [InlineData("var game = Game.init()\nbump(game.score)", "ExclusiveBorrowRequired_Kd")]
    [InlineData("var game = Game.init()\nbump(game.score@uniq)\nlet next = bumped(game.score)", "")]
    public void TheOnlyCandidateDecidesTheAcquisition(string use, string codes)
    {
        var c = Analyze(Types + Game + use);
        Assert.Equal(codes.Length == 0 ? [] : codes.Split(','), Errors(c).Select(static x => x.Code));
    }

    [Fact]
    public void ExplicitAcquisitionsSelectTheirCandidatesAtRunTime()
        => ScalarEmissionTest.EmitFixture("ParameterShapeSelection", Types + Game + "var game = Game.init()\nbump(game.score@uniq)\nlet next = bumped(game.score)\nrequire game.score.points == 10 and next.points == 11 else => $abort(\"bump\")\nConsole.writeLine(\"ok\")", "ok\n");

    // SPEC 9.4.1: a group gathered at the alias stage from two Containers is checked at the use; a qualified Name selects one source.
    [Theory]
    [InlineData("let a = f(v@ref)", "ParameterShapeMismatch_Kd")]
    [InlineData("let a = f(v)", "ParameterShapeMismatch_Kd")]
    [InlineData("let a = A.f(v)\nlet b = B.f(v@ref)", "")]
    public void GroupsGatheredAtTheAliasStageAreCheckedAtTheUse(string use, string codes)
    {
        var c = Analyze("alias A\nalias B\n" + Types + "group A\n    public func f(value: Value) -> i32 => 1\ngroup B\n    public func f(value: ref/Value) -> i32 => 2\nlet v = Value.init()\n" + use);
        var errors = Errors(c);
        Assert.Equal(codes.Length == 0 ? [] : codes.Split(','), errors.Select(static x => x.Code));
        if (errors.Length != 0)
        {
            Assert.Equal("f", errors[0].Text);
            Assert.Contains("A.f", errors[0].Advice, StringComparison.Ordinal);
            Assert.DoesNotContain(c.Diagnostics.Finalize().Diagnostics, static x => x.Code == nameof(DiagnosticCode.PrerequisiteUnavailable_Kd));
        }
    }

    // SPEC 9.5: requirements gathered from Constraints are checked at the use; no syntax selects among them.
    [Fact]
    public void RequirementsGatheredFromConstraintsAreCheckedAtTheUse()
    {
        var c = Analyze(Types + "contract Reader\n    func read(self, value: Node) -> i32\ncontract Consumer\n    func read(self, value: ref/Node) -> i32\nfunc use<T>(value: ref/T, node: ref/Node) -> i32\n    T is Reader\n    T is Consumer\n    return value.read(node)");
        var error = Assert.Single(Errors(c));
        Assert.Equal(nameof(DiagnosticCode.ParameterShapeMismatch_Kd), error.Code);
        Assert.Equal("read", error.Text);
        Assert.Null(error.Advice);
    }

    // SPEC 8.4.2: requirements inherited by refinement join the refining Contract's group.
    [Fact]
    public void RefinedRequirementsJoinTheGroup()
    {
        var c = Analyze(Types + "contract Base\n    func f(self, value: Node) -> ()\ncontract Child: Base\n    func f(self, value: ref/Node) -> ()");
        var error = Assert.Single(Errors(c));
        Assert.Equal(nameof(DiagnosticCode.ParameterShapeMismatch_Kd), error.Code);
        Assert.Equal("ref/Node", error.Text);
    }

    // SPEC 7.3.1: the declarations stay in their group; a call that selects one of them is checked normally.
    [Theory]
    [InlineData("let a = f(Node.init())", "ParameterShapeMismatch_Kd")]
    [InlineData("let node = Node.init()\nlet a = f(node@ref)", "ParameterShapeMismatch_Kd")]
    [InlineData("let a = f(true)", "ParameterShapeMismatch_Kd,NoApplicableOverload_Kd")]
    public void CallsOfAViolatingGroupAreCheckedNormally(string use, string codes)
    {
        var c = Analyze(Types + "func f(value: Node) -> i32 => 1\nfunc f(value: ref/Node) -> i32 => 2\n" + use);
        Assert.Equal(codes.Split(',').Order(StringComparer.Ordinal), Errors(c).Select(static x => x.Code).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void CliAndLanguageServerShowTheModesAndTheRelatedDeclaration()
    {
        var path = Path.GetFullPath("ParameterShape.kimi");
        var c = MinimalEmissionTest.Analyze(Types + "func bump(score: Score) -> Score => score\nfunc bump(score: uniq/Score) -> () => ()", path);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.ParameterShapeMismatch_Kd), record.Code);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("^^^^^^^^^^ position 0 of an unbound call is acquired Exclusive here and ByValue there", console.Text, StringComparison.Ordinal);
        Assert.Contains("bump takes position 0 of an unbound call as Score", console.Text, StringComparison.Ordinal);
        Assert.Contains(record.Advice!, console.Text, StringComparison.Ordinal);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        var check = new CheckOutput(CheckOutcome.Completed, false, TestPresence.No, result);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(check, [identity], identity, related)[identity]);
            Assert.Equal(record.Display!.Range, sent.Range);
            Assert.Equal(record.Code, sent.Code);
            Assert.Contains(record.Label!, sent.Message, StringComparison.Ordinal);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }

    [Fact]
    public void RebindingKeepsEachRecord()
    {
        var c = Analyze(Types + "func pair(left: Value, right: Value) -> () => ()\nfunc pair(left: ref/Value, right: uniq/Value) -> () => ()");
        var first = Errors(c);
        Assert.Equal(2, first.Length);
        c.Bind();
        c.Binding.ReportDiagnostics();
        Assert.Equal(first, Errors(c));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmShapeRejectionAllocatesNothing()
    {
        var c = CompilationTestHelper.ParseSuccess(Types + "func pair(left: Value, right: Value) -> () => ()\nfunc pair(left: ref/Value, right: uniq/Value) -> () => ()\nlet a = Value.init()\npair(a, a)");
        for (var i = 0; i < 4; i++)
        {
            Assert.False(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmAcceptedGroupsAllocateNothing()
    {
        var c = CompilationTestHelper.ParseSuccess(Types + Game + "func f(value: Node) -> i32 => 1\nfunc f<T>(value: T) -> i32 => 2\nvar game = Game.init()\nbump(game.score@uniq)\n_ = bumped(game.score)\n_ = f(Node.init())");
        for (var i = 0; i < 4; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
    }

    private static Compilation Analyze(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        c.Binding.ReportDiagnostics();
        return c;
    }

    private static TestDiagnostic[] Errors(Compilation c)
        => [.. TestDiagnostics.Of(c).Where(static x => x.Severity == DiagnosticSeverity.Error)];
}
