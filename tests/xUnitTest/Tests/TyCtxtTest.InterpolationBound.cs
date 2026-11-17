// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>R8-W2 a1 (SPEC 22.10.5, rule L): the interpolation-bound term, a direct interpolation's stack region over its value Types,
/// which TyCtxt.InterpolationBound interns and folds, and every substitution of a Type or of a substitution folds again; R8-W2 c2t: the
/// interpolation-hint term, a write position's hint over the later value Types (TyCtxt.InterpolationHint), folded again by its own
/// operation at both value-Type sites (TyCtxt.Refold).</summary>
public sealed partial class TyCtxtTest
{
    private const string BoundSource = "func keep<length N>(a: [N of u8]) -> [N of u8] => a\nfunc g<T, U>(x: T, y: Option<T>, z: U) -> i32 => 0\nlet f = keep<2>\n";

    // A closed term is its constant: the literal bytes plus each value's widest encoding within 1,024 bytes (exactly 1,024 included), else
    // 0, as for an unbounded value Type; a closed value over the limit folds even beside an undecided one (T), and so does a decidably
    // unbounded construction (Option<T>); only undecided values with the rest within the limit make a term, interned once per value Types
    // and literal, erased and abstract.
    [Fact]
    public void AnInterpolationBoundFoldsWhereItsValueTypesDecideIt()
    {
        var (c, t, u, option) = BoundTypes();
        var types = c.Types;
        long Fold(long literal, params BoundType[] values) => types.InterpolationBound(types.Tuple(values), literal) is { IsConstant: true } bound ? bound.Value : -1;
        long[] folded =
        [
            Fold(6, BoundType.I32, BoundType.Boolean), Fold(1013, BoundType.I32), Fold(1014, BoundType.I32), Fold(6, BoundType.String),
            Fold(0, BoundType.WrappingOf(BoundType.Primitives["u8"]), BoundType.Unit, BoundType.Char, BoundType.F32, BoundType.F64), Fold(1020, t, BoundType.I32),
            Fold(6, option, t), Fold(1025, t), Fold(6, t, BoundType.I32),
        ];
        Assert.Equal([6 + 11 + 5, 1024, 0, 0, 3 + 2 + 4 + 17 + 24, 0, 0, 0, -1], folded);
        var term = types.InterpolationBound(types.Tuple([t, BoundType.I32]), 6);
        Assert.Equal((LengthOperation.InterpolationBound, 6L, false, true), (term.Operation, term.Value, term.IsConstant, term.HasAbstractPart));
        Assert.Same(types.Tuple([t, BoundType.I32]), term.Values);
        Assert.Same(term, types.InterpolationBound(types.Tuple([t, BoundType.I32]), 6));
        Assert.NotSame(term, types.InterpolationBound(types.Tuple([u, BoundType.I32]), 6));
        var region = types.Intern(BoundTypeKind.FixedArray, null, SemanticsKind.Owner, [BoundType.Primitives["u8"]], lengthExpression: term);
        Assert.True(region.HasAbstractPart && region.ContainsParameter);
        Assert.Equal("[(6 + bound(T, i32)) of u8]", TyCtxt.Name(region));
    }

