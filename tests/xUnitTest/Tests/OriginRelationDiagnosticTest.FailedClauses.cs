// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// SPEC 15.3.3, 23.1.2 (DECISIONS BU-68): a local's clauses are checks that assume nothing, so a failed one leaves Unbound only the
// inference variables it names, through the local's equalities; the other slots complete as they would without it, and its bound clauses
// are judged. A clause that names no bound Origin, or a contract's failed clause, makes the whole declaration unavailable.
public partial class OriginRelationDiagnosticTest
{
    private const string Pair = "struct P {s, t}\n    public let r: ref/i32 during s\n    public let q: ref/i32 during t\n" +
        "    public init(r: ref/i32 during s, q: ref/i32 during t)\n        self.r = r\n        self.q = q\n";

    private const string PairLocal = Pair + "func f(a: ref/i32) -> ref/i32 during a\n    let l = 5\n    let p: P{x} = P.init(a, l@ref)\n";

    private const string CellLocal = Cell + "func f(a: ref/i32) -> ref/i32 during a\n    let h0 = C.init(a)\n    let h: C{x} = h0@move\n";

    private const string Unsatisfied = nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd) + " ";

    // The MIR's records since the flip (R6-check H5): its twin of Binding's record at Binding's row (the clause, or `return p.q` beside
    // `p.q`) until R7 (H0b §5.4, DECISIONS R4-M108), its record at `let k` where Binding rests the slot (R5-M184), and its record at the
    // statement `let p` where Binding records none.
    private const string InitRecord = Unsatisfied + "P.init(a, l@ref)";

    private const string ClauseRecord = Unsatisfied + "origin x.t outlives a";

    private const string ReturnRecord = Unsatisfied + "return p.q";

    private const string LetRecord = Unsatisfied + "let k: ref/i32 during a = p.q";

    private const string SlotV = "struct V {source}\n    public let value: ref/i32 during source\n" +
        "    public init(value: ref/i32 during source) => self.value = value\n";

    private const string GenericV = "struct V<T> {source}\n    public let value: ref/T during source\n" +
        "    public init(value: ref/T during source) => self.value = value\n";

    private const string PickResting = "func pick(x: V) -> V{r}\n    origin r.source == zz\n    return x@move\n";

    /// <summary>Gets each local with a failed clause and its Origin records, and the repair that makes its valid twin with the twin's
    /// records.</summary>
    public static TheoryData<string, string[], string, string, string[]> LocalFailedClauses => new()
    {
        // The slots the failed clause does not name complete, so a valid clause beside it and a use of another slot are judged.
        {
            PairLocal + "        origin x.t outlives a\n        origin x.s == zz\n    return a\n", [InitRecord, ClauseRecord], "x.s == zz", "x.s == a",
            [InitRecord, ClauseRecord]
        },
        {
            PairLocal + "        origin x.s outlives zz\n    return p.q\n", [ReturnRecord, Unsatisfied + "p.q"], "x.s outlives zz", "x.s outlives a",
            [ReturnRecord, Unsatisfied + "p.q"]
        },
        {
            PairLocal + "        origin x.zz outlives a\n    return p.q\n", [ReturnRecord, Unsatisfied + "p.q"], "x.zz", "x.t",
            [InitRecord, ClauseRecord, ReturnRecord, Unsatisfied + "p.q"]
        },

        // The slot it names stays Unbound, resting on it, and so does the equality class of that slot: a fit over it holds (DECISIONS
        // Q13 rule W, PLAN G739), and A5 judges a chain through it between its ends.
        {
            PairLocal + "        origin x.t outlives zz\n    return p.q\n", [ReturnRecord], "x.t outlives zz", "x.t outlives a",
            [InitRecord, ClauseRecord, ReturnRecord, Unsatisfied + "p.q"]
        },
        {
            PairLocal + "        origin x.t outlives zz\n    let k: ref/i32 during a = p.q\n    return k\n", [LetRecord], "x.t outlives zz", "x.t outlives a",
            [InitRecord, ClauseRecord, LetRecord]
        },
        {
            PairLocal + "        origin zz outlives x.t\n    return p.q\n", [ReturnRecord], "zz outlives x.t", "a outlives x.t",
            [ReturnRecord, Unsatisfied + "p.q"]
        },
        {
            PairLocal + "        origin x.t == x.s\n        origin x.s == zz\n    return a\n", [], "x.s == zz", "x.s == a",
            [Unsatisfied + "let p: P{x} = P.init(a, l@ref)"]
        },
        { CellLocal + "        origin x.s outlives zz\n    let w = h.r@follow@ref\n    return w\n", [], "x.s outlives zz", "x.s outlives a", [] },
        {
            Cell + "func g(a: ref/i32, b: ref/i32) -> ref/i32 during a\n    let h0 = C.init(b)\n    let h: C{x} = h0@move\n" +
            "        origin x.s outlives zz\n    let m: ref/i32 during (x.s and b) = b\n    return m\n", [Unproven + "m"], "x.s outlives zz",
            "x.s outlives b", [Unproven + "m"]
        },
    };

    [Theory]
    [MemberData(nameof(LocalFailedClauses))]
    public void ALocalsFailedClauseRestsOnlyTheSlotsItNames(string source, string[] records, string written, string repair, string[] twinRecords)
    {
        Assert.Equal(records, OriginRecords(source + Main));
        Assert.Equal(twinRecords, OriginRecords(source.Replace(written, repair, StringComparison.Ordinal) + Main));
    }

    /// <summary>Gets each declaration whose failed clause leaves a slot of its Types Unbound, with uses of those Types outside it, the
    /// clause as written and its repair (the valid twin).</summary>
    public static TheoryData<string, string, string> UsesOfARestingSlot => new()
    {
        // A result slot: callers that return the result through an equality and borrow out of it.
        {
            SlotV + "func pick(x: V) -> V{r}\n    origin r.source == zz\n    return x@move\n" +
            "func pass(y: V) -> V{q}\n    origin q.source == y.source\n    return pick(y@move)\n" +
            "func borrowIt(y: V) -> ref/i32 during y.source\n    let z = pick(y@move)\n    return z.value\n",
            "r.source == zz", "r.source == x.source"
        },

        // A call argument: the callee's result has the argument's Origin.
        {
            SlotV + PickResting + "func idr(v: ref/i32) -> ref/i32 during v => v\n" +
            "func argBorrow(y: V) -> ref/i32 during y.source\n    let z = pick(y@move)\n    return idr(z.value)\n",
            "r.source == zz", "r.source == x.source"
        },

        // A parameter slot named by the result's raw Origin.
        {
            SlotV + "func rp(x: V{p}) -> raw/i32 during p.source\n    origin p.source == zz\n    return x.value@follow@raw\n" +
            "func rpUse(a: ref/i32) -> raw/i32 during a\n    return rp(V.init(a))\n",
            "    origin p.source == zz\n", string.Empty
        },

        // A parameter slot between two bound clauses.
        {
            SlotV + "func ch(x: ref/i32, y: ref/i32, v: V{p}) -> ref/i32 during x\n    origin p.source == zz\n" +
            "    origin y outlives p.source\n    origin p.source outlives x\n    return y\n" +
            "func chUse(a: ref/i32) -> ref/i32 during a => ch(a, a, V.init(a))\n",
            "    origin p.source == zz\n", string.Empty
        },

        // One slot of a Tuple result, guessed operator.
        {
            SlotV + "func dup(x: ref/i32) -> (V{r}, V{s})\n    origin r.source = s.source\n    origin s.source == x\n" +
            "    return (V.init(x), V.init(x))\n" +
            "func useDup(a: ref/i32) -> ref/i32 during a\n    let t = dup(a)\n    return t.0.value\n",
            "r.source = s.source", "r.source == s.source"
        },

        // A contract requirement's clause: a generic caller over the requirement.
        {
            SlotV + "contract Pick\n    func pick(self: ref/Self, x: V) -> V{r}\n        origin r.source = x.source\n" +
            "struct S\n    Self is Pick\n    public init() => ()\n    public func pick(self: ref/Self, x: V) -> V{r}\n" +
            "        origin r.source == x.source\n        return x@move\n" +
            "func viaG<T>(t: ref/T, y: V{p}) -> V{q}\n    T is Pick\n    origin q.source == p.source\n    return t.pick(y@move)\n",
            "origin r.source = x.source", "origin r.source == x.source"
        },

        // A Field's set slot: the constructor and a member.
        {
            SlotV + "struct S {a}\n    public let item: V{v}\n        origin v.source = a\n" +
            "    public init(item: V during a) => self.item = item@move\n" +
            "    public func peek(self: ref/Self) -> ref/i32 during a => self.item.value\n" +
            "func useS(r: ref/i32) -> ref/i32 during r\n    let s = S.init(V.init(r))\n    return s.peek()\n",
            "v.source = a", "v.source == a"
        },

        // A Case payload's set slot: a construction and a match.
        {
            SlotV + "enum Box {s}\n    Value(V{v})\n        origin v.source = s\n    Empty\n" +
            "func unbox(x: ref/i32) -> ref/i32 during x\n    let b: Box = .Value(V.init(x))\n    match b\n" +
            "        .Value(let w) => return w.value\n        .Empty => return x\n",
            "v.source = s", "v.source == s"
        },

        // A virtual method's result slot: the override's body, whose contract is the original's.
        {
            GenericV + "open struct Base\n    public init() => ()\n    public virtual func get(self: objref/Self, x: V<i32>) -> V<i32>{r}\n" +
            "        origin r.source = x.source\n        return x@move\n" +
            "struct Leaf : Base\n    public init() : base() => ()\n    override func get(self: objref/Self, x: V<i32>) -> V<i32>{r}\n" +
            "        return x@move\n" +
            "func call(b: objref/Base, x: V<i32>) -> i32\n    let v = b.get(x@move)\n    return v.value@follow\n",
            "origin r.source = x.source", "origin r.source == x.source"
        },
    };

    // SPEC 23.1.2 (PLAN G739; DECISIONS Q13 rule W): a slot a declaration's failed clause names stays Unbound in the declaration's Types
    // and reaches every use of them. A fit that meets it reads a failed input directly, so it rests on the part the atom rests on
    // (UnboundCause), wherever the use is: the clause's record is the program's one, and the repaired twin is valid.
    [Theory]
    [MemberData(nameof(UsesOfARestingSlot))]
    public void AFitOverASlotAFailedClauseRestsRecordsNothing(string source, string written, string repair)
    {
        var output = DiagnosticCorpus.Check(source + Main);
        Assert.False(output.Accepted);
        Assert.Empty(OriginRecords(source + Main));
        var twin = source.Replace(written, repair, StringComparison.Ordinal) + Main;
        Assert.NotEqual(source + Main, twin);
        Assert.True(DiagnosticCorpus.Check(twin).Accepted, twin);
    }

    /// <summary>Gets each body with an independent Origin record beside a use of `pick`'s resting result slot.</summary>
    public static TheoryData<string> IndependentBesideARestingSlot => new()
    {
        // Another statement's escape.
        "func esc(y: V) -> ref/i32 during static\n    let z = pick(y@move)\n    let local = 1\n    return local@ref\n",

        // Another position of the same fit.
        "func mix(y: V, a: ref/i32 during p, b: ref/i32 during q) -> (V{s}, ref/i32 during q)\n    origin s.source == y.source\n" +
        "    return (pick(y@move), a)\n",

        // A meet's other operand at the longer end (SPEC 15.3.6).
        "func meet2(x: ref/i32, y: ref/i32) -> ref/i32\n    return x\n" +
        "func useM(y: V, a: ref/i32 during p, b: ref/i32 during q) -> ref/i32 during q\n    origin y.source outlives q\n" +
        "    let z = pick(y@move)\n    return meet2(z.value, a)\n",

        // Another relation of the same function, failing for another reason.
        "func other(y: V, b: ref/i32 during q) -> ref/i32 during y.source\n    let z = pick(y@move)\n    let k = z.value\n    return b\n",
    };

    // SPEC 23.1.2 (DECISIONS Q13 rule W, R4-M118 site (1)): the rule covers only what reads the resting slot, so an independent failure
    // beside it is reported as in the repaired twin.
    [Theory]
    [MemberData(nameof(IndependentBesideARestingSlot))]
    public void AnIndependentRecordBesideARestingSlotIsKept(string body)
    {
        var records = OriginRecords(SlotV + PickResting + body + Main);
        Assert.NotEmpty(records);
        Assert.Equal(OriginRecords(SlotV + PickResting.Replace("== zz", "== x.source", StringComparison.Ordinal) + body + Main), records);
    }

    // A warm rebind of a local whose failed clause rests one slot reuses the failed clauses' storage.
    [Fact]
    [Trait("Purpose", "Allocation")]
    public void AWarmRebindOfALocalsFailedClauseDoesNotAllocate()
    {
        var c = MirBuildTest.Compile(PairLocal + "        origin x.t outlives a\n        origin x.s == zz\n    return a\n" + Main);
        Assert.Contains(c.Binding.Issues, static x => x.Code == DiagnosticCode.UnsatisfiedOriginRelation_Kd);
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
    }
}
