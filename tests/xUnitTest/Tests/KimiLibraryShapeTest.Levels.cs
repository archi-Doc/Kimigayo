// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Reflection;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// PLAN G949 Stage 2 U4 (design §4, 5.7, 7b.3; u4-design): the interners' two levels inside one compilation, the library layer's
// frozen level (filled until the seal, the library table's freeze) and the compilation's own; from U3 (rows s8x, r3a) across the
// compilations that read one frozen library.
public partial class KimiLibraryShapeTest
{
    /// <summary>A program whose `step` resolves `I.(LendingIterator).LentItem(s)` for OwningIterator&lt;Counter&gt; and
    /// BorrowingIterator&lt;Counter&gt; (U3's census finding 4; valid, SPEC 8.9.2): a reading compilation forms hop chains through the library's
    /// frozen conformance paths (r3a; s11, KimiLibraryConcurrencyTest).</summary>
    internal const string HopChainProgram = "struct Counter\n    Self is Iterator\n    associate Iterator.Item is i32\n    public init() => ()\n" +
        "    public func next(self: uniq/Self) -> Option<i32> => .None\n" +
        "func step<I>(it: uniq/I during s) -> Option<I.(LendingIterator).LentItem(s)>\n    I is LendingIterator\n    return it.next()\n" +
        "var i1 = Kimi.Iteration.owning(Counter.init())\nlet x1 = step(i1@uniq)\nvar counter = Counter.init()\n" +
        "var i2 = Kimi.Iteration.borrowing(counter@uniq)\nlet x2 = step(i2@uniq)\n";

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
        Assert.DoesNotContain(Own<(LengthOperation, long, BindingSymbol?, BoundLength?, BoundLength?, BoundType?, BindingSymbol?), BoundLength>(c.Binding, "lengths").Keys, layer.Lengths.ContainsKey);
        foreach (var type in Own<int, List<BoundType>>(c.Types, "types").Values.SelectMany(static x => x))
        {
            Assert.Same(type, c.Types.Rebuild(type, [.. type.Components], type.Origin, [.. type.OriginArguments]));
            Assert.True(type.Id > layer.TypeCount, $"{type}#{type.Id}");
        }

