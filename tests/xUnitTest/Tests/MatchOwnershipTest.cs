// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

public class MatchOwnershipTest
{
    [Theory]
    [InlineData("func f(x: (i32, i32)) => match x\n    (let a, _) => ()")]
    [InlineData("func f(x: Option<(i32, i32)>) => match x\n    .Some((let a, _)) => ()\n    .None => ()")]
    [InlineData("func f(x: Option<i32>) -> i32 => match x\n    .Some(let n) => n\n    .None => 0")]
    [InlineData("func f(x: Option<i32>) -> i32\n    return match x\n        .Some(let n)\n            yield n\n        .None\n            yield 0")]
    [InlineData("func f(x: Option<string>) => match x@move\n    .Some(let text) => Console.writeLine(text)\n    .None => ()")]
    [InlineData("func f(x: Option<Option<string>>) => match x@move\n    .Some(.Some(let text)) => Console.writeLine(text)\n    .Some(_) => ()\n    .None => ()")]
    [InlineData("func f(x: Option<string>) -> string => match x@move\n    .Some(let s) => s@move\n    .None => \"empty\"")]
    [InlineData("func f(x: Option<string>) -> string => match x@move\n    .Some(let s)\n        return s@move\n    .None\n        return \"empty\"")]
    [InlineData("func f(x: Option<Option<i32>>) -> i32 => match x\n    .Some(let inner) => match inner\n        .Some(let n) => n\n        .None => 0\n    .None => 0")]
    [InlineData("func f(x: bool) -> i32 => match x\n    true => 1\n    false => 0")]
    [InlineData("func f(x: ()) -> i32 => match x\n    () => 1")]
    [InlineData("func f(x: string) -> i32 => match x@move\n    \"yes\" => 1\n    _ => 0")]
    [InlineData("func f(x: u128) -> i32 => match x\n    340282366920938463463374607431768211455 => 1\n    _ => 0")]
    [InlineData("func f(x: char) -> i32 => match x\n    'A' => 1\n    _ => 0")]
    [InlineData("func f(x: Option<i8>) -> i8 => match x\n    (Option<i8>.Some(let n,)) => n\n    (::Kimi.Option<i8>.None) => -128")]
    [InlineData("func f(x: Option<i32>) -> i32 => match x@move\n    .Some(var n)\n        n = 2\n        yield n\n    .None\n        yield 0")]
    [InlineData("func f()\n    match Option<string>.Some(\"text\")@move\n        .None\n            ()\n        .Some(_) => ()")]
    [InlineData("func f(c: bool)\n    while c\n        match Option<string>.Some(\"text\")@move\n            .Some(let s)\n                Console.writeLine(s)\n                continue\n            .None\n                exit")]
    [InlineData("func f(c: bool, x: Option<string>)\n    match x@move\n        .Some(let s)\n            while c\n                continue\n            Console.writeLine(s)\n        .None\n            ()")]
    [InlineData("func f(x: string) => match x@move\n    let s => Console.writeLine(s)")]
    [InlineData("func f(x: Option<i32>)\n    match x\n        let copy\n            ()\n    let again = x")]
    [InlineData("enum E\n    One(string)\n    Two(string, string)\nfunc f(x: E) -> string => match x@move\n    .One(let a) => a@move\n    .Two(_, let b) => b@move")]
    [InlineData("func f(x: Option<string>) -> Option<string> => match x@move\n    .Some(let s) => .Some(s@move)\n    .None => .None")]
    [InlineData("func f(x: bool) -> i32 => match x\n    true => match (yield 1)\n        _ => 2\n    false => 0")]
    [InlineData("func f<T>(x: ref/T) => match x\n    let r => ()")]
    [InlineData("func f(x: ref/i32 during static) => match x\n    let r => ()")]
    // SPEC 15.1.6: a bare owned Subject is shared-borrowed, even a generic one, so no unknown Copy/Move acquisition is needed.
    [InlineData("func f<T>(x: T) => match x\n    let value => ()")]
    [InlineData("func f<T>(x: T) => match x\n    _ => ()")]
    public void SupportedMatchesVerify(string source)
    {
        var c = Parse(source);
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
    }

