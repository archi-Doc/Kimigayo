// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Xunit;

namespace XunitTest;

/// <summary>The Type facts A5 reads (docs/dev/DECISIONS.md R4-M36, R4-M73): which values may hold a Loan and what a call may write past them
/// (<see cref="MirTypes.HoldsLoans"/>), and which destructions may observe what they hold (<see cref="MirTypes.Observes"/>), over the
/// parameter Types of one function.</summary>
public sealed class MirTypesTest(ITestOutputHelper output)
{
    private const string Types = "struct Plain\n    var n: i32\nstruct Holder {a}\n    let r: ref/i32 during a\n" +
        "struct Logger {sink}\n    let out: uniq/i32 during sink\n    drop\n        _ = self.out@follow\n" +
        "open struct Base\n    var n: i32\nstruct Chain\n    var next: Option<obj/Chain>\nstruct Tree\n    var children: Array<Tree>\n" +
        "struct Nest<T>\n    var value: T\n    var next: Option<rc/Nest<(T, T)>>\n" +
        "struct Forest {a}\n    let r: ref/i32 during a\n    var children: Array<Forest during a>\n" +
        "struct Table\n    var rows: Dictionary<i32, Table>\nstruct Logged\n    var n: i32\n    var children: Array<Logged>\n    drop\n        _ = self.n\n";

    // SPEC 15.6, 3.2.2, 15.3.5: a value may hold a Loan where a part of its own storage is a borrow, pair or raw layer (a raw pointer
    // carries the holders it addresses and its Loans, SPEC 15.6 Addresses), a Slice, a Function value or an abstract Type, or a Type
    // argument its storage may hold (a library collection behind a raw pointer, a Loan); scalars, strings and Origin-free aggregates hold
    // none. A repeating recursive Type ends exactly, a growing one at the walk's bound, conservatively.
    [Fact]
    public void LoansAreHeldByBorrowsAndWhatMayStoreThem()
    {
        var (body, c) = Probe(
            "func probe<s/T>(scalar: i32, text: string, numbers: Array<i32>, plain: Plain, raw: raw/(ref/i32 during a), chain: Chain, " +
            "tree: Tree, forest: Forest, rows: Table, borrow: ref/i32, borrows: Array<ref/i32 during a>, tuple: (i32, ref/i32 during a), " +
            "fixed: [2 of ref/i32 during a], table: Dictionary<i32, ref/i32 during a>, option: Option<ref/i32 during a>, holder: Holder, " +
            "handle: obj/Holder, slice: Slice<i32>, view: UniqSlice<i32>, loan: Loan<ref/i32 during a>, " +
            "iterator: ArrayOwningIterator<ref/i32 during a>, action: () -> (), layer: s/T, nest: Nest<i32>)\n    s is owner or ref\n" +
            "    return\n");
        var seen = new Dictionary<BoundType, byte>(ReferenceEqualityComparer.Instance);
        Assert.Equal(
            [("scalar", false), ("text", false), ("numbers", false), ("plain", false), ("raw", true), ("chain", false), ("tree", false),
                ("forest", true), ("rows", false),
                ("borrow", true), ("borrows", true), ("tuple", true), ("fixed", true), ("table", true), ("option", true),
                ("holder", true), ("handle", true), ("slice", true), ("view", true), ("loan", true), ("iterator", true), ("action", true),
                ("layer", true), ("nest", true)],
            Parameters(body).Select(x => (x.Name, MirTypes.HoldsLoans(x.Type, c.Types, seen, out _))).ToArray());
    }

