// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

/// <summary>PLAN G276 (WK) and G279, the shared acceptance rows (WORKFLOW Full changes; R4 notes wk\problem-draft-r2.md with
/// problem-final.md: SPEC 15.6.4 read as (iii) with (β′)). A call's write retains the argument's own value (its Loans and the holders it
/// addresses) in a storage exactly when the storage's Type, or a part it holds by value, can hold, Origins fitted, a value of the
/// argument's Type or a borrow formed through it (SPEC 15.6.3); otherwise at most what a read through the argument carries (M2). The
/// answer depends on the Types alone, never on the storage's position (M3), and an abstract Type holds such a value only where the body's
/// premises make it one or a callable the instantiated contract hands the callee's body may retain the argument there, by its result or
/// its writes (M4). One theory per rule; each row is one program of a rule's forms with their twins (an Ok form ends with its use, then
/// the write of <c>v</c>'s referent, and its Bad twin swaps the two; a form with no Ok order has the write first as its twin). A row states
/// every MIR record of its program as the function, the subject's first line and the views (bit i for view i), with the code unless it is
/// <c>ComparisonLoanConflict_Kd</c>. The default pipeline keeps its old rules until R6-check (R4-M50) and is not asserted. Problem r2 §2's
/// D5 (<c>slotBad</c>), D7 (<c>rawBufDead</c>) and D8 (the pinned twoLayers, threeLayers, hiddenLayer, acrossLayers, byUniq,
/// viaUniqLocal, nestedDead and optionalDead) are held by their existing tests and not repeated here.</summary>
public sealed partial class MirBorrowsTest
{
    // The callees and Types the rows share (problem r2 §2's helpers and Types; r2 §3's `R`).
    private const string WkHelpers = """
        func look<T>(value: ref/T) => ()
        func consume<X>(x: X) => ()
        func put<U>(slot: uniq/U, y: U) -> () => slot@follow = y@move
        func keep<F>(f: F) -> F => f@move
        func putIn<U>(slot: uniq/(uniq/U during c), y: U) -> () => slot@follow@follow = y@move
        func putPair<U>(slot: uniq/(U, i32), y: U) -> () => slot@follow = (y@move, 0)
        func putOpt<U>(slot: uniq/Option<U>, y: U) -> () => slot@follow = .Some(y@move)
        func putArr<U>(slot: uniq/[1 of U], y: U) -> () => slot@follow = [y@move]
        struct Node {a}
            public var value: i32 = 0
            public var next: Option<uniq/(Node during a) during a> = .None
            public init() => ()
        struct Leaf {a}
            public var value: i32 = 0
            public var peer: Option<ref/i32 during a> = .None
            public init() => ()
        struct R {a}
            Self is Copy
            public var value: i32 = 0
            public var peer: Option<ref/(R during a) during a> = .None
            public init() => ()
        struct Cell2c3<T>
            public var item: T
            public init(item: T) => self.item = item@move

        """;

    // Template C's heads (problem r2 §2): value and other `uniq/X during o`, X = `ref/i32 during a` or `Leaf during o`; other one layer
    // deeper (D3, D6).
    private const string ConcreteHead = "(value: uniq/(ref/i32 during a) during o, other: uniq/(ref/i32 during a) during o, x: ref/i32 during a)\n";
    private const string ConcreteTwoHead = "(value: uniq/(ref/i32 during a) during o, other: uniq/(uniq/(ref/i32 during a) during o) during p, x: ref/i32 during a)\n";
    private const string OptionHead = "(value: uniq/(ref/i32 during a) during o, other: uniq/(Option<uniq/(ref/i32 during a) during o>) during p, x: ref/i32 during a)\n";
    private const string NodeHead = "(value: uniq/(Node during o) during o, other: uniq/(Node during o) during o)\n";
    private const string LeafHead = "(value: uniq/(Leaf during o) during o, other: uniq/(Leaf during o) during o)\n";
    private const string RHead = "(value: uniq/(R during o) during o, other: uniq/(R during o) during o)\n";
    private const string IntHead = "(value: uniq/i32 during o, other: uniq/(Leaf during o) during o)\n";

    // The callee-side declarations of the builder rows (problem-final; study-beta's forms and the test author's).
    private const string WkMkE = "contract MkE<E>\n    func mk(r: E) -> Self\n";
    private const string WkHoldU = "struct HoldU {a}\n    Self is MkE<uniq/i32 during a>\n    public var held: uniq/i32 during a\n" +
        "    public init(held: uniq/i32 during a) => self.held = held\n    public func mk(r: uniq/i32 during a) -> Self => HoldU.init(held: r@move)\n";

    private const string WkAbsorb = "contract Absorb<E>\n    func absorb(self: uniq/Self, r: E) -> ()\nstruct HoldA {a}\n    Self is Absorb<uniq/i32 during a>\n" +
        "    public var held: Option<uniq/i32 during a> = .None\n    public func absorb(self: uniq/Self, r: uniq/i32 during a) -> () => self.held = .Some(r@move)\n";

    private const string WkMaker = "struct Maker<E, T>\n    public var make: (E) -> T\n    public init(make: (E) -> T) => self.make = make@move\n";
    private const string WkLf = "struct Lf {a}\n    public var value: i32 = 0\n    public var peer: Option<ref/i32 during a>\n" +
        "    public init(p: ref/i32 during a) => self.peer = .Some(p)\n";

    private const string WkBox2 = "struct Box2 {a}\n    public var inner: uniq/i32 during a\n    public init(inner: uniq/i32 during a) => self.inner = inner\n" +
        "func store1(y: uniq/(Box2 during a) during p, w: uniq/(uniq/i32 during p) during q) -> ()\n    w@follow = y@follow.inner@follow@uniq\n";

    private const string WkVirtual = "struct HoldV {a}\n    public var held: uniq/i32 during a\n    public init(held: uniq/i32 during a) => self.held = held\n" +
        "open struct BaseB<T, E>\n    public init() => ()\n    public virtual func mk(self: objref/Self, r: E) -> Option<T> => .None\n" +
        "struct DerB {a} : BaseB<HoldV during a, uniq/i32 during a>\n    public init() => ()\n" +
        "    override func mk(self: objref/Self, r: uniq/i32 during a) -> Option<HoldV during a> => .Some(HoldV.init(held: r@move))\n";

    // The two-layer rows' head (BU's B1): two exclusive layers over one referent Type, `w0` refers to storage the second's Origin lets hold.
    private const string WkTwoLayersHead = "(a1: uniq/(Box2 during a) during o, a2: uniq/(Box2 during a) during p, w0: uniq/(uniq/i32 during p) during q) -> ()\n";

    // The raw-path rows' Type (Revision 10): a raw Field stating the Type's own Origin, which the premise catalog relates to a layer above.
    private const string WkRBox = "struct RBox {a}\n    public var p: raw/(Box2 during a) during a\n    public init(p: raw/(Box2 during a) during a) => self.p = p\n";

    // The view rows' Dictionary (r2.2).
    private const string WkDict = "var d: Dictionary<i32, i32> = [1: 2]\n";

    // The part-Loan rows' two parts of one Type (R4's B1).
    private const string WkTwoParts = "(Option<ref/i32>.Some(z@ref), Option<ref/i32>.Some(z@ref))";

    private const string WkWrap = "struct Wrap {a}\n    public var r: uniq/i32 during a\n    public init(r: uniq/i32 during a) => self.r = r\n";

    // The bodies and head the rows share: a store of the child into the local `slot`, the moves and reborrow of `*v` a builder row passes,
    // and D1's and D2's parameters.
    private const string WkPut = "var slot = other@move\nlet child = v\nput(slot@uniq, child@move)";
    private const string WkMoved = "var v = value@move\nvar slot = other@move\nlet child: uniq/i32 during o = v\n";
    private const string WkEHead = "value: s/T, other: uniq/(s/T), x: T";

    // The G297 rows' bodies (DECISIONS BU-66): a raw handle on the local `b`; a requirement's `T` made from a raw pointer to `w`, which
    // refers to `y`.
    private const string WkRawHandle = "var b: i32 = 0\nlet r = b@uniq\n";
    private const string WkRawMake = "var y: i32 = 1\nvar w: ref/i32 = y@ref\nvar slot: raw/u8 = null\nlet t = T.make(p: slot@raw, q: w@raw@raw/u8)\n";