    [Theory]
    [InlineData("func f(s: string)\n    match s@move\n        _\n            ()\n    Console.writeLine(s)")]
    [InlineData("func f(s: string)\n    match s@move\n        \"yes\"\n            ()\n        _ => ()\n    Console.writeLine(s)")]
    [InlineData("func f(x: Option<string>)\n    match x@move\n        .Some(let s)\n            _ = s@move\n            Console.writeLine(s)\n        .None\n            ()")]
    public void AcquisitionStillRejectsMovedUses(string source)
    {
        var c = Parse(source);
        Assert.False(c.Analyze().IsVerified);
        Assert.Contains(c.Problems, i => i.Code == DiagnosticCode.MovedPlace_Kd);
    }

    [Theory]
    [InlineData("fail()", 1)]
    [InlineData("(fail())", 1)]
    [InlineData("if true => fail() else => fail()", 2)]
    public void NeverSubjectCreatesNoValueOrArmStorage(string subject, int calls)
    {
        var c = Parse($"func fail() -> Never => fail()\nfunc f() -> i32 => match ({subject})\n    _ => 1");
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        var body = Body(c);
        Assert.Equal(calls, body.Operations.Count(o => o.Kind == OwnershipOperationKind.Call));
        Assert.DoesNotContain(body.Places, p => p.Kind == OwnershipPlaceKind.Subject);
        Assert.Empty(body.Matches);
    }

    [Fact]
    public void TestsReadAnIntactSubjectBeforeAnyAcquisition()
    {
        var c = Parse("enum E\n    Pair(string, i32)\nfunc f(x: E) => match x@move\n    .Pair(let s, 0) => Console.writeLine(s)\n    .Pair(_, _) => ()");
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        var body = Body(c);
        var match = Assert.Single(body.Matches);
        var dispatch = body.Operations.Select((o, i) => (o, i)).Single(x => x.o.Kind == OwnershipOperationKind.MatchDispatch).i;
        Assert.Single(body.Edges, e => e.From == dispatch && e.Kind == OwnershipEdgeKind.MatchArm);
        Assert.Contains(body.Edges, e => e.From == body.MatchArms[0].Test && e.To == body.MatchArms[1].Test && e.Kind == OwnershipEdgeKind.False);
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
        var c = Parse("func f(x: Option<i32>) -> i32 => match x@move\n    .Some(let n) => n\n    .None => 0");
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
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
        var c = Parse("enum E\n    Pair(string, string)\nfunc f(x: E) => match x@move\n    .Pair(let a, _) => ()");
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
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
        var c = Parse("enum Inner\n    Pair(string, string)\nenum Outer\n    Pair(Inner, string)\nfunc f(x: Outer) => match x@move\n    .Pair(.Pair(let a, _), _) => ()\n    .Pair(_, _) => ()");
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
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
        var c = Parse("func f(x: i32)\n    let s = \"text\"\n    match x\n        _\n            ()\n        0\n            _ = s@move\n    let again = s@move");
        Assert.Single(c.Binding.PatternWarnings);
        Assert.False(c.Analyze().IsVerified);
        var body = Body(c);
        var s = body.Places.Single(p => p.Source is FieldKoto f && f.NameKoto.IdentifierName == "s");
        var use = body.Operations.Select((o, i) => (o, i)).Last(x => x.o.Kind == OwnershipOperationKind.Consume && x.o.Place == s.Id);
        Assert.True((body.GetInputState(use.i, s.Id) & PlaceState.MayMoved) != 0);
        Assert.All(body.MatchArms, arm => Assert.True(body.IsReachable(arm.Test)));
        Assert.Contains(body.Issues, i => i.Failure == OwnershipRequirement.PossiblyMovedUse);
    }