    // A3 E16 (SPEC 15.6, 15.6.2, 15.6.4; DECISIONS R4-M73, R4-M116): a call may write past a value that may hold, past no shared layer,
    // an exclusive reference to storage that may hold a Loan: an exclusive reference, in an Array, a payload or a Field, a UniqSlice's
    // elements, a Function value, a raw parameter's pointee (the guard); only through a Type argument, a pair layer or an abstract part,
    // which may be one (P13's interim, R4-M76); not past a shared reference, or through an exclusive reference or a raw pointer to
    // Loan-free storage (a scalar, Utf8Writer's Loan<uniq/u8> and raw Fields). A stored raw layer is classed by its step (design B′
    // §4.4): a slot kept only by `ref` writes nothing past it, another slot its pointee by argument, `during self` the holding value's own
    // storage, and an unstated one an exclusive reference to its pointee.
    [Fact]
    public void TheGuardSaysWhatACallMayWritePastAValue()
    {
        var (body, c) = Probe(
            "struct Slot {a}\n    var r: uniq/(ref/i32 during a) during a\n" +
            "struct RefKept<E> {source}\n    let storage: raw/E during source\n    let loan: Loan<ref/E during source>\n" +
            "struct UniqKept<E> {source}\n    let storage: raw/E during source\n    let loan: Loan<uniq/E during source>\n" +
            "struct Owned<E>\n    let storage: raw/E during self\n" +
            "struct Cursor<E>\n    let p: raw/E\n" +
            "func probe<s/T>(borrow: ref/i32, scalar: uniq/i32, writer: Utf8Writer, shared: ref/(uniq/(ref/i32 during a) during a), " +
            "unique: uniq/(ref/i32 during a), array: Array<uniq/(ref/i32 during a) during a>, " +
            "option: Option<uniq/(ref/i32 during a) during a>, slot: Slot, view: UniqSlice<ref/i32 during a>, scalars: UniqSlice<i32>, " +
            "action: () -> (), toRaw: uniq/(raw/(ref/i32 during a)), value: T, layer: s/T, iterator: ArrayIterator<T>, " +
            "loan: Loan<uniq/(ref/i32 during a) during a>, raw: raw/(ref/i32 during a), rawScalar: raw/i32, number: i32, " +
            "refKept: RefKept<ref/i32 during a>, uniqKept: UniqKept<ref/i32 during a>, owned: Owned<ref/i32 during a>, " +
            "ownedUniq: Owned<uniq/(ref/i32 during a) during a>, cursor: Cursor<ref/i32 during a>)\n" +
            "    s is owner or uniq\n    return\n");
        var seen = new Dictionary<BoundType, byte>(ReferenceEqualityComparer.Instance);
        Assert.Equal(
            [("borrow", MirWrites.None), ("scalar", MirWrites.None), ("writer", MirWrites.None), ("shared", MirWrites.None),
                ("unique", MirWrites.Through), ("array", MirWrites.Through), ("option", MirWrites.Through), ("slot", MirWrites.Through),
                ("view", MirWrites.Through), ("scalars", MirWrites.None), ("action", MirWrites.Through), ("toRaw", MirWrites.Through),
                ("value", MirWrites.ThroughArgument), ("layer", MirWrites.ThroughArgument), ("iterator", MirWrites.ThroughArgument),
                ("loan", MirWrites.ThroughArgument), ("raw", MirWrites.Through), ("rawScalar", MirWrites.None), ("number", MirWrites.None),
                ("refKept", MirWrites.None), ("uniqKept", MirWrites.ThroughArgument), ("owned", MirWrites.None),
                ("ownedUniq", MirWrites.Through), ("cursor", MirWrites.Through)],
            Parameters(body).Select(x => (x.Name, Writes(x.Type))).ToArray());

        MirWrites Writes(BoundType type)
        {
            _ = MirTypes.HoldsLoans(type, c.Types, seen, out var writes);
            return writes;
        }
    }

    // SPEC 15.6.6, 16.3.3: destroying a value observes what it holds where its destruction runs a user drop (Logger, also as an element
    // or a Sealed handle's payload), destroys a Function value or an abstract part not proven Copy, or releases a handle whose payload is
    // not Sealed; a Sealed recursive handle ends. A part past the walk's depth bound, also below a growing recursive handle, is deep: the
    // caller's located Unsupported.
    [Fact]
    public void DestructionObservesUserDropsAndUnknownParts()
    {
        var (body, c) = Probe(
            "func probe<T, C>(holder: Holder, logger: Logger, loggers: Array<Logger>, owned: obj/Logger, shared: rc/Plain, atomic: arc/Plain, " +
            "based: obj/Base, action: () -> (), value: T, copied: C, chain: Chain, tree: Tree, forest: Forest, table: Table, logged: Logged, " +
            "nest: Nest<i32>)\n    C is Copy\n    return\n");
        var seen = new HashSet<BoundType>(ReferenceEqualityComparer.Instance);
        Assert.Equal(
            [("holder", false, false), ("logger", true, false), ("loggers", true, false), ("owned", true, false), ("shared", false, false),
                ("atomic", false, false), ("based", true, false), ("action", true, false), ("value", true, false), ("copied", false, false),
                ("chain", false, false), ("tree", false, false), ("forest", false, false), ("table", false, false), ("logged", true, false),
                ("nest", true, true)],
            Parameters(body).Select(x => (x.Name, MirTypes.Observes(x.Type, c.Types, body.Key.Decl, seen, out var deep), deep)).ToArray());
    }

