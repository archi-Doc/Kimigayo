// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>Short adaptations select object payloads and borrow whole slots under one fixed source Type.</summary>
public class SemanticsAdaptationTypeTest
{
    [Theory]
    [InlineData(SemanticsKind.Obj)]
    [InlineData(SemanticsKind.Rc)]
    [InlineData(SemanticsKind.Arc)]
    public void ObjectCasesKeepTheViewAndReferenceCasesWrapTheSlot(SemanticsKind mode)
    {
        var (c, function, pair) = Parse("func f<s/T>(value: s/T)\n    s is object or ref\n    T is ObjectPayload\n    ()");
        var type = c.Binding.SemanticsAdaptation(pair, pair.WholeType!, BoundOrigin.Static);
        ReadOnlySpan<PairCase> objects = [new(pair, mode)];
        ReadOnlySpan<PairCase> references = [new(pair, SemanticsKind.Ref)];
        Assert.True(type.ContainsParameter && type.ContainsPairLayer);
        Assert.False(Binding.TryPairLayer(type, out _, out _));
        Assert.True(Binding.TryAdaptationSelector(type, out var selector));
        Assert.Same(pair.WholeType, selector);

        Assert.Same(c.Binding.CaseType(pair.WholeType!, objects), c.Binding.CaseType(type, objects));
        var reference = c.Binding.CaseType(type, references);
        Assert.Equal(SemanticsKind.Ref, reference.Semantics);
        Assert.Same(BoundOrigin.Static, reference.Origin);
        Assert.Same(c.Binding.CaseType(pair.WholeType!, references), reference.Components[0]);
        Assert.NotEqual(ConstraintProof.Proven, c.Binding.ProveCopy(type, function));
        Assert.NotEqual(ConstraintProof.Proven, c.Binding.ProveOwned(type, function));
        Assert.Contains("case s", Binding.HoverTypeName(type), StringComparison.Ordinal);
    }

    [Fact]
    public void ClosingTheSelectorBeforeTheSourcePreservesPayloadSelection()
    {
        var (c, function, selector) = Parse("func f<s/T, u/U>(value: u/U)\n    s is object\n    u is owner or obj\n    T is ObjectPayload\n    U is ObjectPayload\n    ()");
        var source = function.SymbolOf()!.Schema!.GenericSlots[1].Symbol;
        var type = c.Binding.SemanticsAdaptation(selector, source.WholeType!, null);
        ReadOnlySpan<PairCase> select = [new(selector, SemanticsKind.Obj)];
        var pending = c.Binding.CaseType(type, select);
        Assert.Equal(BoundTypeKind.Semantics, pending.Kind);
        Assert.Null(pending.Symbol);
        Assert.Equal(SemanticsKind.Obj, pending.Semantics);

        ReadOnlySpan<PairCase> owner = [new(source, SemanticsKind.Owner)];
        ReadOnlySpan<PairCase> handle = [new(source, SemanticsKind.Obj)];
        ReadOnlySpan<PairCase> both = [new(selector, SemanticsKind.Obj), new(source, SemanticsKind.Obj)];
        var created = c.Binding.CaseType(pending, owner);
        Assert.Equal(SemanticsKind.Obj, created.Semantics);
        Assert.Same(source.Type, created.Components[0]);
        Assert.Same(created, c.Binding.CaseType(pending, handle));
        Assert.Same(created, c.Binding.CaseType(type, both));
    }

    [Fact]
    public void ASharedObjectViewKeepsItsExistingOrigin()
    {
        var (c, _, pair) = Parse("func f<s/T>(value: s/T)\n    s is objref\n    ()");
        var type = c.Binding.SemanticsAdaptation(pair, pair.WholeType!, BoundOrigin.Static);
        ReadOnlySpan<PairCase> cases = [new(pair, SemanticsKind.ObjRef)];
        var source = c.Binding.CaseType(pair.WholeType!, cases);
        var result = c.Binding.CaseType(type, cases);
        Assert.Same(source, result);
        Assert.Same(c.Binding.PairOuterOrigin(pair), result.Origin);
    }