    [Fact]
    public void SubjectAndPayloadsHaveFreshLifetimesInsideLoops()
    {
        var c = Parse("func f(c: bool)\n    while c\n        match Option<string>.Some(\"text\")@move\n            .Some(let s)\n                Console.writeLine(s)\n            .None\n                ()");
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
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
        var c = Parse("func make() -> Option<string> => .Some(\"text\")\nfunc f() => match make()\n    .Some(let s) => Console.writeLine(s)\n    .None => ()");
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        var body = Body(c);
        var initialize = Assert.Single(body.Operations, o => o.Kind == OwnershipOperationKind.InitializeSubject);
        Assert.Single(body.Operations, o => o.Kind == OwnershipOperationKind.Call && o.Source is InvocationKoto call && call.CallOf()!.Target.Name == "make");
        Assert.DoesNotContain(body.Operations, o => o.Kind == OwnershipOperationKind.Consume && o.Place == initialize.Input);
        Assert.All(body.CleanupSteps.Where(s => s.Place == initialize.Input), s => Assert.Equal(CleanupAction.Skip, s.Action));
    }

    [Theory]
    [InlineData("return a@move", CleanupReason.Return)]
    [InlineData("yield a@move", CleanupReason.SelectionResult)]
    public void TransfersSecureValueBeforeBodyAndSubjectCleanup(string transfer, CleanupReason reason)
    {
        var c = Parse($"enum E\n    Pair(string, string)\nfunc f(c: bool, x: E) -> string => match x@move\n    .Pair(let a, _)\n        let local = \"body\"\n        {transfer}");
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
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
        var c = Parse("enum E\n    Pair(string, string)\nfunc make() -> string => \"outer\"\nfunc consume(a: string, b: string) => ()\nfunc f(c: bool, x: E)\n    consume(\n        make(),\n        match x@move\n            .Pair(let a, _)\n                if c\n                    let local = \"inner\"\n                    return\n                yield a@move\n    )");
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        var body = Body(c);
        var split = Assert.Single(body.Decompositions);
        var patternLocal = body.Places.Single(p => p.Kind == OwnershipPlaceKind.Local && p.Source is SyntaxFormKoto { Akind: KotoKind.BindingPattern });
        var inner = body.Places.Single(p => p.Source is FieldKoto f && f.NameKoto.IdentifierName == "local");
        var outer = body.Places.Single(p => p.Source is InvocationKoto call && call.CallOf()!.Target.Name == "make");
        var steps = body.CleanupSteps.Where(s => body.IsReachable(s.Operation) && body.Operations[s.Operation].Source is ReturnKoto && s.Action == CleanupAction.Destroy && body.Places[s.Place].Type.Name == "string");
        Assert.Equal([inner.Id, patternLocal.Id, split.PayloadStart + 1, outer.Id], steps.Select(s => s.Place));
    }