    // Each row's program, without WkHelpers.
    private static readonly Dictionary<string, string> WkForms = new(StringComparer.Ordinal)
    {
        // M3 (position and identity).
        ["A1"] = WkTwins("a2PutOk", "a2PutBad", WkPair(), WkPut, "_ = slot@follow", "v@follow = x"),
        ["A2"] = WkTwins(
            "b4Chained",
            "b4ChainedBad",
            WkPair("value: s/T, other: s/T, third: s/T, x: T"),
            "var a = other@move\nvar b = third@move\nlet child = v\nput(a@uniq, child@move)\nput(b@uniq, a@move)",
            "_ = b@follow",
            "v@follow = x"),
        ["A3"] = WkTwins("b5ResultPut", "b5ResultPutBad", WkPair(), "var slot = other@move\nlet child = v\nput(slot@uniq, keep(child@move))", "_ = slot@follow", "v@follow = x"),
        ["B1"] = WkTwins("a2DirectOk", "a2DirectBad", WkPair(), "var slot = other@move\nlet child = v\nslot = child@move", "_ = slot@follow", "v@follow = x"),
        ["B2"] = WkTwins("a3RefOk", "a3RefBad", WkPair(), "var slot = other@move\nlet sref = slot@uniq\nlet child = v\nsref@follow = child@move", "_ = slot@follow", "v@follow = x"),
        ["B3"] = WkTwins("a4TupleOk", "a4TupleBad", WkPair(), "var slot = other@move\nvar pair = (slot@uniq, 1)\nlet child = v\npair.0@follow = child@move", "_ = slot@follow", "v@follow = x"),
        ["D1"] = WkForm("eHoldsBad", WkPair(WkEHead), "let child = v\nput(other, child@move)\nv@follow = x") +
            WkForm("eHoldsFirst", WkPair(WkEHead), "v@follow = x\nlet child = v\nput(other, child@move)"),
        ["D2"] = WkForm("eTwoBad", WkPair(WkEHead), "var r = other@move\nlet child = v\nputIn(r@uniq, child@move)\nv@follow = x") +
            WkForm("eTwoFirst", WkPair(WkEHead), "var r = other@move\nv@follow = x\nlet child = v\nputIn(r@uniq, child@move)"),
        ["D3"] = WkForm("concreteTwoBad", ConcreteTwoHead, "var v = value@move\nvar r = other@move\nlet child = v\nputIn(r@uniq, child@move)\nv@follow = x") +
            WkForm("concreteTwoFirst", ConcreteTwoHead, "var v = value@move\nvar r = other@move\nv@follow = x\nlet child = v\nputIn(r@uniq, child@move)"),

        // M2 (part kinds).
        ["C1"] = WkTwins("tuplePutOk", "tuplePutBad", WkPair(), "var pair = (other@move, 1)\nlet child = v\nputPair(pair@uniq, child@move)", "_ = pair.0@follow", "v@follow = x"),
        ["C2"] = WkTwins("tupleRefOk", "tupleRefBad", WkPair(), "var slot = other@move\nvar pair = (slot@uniq, 1)\nlet child = v\nput(pair.0, child@move)", "_ = slot@follow", "v@follow = x"),
        ["C3"] = WkTwins("optPutOk", "optPutBad", WkPair(), "var opt: Option<s/T> = .Some(other@move)\nlet child = v\nputOpt(opt@uniq, child@move)", "look(opt@ref)", "v@follow = x"),
        ["C4"] = WkTwins("arrPutOk", "arrPutBad", WkPair(), "var arr: [1 of s/T] = [other@move]\nlet child = v\nputArr(arr@uniq, child@move)", "look(arr@ref)", "v@follow = x"),
        ["C5"] = WkTwins("arrElemPutOk", "arrElemPutBad", WkPair(), "var arr: Array<s/T> = [other@move]\nlet child = v\nput(arr[0]@uniq, child@move)", "look(arr@ref)", "v@follow = x"),
        ["C6"] = WkTwins("arrAppendOk", "arrAppendBad", WkPair(), "var arr: Array<s/T> = [other@move]\nlet child = v\narr.append(child@move)", "look(arr@ref)", "v@follow = x"),
        ["C7"] = WkTwins("dictIndexOk", "dictIndexBad", "()\n", "var d: Dictionary<i32, i32> = [1: 2]\nlet k = 1\nlet r = d.index(k@ref)", "look(r)", "d.clear()") +
            WkForm("dictGetBad", "()\n", "var d: Dictionary<i32, i32> = [1: 2]\nlet k = 1\nlet r = d.tryGet(k@ref)\nd.clear()\nlook(r@ref)"),
        ["D4"] = WkForm("optEBad", OptionHead, "var v = value@move\nlet child = v\nputOpt(other, child@move)\nv@follow = x") +
            WkForm("optEFirst", OptionHead, "var v = value@move\nv@follow = x\nlet child = v\nputOpt(other, child@move)"),
        ["byValuePart"] = WkForm("first", "(x: (ref/i32 during a, i32)) -> ref/i32 during a\n", "return x.0") +
            WkForm("byValuePartBad", "()\n", "var n = 1\nlet r = first((n@ref, 2))\nn = 5\nlook(r@ref)"),
        ["E1"] = WkTwins("concretePutOk", "concretePutBad", ConcreteHead, "var v = value@move\n" + WkPut, "_ = slot@follow", "v@follow = x"),
        ["E3"] = WkForm("recurNodeBad", NodeHead, "var v = value@move\n" + WkPut + "\n_ = slot.value\nv.value = 5") +
            WkForm("recurNodeFirst", NodeHead, "var v = value@move\nv.value = 5\n" + WkPut + "\n_ = slot.value"),

        // M4 (abstract premises without a callable).
        ["F1"] = WkTwins("uniqCopyOk", "uniqCopyBad", "<s/T>(value: s/T, other: s/T, x: T) -> ()\n    s is uniq\n    T is Copy\n", "var v = value@move\n" + WkPut, "look(slot@ref)", "v@follow = x"),
        ["abstractRef"] = "contract N\n    func n(self: ref/Self) -> ref/i32 during self\n" +
            WkForm("sneakRH", "<T>(w: uniq/(ref/i32 during o), y: ref/T during o) -> ()\n", "return") +
            WkForm("rhUse", "<T>(t: T, z: ref/i32 during o) -> ()\n", "var w = z\nlet b = t@ref\nsneakRH(w@uniq, b)\nconsume(t@move)\n_ = w@follow") +
            WkForm("sneakRN", "<T>(w: uniq/(ref/i32 during o), y: ref/T during o) -> ()\n    T is N\n", "w@follow = y.n()") +
            WkForm("rnUse", "<T>(t: T, z: ref/i32 during o) -> ()\n    T is N\n", "var w = z\nlet b = t@ref\nsneakRN(w@uniq, b)\nconsume(t@move)\n_ = w@follow"),
        ["F2"] = WkTwins("uniqAnyBad", "uniqAnyLate", "<s/T>(value: s/T, other: s/T, x: T) -> ()\n    s is uniq\n", "var v = value@move\n" + WkPut, "look(slot@ref)", "v@follow = x@move"),
        ["rawArg"] = "unsafe func putRaw<V>(handle: raw/i32, value: V) => ()\nunsafe func putSlot(handle: raw/i32, slot: uniq/(raw/i32)) => ()\n" +
            WkForm("dictPairOk", WkRawPair("x: s/T, y: s/T", "owner or uniq"), "let d: Dictionary<i32, s/T> = [1: x]\nlet k = d.length") +
            WkForm("dictTwoOk", WkRawPair("x: s/T, y: s/T", "owner or uniq"), "let d: Dictionary<i32, s/T> = [1: x, 2: y]\nlet k = d.length") +
            WkForm("dictUniqOk", "<T>(x: uniq/T) -> ()\n    T is Copy\n", "let d: Dictionary<i32, uniq/T> = [1: x]\nlet k = d.length") +
            WkForm("dictMovedOk", "<s/T>(x: s/T) -> ()\n    s is owner or uniq\n", "let d: Dictionary<i32, s/T> = [1: x@move]\nlet k = d.length") +
            WkForm("dictRefUniqOk", WkRawPair("x: s/T", "ref or uniq"), "let d: Dictionary<i32, s/T> = [1: x]\nlet k = d.length") +
            WkForm("arrPairOk", WkRawPair("x: s/T, y: s/T", "owner or uniq"), "let a: Array<s/T> = [x, y]\nlet k = a.length") +
            WkForm("putRawOk", WkRawPair("x: s/T", "owner or uniq"), WkRawHandle + "unsafe => putRaw(r@follow@raw, x)\nb = 1") +
            WkForm("putRawUniqOk", "<T>(x: uniq/T) -> ()\n    T is Copy\n", WkRawHandle + "unsafe => putRaw(r@follow@raw, x)\nb = 1") +
            WkForm("putRawDeadOk", "<T>(x: T) -> ()\n    T is Copy\n", "var t = x\n" + WkRawHandle + "unsafe => putRaw(r@follow@raw, t@uniq)\nb = 1") +
            WkForm("putRawLiveOk", "<T>(x: T) -> ()\n    T is Copy\n", "var t = x\n" + WkRawHandle + "unsafe => putRaw(r@follow@raw, t@uniq)\nb = 1\nlet u = t") +
            WkForm("putSlotBad", "() -> ()\n", WkRawHandle + "var s: raw/i32 = null\nunsafe => putSlot(r@follow@raw, s@uniq)\nb = 1\nlook(s@ref)"),

        // M3 with R1 (a Loan on a part: the storage the call writes is the Place the Loan names, its holder the Place's root).
        ["partLoan"] = WkLf +
            WkForm("sneak4", "<T>(slot: uniq/((uniq/(Option<ref/i32 during q>) during q, Option<T>)), y: uniq/(Lf during q) during q) -> ()\n", "slot@follow.0@follow = .Some(y.value@ref)") +
            WkForm(
                "sneak5",
                "<T>(slot: uniq/((uniq/(Option<ref/i32 during q>) during q, uniq/(Option<ref/i32 during r>) during r, Option<T>)), y: uniq/(Lf during q) during q) -> ()\n",
                "slot@follow.0@follow = .Some(y.value@ref)") +
            WkTwins("pa3Ok", "pa3Bad", "<T>() -> ()\n    T is Copy\n", WkPartLoan("(Option<ref/i32>.Some(z@ref), 1)", "(inner.0@uniq, Option<T>.None)", "sneak4"), "look(inner@ref)", "leaf.value = 5") +
            WkTwins("pa3ConcreteOk", "pa3ConcreteBad", "() -> ()\n", WkPartLoan("(Option<ref/i32>.Some(z@ref), 1)", "(inner.0@uniq, Option<i32>.None)", "sneak4"), "look(inner@ref)", "leaf.value = 5") +
            WkForm("pa3WholeBad", "<T>() -> ()\n    T is Copy\n", WkPartLoan("Option<ref/i32>.Some(z@ref)", "(inner@uniq, Option<T>.None)", "sneak4") + "\nleaf.value = 5\nlook(inner@ref)") +
            WkForm("pa5FirstBad", "<T>() -> ()\n    T is Copy\n", WkPartLoan(WkTwoParts, "(inner.0@uniq, inner.1@uniq, Option<T>.None)", "sneak5") + "\nleaf.value = 5\nlook(inner.0@ref)") +
            WkForm("pa5Second", "<T>() -> ()\n    T is Copy\n", WkPartLoan(WkTwoParts, "(inner.0@uniq, inner.1@uniq, Option<T>.None)", "sneak5") + "\nleaf.value = 5\nlook(inner.1@ref)"),

        // R5 (the borrows formed through the argument).
        ["E2"] = WkTwins("recurLeafOk", "recurLeafBad", LeafHead, "var v = value@move\n" + WkPut, "_ = slot.value", "v.value = 5"),
        ["E2a"] = WkTwins(
            "recurLeafDistinctOk",
            "recurLeafDistinctBad",
            "(value: uniq/(Leaf during a) during o, other: uniq/(Leaf during a) during o)\n",
            "var v = value@move\n" + WkPut,
            "_ = slot.value",
            "v.value = 5"),
        ["twoLayers"] = WkBox2 +
            WkForm("sneakOP", "(x: (uniq/(Box2 during a) during o, uniq/(Box2 during a) during p), w: uniq/(uniq/i32 during p) during q) -> ()\n", "var t = x@move\nstore1(t.1@move, w)") +
            WkForm("sneakPO", "(x: (uniq/(Box2 during a) during p, uniq/(Box2 during a) during o), w: uniq/(uniq/i32 during p) during q) -> ()\n", "var t = x@move\nstore1(t.0@move, w)") +
            WkForm("opBad", WkTwoLayersHead, "sneakOP((a1@follow@uniq, a2@follow@uniq), w0@follow@uniq)\na2@follow.inner@follow = 5") +
            WkForm("opFirst", WkTwoLayersHead, "a2@follow.inner@follow = 5\nsneakOP((a1@follow@uniq, a2@follow@uniq), w0@follow@uniq)") +
            WkForm("poBad", WkTwoLayersHead, "sneakPO((a2@follow@uniq, a1@follow@uniq), w0@follow@uniq)\na2@follow.inner@follow = 5") +
            WkForm("poFirst", WkTwoLayersHead, "a2@follow.inner@follow = 5\nsneakPO((a2@follow@uniq, a1@follow@uniq), w0@follow@uniq)"),
        ["rawPath"] = WkBox2 + WkRBox + WkRawForms(string.Empty, "a", "raw/(Box2 during a) during r") + WkRawForms("Rel", "r", "RBox during r") +
            WkForm("sneakR3", "(zr: ref/i32 during r, zp: ref/(Box2 during a) during p, x: uniq/(raw/(Box2 during a) during r) during p, w: uniq/(uniq/i32 during p) during q) -> () => ()\n") +
            WkForm("r3Ok", "(zr: ref/i32 during r, zp: ref/(Box2 during a) during p, a2: uniq/(raw/(Box2 during a) during r) during p, a3: raw/(Box2 during a) during r, w0: uniq/(uniq/i32 during p) during q) -> ()\n", "sneakR3(zr, zp, a2@follow@uniq, w0@follow@uniq)\na2@follow = a3") +
            WkForm("sneakR3R", "(zr: ref/i32 during r, x: uniq/(RBox during r) during p, w: uniq/(uniq/i32 during p) during q) -> () => ()\n") +
            WkForm("r3RBad", "(zr: ref/i32 during r, a2: uniq/(RBox during r) during p, a3: RBox during r, w0: uniq/(uniq/i32 during p) during q) -> ()\n", "sneakR3R(zr, a2@follow@uniq, w0@follow@uniq)\na2@follow = a3"),
        ["viewHolds"] =
            WkForm("iterClearBad", "() -> ()\n", WkDict + "var it = d.iterate()\nd.clear()\n_ = it.next()") +
            WkForm("iterClearOk", "() -> ()\n", WkDict + "var it = d.iterate()\n_ = it.next()\nd.clear()") +
            WkForm("iterUniqLengthBad", "() -> ()\n", WkDict + "var it = d.iterateUniq()\n_ = d.length\n_ = it.next()") +
            WkForm("iterUniqLengthOk", "() -> ()\n", WkDict + "var it = d.iterateUniq()\n_ = it.next()\n_ = d.length") +
            WkForm("utf8MoveBad", "() -> ()\n", "let text = \"ab\"\nlet view = Text.utf8(text)\nconsume(text@move)\nlook(view@ref)") +
            WkForm("utf8MoveOk", "() -> ()\n", "let text = \"ab\"\nlet view = Text.utf8(text)\nlook(view@ref)\nconsume(text@move)"),
        ["putR"] = WkForm("putRUse", RHead, "var v = value@move\n" + WkPut + "\n_ = slot.value\nv.value = 5") +
            WkForm("putRFirst", RHead, "var v = value@move\nv.value = 5\n" + WkPut + "\n_ = slot.value"),
        ["use"] = WkForm("tie0c1", "(a: ref/i32 during q, c: ref/i32 during o, b: Option<uniq/(ref/i32 during q) during o>) -> ()\n", "return") +
            WkForm("tie0c2", "(a: ref/i32 during q, b: Option<uniq/(ref/i32 during q) during q>) -> ()\n", "return") +
            WkForm("tie0c3", "(a: ref/i32 during q, b: Option<uniq/(i32, ref/i32 during q) during q>) -> ()\n", "return") +
            WkForm("use1", "()\n", "let y = 5\nlet z = 6\nvar x = y@ref\ntie0c1(z@ref, z@ref, .Some(x@uniq))\nlet w = x@follow") +
            WkForm("use2", "()\n", "let y = 5\nlet z = 6\nvar x = y@ref\ntie0c2(z@ref, .Some(x@uniq))\nlet w = x@follow") +
            WkForm("use3Bad", "()\n", "let y = 5\nlet z = 6\nvar x = (1, y@ref)\ntie0c3(z@ref, .Some(x@uniq))\nlet w = x.1"),

        // R7 (Closures).
        ["D6/G4"] = WkForm(
            "closureEBad",
            ConcreteTwoHead,
            "var v = value@move\nvar set = func [other@move] (y: uniq/(ref/i32 during a) during o) -> ()\n    other@follow = y@move\nlet child = v\nset(child@move)\nv@follow = x") +
            WkForm(
            "closureEOk",
            ConcreteHead,
            "var v = value@move\nvar set = func [other@move] (y: uniq/(ref/i32 during a) during o) -> ()\n    other@follow = y@follow\nlet child = v\nset(child@move)\nv@follow = x"),
        ["G1"] = WkForm(
            "a3ClosureBad",
            WkPair(),
            "var slot = other@move\nvar set = func [slot@uniq] (y: s/T) -> ()\n    slot@follow = y@move\nlet child = v\nset(child@move)\nv@follow = x\n_ = slot@follow") +
            WkForm(
            "a3ClosureFirst",
            WkPair(),
            "var slot = other@move\nvar set = func [slot@uniq] (y: s/T) -> ()\n    slot@follow = y@move\nv@follow = x\nlet child = v\nset(child@move)\n_ = slot@follow"),
        ["G2"] = WkForm(
            "pairCellBad",
            WkPair(),
            "var cell = Cell2c3.init(item: other@move)\nvar set = func [cell@uniq] (y: s/T) -> ()\n    cell@follow.item = y@move\nlet child = v\nset(child@move)\nv@follow = x\n_ = cell.item@follow") +
            WkForm(
            "pairCellFirst",
            WkPair(),
            "var cell = Cell2c3.init(item: other@move)\nvar set = func [cell@uniq] (y: s/T) -> ()\n    cell@follow.item = y@move\nv@follow = x\nlet child = v\nset(child@move)\n_ = cell.item@follow"),
        ["G3"] = WkForm(
            "closureVarOk",
            ConcreteHead,
            "var v = value@move\nvar set = func [var other] (y: uniq/(ref/i32 during a) during o) -> ()\n    other = y@move\nlet child = v\nset(child@move)\nv@follow = x") +
            WkForm(
            "closureVarBad",
            ConcreteHead,
            "var v = value@move\nvar set = func [var other] (y: uniq/(ref/i32 during a) during o) -> ()\n    other = y@move\nlet child = v\nset(child@move)\nv@follow = x\nlook(set@ref)"),

        // H (a borrow stored through the argument).
        ["H1"] = WkForm("sneakInt", "(slot: uniq/(uniq/(Leaf during o) during o), y: uniq/i32 during o)\n", "slot@follow@follow.peer = .Some(y@follow@ref)") +
            WkForm("sneakIntUse", IntHead, "var v = value@move\nvar slot = other@move\nlet child = v\nsneakInt(slot@uniq, child@move)\n_ = slot.value\nv@follow = 5") +
            WkForm("sneakIntFirst", IntHead, "var v = value@move\nvar slot = other@move\nv@follow = 5\nlet child = v\nsneakInt(slot@uniq, child@move)\n_ = slot.value"),
        ["H2"] = WkForm("sneakLeaf", "(slot: uniq/(uniq/(Leaf during o) during o), y: uniq/(Leaf during o) during o)\n", "slot@follow@follow.peer = .Some(y.value@ref)") +
            WkForm("sneakLeafUse", LeafHead, "var v = value@move\nvar slot = other@move\nlet child = v\nsneakLeaf(slot@uniq, child@move)\n_ = slot.value\nv.value = 5") +
            WkForm("sneakLeafFirst", LeafHead, "var v = value@move\nvar slot = other@move\nv.value = 5\nlet child = v\nsneakLeaf(slot@uniq, child@move)\n_ = slot.value"),
        ["H3"] = WkForm(
            "closureSneakUse",
            LeafHead,
            "var v = value@move\nvar set = func [other@move] (y: uniq/(Leaf during o) during o) -> ()\n    other.peer = .Some(y.value@ref)\nlet child = v\nset(child@move)\nv.value = 5") +
            WkForm(
            "closureSneakFirst",
            LeafHead,
            "var v = value@move\nvar set = func [other@move] (y: uniq/(Leaf during o) during o) -> ()\n    other.peer = .Some(y.value@ref)\nv.value = 5\nlet child = v\nset(child@move)"),
        ["H4"] = WkForm("directLeaf", LeafHead, "var v = value@move\nvar slot = other@move\nlet child = v\nslot@follow.peer = .Some(child.value@ref)\n_ = slot.value\nv.value = 5") +
            WkForm("directLeafContent", LeafHead, "var v = value@move\nvar slot = other@move\nlet child = v\nslot@follow.peer = child.peer\n_ = slot.value\nv.value = 5"),

        // The callables the instantiated contract hands the callee's body (M4, (β′)'s list; PLAN G279).
        ["reqArg"] = WkMkE + WkHoldU + WkForm("sneakG", "<T, E>(slot: uniq/T, y: E) -> ()\n    T is MkE<E>\n", "slot@follow = T.mk(y@move)") +
            WkBuilders("betaG", "<T>(value: uniq/i32 during o, other: T) -> ()\n    T is MkE<uniq/i32 during o>\n", "sneakG<T, uniq/i32 during o>(slot@uniq, child@move)"),
        ["resultG"] = WkMkE + WkEnded(
            "resultGOk",
            "resultGBad",
            "<T>(value: uniq/i32 during o) -> ()\n    T is MkE<uniq/i32 during o>\n",
            "var v = value@move\nlet child: uniq/i32 during o = v\nlet t = T.mk(child@move)",
            "t"),
        ["reqStatic"] = "contract MkS<E>\n    func build(r: E) -> Self\n" + WkForm("sneakS", "<T, E>(slot: uniq/T, y: E) -> ()\n    T is MkS<E>\n", "slot@follow = T.build(y@move)") +
            WkBuilders("reqStatic", "<T>(value: uniq/i32 during o, other: T) -> ()\n    T is MkS<uniq/i32 during o>\n", "sneakS<T, uniq/i32 during o>(slot@uniq, child@move)"),
        ["reqContains"] = "contract MkO<E>\n    func mkOpt(r: E) -> Option<Self>\n" +
            WkForm("sneakO", "<T, E>(slot: uniq/Option<T>, y: E) -> ()\n    T is MkO<E>\n", "slot@follow = T.mkOpt(y@move)") +
            WkBuilders("reqContains", "<T>(value: uniq/i32 during o, other: Option<T>) -> ()\n    T is MkO<uniq/i32 during o>\n", "sneakO<T, uniq/i32 during o>(slot@uniq, child@move)"),
        ["reqOther"] = "contract Builder<E, T>\n    func run(self: ref/Self, r: E) -> T\n" +
            WkForm("sneakR", "<T, E, F>(slot: uniq/T, y: E, f: ref/F) -> ()\n    F is Builder<E, T>\n", "slot@follow = f.run(y@move)") +
            WkBuilders(
                "reqOther",
                "<T, F>(value: uniq/i32 during o, other: T, f: ref/F) -> ()\n    F is Builder<uniq/i32 during o, T>\n",
                "sneakR<T, uniq/i32 during o, F>(slot@uniq, child@move, f)"),
        ["reqAssoc"] = "contract Holds<E>\n    associate Box\n    func wrap(r: E) -> Self.Box\n" +
            WkForm("sneakB", "<T, E>(slot: uniq/T.Box, y: E) -> ()\n    T is Holds<E>\n", "slot@follow = T.wrap(y@move)") +
            WkBuilders("reqAssoc", "<T>(value: uniq/i32 during o, other: T.Box) -> ()\n    T is Holds<uniq/i32 during o>\n", "sneakB<T, uniq/i32 during o>(slot@uniq, child@move)"),
        ["fnValue"] = WkForm("sneakF", "<T, E>(slot: uniq/T, y: E, make: (E) -> T) -> ()\n", "slot@follow = make(y@move)") +
            WkBuilders("betaF", "<T>(value: uniq/i32 during o, other: T, make: (uniq/i32 during o) -> T) -> ()\n", "sneakF(slot@uniq, child@move, make@move)"),
        ["resultF"] = WkEnded(
            "resultFOk",
            "resultFBad",
            "<T>(value: uniq/i32 during o, make: (uniq/i32 during o) -> T) -> ()\n",
            "var v = value@move\nlet child: uniq/i32 during o = v\nlet t = make(child@move)",
            "t"),
        ["fnInTuple"] = WkForm("sneakT", "<T, E>(slot: uniq/T, y: E, pair: ((E) -> T, i32)) -> ()\n", "slot@follow = pair.0(y@move)") +
            WkBuilders("fnInTuple", "<T>(value: uniq/i32 during o, other: T, pair: ((uniq/i32 during o) -> T, i32)) -> ()\n", "sneakT(slot@uniq, child@move, pair@move)"),
        ["fnInOption"] = WkForm("sneakP", "<T, E>(slot: uniq/T, y: E, maybe: Option<(E) -> T>) -> ()\n", "match maybe\n    .Some(let make) => slot@follow = make(y@move)\n    .None => ()") +
            WkBuilders("fnInOption", "<T>(value: uniq/i32 during o, other: T, maybe: Option<(uniq/i32 during o) -> T>) -> ()\n", "sneakP(slot@uniq, child@move, maybe@move)"),
        ["fnInStruct"] = WkMaker + WkForm("sneakM", "<T, E>(slot: uniq/T, y: E, m: Maker<E, T>) -> ()\n", "slot@follow = m.make(y@move)") +
            WkBuilders("fnInStruct", "<T>(value: uniq/i32 during o, other: T, m: Maker<uniq/i32 during o, T>) -> ()\n", "sneakM(slot@uniq, child@move, m@move)"),
        ["reqBounded"] = "contract Mk\n    func mk(r: ref/i32) -> Self\n" + WkForm("sneakK", "<T>(slot: uniq/T, y: ref/i32 during o) -> ()\n    T is Mk\n", "slot@follow = T.mk(y)") +
            WkBuilders("reqBounded", "<T>(value: uniq/i32 during o, other: T) -> ()\n    T is Mk\n", "sneakK<T>(slot@uniq, child@follow@ref)"),
        ["writerReq"] = WkAbsorb + WkForm("sneakA", "<T, E>(slot: uniq/T, y: E) -> ()\n    T is Absorb<E>\n", "slot.absorb(y@move)") +
            WkBuilders("writerReq", "<T>(value: uniq/i32 during o, other: T) -> ()\n    T is Absorb<uniq/i32 during o>\n", "sneakA<T, uniq/i32 during o>(slot@uniq, child@move)") +
            WkForm("writerReqDirectBad", "<T>(value: uniq/i32 during o, other: T) -> ()\n    T is Absorb<uniq/i32 during o>\n", WkMoved + "slot.absorb(child@move)\nv@follow = 5\nlook(slot@ref)"),
        ["virtual"] = WkVirtual +
            WkEnded(
                "virtOk",
                "virtBad",
                "<T>(value: uniq/i32 during o, b: objref/BaseB<T, uniq/i32 during o>) -> ()\n",
                "var v = value@move\nlet child: uniq/i32 during o = v\nlet t = b.mk(child@move)",
                "t") +
            WkForm(
                "virtConcreteBad",
                "(value: uniq/i32 during o, b: objref/BaseB<HoldV during o, uniq/i32 during o>) -> ()\n",
                "var v = value@move\nlet child: uniq/i32 during o = v\nlet t = b.mk(child@move)\nv@follow = 5\nlook(t@ref)"),
        ["erasedOrigins"] = "open struct BaseG<T>\n    public init() => ()\n    public virtual func pick(self: objref/Self, x: ref/i32, y: ref/i32) -> ref/i32 during x => x\n" +
            "contract Pk\n    func pick(self: ref/Self, x: ref/i32, y: ref/i32) -> ref/i32 during x\n" +
            WkForm("virtPickOk", "<T>(b: objref/BaseG<T>) -> ()\n", WkPick("b") + "n = 5\nlook(r@ref)") +
            WkForm("virtPickBad", "<T>(b: objref/BaseG<T>) -> ()\n", WkPick("b") + "m = 5\nlook(r@ref)") +
            WkForm("virtPickConcreteOk", "(b: objref/BaseG<i32>) -> ()\n", WkPick("b") + "n = 5\nlook(r@ref)") +
            WkForm("reqPickOk", "<T>(t: ref/T) -> ()\n    T is Pk\n", WkPick("t") + "n = 5\nlook(r@ref)"),
        ["erasedSetter"] = "contract Ct<E>\n    E is Copy\n    property item: E has get, set\n" +
            "struct Sp\n    Self is Ct<i32>\n    public var item: i32 = 7\n    public init() => ()\n" +
            WkForm("setGetOk", "<T>(s: uniq/T) -> i32\n    T is Ct<i32>\n", "s.item = 9\nreturn s.item") +
            WkForm("setAddOk", "<T>(s: uniq/T) -> i32\n    T is Ct<i32>\n", "s.item += 2\nreturn s.item") +
            WkForm("setGetConcreteOk", "(s: uniq/Sp) -> i32\n", "s.item = 9\nreturn s.item") +
            WkForm("setHeldBad", "<T>(s: uniq/T) -> i32\n    T is Ct<i32>\n", "let r = s@follow@ref\ns.item = 9\nlook(r)\nreturn 0"),
        ["writerFn"] = WkForm("sneakW", "<T, E>(slot: uniq/T, y: E, set: (uniq/T, E) -> ()) -> ()\n", "set(slot, y@move)") +
            WkBuilders("writerFn", "<T>(value: uniq/i32 during o, other: T, set: (uniq/T, uniq/i32 during o) -> ()) -> ()\n", "sneakW(slot@uniq, child@move, set@move)") +
            WkForm("writerFnDirectBad", "<T>(value: uniq/i32 during o, other: T, set: (uniq/T, uniq/i32 during o) -> ()) -> ()\n", WkMoved + "set(slot@uniq, child@move)\nv@follow = 5\nlook(slot@ref)"),
        ["callableBound"] = WkWrap + WkForm("sneakC", "<T, E, F>(slot: uniq/T, y: E, make: ref/F) -> ()\n    F is Callable<(E) -> T>\n", "slot@follow = make(y@move)") +
            WkEnded(
                "callableOk",
                "callableBad",
                "<T, F>(value: uniq/i32 during o, other: T, make: ref/F) -> ()\n    F is Callable<(Wrap{w}) -> T>\n    origin w.a == o\n",
                WkMoved + "let wr = Wrap.init(r: child@move)\nsneakC(slot@uniq, wr@move, make)",
                "slot"),
        ["rawReq"] = "contract MakeW\n    func make(p: raw/(raw/u8), q: raw/u8) -> Self\ncontract MakeP\n    func make(p: raw/(raw/i32)) -> Self\n" +
            "func fillRaw<T>(dst: uniq/T, p: raw/u8)\n    T is MakeW\n    return\nfunc fillBare<T>(dst: uniq/T, p: raw/u8) => ()\n" +
            WkForm("fill32", "<T>(dst: uniq/T, p: raw/i32)\n    T is MakeW\n", "var slot: raw/u8 = null\ndst@follow = T.make(p: slot@raw, q: p@raw/u8)") +
            WkForm("fillP", "<T>(dst: uniq/T, p: raw/i32)\n    T is MakeP\n", "var s = p\ndst@follow = T.make(p: s@raw)") +
            WkForm("useH", "<T>(dst: uniq/T, p: raw/i32, mk: (raw/u8) -> T)\n", "dst@follow = mk(p@raw/u8)") +
            WkForm("fill32Bad", "<T>(x: T) -> ()\n    T is MakeW\n", WkRawFill("fill32", "i32") + "y = 5\nconsume(t@move)") +
            WkForm("fillPBad", "<T>(x: T) -> ()\n    T is MakeP\n", WkRawFill("fillP", "i32") + "y = 5\nconsume(t@move)") +
            WkForm("useHBad", "<T>(x: T, mk: (raw/u8) -> T) -> ()\n", WkRawFill("useH", "i32", ", mk@move") + "y = 5\nconsume(t@move)") +
            WkForm("makeRawBad", "<T>() -> ()\n    T is MakeW\n", WkRawMake + "y = 5\nconsume(t@move)") +
            WkForm("makeRawOk", "<T>() -> ()\n    T is MakeW\n", WkRawMake + "consume(t@move)\ny = 5") +
            WkForm("fillRawBad", "<T>(x: T) -> ()\n    T is MakeW\n", WkRawFill("fillRaw") + "y = 5\nconsume(t@move)") +
            WkForm("fillBareLate", "<T>(x: T) -> ()\n", WkRawFill("fillBare") + "y = 5\nconsume(t@move)"),
        ["rawCast"] = "func usep(p: raw/u8) => ()\n" + WkCastHolder("S8", "u8") + WkCastHolder("S32", "i32") +
            WkCastTwins("cast8", "S8", "u8", "raw/i32", "p@raw/u8") + WkCastTwins("same8", "S8", "u8", "raw/u8", "p") +
            WkCastTwins("cast32", "S32", "i32", "raw/i32", "p@raw/u8") + WkCastTwins("same32", "S32", "i32", "raw/u8", "p"),
    };

