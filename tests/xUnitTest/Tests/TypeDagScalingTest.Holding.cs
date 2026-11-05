// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>PLAN G307, DECISIONS R4-M157 and BU-58 (V16): the holding walk (<see cref="MirTypes.HoldsLoans"/>) keys a stored part's inline
/// Tuples and fixed arrays by what their facts read (<see cref="MirHoldingKey"/>): the Type the declaration writes there
/// (<see cref="TyCtxt.WrittenAt"/>) with the holding instance, or where it writes nothing their Type in a stored part. So a Type shared in a
/// DAG is walked once per query, inside the written structure and below it. Each row builds its Types with TyCtxt over one checked
/// program, so a Tuple of 2^k positions costs k Types: a walk that visits positions meets 2^30 of them (a hang the runner's timeout
/// reports), the keyed walk tens. Every row is in Own mode, where the stored part is walked; its answers are pinned at a size the
/// per-position walk also reaches.</summary>
public partial class TypeDagScalingTest
{
    // The forms: a generic holder of a Type argument, a holder of a raw-free written Tuple, one writing a raw layer beside a Type argument,
    // BU's written twin with Loan-holding pointees, R4's reviewer's twins (Q3: two written Tuples kept as one; SP: one written part kept
    // alike by two instances, its `during self` layer reading each instance's Type argument) and enum E3 (one interned payload for Cases
    // written differently), and holders of many parts that store one Type argument.
    private const string HoldingForms = """
        struct D<T>
            public var x: T
        struct Wrap<E>
            public var value: E
        struct S
            public var t: (i32, i32)
        struct P<T>
            public var x: ((raw/i32 during static, i32), T)
        struct Q2<T> {s}
            public var r: ref/i32 during s
            public var x: ((raw/(ref/i32 during s) during s, raw/(ref/i32 during s) during s), T)
        struct Q3 {s}
            public var r: ref/i32 during s
            public var x: ((raw/(ref/i32 during s) during s, i32), (raw/(ref/i32 during static) during static, i32))
        struct SP<T>
            internal var x: (raw/i32 during self, i32)
            public init()
                self.x = (null, 0)
        enum E3 {s}
            A(raw/(ref/i32 during s) during s, i32)
            B(raw/(ref/i32 during static) during static, i32)
            C(ref/i32 during s)

        """;

    // R1: a raw leaf below the written region (D's `x: T` writes nothing) is read at each meeting of its keyed parent, in reach of the
    // bound (the leaves of T_30 lie at depth 31): Origin-less and `static` leaves hold a Loan with no write fact; an Exclusive leaf, whose
    // members are unpublished below the region, is the Limit. T_12's answers are the per-position walk's.
    [Theory]
    [InlineData("a", 12, "True None")]
    [InlineData("a", 30, "True None")]
    [InlineData("b", 12, "True None")]
    [InlineData("b", 30, "True None")]
    [InlineData("e", 12, "True Limit")]
    [InlineData("e", 30, "True Limit")]
    public void ARawLeafBelowTheWrittenRegionIsReadOncePerType(string leaf, int size, string answer)
    {
        var (c, p) = HoldingProbe();
        Assert.Equal(answer, Holding(c, With(c, p["d"], Doubled(c, p[leaf], size)), out _));
    }

    // R1's key set: below the region each inline Tuple of D's part is one key in a stored part, whatever the positions: T_40 through T_8,
    // T_8 met past the bound only (in Own mode T_40 lies at depth 1); through a uniq reference one level deeper, T_40 through T_9.
    [Theory]
    [InlineData(false, 8)]
    [InlineData(true, 9)]
    public void AStoredPartsInlineTypesAreItsKeys(bool reference, int past)
    {
        var (c, p) = HoldingProbe();
        var doubled = new BoundType[41];
        doubled[0] = BoundType.I32;
        for (var k = 1; k <= 40; k++)
        {
            doubled[k] = Tuple(c, doubled[k - 1], doubled[k - 1]);
        }

        var d = With(c, p["d"], doubled[40]);
        var seen = new Dictionary<MirHoldingKey, MirModeDepths>();
        _ = MirTypes.HoldsLoans(reference ? Uniq(c, d) : d, c.Types, seen, [], out _);
        var stored = seen.Where(static x => x.Key.Stored).ToArray();
        Assert.Equal(
            [.. Enumerable.Range(past, 41 - past).Select(k => new MirHoldingKey(doubled[k], true, null, null))],
            stored.Select(static x => x.Key).OrderBy(x => Array.IndexOf(doubled, x.Type)).ToArray());
        Assert.Equal([doubled[past]], stored.Where(static x => IsPast(x.Value)).Select(static x => x.Key.Type));
    }

