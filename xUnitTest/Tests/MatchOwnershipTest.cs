// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

[TestClass(DisableParallelization = true)]
public class MatchOwnershipTest
{
    [Theory]
    [InlineData("func f(x: Option<i32>) -> i32 => match x\n    .Some(let n) => n\n    .None => 0")]
    [InlineData("func f(x: Option<i32>) -> i32\n    return match x\n        .Some(let n)\n            yield n\n        .None\n            yield 0")]
    [InlineData("func f(x: Option<string>) => match x\n    .Some(let text) => writeLine(text)\n    .None => ()")]
    [InlineData("func f(x: Option<Option<string>>) => match x\n    .Some(.Some(let text)) => writeLine(text)\n    .Some(_) => ()\n    .None => ()")]
    [InlineData("func f(x: Option<string>) -> string => match x\n    .Some(let s) => s\n    .None => \"empty\"")]
    [InlineData("func f(x: Option<string>) -> string => match x\n    .Some(let s)\n        return s\n    .None\n        return \"empty\"")]
    [InlineData("func f(x: Option<Option<i32>>) -> i32 => match x\n    .Some(let inner) => match inner\n        .Some(let n) => n\n        .None => 0\n    .None => 0")]
    [InlineData("func f(x: bool) -> i32 => match x\n    true => 1\n    false => 0")]
    [InlineData("func f(x: ()) -> i32 => match x\n    () => 1")]
    [InlineData("func f(x: string) -> i32 => match x\n    \"yes\" => 1\n    _ => 0")]
    [InlineData("func f(x: u128) -> i32 => match x\n    340282366920938463463374607431768211455 => 1\n    _ => 0")]
    [InlineData("func f(x: char) -> i32 => match x\n    'A' => 1\n    _ => 0")]
    [InlineData("func f(x: Option<i8>) -> i8 => match x\n    (Option<i8>.Some(let n,)) => n\n    (::Core.Option<i8>.None) => -128")]
    [InlineData("func f(x: Option<i32>) -> i32 => match x\n    .Some(var n)\n        n = 2\n        yield n\n    .None\n        yield 0")]
    [InlineData("func f()\n    match Option<string>.Some(\"text\")\n        .None\n            ()\n        .Some(_) => ()")]
    [InlineData("func f(c: bool)\n    while c\n        match Option<string>.Some(\"text\")\n            .Some(let s)\n                writeLine(s)\n                continue\n            .None\n                exit")]
    [InlineData("func f(c: bool, x: Option<string>)\n    match x\n        .Some(let s)\n            while c\n                continue\n            writeLine(s)\n        .None\n            ()")]
    [InlineData("func f(x: string) => match x\n    let s => writeLine(s)")]
    [InlineData("func f(x: Option<i32>)\n    match x\n        let copy\n            ()\n    let again = x")]
    [InlineData("enum E\n    One(string)\n    Two(string, string)\nfunc f(x: E) -> string => match x\n    .One(let a) => a\n    .Two(_, let b) => b")]
    [InlineData("func f(x: Option<string>) -> Option<string> => match x\n    .Some(let s) => .Some(s)\n    .None => .None")]
    [InlineData("func f(x: bool) -> i32 => match x\n    true => match (yield 1)\n        _ => 2\n    false => 0")]
    public void SupportedMatchesVerify(string source)
    {
        var c = Parse(source);
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
    }

    [Theory]
    [InlineData("func f<T>(x: T) => match x\n    let value => ()")]
    [InlineData("func f<T>(x: T) => match x\n    _ => ()")]
    [InlineData("func f(x: ref/i32 from static) => match x\n    let r => ()")]
    [InlineData("func f<T>(x: ref/T) => match x\n    let r => ()")]
    [InlineData("func f(x: (i32, i32)) => match x\n    (let a, _) => ()")]
    [InlineData("func f(x: Option<(i32, i32)>) => match x\n    .Some((let a, _)) => ()\n    .None => ()")]
    public void BoundButUnsupportedMatchCannotVerify(string source)
    {
        var c = Parse(source);
        Assert.True(c.Binding.Result.IsComplete);
        var result = c.Ownership.Analyze();
        Assert.False(result.IsVerified);
        Assert.True(result.UnsupportedCount > 0, Describe(c));
        Assert.Contains(c.Ownership.Issues, i => i.Failure == OwnershipFailure.Unsupported && i.Source is MatchKoto);
    }

