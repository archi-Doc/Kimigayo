// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>R8-W2 a1 (SPEC 22.10.5, rule L): the interpolation-bound term, a direct interpolation's stack region over its value Types,
/// which TyCtxt.InterpolationBound interns and folds, and every substitution of a Type or of a substitution folds again.</summary>
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

    // A term over T substitutes as a Type does (11 + literal for i32, 0 for string), whether a region's length or an Item's length argument,
    // and Compose reaches it in an inner substitution's lengths, folding it over the outer Types.
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
            values.Select(x => types.Apply(region, types.Synthesized(g, null, [x, BoundType.Boolean]))!).Select(static x => (x.Length, x.LengthExpression)));
        var keep = KotoTree.Walk(c.Kotonoha.RootKoto).Select(static x => x.TypeOf()).First(static x => x is { Kind: BoundTypeKind.FunctionItem })!;
        var item = types.Intern(keep.Kind, keep.Symbol, keep.Semantics, keep.Components.ToArray(), lengthArguments: [term]);
        Assert.True(item.HasAbstractPart && item.ContainsParameter);
        var inner = types.SubstOfItem(item)!;
        Assert.Same(term, inner.Lengths[0]);
        var composed = types.Compose(inner, types.Synthesized(g, null, [BoundType.I32, BoundType.Boolean])).Subst!;
        Assert.Equal((true, 17L), (composed.Lengths[0]!.IsConstant, composed.Lengths[0]!.Value));
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