    // M3, SPEC 15.6.4 with R1: a call keeps what the direct stores its Types permit would keep, whatever the storage's position or identity:
    // a local written through a call (A1), a chain of calls (A2), a call's result (A3), a direct store (B1), a store through a reference (B2)
    // or a Tuple's element (B3), storage the body does not own (D1) and a second layer (D2, D3). The Ok forms' abstract T past the holder
    // holds nothing of the child (M4).
    [Theory]
    [InlineData("A1", "a2PutBad: v@follow = x 2")] // Fails on dev by design: a2PutOk `v@follow = x` 2 (the a5-writes ledger rows).
    [InlineData("A2", "b4ChainedBad: v@follow = x 2")] // Fails on dev by design: b4Chained `v@follow = x` 2.
    [InlineData("A3", "b5ResultPutBad: v@follow = x 2")] // Fails on dev by design: b5ResultPut `v@follow = x` 2.
    [InlineData("B1", "a2DirectBad: v@follow = x 2")]
    [InlineData("B2", "a3RefBad: v@follow = x 2")]
    [InlineData("B3", "a4TupleBad: v@follow = x 2")]
    [InlineData("D1", "eHoldsBad: v@follow = x 2")]
    [InlineData("D2", "eTwoBad: v@follow = x 2")]
    [InlineData("D3", "concreteTwoBad: v@follow = x 1")]
    // Fails on fd4f56ae3: pa3Bad valid (the holder `inner`, reached through a Loan on its part, took the entry of its whole Type, which
    // none names: the Abstract one). pa5Second: both parts have one erased Type, whose entry joins the storages of both (Revision 2's
    // pairs separate only a Subst's Origins).
    [InlineData("partLoan", "pa3Bad: inner@ref 1|pa3Bad: leaf.value = 5 1|pa3ConcreteBad: inner@ref 1|pa3ConcreteBad: leaf.value = 5 1|pa3ConcreteOk: inner@ref 1|pa3Ok: inner@ref 1|pa3WholeBad: inner@ref 1|pa3WholeBad: leaf.value = 5 1|pa5FirstBad: inner.0@ref 1|pa5FirstBad: leaf.value = 5 1|pa5Second: inner.1@ref 1|pa5Second: leaf.value = 5 1")]
    public void ACallKeepsTheArgumentByTheStoragesTypeWhateverItsPosition(string row, string expected)
        => Assert.Equal(WkExpected(expected), WkRecords(row));