    [Fact]
    public void NestedSubjectsUseTheirOwnActiveCaseDuringReturn()
    {
        var c = Parse("enum E\n    Pair(string, string)\nfunc f(x: E, y: E) -> string => match x@move\n    .Pair(let a, _) => match y@move\n        .Pair(let b, _)\n            return b@move");
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
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
        var c = Parse("func f(c: bool)\n    while c\n        match Option<string>.Some(\"text\")@move\n            .Some(let s)\n                continue\n            .None\n                ()");
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
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
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, "func f(x: Option<string>)\n    match x@move\n        .Some(let s) => Console.writeLine(s)");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, i => i.Code == DiagnosticCode.NonExhaustiveMatch_Kd);
        Assert.False(c.Analyze().IsVerified);
    }

    [Fact]
    public void SubjectEvaluationTemporariesKeepTheirExpressionLifetime()
    {
        var c = Parse("func make() -> string => \"temporary\"\nfunc consume(a: i32, b: i32) => ()\nfunc f()\n    consume(\n        2,\n        match make() == \"expected\"\n            true => 1\n            false => 0\n    )");
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        var body = Body(c);
        var inputTemporary = body.Places.Single(p => p.Source is InvocationKoto call && call.CallOf()!.Target.Name == "make");
        var destroy = Assert.Single(body.CleanupSteps, s => s.Place == inputTemporary.Id && s.Action == CleanupAction.Destroy);
        var consume = body.Operations.Select((o, i) => (o, i)).Single(x => x.o.Kind == OwnershipOperationKind.Call && x.o.Source is InvocationKoto call && call.CallOf()!.Target.Name == "consume");
        Assert.True(destroy.Operation > consume.i);
    }

    [Fact]
    public void AbortAfterAcquisitionHasNoReachableCleanup()
    {
        var c = Parse("func fail() -> Never => fail()\nfunc f(x: Option<string>) => match x@move\n    .Some(let s) => fail()\n    .None => fail()");
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        var body = Body(c);
        Assert.All(body.CleanupSteps, s => Assert.False(body.IsReachable(s.Operation)));
        Assert.Equal(2, body.Edges.Count(e => e.Kind == OwnershipEdgeKind.Abort));
    }

    [Fact]
    public void YieldToIfIsNotRedirectedIntoTheMatchFrame()
    {
        var c = Parse("func f(x: bool) -> i32 => match x\n    true => (if x\n        yield 1\n    else\n        yield 2\n    )\n    false => 0");
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        Assert.Empty(c.Problems);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void MatchMayPrecedeAnotherCallArgument(string newline)
    {
        var source = "func consume(a: i32, b: i32) => ()\nfunc f(x: bool)\n    consume(\n        match x\n            true => 1\n            false => 0\n        , 2\n    )".Replace("\n", newline, StringComparison.Ordinal);
        var c = Parse(source);
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        Assert.Single(Body(c).Matches);
        Assert.Single(Body(c).Operations, o => o.Kind == OwnershipOperationKind.Call);
    }

    [Fact]
    public void DedentDoesNotReplaceTheRequiredArgumentComma()
    {
        var c = Compilation.CreateForTest();
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, "func f(x: bool)\n    consume(\n        match x\n            true => 1\n            false => 0\n        2\n    )");
        Assert.NotEmpty(TestDiagnostics.Of(c));
    }

    [Fact]
    public void BodyBindingsAreAcquiredLeftToRightAndCleanedInReverse()
    {
        var c = Parse("enum E\n    Pair(string, string)\nfunc f(x: E) => match x@move\n    .Pair(let a, let b) => ()");
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        var body = Body(c);
        var acquisitions = body.Operations.Where(o => o.Kind == OwnershipOperationKind.AcquirePattern).ToArray();
        Assert.Equal(["a", "b"], acquisitions.Select(o => body.Places[o.Input].Source.SymbolOf()!.Name));
        var cleanup = body.CleanupPlans.First(p => p.Reason == CleanupReason.ScopeExit);
        var destroyed = body.CleanupSteps.Skip(cleanup.Start).Take(cleanup.Count).Where(s => s.Action == CleanupAction.Destroy).Select(s => s.Place);
        Assert.Equal(acquisitions.Reverse().Select(o => o.Input), destroyed);
    }

    [Fact]
    public void ChangedPayloadStorageRebuildsMatchAndDecompositionPlans()
    {
        var c = Parse("enum E\n    One(i32)\nfunc f(x: E) => match x@move\n    .One(let a) => ()\n    _ => ()");
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        var body = Body(c);
        var matches = body.Matches;
        var decompositions = body.Decompositions;
        var originalCases = AdtDef.CaseTypes(body.Places[matches[0].Subject].Type)!;
        var declaration = (EnumKoto)Assert.IsType<BoundEnumCase>(decompositions[0].Case).Owner.Declaration;
        c.Kotonoha.CreateCodeContext().Parse(declaration, "Borrowed(ref/i32 during static)");
        Assert.True(c.Bind().IsComplete);
        Assert.False(body.IsVerified);
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        var rebuilt = body.Places[Assert.Single(matches).Subject].Type;
        Assert.Single(originalCases);
        Assert.NotSame(originalCases, AdtDef.CaseTypes(rebuilt));
        Assert.Equal(2, AdtDef.CaseTypes(rebuilt)!.Length);
        Assert.Single(decompositions);
    }

    // DECISIONS BU-14 (D5 exception until R6-check): a Binding that may borrow a part of the owned Subject or item past a pair layer
    // admitting owner, or that selects a pair layer, is the old path's located Unsupported_Kd at its match or loop, although Binding
    // accepts it and the MIR builds it (SPEC 13.5.5, 15.1.6); a borrowed path's pair layer, a Wildcard (a for slot `_` included, SPEC
    // 14.8.1) and a structural position that only tests stay judged here.
    [Theory]
    [InlineData("func f<s/T>(pair: (s/T, i32))\n    s is owner or uniq\n    match pair@move\n        (let x, _) => use(x)", "match pair@move")]
    [InlineData("func f<s/T>(pair: (s/T, i32))\n    s is uniq\n    match pair@move\n        (let x, _) => use(x)", "match pair@move")]
    [InlineData("func f<s/T>(o: s/Option<i32>, marker: s/T)\n    s is owner or uniq\n    match o@move\n        .Some(let n) => n@follow = 5\n        .None => ()", "match o@move")]
    [InlineData("func f<s/T>(arr: Array<(s/T, i32)>)\n    s is owner or uniq\n    for (x, _) in arr@move => use(x)", "for (x, _) in arr@move => use(x)")]
    [InlineData("func f<s/T>(items: Array<s/(i32, i32) during a>, marker: s/T)\n    s is owner or uniq\n    for (x, y) in items@move => use(x)", "for (x, y) in items@move => use(x)")]
    [InlineData("func f<s/T>(items: Array<s/(i32, i32) during a>, marker: s/T)\n    s is owner or uniq\n    for (x, y) in items => use(x)", null)]
    [InlineData("func f<s/T>(o: s/Option<i32>, marker: s/T)\n    s is uniq\n    match o@move\n        .Some(let n) => n@follow = 5\n        .None => ()", null)]
    [InlineData("func f<s/T>(pair: (s/T, i32)) -> i32\n    s is owner or uniq\n    match pair@move\n        (_, let n) => return n", null)]
    [InlineData("func f<s/T>(pair: (s/T, i32)) -> i32\n    s is owner\n    match pair@move\n        (_, let n) => return n", null)]
    [InlineData("func f<s/T>(o: s/Option<T>, marker: s/T) -> bool\n    s is owner or uniq\n    match o@move\n        .Some(_) => return true\n        .None => return false", null)]
    [InlineData("func f<s/T>(value: s/T) -> i32\n    s is owner or uniq\n    match value@move\n        _ => return 3", null)]
    [InlineData("struct Holder<E>\n    public computed p: (E, i32)\n        get(self: ref/Self) -> (E, i32)\n            $abort(\"p\")\nfunc f<s/T>(holder: ref/Holder<s/T>)\n    s is owner or uniq\n    match holder.p\n        (_, let n) => use(n)", null)]
    [InlineData("func f<s/T>(arr: Array<(s/T, i32)>) -> i32\n    s is owner or uniq\n    var total: i32 = 0\n    for (_, n) in arr@move => total += n\n    return total", null)]
    public void APairLayerBorrowIntoAnOwnedSubjectIsTheOldPathsLimit(string source, string? at)
    {
        var c = MinimalEmissionTest.Analyze("func use<T>(value: ref/T) => ()\n" + source + "\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var unsupported = c.Problems.Where(static x => x.Code == DiagnosticCode.Unsupported_Kd).Select(x => c.SyntaxOf(x.Subject).ToString()!.Split('\n')[0]).ToArray();
        Assert.Equal(at is null ? [] : [at], unsupported);
    }

    private static OwnershipBody Body(Compilation c) => c.Ownership.Bodies.Single(b => b.Function.Name == "f");

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
        public void WarmMatchOwnershipAndBindingReuseStorage(int count)
        {
            var source = new System.Text.StringBuilder("func f()\n");
            for (var i = 0; i < count; i++)
            {
                source.Append("    match Option<Option<string>>.Some(.Some(\"text\"))\n        .Some(.Some(let s))\n            Console.writeLine(s)\n        .Some(_)\n            ()\n        .None\n            ()\n");
            }

            var c = Parse(source.ToString());
            Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
            var body = Body(c);
            var plans = body.Matches;
            Assert.Equal(0, AllocationMeasurement.Measure(() => c.Analyze()));
            Assert.Equal(0, AllocationMeasurement.Measure(() =>
            {
                c.Bind();
                c.Analyze();
            }));
            Assert.Same(body, Body(c));
            Assert.Same(plans, body.Matches);
            Assert.Equal(count, plans.Count);
            c.Bind();
            Assert.False(body.IsVerified);
            Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        }
    }
}