    [Theory]
    [InlineData("func f(s: string)\n    match s\n        _\n            ()\n    writeLine(s)")]
    [InlineData("func f(s: string)\n    match s\n        \"yes\"\n            ()\n        _ => ()\n    writeLine(s)")]
    [InlineData("func f(x: Option<string>)\n    match x\n        .Some(let s)\n            writeLine(s)\n            writeLine(s)\n        .None\n            ()")]
    public void AcquisitionStillRejectsMovedUses(string source)
    {
        var c = Parse(source);
        Assert.False(c.Ownership.Analyze().IsVerified);
        Assert.Contains(c.Ownership.Issues, i => i.Failure == OwnershipFailure.PossiblyMovedUse);
    }

    [Theory]
    [InlineData("fail()", 1)]
    [InlineData("(fail())", 1)]
    [InlineData("if true => fail() else => fail()", 2)]
    public void NeverSubjectCreatesNoValueOrArmStorage(string subject, int calls)
    {
        var c = Parse($"func fail() -> Never => fail()\nfunc f() -> i32 => match ({subject})\n    _ => 1");
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        var body = Body(c);
        Assert.Equal(calls, body.Operations.Count(o => o.Kind == OwnershipOperationKind.Call));
        Assert.DoesNotContain(body.Places, p => p.Kind == OwnershipPlaceKind.Subject);
        Assert.Empty(body.Matches);
    }

    [Fact]
    public void TestsReadAnIntactSubjectBeforeAnyAcquisition()
    {
        var c = Parse("enum E\n    Pair(string, i32)\nfunc f(x: E) => match x\n    .Pair(let s, 0) => writeLine(s)\n    .Pair(_, _) => ()");
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        var body = Body(c);
        var match = Assert.Single(body.Matches);
        var dispatch = body.Operations.Select((o, i) => (o, i)).Single(x => x.o.Kind == OwnershipOperationKind.MatchDispatch).i;
        Assert.Equal(2, body.Edges.Count(e => e.From == dispatch && e.Kind == OwnershipEdgeKind.MatchArm));
        Assert.DoesNotContain(body.Edges, e => e.From == dispatch && e.Kind == OwnershipEdgeKind.Unmatched);
        foreach (var arm in body.MatchArms)
        {
            Assert.True(body.IsReachable(arm.Test));
            var state = body.GetInputState(arm.Test, match.Subject);
            Assert.True((state & PlaceState.MustInit) != 0);
            Assert.False((state & PlaceState.MayMoved) != 0);
        }

        Assert.Single(body.Operations, o => o.Kind == OwnershipOperationKind.AcquirePattern);
        Assert.Single(body.Decompositions);
        Assert.Empty(body.Constructions);
    }

    [Fact]
    public void CopyBindingAlsoHasADecomposedInitializedInput()
    {
        var c = Parse("func f(x: Option<i32>) -> i32 => match x\n    .Some(let n) => n\n    .None => 0");
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        var body = Body(c);
        var acquire = body.Operations.Select((o, i) => (o, i)).Single(x => x.o.Kind == OwnershipOperationKind.AcquirePattern);
        Assert.Equal(AcquisitionKind.Copy, acquire.o.Acquisition);
        Assert.Equal(OwnershipPlaceKind.Payload, body.Places[acquire.o.Place].Kind);
        Assert.True((body.GetInputState(acquire.i, acquire.o.Place) & PlaceState.MustInit) != 0);
        Assert.Contains(body.CleanupSteps, s => s.Place == acquire.o.Place && s.Action == CleanupAction.Destroy);
    }

