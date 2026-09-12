// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

[TestClass(DisableParallelization = true)]
public class EnumOwnershipTest
{
    [Theory]
    [InlineData("let x = Option<i32>.Some(42)")]
    [InlineData("let x: Option<i32> = .Some(42)")]
    [InlineData("let x = Option<i32>.None")]
    [InlineData("let x: Option<i32> = .None")]
    [InlineData("let x = Result<i32, string>.Err(\"missing\")")]
    [InlineData("let x = Option<Option<i32>>.Some(Option<i32>.Some(1))")]
    [InlineData("let x: Option<Result<i32, string>> = .Some(.Err(\"error\"))")]
    [InlineData("let x = Option<i32>.Some(1)\nlet y = x\nlet z = x")]
    [InlineData("let x = Result<i32, i32>.Ok(1)\nlet y = x")]
    [InlineData("func echo(x: Option<string>) -> Option<string> => x\nlet y = echo(Option<string>.Some(\"text\"))")]
    [InlineData("func echo(x: Option<string>) -> Option<string>\n    return x\nlet y = echo(Option<string>.None)")]
    [InlineData("func choose(c: bool) -> Option<string> => if c => Option<string>.Some(\"a\") else => Option<string>.None")]
    [InlineData("func f(c: bool)\n    let x: Option<string>\n    if c\n        x = Option<string>.None\n    else\n        x = Option<string>.Some(\"a\")\n    let y = x")]
    [InlineData("enum E\n    Wrapped(())\nlet x = E.Wrapped(())")]
    [InlineData("let x = Option<i8>.Some(-128)")]
    [InlineData("let x = Option<f32>.Some(1.5)")]
    [InlineData("enum E\n    Value(string)\nfunc echo(x: E) -> E => x\nlet x: E = echo(E.Value(\"a\"))")]
    public void SupportedEnumProgramsVerify(string source)
    {
        var c = Parse(source);
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        Assert.Empty(c.Ownership.ControlFlow!.PendingBinding);
    }

    [Theory]
    [InlineData("E.Full(127)")]
    [InlineData(".Full(127)")]
    [InlineData("(E.Full(127))")]
    [InlineData("(.Full(127))")]
    public void NongenericDesignatorsShareConstructionOperations(string expression)
        => CheckConstruction("enum E\n    Full(i8)\n", "E", expression, 1);

    [Theory]
    [InlineData("E<i8>.Full(127)")]
    [InlineData(".Full(127)")]
    [InlineData("(E<i8>.Full(127))")]
    [InlineData("(.Full(127))")]
    public void GenericDesignatorsShareConstructionOperations(string expression)
        => CheckConstruction("enum E<T>\n    Full(T)\n", "E<i8>", expression, 1);

    [Theory]
    [InlineData("E<i32>.Empty")]
    [InlineData(".Empty")]
    [InlineData("(E<i32>.Empty)")]
    [InlineData("(.Empty)")]
    public void EmptyDesignatorsNeedNoRuntimeReceiver(string expression)
        => CheckConstruction("enum E<T>\n    Full(T)\n    Empty\n", "E<i32>", expression, 0);