    // R2: the Stored bit decides. `Te` outside a stored part reads its Exclusive layer as unstated (no write fact); as Wrap's part (`value:
    // E` writes nothing) the layer steps Exclusive with unpublished members, the Limit, in either order of the two parts.
    [Fact]
    public void AnInlinePartOfAStoredPartIsKeyedApartFromItsType()
    {
        var (c, p) = HoldingProbe();
        var te = Tuple(c, p["e"], BoundType.I32);
        var wrapped = With(c, p["w"], te);
        Assert.Equal("True Limit", Holding(c, Tuple(c, te, wrapped), out _));
        Assert.Equal("True Limit", Holding(c, Tuple(c, wrapped, te), out _));
    }

    // R3: the written Type decides. Q2's part keeps one interned Tuple at [0], which the declaration writes (its layers capped Shared by
    // the ref-required slot, nothing past them), and at [1], which T supplies (its layers step Static, past which the ref pointee gives
    // the write fact).
    [Fact]
    public void OneKeptTupleWrittenAndSuppliedIsKeyedByItsWrittenType()
    {
        var (c, p) = HoldingProbe();
        var shared = c.Types.StoredParts(p["q2"], out _)[1]!.Components[0];
        var twin = With(c, p["q2"], shared);
        var kept = c.Types.StoredParts(twin, out _)[1]!;
        Assert.Same(kept.Components[0], kept.Components[1]);
        Assert.Equal("True Through", Holding(c, twin, out _));
    }

    // R3, both written: Q3's part keeps one interned Tuple at [0] and [1], written differently (a ref-required slot, capped Shared; and
    // `static`, past which the ref pointee gives the write fact), so each written Type is its own key.
    [Fact]
    public void OneKeptTupleWrittenTwiceIsKeyedByEachWrittenType()
    {
        var (c, p) = HoldingProbe();
        var kept = c.Types.StoredParts(p["q3"], out _)[1]!;
        Assert.Same(kept.Components[0], kept.Components[1]);
        Assert.NotSame(TyCtxt.WrittenAt(p["q3"], 1, [0]), TyCtxt.WrittenAt(p["q3"], 1, [1]));
        Assert.Equal("True Through", Holding(c, p["q3"], out _));
    }

    // The holder decides inside the written region (implementation review 1's N1): SP<i32> and SP<B> keep one interned part with one
    // written Type, whose `during self` layer steps Own and admits the holding instance's Type argument, B an exclusive reference to a
    // borrow, so the pair's guard is the write fact in either order of its parts.
    [Fact]
    public void AnInlinePartInsideTheWrittenRegionIsKeyedByItsHolder()
    {
        var (c, p) = HoldingProbe();
        var (plain, lent) = (p["sp"], With(c, p["sp"], p["u"]));
        Assert.Same(c.Types.StoredParts(plain, out _)[0], c.Types.StoredParts(lent, out _)[0]);
        Assert.Equal("True Through", Holding(c, Tuple(c, plain, lent), out _));
        Assert.Equal("True Through", Holding(c, Tuple(c, lent, plain), out _));
    }

    // R3b: E3's Cases A and B keep one interned payload at `E3 during static`, A's layer capped Shared, B's stepping Static past a ref
    // pointee: the payload components are the Case's first positions, keyed by their own written Types, so B's write fact is read.
    [Fact]
    public void AnEnumCasesPayloadComponentsAreItsFirstPositions()
    {
        var (c, p) = HoldingProbe();
        var payloads = c.Types.StoredParts(p["e3"], out _);
        Assert.Same(payloads[0], payloads[1]);
        Assert.Equal("True Through", Holding(c, p["e3"], out _));
    }

