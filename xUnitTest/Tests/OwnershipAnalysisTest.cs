// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// Keep the large retained-state workloads separate from other allocation measurements.
[TestClass(DisableParallelization = true)]
public class OwnershipAnalysisTest
{
    [Theory]
    [InlineData("writeLine(\"Hello world\")")]
    [InlineData("public func main()\n    let message = \"Hello world\"\n    writeLine(message)")]
    [InlineData("let x: i32\nx = 1\nlet y = x\nlet z = x")]
    [InlineData("var s = \"a\"\nwriteLine(s)\ns = \"b\"\nwriteLine(s)")]
    [InlineData("func echo(x: string) -> string => x\nwriteLine(echo(\"x\"))")]
    [InlineData("func echo(x: string) -> string\n    return x\nwriteLine(echo(\"x\"))")]
    [InlineData("func f(c: bool)\n    let s: string\n    if c\n        s = \"a\"\n    else\n        s = \"b\"\n    writeLine(s)")]
    [InlineData("func f(c: bool)\n    while c\n        let s = \"a\"\n        writeLine(s)")]
    [InlineData("func f(c: bool)\n    while c\n        let s = \"a\"\n        continue")]
    [InlineData("func f()\n    while true\n        let s = \"a\"\n        exit")]
    [InlineData("func f()\n    while true\n        ()")]
    [InlineData("let s = \"a\"\nif s == \"a\"\n    writeLine(s)")]
    [InlineData("let s: string\nif true\n    s = \"a\"\nwriteLine(s)")]
    [InlineData("#if false\nfunc excluded()\n    defer: writeLine(\"later\")\nwriteLine(\"selected\")")]
    [InlineData("func sink<T>(x: T) => ()\nfunc twice<T>(x: T)\n    T is Copy\n    sink(x)\n    sink(x)")]
    public void SupportedProgramsVerify(string source)
    {
        var c = Parse(source);
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
    }