    [Fact]
    public void CleanupUsesReverseStructureAfterBodyBindings()
    {
        var c = Parse("enum E\n    Pair(string, string)\nfunc f(x: E) => match x\n    .Pair(let a, _) => ()");
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        var body = Body(c);
        var match = Assert.Single(body.Matches);
        var split = Assert.Single(body.Decompositions);
        var local = Assert.Single(body.Places, p => p.Kind == OwnershipPlaceKind.Local);
        var cleanup = body.CleanupSteps.Where(s => ReferenceEquals(body.Operations[s.Operation].Source, match.Binding.Syntax)).ToArray();
        Assert.Equal([split.PayloadStart + 1, split.PayloadStart, match.Subject], cleanup.Where(s => s.Place == match.Subject || body.Places[s.Place].Kind == OwnershipPlaceKind.Payload).Select(s => s.Place));
        Assert.Equal([local.Id, split.PayloadStart + 1], cleanup.Where(s => s.Action == CleanupAction.Destroy).Select(s => s.Place));
        Assert.Contains(cleanup, s => s.Place == match.Subject && s.Action == CleanupAction.Skip);
    }

    [Fact]
    public void NestedCleanupDoesNotFollowProjectionCreationOrder()
    {
        var c = Parse("enum Inner\n    Pair(string, string)\nenum Outer\n    Pair(Inner, string)\nfunc f(x: Outer) => match x\n    .Pair(.Pair(let a, _), _) => ()\n    .Pair(_, _) => ()");
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        var body = Body(c);
        var match = Assert.Single(body.Matches);
        var outer = body.Decompositions[0];
        var inner = body.Decompositions[1];
        var local = Assert.Single(body.Places, p => p.Kind == OwnershipPlaceKind.Local);
        var cleanup = body.CleanupPlans.First(p => p.Reason == CleanupReason.ScopeExit);
        var destroyed = body.CleanupSteps.Skip(cleanup.Start).Take(cleanup.Count).Where(s => s.Action == CleanupAction.Destroy).Select(s => s.Place);
        Assert.Equal([local.Id, outer.PayloadStart + 1, inner.PayloadStart + 1], destroyed);
    }

    [Fact]
    public void WarningCoveredArmParticipatesInMovedStateJoin()
    {
        var c = Parse("func f(x: i32)\n    let s = \"text\"\n    match x\n        _\n            ()\n        0\n            writeLine(s)\n    writeLine(s)");
        Assert.Single(c.Binding.PatternWarnings);
        Assert.False(c.Ownership.Analyze().IsVerified);
        var body = Body(c);
        var s = body.Places.Single(p => p.Source is FieldKoto f && f.NameKoto.IdentifierName == "s");
        var use = body.Operations.Select((o, i) => (o, i)).Last(x => x.o.Kind == OwnershipOperationKind.Consume && x.o.Place == s.Id);
        Assert.True((body.GetInputState(use.i, s.Id) & PlaceState.MayMoved) != 0);
        Assert.All(body.MatchArms, arm => Assert.True(body.IsReachable(arm.Test)));
        Assert.Contains(body.Issues, i => i.Failure == OwnershipFailure.PossiblyMovedUse);
    }

    [Fact]
    public void SubjectAndPayloadsHaveFreshLifetimesInsideLoops()
    {
        var c = Parse("func f(c: bool)\n    while c\n        match Option<string>.Some(\"text\")\n            .Some(let s)\n                writeLine(s)\n            .None\n                ()");
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        var body = Body(c);
        var match = Assert.Single(body.Matches);
        var split = Assert.Single(body.Decompositions);
        foreach (var place in new[] { match.Subject, split.PayloadStart })
        {
            Assert.Contains(body.Operations, o => o.Kind == OwnershipOperationKind.Declare && o.Place == place);
        }

        var open = body.Operations.Select((o, i) => (o, i)).Single(x => x.o.Kind == OwnershipOperationKind.DecomposeCase);
        Assert.False((body.GetInputState(open.i, match.Subject) & PlaceState.MayMoved) != 0);
        Assert.Equal(PlaceState.None, body.GetInputState(open.i, split.PayloadStart));
        var acquire = body.Operations.Select((o, i) => (o, i)).Single(x => x.o.Kind == OwnershipOperationKind.AcquirePattern);
        Assert.False((body.GetInputState(acquire.i, split.PayloadStart) & PlaceState.MayMoved) != 0);
        Assert.DoesNotContain(body.CleanupPlans.Where(p => p.Reason == CleanupReason.Replacement).SelectMany(p => body.CleanupSteps.Skip(p.Start).Take(p.Count)), s => s.Place == match.Subject || s.Place == split.PayloadStart);
    }