    // M2: each part held by value counts (a Tuple's element C1, C2, an Option's payload C3, D4, a fixed array's and an Array's element C4,
    // C5, C6, a Dictionary's value C7, a Field E3); a storage whose Type holds neither the argument's Type nor a borrow formed through it
    // does not (E1's `ref/i32`).
    [Theory]
    [InlineData("C1", "tuplePutBad: v@follow = x 2")] // Fails on dev by design: tuplePutOk `v@follow = x` 2.
    [InlineData("C2", "tupleRefBad: v@follow = x 2")] // Fails on dev by design: tupleRefOk `v@follow = x` 2.
    [InlineData("C3", "optPutBad: v@follow = x 2")] // Fails on dev by design: optPutOk `v@follow = x` 2.
    [InlineData("C4", "arrPutBad: v@follow = x 2")] // Fails on dev by design: arrPutOk `v@follow = x` 2.
    [InlineData("C5", "arrElemPutBad: v@follow = x 2")] // Fails on dev by design: arrElemPutOk `v@follow = x` 2.
    [InlineData("C6", "arrAppendBad: v@follow = x 2")]
    [InlineData("byValuePart", "byValuePartBad: n = 5 1")] // R4's review B3: a by-value argument's part (SPEC 15.6.4 as revised).
    // Fails on fd4f56ae3: dictIndexBad and dictGetBad valid (a Dictionary's values were no part held by value, so `ref/V during self`
    // kept only Content).
    [InlineData("C7", "dictGetBad: d.clear() CallActivationConflict_Kd 1|dictIndexBad: d.clear() CallActivationConflict_Kd 1")]
    // r2.2 (design-r2 §9b): a raw layer stating the argument's Origin, as a position, reads as the argument's borrow (DictionaryRefRemainder's
    // `raw/u8 during source`: `ref/Dictionary<K, V>`, which holds the argument's own value), and a string holds its `u8` bytes, so
    // Utf8Slice's `Slice<u8> during source` is a borrow of a part reached through `text`. Fails on 295224f0e (since 0fd5e2f37): every Bad
    // form valid (the results kept only Content).
    [InlineData("viewHolds", "iterClearBad: d.clear() CallActivationConflict_Kd 1|iterUniqLengthBad: d.length 1|utf8MoveBad: text 1")]
    [InlineData("D4", "optEBad: v@follow = x 1")]
    [InlineData("E1", "concretePutBad: v@follow = x 1")] // Fails on dev by design: concretePutOk `v@follow = x` 1.
    [InlineData("E3", "recurNodeBad: v.value = 5 1")]
    public void EachPartHoldsTheArgumentByItsType(string row, string expected)
        => Assert.Equal(WkExpected(expected), WkRecords(row));