    [Theory]
    [InlineData("let x = Option<string>.Some(\"a\")\nlet y = x\nlet z = x", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let x = Result<i32, i32>.Ok(1)\nlet y = x\nlet z = x", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let x: Option<string>\nlet y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("let x = Option<string>.None\nx = Option<string>.Some(\"a\")", OwnershipFailure.ReassignedLet)]
    [InlineData("let x = Option<string>.None\nlet y = x\nx = Option<string>.None", OwnershipFailure.ReassignedLet)]
    [InlineData("enum E\n    Both(string, string)\nlet s = \"a\"\nlet x = E.Both(s, s)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("func f(c: bool)\n    let x = Option<string>.None\n    if c\n        let y = x\n    let z = x", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("func f(c: bool)\n    let x = Option<string>.None\n    while c\n        let y = x", OwnershipFailure.PossiblyMovedUse)]
    public void EnumStateErrorsAreRejected(string source, OwnershipFailure expected)
    {
        var c = Parse(source);
        Assert.False(c.Ownership.Analyze().IsVerified);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == expected);
    }

    [Fact]
    public void SuccessfulConstructionTransfersPayloadResponsibilityExactlyOnce()
    {
        var c = Parse("enum E\n    Both(string, string)\nlet s = \"first\"\nlet x = E.Both(s, \"second\")");
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        var body = Body(c);
        var construction = Assert.Single(body.Constructions);
        var complete = body.Operations.Select((operation, index) => (operation, index)).Single(x => x.operation.Kind == OwnershipOperationKind.CompleteConstruction);
        for (var i = 0; i < construction.PayloadCount; i++)
        {
            var payload = construction.PayloadStart + i;
            Assert.True((body.GetInputState(complete.index, payload) & PlaceState.MustInit) != 0);
            Assert.All(body.CleanupSteps.Where(x => x.Place == payload), x => Assert.Equal(CleanupAction.Skip, x.Action));
            Assert.DoesNotContain(body.CleanupPlans, x => x.Reason == CleanupReason.Replacement && body.CleanupSteps.Skip(x.Start).Take(x.Count).Any(y => y.Place == payload));
        }

        var x = body.Places.Single(x => x.Source is FieldKoto field && field.NameKoto.IdentifierName == "x");
        Assert.Single(body.CleanupSteps, step => step.Place == x.Id && step.Action == CleanupAction.Destroy);
    }

    [Fact]
    public void AbandonmentInterleavesPlacedPayloadsAndSurvivingTemporaries()
    {
        var c = Parse("enum E\n    Make(bool, string, i32)\nfunc makeText() -> string => \"temporary\"\nfunc use(c: bool)\n    let t = \"comparison\"\n    E.Make(makeText() == t, \"payload\", if c => return else => 1)");
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        var body = Body(c, "use");
        var construction = Assert.Single(body.Constructions);
        var steps = ReturnDestruction(body);
        var payload0 = Array.FindIndex(steps, x => x.Place == construction.PayloadStart);
        var payload1 = Array.FindIndex(steps, x => x.Place == construction.PayloadStart + 1);
        var temporary = Array.FindIndex(steps, x => x.Source is InvocationKoto call && call.BoundCall?.Target.Name == "makeText");
        Assert.True(payload1 >= 0 && payload0 > payload1 && temporary > payload0);
        Assert.DoesNotContain(steps, x => x.Place == construction.Place || x.Place == construction.PayloadStart + 2);
    }

    [Fact]
    public void NestedAbandonmentCleansInnerPayloadThenOuterPayload()
    {
        var c = Parse("enum E\n    Inner(string, i32)\nenum Outer\n    Both(string, E)\nfunc use(c: bool)\n    Outer.Both(\"outer\", E.Inner(\"inner\", if c => return else => 1))");
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        var body = Body(c, "use");
        var outer = body.Constructions[0];
        var inner = body.Constructions[1];
        var steps = ReturnDestruction(body);
        var innerStep = Array.FindIndex(steps, x => x.Place == inner.PayloadStart);
        var outerStep = Array.FindIndex(steps, x => x.Place == outer.PayloadStart);
        Assert.True(innerStep >= 0 && outerStep > innerStep);
        Assert.DoesNotContain(steps, x => x.Place == inner.Place || x.Place == outer.Place);
    }

    [Fact]
    public void ReturnInsidePayloadCleansLocalBeforeEarlierPayload()
    {
        var c = Parse("enum E\n    Both(string, i32)\nfunc use(c: bool)\n    E.Both(\n        \"outer\",\n        if c\n            let inner = \"inner\"\n            return\n        else => 1\n    )");
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        var body = Body(c, "use");
        var steps = ReturnDestruction(body);
        var local = Array.FindIndex(steps, x => x.Source is FieldKoto);
        var payload = Array.FindIndex(steps, x => x.Place == body.Constructions[0].PayloadStart);
        Assert.True(local >= 0 && payload > local);
    }

    [Fact]
    public void ReturningAnEnumSecuresResultBeforeAbandonedConstructionCleanup()
    {
        var c = Parse("enum E\n    Both(string, i32)\nfunc use(c: bool) -> Option<string>\n    E.Both(\"outer\", if c => return Option<string>.Some(\"result\") else => 1)\n    return Option<string>.None");
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        var body = Body(c, "use");
        var cleanup = ReturnDestruction(body).Single(x => x.Place == body.Constructions[0].PayloadStart);
        var secured = body.Operations.Take(cleanup.Operation).Last(x => x.Kind == OwnershipOperationKind.Write && body.Places[x.Place].Kind == OwnershipPlaceKind.Result);
        Assert.True((body.GetInputState(cleanup.Operation, secured.Place) & PlaceState.MustInit) != 0);
        Assert.DoesNotContain(ReturnDestruction(body), x => body.Places[x.Place].Kind == OwnershipPlaceKind.Result);
    }

    [Theory]
    [InlineData("continue")]
    [InlineData("exit")]
    public void LoopTransferAbandonsConstructionAndResetsPayloads(string transfer)
    {
        var c = Parse($"enum E\n    Both(string, i32)\nfunc use(c: bool)\n    while c\n        E.Both(\"a\", if c => {transfer} else => 1)");
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        var body = Body(c, "use");
        var construction = Assert.Single(body.Constructions);
        Assert.Contains(body.CleanupSteps, x => x.Place == construction.PayloadStart && x.Action == CleanupAction.Destroy && body.Operations[x.Operation].Source is JumpKoto);
        foreach (var indexed in body.Operations.Select((operation, index) => (operation, index)).Where(x => x.operation.Kind == OwnershipOperationKind.PayloadPlacement))
        {
            Assert.Equal(PlaceState.None, body.GetInputState(indexed.index, indexed.operation.Place));
            Assert.Equal(PlacementKind.None, indexed.operation.Placement);
        }
    }

    [Fact]
    public void AbortDoesNotReachCleanupOrConstructionCompletion()
    {
        var c = Parse("enum E\n    Both(string, i32)\nfunc stop() -> Never\n    loop\n        ()\nfunc use()\n    E.Both(\"a\", stop())");
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        var body = Body(c, "use");
        Assert.All(body.Edges.Where(x => x.Kind == OwnershipEdgeKind.Abort), x => Assert.Equal(OwnershipOperationKind.Exit, body.Operations[x.To].Kind));
        Assert.All(body.Operations.Select((operation, index) => (operation, index)).Where(x => x.operation.Kind == OwnershipOperationKind.CompleteConstruction), x => Assert.False(body.IsReachable(x.index)));
        Assert.DoesNotContain(body.CleanupSteps, x => x.Action == CleanupAction.Destroy);
    }

    [Fact]
    public void WholeReplacementUsesPostAcquisitionState()
    {
        var c = Parse("var x = Option<string>.Some(\"a\")\nx = Option<string>.None\nx = x");
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        var body = Body(c);
        var local = body.Places.Single(x => x.Kind == OwnershipPlaceKind.Local);
        var writes = body.Operations.Where(x => x.Kind == OwnershipOperationKind.Write && x.Place == local.Id).ToArray();
        Assert.Equal(PlacementKind.Replacement, writes[^2].Placement);
        Assert.Equal(PlacementKind.Reinitialization, writes[^1].Placement);
        Assert.Contains(body.CleanupSteps, x => x.Place == local.Id && body.Operations[x.Operation].Placement == PlacementKind.Reinitialization && x.Action == CleanupAction.Skip);
    }

    [Fact]
    public void ConditionalReplacementAndCleanupUseWholeEnumState()
    {
        var c = Parse("func use(c: bool)\n    var x: Option<string>\n    if c\n        x = Option<string>.Some(\"a\")\n    x = Option<string>.None\n    if c\n        let y = x");
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        var body = Body(c, "use");
        var local = body.Places.Single(x => x.Source is FieldKoto field && field.NameKoto.IdentifierName == "x");
        Assert.Contains(body.Operations, x => x.Place == local.Id && x.Kind == OwnershipOperationKind.Write && x.Placement == PlacementKind.ConditionalReplacement);
        Assert.Contains(body.CleanupSteps, x => x.Place == local.Id && x.Action == CleanupAction.Conditional && body.Operations[x.Operation].Kind == OwnershipOperationKind.Cleanup);
    }

    [Fact]
    public void CompletedTemporaryRegistersAfterItsConditionTemporaries()
    {
        var c = Parse("enum E\n    Both(bool, i32)\nfunc makeText() -> string => \"condition\"\nfunc use(c: bool)\n    let t = \"test\"\n    E.Both((if makeText() == t => \"yes\" else => \"no\") == t, if c => return else => 1)");
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        var body = Body(c, "use");
        var steps = ReturnDestruction(body);
        var result = Array.FindIndex(steps, x => x.Source is IfKoto && body.Places[x.Place].Type.Name == "string");
        var condition = Array.FindIndex(steps, x => x.Source is InvocationKoto call && call.BoundCall?.Target.Name == "makeText");
        var payload = Array.FindIndex(steps, x => x.Place == body.Constructions[0].PayloadStart);
        Assert.True(payload >= 0 && result > payload);
        Assert.Equal(-1, condition); // Condition temporaries end before entering either branch.
        Assert.Contains(body.CleanupPlans, p => p.Reason == CleanupReason.ExpressionEnd);
    }

    [Fact]
    public void RebindingChangedStorageInvalidatesTheSupportedTypeCache()
    {
        var c = Parse("struct S\n    var value: i32\nenum E\n    Empty\nlet x = E.Empty");
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        var body = Body(c);
        var declaration = (EnumKoto)body.Constructions[0].Case.Owner.Declaration;
        c.Kotonoha.CreateCodeContext().Parse(declaration, "Full(S)");
        Assert.True(c.Bind().IsComplete);
        Assert.False(body.IsVerified);
        Assert.False(c.Ownership.Analyze().IsVerified);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Theory]
    [InlineData("struct S\n    var value: i32\nenum E\n    Empty\n    Full(S)\nlet x = E.Empty")]
    [InlineData("enum E\n    Empty\n    Again(E)\nlet x = E.Empty")]
    [InlineData("enum E<T>\n    Empty\n    Again(E<E<T>>)\nlet x = E<i32>.Empty")]
    [InlineData("func f<T>(x: T) -> Option<T> => .Some(x)")]
    [InlineData("enum A\n    Empty\n    Again(B)\nenum B\n    Again(A)\nlet x = A.Empty")]
    [InlineData("enum V<T> origin a\n    Some(ref/T from a)\nfunc f()\n    var n = 1\n    let v = V<i32>.Some(n)")]
    [InlineData("func f(x: ref/i32 from static) -> Option<ref/i32 from static> => .Some(x)")]
    [InlineData("func f(x: uniq/i32) -> Option<uniq/i32 from x> => .Some(x)")]
    public void UnsupportedPayloadTypesCannotBeHiddenByAnEmptyCase(string source)
    {
        var c = Parse(source);
        var result = c.Ownership.Analyze();
        Assert.False(result.IsVerified);
        Assert.True(result.UnsupportedCount > 0, Describe(c));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(32)]
    [InlineData(128)]
    public void WarmEnumAnalysisReusesStateAndPlans(int count)
    {
        var source = "func use(c: bool)\n" + string.Join('\n', Enumerable.Range(0, count).Select(i => $"    var x{i} = Option<Option<string>>.Some(Option<string>.Some(\"a\"))\n    if c\n        let y{i} = x{i}"));
        var c = Parse(source);
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        var body = Body(c, "use");
        var plans = body.Constructions;
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Ownership.Analyze()));
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Bind();
            c.Ownership.Analyze();
        }));
        Assert.Same(body, Body(c, "use"));
        Assert.Same(plans, body.Constructions);
        Assert.Equal(count * 2, plans.Count);
        c.Bind();
        Assert.False(body.IsVerified);
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        Assert.Equal(count * 2, body.Constructions.Count);
    }

    private static void CheckConstruction(string declarations, string type, string expression, int count)
    {
        var c = Parse(declarations + $"let x: {type} = {expression}");
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        Assert.Empty(c.Ownership.ControlFlow!.PendingBinding);
        var body = Body(c);
        var construction = Assert.Single(body.Constructions);
        Assert.Equal(count, construction.PayloadCount);
        Assert.Equal(count, body.Operations.Count(x => x.Kind == OwnershipOperationKind.PayloadPlacement));
        Assert.Single(body.Operations, x => x.Kind == OwnershipOperationKind.CompleteConstruction);
        Assert.DoesNotContain(body.Operations, x => x.Kind is OwnershipOperationKind.Call or OwnershipOperationKind.CallEntry);
        var baseline = Parse(declarations + $"let x: {type} = " + (count == 0 ? ".Empty" : ".Full(127)"));
        Assert.True(baseline.Ownership.Analyze().IsVerified, Describe(baseline));
        Assert.Equal(Body(baseline).Operations.Select(x => x.Kind), body.Operations.Select(x => x.Kind));
        if (body.Places[construction.Place].Type.Kind == BoundTypeKind.Nominal)
        {
            Assert.Same(construction.Case.Owner.Type, body.Places[construction.Place].Type);
        }

        if (count != 0)
        {
            var payload = body.Places[construction.PayloadStart];
            Assert.Equal("i8", payload.Type.Name);
            Assert.Equal(AcquisitionKind.Copy, payload.Acquisition);
        }
    }

    private static OwnershipCleanupStep[] ReturnDestruction(OwnershipBody body)
        => body.CleanupSteps.Where(x => x.Action == CleanupAction.Destroy && body.Operations[x.Operation].Source is ReturnKoto).ToArray();

    private static OwnershipBody Body(Compilation c, string? name = null)
        => c.Ownership.Bodies.Single(x => name is null ? x.Function.IsGenerated : x.Function.Name == name);

    private static Compilation Parse(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare("x86_64-pc-windows-msvc"));
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, source);
        Assert.True(c.Kotonoha.DiagnosticCollection.GetArray().Length == 0, string.Join("\n", c.Kotonoha.DiagnosticCollection.GetArray().Select(x => x.ToString("source"))));
        Assert.True(c.Bind().IsComplete, string.Join("\n", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node}")));
        return c;
    }

    private static string Describe(Compilation c)
        => string.Join("\n", c.Ownership.Issues.Select(x => $"{x.Failure}: {x.Source}")) +
            string.Join("\n", c.Ownership.ControlFlow?.Issues.Select(x => x.Message) ?? []) +
            string.Join("\n", c.Ownership.ControlFlow?.PendingBinding.Select(x => $"pending: {x}") ?? []);
}
