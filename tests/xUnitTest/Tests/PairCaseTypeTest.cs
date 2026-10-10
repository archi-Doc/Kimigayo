// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 8.10 Semantics cases: the outer Origin <c>o</c> of a pair binder (SPEC 8.1.1), the complete Type every pair
/// layer takes in one admitted case, the bare-acquisition plan per case (SPEC 8.9) and the binders in scope of a body.</summary>
public class PairCaseTypeTest
{
    private const string Box = "struct Box<E>\n    public var item: E\n    public init(item: E) => self.item = item@move\n";

    [Fact]
    public void CaseTypeFormsTheCompleteTypeOfEachCase()
    {
        var c = Parse(Box + "func f<s/T>(value: s/T, shared: ref/Box<s/T>, applied: s/Box<T>) -> ()\n    s is owner or uniq\n    ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Function(c, "f");
        var target = f.BoundSymbol!.Schema!.GenericSlots[0].Symbol;
        var whole = target.WholeType!;
        var projection = target.Type!;
        var o = c.Binding.PairOuterOrigin(target);
        ReadOnlySpan<PairCase> uniq = [new(target, SemanticsKind.Uniq)];
        ReadOnlySpan<PairCase> owner = [new(target, SemanticsKind.Owner)];

        var exclusive = c.Binding.CaseType(whole, uniq);
        Assert.Equal(BoundTypeKind.Semantics, exclusive.Kind);
        Assert.Equal(SemanticsKind.Uniq, exclusive.Semantics);
        Assert.Same(projection, exclusive.Components[0]);
        Assert.Same(o, exclusive.Origin);
        Assert.Same(exclusive, c.Binding.CaseType(whole, uniq));
        Assert.Same(projection, c.Binding.CaseType(whole, owner));

        var shared = f.Parameters[1].Type.TypeOf()!;
        var sharedUniq = c.Binding.CaseType(shared, uniq);
        Assert.Equal(SemanticsKind.Ref, sharedUniq.Semantics);
        Assert.Same(shared.Origin, sharedUniq.Origin);
        Assert.Same(exclusive, sharedUniq.Components[0].Components[0]);
        Assert.Same(projection, c.Binding.CaseType(shared, owner).Components[0].Components[0]);

        var applied = f.Parameters[2].Type.TypeOf()!;
        Assert.Equal(BoundTypeKind.SemanticsApplication, applied.Kind);
        var appliedUniq = c.Binding.CaseType(applied, uniq);
        Assert.Equal(SemanticsKind.Uniq, appliedUniq.Semantics);
        Assert.Same(applied.Components[0], appliedUniq.Components[0]);
        Assert.Same(applied.Origin, appliedUniq.Origin); // The application's own conditional slot, never W's outer Origin.
        Assert.Same(applied.Components[0], c.Binding.CaseType(applied, owner));

        Assert.Same(BoundType.I32, c.Binding.CaseType(BoundType.I32, uniq));
        Assert.Same(projection, c.Binding.CaseType(projection, uniq));
    }

    [Fact]
    public void NestedPairLayersFormInnerFirst()
    {
        var c = Parse("func g<s/T, t/U>(value: s/(t/U)) -> ()\n    s is value or valueborrow\n    t is valueborrow\n    ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var g = Function(c, "g");
        var outer = g.BoundSymbol!.Schema!.GenericSlots[0].Symbol;
        var inner = g.BoundSymbol.Schema.GenericSlots[1].Symbol;
        var type = g.Parameters[0].Type.TypeOf()!;
        ReadOnlySpan<PairCase> cases = [new(outer, SemanticsKind.Ref), new(inner, SemanticsKind.Uniq)];
        var formed = c.Binding.CaseType(type, cases);
        Assert.Equal(SemanticsKind.Ref, formed.Semantics);
        Assert.Same(type.Origin, formed.Origin);
        Assert.Equal(SemanticsKind.Uniq, formed.Components[0].Semantics);
        Assert.Same(inner.Type, formed.Components[0].Components[0]);
        ReadOnlySpan<PairCase> owners = [new(outer, SemanticsKind.Owner), new(inner, SemanticsKind.Owner)];
        Assert.Same(inner.Type, c.Binding.CaseType(type, owners));
    }

    [Fact]
    public void PairOuterOriginIsOneFixedAtomPerBinder()
    {
        var c = Parse("func f<s/T>(value: s/T, other: s/T) -> ()\n    s is owner or uniq\n    ()\nfunc g<s/T>(value: s/T during a) -> ()\n    s is ref or uniq\n    ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Function(c, "f");
        var g = Function(c, "g");
        var ft = f.BoundSymbol!.Schema!.GenericSlots[0].Symbol;
        var gt = g.BoundSymbol!.Schema!.GenericSlots[0].Symbol;
        var o = c.Binding.PairOuterOrigin(ft);
        Assert.Equal(OriginKind.Parameter, o.Kind);
        Assert.Same(f, o.Binder);
        Assert.Same(ft.Declaration, o.Occurrence);
        Assert.True(o.Slot < 0);
        Assert.NotSame(o, c.Binding.PairOuterOrigin(gt));
        Assert.Same(o, c.Binding.OuterOrigin(ft.WholeType!));
        Assert.Same(o, c.Binding.OuterOrigin(f.Parameters[1].Type.TypeOf()!));
        Assert.Null(ft.WholeType!.Origin);

        // An annotated occurrence is a pair layer whose slot is the annotation (SPEC 8.1.2).
        var annotated = g.Parameters[0].Type.TypeOf()!;
        Assert.True(Binding.TryPairLayer(annotated, out var whole, out var target));
        Assert.Same(gt.WholeType, whole);
        Assert.Same(gt.Type, target);
        Assert.Same(g.BoundSymbol.Schema.Origins[0].Origin, c.Binding.OuterOrigin(annotated));

        Assert.True(c.Bind().IsComplete);
        Assert.Same(o, c.Binding.PairOuterOrigin(Function(c, "f").BoundSymbol!.Schema!.GenericSlots[0].Symbol));
    }

    [Theory]
    [InlineData("s is owner or uniq\n    T is Copy", SemanticsMask.None, SemanticsMask.Uniq)]
    [InlineData("s is owner or uniq", SemanticsMask.Owner, SemanticsMask.Uniq)]
    [InlineData("s is ref or uniq", SemanticsMask.None, SemanticsMask.Uniq)]
    [InlineData("s is uniq", SemanticsMask.None, SemanticsMask.Uniq)]
    [InlineData("s is value or valueborrow\n    T is Copy", SemanticsMask.None, SemanticsMask.Uniq)]
    [InlineData("s is owner or rc\n    T is Copy", SemanticsMask.Rc, SemanticsMask.None)]
    [InlineData("s is objref or objuniq", SemanticsMask.None, SemanticsMask.ObjUniq)]
    public void BareAcquisitionPlansEachAdmittedCase(string premises, SemanticsMask failing, SemanticsMask reborrow)
    {
        var c = Parse($"func f<s/T>(value: s/T) -> ()\n    {premises}\n    ()");
        c.Bind();
        var f = Function(c, "f");
        var type = f.Parameters[0].Type.TypeOf()!;
        Assert.Equal(failing, c.Binding.BareAcquisition(type, f.Parameters[0].Type, out var reborrowed));
        Assert.Equal(reborrow, reborrowed);
        Assert.Equal(SemanticsMask.None, c.Binding.BareAcquisition(BoundType.I32, f.Parameters[0].Type, out var none));
        Assert.Equal(SemanticsMask.None, none);
    }

    [Fact]
    public void PairBindersListsTheScopeChainOuterFirst()
    {
        var c = Parse("struct Holder<h/H>\n    h is value or valueborrow\n    public var item: h/H\n    public init(item: h/H) => self.item = item@move\n" +
            "    public func peek<s/T, u/V>(self: ref/Self, value: s/T, other: u/V) -> ()\n        s is owner or uniq\n        u is owner or rc\n        let k = func [value] () => ()\n        ()\n");
        c.Bind();
        var peek = Function(c, "peek");
        var binders = new List<PairBinder>();
        c.Binding.PairBinders(peek, binders);
        Assert.Equal(3, binders.Count);
        Assert.Equal("H", binders[0].Target.Name);
        Assert.Equal(SemanticsMask.Owner | SemanticsMask.ValueBorrow, binders[0].Admitted);
        Assert.True(binders[0].Resolved);
        Assert.Equal("T", binders[1].Target.Name);
        Assert.Equal(SemanticsMask.Owner | SemanticsMask.Uniq, binders[1].Admitted);
        Assert.True(binders[1].Resolved);
        Assert.Equal("V", binders[2].Target.Name);
        Assert.Equal(SemanticsMask.Owner | SemanticsMask.Rc, binders[2].Admitted);
        Assert.True(binders[2].Resolved);

        var closure = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.IsAnonymous);
        c.Binding.PairBinders(closure, binders);
        Assert.Equal(3, binders.Count);
        Assert.Equal("H", binders[0].Target.Name);
        Assert.Equal("T", binders[1].Target.Name);
    }

    // SPEC 8.1.1, 15.6.1: in the admitted borrow cases a pair layer is a borrow of its target within its outer-Origin slot, so the
    // target's Origins outlive that slot by well-formedness; a callee premise `a outlives b` is proven from `v: s/Box<ref/i32 during a> during b`.
    [Fact]
    public void PairLayerWellFormednessProvesACalleePremise()
    {
        var c = Parse(Box + "func wrap(p: ref/Box<ref/i32 during x> during y) -> ref/i32 during x\n    return p.item\n" +
            "func f<s/T>(v: s/Box<ref/i32 during a> during b) -> ref/i32 during a\n    s is ref or uniq\n    return wrap(v@follow@ref)\n");
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    private static Compilation Parse(string source)
        => CompilationTestHelper.ParseSuccess(source, "cases.kimi");

    private static FunctionKoto Function(Compilation c, string name)
        => Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => !x.IsAnonymous && x.Name == name);

    private static string Describe(Compilation c) => string.Join("; ", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node}"));
}