    // M4: with no callable handed to the callee, an abstract Type holds only what the body's premises make it: `T is Copy` excludes `uniq/T`
    // (F1), and an unconstrained T holds nothing of the argument (F2, the test author's derivation adopted by problem-final), so only the
    // local `slot` keeps the child.
    [Theory]
    [InlineData("F1", "uniqCopyBad: v@follow = x 1")] // Fails on dev by design: uniqCopyOk `v@follow = x` 1.
    [InlineData("F2", "uniqAnyLate: v@follow = x@move 1")] // Fails on dev by design: uniqAnyBad `v@follow = x@move` 1.
    // An abstract referent holds any part (the sound over-approximation of (β′) on the source side, DECISIONS R4-M154): a `ref/T` source
    // fits a `ref/i32` position, so rhUse errs though sneakRH stores nothing, and rnUse, whose requirement `n` lets it store a borrow of a
    // part of `t` into `*w`, must.
    [InlineData("abstractRef", "rhUse: t 1|rnUse: t 1")]
    // A raw argument's own value alike (PLAN G297, DECISIONS BU-66): no premise makes T one, so the builder's placeEntry keeps no handle in
    // a pair carrier's `T` (Dictionary literals; Array literals, which place their elements through no raw handle, beside them) and
    // neither does a user call's (putRaw*); a raw layer that can hold it does (putSlotBad, rule A). Fails on dev by design: dictPairOk,
    // dictTwoOk, dictMovedOk and dictRefUniqOk `[1: x…]` 3, dictUniqOk 1, putRawOk `b = 1` 3, putRawUniqOk, putRawDeadOk, putRawLiveOk 1.
    [InlineData("rawArg", "putSlotBad: b = 1 1")]
    public void AnAbstractTypeHoldsOnlyWhatItsPremisesMakeIt(string row, string expected)
        => Assert.Equal(WkExpected(expected), WkRecords(row));