    [Fact]
    public void SubjectIsEvaluatedOnceAndMaterializedWithoutAnotherConsume()
    {
        var c = Parse("func make() -> Option<string> => .Some(\"text\")\nfunc f() => match make()\n    .Some(let s) => writeLine(s)\n    .None => ()");
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        var body = Body(c);
        var initialize = Assert.Single(body.Operations, o => o.Kind == OwnershipOperationKind.InitializeSubject);
        Assert.Single(body.Operations, o => o.Kind == OwnershipOperationKind.Call && o.Source is InvocationKoto call && call.BoundCall!.Target.Name == "make");
        Assert.DoesNotContain(body.Operations, o => o.Kind == OwnershipOperationKind.Consume && o.Place == initialize.Input);
        Assert.All(body.CleanupSteps.Where(s => s.Place == initialize.Input), s => Assert.Equal(CleanupAction.Skip, s.Action));
    }

    [Theory]
    [InlineData("return a", CleanupReason.Return)]
    [InlineData("yield a", CleanupReason.SelectionResult)]
    public void TransfersSecureValueBeforeBodyAndSubjectCleanup(string transfer, CleanupReason reason)
    {
        var c = Parse($"enum E\n    Pair(string, string)\nfunc f(c: bool, x: E) -> string => match x\n    .Pair(let a, _)\n        let local = \"body\"\n        {transfer}");
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        var body = Body(c);
        var split = Assert.Single(body.Decompositions);
        var local = body.Places.Single(p => p.Source is FieldKoto f && f.NameKoto.IdentifierName == "local");
        var cleanup = body.CleanupPlans.Single(p => p.Reason == reason && p.Edge >= 0 && body.IsReachable(body.Edges[p.Edge].From));
        var steps = body.CleanupSteps.Skip(cleanup.Start).Take(cleanup.Count).ToArray();
        Assert.Equal(OwnershipOperationKind.Write, body.Operations[body.Edges[cleanup.Edge].From].Kind);
        Assert.Equal([local.Id, split.PayloadStart + 1], steps.Where(s => s.Action == CleanupAction.Destroy && body.Places[s.Place].Type.Name == "string").Select(s => s.Place));
        Assert.Contains(steps, s => s.Place == split.PayloadStart && s.Action == CleanupAction.Skip);
    }

    [Fact]
    public void DeepReturnCleansActiveDecompositionBeforeOuterTemporary()
    {
        var c = Parse("enum E\n    Pair(string, string)\nfunc make() -> string => \"outer\"\nfunc consume(a: string, b: string) => ()\nfunc f(c: bool, x: E)\n    consume(\n        make(),\n        match x\n            .Pair(let a, _)\n                if c\n                    let local = \"inner\"\n                    return\n                yield a\n    )");
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        var body = Body(c);
        var split = Assert.Single(body.Decompositions);
        var patternLocal = body.Places.Single(p => p.Kind == OwnershipPlaceKind.Local && p.Source is SyntaxFormKoto { Akind: KotoKind.BindingPattern });
        var inner = body.Places.Single(p => p.Source is FieldKoto f && f.NameKoto.IdentifierName == "local");
        var outer = body.Places.Single(p => p.Source is InvocationKoto call && call.BoundCall!.Target.Name == "make");
        var steps = body.CleanupSteps.Where(s => body.IsReachable(s.Operation) && body.Operations[s.Operation].Source is ReturnKoto && s.Action == CleanupAction.Destroy && body.Places[s.Place].Type.Name == "string");
        Assert.Equal([inner.Id, patternLocal.Id, split.PayloadStart + 1, outer.Id], steps.Select(s => s.Place));
    }

