// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Reflection;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// PLAN G949 Stage 2 U4 (design §4, 5.7, 7b.3; u4-design): the interners' two levels inside one compilation, the library layer's
// frozen level (filled until the seal, the library table's freeze) and the compilation's own.
public partial class KimiLibraryShapeTest
{
    // s8 within one compilation (U4; C-380 F3 (a)): after the first Bind of each of c2's programs, a second Bind and Analyze, every
    // interned object a library object holds (a Type other than the statics and the parameter Types, a substitution, an Origin
    // intersection, a length, a constraint) is in the layer's frozen level; the frozen levels hold the objects they held after the first
    // Bind (nothing inserted after the seal); and the compilation's level holds no content of the frozen level (the constraints' and
    // lengths' keys, and each compilation-level Type re-interned by its parts is itself). Before U4 there is no frozen level.
    [Theory]
    [MemberData(nameof(ReferencePrograms))]
    public void TheLibrarysInternedObjectsAreTheFrozenLevel(string program)
    {
        var c = Bound(program);
        var layer = c.Library.Layer;
        var frozen = FrozenLevel(layer);
        c.Bind();
        _ = c.Analyze();
        Assert.Equal(frozen.Count, FrozenLevel(layer).Count);
        var hir = c.Library.Kotonoha.Syntax.Hir;
        var objects = LibraryObjects(c, hir.ErasedTypes, hir.Meanings);
        Assert.Empty(objects.Where(x => Interned(x) && !frozen.Contains(x)).Select(static x => $"{x.GetType().Name} {x}").Distinct());

        // u5 (ruling 7): the library pass interns Origin intersections, at the frozen level. u4: it forms no substitution (on S1-1's
        // c58b9247d every one is formed after the seal, at MIR time), so a program's substitutions are at the compilation's level, which
        // the empty program leaves empty; none is in the frozen level (u1's walk).
        Assert.NotEmpty(layer.Intersections);
        Assert.Equal(program.Length == 0, Own<int, List<Subst>>(c.Types, "substs").Count == 0);
        Assert.DoesNotContain(Own<ConstraintKey, BoundConstraint>(c.Binding, "constraints").Keys, layer.Constraints.ContainsKey);
        Assert.DoesNotContain(Own<(LengthOperation, long, BindingSymbol?, BoundLength?, BoundLength?, BoundType?), BoundLength>(c.Binding, "lengths").Keys, layer.Lengths.ContainsKey);
        foreach (var type in Own<int, List<BoundType>>(c.Types, "types").Values.SelectMany(static x => x))
        {
            Assert.Same(type, c.Types.Rebuild(type, [.. type.Components], type.Origin, [.. type.OriginArguments]));
            Assert.True(type.Id > layer.TypeCount, $"{type}#{type.Id}");
        }

        static bool Interned(object x) => x is BoundType { Id: > BoundType.StaticCount, Kind: not (BoundTypeKind.Parameter or BoundTypeKind.TargetProjection) }
            or Subst or BoundOrigin { Kind: OriginKind.Intersection } or BoundLength or BoundConstraint;
    }

    // C-380 F3 (a) and (b), and the frozen bound references (U4): a constraint a program pass interns with a frozen one's content is that
    // object; no frozen non-`Not` constraint lacks its negation (O-U2-4's reading, U2b's eager step asserted here); every frozen bound
    // Contract reference is complete (State 2) after the program pass's ResetContracts and a second Bind.
    [Fact]
    public void AProgramsConstraintEqualToAFrozenOneIsThatObject()
    {
        var c = Bound(string.Empty);
        var layer = c.Library.Layer;
        Assert.NotEmpty(layer.Constraints);
        Assert.Empty(layer.Constraints.Values.Where(static x => x.Kind != ConstraintKind.Not && x.Negation is null).Select(static x => x.ToString()));
        var intern = typeof(Binding).GetMethod("InternConstraint", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (var (key, constraint) in layer.Constraints.Take(64))
        {
            Assert.Same(constraint, intern.Invoke(c.Binding, [key]));
        }

        c.Bind();
        Assert.Empty(layer.Part.BoundContracts.Values.Where(static x => x.Contract!.State != 2).Select(static x => x.Type!.ToString()));
    }

    // u12 (the seal's refusals; an internal contract, probed by reflection): after the seal, a frozen constraint without its negation and
    // a frozen bound Contract reference that is not complete are broken contracts: negating or re-forming one throws the
    // internal-invariant form and writes nothing onto the frozen object. Before U4 the first wrote a compilation-level `Not` onto the
    // frozen constraint and the second rebuilt the frozen entry in place.
    [Fact]
    public void TheSealRefusesAWriteIntoAFrozenObject()
    {
        var c = Bound(string.Empty);
        var layer = c.Library.Layer;
        var constraint = layer.Constraints.Values.First(static x => x.Kind != ConstraintKind.Not);
        constraint.Negation = null;
        var negate = typeof(Binding).GetMethod("NegateConstraint", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var thrown = Assert.Throws<TargetInvocationException>(() => negate.Invoke(c.Binding, [constraint]));
        Assert.Contains("InternalInvariant", thrown.InnerException!.Message);
        Assert.Null(constraint.Negation);

        var (reference, bound) = layer.Part.BoundContracts.First();
        bound.Contract!.State = 0;
        var form = typeof(Binding).GetMethod("BoundContractReference", BindingFlags.Instance | BindingFlags.NonPublic)!;
        thrown = Assert.Throws<TargetInvocationException>(() => form.Invoke(c.Binding, [reference]));
        Assert.Contains("InternalInvariant", thrown.InnerException!.Message);
    }

    private static HashSet<object> FrozenLevel(Kimi.Compiler.LibraryLayer layer)
    {
        var set = new HashSet<object>(ReferenceEqualityComparer.Instance);
        set.UnionWith(layer.Types.Values.SelectMany(static x => x));
        set.UnionWith(layer.Substs.Values.SelectMany(static x => x));
        set.UnionWith(layer.Intersections.Values.SelectMany(static x => x));
        set.UnionWith(layer.Lengths.Values);
        set.UnionWith(layer.Constraints.Values);
        return set;
    }

    private static Dictionary<TKey, TValue> Own<TKey, TValue>(object owner, string field)
        where TKey : notnull
        => (Dictionary<TKey, TValue>)owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
}