    // (iii), R5: a borrow formed through the argument (of its referent or of a part reached through its exclusive layers, before a shared
    // dereference) counts as its own value where it fits, Origins fitted: `Leaf during o`'s peer (E2) and `R during o`'s (putR) hold one,
    // `Leaf during a`'s cannot (E2a: nothing proves `o` outlives `a`); x's referent `ref/i32 during q` holds no borrow formed through
    // `.Some(x@uniq)` (use1, use2; BU-49's own-Origin row superseded), a Tuple's `ref/i32 during q` beside an `i32` does (use3Bad).
    [Theory]
    [InlineData("E2", "recurLeafBad: v.value = 5 1|recurLeafOk: v.value = 5 1")]
    [InlineData("E2a", "recurLeafDistinctBad: v.value = 5 1")] // Fails on dev by design: recurLeafDistinctOk `v.value = 5` 1.
    [InlineData("putR", "putRUse: v.value = 5 1")]
    // Fails on fd4f56ae3: opBad valid (the second exclusive layer over `Box2 during a` formed no source; poBad, the Tuple swapped, erred).
    [InlineData("twoLayers", "opBad: a2@follow.inner@follow = 5 1|poBad: a2@follow.inner@follow = 5 1")]
    // Revision 10: a source's path keeps each layer the premise catalog's well-formedness does not relate to the layer above, so a raw
    // element's path no longer hides the uniq one to the same Type. Fails on ca38e24ad: rdBad valid (rdSwapBad, the Tuple swapped, erred).
    // RBox's raw Field states RBox's Origin, which the catalog relates, so it drops from the key (rdRelBad, rdRelSwapBad, r3RBad). r3Ok
    // (BU's r3Bad), as (iii) intends: the `during p` position can hold neither the argument's Type nor a borrow formed through its path,
    // which through the raw layer lies within r (SPEC 5.2.2), and nothing proves r outlives p. Fails on dev by design: r3Ok
    // `a2@follow = a3` 1.
    [InlineData("rawPath", "r3RBad: a2@follow = a3 1|rdBad: x@follow.1@follow@uniq 1|rdRelBad: x@follow.1@follow@uniq 1|rdRelSwapBad: x@follow.0@follow@uniq 1|rdSwapBad: x@follow.0@follow@uniq 1")]
    [InlineData("use", "use3Bad: x.1 1")] // Fails on dev by design: use2 `x@follow` 1 (BU-49's pin).
    public void TheBorrowsFormedThroughTheArgumentCountAsItsOwnValue(string row, string expected)
        => Assert.Equal(WkExpected(expected), WkRecords(row));