    [Theory]
    [InlineData("let s: string\nwriteLine(s)", OwnershipFailure.UninitializedUse)]
    [InlineData("let s = \"a\"\nwriteLine(s)\nwriteLine(s)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let s = \"a\"\nwriteLine(s)\nif s == \"x\"\n    ()", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let s = \"a\"\ns = \"b\"", OwnershipFailure.ReassignedLet)]
    [InlineData("let s = \"a\"\nwriteLine(s)\ns = \"b\"", OwnershipFailure.ReassignedLet)]
    [InlineData("func f(c: bool)\n    var s: string\n    if c\n        s = \"a\"\n    writeLine(s)", OwnershipFailure.UninitializedUse)]
    [InlineData("func f(c: bool)\n    let s = \"a\"\n    if c\n        writeLine(s)\n    writeLine(s)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("func f(c: bool)\n    let s: string\n    if c\n        s = \"a\"\n    s = \"b\"", OwnershipFailure.ReassignedLet)]
    [InlineData("func f(c: bool)\n    let s = \"a\"\n    while c\n        writeLine(s)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("func f(c: bool)\n    let s: string\n    while c\n        s = \"a\"", OwnershipFailure.ReassignedLet)]
    public void StateErrorsAreDiagnosedAfterConvergence(string source, OwnershipFailure failure)
    {
        var c = Parse(source);
        Assert.False(c.Ownership.Analyze().IsVerified);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidAssignment_Kd);
    }

    [Theory]
    [InlineData("func show(s: ref/string) => ()\nlet s = \"a\"\nshow(s)")]
    [InlineData("let s = \"a\" + \"b\"")]
    [InlineData("var s = \"a\"\ns += \"b\"")]
    [InlineData("defer: writeLine(\"later\")")]
    [InlineData("func f(x?: string = \"x\") => ()\nf()")]
    [InlineData("func echo(s: string) -> string => s\nlet s = \"a\"\nif s == echo(s)\n    ()")]
    public void UnsupportedOwnershipCannotBecomeVerified(string source)
    {
        var c = Parse(source);
        var result = c.Ownership.Analyze();
        Assert.False(result.IsVerified);
        Assert.True(result.UnsupportedCount > 0, Describe(c));
        Assert.All(c.Ownership.Bodies, x => Assert.False(x.IsVerified));
    }

    [Fact]
    public void ImplicitBorrowIsRecordedAndChecksMovedState()
    {
        var c = Parse("func show(s: ref/string) => ()\nlet s = \"a\"\nwriteLine(s)\nshow(s)");
        c.Ownership.Analyze();
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.PossiblyMovedUse);
        Assert.Contains(Body(c).Operations, x => x.Use == PlaceUseKind.Borrow);
    }

    [Theory]
    [InlineData("func sink<T>(x: T) => ()\nfunc twice<T>(x: T)\n    sink(x)\n    sink(x)", false)]
    [InlineData("func sink<T>(x: T) => ()\nfunc once<T>(x: T)\n    sink(x)", true)]
    public void UnknownCopyIsCheckedAtTheGenericDefinition(string source, bool valid)
    {
        var c = Parse(source);
        var result = c.Ownership.Analyze();
        Assert.True(valid == result.IsVerified, Describe(c));
        Assert.Contains(c.Ownership.Bodies.SelectMany(x => x.Operations), x => x.Acquisition == AcquisitionKind.CopyOrMove);
        if (!valid)
        {
            Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.PossiblyMovedUse);
        }
    }

    [Fact]
    public void ConditionalReplacementUsesPostRhsState()
    {
        var c = Parse("func f(c: bool)\n    var s: string\n    if c\n        s = \"a\"\n    s = \"b\"\n    s = s");
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        var body = c.Ownership.Bodies.Single(x => x.Function.Name == "f");
        var s = body.Places.Single(x => x.Source is FieldKoto f && f.NameKoto.IdentifierName == "s");
        var writes = body.Operations.Where(x => x.Kind == OwnershipOperationKind.Write && x.Place == s.Id).ToArray();
        Assert.Equal(PlacementKind.ConditionalReplacement, writes[^2].Placement);
        Assert.Equal(PlacementKind.Reinitialization, writes[^1].Placement);
        var conditional = body.CleanupSteps.Single(x => x.Place == s.Id && body.Operations[x.Operation].Placement == PlacementKind.ConditionalReplacement);
        Assert.Equal(CleanupAction.Conditional, conditional.Action);
        var self = body.CleanupSteps.Single(x => x.Place == s.Id && body.Operations[x.Operation].Placement == PlacementKind.Reinitialization);
        Assert.Equal(CleanupAction.Skip, self.Action);
    }

    [Fact]
    public void ExitCleanupFollowsDeclarationOrderAndSkipsMovedValues()
    {
        var c = Parse("func f(first: string, second: string)\n    let a = \"a\"\n    let b: string\n    b = \"b\"\n    writeLine(a)\n    return");
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        var body = c.Ownership.Bodies.Single(x => x.Function.Name == "f");
        var plan = body.CleanupPlans.First(x => x.Reason == CleanupReason.Return && x.Edge >= 0 && body.IsReachable(body.Edges[x.Edge].From));
        var steps = body.CleanupSteps.Skip(plan.Start).Take(plan.Count).Where(x => body.Places[x.Place].Kind != OwnershipPlaceKind.Temporary).ToArray();
        Assert.Equal(new[] { CleanupAction.Destroy, CleanupAction.Skip, CleanupAction.Destroy, CleanupAction.Destroy }, steps.Select(x => x.Action));
        Assert.Equal(new[] { OwnershipPlaceKind.Local, OwnershipPlaceKind.Local, OwnershipPlaceKind.Parameter, OwnershipPlaceKind.Parameter }, steps.Select(x => body.Places[x.Place].Kind));
    }

    [Fact]
    public void ReturnDuringLaterArgumentCleansEarlierTemporaryBeforeCalleeEntry()
    {
        var c = Parse("func makeText() -> string => \"a\"\nfunc f(a: string, b: i32) => ()\nfunc use(c: bool)\n    f(makeText(), if c => return else => 1)");
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        var body = c.Ownership.Bodies.Single(x => x.Function.Name == "use");
        var value = body.Places.Single(x => x.Kind == OwnershipPlaceKind.Temporary && x.Source is InvocationKoto call && call.BoundCall!.Target.Name == "makeText");
        Assert.Contains(body.CleanupSteps, x => x.Place == value.Id && x.Action == CleanupAction.Destroy && body.Operations[x.Operation].Source is ReturnKoto);
        Assert.Contains(body.Operations, x => x.Kind == OwnershipOperationKind.CallEntry && x.Place == value.Id);
        Assert.All(body.Edges.Where(x => x.Kind == OwnershipEdgeKind.Abort), x => Assert.Equal(OwnershipOperationKind.Exit, body.Operations[x.To].Kind));
    }

    [Fact]
    public void ReturnSecuresTheResultBeforeCleanupAndDelivery()
    {
        var c = Parse("func f(s: string) -> string\n    return s");
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        var body = c.Ownership.Bodies.Single(x => x.Function.Name == "f");
        var operations = body.Operations.ToArray();
        var secure = Array.FindIndex(operations, x => x.Kind == OwnershipOperationKind.Write && body.Places[x.Place].Kind == OwnershipPlaceKind.Result);
        var cleanup = Array.FindIndex(operations, x => x.Kind == OwnershipOperationKind.Cleanup);
        var deliver = Array.FindIndex(operations, x => x.Kind == OwnershipOperationKind.Deliver);
        Assert.True(secure < cleanup && cleanup < deliver);
    }

    [Fact]
    public void RebindInvalidatesAllVerification()
    {
        var c = Parse("let s = \"a\"\nwriteLine(s)");
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        var old = Body(c);
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, "let other: string\nwriteLine(other)");
        c.Bind();
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.False(old.IsVerified);
        Assert.Empty(c.Ownership.Bodies);
        Assert.False(c.Ownership.Analyze().IsVerified);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(32)]
    [InlineData(128)]
    public void WarmBindingAndBothAnalysesReuseStorage(int count)
    {
        var source = new System.Text.StringBuilder("func f(c: bool)\n");
        for (var i = 0; i < count; i++)
        {
            source.Append("    var s").Append(i).Append(" = \"a\"\n    if c\n        writeLine(s").Append(i).Append(")\n");
        }

        var c = Parse(source.ToString());
        for (var i = 0; i < 8; i++)
        {
            c.Bind();
            Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 4; i++)
        {
            c.Bind();
            c.Ownership.Analyze();
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Theory]
    [InlineData("let x: i32\nx = 1", true)]
    [InlineData("let x = 1\nx = 2", false)]
    [InlineData("let s: string\nwriteLine(s)", false)]
    [InlineData("let s = \"a\"\nwriteLine(s)\nwriteLine(s)", false)]
    [InlineData("let s = \"a\" + \"b\"", false)]
    [InlineData("writeLine(missing)", false)]
    public async Task ProjectBuildRequiresOwnershipVerification(string source, bool expected)
    {
        var c = Compilation.CreateForTest();
        c.Project.AddSource("ownership.kimi", source);
        Assert.Equal(expected, await c.Project.Build());
    }

    [Theory]
    [InlineData("func f(x: i32)\n    x = 2", DiagnosticCode.InvalidAssignment_Kd)]
    [InlineData("let x: i32\nfunc f()\n    x = 2", DiagnosticCode.InvalidCaptureBinding_Kd)]
    [InlineData("let x = 1\nx++", DiagnosticCode.InvalidAssignment_Kd)]
    public void StructuralAssignmentErrorsRemainInBinding(string source, DiagnosticCode code)
    {
        var c = Compilation.CreateForTest();
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, source);
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == code);
        Assert.False(c.Ownership.Analyze().IsVerified);
    }

    [Fact]
    public void StaticStringTemporaryHasCleanupResponsibility()
    {
        var c = Parse("\"discarded\"");
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        var body = Body(c);
        var literal = Assert.Single(body.Places, x => x.Source is StringLiteralKoto);
        Assert.Equal(OwnershipPlaceKind.Temporary, literal.Kind);
        Assert.Contains(body.CleanupSteps, x => x.Place == literal.Id && x.Action == CleanupAction.Destroy);
    }

    [Fact]
    public void BranchMoveMakesExitCleanupConditional()
    {
        var c = Parse("func f(c: bool)\n    let s = \"a\"\n    if c\n        writeLine(s)");
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        var body = c.Ownership.Bodies.Single(x => x.Function.Name == "f");
        var local = body.Places.Single(x => x.Kind == OwnershipPlaceKind.Local);
        Assert.Contains(body.CleanupSteps, x => x.Place == local.Id && x.Action == CleanupAction.Conditional);
    }

    [Fact]
    public void ScalarSelfAssignmentReplacesTheCopiedOldValue()
    {
        var c = Parse("var x = 1\nx = x");
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        var body = Body(c);
        var write = body.Operations.Last(x => x.Kind == OwnershipOperationKind.Write);
        Assert.Equal(PlacementKind.Replacement, write.Placement);
        Assert.Contains(body.CleanupSteps, x => x.Place == write.Place && x.Action == CleanupAction.Destroy);
    }

    [Fact]
    public void LoopTransferCleansInnerLocalAndKeepsOuterLocal()
    {
        var c = Parse("func f(c: bool)\n    let outer = \"a\"\n    while c\n        let inner = \"b\"\n        continue\n    writeLine(outer)");
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        var body = c.Ownership.Bodies.Single(x => x.Function.Name == "f");
        var plan = Assert.Single(body.CleanupPlans, x => x.Reason == CleanupReason.LoopTransfer);
        var local = Assert.Single(body.CleanupSteps.Skip(plan.Start).Take(plan.Count), x => body.Places[x.Place].Kind == OwnershipPlaceKind.Local);
        Assert.Equal("inner", ((FieldKoto)local.Source).NameKoto.IdentifierName);
        Assert.Equal(CleanupAction.Destroy, local.Action);
    }

    [Fact]
    public void ReanalysisClearsPreviousFlowResults()
    {
        var invalid = Parse("func f() -> i32\n    ()");
        var valid = Parse("let x = 1");
        var flow = invalid.AnalyzeControlFlow(invalid.Binding.TypeSystem);
        Assert.NotEmpty(flow.Issues);
        flow.Reanalyze(valid.Kotonoha.RootKoto);
        Assert.Empty(flow.Issues);
        Assert.Empty(flow.PendingBinding);
        Assert.DoesNotContain(flow.Nodes.Keys, x => ReferenceEquals(x, invalid.Kotonoha.RootKoto));
    }

    private static OwnershipBody Body(Compilation c) => c.Ownership.Bodies.Single(x => x.Function.IsGenerated);

    private static Compilation Parse(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare("x86_64-pc-windows-msvc"));
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, source);
        Assert.Empty(c.Kotonoha.DiagnosticCollection.GetArray());
        Assert.True(c.Bind().IsComplete, string.Join("\n", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node}")));
        return c;
    }

    private static string Describe(Compilation c)
        => string.Join("\n", c.Ownership.Issues.Select(x => $"{x.Failure}: {x.Source} {x.Source.GetType().Name} {x.Source.BoundType?.Kind}")) +
            string.Join("\n", c.Ownership.ControlFlow?.Issues.Select(x => x.Message) ?? []) +
            string.Join("\n", c.Ownership.ControlFlow?.PendingBinding.Select(x => $"pending: {x}") ?? []);
}