    [Theory]
    [InlineData(SemanticsKind.ObjRef)]
    [InlineData(SemanticsKind.ObjUniq)]
    public void AnExclusiveObjectViewReborrowsWithinItsExistingOrigin(SemanticsKind target)
    {
        var (c, _, pair) = Parse("func f<s/T>(value: s/T)\n    s is objuniq\n    ()");
        ReadOnlySpan<PairCase> cases = [new(pair, SemanticsKind.ObjUniq)];
        var source = c.Binding.CaseType(pair.WholeType!, cases);
        var result = c.Binding.AdaptedType(target, source, BoundOrigin.Static);
        Assert.Equal(target, result.Semantics);
        Assert.Same(source.Components[0], result.Components[0]);
        Assert.Same(source.Origin, result.Origin);
        Assert.Same(c.Binding.PairOuterOrigin(pair), result.Origin);
    }

    [Fact]
    public void ResultSemanticsKeepsTheOwnerSourceMode()
    {
        var (c, _, pair) = Parse("func f<s/T>(value: s/T)\n    s is owner or objref\n    ()");
        var source = c.Binding.SharedReference(BoundType.I32, BoundOrigin.Static);
        var type = c.Binding.SemanticsAdaptation(pair, source, BoundOrigin.Static);
        Assert.Equal(SemanticsMask.Ref | SemanticsMask.ObjRef, c.Binding.ResultSemantics(type, pair.Scope));
    }

    [Fact]
    public void OwnerApplicationsPreservePayloadModeAndOrigin()
    {
        var (c, function, pair) = Parse("func f<s/T>(value: s/T)\n    s is owner\n    ()");
        var local = new BoundOrigin(OriginKind.Input, function, name: "payload");
        var reference = c.Binding.SharedReference(BoundType.I32, local);
        var longer = c.Binding.SharedReference(BoundType.I32, BoundOrigin.Static);
        var actual = new BoundType("s/ref/i32", BoundTypeKind.SemanticsApplication, pair, SemanticsKind.Parameter, [reference], origin: local);
        var same = new BoundType("s/ref/i32", BoundTypeKind.SemanticsApplication, pair, SemanticsKind.Parameter, [reference], origin: BoundOrigin.Static);
        var expected = new BoundType("s/ref/i32", BoundTypeKind.SemanticsApplication, pair, SemanticsKind.Parameter, [longer], origin: local);
        Assert.Equal(SemanticsMask.Ref, c.Binding.ResultSemantics(actual, pair.Scope));
        Assert.True(c.Binding.FitsTypeAt(actual, same, function));
        Assert.False(c.Binding.FitsTypeAt(actual, expected, function));
        ReadOnlySpan<PairCase> cases = [new(pair, SemanticsKind.Owner)];
        Assert.Same(reference, c.Binding.CaseType(actual, cases));
    }

    [Theory]
    [InlineData("ObjectPayload")]
    [InlineData("Sealed")]
    [InlineData("PrimitiveInteger")]
    public void OwnerEvidenceDeterminesAPlainTypeParametersMode(string evidence)
    {
        var (c, function, _) = Parse($"func f<s/T, U>(value: U)\n    s is owner\n    U is {evidence}\n    ()");
        var parameter = function.SymbolOf()!.Schema!.GenericSlots[1].Symbol;
        Assert.Equal(SemanticsMask.Owner, c.Binding.ResultSemantics(parameter.Type!, parameter.Scope));
    }