    // R7, SPEC 15.8.2: a Closure call retains by its environment's Types as a call by its parameters' (D6, G4; G3: a `var` binding holds
    // `y`, the `ref/i32` past it nothing formed through `y`); a capture's referent holding `y` keeps it (G1, G2). Their records at the
    // capture (`slot@follow`, `var slot = other@move` and the cell's) are R4-M148's self-store class, PLAN G148 (2), not compared here.
    [Theory]
    [InlineData("D6/G4", "closureEBad: v@follow = x 1", "")]
    [InlineData("G1", "a3ClosureBad: v@follow = x 2", "a3ClosureBad: slot@follow|a3ClosureBad: var slot = other@move|a3ClosureFirst: slot@follow|a3ClosureFirst: var slot = other@move")]
    [InlineData(
        "G2",
        "pairCellBad: v@follow = x 2",
        "pairCellBad: cell.item@follow|pairCellBad: var cell = Cell2c3.init(item: other@move)|pairCellFirst: cell.item@follow|pairCellFirst: var cell = Cell2c3.init(item: other@move)")]
    [InlineData("G3", "closureVarBad: v@follow = x 1", "")] // Fails on dev by design: closureVarOk `v@follow = x` 1.
    public void AClosureCallKeepsTheArgumentByItsEnvironmentsTypes(string row, string expected, string apart)
        => Assert.Equal(WkExpected(expected), WkRecords(row, apart));

    // R5 with R1: a callee or Closure that stores a shared borrow through its exclusive argument into storage the body does not own keeps
    // the argument's Loan as the borrow's parent Loan (H1-H3), as the same store without a call does (H4's directLeaf); storing what a read
    // through the argument carries does not (directLeafContent).
    [Theory]
    [InlineData("H1", "sneakIntUse: v@follow = 5 1")]
    [InlineData("H2", "sneakLeafUse: v.value = 5 1")]
    [InlineData("H3", "closureSneakUse: v.value = 5 1")]
    [InlineData("H4", "directLeaf: v.value = 5 1")]
    public void ABorrowStoredThroughTheArgumentKeepsItsParentLoan(string row, string expected)
        => Assert.Equal(WkExpected(expected), WkRecords(row));

    // M4, (β′) (PLAN G279): a callable the instantiated contract hands the callee's body lets an abstract Type's storage hold the argument
    // where SPEC 15.6.4 applied to the callable's own signature retains it there, by its result or by its writes: Contract requirements
    // building Self from a Contract Type argument (reqArg, resultG, reqStatic), a Type containing Self (reqContains), another Type
    // parameter's requirement returning T (reqOther), an associated Type (reqAssoc), Function values as inputs and in a Tuple, an Option and
    // a nominal Field (fnValue, resultF, fnInTuple, fnInOption, fnInStruct), a Callable bound over an aggregate fixed by `origin w.a == o`
    // (callableBound), and writers through a requirement or a Function value (writerReq, writerFn, each with the body's own call beside
    // it). A requirement whose borrow input's Origin is universal can hold nothing of it (reqBounded, SPEC 15.3.7). Each Ok twin ends the
    // abstract local's life before the write (WkEnded).
    [Theory]
    [InlineData("reqArg", "betaGBad: v@follow = 5 1")] // Fails on dev by design: betaGBad valid (G279's acceptance).
    [InlineData("resultG", "resultGBad: v@follow = 5 1")] // Fails on dev by design: resultGBad valid.
    [InlineData("reqStatic", "reqStaticBad: v@follow = 5 1")] // Fails on dev by design: reqStaticBad valid.
    [InlineData("reqContains", "reqContainsBad: v@follow = 5 1")] // Fails on dev by design: reqContainsBad valid.
    [InlineData("reqOther", "reqOtherBad: v@follow = 5 1")] // Fails on dev by design: reqOtherBad valid.
    [InlineData("reqAssoc", "reqAssocBad: v@follow = 5 1")] // Fails on dev by design: reqAssocOk `v@follow = 5` 1.
    [InlineData("fnValue", "betaFBad: v@follow = 5 1")] // Fails on dev by design: betaFOk `v@follow = 5` 1.
    [InlineData("resultF", "resultFBad: v@follow = 5 1")]
    [InlineData("fnInTuple", "fnInTupleBad: v@follow = 5 1")] // Fails on dev by design: fnInTupleOk `v@follow = 5` 1.
    [InlineData("fnInOption", "fnInOptionBad: v@follow = 5 1")] // Fails on dev by design: fnInOptionOk `v@follow = 5` 1.
    [InlineData("fnInStruct", "fnInStructBad: v@follow = 5 1")] // Fails on dev by design: fnInStructBad valid.
    [InlineData("reqBounded", "")]
    // A writer stores the child into storage T's value refers to, which the body does not own (SPEC 15.6), so its Ok order conflicts too.
    [InlineData("writerReq", "writerReqBad: v@follow = 5 1|writerReqDirectBad: v@follow = 5 1|writerReqOk: v@follow = 5 1")] // Fails on dev by design: all valid.
    [InlineData("writerFn", "writerFnBad: v@follow = 5 1|writerFnDirectBad: v@follow = 5 1|writerFnOk: v@follow = 5 1")]
    [InlineData("callableBound", "callableBad: v@follow = 5 1")] // Fails on dev by design: callableOk `v@follow = 5` 1.
    // Fails on fd4f56ae3: virtBad valid (a virtual callee on an abstract declaring Type read its base's contract, not as erased).
    [InlineData("virtual", "virtBad: v@follow = 5 1|virtConcreteBad: v@follow = 5 1")]
    // Revision 9: an erased signature's per-call Origins are its Binder's, the virtual's own as a requirement's (`x`, `y`, the receiver's):
    // only `x` reaches the result. Fails on c1389e455: virtPickOk errs at `n = 5` (a virtual's Origins read as fixed, R4's R2-B1).
    [InlineData("erasedOrigins", "virtPickBad: m = 5 1")]
    // A Property requirement's accessor is erased on an abstract Self as a requirement is, its per-call Origins bound by the accessor's
    // Binder (the Property requirement): setGetOk and setAddOk are valid, as their concrete twin is; setHeldBad's setter meets the live
    // `r`. Fails on 295224f0e (since 4b58be003): setGetOk and setAddOk err at `s` (the erased signature's Binder was the accessor's
    // FunctionKoto, so the setter's own Origins read as fixed).
    [InlineData("erasedSetter", "setHeldBad: s.item = 9 CallActivationConflict_Kd 1")]
    // A raw argument's own value (DECISIONS BU-66): the erased requirement `T.make` (the owner, 2026-10-30: 「(a) 今の文面に含まれると読み、
    // 今の動作のまま進める」) and fillRaw, whose premise hands it `make`, keep the pointer to `w` in `t`; fillBare, handed nothing, does not.
    // Fails on dev by design: fillBareLate `y = 5` 1. Rule 3 reads no Type (SPEC 15.6.4, 5.4: a raw cast keeps the address and
    // provenance; G297 fix 1): fill32 casts `p` to `make`'s `raw/u8`, fillP hands a pointer to a local holding it, useH casts it into a
    // handed callable value; each keeps the pointer to `w` in `t`. Fails on fd7058a42: fill32Bad, fillPBad and useHBad valid.
    [InlineData("rawReq", "fill32Bad: y = 5 1|fillPBad: y = 5 1|fillRawBad: y = 5 1|makeRawBad: y = 5 1|useHBad: y = 5 1")]
    // A raw layer stating an Origin retains a raw argument's own value whatever its pointee Type (SPEC 15.6.4: as the borrow its Origin
    // belongs to would; 5.4: a raw cast keeps the address; DECISIONS BU-66, R4's P2): cast8 and cast32 store `p: raw/i32` as a `raw/u8`
    // into `s.q`, as same8 and same32 store a `raw/u8`. Fails on dev and fd7058a42 (R4's probe): cast8Bad and cast32Bad valid.
    [InlineData("rawCast", "cast32Bad: y = 5 1|cast8Bad: y = 5 1|same32Bad: y = 5 1|same8Bad: y = 5 1")]
    public void ACallableTheContractHandsTheBodyLetsAnAbstractTypeHoldTheArgument(string row, string expected)
        => Assert.Equal(WkExpected(expected), WkRecords(row));