    // A region's term over T substitutes as a Type does (11 + literal for i32, 0 for string), and Compose reaches it in an inner
    // substitution's lengths (made here through an Item), folding it over the outer instance.
    [Fact]
    public void ASubstitutionFoldsAnInterpolationBound()
    {
        var (c, t, _, _) = BoundTypes();
        var types = c.Types;
        var g = types.Identity(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "g")).Target;
        var term = types.InterpolationBound(types.Tuple([t]), 6);
        var region = types.Intern(BoundTypeKind.FixedArray, null, SemanticsKind.Owner, [BoundType.Primitives["u8"]], lengthExpression: term);
        BoundType[] values = [BoundType.I32, BoundType.String];
        Assert.Equal(
            [(17L, (BoundLength?)null), (0L, null)],
            values.Select(x => types.Apply(region, types.Synthesized(g, null, [x, BoundType.Boolean], []))!)
                .Select(static x => (x.Length, x.LengthExpression)));
        var keep = KotoTree.Walk(c.Kotonoha.RootKoto).Select(static x => x.TypeOf()).First(static x => x is { Kind: BoundTypeKind.FunctionItem })!;
        var item = types.Intern(keep.Kind, keep.Symbol, keep.Semantics, keep.Components.ToArray(), lengthArguments: [term]);
        var inner = types.SubstOfItem(item)!;
        Assert.Same(term, inner.Lengths[0]);
        var composed = types.Compose(inner, types.Synthesized(g, null, [BoundType.I32, BoundType.Boolean], [])).Subst!;
        Assert.Equal((true, 17L), (composed.Lengths[0]!.IsConstant, composed.Lengths[0]!.Value));
    }

    // A term over an associated projection folds over its normalized instance, through Apply and Compose: S.Item is i32 for an IntItem
    // (11 + literal) and string for a TextItem (0); a requirement's Self.Item alike under its conforming Self.
    [Fact]
    public void AProjectionTermFoldsInItsNormalizedInstance()
    {
        var c = MinimalEmissionTest.Analyze(
            "contract HasItem\n    associate Item\n    func take(self: ref/Self, item: Self.Item) -> i32\n" +
            "struct IntItem\n    Self is HasItem\n    associate HasItem.Item is i32\n    public func take(self: ref/Self, item: i32) -> i32 => item\n" +
            "struct TextItem\n    Self is HasItem\n    associate HasItem.Item is string\n    public func take(self: ref/Self, item: string) -> i32 => 0\n" +
            "func h<S>(x: S.Item, a: IntItem, b: TextItem) -> i32\n    S is HasItem\n    return 0\n" + BoundSource);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var types = c.Types;
        var nodes = KotoTree.Walk(c.Kotonoha.RootKoto).ToArray();
        var h = nodes.OfType<FunctionKoto>().Single(static x => x.Name == "h");
        var parameters = h.Parameters.Select(static x => x.Type.TypeOf()!).ToArray();
        var target = types.Identity(h).Target;
        var u8 = BoundType.Primitives["u8"];
        var term = types.InterpolationBound(types.Tuple([parameters[0]]), 6);
        var region = types.Intern(BoundTypeKind.FixedArray, null, SemanticsKind.Owner, [u8], lengthExpression: term);
        var keep = nodes.Select(static x => x.TypeOf()).First(static x => x is { Kind: BoundTypeKind.FunctionItem })!;
        var inner = types.SubstOfItem(types.Intern(keep.Kind, keep.Symbol, keep.Semantics, keep.Components.ToArray(), lengthArguments: [term]))!;
        long[] folded =
        [
            .. parameters[1..].SelectMany(x => new[]
            {
                types.Apply(region, types.Synthesized(target, null, [x], []))!.Length,
                types.Compose(inner, types.Synthesized(target, null, [x], [])).Subst!.Lengths[0]!.Value,
            }),
        ];
        Assert.Equal([17, 17, 0, 0], folded);
        var take = nodes.OfType<FunctionKoto>().Single(static x => x is { Name: "take", IsRequirement: true });
        var selfTerm = types.InterpolationBound(types.Tuple([take.Parameters[1].Type.TypeOf()!]), 6);
        var selfRegion = types.Intern(BoundTypeKind.FixedArray, null, SemanticsKind.Owner, [u8], lengthExpression: selfTerm);
        var contract = types.Identity(nodes.OfType<ContractKoto>().Single()).Target.Type!;
        Assert.Equal(17, types.Apply(selfRegion, types.Synthesized(types.Identity(take).Target, parameters[1], contract))!.Length);

        // A hint over S.Item folds alike through InstanceLength, its instance read: 11 + literal for IntItem, the literal for TextItem.
        var hint = types.InterpolationHint(types.Tuple([parameters[0]]), 6);
        (bool, long)[] hints =
        [
            .. parameters[1..].Select(x => c.Binding.InstanceLength(hint, types.Synthesized(target, null, [x], []))!)
                .Select(static x => (x.IsConstant, x.Value)),
        ];
        Assert.Equal([(true, 17L), (true, 6L)], hints);
    }

    // R8-W2 c2t: the interpolation-hint term, the bytes an owning interpolation's later writes may still reserve (SPEC 22.10.5; R3-10):
    // the later literal bytes plus each later bounded value's widest encoding; an unbounded value counts 0 (where the bound term gives 0),
    // with no limit, and 0 past the maximum object size (2^63 - 1), even beside an undecided value. A term only beside an undecided value,
    // interned once per value Types and literal, apart from the bound term over the same ones, and spelled as a hint.
    [Fact]
    public void AnInterpolationHintFoldsWhereItsValueTypesDecideIt()
    {
        var (c, t, u, option) = BoundTypes();
        var types = c.Types;
        long Fold(long literal, params BoundType[] values)
            => types.InterpolationHint(types.Tuple(values), literal) is { IsConstant: true } hint ? hint.Value : -1;
        long[] folded =
        [
            Fold(6, BoundType.I32, BoundType.Boolean), Fold(6, BoundType.String), Fold(6, BoundType.String, BoundType.I32), Fold(1025, BoundType.I32),
            Fold(long.MaxValue - 11, BoundType.I32), Fold(long.MaxValue - 10, BoundType.I32), Fold(6, option), Fold(6, option, t),
            Fold(long.MaxValue - 10, t, BoundType.I32), Fold(0),
        ];
        Assert.Equal([6 + 11 + 5, 6, 6 + 11, 1025 + 11, long.MaxValue, 0, 6, -1, 0, 0], folded);
        var term = types.InterpolationHint(types.Tuple([t, BoundType.I32]), 6);
        Assert.Equal((LengthOperation.InterpolationHint, 6L, false, true), (term.Operation, term.Value, term.IsConstant, term.HasAbstractPart));
        Assert.Same(types.Tuple([t, BoundType.I32]), term.Values);
        Assert.Same(term, types.InterpolationHint(types.Tuple([t, BoundType.I32]), 6));
        Assert.NotSame(term, types.InterpolationHint(types.Tuple([u, BoundType.I32]), 6));
        Assert.NotSame(term, types.InterpolationBound(types.Tuple([t, BoundType.I32]), 6));
        Assert.Equal("(6 + hint(T, i32))", Binding.DiagnosticLengthName(term));
    }

    // The hint term's substitution oracle at both value-Type sites, Binding.SubstituteLength and Binding.InstanceLength (R3-N4): a hint over
    // T is 11 + literal at i32 and the literal alone at string; a literal past the maximum object size with i32 gives 0, with string it
    // stays; an image still undecided (T to U) stays a hint over U, never a bound; the bound term over T keeps a1's 17 at i32.
    [Fact]
    public void ASubstitutionFoldsAnInterpolationHint()
    {
        var (c, t, u, _) = BoundTypes();
        var types = c.Types;
        var g = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "g");
        var target = types.Identity(g).Target;
        var hint = types.InterpolationHint(types.Tuple([t]), 6);
        var wide = types.InterpolationHint(types.Tuple([t]), long.MaxValue - 10);
        (LengthOperation, long)[] At(BoundLength term, BoundType value)
        {
            BoundType?[] slots = [value, BoundType.Boolean];
            BoundLength[] images =
                [c.Binding.SubstituteLength(term, g, slots, [])!, c.Binding.InstanceLength(term, types.Synthesized(target, null, slots, []))!];
            Assert.All(images, x => Assert.True(x.IsConstant || ReferenceEquals(x.Values, types.Tuple([u]))));
            return [.. images.Select(static x => (x.Operation, x.Value))];
        }

        (LengthOperation, long)[] folded =
        [
            .. At(hint, BoundType.I32), .. At(hint, BoundType.String), .. At(wide, BoundType.I32), .. At(wide, BoundType.String), .. At(hint, u),
            .. At(types.InterpolationBound(types.Tuple([t]), 6), BoundType.I32),
        ];
        const LengthOperation Constant = LengthOperation.Constant;
        const LengthOperation Hint = LengthOperation.InterpolationHint;
        (LengthOperation, long)[] expected =
        [
            (Constant, 17), (Constant, 17), (Constant, 6), (Constant, 6), (Constant, 0), (Constant, 0), (Constant, long.MaxValue - 10),
            (Constant, long.MaxValue - 10), (Hint, 6), (Hint, 6), (Constant, 17), (Constant, 17),
        ];
        Assert.Equal(expected, folded);
    }

    // A warm hint and its refolds are the same lengths and allocate nothing (c2b's builder and the collector's instance read): the open and
    // the closed producer, and both value-Type sites at i32.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmInterpolationHintIsTheSameAndAllocatesNothing()
    {
        var (c, t, _, _) = BoundTypes();
        var types = c.Types;
        var g = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "g");
        var open = types.Tuple([t, BoundType.I32]);
        var closed = types.Tuple([BoundType.I32]);
        BoundType?[] slots = [BoundType.I32, BoundType.Boolean];
        var subst = types.Synthesized(types.Identity(g).Target, null, slots, []);
        var hint = types.InterpolationHint(open, 6);
        BoundLength[] first =
            [hint, types.InterpolationHint(closed, 6), c.Binding.SubstituteLength(hint, g, slots, [])!, c.Binding.InstanceLength(hint, subst)!];
        var changed = 0;
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            changed += ReferenceEquals(types.InterpolationHint(open, 6), first[0]) ? 0 : 1;
            changed += ReferenceEquals(types.InterpolationHint(closed, 6), first[1]) ? 0 : 1;
            changed += ReferenceEquals(c.Binding.SubstituteLength(hint, g, slots, []), first[2]) ? 0 : 1;
            changed += ReferenceEquals(c.Binding.InstanceLength(hint, subst), first[3]) ? 0 : 1;
        }));
        Assert.Equal(0, changed);
        long[] values = [.. first[1..].Select(static x => x.Value)];
        Assert.Equal([6 + 11, 6 + 11 + 11, 6 + 11 + 11], values);
    }

    // CompareLength orders a commutative operation's term operands by a key of their value Types (count, then kind and name), never by
    // reference, so either order interns one sum with the same operands in the same places.
    [Fact]
    public void ASumOfTermsHasOneOrder()
    {
        var (c, t, u, _) = BoundTypes();
        var types = c.Types;
        var (one, two) = (types.InterpolationBound(types.Tuple([u]), 6), types.InterpolationBound(types.Tuple([t]), 6));
        var wide = types.InterpolationBound(types.Tuple([t, u]), 6);
        foreach (var (first, second) in new[] { (one, two), (wide, two) })
        {
            var sum = c.Binding.InternLength(LengthOperation.Add, left: first, right: second);
            Assert.Same(sum, c.Binding.InternLength(LengthOperation.Add, left: second, right: first));
            Assert.Same(second, sum.Left);
        }
    }

    // The Types the rows above read: g's parameters T and U and Option<T>.
    private static (Compilation C, BoundType T, BoundType U, BoundType Option) BoundTypes()
    {
        var c = MinimalEmissionTest.Analyze(BoundSource);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var g = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "g");
        return (c, g.Parameters[0].Type.TypeOf()!, g.Parameters[2].Type.TypeOf()!, g.Parameters[1].Type.TypeOf()!);
    }
}
