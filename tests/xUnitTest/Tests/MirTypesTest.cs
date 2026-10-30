// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using System.Text;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Xunit;

namespace XunitTest;

/// <summary>The Type facts A5 reads (docs/dev/DECISIONS.md R4-M36, R4-M73, R4-M147): which values may hold a Loan and what a call may write past them
/// (<see cref="MirTypes.HoldsLoans"/>), what destructions may observe of what they hold (<see cref="MirTypes.Observation"/>), and the
/// callables a value hands a callee (TyCtxt.HandedCallables, R4-M154), over the parameter Types of one function.</summary>
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
        var seen = new Dictionary<MirHoldingKey, MirModeDepths>();
        Assert.Equal(
            [("scalar", false), ("text", false), ("numbers", false), ("plain", false), ("raw", true), ("chain", false), ("tree", false),
                ("forest", true), ("rows", false),
                ("borrow", true), ("borrows", true), ("tuple", true), ("fixed", true), ("table", true), ("option", true),
                ("holder", true), ("handle", true), ("slice", true), ("view", true), ("loan", true), ("iterator", true), ("action", true),
                ("layer", true), ("nest", true)],
            Parameters(body).Select(x => (x.Name, MirTypes.HoldsLoans(x.Type, c.Types, seen, [], out _))).ToArray());
    }

    // A3 E16 (SPEC 15.6, 15.6.2, 15.6.4, 15.3.5; DECISIONS R4-M73, R4-M147): a call may write past a value that may hold, past no shared
    // layer, an exclusive reference to storage that may hold a Loan: an exclusive reference, in an Array, a payload or a Field, a
    // UniqSlice's elements, a Function value, a raw parameter's pointee, an abstract part (the owner's Q1 (a)) and a pair layer's exclusive
    // case (the guard); not past a shared reference, through an exclusive reference or a raw pointer to Loan-free storage (a scalar,
    // Utf8Writer's Loan<uniq/u8> and raw Fields), or through a struct's or enum's Type argument, which has no address (an iterator's, a
    // Loan's). A stored raw layer is read by its step on its instance: a slot kept only by `ref` writes nothing past it, another slot its
    // pointee and its exclusive members' referents (uniqKept), `during self` the holding instance's Type arguments by their actual Types
    // (owned, ownedUniq), and an Origin-less layer its declaration states the write fact (cursor).
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
        var seen = new Dictionary<MirHoldingKey, MirModeDepths>();
        Assert.Equal(
            [("borrow", MirWrites.None), ("scalar", MirWrites.None), ("writer", MirWrites.None), ("shared", MirWrites.None),
                ("unique", MirWrites.Through), ("array", MirWrites.Through), ("option", MirWrites.Through), ("slot", MirWrites.Through),
                ("view", MirWrites.Through), ("scalars", MirWrites.None), ("action", MirWrites.Through), ("toRaw", MirWrites.Through),
                ("value", MirWrites.Through), ("layer", MirWrites.Through), ("iterator", MirWrites.None),
                ("loan", MirWrites.None), ("raw", MirWrites.Through), ("rawScalar", MirWrites.None), ("number", MirWrites.None),
                ("refKept", MirWrites.None), ("uniqKept", MirWrites.Through), ("owned", MirWrites.None),
                ("ownedUniq", MirWrites.Through), ("cursor", MirWrites.Through)],
            Parameters(body).Select(x => (x.Name, Writes(x.Type))).ToArray());

        MirWrites Writes(BoundType type)
        {
            _ = MirTypes.HoldsLoans(type, c.Types, seen, [], out var writes);
            return writes;
        }
    }

    // SPEC 23.1.1, IMPL 21.3.2 (DECISIONS R4-M153): a part past the walk's bound where the write fact would be read is the guard's own
    // outcome, Deep, never the write fact: past an exclusive reference (unique40) and in the value's own storage (value40), while a decided
    // write elsewhere in the value outranks it (mixed40); past a shared reference nothing is read (shared40), and at depth 10 the walk is
    // exact (unique10: a Tuple of integers past an exclusive reference gives nothing). Beside a stored raw layer whose members Binding
    // does not publish (Limit, as w), the one answer (MirOutcome.Join) is Limit (limitDeep40), as at depth 10 (limitDeep10).
    [Fact]
    public void APartPastTheBoundIsTheGuardsDeepOutcome()
    {
        var (body, c) = Probe(
            $"struct Wrap<E>\n    var value: E\nfunc probe(x: uniq/(ref/i32 during a) during a, unique40: uniq/{Nested(40)}, value40: {Nested(40)}, " +
            $"mixed40: (uniq/(ref/i32 during a) during a, uniq/{Nested(40)} during a), shared40: ref/{Nested(40)}, unique10: uniq/{Nested(10)}, " +
            $"w: Wrap<raw/u8 during x>, limitDeep40: (Wrap<raw/u8 during x>, uniq/{Nested(40)} during a), " +
            $"limitDeep10: (Wrap<raw/u8 during x>, uniq/{Nested(10)} during a))\n    return\n");
        var seen = new Dictionary<MirHoldingKey, MirModeDepths>();
        Assert.Equal(
            [("x", MirWrites.Through), ("unique40", MirWrites.Deep), ("value40", MirWrites.Deep), ("mixed40", MirWrites.Through),
                ("shared40", MirWrites.None), ("unique10", MirWrites.None), ("w", MirWrites.Limit), ("limitDeep40", MirWrites.Limit),
                ("limitDeep10", MirWrites.Limit)],
            Parameters(body).Select(x => (x.Name, Writes(x.Type))).ToArray());

        MirWrites Writes(BoundType type)
        {
            _ = MirTypes.HoldsLoans(type, c.Types, seen, [], out var writes);
            return writes;
        }

        static string Nested(int levels) => new string('(', levels) + "i32" + string.Concat(Enumerable.Repeat(", i32)", levels));
    }

    // SPEC 23.1.1 (DECISIONS R4-M153's revision 3): the holding walk enters a (Type, mode) at the shallowest depth it meets it, so one
    // Type at two depths answers alike in either order: an exclusive reference to a borrow 41 levels down a Tuple and 9 levels down beside
    // it (the deep one's tail being the shallow one's Type) is the guard's write fact, never Deep, the deep component first or last.
    [Fact]
    public void AGuardsTypeMetPastAndWithinTheBoundAnswersAlikeInEitherOrder()
    {
        const string Unique = "uniq/(ref/i32 during a) during a";
        var deep = Nested(Unique, 40);
        var shallow = Nested(Unique, 8);
        var (body, c) = Probe($"func probe(x: {Unique}, deepFirst: ({deep}, {shallow}), shallowFirst: ({shallow}, {deep}))\n    return\n");
        var walked = new Dictionary<MirHoldingKey, MirModeDepths>();
        Assert.Equal(
            [("x", MirWrites.Through), ("deepFirst", MirWrites.Through), ("shallowFirst", MirWrites.Through)],
            Parameters(body).Select(x => (x.Name, MirTypes.HoldsLoans(x.Type, c.Types, walked, [], out var writes) ? writes : MirWrites.None)).ToArray());

        static string Nested(string inner, int levels) => new string('(', levels) + inner + string.Concat(Enumerable.Repeat(", i32)", levels));
    }

    // SPEC 23.1.1, 23.1.2 (DECISIONS R4-M153's revision 3): a walk's answer is the Join of its parts' outcomes, the same in every order of
    // a Tuple's components, an Array element's components and a struct's Fields. Beside a part past the bound (deep, a Tuple of i32
    // nested 40 deep: Deep alone) a decided part is the answer before and after it (Observation: a Logger, whose drop observes; the guard:
    // an exclusive reference to a borrow), and a Limit part outranks it (an abstract part not proven Copy; a stored raw layer whose members
    // Binding does not publish). At depth 10 nothing is past the bound (deep: none) and each answer is the same.
    [Theory]
    [InlineData(40)]
    [InlineData(10)]
    public void AWalksAnswerIsTheSameInEveryOrderOfItsParts(int depth)
    {
        var deep = new string('(', depth) + "i32" + string.Concat(Enumerable.Repeat(", i32)", depth));
        var past = depth > 32 ? "Deep" : "None";
        var observed = Rows("o", "Logger", "T", "Decided");
        var guarded = Rows("g", "uniq/(ref/i32 during a) during a", "Wrap<raw/u8 during x>", "Through");
        var (body, c) = Probe(
            "struct Wrap<E>\n    var value: E\nstruct Two<A, B>\n    var a: A\n    var b: B\n" +
            $"func probe<T>(x: uniq/(ref/i32 during a) during a, {string.Join(", ", observed.Concat(guarded).Select(static x => $"{x.Name}: {x.Type}"))})\n" +
            "    return\n");
        var entered = new Dictionary<BoundType, byte>(ReferenceEqualityComparer.Instance);
        var walked = new Dictionary<MirHoldingKey, MirModeDepths>();
        Assert.Equal(
            observed.Concat(guarded).Select(static x => (x.Name, x.Answer)).ToArray(),
            Parameters(body).Skip(1).Select(x => (x.Name, x.Name[0] == 'o' ? Observed(x.Type) : Guard(x.Type))).ToArray());

        string Observed(BoundType type) => MirTypes.Observation(type, c.Types, body.Key.Decl, entered).ToString();

        string Guard(BoundType type)
        {
            _ = MirTypes.HoldsLoans(type, c.Types, walked, [], out var writes);
            return writes.ToString();
        }

        // Each container of each order of a decided part (D) or a Limit part (L) and the deep part (P), and the deep part alone.
        (string Name, string Type, string Answer)[] Rows(string walk, string decided, string limit, string decidedAnswer)
        {
            (string Name, string Format)[] containers = [("Tuple", "({0}, {1})"), ("Array", "Array<({0}, {1})>"), ("Field", "Two<{0}, {1}>")];
            (string Name, string First, string Second, string Answer)[] orders =
                [("DP", decided, deep, decidedAnswer), ("PD", deep, decided, decidedAnswer), ("LP", limit, deep, "Limit"), ("PL", deep, limit, "Limit")];
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            return
            [
                .. containers.SelectMany(x => orders.Select(o => ($"{walk}{x.Name}{o.Name}", string.Format(culture, x.Format, o.First, o.Second), o.Answer))),
                ($"{walk}Deep", deep, past),
            ];
        }
    }

    // SPEC 15.8.2, IMPL 21.1.2 (DECISIONS R4-M148): a Closure value holds its environment's parts, a Tuple of its bindings: a captured
    // exclusive reference to storage that may hold a Loan (exclusive), an abstract binding (abstract) and a Function value (function) let a
    // call write past it; a shared reference, a `var` one included (shared), and an exclusive reference to a scalar (scalar) do not; a stored
    // raw layer stepping Exclusive with unpublished members leaves it undecided (limit); scalars only hold no Loan (none).
    [Fact]
    public void AClosureHoldsItsEnvironmentsParts()
    {
        var c = MirBuildTest.Compile(
            "struct Wrap2c3<E>\n    var value: E\n    public init(value: E) => self.value = value\n" +
            "func probe<T>(x: ref/i32, w: uniq/i32, value: T, g: () -> ())\n    T is Copy\n" +
            "    var holder: ref/i32 during x = x\n    let exclusive = func [holder@uniq] () => ()\n" +
            "    var state: ref/i32 during x = x\n    let shared = func [var state] () => ()\n" +
            "    let scalar = func [w] () => ()\n" +
            "    let n: i32 = 1\n    let none = func [n] () -> i32 => n\n" +
            "    let abstract = func [var value] () -> T => value\n" +
            "    let function = func [g@move] () => g()\n" +
            "    var y = 1\n    var r: ref/i32 = y@ref\n    var u = r@uniq\n" +
            "    var h = Wrap2c3.init(u@follow@raw@raw/u8)\n    let limit = func [h@move] () => ()\n" +
            "public func main() => ()\n");
        Assert.Empty(c.Binding.Issues);
        var seen = new Dictionary<MirHoldingKey, MirModeDepths>();
        Assert.Equal(
            ["Through", "None", "None", "no Loan", "Through", "Through", "Limit"],
            KotoTree.Walk(c.Kotonoha.RootKoto).OfType<Kimi.Compiler.Parsing.FunctionKoto>().Where(static x => x.IsAnonymous)
                .Select(x => MirTypes.HoldsLoans(x.ClosureDefinition!.EnvironmentType, c.Types, seen, [], out var writes) ? $"{writes}" : "no Loan").ToArray());
    }

    // SPEC 5.7 Calls, 15.6.4 (DECISIONS R4-M147, BU-41): a raw layer a holder stores at any depth below it is read on the instance holding it,
    // as the holder's substitution makes that instance: a Field (w), a nested instance's Field (o), an inline Tuple (t), a Case payload (e,
    // ow), an Array element in a Field (h), an Own layer's pointee (op) and its Type argument in a nested Field (bb), and a base (d, bw). A
    // layer a Type argument supplies whose Origin steps Exclusive publishes no members, so a Loan-free pointee leaves the guard undecided
    // (Limit; also past a shared parameter borrow, hs), while a pointee that may hold a Loan decides it (wr, orr, dr), as does an exclusive
    // part beside it (mixed, Through over Limit); the same layer with no Origin, `static` or a plain scalar pointee writes nothing past it
    // (plain, plainA, plainE, st, dp). A layer written in a Type argument of the declaration's own base clause is the declaration's, which
    // its kept part states (ds: Through; DECISIONS BU-46, R4-M151).
    [Fact]
    public void TheGuardReadsEachStoredLayerOnItsInstance()
    {
        var (body, c) = Probe(
            "struct Wrap<E>\n    var value: E\nstruct Outer<P>\n    var w: Wrap<P>\nstruct Pair2<P>\n    var p: (P, i32)\n" +
            "enum Opt2<P>\n    Has(Wrap<P>)\n    Not\nstruct Hold<P>\n    var items: Array<Wrap<P>>\nstruct OP<P>\n    var data: raw/Wrap<P> during self\n" +
            "struct ErasedBag<T>\n    var data: raw/u8 during self\nstruct Box1<P>\n    var b: ErasedBag<Wrap<P>>\n" +
            "open struct BaseW<P>\n    var w: Wrap<P>\nstruct Der<P> : BaseW<P>\n    var n: i32\n" +
            "struct DerS : BaseW<raw/u8>\n    var n: i32\n" +
            "struct Mixed<E> {a}\n    var value: E\n    var u: uniq/(ref/i32 during a) during a\n" +
            "func probe(x: uniq/(ref/i32 during a) during a, q: ref/(ref/i32 during a) during a, w: Wrap<raw/u8 during x>, " +
            "wr: Wrap<raw/(ref/i32 during a) during x>, o: Outer<raw/u8 during x>, orr: Outer<raw/(ref/i32 during a) during x>, " +
            "t: Pair2<raw/u8 during x>, e: Opt2<raw/u8 during x>, h: Hold<raw/u8 during x>, op: OP<raw/u8 during x>, " +
            "bb: Box1<raw/u8 during x>, ow: Option<Wrap<raw/u8 during x>>, d: Der<raw/u8 during x>, bw: BaseW<raw/u8 during x>, " +
            "dr: Der<raw/(ref/i32 during a) during x>, hs: Wrap<raw/u8 during q>, mixed: Mixed<raw/u8 during x> during a, " +
            "plain: Outer<raw/u8>, plainA: Hold<raw/u8>, plainE: Opt2<raw/u8>, st: Outer<raw/u8 during static>, dp: Der<raw/u8>, " +
            "ds: DerS)\n" +
            "    return\n");
        var seen = new Dictionary<MirHoldingKey, MirModeDepths>();
        var members = new List<RawMember>();
        Assert.Equal(
            [("x", MirWrites.Through), ("q", MirWrites.None), ("w", MirWrites.Limit), ("wr", MirWrites.Through), ("o", MirWrites.Limit),
                ("orr", MirWrites.Through), ("t", MirWrites.Limit), ("e", MirWrites.Limit), ("h", MirWrites.Limit), ("op", MirWrites.Limit),
                ("bb", MirWrites.Limit), ("ow", MirWrites.Limit), ("d", MirWrites.Limit), ("bw", MirWrites.Limit), ("dr", MirWrites.Through),
                ("hs", MirWrites.Limit), ("mixed", MirWrites.Through), ("plain", MirWrites.None), ("plainA", MirWrites.None),
                ("plainE", MirWrites.None), ("st", MirWrites.None), ("dp", MirWrites.None), ("ds", MirWrites.Through)],
            Parameters(body).Select(x => (x.Name, Writes(x.Type))).ToArray());
        Assert.Empty(members);

        MirWrites Writes(BoundType type)
        {
            _ = MirTypes.HoldsLoans(type, c.Types, seen, members, out var writes);
            return writes;
        }
    }

    // SPEC 5.7 Calls (DECISIONS BU-46, R4-M151): past a raw layer without an Origin that a declaration writes, at any depth below the
    // holder, a call may write (Through), as past the holder's own (direct): in a Type argument of a Field (holder2, holdOpt, holdEnum,
    // holdTW, hfa, how), a base clause (derS, d2, d3, b3), beside a supplied argument (m), a nested holder (h3, oh), a pair slot's argument
    // (hpso, hpss, hpst) and its application `s/U` (hpu, hpus), a Case payload's Tuple (ep), and outside a stored part, read by the same
    // rule: an Array element of a Field (hold2) and a `during self` pointee's content (sb, hb2); the kept Type tells the holders apart, so
    // the walk's per-Type memo gives one answer in either order (g, g2, top); where the parameter's Type expression writes the layer over a
    // Loan-free pointee, nothing (the twins, box2 the `during self` one).
    [Fact]
    public void TheGuardWritesPastALayerADeclarationWrites()
    {
        var (body, c) = Probe(
            "struct Wrap<E>\n    var value: E\nstruct Outer<P>\n    var w: Wrap<P>\nstruct RawBuf\n    var data: raw/u8\n" +
            "struct Holder2\n    var b: Wrap<raw/u8>\nstruct HoldOpt\n    var o: Option<raw/u8>\nopen struct B1<P>\n    var v: P\n" +
            "open struct B2<Q> : B1<Q>\n    var m: i32\nopen struct B3<Q> : B1<Wrap<raw/u8>>\n    var q: Q\nstruct DerS : B1<raw/u8>\n    var n: i32\n" +
            "struct D2 : B2<raw/u8>\n    var n: i32\nstruct D3 : B3<i32>\n    var n: i32\nstruct Two<A, B>\n    var a: A\n    var b: B\n" +
            "struct M<P>\n    var w: Two<raw/u8, P>\nstruct M2<P>\n    var w: Two<P, P>\nenum MyOpt<T>\n    Has(T)\n    Not\n" +
            "struct HoldEnum\n    var e: MyOpt<raw/u8>\nstruct HoldTW\n    var t: Wrap<(raw/u8, i32)>\nstruct H3\n    var h: Holder2\n" +
            "struct PS<s/T>\n    var t: s/T\nstruct HPSo\n    var p: PS<raw/u8>\nstruct HPSs {s}\n    var loan: Loan<uniq/i32 during s>\n" +
            "    var p: PS<raw/u8 during s>\nstruct HPSt\n    var p: PS<raw/u8 during static>\nstruct PU<s/T>\n    s is raw or owner\n" +
            "    var v: s/i32\nstruct HPU\n    var p: PU<raw/u8>\nstruct HPUs {s}\n    var loan: Loan<uniq/i32 during s>\n    var p: PU<raw/u8 during s>\n" +
            "struct HFA\n    var f: Wrap<[2 of raw/u8]>\nenum EP\n    A((Wrap<raw/u8>, i32))\n    B\nstruct HOW\n    var o: Option<Wrap<raw/u8>>\n" +
            "struct G<P>\n    var a: Outer<P>\n    var b: Holder2\nstruct G2<P>\n    var b: Holder2\n    var a: Outer<P>\n" +
            "struct Hold<P>\n    var items: Array<Wrap<P>>\nenum Opt2<P>\n    Has(Wrap<P>)\n    Not\nstruct DerX<P> : B1<Wrap<P>>\n    var n: i32\n" +
            "struct Hold2\n    var items: Array<raw/u8>\nstruct SB\n    var data: raw/(raw/u8) during self\nstruct Box2<T>\n    var p: raw/T during self\n" +
            "struct HB2\n    var b: Box2<raw/u8>\n" +
            "func probe(yu: uniq/i32, direct: RawBuf, holder2: Holder2, holdOpt: HoldOpt, derS: DerS, d2: D2, d3: D3, b3: B3<i32>, m: M<raw/u8>, " +
            "holdEnum: HoldEnum, holdTW: HoldTW, h3: H3, oh: Outer<Holder2>, hpso: HPSo, hpss: HPSs during yu, hpst: HPSt, hpu: HPU, " +
            "hpus: HPUs during yu, hfa: HFA, ep: EP, how: HOW, hold2: Hold2, sb: SB, hb2: HB2, g: G<raw/u8>, g2: G2<raw/u8>, " +
            "top: (Outer<raw/u8>, Holder2), outer: Outer<raw/u8>, wrap: Wrap<raw/u8>, m2: M2<raw/u8>, ps: PS<raw/u8>, pu: PU<raw/u8>, " +
            "hold: Hold<raw/u8>, opt2: Opt2<raw/u8>, derX: DerX<raw/u8>, arr: Array<raw/u8>, ow: Outer<Wrap<raw/u8>>, box2: Box2<raw/u8>)\n" +
            "    return\n");
        var seen = new Dictionary<MirHoldingKey, MirModeDepths>();
        var members = new List<RawMember>();
        string[] through =
        [
            "direct", "holder2", "holdOpt", "derS", "d2", "d3", "b3", "m", "holdEnum", "holdTW", "h3", "oh", "hpso", "hpss", "hpst", "hpu", "hpus",
            "hfa", "ep", "how", "hold2", "sb", "hb2", "g", "g2", "top",
        ];
        Assert.Equal(
            Parameters(body).Skip(1).Select(x => (x.Name, through.Contains(x.Name) ? MirWrites.Through : MirWrites.None)).ToArray(),
            Parameters(body).Skip(1).Select(x => (x.Name, Writes(x.Type))).ToArray());
        Assert.Empty(members);

        MirWrites Writes(BoundType type)
        {
            _ = MirTypes.HoldsLoans(type, c.Types, seen, members, out var writes);
            return writes;
        }
    }

    // SPEC 15.6.6, 16.3.3: destroying a value observes what it holds where its destruction runs a user drop (Logger, also as an element
    // or a Sealed handle's payload), destroys a Function value, or releases a handle whose payload is not Sealed; an abstract part not
    // proven Copy, whose instance decides, is the Limit (DECISIONS R4-M147); a Sealed recursive handle ends. A part past the walk's depth
    // bound, also below a growing recursive handle, is Deep (DECISIONS R4-M153). An observing part is the decided answer (MirOutcome).
    [Fact]
    public void DestructionObservesUserDropsAndUnknownParts()
    {
        var (body, c) = Probe(
            "func probe<T, C>(holder: Holder, logger: Logger, loggers: Array<Logger>, owned: obj/Logger, shared: rc/Plain, atomic: arc/Plain, " +
            "based: obj/Base, action: () -> (), value: T, copied: C, chain: Chain, tree: Tree, forest: Forest, table: Table, logged: Logged, " +
            "nest: Nest<i32>)\n    C is Copy\n    return\n");
        var entered = new Dictionary<BoundType, byte>(ReferenceEqualityComparer.Instance);
        Assert.Equal(
            [("holder", "None"), ("logger", "Decided"), ("loggers", "Decided"),
                ("owned", "Decided"), ("shared", "None"), ("atomic", "None"),
                ("based", "Decided"), ("action", "Decided"), ("value", "Limit"),
                ("copied", "None"), ("chain", "None"), ("tree", "None"), ("forest", "None"),
                ("table", "None"), ("logged", "Decided"), ("nest", "Deep")],
            Parameters(body).Select(x => (x.Name, MirTypes.Observation(x.Type, c.Types, body.Key.Decl, entered).ToString())).ToArray());
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

    // SPEC 15.6.4, 8.10 (β′; DECISIONS R4-M154): the callables a value hands a callee's body that holds it (TyCtxt.HandedCallables) are its
    // Function parts at any depth: a Function value, in a Tuple, an Option, an Array and a struct's Field; Origin-free data holds none, and
    // a growing recursive Type ends at the walk's bound.
    [Fact]
    public void AValueHandsTheCallablesItsPartsHold()
    {
        var (body, c) = Probe("struct Maker<E>\n    var make: (E) -> i32\n" +
            "func probe(action: () -> (), pair: ((i32) -> i32, i32), maybe: Option<() -> ()>, many: Array<(i32) -> ()>, maker: Maker<i32>, " +
            "plain: Plain, nest: Nest<i32>)\n    return\n");
        Assert.Equal(
            [("action", 1), ("pair", 1), ("maybe", 1), ("many", 1), ("maker", 1), ("plain", 0), ("nest", 0)],
            Parameters(body).Select(x => (x.Name, c.Types.HandedCallables(x.Type).Length)).ToArray());
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
        Assert.True(MirTypes.Observation(pairs, c.Types, body.Key.Decl, new(ReferenceEqualityComparer.Instance)).IsNone);
        Assert.False(MirTypes.HoldsRaw(pairs, seen) || MirTypes.HoldsLoans(pairs, c.Types, new(), [], out _));
    }

    // V16 (DECISIONS R4-M153's revision 3): an outcome walk enters a Type again only where it meets it shallower than before (MirDepths),
    // at most once per depth within the bound, so a Type DAG whose shared parts lie at many depths, Fibonacci structs 60 deep (F(60) paths
    // as a tree), is walked in work linear in its distinct Types; the first path the walk takes passes the bound, but every F lies within
    // 30 levels of the root, so no part's Type is left unentered and nothing is Deep.
    [Fact]
    public void AnOutcomeWalkEntersATypeAtMostOncePerDepth()
    {
        var text = new StringBuilder("struct F0\n    var a: i32\nstruct F1\n    var a: F0\n    var b: i32\n");
        for (var i = 2; i <= 60; i++)
        {
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"struct F{i}\n    var a: F{i - 1}\n    var b: F{i - 2}\n");
        }

        var (body, c) = Probe(text.Append("func probe(f: F60)\n    return\n").ToString());
        var f = Parameters(body).Single().Type;
        var entered = new Dictionary<BoundType, byte>(ReferenceEqualityComparer.Instance);
        var walk = new DepthWalk(entered);
        Assert.False(MirTypes.Destruction(f, ref walk));
        output.WriteLine($"entered {walk.Entered} times over {entered.Count} Types");
        Assert.Equal((62, false), (entered.Count, walk.Past)); // F60..F0 and i32.
        Assert.InRange(walk.Entered, 62, 33 * 62);
        Assert.True(MirTypes.Observation(f, c.Types, body.Key.Decl, entered).IsNone);
    }

    // R4-M36, R4-M116: every local Type of every library body is classified and none is deep; a borrow or raw layer always holds a Loan.
    // The output gives the counts.
    [Fact]
    public void LibraryTypesClassify()
    {
        var c = MirBuildTest.Compile("public func main() => ()\n");
        var walked = new Dictionary<MirHoldingKey, MirModeDepths>();
        var entered = new Dictionary<BoundType, byte>(ReferenceEqualityComparer.Instance);
        var (locals, holding, observing) = (0, 0, 0);
        foreach (var body in c.BuildMir().Bodies)
        {
            foreach (var local in body.Locals.ToArray())
            {
                var holds = MirTypes.HoldsLoans(local.Type, c.Types, walked, [], out _);
                var raw = local.Type.Semantics == Kimi.Compiler.Parsing.SemanticsKind.Raw;
                Assert.True(holds || !(raw || MirTypes.IsReference(local.Type)), $"{body.Key.Decl?.Name}: {local.Name}");
                var observation = MirTypes.Observation(local.Type, c.Types, body.Key.Decl, entered);
                Assert.False(observation.IsDeep);
                var observes = !observation.IsNone;
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

        public bool First(BoundType type, int depth)
        {
            this.Met++;
            return seen.Add(type);
        }

        public readonly bool Visit(BoundType type, int depth) => false;

        public readonly bool Deep(BoundType type) => false;
    }

    // A walk that reports nothing and counts the Types it enters by the outcome walks' memo (MirDepths).
    private struct DepthWalk(Dictionary<BoundType, byte> entered) : IMirDestruction
    {
        public int Entered { get; private set; }

        public readonly bool Past => entered.ContainsValue(MirDepths.Past);

        public bool First(BoundType type, int depth)
        {
            var enters = MirDepths.Enter(ref CollectionsMarshal.GetValueRefOrAddDefault(entered, type, out _), depth);
            this.Entered += enters ? 1 : 0;
            return enters;
        }

        public readonly bool Visit(BoundType type, int depth) => false;

        public readonly bool Deep(BoundType type)
        {
            MirDepths.Meet(ref CollectionsMarshal.GetValueRefOrAddDefault(entered, type, out _));
            return false;
        }
    }
}