    // R5 (DECISIONS R4-M157, R4-M153's per-Type rule on the key): an inline part of a stored part past the bound gives the bound's facts
    // only where its key is never entered within it, so the answer is the same in every order of the parts. W = ((i32, i32), (i32, i32))
    // is D's part at depth 2 and again at depth 32 below 30 levels of N (each (N, i32)); S's written (i32, i32) and P's written region are
    // a stored part's root at depth 33 below N30 and at depth 3 beside it; i32 beside N30 enters no shallower key.
    [Theory]
    [InlineData("W,N30", false, "False None")]
    [InlineData("W,N30", true, "True None")]
    [InlineData("i32,N30", false, "True Deep")]
    [InlineData("N30(S),S", false, "False None")]
    [InlineData("S,N30(S)", false, "False None")]
    [InlineData("N30(P),i32", false, "True Deep")]
    [InlineData("N30(P),P", false, "True None")]
    [InlineData("P,N30(P)", false, "True None")]
    public void APartPastTheBoundGivesTheBoundsFactsOnlyWhereItsKeyIsNeverEntered(string shape, bool reference, string answer)
    {
        var (c, p) = HoldingProbe();
        var w = Doubled(c, BoundType.I32, 2);
        var part = shape switch
        {
            "W,N30" => Tuple(c, w, Nested(c, w, 30)),
            "i32,N30" => Tuple(c, BoundType.I32, Nested(c, w, 30)),
            "N30(S),S" => Tuple(c, Nested(c, p["s"], 30), p["s"]),
            "S,N30(S)" => Tuple(c, p["s"], Nested(c, p["s"], 30)),
            "N30(P),i32" => Tuple(c, Nested(c, p["p"], 30), BoundType.I32),
            "N30(P),P" => Tuple(c, Nested(c, p["p"], 30), p["p"]),
            "P,N30(P)" => Tuple(c, p["p"], Nested(c, p["p"], 30)),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
        };
        var d = With(c, p["d"], part);
        Assert.Equal(answer, Holding(c, reference ? Uniq(c, d) : d, out _));
    }

    // R6 (V16): a Type that several instances store below their written region is one key, walked once: All_m holds m structs whose
    // `x: T` stores T_30, and the walk has T_30's 30 inline Tuples as keys at every m.
    [Theory]
    [InlineData(4)]
    [InlineData(16)]
    [InlineData(64)]
    public void ATypeStoredByManyInstancesIsWalkedOnce(int count)
    {
        var (c, p) = HoldingProbe();
        var all = With(c, p[$"all{count}"], Doubled(c, BoundType.I32, 30));
        Assert.Equal("False None", Holding(c, all, out var seen));
        Assert.Equal(30, seen.Keys.Count(static x => x.Stored));
    }