        static bool Interned(object x) => x is BoundType { Id: > BoundType.StaticCount, Kind: not (BoundTypeKind.Parameter or BoundTypeKind.TargetProjection) }
            or Subst or BoundOrigin { Kind: OriginKind.Intersection } or BoundLength or BoundConstraint;
    }

    // C-380 F3 (a) and the frozen bound references (U4): a constraint a program pass interns with a frozen one's content is that object;
    // every frozen bound Contract reference is complete (State 2) after the program pass's ResetContracts and a second Bind. F3 (b) (no
    // frozen non-`Not` constraint lacks its negation) is U2b's row e1 over the same constraints (B's ruling 13).
    [Fact]
    public void AProgramsConstraintEqualToAFrozenOneIsThatObject()
    {
        var c = Bound(string.Empty);
        var layer = c.Library.Layer;
        Assert.NotEmpty(layer.Constraints);
        var intern = typeof(Binding).GetMethod("InternConstraint", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (var (key, constraint) in layer.Constraints.Take(64))
        {
            Assert.Same(constraint, intern.Invoke(c.Binding, [key]));
        }

        c.Bind();
        Assert.Empty(layer.Part.BoundContracts.Values.Where(static x => x.Contract!.State != 2).Select(static x => x.Type!.ToString()));
    }

    // C-380 22:00 C1 (B's ruling 19): a program that proves a frozen constraint the library pass never proved (`Duration is Copy`, the
    // library's own `Self is Copy` in Kimi.Time) forms its negation through FormNegation, which must find it formed at the seal (D-4 over
    // the layer's level). Labels from SPEC before the first run: both valid (SPEC 8.6, Duration conforms to Copy by its declaration; 8.2, a
    // call's clause `T is Copy` holds for Duration; a Copy value is copied by `let`), so Check accepts with no record. Before (7a221d6f6..
    // def7db882, U4 without D-4 over the layer) both threw the refusal's internal-invariant form.
    [Theory]
    [InlineData("let duration = Kimi.Time.Duration.init(microseconds: 5)\nlet copied = duration\nlet again = duration\n")]
    [InlineData("func twice<T>(x: T) -> T\n    T is Copy\n    return x\n\nlet duration = Kimi.Time.Duration.init(microseconds: 5)\nlet other = twice(duration)\n")]
    public void AProgramProvingAFrozenConstraintIsAccepted(string program)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare("x86_64-pc-windows-msvc"));
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, program);
        Assert.True(c.Check());
        Assert.Empty(TestDiagnostics.Of(c));
    }

    // u12 (the seal's refusals; an internal contract, probed by reflection): after the seal, a frozen constraint without its negation and
    // a frozen bound Contract reference that is not complete are broken contracts: negating or re-forming one throws the
    // internal-invariant form and writes nothing onto the frozen object. Before U4 the first wrote a compilation-level `Not` onto the
    // frozen constraint and the second rebuilt the frozen entry in place.
    [Fact]
    public void TheSealRefusesAWriteIntoAFrozenObject()
    {
        var c = BoundOverItsOwnLibrary(string.Empty); // It writes frozen library objects: on a library of its own (x1, X11).
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

    // s8x (F-B3, D-4; PLAN G949 Stage 2 U3): two reading compilations hold one frozen level. The objects reachable from the library's are
    // the same objects from both (the reference objects; a struct is a copy), and a frozen Type re-interned by its parts in either
    // compilation is that object.
    [Fact]
    public void TwoCompilationsShareTheFrozenLevel()
    {
        var (hello, milestone) = (Bound("example/Hello"), Bound("milestone/Milestone26"));
        _ = hello.Analyze();
        _ = milestone.Analyze();
        var hir = hello.Library.Kotonoha.Syntax.Hir;
        var objects = new HashSet<object>(LibraryObjects(hello, hir.ErasedTypes, hir.Meanings).Where(static x => !x.GetType().IsValueType), ReferenceEqualityComparer.Instance);
        Assert.NotEmpty(objects);
        Assert.True(objects.SetEquals(LibraryObjects(milestone, hir.ErasedTypes, hir.Meanings).Where(static x => !x.GetType().IsValueType)));
        var types = hello.Library.Layer.Types.Values.SelectMany(static x => x).Where(static x => x.Kind is not (BoundTypeKind.Parameter or BoundTypeKind.TargetProjection)).ToArray();
        Assert.NotEmpty(types);
        foreach (var type in types)
        {
            Assert.Same(type, hello.Types.Rebuild(type, [.. type.Components], type.Origin, [.. type.OriginArguments]));
            Assert.Same(type, milestone.Types.Rebuild(type, [.. type.Components], type.Origin, [.. type.OriginArguments]));
        }
    }

    // r3a (PLAN G949 Stage 2 U3, Revision 3; B's ruling 36): no compilation writes a frozen library object. Two reading compilations of
    // HopChainProgram (the census's finding 4; TyCtxtTest.ProvenPathsBindTheirCommonType's shape) form hop chains through the library's
    // frozen paths, each its own (Binding.frozenPathHops), and no frozen path's chains hold a step above L (a program's Type). Before
    // Revision 3 the chains were appended to the frozen paths. Hello and Milestone26 form none after the seal.
    [Fact]
    public void NoCompilationExtendsAFrozenPathsHopChains()
    {
        var (first, second) = (MinimalEmissionTest.Analyze(HopChainProgram), MinimalEmissionTest.Analyze(HopChainProgram));
        Assert.True(first.Binding.Result.IsComplete && second.Binding.Result.IsComplete, MinimalEmissionTest.Describe(first));
        var layer = first.Library.Layer;
        var paths = ((System.Collections.IDictionary)typeof(ImplTable).GetField("paths", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(layer.Impls)!).Values.Cast<BoundConformancePath>();
        var frozen = paths.SelectMany(static x => x.HopChains ?? []).ToHashSet<Binding.ResolutionHops>(ReferenceEqualityComparer.Instance);
        Assert.Empty(frozen.Where(x => x.Step.Id > layer.TypeCount).Select(static x => $"{x.Path.Type.Name} is {x.Path.Contract.Name}: {x.Step}"));
        var own = HopsOf(first).Concat(HopsOf(second)).ToArray();
        Assert.NotEmpty(HopsOf(first));
        Assert.NotEmpty(HopsOf(second));
        Assert.DoesNotContain(own, frozen.Contains);
        Assert.Empty(HopsOf(first).Intersect(HopsOf(second), ReferenceEqualityComparer.Instance));

        static IEnumerable<Binding.ResolutionHops> HopsOf(Compilation c)
            => ((System.Collections.IDictionary)typeof(Binding).GetField("frozenPathHops", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c.Binding)!).Values.Cast<Binding.ResolutionHops>();
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
