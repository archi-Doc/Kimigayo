// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// HoldsLoans over TyCtxt.Holdings (G416 U2): the table's cells in the walk's three modes, and the bound's accounting.
public sealed partial class MirTypesTest
{
    private const string EntryForms = "struct W<E>\n    var value: E\nstruct E0<T>\nenum En<T>\n    A(T)\n    B\nstruct Two<A, B>\n    var a: A\n    var b: B\n" +
        "struct Leaf : Base\n    var m: i32\nopen struct BaseL {a}\n    let r: ref/i32 during a\n" +
        "struct RefKept<E> {source}\n    let storage: raw/E during source\n    let loan: Loan<ref/E during source>\n" +
        "struct UniqKept<E> {source}\n    let storage: raw/E during source\n    let loan: Loan<uniq/E during source>\n" +
        "struct Owned<E>\n    let storage: raw/E during self\nstruct OwnedO {a}\n    let storage: raw/i32 during self\n    let r: ref/i32 during a\n" +
        "struct Cursor<E>\n    let p: raw/E\nstruct TF\n    var t: (raw/u8, i32)\nstruct AF\n    var t: [2 of raw/u8 during static]\n" +
        "struct AL\n    var t: Array<raw/u8>\nstruct FT<T>\n    var t: (T, i32)\nenum EP\n    A((W<raw/u8>, i32))\n    C(raw/u8, i32)\n    B\n";

    private const string Exclusive = "uniq/(ref/i32 during a) during a";

    // SPEC 3.3.4, 15.6, 15.6.4 (PLAN G416 U2): each form's answer in the value's own storage, past an exclusive reference (`uniq/`), as
    // a Field-less Type's argument (`E0<>`) and past a shared reference (`ref/`): every entry past a step gives its mode's fact, and only the
    // value's own storage goes past it; a Type argument has no address, so it gives Loan facts in the value's own storage, and stays
    // Exclusive past an exclusive reference, where a call may replace the whole value. Past an exclusive reference every part that may hold
    // a Loan is the write fact, and past a shared reference only the Loan remains. Answers: "-" no Loan, else the guard.
    [Fact]
    public void TheGuardReadsEachEntryInItsMode()
    {
        (string Type, string Own, string Uniq, string Argument)[] forms =
        [
            ("i32", "-", "None", "-"), ("string", "-", "None", "-"), ("(i32, ref/i32 during a)", "None", "Through", "None"),
            ("[2 of ref/i32 during a]", "None", "Through", "None"), ("Leaf", "-", "None", "-"), ("BaseL during a", "None", "Through", "None"),
            ("obj/Leaf", "-", "None", "-"), ("obj/(BaseL during a)", "None", "Through", "None"), ("rc/Leaf", "-", "None", "-"),
            ($"rc/W<{Exclusive}>", "Through", "Through", "None"), ($"arc/W<{Exclusive}>", "Through", "Through", "None"),
            ("objref/Leaf during a", "None", "Through", "None"), ($"objuniq/W<{Exclusive}> during a", "Through", "Through", "None"),
            ("ref/i32 during a", "None", "Through", "None"), ("uniq/i32 during a", "None", "Through", "None"), (Exclusive, "Through", "Through", "None"),
            ("Slice<ref/i32 during a>", "None", "Through", "None"), ("UniqSlice<i32>", "None", "Through", "None"),
            ("UniqSlice<ref/i32 during a>", "Through", "Through", "None"), ("Array<ref/i32 during a>", "None", "Through", "None"),
            ($"Array<{Exclusive}>", "Through", "Through", "None"), ("Dictionary<i32, ref/i32 during a>", "None", "Through", "None"),
            ($"Dictionary<i32, {Exclusive}>", "Through", "Through", "None"), ("() -> ()", "Through", "Through", "None"),
            ("raw/u8", "None", "Through", "None"), ("raw/(ref/i32 during a)", "Through", "Through", "None"), ($"raw/({Exclusive})", "Through", "Through", "None"),
            ("raw/u8 during static", "None", "Through", "None"), ("raw/(ref/i32 during a) during static", "Through", "Through", "None"),
            ("E0<i32>", "-", "None", "-"), ("E0<ref/i32 during a>", "None", "Through", "None"), ($"E0<{Exclusive}>", "None", "Through", "None"),
            ("E0<raw/u8>", "None", "Through", "None"), ("W<ref/i32 during a>", "None", "Through", "None"), ($"W<{Exclusive}>", "Through", "Through", "None"),
            ("En<ref/i32 during a>", "None", "Through", "None"), ($"En<{Exclusive}>", "Through", "Through", "None"),
            ("Loan<ref/i32 during a>", "None", "Through", "None"), ($"Loan<{Exclusive}>", "None", "Through", "None"),
            ("Owned<i32>", "None", "Through", "None"), ("Owned<ref/i32 during a>", "None", "Through", "None"), ($"Owned<{Exclusive}>", "Through", "Through", "None"),
            ("OwnedO during a", "Through", "Through", "None"), ("RefKept<ref/i32 during a>", "None", "Through", "None"),
            ("UniqKept<ref/i32 during a>", "Through", "Through", "None"), ("UniqKept<i32>", "None", "Through", "None"),
            ("Cursor<i32>", "Through", "Through", "None"), ("Cursor<ref/i32 during a>", "Through", "Through", "None"), ("TF", "Through", "Through", "None"),
            ("AF", "None", "Through", "None"), ("AL", "Through", "Through", "None"), ("FT<raw/u8>", "None", "Through", "None"),
            ("W<(i32, raw/u8)>", "None", "Through", "None"), ("EP", "Through", "Through", "None"),
            ("Text.HeapBuffer", "None", "Through", "None"),
        ];
        var parameters = forms.SelectMany(static (x, i) => new[] { $"own{i}: {x.Type}", $"uni{i}: uniq/({x.Type}) during a", $"arg{i}: E0<{x.Type}>", $"sha{i}: ref/({x.Type}) during a" });
        var (body, c) = Probe(EntryForms + $"func probe({string.Join(", ", parameters)})\n    return\n");
        var (seen, members, entries) = (new Dictionary<MirHoldingKey, MirModeDepths>(), new List<RawMember>(), new List<Hold>());
        var expected = forms.SelectMany(static x => new[] { (x.Type, x.Own), (x.Type, x.Uniq), (x.Type, x.Argument), (x.Type, "None") });
        Assert.Equal(expected, Parameters(body).Select((x, i) => (forms[i / 4].Type, Guard(x.Type))));
        Assert.Empty(members);
        Assert.Empty(entries);

        string Guard(BoundType type) => MirTypes.HoldsLoans(type, c.Types, seen, members, entries, out var writes) ? writes.ToString() : "-";
    }

