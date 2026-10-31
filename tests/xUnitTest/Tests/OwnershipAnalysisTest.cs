// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class OwnershipAnalysisTest
{
    [Theory]
    [InlineData("Console.writeLine(\"Hello world\")")]
    [InlineData("public func main()\n    let message = \"Hello world\"\n    Console.writeLine(message)")]
    [InlineData("let x: i32\nx = 1\nlet y = x\nlet z = x")]
    [InlineData("var s = \"a\"\nConsole.writeLine(s)\ns = \"b\"\nConsole.writeLine(s)")]
    [InlineData("func echo(x: string) -> string => x@move\nConsole.writeLine(echo(\"x\"))")]
    [InlineData("func echo(x: string) -> string\n    return x@move\nConsole.writeLine(echo(\"x\"))")]
    [InlineData("let s = \"a\"\nConsole.writeLine(s)\nConsole.writeLine(s)")]
    [InlineData("func f(c: bool)\n    let s: string\n    if c\n        s = \"a\"\n    else\n        s = \"b\"\n    Console.writeLine(s)")]
    [InlineData("func f(c: bool)\n    while c\n        let s = \"a\"\n        Console.writeLine(s)")]
    [InlineData("func f(c: bool)\n    while c\n        let s = \"a\"\n        continue")]
    [InlineData("func f()\n    while true\n        let s = \"a\"\n        exit")]
    [InlineData("func f()\n    while true\n        ()")]
    [InlineData("let s = \"a\"\nif s == \"a\"\n    Console.writeLine(s)")]
    [InlineData("#if false\nfunc excluded()\n    defer => Console.writeLine(\"later\")\nConsole.writeLine(\"selected\")")]
    [InlineData("func sink<T>(x: T) => ()\nfunc twice<T>(x: T)\n    T is Copy\n    sink(x)\n    sink(x)")]
    [InlineData("defer => Console.writeLine(\"later\")")]
    public void SupportedProgramsVerify(string source)
    {
        var c = Parse(source);
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
    }

    [Theory]
    [InlineData("let s: string\nConsole.writeLine(s)", DiagnosticCode.UninitializedPlace_Kd)]
    [InlineData("let s = \"a\"\n_ = s@move\nConsole.writeLine(s)", DiagnosticCode.MovedPlace_Kd)]
    [InlineData("let s = \"a\"\n_ = s@move\nif s == \"x\"\n    ()", DiagnosticCode.MovedPlace_Kd)]
    [InlineData("let s = \"a\"\nlet taken = s", DiagnosticCode.TransferRequired_Kd)]
    [InlineData("let s = \"a\"\ns = \"b\"", DiagnosticCode.ReassignedLet_Kd)]
    [InlineData("let s = \"a\"\nConsole.writeLine(s)\ns = \"b\"", DiagnosticCode.ReassignedLet_Kd)]
    [InlineData("func f(c: bool)\n    var s: string\n    if c\n        s = \"a\"\n    Console.writeLine(s)", DiagnosticCode.UninitializedPlace_Kd)]
    [InlineData("func f(c: bool)\n    let s = \"a\"\n    if c\n        _ = s@move\n    Console.writeLine(s)", DiagnosticCode.MovedPlace_Kd)]
    [InlineData("func f(c: bool)\n    let s: string\n    if c\n        s = \"a\"\n    s = \"b\"", DiagnosticCode.ReassignedLet_Kd)]
    [InlineData("func f(c: bool)\n    let s = \"a\"\n    while c\n        _ = s@move", DiagnosticCode.MovedPlace_Kd)]
    [InlineData("func f(c: bool)\n    let s: string\n    while c\n        s = \"a\"", DiagnosticCode.ReassignedLet_Kd)]
    [InlineData("let s: string\nif true\n    s = \"a\"\nConsole.writeLine(s)", DiagnosticCode.UninitializedPlace_Kd)]
    public void StateErrorsAreDiagnosedAfterConvergence(string source, DiagnosticCode failure)
    {
        var c = Parse(source);
        Assert.False(c.Analyze().IsVerified);
        Assert.Contains(c.Problems, x => x.Code == failure);
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidAssignment_Kd);
    }

    [Theory]
    [InlineData("let moved = (\"first\", \"last\").0")]
    public void UnsupportedOwnershipCannotBecomeVerified(string source)
    {
        var c = Parse(source);
        var result = c.Analyze();
        Assert.False(result.IsVerified);
        Assert.Contains(c.Problems, x => x.Code == DiagnosticCode.Unsupported_Kd);
    }

    [Fact]
    public void OwnedDefaultsNowVerifyThroughTheOrdinaryPipeline()
    {
        var c = Parse("func f(x: string = \"x\") => ()\nf()");
        var result = c.Analyze();
        Assert.True(result.IsVerified, MinimalEmissionTest.Describe(c));
    }

    [Fact]
    public void ImplicitBorrowIsRecordedAndChecksMovedState()
    {
        var c = Parse("func show(s: ref/string) => ()\nlet s = \"a\"\n_ = s@move\nshow(s)");
        c.Analyze();
        Assert.Contains(c.Problems, x => x.Code == DiagnosticCode.MovedPlace_Kd);
        Assert.Contains(Body(c).Operations, x => x.Use == PlaceUseKind.Borrow);
    }

    [Theory]
    [InlineData("func sink<T>(x: T) => ()\nfunc twice<T>(x: T)\n    sink(x@move)\n    sink(x@move)", false)]
    [InlineData("func sink<T>(x: T) => ()\nfunc once<T>(x: T)\n    sink(x@move)", true)]
    public void UnknownCopyIsCheckedAtTheGenericDefinition(string source, bool valid)
    {
        var c = Parse(source);
        var result = c.Analyze();
        // The Copy-unproven parameter keeps its generic Type; its x@move transfer is checked as a Move (SPEC 13.5.3; its MIR:
        // tests/mir/generics.kimi `wrapSome`).
        Assert.True(valid == result.IsVerified, MinimalEmissionTest.Describe(c));
        if (!valid)
        {
            Assert.Contains(c.Problems, x => x.Code == DiagnosticCode.MovedPlace_Kd);
        }
    }

    [Theory]
    [InlineData("func sink<T>(x: T) => ()\nfunc once<T>(x: T)\n    sink(x)")]
    [InlineData("func once<T>(x: T) -> T => x")]
    public void BareCopyUnprovenGenericValueRequiresATransfer(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare("x86_64-pc-windows-msvc"));
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, source);
        var bound = c.Bind().IsComplete;
        Assert.False(bound && c.Analyze().IsVerified);
        Assert.True(
            c.Binding.Issues.Any(x => x.Code == DiagnosticCode.TransferRequired_Kd) || c.Problems.Any(x => x.Code == DiagnosticCode.TransferRequired_Kd),
            string.Join("\n", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node}")) + MinimalEmissionTest.Describe(c));
    }

    [Fact]
    public void ConditionalReplacementUsesPostRhsState()
    {
        var c = Parse("func f(c: bool)\n    var s: string\n    if c\n        s = \"a\"\n    s = \"b\"\n    s = s@move");
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
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
        var c = Parse("func f(first: string, second: string)\n    let a = \"a\"\n    let b: string\n    b = \"b\"\n    _ = a@move\n    return");
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
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
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        var body = c.Ownership.Bodies.Single(x => x.Function.Name == "use");
        var value = body.Places.Single(x => x.Kind == OwnershipPlaceKind.Temporary && x.Source is InvocationKoto call && call.CallOf()!.Target.Name == "makeText");
        Assert.Contains(body.CleanupSteps, x => x.Place == value.Id && x.Action == CleanupAction.Destroy && body.Operations[x.Operation].Source is ReturnKoto);
        Assert.Contains(body.Operations, x => x.Kind == OwnershipOperationKind.CallEntry && x.Place == value.Id);
        Assert.All(body.Edges.Where(x => x.Kind == OwnershipEdgeKind.Abort), x => Assert.Equal(OwnershipOperationKind.Exit, body.Operations[x.To].Kind));
    }

    // A return secures its result before the cleanup and the delivery (its MIR: tests/mir/scopes.kimi `securedReturn`).
    [Fact]
    public void ReturnSecuresTheResultBeforeCleanupAndDelivery()
    {
        var c = Parse("func f(s: string) -> string\n    return s@move");
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
    }

    // A return inside an argument destroys the inner local before the outer temporary (its MIR: tests/mir/scopes.kimi
    // `returnInArgument`), whatever the newlines.
    [Theory]
    [InlineData("\n", false)]
    [InlineData("\r\n", false)]
    [InlineData("\r", false)]
    [InlineData("\n", true)]
    [InlineData("\r\n", true)]
    [InlineData("\r", true)]
    public void ReturnInArgumentCleansInnerLocalBeforeOuterTemporary(string newline, bool trailingNewline)
    {
        var source = "func makeText() -> string => \"outer\"\nfunc consume(a: string, b: i32) => ()\nfunc use(c: bool)\n    consume(\n        makeText(),\n        if c\n            let inner = \"inner\"\n            return\n        else => 1\n    )";
        var c = Parse(source.Replace("\n", newline) + (trailingNewline ? newline : string.Empty));
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        if (newline != "\n" || trailingNewline)
        {
            Assert.Equal(MirDumpTest.Dump(Parse(source)), MirDumpTest.Dump(c));
        }
    }

    [Fact]
    public void RebindInvalidatesAllVerification()
    {
        var c = Parse("let s = \"a\"\nConsole.writeLine(s)");
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, "let other: string\nConsole.writeLine(other)");
        c.Bind();
        Assert.False(c.Analysis.IsVerified);
        Assert.Equal(0, c.Analysis.BodyCount);
        Assert.False(c.Analyze().IsVerified);
    }

    [Fact]
    public void BorrowedArgumentDoesNotMoveItsSource()
    {
        var c = Parse("func show(s: ref/string) => ()\nlet s = \"a\"\nshow(s)\nshow(s)");
        c.Analyze();
        Assert.DoesNotContain(c.Problems, x => x.Code != DiagnosticCode.Unsupported_Kd);
        Assert.Empty(c.Problems);
        Assert.DoesNotContain(Body(c).Operations, x => x.Kind == OwnershipOperationKind.CallEntry && Body(c).Places[x.Place].Kind != OwnershipPlaceKind.Temporary);
    }

    [Fact]
    public void BodylessRequirementsAreNotOwnershipBodies()
    {
        var c = Parse("contract Named\n    func name(self: ref/Self) -> i32\nConsole.writeLine(\"x\")");
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        Assert.Equal(1, c.Analysis.BodyCount);
    }

    [Fact]
    public void InputStatesReplayWithinBlocks()
    {
        var c = Parse("func f(c: bool)\n    let s = \"a\"\n    if c\n        _ = s@move\n    let t = 1");
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        var body = c.Ownership.Bodies.Single(x => x.Function.Name == "f");
        var s = body.Places.Single(x => x.Source is FieldKoto { NameKoto.IdentifierName: "s" }).Id;
        var consume = body.Operations.ToList().FindIndex(x => x.Kind == OwnershipOperationKind.Consume && x.Place == s);
        Assert.Equal(PlaceState.MustInit | PlaceState.MayInit | PlaceState.MayAssigned | PlaceState.MayOwn, body.GetInputState(consume, s));
        var last = body.Operations.ToList().FindLastIndex(x => x.Kind == OwnershipOperationKind.Deliver);
        Assert.Equal(PlaceState.None, body.GetInputState(last, s) & PlaceState.MustInit);
        Assert.Equal(PlaceState.MayMoved, body.GetInputState(last, s) & PlaceState.MayMoved);
    }

    [Theory]
    [InlineData("let x: i32\nx = 1", true)]
    [InlineData("let x = 1\nx = 2", false)]
    [InlineData("let s: string\nConsole.writeLine(s)", false)]
    [InlineData("let s = \"a\"\nConsole.writeLine(s)\nConsole.writeLine(s)", true)]
    [InlineData("let s = \"a\"\nlet taken = s@move\nConsole.writeLine(s)", false)]
    [InlineData("let moved = (\"first\", \"last\").0", false)]
    [InlineData("Console.writeLine(missing)", false)]
    public async Task ProjectBuildRequiresOwnershipVerification(string source, bool expected)
    {
        var c = Compilation.CreateForTest();
        c.Project.AddSource("ownership.kimi", source);
        Assert.Equal(expected, await c.Project.Check(TestContext.Current.CancellationToken));
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
        Assert.False(c.Analyze().IsVerified);
    }

    [Fact]
    public void StaticStringTemporaryHasCleanupResponsibility()
    {
        var c = Parse("\"discarded\"");
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        var body = Body(c);
        var literal = Assert.Single(body.Places, x => x.Source is StringLiteralKoto);
        Assert.Equal(OwnershipPlaceKind.Temporary, literal.Kind);
        Assert.Contains(body.CleanupSteps, x => x.Place == literal.Id && x.Action == CleanupAction.Destroy);
    }

    [Fact]
    public void BranchMoveMakesExitCleanupConditional()
    {
        var c = Parse("func f(c: bool)\n    let s = \"a\"\n    if c\n        _ = s@move");
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        var body = c.Ownership.Bodies.Single(x => x.Function.Name == "f");
        var local = body.Places.Single(x => x.Kind == OwnershipPlaceKind.Local);
        Assert.Contains(body.CleanupSteps, x => x.Place == local.Id && x.Action == CleanupAction.Conditional);
    }

    [Fact]
    public void ScalarSelfAssignmentReplacesTheCopiedOldValue()
    {
        var c = Parse("var x = 1\nx = x");
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        var body = Body(c);
        var write = body.Operations.Last(x => x.Kind == OwnershipOperationKind.Write);
        Assert.Equal(PlacementKind.Replacement, write.Placement);
        Assert.Contains(body.CleanupSteps, x => x.Place == write.Place && x.Action == CleanupAction.Destroy);
    }

    [Fact]
    public void LoopTransferCleansInnerLocalAndKeepsOuterLocal()
    {
        var c = Parse("func f(c: bool)\n    let outer = \"a\"\n    while c\n        let inner = \"b\"\n        continue\n    Console.writeLine(outer)");
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        var body = c.Ownership.Bodies.Single(x => x.Function.Name == "f");
        var plan = Assert.Single(body.CleanupPlans, x => x.Reason == CleanupReason.LoopTransfer);
        var local = Assert.Single(body.CleanupSteps.Skip(plan.Start).Take(plan.Count), x => body.Places[x.Place].Kind == OwnershipPlaceKind.Local);
        Assert.Equal("inner", ((FieldKoto)local.Source).NameKoto.IdentifierName);
        Assert.Equal(CleanupAction.Destroy, local.Action);
    }

    [Fact]
    public void ReanalysisClearsPreviousFlowResults()
    {
        var invalid = Parse("func f()\n    require true else => ()\n    ()");
        var valid = Parse("let x = 1");
        var flow = invalid.AnalyzeControlFlow();
        Assert.NotEmpty(flow.Issues);
        flow.Reanalyze(valid.Kotonoha.RootKoto);
        Assert.Empty(flow.Issues);
        Assert.Empty(flow.PendingBinding);
        Assert.DoesNotContain(flow.Nodes.Keys, x => ReferenceEquals(x, invalid.Kotonoha.RootKoto));
    }

    private static OwnershipBody Body(Compilation c) => c.Ownership.Bodies.Single(x => x.Function.IsGenerated);

    private static Compilation Parse(string source)
        => CompilationTestHelper.BindSuccess(source);

    [Collection<AllocationIsolationCollection>]
    [Trait("Purpose", "Allocation")]
    public class AllocationTests
    {
        [Theory]
        [InlineData(1)]
        [InlineData(32)]
        [InlineData(128)]
        public void WarmBindingAndBothAnalysesReuseStorage(int count)
        {
            var source = new System.Text.StringBuilder("func f(c: bool)\n");
            for (var i = 0; i < count; i++)
            {
                source.Append("    var s").Append(i).Append(" = \"a\"\n    if c\n        Console.writeLine(s").Append(i).Append(")\n");
            }

            var c = Parse(source.ToString());
            c.Bind();
            Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
            var phases = new long[2 * (AllocationMeasurement.WarmupOperations + AllocationMeasurement.MeasuredOperations)];
            var phase = 0;
            var allocated = AllocationMeasurement.Measure(() =>
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                c.Bind();
                phases[phase++ % phases.Length] = GC.GetAllocatedBytesForCurrentThread() - before;
                before = GC.GetAllocatedBytesForCurrentThread();
                c.Analyze();
                phases[phase++ % phases.Length] = GC.GetAllocatedBytesForCurrentThread() - before;
            });
            Assert.True(allocated == 0, $"Allocated {allocated}; Binding/Ownership phases of the last 16 operations: {string.Join(',', phases)}");
        }
    }
}