    // A raw-path row's forms (Revision 10): sneak{name} stores into `*w` a borrow formed through the Tuple `*x`'s uniq element beside
    // `other` (both over `Box2 during {origin}`), sneak{name}S with the Tuple swapped; rd{name}Bad and rd{name}SwapBad write through that
    // element after the call, rd{name}First before it.
    private static string WkRawForms(string name, string origin, string other)
    {
        var uniq = $"uniq/(Box2 during {origin}) during p";
        string Head(string first, string second)
            => $"(zr: ref/i32 during r, zp: ref/(Box2 during {origin}) during p, x: uniq/(({first}, {second})) during o, w: uniq/(uniq/i32 during o) during q) -> ()\n";
        string Through(int element) => $"let b = x@follow.{element}@follow@uniq\n";
        var call = "(zr, zp, x@follow@uniq, w@follow@uniq)\n";
        return WkForm($"sneak{name}", Head(other, uniq), Through(1) + "w@follow = b@follow.inner@follow@uniq") +
            WkForm($"sneak{name}S", Head(uniq, other), Through(0) + "w@follow = b@follow.inner@follow@uniq") +
            WkForm($"rd{name}Bad", Head(other, uniq), $"sneak{name}{call}" + Through(1) + "b@follow.inner@follow = 5") +
            WkForm($"rd{name}First", Head(other, uniq), Through(1) + $"b@follow.inner@follow = 5\nsneak{name}{call}") +
            WkForm($"rd{name}SwapBad", Head(uniq, other), $"sneak{name}S{call}" + Through(0) + "b@follow.inner@follow = 5");
    }

    // The erased-Origin rows' body: `pick` on the receiver `on` over m and n, its result kept in `r`.
    private static string WkPick(string on) => $"var m = 1\nvar n = 2\nlet r = {on}.pick(m@ref, n@ref)\n";

    // A part-Loan row's body (R4's review B1): `inner` holds a borrow of z, `pair` a Loan on a part of it (or the whole), passed to a
    // callee storing a borrow formed through `leaf` into it.
    private static string WkPartLoan(string inner, string pair, string callee)
        => $"var z = 0\nvar leaf = Lf.init(p: z@ref)\nvar inner = {inner}\nvar pair = {pair}\n{callee}(pair@uniq, leaf@uniq)";

    // Template P's head (problem r2 §2): a pair binding admitting owner and uniq, a Copy T and `var v = value@move`.
    private static string WkPair(string parameters = "value: s/T, other: s/T, x: T")
        => $"<s/T>({parameters}) -> ()\n    s is owner or uniq\n    T is Copy\n    var v = value@move\n";

    // A G297 row's head (DECISIONS BU-66): a pair binding admitting `cases` over a Copy T; and its body filling the local `t` through
    // `callee` from a raw pointer to `w`, which refers to `y`.
    private static string WkRawPair(string parameters, string cases) => $"<s/T>({parameters}) -> ()\n    s is {cases}\n    T is Copy\n";

    // A G297 cast row's holder (R4's P2): a struct whose `raw/u8 during a` Field `q` shares its Origin with a `ref/<anchor> during a` Field.
    private static string WkCastHolder(string name, string anchor)
        => $"struct {name} {{a}}\n    public var q: raw/u8 during a\n    public let r: ref/{anchor} during a\n    public init(r: ref/{anchor} during a)\n" +
            "        self.q = null\n        self.r = r\n";

    // A cast row's twins: `{name}` stores `p: {pointer} during k.a` as `{stored}` into `s.q`; the Ok form uses `s.q` before `y = 5`.
    private static string WkCastTwins(string name, string holder, string anchor, string pointer, string stored)
        => $"func {name}(s: uniq/{holder}{{k}}, p: {pointer} during k.a) => s.q = {stored}\n" +
            WkTwins(
                $"{name}Ok",
                $"{name}Bad",
                "() -> ()\n",
                $"var z: {anchor} = 0\nvar y: i32 = 1\nlet w = y@ref\nlet bw = w@ref\nvar s = {holder}.init(z@ref)\n{name}(s@uniq, bw@follow@raw@{pointer})",
                "usep(s.q)",
                "y = 5");

    private static string WkRawFill(string callee, string pointee = "u8", string more = "")
        => $"var t = x@move\nvar y: i32 = 1\nvar w: ref/i32 = y@ref\n{callee}(t@uniq, w@raw@raw/{pointee}{more})\n";

    // A function: its name, its head (what follows the name, Constraint lines included) and its body, one line per `\n`.
    private static string WkForm(string name, string head, string body = "")
        => $"func {name}{head}" + string.Concat(body.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(static x => $"    {x}\n"));

    // Problem r2 §2's twins: the Ok form ends with its use, then the write; the Bad twin swaps the two.
    private static string WkTwins(string ok, string bad, string head, string body, string use, string write)
        => WkForm(ok, head, $"{body}\n{use}\n{write}") + WkForm(bad, head, $"{body}\n{write}\n{use}");

    // A builder row's twins: `v` and `slot` moved from the parameters and `child`, an exclusive reborrow of `*v` during o, passed to the call.
    private static string WkBuilders(string name, string head, string call) => WkEnded($"{name}Ok", $"{name}Bad", head, WkMoved + call, "slot");

    // The twins over a local of an abstract, non-Copy Type: the Ok form uses the local, ends its life (`consume`) and then writes, since
    // destroying an abstract part not proven Copy at its scope's end observes what it holds (DECISIONS R4-M147); the Bad twin writes before
    // the use.
    private static string WkEnded(string ok, string bad, string head, string body, string local)
        => WkForm(ok, head, $"{body}\nlook({local}@ref)\nconsume({local}@move)\nv@follow = 5") + WkForm(bad, head, $"{body}\nv@follow = 5\nlook({local}@ref)");

    private static string[] WkExpected(string expected) => [.. expected.Split('|', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal)];

    // A row's records: every MIR problem of the program's own bodies but those at the subjects listed apart, in ordinal order. The program
    // parses with no error (MirBuildTest.Compile), binds with no issue and builds with no finding (Analyze).
    private static string[] WkRecords(string row, string apart = "")
    {
        var c = MirBuildTest.Compile(WkHelpers + WkForms[row]);
        var skipped = apart.Split('|', StringSplitOptions.RemoveEmptyEntries);
        return [.. Analyze(c).Problems.All.ToArray().Where(x => x.Subject.Module == 0)
            .Select(x => (Subject: $"{Function(c.SyntaxOf(x.Subject))}: {Line(c, x.Subject)}", x.Code, x.Views))
            .Where(x => Array.IndexOf(skipped, x.Subject) < 0)
            .Select(static x => x.Code == DiagnosticCode.ComparisonLoanConflict_Kd ? $"{x.Subject} {x.Views}" : $"{x.Subject} {x.Code} {x.Views}")
            .Order(StringComparer.Ordinal)];
    }
}