    // Q2's condition 4, fact.md §1: a form with no row is one Unsupported entry, the walk's undecided outcome wherever a mode reads the write
    // fact, never the write fact and never nothing. No valid form reaches it, so the row asks with a nominal Type of a function's symbol, no
    // struct, enum or Contract: in the value's own storage, past an exclusive reference, and as a Type argument, which reads Loans only.
    [Fact]
    public void AnUnsupportedEntryIsTheGuardsLimit()
    {
        var (body, c) = Probe(EntryForms + "func probe(e: E0<i32>)\n    return\n");
        var unlisted = c.Types.Intern(BoundTypeKind.Nominal, body.Key.Decl, SemanticsKind.Owner, []);
        var phantom = Parameters(body).Single().Type;
        var (seen, members, entries) = (new Dictionary<MirHoldingKey, MirModeDepths>(), new List<RawMember>(), new List<Hold>());
        Assert.Equal(
            ["Limit", "Limit", "None"],
            new[]
            {
                unlisted,
                c.Types.Intern(BoundTypeKind.Semantics, null, SemanticsKind.Uniq, [unlisted]),
                c.Types.Intern(BoundTypeKind.Constructed, phantom.Symbol, SemanticsKind.Owner, [unlisted]),
            }.Select(x => MirTypes.HoldsLoans(x, c.Types, seen, members, entries, out var writes) ? writes.ToString() : "-"));
    }

    // IMPL 21.3.2 (the limit is implementation-defined; fact.md §1, row 9; PLAN G416 U2): a level is one entry. A Case's payload component
    // is one level below its enum, so the exclusive reference at depth n is the write fact through 31 and past the bound from 32, where its
    // referent lies, and an i32 is exact through 32. A Dictionary's K and V are two levels below it, through its `(K, V)` entry (the owner's
    // O-47, O-49, PLAN G408: the named limit is a storage nest past 32 entries, a Dictionary level counting two; the records are in
    // MirBorrowsTest's ADictionaryNestPastTheBoundIsALocatedLimit), so the
    // reference is past the bound from 16, and a nest of integers, whose innermost entry's key is never entered, from 17.
    [Theory]
    [InlineData(16, "Through", "-", "Deep", "-")]
    [InlineData(17, "Through", "-", "Deep", "Deep")]
    [InlineData(31, "Through", "-", "Deep", "Deep")]
    [InlineData(32, "Deep", "-", "Deep", "Deep")]
    [InlineData(33, "Deep", "Deep", "Deep", "Deep")]
    public void TheBoundCountsEntries(int depth, string enumBorrow, string enumInteger, string dictionaryBorrow, string dictionaryInteger)
    {
        var (body, c) = Probe(EntryForms +
            $"func probe(x: {Exclusive}, eb: {Nested("En<", ">", Exclusive, depth)}, ei: {Nested("En<", ">", "i32", depth)}, " +
            $"db: {Nested("Dictionary<i32, ", ">", Exclusive, depth)}, di: {Nested("Dictionary<i32, ", ">", "i32", depth)})\n    return\n");
        var (seen, entries) = (new Dictionary<MirHoldingKey, MirModeDepths>(), new List<Hold>());
        Assert.Equal(
            [("x", "Through"), ("eb", enumBorrow), ("ei", enumInteger), ("db", dictionaryBorrow), ("di", dictionaryInteger)],
            Parameters(body).Select(x => (x.Name, MirTypes.HoldsLoans(x.Type, c.Types, seen, [], entries, out var writes) ? writes.ToString() : "-")));

        static string Nested(string open, string close, string inner, int levels)
            => string.Concat(Enumerable.Repeat(open, levels)) + inner + string.Concat(Enumerable.Repeat(close, levels));
    }
}