    [Fact]
    public void AReachableRawCaseKeepsTheInputTypeInvariant()
    {
        var (c, function, pair) = Parse("func f<s/T>(value: s/T)\n    s is raw or objref\n    ()");
        var longer = c.Binding.AdaptedType(SemanticsKind.ObjRef, BoundType.I32, BoundOrigin.Static);
        var shorter = c.Binding.AdaptedType(SemanticsKind.ObjRef, BoundType.I32, c.Binding.PairOuterOrigin(pair));
        var actual = c.Binding.SemanticsAdaptation(pair, longer, BoundOrigin.Static);
        var expected = c.Binding.SemanticsAdaptation(pair, shorter, BoundOrigin.Static);
        Assert.False(c.Binding.FitsTypeAt(actual, expected, function));
        ReadOnlySpan<PairCase> cases = [new(pair, SemanticsKind.Raw)];
        Assert.False(Binding.FitsType(c.Binding.CaseType(actual, cases), c.Binding.CaseType(expected, cases)));
    }

    [Fact]
    public void ClosedCallSubstitutionUsesTheSameNormalization()
    {
        var (c, _, pair) = Parse("func f<s/T>(value: s/T)\n    s is object or ref\n    T is ObjectPayload\n    ()\nlet handle = 7@obj\nf(handle@move)\nlet number = 7\nf(number@ref)");
        var type = c.Binding.SemanticsAdaptation(pair, pair.WholeType!, BoundOrigin.Static);
        var calls = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Where(x => x.CallOf()?.Target.Name == "f").ToArray();
        Assert.Equal(2, calls.Length);
        var owner = c.Binding.InstantiateStorageType(type, calls[0].CallOf()!);
        Assert.NotNull(owner);
        Assert.Equal(SemanticsKind.Obj, owner.Semantics);
        Assert.Same(BoundType.I32, owner.Components[0]);
        var borrow = c.Binding.InstantiateStorageType(type, calls[1].CallOf()!);
        Assert.NotNull(borrow);
        Assert.Equal(SemanticsKind.Ref, borrow.Semantics);
        Assert.Equal(SemanticsKind.Ref, borrow.Components[0].Semantics);
        Assert.Same(BoundType.I32, borrow.Components[0].Components[0]);
    }