    // R4 (BU's B1 form): a closed associated projection's resolution writes H's part as a Tuple DAG of 2^n positions from O(n) text, so
    // the written Type is shared too: each level is one key with its written Type in H, in Own mode (a uniq reference to H decides at its
    // `loan` Field first). The raw leaves reach Binding's raw-Origin walks of that chain, linear since PLAN G309.
    [Theory]
    [InlineData("i32", 12, "True None", 12)]
    [InlineData("i32", 40, "True Deep", 33)]
    [InlineData("raw/i32 during static", 12, "True None", 12)]
    [InlineData("raw/i32 during static", 30, "True None", 30)]
    [InlineData("raw/i32 during s", 12, "True None", 12)]
    [InlineData("raw/i32 during s", 30, "True None", 30)]
    public void AWrittenTypeDagIsWalkedOncePerWrittenType(string leaf, int size, string answer, int keys)
    {
        var type = leaf;
        for (var i = 0; i < size; i++)
        {
            type = $"Dup<{type}>.Item";
        }

        var c = MirBuildTest.Compile("contract HasItem\n    associate Item\nstruct Dup<T>\n    Self is HasItem\n    associate HasItem.Item is (T, T)\n" +
            $"struct H {{s}}\n    internal let loan: Loan<ref/i32 during s>\n    internal var x: {type}\nfunc probe(q: ref/i32, h: ref/H during q) => ()\n" +
            "public func main() => ()\n");
        Assert.True(c.Binding.Issues.Count == 0, string.Join(", ", c.Binding.Issues.Select(static x => $"{x.Code} {x.Node}")));
        var h = c.BuildMir().Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "probe").Locals[2].Type.Components[0];
        Assert.Equal(answer, Holding(c, h, out var seen));
        Assert.Equal(keys, seen.Keys.Count(static x => x.Stored && x.Written is not null));
    }

    // The checked program of the forms and of holders of 4, 16 and 64 parts, and its probe's parameter Types by name.
    private static (Compilation Compilation, Dictionary<string, BoundType> Parameters) HoldingProbe()
    {
        var text = new StringBuilder(HoldingForms);
        for (var i = 0; i < 64; i++)
        {
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"struct S{i}<T>\n    public var x: T\n");
        }

        foreach (var count in new[] { 4, 16, 64 })
        {
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"struct All{count}<T>\n");
            for (var i = 0; i < count; i++)
            {
                text.Append(System.Globalization.CultureInfo.InvariantCulture, $"    public var a{i}: S{i}<T>\n");
            }
        }

        text.Append("func probe(a: raw/i32, b: raw/i32 during static, x: uniq/i32, e: raw/i32 during x, d: D<i32>, w: Wrap<i32>, s: S, p: P<i32>, ")
            .Append("q2: Q2<i32> during static, q3: Q3 during static, sp: SP<i32>, u: uniq/(ref/i32 during k) during k, e3: E3 during static, ")
            .Append("all4: All4<i32>, all16: All16<i32>, all64: All64<i32>)\n    return\n")
            .Append("public func main() => ()\n");
        var c = MirBuildTest.Compile(text.ToString());
        Assert.True(c.Binding.Issues.Count == 0, string.Join(", ", c.Binding.Issues.Select(static x => $"{x.Code} {x.Node}")));
        var body = c.BuildMir().Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "probe");
        return (c, body.Locals.ToArray().Skip(1).TakeWhile(static x => x.Kind == MirLocalKind.Arg).ToDictionary(static x => x.Name!, static x => x.Type));
    }

    // The holding walk's answer in Own mode, whether the value may hold a Loan and the guard, with its keys.
    private static string Holding(Compilation c, BoundType type, out Dictionary<MirHoldingKey, MirModeDepths> seen)
    {
        seen = new();
        var holds = MirTypes.HoldsLoans(type, c.Types, seen, [], out var writes);
        return $"{holds} {writes}"; // HoldsLoans gives None where the value holds no Loan.
    }

    private static bool IsPast(MirModeDepths modes) => modes[0] == MirDepths.Past || modes[1] == MirDepths.Past || modes[2] == MirDepths.Past;

    private static BoundType Tuple(Compilation c, params BoundType[] parts) => c.Types.Intern(BoundTypeKind.Tuple, null, SemanticsKind.Owner, parts);

    private static BoundType Uniq(Compilation c, BoundType type) => c.Types.Intern(BoundTypeKind.Semantics, null, SemanticsKind.Uniq, [type]);

    // A generic Type of the probe with another Type argument, its Origin arguments kept.
    private static BoundType With(Compilation c, BoundType generic, BoundType argument)
        => c.Types.Intern(BoundTypeKind.Constructed, generic.Symbol, SemanticsKind.Owner, [argument], originArguments: [.. generic.OriginArguments]);

    // T_k: T_0 the leaf, T_{k+1} = (T_k, T_k), a Tuple of 2^k positions in k + 1 Types.
    private static BoundType Doubled(Compilation c, BoundType leaf, int size)
    {
        for (var k = 0; k < size; k++)
        {
            leaf = Tuple(c, leaf, leaf);
        }

        return leaf;
    }

    // N_j: N_0 the part, N_{j+1} = (N_j, i32), the part j levels down.
    private static BoundType Nested(Compilation c, BoundType part, int levels)
    {
        for (var j = 0; j < levels; j++)
        {
            part = Tuple(c, part, BoundType.I32);
        }

        return part;
    }
}