    // SPEC 8.11.2 (DECISIONS R4-M58): a value holds raw authority where a part is a raw pointer or an rc handle; each Type is walked once
    // per query, so a repeating recursive Type is not raw-holding for its repeat, and a growing one still reaches the bound.
    [Fact]
    public void RawAuthorityWalksEachTypeOnce()
    {
        var body = Probe("func probe(tree: Tree, table: Table, chain: Chain, logged: Logged, raw: raw/i32, nest: Nest<i32>)\n    return\n").Body;
        var seen = new HashSet<BoundType>(ReferenceEqualityComparer.Instance);
        Assert.Equal(
            [("tree", false), ("table", false), ("chain", false), ("logged", false), ("raw", true), ("nest", true)],
            Parameters(body).Select(x => (x.Name, MirTypes.HoldsRaw(x.Type, seen))).ToArray());
    }

    // V16 (DECISIONS R4-M58): the destruction walk enters each Type once per query, so a Type several parts share, nested pairs 25 deep
    // (2^25 leaves as a tree), is walked in work linear in its distinct Types, and so are the facts over it and the analyses of a body
    // that holds one.
    [Fact]
    public void ADestructionWalkIsLinearInDistinctTypes()
    {
        var text = new StringBuilder("struct P0\n    var a: i32\n    var b: i32\n");
        for (var i = 1; i <= 24; i++)
        {
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"struct P{i}\n    var a: P{i - 1}\n    var b: P{i - 1}\n");
        }

        var (body, c) = Probe(text.Append("func probe(pairs: P24)\n    var local: P24 = pairs\n    return\n").ToString());
        var pairs = Parameters(body).Single().Type;
        var seen = new HashSet<BoundType>(ReferenceEqualityComparer.Instance);
        var walk = new Walk(seen);
        Assert.False(MirTypes.Destruction(pairs, ref walk));
        Assert.Equal((26, 51), (seen.Count, walk.Met)); // P24..P0 and i32; each struct's two Fields met once each.
        Assert.False(MirTypes.MayNeedDrop(pairs, seen));
        Assert.False(MirTypes.Observes(pairs, c.Types, body.Key.Decl, seen, out var deep) || deep);
        Assert.False(MirTypes.HoldsRaw(pairs, seen) || MirTypes.HoldsLoans(pairs, c.Types, new(ReferenceEqualityComparer.Instance), out _));
    }

    // R4-M36, R4-M116: every local Type of every library body is classified and none is deep; a borrow or raw layer always holds a Loan.
    // The output gives the counts.
    [Fact]
    public void LibraryTypesClassify()
    {
        var c = MirBuildTest.Compile("public func main() => ()\n");
        var seen = new HashSet<BoundType>(ReferenceEqualityComparer.Instance);
        var walked = new Dictionary<BoundType, byte>(ReferenceEqualityComparer.Instance);
        var (locals, holding, observing) = (0, 0, 0);
        foreach (var body in c.BuildMir().Bodies)
        {
            foreach (var local in body.Locals.ToArray())
            {
                var holds = MirTypes.HoldsLoans(local.Type, c.Types, walked, out _);
                var raw = local.Type.Semantics == Kimi.Compiler.Parsing.SemanticsKind.Raw;
                Assert.True(holds || !(raw || MirTypes.IsReference(local.Type)), $"{body.Key.Decl?.Name}: {local.Name}");
                var observes = MirTypes.Observes(local.Type, c.Types, body.Key.Decl, seen, out var deep);
                Assert.False(deep, $"{body.Key.Decl?.Name}: {local.Name}");
                (locals, holding, observing) = (locals + 1, holding + (holds ? 1 : 0), observing + (observes ? 1 : 0));
            }
        }

        output.WriteLine($"library locals {locals}: holding Loans {holding}, observing {observing}");
        Assert.True(holding > 0 && observing > 0);
    }

    private static (MirBody Body, Compilation Compilation) Probe(string function)
    {
        var c = MirBuildTest.Compile(Types + function);
        Assert.True(c.Binding.Issues.Count == 0, string.Join(", ", c.Binding.Issues.Select(static x => $"{x.Code} {x.Node}")));
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        return (program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "probe"), c);
    }

    // The Arg locals after the return local, in parameter order.
    private static IEnumerable<(string Name, BoundType Type)> Parameters(MirBody body)
        => body.Locals.ToArray().Skip(1).TakeWhile(static x => x.Kind == MirLocalKind.Arg).Select(static x => (x.Name!, x.Type));

    // A walk that reports nothing and counts the Types it meets, each a First call.
    private struct Walk(HashSet<BoundType> seen) : IMirDestruction
    {
        public int Met { get; private set; }

        public bool First(BoundType type)
        {
            this.Met++;
            return seen.Add(type);
        }

        public readonly bool Visit(BoundType type, int depth) => false;

        public readonly bool Deep() => false;
    }
}