    [Fact]
    public void NestedSubjectsUseTheirOwnActiveCaseDuringReturn()
    {
        var c = Parse("enum E\n    Pair(string, string)\nfunc f(x: E, y: E) -> string => match x\n    .Pair(let a, _) => match y\n        .Pair(let b, _)\n            return b");
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        var body = Body(c);
        Assert.Equal(2, body.Matches.Count);
        Assert.Equal(2, body.Decompositions.Count);
        var first = body.Decompositions[0];
        var second = body.Decompositions[1];
        var a = body.Places.Single(p => p.Kind == OwnershipPlaceKind.Local && p.Source is SyntaxFormKoto { BoundSymbol.Name: "a" });
        var steps = body.CleanupSteps.Where(s => body.IsReachable(s.Operation) && body.Operations[s.Operation].Source is ReturnKoto && s.Action == CleanupAction.Destroy && body.Places[s.Place].Type.Name == "string");
        Assert.Equal([second.PayloadStart + 1, a.Id, first.PayloadStart + 1], steps.Select(s => s.Place));
    }

    [Fact]
    public void OuterLoopContinueCleansTheSelectedSubject()
    {
        var c = Parse("func f(c: bool)\n    while c\n        match Option<string>.Some(\"text\")\n            .Some(let s)\n                continue\n            .None\n                ()");
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        var body = Body(c);
        var local = body.Places.Single(p => p.Kind == OwnershipPlaceKind.Local && p.Source is SyntaxFormKoto { Akind: KotoKind.BindingPattern });
        var cleanup = body.CleanupPlans.Single(p => p.Reason == CleanupReason.LoopTransfer);
        var steps = body.CleanupSteps.Skip(cleanup.Start).Take(cleanup.Count).ToArray();
        Assert.Contains(steps, s => s.Place == local.Id && s.Action == CleanupAction.Destroy);
        Assert.Contains(steps, s => s.Place == body.Matches[0].Subject && s.Action == CleanupAction.Skip);
    }