    [Fact]
    public void AnInferredPairArgumentProjectsItsResultTargetPerCase()
    {
        var (c, _, pair) = Parse("func pass<u/U>(value: u/U) -> u/U\n    u is obj or ref\n    return value@move\n" +
            "func f<s/T>(value: s/T)\n    s is obj or ref\n    T is ObjectPayload\n    let adapted = value@move@s\n    let carried = pass(adapted@move)\n    _ = carried@move");
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>(), x => x.CallOf()?.Target.Name == "pass").CallOf()!;
        var parameter = call.Target.Schema!.GenericSlots[0].Symbol;
        var projected = c.Binding.InstantiateStorageType(parameter.Type!, call);
        Assert.NotNull(projected);
        ReadOnlySpan<PairCase> objects = [new(pair, SemanticsKind.Obj)];
        ReadOnlySpan<PairCase> references = [new(pair, SemanticsKind.Ref)];
        Assert.Same(pair.Type, c.Binding.CaseType(projected, objects));
        Assert.Same(c.Binding.CaseType(pair.WholeType!, references), c.Binding.CaseType(projected, references));
    }

    [Fact]
    public void ASubstitutedApplicationUsesTheAdaptedMode()
    {
        var (c, _, pair) = Parse("func take<u/U>(value: u/i32)\n    u is objref\n    ()\n" +
            "func f<s/T>(value: s/T)\n    s is owner or objref\n    ()");
        var callee = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.Name == "take");
        var view = c.Binding.AdaptedType(SemanticsKind.ObjRef, BoundType.I32, c.Binding.PairOuterOrigin(pair));
        var type = c.Binding.SemanticsAdaptation(pair, view, BoundOrigin.Static);
        var call = new CallPlan();
        call.Set(callee.SymbolOf()!, BoundType.Unit, null, [0], [type], origins: [BoundOrigin.Static], inputOrigins: [BoundOrigin.Static]);
        var substituted = c.Binding.InstantiateStorageType(callee.Parameters[0].Type.TypeOf()!, call);
        Assert.NotNull(substituted);
        ReadOnlySpan<PairCase> owner = [new(pair, SemanticsKind.Owner)];
        ReadOnlySpan<PairCase> borrow = [new(pair, SemanticsKind.ObjRef)];
        Assert.Equal(SemanticsKind.ObjRef, c.Binding.CaseType(substituted, owner).Semantics);
        Assert.Equal(SemanticsKind.ObjRef, c.Binding.CaseType(substituted, borrow).Semantics);
    }

    [Fact]
    public void ASubstitutedOuterOriginAppliesToTheResultView()
    {
        var (c, _, pair) = Parse("func take<u/U>(value: u/U)\n    u is objref or ref\n    ()\n" +
            "func f<s/T>(value: s/T)\n    s is objref or ref\n    ()");
        var callee = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.Name == "take");
        var view = c.Binding.AdaptedType(SemanticsKind.ObjRef, BoundType.I32, c.Binding.PairOuterOrigin(pair));
        var type = c.Binding.SemanticsAdaptation(pair, view, BoundOrigin.Static);
        var call = new CallPlan();
        call.Set(callee.SymbolOf()!, BoundType.Unit, null, [0], [type], origins: [BoundOrigin.Static], inputOrigins: [BoundOrigin.Static]);
        var original = callee.Parameters[0].Type.TypeOf()!;
        var written = new BoundType(original.Name, original.Kind, original.Symbol, original.Semantics, origin: BoundOrigin.Static);
        var substituted = c.Binding.InstantiateStorageType(written, call);
        Assert.NotNull(substituted);
        ReadOnlySpan<PairCase> borrow = [new(pair, SemanticsKind.ObjRef)];
        Assert.Same(BoundOrigin.Static, c.Binding.CaseType(substituted, borrow).Origin);
        ReadOnlySpan<PairCase> reference = [new(pair, SemanticsKind.Ref)];
        var wrapped = c.Binding.CaseType(substituted, reference);
        Assert.Same(BoundOrigin.Static, wrapped.Origin);
        Assert.Same(view, wrapped.Components[0]);
    }

    [Fact]
    public void NestedIndependentSelectorsCheckEveryReachableOuterMode()
    {
        const string Source = "func f<s/T, t/V, u/U>(value: u/U)\n    s is owner or obj\n    t is owner or obj\n    u is owner or obj\n    U is ObjectPayload\n" +
            "    let first = value@move@s\n    let second = first@move@t\n    _ = second@move\n" +
            "f<i32, obj/i32, i32>(7)\nf<obj/i32, i32, obj/i32>(8@obj)";
        NativeAllocationAudit.WriteFixture("GenericAdaptationNestedSelectors", Source, 2, 2, 40);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void RepeatedCaseNormalizationReusesInternedTypes()
    {
        var (c, _, pair) = Parse("func f<s/T>(value: s/T)\n    s is object or ref\n    T is ObjectPayload\n    ()");
        var type = c.Binding.SemanticsAdaptation(pair, pair.WholeType!, BoundOrigin.Static);
        PairCase[] objects = [new(pair, SemanticsKind.Obj)];
        PairCase[] references = [new(pair, SemanticsKind.Ref)];
        var objectType = c.Binding.CaseType(type, objects);
        var referenceType = c.Binding.CaseType(type, references);
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(
            () =>
            {
                valid &= ReferenceEquals(type, c.Binding.SemanticsAdaptation(pair, pair.WholeType!, BoundOrigin.Static));
                valid &= ReferenceEquals(objectType, c.Binding.CaseType(type, objects));
                valid &= ReferenceEquals(referenceType, c.Binding.CaseType(type, references));
            },
            iterations: 64,
            warmupIterations: 32));
        Assert.True(valid);
    }

    private static (Compilation Compilation, FunctionKoto Function, BindingSymbol Pair) Parse(string source)
    {
        var c = CompilationTestHelper.ParseSuccess(source, "adaptation-types.kimi");
        Assert.True(c.Bind().IsComplete, string.Join("; ", c.Binding.Issues));
        var function = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.Name == "f");
        return (c, function, function.SymbolOf()!.Schema!.GenericSlots[0].Symbol);
    }
}