    [Fact]
    public void DiscardedMatchStillRequiresExhaustiveness()
    {
        var c = Compilation.CreateForTest();
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, "func f(x: Option<string>)\n    match x\n        .Some(let s) => writeLine(s)");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, i => i.Code == DiagnosticCode.NonExhaustiveMatch_Kd);
        Assert.False(c.Ownership.Analyze().IsVerified);
    }

    [Fact]
    public void SubjectEvaluationTemporariesKeepTheirExpressionLifetime()
    {
        var c = Parse("func make() -> string => \"temporary\"\nfunc consume(a: i32, b: i32) => ()\nfunc f()\n    consume(\n        2,\n        match make() == \"expected\"\n            true => 1\n            false => 0\n    )");
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        var body = Body(c);
        var inputTemporary = body.Places.Single(p => p.Source is InvocationKoto call && call.BoundCall!.Target.Name == "make");
        var destroy = Assert.Single(body.CleanupSteps, s => s.Place == inputTemporary.Id && s.Action == CleanupAction.Destroy);
        var consume = body.Operations.Select((o, i) => (o, i)).Single(x => x.o.Kind == OwnershipOperationKind.Call && x.o.Source is InvocationKoto call && call.BoundCall!.Target.Name == "consume");
        Assert.True(destroy.Operation > consume.i);
    }

    [Fact]
    public void AbortAfterAcquisitionHasNoReachableCleanup()
    {
        var c = Parse("func fail() -> Never => fail()\nfunc f(x: Option<string>) => match x\n    .Some(let s) => fail()\n    .None => fail()");
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        var body = Body(c);
        Assert.All(body.CleanupSteps, s => Assert.False(body.IsReachable(s.Operation)));
        Assert.Equal(2, body.Edges.Count(e => e.Kind == OwnershipEdgeKind.Abort));
    }

    [Fact]
    public void YieldToIfIsNotRedirectedIntoTheMatchFrame()
    {
        var c = Parse("func f(x: bool) -> i32 => match x\n    true => (if x\n        yield 1\n    else\n        yield 2\n    )\n    false => 0");
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        Assert.Empty(c.Ownership.Issues);
        Assert.All(c.Ownership.ControlFlow!.Targets.Where(t => t.Key is YieldKoto), t => Assert.IsType<IfKoto>(t.Value));
        Assert.All(c.Ownership.ControlFlow!.Targets.Keys, t => Assert.IsType<YieldKoto>(t));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void MatchMayPrecedeAnotherCallArgument(string newline)
    {
        var source = "func consume(a: i32, b: i32) => ()\nfunc f(x: bool)\n    consume(\n        match x\n            true => 1\n            false => 0\n        , 2\n    )".Replace("\n", newline, StringComparison.Ordinal);
        var c = Parse(source);
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        Assert.Single(Body(c).Matches);
        Assert.Single(Body(c).Operations, o => o.Kind == OwnershipOperationKind.Call);
    }

    [Fact]
    public void DedentDoesNotReplaceTheRequiredArgumentComma()
    {
        var c = Compilation.CreateForTest();
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, "func f(x: bool)\n    consume(\n        match x\n            true => 1\n            false => 0\n        2\n    )");
        Assert.NotEmpty(c.Kotonoha.DiagnosticCollection.GetArray());
    }

    [Fact]
    public void BodyBindingsAreAcquiredLeftToRightAndCleanedInReverse()
    {
        var c = Parse("enum E\n    Pair(string, string)\nfunc f(x: E) => match x\n    .Pair(let a, let b) => ()");
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        var body = Body(c);
        var acquisitions = body.Operations.Where(o => o.Kind == OwnershipOperationKind.AcquirePattern).ToArray();
        Assert.Equal(["a", "b"], acquisitions.Select(o => body.Places[o.Input].Source.BoundSymbol!.Name));
        var cleanup = body.CleanupPlans.First(p => p.Reason == CleanupReason.ScopeExit);
        var destroyed = body.CleanupSteps.Skip(cleanup.Start).Take(cleanup.Count).Where(s => s.Action == CleanupAction.Destroy).Select(s => s.Place);
        Assert.Equal(acquisitions.Reverse().Select(o => o.Input), destroyed);
    }

    [Fact]
    public void ChangedPayloadStorageCannotReuseOldMatchOrDecompositionPlans()
    {
        var c = Parse("enum E\n    One(i32)\nfunc f(x: E) => match x\n    .One(let a) => ()\n    _ => ()");
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        var body = Body(c);
        var matches = body.Matches;
        var decompositions = body.Decompositions;
        var declaration = (EnumKoto)decompositions[0].Case.Owner.Declaration;
        c.Kotonoha.CreateCodeContext().Parse(declaration, "Borrowed(ref/i32 from static)");
        Assert.True(c.Bind().IsComplete);
        Assert.False(body.IsVerified);
        Assert.False(c.Ownership.Analyze().IsVerified);
        Assert.Contains(c.Ownership.Issues, i => i.Source is MatchKoto && i.Failure == OwnershipFailure.Unsupported);
        Assert.Empty(matches);
        Assert.Empty(decompositions);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(32)]
    [InlineData(128)]
    public void WarmMatchOwnershipAndBindingReuseStorage(int count)
    {
        var source = new System.Text.StringBuilder("func f()\n");
        for (var i = 0; i < count; i++)
        {
            source.Append("    match Option<Option<string>>.Some(.Some(\"text\"))\n        .Some(.Some(let s))\n            writeLine(s)\n        .Some(_)\n            ()\n        .None\n            ()\n");
        }

        var c = Parse(source.ToString());
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        var body = Body(c);
        var plans = body.Matches;
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Ownership.Analyze()));
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Bind();
            c.Ownership.Analyze();
        }));
        Assert.Same(body, Body(c));
        Assert.Same(plans, body.Matches);
        Assert.Equal(count, plans.Count);
        c.Bind();
        Assert.False(body.IsVerified);
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
    }

    private static OwnershipBody Body(Compilation c) => c.Ownership.Bodies.Single(b => b.Function.Name == "f");

    private static Compilation Parse(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare("x86_64-pc-windows-msvc"));
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, source);
        Assert.True(c.Kotonoha.DiagnosticCollection.GetArray().Length == 0, string.Join("\n", c.Kotonoha.DiagnosticCollection.GetArray().Select(i => i.ToString("source"))));
        Assert.True(c.Bind().IsComplete, string.Join("\n", c.Binding.Issues.Select(i => $"{i.Code}: {i.Node}")));
        return c;
    }

    private static string Describe(Compilation c) => string.Join("\n", c.Ownership.Issues.Select(i => $"{i.Failure} ({i.Source.GetType().Name}): {i.Source}")) +
        string.Join("\n", c.Ownership.ControlFlow!.Issues.Select(i => i.Message));
}
