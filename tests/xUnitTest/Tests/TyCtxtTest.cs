// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>The Type context (<c>Compilation.Types</c>, docs/dev/COMPILER_ARCHITECTURE.md, R2b): a substitution is interned, so a warm
/// lookup returns the same object and allocates nothing, and a call's, closure's or function item's substitution applied to a declared
/// Type is the instantiation Binding makes for it, over the Kimi library and every milestone program.</summary>
public sealed partial class TyCtxtTest
{
    // Generic functions whose requirement calls, in the closed calls below, run each witness kind: a conformance witness, an inherited one, one
    // through a conditional conformance of a Type with an Origin slot, one through a bound Contract reference (also written in a generic Type's
    // own parameter), an accessor-call and a standard property witness (get and set), a fixed array's entry, the integer Position and built-in
    // comparison and arithmetic (the latter through a bound reference).
    internal const string WitnessSource = """
        contract Shape
            func area(self: ref/Self) -> i32
        struct Square
            Self is Shape
            public var side: i32
            public init(side: i32) => self.side = side
            public func area(self: ref/Self) -> i32 => self.side * self.side
        public open struct Base
            public var side: i32 = 2
            public func area(self: ref/Self) -> i32 => self.side
        public struct Leaf : Base
            Self is Shape
            public init() => ()
        struct Held<T> {a}
            Self is Shape when T is Copy
            public var value: ref/T during a
            public init(value: ref/T during a) => self.value = value
            public func area(self: ref/Self) -> i32 => 4
        contract Counted
            property count: i32 has get, set
        struct Tally
            Self is Counted
            public var count: i32 = 1
            public init() => ()
        struct Gauge
            Self is Counted
            var stored: i32 = 2
            public computed count: i32
                get() -> i32 => self.stored
                set(value: i32) -> () => self.stored = value
            public init() => ()
        struct Plain
            Self is Addable<i32>
            associate Output is i32
            public init() => ()
            public func added(self: ref/Self, right: ref/i32) -> i32 => 6
        struct Wrap<V>
            V is PrimitiveInteger
            Self is Addable<V>
            associate Output is i32
            public init() => ()
            public func added(self: ref/Self, right: ref/V) -> i32 => 7
        func measure<T>(x: ref/T) -> i32
            T is Shape
            return x.area()
        func bump<T>(x: uniq/T) -> i32
            T is Counted
            x.count = x.count + 1
            return x.count
        func add<T>(left: ref/T, right: ref/i32) -> i32
            T is Addable<i32>
            T.(Addable<i32>).Output is i32
            return left.added(right)
        func same<T>(x: T) -> bool
            T is Equatable
            return x == x
        func count<C>(items: ref/C) -> i32
            C is Iterable
            var n = 0
            for item in items => n += 1
            return n
        func at<P>(p: P, length: isize) -> Option<isize>
            P is Position
            return p.tryResolve(length)
        let square = Square.init(3)
        let leaf = Leaf.init()
        let seven = 7
        let held = Held<i32>.init(seven@ref)
        var tally = Tally.init()
        var gauge = Gauge.init()
        let plain = Plain.init()
        let wrap = Wrap<i32>.init()
        let five = 5
        let fixed: [3 of i32] = [1, 2, 3]
        require measure(square) == 9 and measure(leaf) == 2 and measure(held) == 4 else => $abort("shape")
        require bump(tally@uniq) == 2 and bump(gauge@uniq) == 3 else => $abort("count")
        require add(plain, 2) == 6 and add(wrap, 2) == 7 and add(five, 2) == 7 else => $abort("add")
        require same(8) and count(fixed) == 3 else => $abort("built in")
        _ = at(1, 3)
        """;

    // A generic function whose closure an instance closes, called with two lengths.
    private const string BorrowsPrelude = "func inspect<T>(value: ref/T) => ()\npublic func main() -> () => ()\n";

    // Z6-F2's rows (AFitFromAnotherOriginMarksTheHoldersAtoms): the holders, t6's setup and tail around a row's holder and fit (h's layer
    // states the atom `s@follow@raw` states, w's projection: SharedAddress by the address, FittedIntoHolder by the fit and, r's address
    // being exclusive, FittedExclusive), d04's holder in parts, and t9's Cell.
    private const string FitsPrelude = "struct Wrap<E>\n    var value: E\n    public init(value: E) => self.value = value@move\n" +
        "    public func put<U>(self: uniq/Self, v: U) => ()\nstruct Box<E>\n    public var value: E\n    public init(value: E) => self.value = value@move\n" +
        "    public func put<U>(self: uniq/Self, v: U) => ()\n    public func set(self: uniq/Self, v: E) => self.value = v@move\n";

    private const string FitHead = "var x = 1|var y = 2|var z = 3|var w: ref/i32 = x@ref|var v: ref/i32 = y@ref|let s = w@ref|var r = v@uniq|";

    private const string FitTail = "|h.put(z@ref)|z = 5|inspect(v)";

    private const string FitS = "var w: ref/i32 = x@ref Projection 0: ref/i32 S SharedAddress, FittedIntoHolder, FittedExclusive";

    private const string FitStore = "func store(h: uniq/Wrap<raw/u8 during a>, q: raw/u8 during b, ka: ref/(ref/i32 during c) during a, kb: ref/i32 during b)|" +
        "    origin b outlives a|    h@follow = Wrap.init(q)|";

    // ce2's callee (`origin b outlives a`), ce2t's (`origin a outlives b`) and their caller, whose `static` arguments leave a's solution
    // kx's Origin.
    private const string FitClause = "func store2(ka: ref/i32 during a, kb: ref/i32 during b, h: uniq/(raw/u8 during a), q: raw/u8 during b)|" +
        "    origin b outlives a|    h@follow = q|";

    private const string FitClauseT = "func store5(ka: ref/i32 during a, kb: ref/i32 during b, h: uniq/(raw/u8 during a), q: raw/u8 during a)|" +
        "    origin a outlives b|    h@follow = q|";

    private const string FitClauseCaller = "func ce2(ks: ref/i32 during static, qs: raw/u8 during static)|    var x = 1|    let kx = x@ref|" +
        "    var hp = kx@follow@raw@raw/u8|    ";

    private const string FitHolderStruct = "struct HF {s}|    var p: ";

    private const string FitHolderBody = "|        self.p = q|        self.k = k|    public func put<U>(self: uniq/Self, v: U) => ()|";

    private const string FitHolderUse = "var x = 1|var y = 2|var z = 3|var v: ref/i32 = y@ref|var r = v@uniq|var h = HF.init(x@ref, r@follow@raw";

    private const string FitCell = "struct Cell {o}|    var p: raw/u8 during o|    var k: uniq/i32 during o|    public init(p: raw/u8 during o, k: uniq/i32 during o)|" +
        "        self.p = p|        self.k = k|    public func put<U>(self: uniq/Self, v: U) => ()|";

    private const string ClosureSource = "func identity<length N>(value: [N of i32]) -> [N of i32]\n    let action = func [] (inner: [N of i32]) -> [N of i32] => inner\n" +
        "    return action(value)\n_ = identity([1, 2])\n_ = identity([5])\n";

    // A generic function with a closure, called with two borrows of different Origins, one whose call binds an input Origin, and a
    // nongeneric function with Origins.
    private const string EraseSource = "func keep<T>(value: T) -> T\n    T is Copy\n    let probe = func [] (x: T) -> T => x\n    return probe(value)\n" +
        "func view<T>(value: ref/T) -> i32 => 1\nfunc host(a: ref/i32, b: ref/i32) -> i32\n    _ = keep(a)\n    _ = keep(b)\n    _ = view(a)\n    return 0\n" +
        "let x = 1\nlet y = 2\n_ = host(x@ref, y@ref)\n";

    // A raw Field and a Case payload stating `self`, and a raw Field stating a slot, held under a local's Origin and under `static`.
    private const string RawShapeSource = "struct Block\n    internal let data: raw/u8 during self\n    public init() => self.data = null\n" +
        "enum Cell\n    Some(raw/u8 during self)\n    None\n" +
        "struct View<T> {source}\n    internal let loan: Loan<ref/T during source>\n    internal let at: raw/T during source\n" +
        "    public init(target: ref/T during source)\n        self.at = target@follow@raw\n        self.loan = Loan.init(target)\n" +
        "func held(view: View<i32> during static) -> View<i32> during static => view@move\n" +
        "func local(x: ref/i32) -> i32\n    let v = View<i32>.init(x)\n    let b = Block.init()\n    let c = Cell.None\n    return 0\n";

    // Holders whose stored parts reach a raw layer a Type argument supplies below them: through a Field (Outer), an inline Tuple (Pair2), a
    // Case payload (Opt2), an Array element (Hold), an Own layer's pointee (OP), an Own layer's Type argument (Box1), the base (Der), an
    // Option, an enum with a stated and a supplied payload (E2), fixed arrays (FA), a Tuple (TP) and a pair slot (Holder).
    private const string KeptSource = "struct Wrap<E>\n    var value: E\nstruct Outer<P>\n    var w: Wrap<P>\nstruct Pair2<P>\n    var p: (Wrap<P>, i32)\n" +
        "enum Opt2<P>\n    Has(Wrap<P>)\n    Not\nstruct Hold<P>\n    var items: Array<Wrap<P>>\n" +
        "struct OP<P>\n    var data: raw/Wrap<P> during self\n    public init() => self.data = null\n" +
        "struct ErasedBag<T>\n    var data: raw/u8 during self\n    public init() => self.data = null\nstruct Box1<P>\n    var b: ErasedBag<Wrap<P>>\n" +
        "open struct BaseW<P>\n    var w: Wrap<P>\nstruct Der<P> : BaseW<P>\n    var n: i32\nstruct OuterE<P>\n    var e: Option<P>\n" +
        "enum E2<P>\n    A(raw/u8, P)\n    B\nstruct OuterE2<P>\n    var e: E2<P>\nstruct FA<P>\n    var cells: [2 of P]\n    var stated: [2 of raw/u8]\n" +
        "struct OuterF<P>\n    var f: FA<P>\nstruct TP<P>\n    var t: (i32, P)\nstruct OuterT<P>\n    var t: TP<P>\n" +
        "struct Holder<s/T>\n    internal let value: s/T\nstruct OuterH<P>\n    var h: Holder<P>\n" +
        "func probe(x: uniq/i32, o: Outer<raw/u8 during x>, t: Pair2<raw/u8 during x>, e: Opt2<raw/u8 during x>, h: Hold<raw/u8 during x>, " +
        "op: OP<raw/u8 during x>, bb: Box1<raw/u8 during x>, d: Der<raw/u8 during x>, plain: Outer<raw/u8>, oe: OuterE<raw/u8 during x>, " +
        "oe2: OuterE2<raw/u8 during x>, of: OuterF<raw/u8 during x>, ot: OuterT<raw/u8 during x>, oh: OuterH<raw/u8 during x>) => ()\n";

    // Holders whose declarations write an Origin-less raw layer at some depth below them (DECISIONS BU-46): in a Field, a Tuple, a Type
    // argument of a Field (Holder2, HoldOpt, HoldEnum, HoldTW, HFA, HOW, Hold2), a base clause (DerS, D2, D3 through B3's), beside a
    // supplied argument (M), in a nested holder (H3), a pair slot's argument (HPSo, HPSs, HPSt) and its application `s/U` (HPU, HPUs), a
    // Case payload's Tuple (EP) and a `during self` pointee (SB, HB2); then the twins whose layers a Type argument supplies.
    private const string DeclaredSource = "struct Wrap<E>\n    var value: E\nstruct Outer<P>\n    var w: Wrap<P>\nstruct RawBuf\n    var data: raw/u8\n" +
        "struct HoldTuple\n    var t: (raw/u8, i32)\nstruct Holder2\n    var b: Wrap<raw/u8>\nstruct HoldOpt\n    var o: Option<raw/u8>\n" +
        "open struct B1<P>\n    var v: P\nopen struct B2<Q> : B1<Q>\n    var m: i32\nopen struct B3<Q> : B1<Wrap<raw/u8>>\n    var q: Q\n" +
        "struct DerS : B1<raw/u8>\n    var n: i32\nstruct D2 : B2<raw/u8>\n    var n: i32\nstruct D3 : B3<i32>\n    var n: i32\n" +
        "struct DerX<P> : B1<Wrap<P>>\n    var n: i32\nstruct Two<A, B>\n    var a: A\n    var b: B\nstruct M<P>\n    var w: Two<raw/u8, P>\n" +
        "struct M2<P>\n    var w: Two<P, P>\nenum MyOpt<T>\n    Has(T)\n    Not\nstruct HoldEnum\n    var e: MyOpt<raw/u8>\n" +
        "struct HoldTW\n    var t: Wrap<(raw/u8, i32)>\nstruct H3\n    var h: Holder2\nstruct PS<s/T>\n    var t: s/T\nstruct HPSo\n    var p: PS<raw/u8>\n" +
        "struct HPSs {s}\n    var loan: Loan<uniq/i32 during s>\n    var p: PS<raw/u8 during s>\nstruct HPSt\n    var p: PS<raw/u8 during static>\n" +
        "struct PU<s/T>\n    s is raw or owner\n    var v: s/i32\nstruct HPU\n    var p: PU<raw/u8>\n" +
        "struct HPUs {s}\n    var loan: Loan<uniq/i32 during s>\n    var p: PU<raw/u8 during s>\nstruct HFA\n    var f: Wrap<[2 of raw/u8]>\n" +
        "enum EP\n    A((Wrap<raw/u8>, i32))\n    B\nstruct HOW\n    var o: Option<Wrap<raw/u8>>\nstruct Hold2\n    var items: Array<raw/u8>\n" +
        "struct SB\n    var data: raw/(raw/u8) during self\nstruct Box2<T>\n    var p: raw/T during self\nstruct HB2\n    var b: Box2<raw/u8>\n" +
        "struct Hold<P>\n    var items: Array<Wrap<P>>\nenum Opt2<P>\n    Has(Wrap<P>)\n    Not\n" +
        "func probe(yu: uniq/i32, rawBuf: RawBuf, holdTuple: HoldTuple, holder2: Holder2, holdOpt: HoldOpt, derS: DerS, d2: D2, d3: D3, " +
        "b3: B3<i32>, m: M<raw/u8>, holdEnum: HoldEnum, holdTW: HoldTW, h3: H3, oh: Outer<Holder2>, hpso: HPSo, hpss: HPSs during yu, " +
        "hpst: HPSt, hpu: HPU, hpus: HPUs during yu, hfa: HFA, ep: EP, how: HOW, hold2: Hold2, sb: SB, hb2: HB2, outer: Outer<raw/u8>, " +
        "wrap: Wrap<raw/u8>, m2: M2<raw/u8>, ps: PS<raw/u8>, pu: PU<raw/u8>, hold: Hold<raw/u8>, opt2: Opt2<raw/u8>, derX: DerX<raw/u8>, " +
        "arr: Array<raw/u8>, box2: Box2<raw/u8>, ow: Outer<Wrap<raw/u8>>) => ()\n";

    // Defaults that call a generic function, a length-generic one, one with an Origin, a requirement and, in a generic struct's method, a
    // function over the struct's Type parameter.
    private const string ComposeSource = "func copy<T>(x: T) -> T\n    T is Copy\n    return x\nfunc f<T>(x: T, y: T = copy(x)) -> T\n    T is Copy\n    return y\n" +
        "func count<length N>(values: ref/[N of i32]) -> isize => values.length\nfunc size<length N>(values: ref/[N of i32], k: isize = count(values)) -> isize => k\n" +
        "func first<T>(values: ref/[2 of T] during a) -> ref/T during a => values[0]@ref/T\n" +
        "func pick<T>(values: ref/[2 of T] during s, r: ref/T during s = first(values)) -> ref/T during s => r\n" +
        "func same<T>(x: T, equal: bool = x == x) -> bool\n    T is Equatable\n    return equal\n" +
        "struct Box<T>\n    T is Copy\n    public var value: T\n    public init(value: T) => self.value = value\n    public func get(self: ref/Self) -> T => self.value\n" +
        "    public func orElse(self: ref/Self, other: T = self.get()) -> T => other\n" +
        "let fixed: [3 of i32] = [1, 2, 3]\nlet two: [2 of i32] = [4, 5]\nlet box = Box<i32>.init(6)\nlet p = pick(two@ref)\n" +
        "require f(7) == 7 and f(true) and size(fixed@ref) == 3 and p@follow == 4 and same(8) and same(true) and box.orElse() == 6 else => $abort(\"compose\")\n";

    // An Origin-generic function; a generic function with a closure and its specialization with one; a length-generic function with a
    // default; named functions nested one and two levels in a generic function and one in a block of it; one nested in a nongeneric function;
    // a generic struct whose method nests a function, with an accessor and a generic method whose specialization has a closure and a nested
    // function (the F3 reproducer spec2.kimi); a nongeneric struct with an accessor; a group's static.
    private const string InstanceSource = "func plain(value: ref/i32 during a) -> ref/i32 during a => value\n" +
        "func pass<T>(value: T) -> T\n    T is Copy\n    let keep = func [] (inner: T) -> T => inner\n    return keep(value)\n" +
        "specialize func pass<i32>(value: i32) -> i32\n    let keep = func [] (inner: i32) -> i32 => inner\n    return keep(value)\n" +
        "func size<length N>(values: ref/[N of i32], k: isize = values.length) -> isize => k\n" +
        "func outer<T>(x: T) -> T\n    T is Copy\n    func middle(y: T) -> T\n        func inner(z: T) -> T => z\n        return inner(y)\n" +
        "    if true\n        func scoped() -> i32 => 1\n        _ = scoped()\n    return middle(x)\n" +
        "func host() -> i32\n    func helper() -> i32 => 1\n    return helper()\n" +
        "struct Box<T>\n    T is Copy\n    public var value: T\n    public computed current: T\n        get() -> T => self.value\n" +
        "    public init(value: T) => self.value = value\n    public func get(self: ref/Self) -> T\n        func local() -> i32 => 1\n" +
        "        _ = local()\n        return self.value\n" +
        "    public func f<U>(self: ref/Self, x: U) -> T => self.value\n    specialize func f<i32>(self: ref/Self, x: i32) -> T\n" +
        "        let g = func [] (y: T) -> T => y\n        func nested() -> i32 => 1\n        _ = nested()\n        return g(self.value)\n" +
        "struct Fixed\n    public var value: i32\n    public computed current: i32\n        get() -> i32 => self.value\n    public init(value: i32) => self.value = value\n" +
        "group Limits\n    public let limit: i32 = seed()\n    public func seed() -> i32 => 3\n";

    // Nested definitions in a generic struct's Field initializer (a Closure and a function in it), getter (a function, a Closure and a
    // function in it) and setter, in its method, and a Closure in a static's initializer.
    private const string EnclosingSource = "struct Box<T>\n    T is Copy\n    public var value: T\n" +
        "    public var count: isize = (func [] () -> isize\n        func size() -> isize => 2\n        return size()\n    )()\n" +
        "    public init(value: T) => self.value = value\n" +
        "    public computed got: T\n        get() -> T\n            func inner(z: T) -> T => z\n            let keep = func [] (y: T) -> T\n" +
        "                func deep(w: T) -> T => w\n                return deep(y)\n            return keep(inner(self.value))\n" +
        "        set(value: T) -> ()\n            func store(z: T) -> T => z\n            self.value = store(value)\n" +
        "    public func get(self: ref/Self) -> T\n        func local(z: T) -> T => z\n        return local(self.value)\n" +
        "group Limits\n    public let limit: i32 = (func [] () -> i32 => 3)()\n" +
        "func outer<T>(x: T) -> T\n    T is Copy\n    var box = Box<T>.init(x)\n    box.got = x\n    return box.got\n" +
        "require outer(3) == 3 and Box<i32>.init(4).get() == 4 and Limits.limit == 3 else => $abort(\"enclosing\")\n";

    // C → B → A with a default and a nonvirtual member: C's base calls run B's override, B's runs the original.
    private const string BaseCallHierarchy = VirtualDispatchTest.Hierarchy +
        "let c = C.init(3)@obj\nrequire c.value() == 34 and c.direct() == 14 else => $abort(\"base\")\n";

    // P → Q → R whose slot takes and returns a borrow: R's base call runs Q's override, Q's runs the original.
    private const string BorrowingHierarchy = "open struct P\n    public init() => ()\n" +
        "    public virtual func pick(self: objref/Self, values: ref/[2 of i32] during s) -> ref/i32 during s => values[0]@ref\n" +
        "open struct Q : P\n    public init() => ()\n" +
        "    override func pick(self: objref/Self, values: ref/[2 of i32] during s) -> ref/i32 during s => base.pick(values)\n" +
        "struct R : Q\n    public init() => ()\n" +
        "    override func pick(self: objref/Self, values: ref/[2 of i32] during s) -> ref/i32 during s => base.pick(values)\n" +
        "let r = R.init()@obj\nlet two: [2 of i32] = [4, 5]\nrequire r.pick(two@ref)@follow == 4 else => $abort(\"pick\")\n";

    // X and Y, a ring through rc handles, X beside a Field whose Type did not bind, and Z, which stores Y inline.
    private const string RejectedRing = "struct X\n    public var t: Option<rc/Y>\n    public var f: MISSING\nstruct Y\n    public var x: Option<rc/X>\n" +
        "struct Z\n    public var y: Y\n";

    // Slots only conditional pair slots use (SPEC 15.3.5), each stated by a raw Field: H's (`ref or uniq`), M's beside a `ref` Field and
    // G's beside a Loan Field (`owner or uniq`), an inherited binder's (Outer<s/T>.Inner), N's through a nested Holder bound to its pair
    // argument, and W's of two binders (`ref or uniq`, `owner or ref`).
    private const string ConditionalSource = "struct H<s/T> {a}\n    s is ref or uniq\n    internal let item: s/T during a\n    internal var at: raw/i32 during a\n" +
        "struct M<s/T> {a}\n    s is owner or uniq\n    internal let item: s/T during a\n    internal let keep: ref/i32 during a\n    internal var at: raw/i32 during a\n" +
        "struct G<s/T> {a}\n    s is owner or uniq\n    internal let loan: Loan<ref/bool during a>\n    internal let item: s/T during a\n    internal var at: raw/i32 during a\n" +
        "struct Outer<s/T>\n    s is ref or uniq\n    internal struct Inner {a}\n        internal let x: s/T during a\n        internal var at: raw/i32 during a\n" +
        "struct N<q/U> {b}\n    q is ref or uniq\n    internal let h: H<q/U during b> during b\n    internal var at: raw/i32 during b\n" +
        "struct W<s/T, t/U> {a}\n    s is ref or uniq\n    t is owner or ref\n    internal let x: s/T during a\n    internal let y: t/U during a\n    internal var at: raw/i32 during a\n" +
        "func probe(r: uniq/i32, q: ref/i32, z: uniq/i64, hu: H<uniq/i32 during r> during r, hr: H<ref/i32 during q> during q, mo: M<i32> during r, " +
        "mu: M<uniq/i64 during z> during r, gu: G<uniq/i32 during r> during r, go: G<i32> during r, iu: Outer<uniq/i32 during r>.Inner during r, " +
        "ir: Outer<ref/i32 during q>.Inner during q, nu: N<uniq/i32 during r> during r, nr: N<ref/i32 during q> during q, " +
        "wu: W<uniq/i32 during r, i32> during r, wr: W<ref/i32 during q, ref/i64 during q> during q) => ()\npublic func main() => ()\n";

    // The parameter of ConditionalSource's probe and the part of its raw Field, in its order.
    private static readonly (int At, int Part)[] ConditionalRows = [(3, 1), (4, 1), (5, 2), (6, 2), (7, 2), (8, 2), (9, 1), (10, 1), (11, 1), (12, 1), (13, 2), (14, 2)];

    // ABodyAtomListsTheBorrowsFormedWithIt's rows (binder's first line, atom kind and slot: referent and mode): originpub design R2.9's
    // expectation, from SPEC before the run. The address `values@raw` adds none: it "checks like an immediately ending @ref ... and
    // leaves no Loan" (SPEC 5.4), no borrow value whose Origin a Type could state. m, reached with shared access, is ref/i32 (SPEC 15.1.6)
    // under P[o], the Subject Place's atom, formed where Check decides the binding's acquisition (G147 ruling 19).
    private static readonly string[] ExpectedRow1 =
    [
        "(*p)@uniq Anchor 0: i32 X",
        "let n = match o@ref Projection 0: i32 S",
        "let s = w@ref Inference -1:  Claimed",
        "let view = values[0..2] Projection 0: Slice<i32> S",
        "var b = (0, x@ref) Projection 0: i32 X",
        "var h = 4 Projection 0: i32 S",
        "var o: Option<i32> = .Some(3) Projection 0: Option<i32> S, i32 S",
        "var values: [2 of i32] = [1, 2] Projection 0: i32 S",
        "var w: ref/i32 = x@ref Projection 0: ref/i32 S",
        "var w: ref/i32 = x@ref Projection 0: ref/i32 X",
        "var x = 1 Projection 0: i32 S",
        "var x = 1 Projection 0: i32 S",
    ];

    // SPEC 8.1.3, 8.15.1: the declared parameter and result Types of the function a call's substitution instantiates are the call's
    // instantiation (SPEC 8.4.1, 8.6.4: a requirement's under its conforming Self and the bound Contract reference it dispatches through, which
    // SubstOf records as its declaring Type; SPEC 6.4.4: a direct base call's function is the implementation it runs, in the Type
    // implementing it), a closure in such a Type has its instance's signature, and a function item's substitution applies as its context
    // does.
    [Theory]
    [MemberData(nameof(HirColumnsTest.Corpus), MemberType = typeof(HirColumnsTest))]
    public void ApplyIsTheInstantiation(string name)
    {
        var (c, module) = SemanticSnapshotTest.Bind(name);
        CheckInstantiations(c, module.RootKoto);
    }

    // SPEC 6.4.4: in A → B → C, C's base calls (from its override and, with a default, from a nonvirtual member) run B's override and
    // B's base call runs the original; each call is its implementation's instantiation and keeps the slot's contract, borrowed inputs
    // and results with their Origins included.
    [Theory]
    [InlineData(BaseCallHierarchy, 3, 2)]
    [InlineData(BorrowingHierarchy, 2, 1)]
    public void ADirectBaseCallIsItsImplementationsInstantiation(string source, int direct, int intoOverrides)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var calls = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Select(static x => x.CallOf())
            .Where(static x => x is { Kind: CalleeKind.Virtual, VirtualIsDirect: true }).ToArray();
        Assert.Equal(direct, calls.Length);
        Assert.Equal(intoOverrides, calls.Count(static x => !ReferenceEquals(x!.VirtualImplementation, x.Target.Declaration)));
        CheckInstantiations(c, c.Kotonoha.RootKoto);
    }

    // PLAN G949 S1-b2 (DECISIONS BU-220): the statics hold the ids 1..StaticCount from one fixed list, the same objects in every
    // compilation; a compilation's TyCtxt numbers every Type it makes after them, the interned ones and the parameter Types (a generic
    // parameter's whole Type and target projection, a Contract's Self), each id once and with no gap, in the order it makes them, so two
    // compilations of one program number alike. From PLAN G949 Stage 2 U3 (u13) the compilation reads the frozen library, whose layer
    // holds the ids up to its L, so the compilation's own ids number on from L + 1 with no gap.
    [Fact]
    public void TypeIdsAreDenseAndUnique()
    {
        var statics = new[] { BoundType.Unit, BoundType.Never, BoundType.Boolean, BoundType.Range }.Concat(BoundType.Primitives.Values)
            .Concat(BoundType.WrappingScalars.Values).Distinct().Select(static x => x.Id).Order();
        Assert.Equal(Enumerable.Range(1, BoundType.StaticCount), statics);
        var first = Made(CompilationTestHelper.BindSuccess("()"));
        var second = Made(CompilationTestHelper.BindSuccess("()"));
        Assert.Equal(Enumerable.Range(BoundType.StaticCount + 1, first.Length), first);
        Assert.Equal(first, second);

        // The ids of every Type the compilation's TyCtxt made (its interned ones, each declared symbol's own Types), as many as its count.
        static int[] Made(Compilation c)
        {
            const System.Reflection.BindingFlags Private = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var interned = ((Dictionary<int, List<BoundType>>)typeof(TyCtxt).GetField("types", Private)!.GetValue(c.Types)!).Values.Concat(c.Library.Layer.Types.Values).SelectMany(static x => x);
            var own = new List<BoundType?>();
            foreach (var symbol in c.Binding.Semantics.Declarations)
            {
                own.AddRange([symbol.WholeType, symbol.Type, symbol.SelfSymbol?.Type]);
            }

            int[] ids = [.. interned.Concat(own.OfType<BoundType>()).Where(static x => x.Id > BoundType.StaticCount).Distinct().Select(static x => x.Id).Order()];
            Assert.Equal((int)typeof(TyCtxt).GetField("typeCount", Private)!.GetValue(c.Types)! - BoundType.StaticCount, ids.Length);
            return ids;
        }
    }

    // PLAN G949: the interner makes one Type per distinct content, whatever the hashes of its parts, and TyCtxt.TypeCount counts each Type
    // it makes. A part's hash is its identity hash, which the thread's earlier objects move: two Tuples over two Types whose identity
    // hashes are equal share one key of the interned Types' table and are still two Types (a count of the keys would count them as one).
    [Fact]
    public void TypesWhosePartsShareAnIdentityHashAreCountedApart()
    {
        var c = CompilationTestHelper.BindSuccess("()");
        var symbol = c.Binding.Library.DynamicArray;
        var made = new Dictionary<int, BoundType>();
        BoundType? first = null;
        BoundType? second = null;
        while (second is null && made.Count < 1_000_000)
        {
            // An identity hash has 26 bits: two of about 10,000 fresh Types share one at even odds, two of 1,000,000 at certainty.
            var type = c.Types.Parameter("T", BoundTypeKind.Parameter, symbol);
            if (!made.TryAdd(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(type), type))
            {
                (first, second) = (made[System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(type)], type);
            }
        }

        Assert.NotNull(second);
        var before = c.Types.TypeCount;
        Assert.NotSame(c.Types.Tuple([first!]), c.Types.Tuple([second]));
        Assert.Equal(before + 2, c.Types.TypeCount);
    }

    // SPEC 6.1.3, 8.1.3: a closure an instance closed is in that instance's substitution and has its signature; a closure no instance closed is
    // in the identity of the function enclosing it, which keeps every Type.
    [Fact]
    public void AClosureIsInItsInstancesSubstitution()
    {
        var c = MinimalEmissionTest.Analyze(ClosureSource);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var nodes = KotoTree.Walk(c.Kotonoha.RootKoto).ToArray();
        var function = nodes.OfType<FunctionKoto>().Single(static x => x.Name == "identity");
        var closure = nodes.OfType<FunctionKoto>().Single(static x => x.IsAnonymous).ClosureOf()!;
        var calls = nodes.OfType<InvocationKoto>().Where(static x => x.CallOf()?.Target.Name == "identity").Select(static x => x.CallOf()!).ToArray();
        var closed = calls.Select(x => c.Types.ReadableInstance(closure.EnvironmentType!, c.Types.StorageSubst(x))!).ToArray();
        Assert.All(closed, static x => Assert.NotNull(x.InstanceContext));
        Assert.Equal(calls.Select(c.Types.SubstOf), closed.Select(c.Types.SubstOfClosure));
        Assert.NotSame(c.Types.SubstOf(calls[0]), c.Types.SubstOf(calls[1]));
        Assert.All(closed, x => Assert.Same(c.Binding.ClosureSignature(x), c.Types.Apply(closure.Signature!, c.Types.SubstOfClosure(x)!).Type));
        var identity = c.Types.SubstOfClosure(closure.EnvironmentType!)!;
        Assert.Same(c.Types.Identity(function), identity);
        Assert.Same(closure.Signature, c.Types.Apply(closure.Signature!, identity).Type);
    }

    // SPEC 15.3.1, 15.3.6 (PLAN G457): a call's instance maps each of the callee's Type and Origin slots once, so a Type argument naming the
    // callee's own Origin in its own body keeps the caller's (it became the callee's image `j` before).
    [Fact]
    public void ACallsInstanceMapsEachSlotOnce()
    {
        var c = MinimalEmissionTest.Analyze("func f<T>(t: T, y: ref/i32 during k, z: ref/i32 during j) -> (T, ref/i32 during k)\n    let p: (ref/i32 during k, ref/i32 during j) = f<ref/i32 during k>(y, z, z)\n    return (t@move, y)\npublic func main() => ()\n");
        var nodes = KotoTree.Walk(c.Kotonoha.RootKoto).ToArray();
        var declared = nodes.OfType<VariableKoto>().Single(static x => x.NameKoto.IdentifierName == "p").TypeKoto!.TypeOf();
        var call = nodes.OfType<InvocationKoto>().Single().CallOf()!;
        Assert.Same(declared, c.Types.ReadableInstance(call.Target.Type!, c.Types.StorageSubst(call)));
    }

    // SPEC 6.1.3, 8.1.3, PLAN G142: a closure's context is the whole substitution of the instance that closed it, its conforming Self included, so two
    // substitutions that differ only in Self close it twice. No source call gives a body with a closure a Self (a Contract declares no default
    // implementation, SPEC 8.6.1), so the test synthesizes the two calls.
    [Fact]
    public void AClosureContextKeepsTheConformingSelf()
    {
        var c = MinimalEmissionTest.Analyze(ClosureSource);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var nodes = KotoTree.Walk(c.Kotonoha.RootKoto).ToArray();
        var closure = nodes.OfType<FunctionKoto>().Single(static x => x.IsAnonymous).ClosureOf()!;
        var call = nodes.OfType<InvocationKoto>().Select(static x => x.CallOf()).First(static x => x?.Target.Name == "identity")!;
        var substs = new[] { BoundType.Boolean, BoundType.I32 }.Select(self => c.Types.SubstOf(Conforming(c.Binding, call, self))!).ToArray();
        var closed = substs.Select(x => c.Types.Apply(closure.EnvironmentType!, x).Type!).ToArray();
        Assert.Equal(substs, closed.Select(c.Types.SubstOfClosure));
        Assert.NotSame(closed[0], closed[1]);

        static CallPlan Conforming(Binding binding, CallPlan call, BoundType self)
        {
            var synthesized = new CallPlan();
            synthesized.Set(
                binding,
                call.Target,
                call.ReturnType,
                null,
                [],
                call.TypeArguments,
                conformingType: self,
                declaringType: call.DeclaringType,
                origins: call.Origins,
                inputOrigins: call.InputOrigins,
                lengthArguments: call.LengthArguments);
            return synthesized;
        }
    }

    // SPEC 4.4: a substitution holds one length entry per slot whichever producer forms it, so a direct call, a function item and a
    // synthesized call of one instance are one substitution.
    [Fact]
    public void ASubstitutionHasOneLengthEntryPerSlot()
    {
        var c = MinimalEmissionTest.Analyze("func make<T>() -> i32 => 1\nlet f = make<i32>\nrequire make<i32>() == f() else => $abort(\"make\")\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var nodes = KotoTree.Walk(c.Kotonoha.RootKoto).ToArray();
        var call = nodes.OfType<InvocationKoto>().Select(static x => x.CallOf()).Single(static x => x is { Kind: CalleeKind.Function, Target.Name: "make" })!;
        var item = nodes.Select(static x => x.TypeOf()).First(static x => x is { Kind: BoundTypeKind.FunctionItem })!;
        var subst = c.Types.SubstOf(call)!;
        Assert.Equal(1, subst.Lengths.Length);
        Assert.Same(subst, c.Types.SubstOfItem(item));
        Assert.Same(subst, c.Types.Synthesized(call.Target, null, call.TypeArguments, []));
    }

    // SPEC 3.4.3, 13.5.5: selection follows a reference or object handle as a Deref and a pair layer, the original s/T or an application
    // s/U, as a PairLayer of the binder in scope, never a raw pointer or an owned value. SPEC 23.1: a Type is named as diagnostics spell it.
    [Fact]
    public void LayersAreReferencesHandlesAndPairLayers()
    {
        var c = MinimalEmissionTest.Analyze("func f<s/T>(value: s/T, count: s/i32) -> s/T\n    s is owner or uniq\n    T is Copy\n    _ = count\n    return value\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var types = c.Types;
        var function = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "f");
        var pair = function.Parameters[0].Type.TypeOf()!;
        var application = function.Parameters[1].Type.TypeOf()!;
        var binders = new List<PairBinder>();
        PairBinder.Collect(c.Binding, function, binders);
        Assert.Equal(HirPlaceKind.Deref, TyCtxt.Layer(types.Reference(SemanticsKind.Uniq, BoundType.I32), out var below, out var binder));
        Assert.Same(BoundType.I32, below);
        Assert.Null(binder);
        Assert.Equal(HirPlaceKind.Deref, TyCtxt.Layer(types.Reference(SemanticsKind.ObjRef, BoundType.I32), out _, out _));
        Assert.Equal(HirPlaceKind.Value, TyCtxt.Layer(types.Pointer(BoundType.I32), out below, out binder));
        Assert.Equal((types.Pointer(BoundType.I32), null), (below, binder));
        Assert.Equal(HirPlaceKind.Value, TyCtxt.Layer(types.Tuple([BoundType.I32, BoundType.Boolean]), out _, out _));
        Assert.Equal(HirPlaceKind.PairLayer, TyCtxt.Layer(pair, out below, out binder));
        Assert.Same(Assert.Single(binders).Target, binder);
        Assert.Equal(HirPlaceKind.Value, TyCtxt.Layer(below, out _, out binder));
        Assert.Null(binder);
        Assert.Equal((BoundTypeKind.SemanticsApplication, HirPlaceKind.PairLayer), (application.Kind, TyCtxt.Layer(application, out below, out binder)));
        Assert.Equal((BoundType.I32, binders[0].Target), (below, binder));
        Assert.Equal(["uniq/i32", "s/T", "s/i32"], [TyCtxt.Name(types.Reference(SemanticsKind.Uniq, BoundType.I32)), TyCtxt.Name(pair), TyCtxt.Name(application)]);
    }

    // SPEC 5.2.1 condition 3 (the owner's 「Loan 要求で上限 (推奨)」): a raw layer a Field declares with a slot whose Loan requirement is
    // `ref` is Shared under any instance, its image a `uniq` input's Origin and a member taking an exclusive borrow of the slot included
    // (R4's cap and reqcap); `during static` is Static, and a `uniq` slot's layer the image's kind.
    [Fact]
    public void AStoredLayerOfARefSlotIsShared()
    {
        var c = MinimalEmissionTest.Analyze("struct G\n    internal let g: raw/u8 during static\n    public init() => self.g = null\n" +
            "struct H {s}\n    internal let loan: Loan<ref/i32 during s>\n    internal var at: raw/i32 during s\n" +
            "    public func m(self: uniq/Self, x: uniq/i32 during s)\n        self.at = x@follow@raw\n        unsafe => *self.at = 1\n" +
            "struct K {s}\n    internal let loan: Loan<uniq/i32 during s>\n    internal var at: raw/i32 during s\n" +
            "func probe(y: uniq/i32, g: G, h: H during y, k: K during y) => ()\npublic func main() => ()\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var p = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "probe").Parameters.Select(static x => x.Type.TypeOf()!).ToArray();
        Assert.Equal([HoldStep.Static, HoldStep.Shared, HoldStep.Exclusive], [c.Types.StoredStep(p[1], 0, []), c.Types.StoredStep(p[2], 1, []), c.Types.StoredStep(p[3], 1, [])]);
    }

    // SPEC 15.3.5, 5.2.1 condition 3, 5.7 Origins (DECISIONS BU-44): a raw layer stating a slot only conditional pair slots use steps by
    // the case its holder selects, Shared only where the slot is `ref` in every such case, through an enclosing declaration's binder
    // (Outer<…>.Inner), a nested pair argument (N's Holder) and two binders (W) too; its members are the slot's keepers in those cases,
    // a pair layer's referent only in its binder's borrow cases (M's i64, G's i32) beside a Field that keeps it in every case, each
    // exclusive where the slot is `uniq` in some case.
    [Fact]
    public void AConditionalSlotsLayerStepsAndKeepsByItsHoldersCase()
    {
        var (types, p) = ConditionalLayers();
        var rows = new List<string>();
        foreach (var (at, part) in ConditionalRows)
        {
            var members = new List<RawMember>();
            Assert.True(MembersAt(types, p[at], part, [], members));
            rows.Add($"{types.StoredStep(p[at], part, [])} [{string.Join(", ", members.Select(static x => x.Referent.Name + (x.Exclusive ? " excl" : string.Empty)))}]");
        }

        Assert.Equal(
            ["Exclusive [i32 excl]", "Shared [i32]", "Shared [i32]", "Exclusive [i64 excl, i32 excl]", "Exclusive [bool excl, i32 excl]", "Shared [bool]",
                "Exclusive [i32 excl]", "Shared [i32]", "Exclusive [i32 excl, i32 excl]", "Shared [i32, i32]", "Exclusive [i32 excl]", "Shared [i32, i64]"],
            rows);
    }

    // SPEC 15.3.5, 5.2.1 condition 3, 6.1.3 (DECISIONS BU-44): a nested declaration's raw Field stating its enclosing declaration's slot
    // steps by the case the holder's inherited argument selects, which follows the holder's own arguments: Outer's `a` is `ref` in the ref
    // case and `uniq` in the uniq one, so the Field is Shared under `Outer<ref/…>` and Exclusive under `Outer<uniq/…>`, whatever Inner's
    // own argument is, and so for a nested declaration without own generics.
    [Fact]
    public void AnInheritedSlotsLayerStepsByItsHoldersCase()
    {
        var c = MinimalEmissionTest.Analyze("struct Outer<s/T> {a}\n    s is ref or uniq\n    internal let item: s/T during a\n" +
            "    internal struct Inner<U>\n        internal let u: U\n        internal var p: raw/i32 during a\n    internal struct Plain\n        internal var p: raw/i32 during a\n" +
            "func probe(r: uniq/i32, q: ref/i32, z: ref/i64, iu: Outer<uniq/i32 during r>.Inner<i64>, ir: Outer<ref/i32 during q>.Inner<i64>, " +
            "iuz: Outer<uniq/i32 during r>.Inner<ref/i64 during z>, irz: Outer<ref/i32 during q>.Inner<uniq/i64 during r>, " +
            "pu: Outer<uniq/i32 during r>.Plain, pr: Outer<ref/i32 during q>.Plain) => ()\npublic func main() => ()\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var p = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "probe").Parameters.Select(static x => x.Type.TypeOf()!).ToArray();
        Assert.Equal(
            [HoldStep.Exclusive, HoldStep.Shared, HoldStep.Exclusive, HoldStep.Shared, HoldStep.Exclusive, HoldStep.Shared],
            [c.Types.StoredStep(p[3], 1, []), c.Types.StoredStep(p[4], 1, []), c.Types.StoredStep(p[5], 1, []), c.Types.StoredStep(p[6], 1, []), c.Types.StoredStep(p[7], 0, []), c.Types.StoredStep(p[8], 0, [])]);
    }

    // DECISIONS BU-44: a binder after 64 pair slots keeps its cases (its ordinal has no width limit): a nested declaration's raw Field
    // stating its enclosing slot steps by the holder's inherited 65th argument, after its own, and a raw Field of a wide Holder steps and
    // keeps by that Holder's 65th argument.
    [Fact]
    public void ABinderAfterSixtyFourPairSlotsKeepsItsCases()
    {
        var (c, p) = WideLayers();
        var types = c.Types;
        var rows = new List<string>();
        foreach (var (at, part) in new[] { (2, 1), (3, 1), (4, 1), (5, 1) })
        {
            var members = new List<RawMember>();
            Assert.True(MembersAt(types, p[at], part, [], members));
            rows.Add($"{types.StoredStep(p[at], part, [])} [{string.Join(", ", members.Select(static x => x.Referent.Name + (x.Exclusive ? " excl" : string.Empty)))}]");
        }

        Assert.Equal(["Exclusive []", "Shared []", "Exclusive [i32 excl]", "Shared [i32]"], rows);
    }

    // DECISIONS BU-44: a warm rebind over binders beyond the 64th, and reading their steps and members again, allocates nothing: the
    // fixed point reuses each slot's words and a Binding's one array per such ordinal.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmRebindOfWideBindersAllocatesNothing()
    {
        var (c, p) = WideLayers();
        var types = c.Types;
        var members = new List<RawMember>(8);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Binding.Bind(BindingMode.Final);
            members.Clear();
            if (types.StoredStep(p[2], 1, []) != HoldStep.Exclusive || types.StoredStep(p[5], 1, []) != HoldStep.Shared || !MembersAt(types, p[4], 1, [], members))
            {
                throw new InvalidOperationException("Wide raw steps changed.");
            }
        }));
    }

    // DECISIONS BU-44: reading those steps and members again, the conditional keepers by their holders' cases, allocates nothing (A5 reads
    // them per instance and pass).
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmConditionalSlotReadAllocatesNothing()
    {
        var (types, p) = ConditionalLayers();
        var members = new List<RawMember>(16);
        var steps = 0;
        foreach (var (at, part) in ConditionalRows)
        {
            steps += (int)types.StoredStep(p[at], part, []);
            MembersAt(types, p[at], part, [], members);
        }

        var count = members.Count;
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            members.Clear();
            var again = 0;
            foreach (var (at, part) in ConditionalRows)
            {
                again += (int)types.StoredStep(p[at], part, []);
                MembersAt(types, p[at], part, [], members);
            }

            if (again != steps || members.Count != count)
            {
                throw new InvalidOperationException("Conditional raw steps changed.");
            }
        }));
    }

    // SPEC 5.7 Origins, Calls (G160-4's (3a)): the borrows a stored raw layer's Origin belongs to are the keepers of the slot the instance
    // binds it to, under the instance (View's `ref` Loan of i32, K's `uniq` one); `static` has none.
    [Fact]
    public void RawMembersAreTheBorrowsAnOriginBelongsTo()
    {
        var c = MinimalEmissionTest.Analyze("struct G\n    internal let g: raw/u8 during static\n    public init() => self.g = null\n" +
            "struct H {s}\n    internal let loan: Loan<ref/i32 during s>\n    internal var at: raw/i32 during s\n" +
            "struct K {s}\n    internal let loan: Loan<uniq/i32 during s>\n    internal var at: raw/i32 during s\n" +
            "func probe(y: uniq/i32, g: G, h: H during y, k: K during y) => ()\n" +
            "public func main() => ()\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var p = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "probe").Parameters.Select(static x => x.Type.TypeOf()!).ToArray();
        var members = new List<RawMember>();
        Assert.True(MembersAt(c.Types, p[1], 0, [], members));
        Assert.Empty(members);
        Assert.True(MembersAt(c.Types, p[2], 1, [], members) && MembersAt(c.Types, p[3], 1, [], members));
        Assert.Equal([new(0, BoundType.I32, false), new(0, BoundType.I32, true)], members);
    }

    // PLAN G949 S1-r1b (review-s1r1b C1: v9 through the reader MIR's holding walk uses): a library slot's keepers reach a program's raw
    // layer through TyCtxt.RawMembers. X's `at` belongs to the borrows X's slot `s` is kept by: `it`, a BorrowingIterator<Counter> during
    // s, keeps it through the library slot `source`, which the library pass keeps by its `iterator: uniq/I during source` Field, so the
    // member is Counter's exclusive Loan, after a first and a second Bind. A library slot's list is current by its part's requirement
    // call, the library pass's; read against the program pass's call instead, it answers no keepers and the member is lost. Labelled from
    // SPEC first (5.7 Origins, 15.3.5; Counter is an Iterator, so a LendingIterator, as BorrowingIterator requires): valid.
    [Fact]
    public void ALibrarySlotsKeepersAreARawLayersMembersAfterEachBind()
    {
        var c = MinimalEmissionTest.Analyze("struct Counter\n    Self is Iterator\n    associate Iterator.Item is i32\n    public init() => ()\n" +
            "    public func next(self: uniq/Self) -> Option<i32> => .None\nstruct X {s}\n    internal let at: raw/u8 during s\n" +
            "    internal let it: Kimi.Iteration.BorrowingIterator<Counter> during s\nfunc probe(y: uniq/Counter, x: X during y) => ()\n" +
            "public func main() => ()\n");
        for (var bind = 0; bind < 2; bind++)
        {
            Assert.True((bind == 0 ? c.Binding.Result : c.Bind()).IsComplete, MinimalEmissionTest.Describe(c));
            var holder = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "probe").Parameters[1].Type.TypeOf()!;
            var members = new List<RawMember>();
            Assert.True(MembersAt(c.Types, holder, 0, [], members));
            Assert.Equal(["0 Counter True"], members.Select(static x => $"{x.Input} {TyCtxt.Name(x.Referent)} {x.Exclusive}"));
        }
    }

    // SPEC 5.7 (the final review's R3; DECISIONS BU-24, U2): a keeper reached on many paths is walked once per slot, owner Type, input and
    // mode, so a ladder of n diamonds (D_i keeps its slot through D_(i-1) and through E_(i-1), which keeps it through D_(i-1)) gives D0's
    // one keeper once as a member of the raw layer X stores, not once per path (2^n), and the walk is linear in n.
    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    public void AKeeperReachedOnManyPathsIsWalkedOnce(int n)
    {
        var source = "struct D0 {s}\n    internal let r: ref/i32 during s\n";
        for (var i = 1; i <= n; i++)
        {
            source += $"struct E{i - 1} {{s}}\n    internal let x: D{i - 1} during s\nstruct D{i} {{s}}\n    internal let a: D{i - 1} during s\n    internal let b: E{i - 1} during s\n";
        }

        var c = MinimalEmissionTest.Analyze(source + $"struct X {{s}}\n    internal let at: raw/u8 during s\n    internal let d: D{n} during s\n" +
            "func f(y: uniq/i32, x: X during y) => ()\npublic func main() => ()\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var p = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "f").Parameters.Select(static x => x.Type.TypeOf()!).ToArray();
        var members = new List<RawMember>();
        Assert.True(MembersAt(c.Types, p[1], 0, [], members));
        Assert.Equal([new(0, BoundType.I32, false)], members);
    }

    // SPEC 5.7 (DECISIONS BU-24): a formed raw Origin is the plain Origin it is formed with, also through a shared borrow: a closure
    // parameter's projection, an address through a local's shared borrow, a call's result whose Origin is that local's region.
    [Fact]
    public void AFormedRawOriginIsThePlainOrigin()
    {
        var c = MinimalEmissionTest.Analyze(RawPointerSyntaxTest.KeptView +
            "func id(x: ref/i32, p: raw/i32 during x) -> raw/i32 during x => p\n" +
            "func f()\n    let g = func (l: ref/View, p: raw/i32 during l.source) => ()\n    let n = 1\n    let r = n@ref\n    let a = r@follow@raw\n" +
            "    let b = id(r, a)\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var nodes = KotoTree.Walk(c.Kotonoha.RootKoto).ToArray();
        var closure = Assert.Single(nodes.OfType<FunctionKoto>(), static x => x.IsAnonymous);
        var r = nodes.OfType<FieldKoto>().Single(static x => x.NameKoto.IdentifierName == "r").SymbolOf()!.Type!.Origin;
        BoundType[] layers =
        [
            closure.Parameters[1].Type.TypeOf()!,
            nodes.OfType<VariableKoto>().Single(static x => x.NameKoto.IdentifierName == "a").SymbolOf()!.Type!,
            nodes.OfType<VariableKoto>().Single(static x => x.NameKoto.IdentifierName == "b").SymbolOf()!.Type!,
        ];
        Assert.Equal(OriginKind.Inference, r!.Kind); // r's slot is its region; a read in its own body takes the value (PLAN G492-a (ii)).
        var value = c.Binding.RegionValueAtUse(r!, r!.Binder!);
        Assert.Contains(layers[0].RawOrigin!.Kind, new[] { OriginKind.Inference, OriginKind.Projection });
        Assert.Same(value, layers[1].RawOrigin);

        // A call's result is a producer at the call (the owner's O-385 (a)): its argument's Origin is read there, r's value, as the raw
        // view's is; stored as r's region it differs where b is named in a nested body (PLAN G492-a (ii) phase 4: brow2).
        Assert.Same(value, layers[2].RawOrigin);
    }

    // SPEC 5.7 (design-final §15; DECISIONS BU-24, BU-41): a raw layer an instance stores has the step of its declared Field Type or Case
    // payload under the instance's substitution, the one reader A5 has for it: `self` the holder's own, a `ref` slot Shared (also under
    // `static`), a `uniq` slot Exclusive, an inline Tuple's or fixed array's layer by its positions; the declaration states each with an
    // Origin, so none states Unstated (RawStated); AdtDef's stored Types state none.
    [Fact]
    public void AStoredRawLayerHasItsDeclaredStep()
    {
        var (types, p) = StoredLayers();
        (BoundType Instance, int Part, int[] Positions)[] layers = [(p[3], 0, []), (p[4], 0, [0]), (p[5], 1, []), (p[6], 1, []), (p[7], 0, []), (p[8], 0, [0]), (p[8], 1, [1])];
        Assert.Equal(
            [HoldStep.Own, HoldStep.Own, HoldStep.Shared, HoldStep.Shared, HoldStep.Exclusive, HoldStep.Own, HoldStep.Own],
            layers.Select(x => types.StoredStep(x.Instance, x.Part, x.Positions)));
        Assert.All(layers, x => Assert.False(TyCtxt.RawStated(KeptLayer(types, x.Instance, x.Part, x.Positions))));
        Assert.All([AdtDef.FieldType(types, p[3], 0)!, AdtDef.CaseTypes(types, p[4])![0]!.Components[0], AdtDef.FieldType(types, p[5], 1)!], static x => Assert.Null(x.RawOrigin));
    }

    // SPEC 5.7, 5.2.1 condition 3 (DECISIONS BU-24; R4's constraint 2): a raw layer a Type argument supplies, where the declaration states a
    // Type parameter or a pair layer (a Field, a Case payload, a Tuple or array element, or above the layer in a Tuple argument), is no
    // declared layer: it has the kind of the Origin the argument states (none Unstated, `static` Static, any other Exclusive), never the
    // stored cap, and a pair's raw case states none (SPEC 8.1.2), nor does a raw argument a pair slot binds (SPEC 8.1.1, DECISIONS BU-34:
    // `Holder<raw/i32 during y>` is `Holder<raw/i32>`). Its Origin's borrows are not the instance's: an Exclusive one's members are read
    // where its Origin is bound (PLAN G148 (1), R7 plan Z6: tb's y, probe's `y: uniq/i32` and the keeper `loan: Loan<uniq/E during source>`
    // of r's R, each an exclusive i32; FAIL-FIRST: false, no member). No declaration states such a layer (RawStated). A path whose substituted part is no raw layer stays a broken contract, as
    // does a `during self` layer a Type argument supplies (DECISIONS BU-58: `self` binds Holder only on a layer its declaration writes).
    [Fact]
    public void ARawLayerATypeArgumentSuppliesStepsByItsOwnOrigin()
    {
        var (types, p) = StoredLayers();
        (BoundType Instance, int Part, int[] Positions)[] layers =
        [
            (p[9], 0, []), (p[10], 0, []), (p[11], 0, []), (p[12], 0, []), (p[13], 0, []), (p[14], 1, []), (p[15], 0, [0]), (p[15], 1, [0]),
            (p[16], 0, [0]), (p[17], 0, [0]), (p[18], 0, [0]), (p[19], 0, [0]), (p[21], 0, []),
        ];
        Assert.Equal(
            [HoldStep.Address, HoldStep.Exclusive, HoldStep.Static, HoldStep.Address, HoldStep.Address, HoldStep.Address, HoldStep.Address,
                HoldStep.Address, HoldStep.Address, HoldStep.Address, HoldStep.Exclusive, HoldStep.Exclusive, HoldStep.Address],
            layers.Select(x => types.StoredStep(x.Instance, x.Part, x.Positions)));
        Assert.All(layers, x => Assert.False(TyCtxt.RawStated(KeptLayer(types, x.Instance, x.Part, x.Positions))));
        var members = new List<RawMember>();
        Assert.Equal((true, true, true), (MembersAt(types, p[10], 0, [], members), MembersAt(types, p[9], 0, [], members), MembersAt(types, p[21], 0, [], members)));
        Assert.Equal(["i32 X", "i32 X"], members.Select(static x => $"{x.Referent} {(x.Exclusive ? "X" : "S")}"));
        Assert.Same(p[13], p[21]);
        Assert.Contains("InternalInvariant", Assert.Throws<InvalidOperationException>(() => types.StoredStep(p[20], 0, [])).Message, StringComparison.Ordinal);
        var supplied = types.Intern(BoundTypeKind.Constructed, p[9].Symbol, SemanticsKind.Owner, [types.Pointer(BoundType.I32, BoundOrigin.Holder)]);
        Assert.Contains("InternalInvariant", Assert.Throws<InvalidOperationException>(() => types.StoredStep(supplied, 0, [])).Message, StringComparison.Ordinal);
    }

    // DECISIONS BU-58: WrittenAt is the Type a part's declaration writes at inline positions while it is a Tuple, a fixed array or a raw
    // layer: a struct Field's root, a Case payload's component at its first position, a Tuple's component and a fixed array's element
    // below them, a raw-free written Tuple included; null at or past a scalar, a Type parameter, a pair layer or an open projection, and
    // at an enum's [], so null at p is null below p.
    [Fact]
    public void WrittenAtIsTheTypeADeclarationWritesInline()
    {
        var (_, p) = StoredLayers();
        (BoundType Instance, int Part, int[] Positions)[] rows =
        [
            (p[3], 0, []), (p[4], 0, [0]), (p[7], 0, []), (p[8], 0, []), (p[8], 0, [0]), (p[8], 1, []), (p[8], 1, [1]), (p[16], 0, []),
            (p[17], 0, []), (p[8], 0, [1]), (p[4], 0, []), (p[16], 0, [0]), (p[17], 0, [1]), (p[9], 0, []), (p[18], 0, [0]), (p[13], 0, []),
            (p[15], 0, [0]), (p[15], 1, [0]),
        ];
        Assert.Equal(
            [
                "raw/u8 during Holder", "raw/u8 during Holder", "raw/E during Parameter", "(raw/u8, isize)", "raw/u8 during Holder", "[2 of raw/u8]",
                "raw/u8 during Holder", "(T, i32)", "[2 of T]", "-", "-", "-", "-", "-", "-", "-", "-", "-",
            ],
            rows.Select(static x => Shown(TyCtxt.WrittenAt(x.Instance, x.Part, x.Positions))));
        var c = MirBuildTest.Compile("contract HasElem\n    associate Element\nstruct Src\n    Self is HasElem\n    associate Element is i32\nstruct W<S>\n" +
            "    S is HasElem\n    internal var e: S.Element\nfunc probe(w: W<Src>) => ()\npublic func main() => ()\n");
        var w = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "probe").Parameters[0].Type.TypeOf()!;
        Assert.Equal((BoundType.I32, null), (c.Types.StoredParts(w, out _)[0], TyCtxt.WrittenAt(w, 0, [])));
    }

    // DECISIONS BU-58 (G307's Q1): one kept part at two positions whose declarations write different Types, P's Fields x and y and E's and
    // E3's Cases A and B sharing one kept payload Tuple, steps by what each writes there (a slot's ref cap, Shared, against a supplied
    // layer's own Origin, Exclusive, or `static`), so WrittenAt tells the positions apart. Where WrittenAt is null, the layer steps by its
    // kept Origin's kind alone and its members are read where its Origin is bound (PLAN G148 (1), R7 plan Z6; SPEC 5.7 Origins: probe's
    // `y: uniq/i32`, an exclusive i32, and the keepers of the slots y binds, E's `C(Loan<ref/i32 during s>)` and P's `loan`, shared i32s;
    // FAIL-FIRST: false, no member).
    [Fact]
    public void WrittenAtTellsApartThePositionsOfOneKeptPart()
    {
        var c = MirBuildTest.Compile(
            "enum E<T> {s}\n    A(raw/i32 during s, i32)\n    B(T, i32)\n    C(Loan<ref/i32 during s>)\nstruct P<T> {s}\n    internal let loan: Loan<ref/i32 during s>\n" +
            "    internal var x: (raw/i32 during s, i32)\n    internal var y: T\nenum E3 {s}\n    A(raw/(ref/i32 during s) during s, i32)\n" +
            "    B(raw/(ref/i32 during static) during static, i32)\n    C(ref/i32 during s)\n" +
            "func probe(y: uniq/i32, e: E<raw/i32 during y> during y, p: P<(raw/i32 during y, i32)> during y, e3: E3 during static) => ()\npublic func main() => ()\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var types = c.Types;
        var q = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "probe").Parameters.Select(static x => x.Type.TypeOf()!).ToArray();
        (BoundType Instance, int Part, int Other)[] twins = [(q[1], 0, 1), (q[2], 1, 2), (q[3], 0, 1)];
        Assert.All(twins, x => Assert.Same(types.StoredParts(x.Instance, out _)[x.Part], types.StoredParts(x.Instance, out _)[x.Other]));
        (BoundType Instance, int Part, int[] Positions)[] rows = [(q[1], 0, [0]), (q[1], 1, [0]), (q[2], 1, [0]), (q[2], 2, [0]), (q[3], 0, [0]), (q[3], 1, [0])];
        var members = new List<RawMember>();
        Assert.Equal(
            ["raw/i32 during Parameter Shared True 1", "- Exclusive True 3", "raw/i32 during Parameter Shared True 1", "- Exclusive True 3",
                "raw/ref/i32 during Parameter Shared True 0", "raw/ref/i32 during Static Static True 0"],
            rows.Select(x => $"{Shown(TyCtxt.WrittenAt(x.Instance, x.Part, x.Positions))} {types.StoredStep(x.Instance, x.Part, x.Positions)} " +
                $"{MembersAt(types, x.Instance, x.Part, x.Positions, Cleared(members))} {members.Count}"));
        Assert.True(MembersAt(types, q[1], 1, [0], Cleared(members)));
        Assert.Equal(["i32 X", "i32 S", "i32 S"], members.Select(static x => $"{x.Referent} {(x.Exclusive ? "X" : "S")}"));
        Assert.Equal(
            [Binding.StepOf(KeptLayer(types, q[1], 1, [0]).RawOrigin), Binding.StepOf(KeptLayer(types, q[2], 2, [0]).RawOrigin)],
            [types.StoredStep(q[1], 1, [0]), types.StoredStep(q[2], 2, [0])]);
        (BoundType Instance, int Part)[] roots = [(q[2], 1), (q[2], 2), (q[1], 0)];
        Assert.Equal(["(raw/i32, i32)", "-", "-"], roots.Select(static x => Shown(TyCtxt.WrittenAt(x.Instance, x.Part, []))));

        static List<RawMember> Cleared(List<RawMember> list)
        {
            list.Clear();
            return list;
        }
    }

    // DECISIONS BU-58: a closed projection's resolution is the Type the declaration writes, so WrittenAt follows the written DAG of
    // TypeDagScalingTest's projection chain down to its leaf, the kept part's own nodes where the leaf states `static` (as written).
    [Fact]
    public void WrittenAtFollowsAWrittenProjectionDag()
    {
        var c = MirBuildTest.Compile(TypeDagScalingTest.ProjectionChain("raw/i32 during static", 8));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var holder = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "pv").Parameters[1].Type.TypeOf()!;
        var kept = c.Types.StoredParts(holder, out _)[1]!;
        var path = new int[8];
        for (var k = 0; k <= 8; k++)
        {
            Assert.Same(kept, TyCtxt.WrittenAt(holder, 1, path.AsSpan(0, k)));
            kept = k < 8 ? kept.Components[0] : kept;
        }

        Assert.Equal(HoldStep.Static, c.Types.StoredStep(holder, 1, path));
    }

    // SPEC 5.7, 5.2.1 condition 3 (DECISIONS BU-41): an instance's stored parts (StoredParts) are its substitution of the declared Fields,
    // Case payload Tuples and base with the raw Origins its Type arguments give, AdtDef's being these without them (the same Types by
    // reference, null where AdtDef's are, also where Binding rejected a Field or a Case): below the holder, a layer a Type argument
    // supplies keeps its Origin at a Field, an inline Tuple, a Case payload, an Array element, an Own layer's pointee and Type argument
    // and the base; a holder whose argument states no Origin stores AdtDef's parts. A layer of a rejected Case's payload is no part
    // Binding accepts (DECISIONS BU-37), so reading it is a broken contract, also where the layer's own payload substitutes.
    [Fact]
    public void AnInstancesStoredPartsKeepItsRawOrigins()
    {
        var (c, p) = KeptParts();
        foreach (var holder in p[1..8])
        {
            Assert.Contains(Kept(c, holder), static x => x?.CarriesRawOrigin == true);
            Assert.DoesNotContain(Stored(c.Types, holder), static x => x?.CarriesRawOrigin == true);
        }

        var plain = Kept(c, p[8]);
        Assert.Same(AdtDef.FieldType(c.Types, p[8], 0), plain[0]);
        var failed = MirBuildTest.Compile("struct Bad<P>\n    public var v: P\n    public var m: Missing\nenum E<P>\n    A(Missing, P)\n    B(P)\n" +
            "func f(x: uniq/i32, b: Bad<raw/u8 during x>, e: E<raw/u8 during x>) => ()\n");
        Assert.False(failed.Binding.Result.IsComplete);
        var q = KotoTree.Walk(failed.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "f").Parameters.Select(static x => x.Type.TypeOf()!).ToArray();
        Assert.Equal([false, true, true, true, false], Kept(failed, q[1]).Concat(Kept(failed, q[2])).Select(static x => x is null));
        Assert.True(Kept(failed, q[1])[0]!.CarriesRawOrigin && Kept(failed, q[2])[1]!.CarriesRawOrigin);
        Assert.Contains("InternalInvariant", Assert.Throws<InvalidOperationException>(() => failed.Types.StoredStep(q[2], 0, [1])).Message, StringComparison.Ordinal);

        // A struct's kept Fields and kept base, or an enum's kept Case payload Tuples, each AdtDef's WithoutRawOrigins.
        static BoundType?[] Kept(Compilation c, BoundType holder)
        {
            var parts = c.Types.StoredParts(holder, out var parent).ToArray();
            BoundType?[] kept = AdtDef.IsStruct(holder) ? [.. parts, parent] : parts;
            var erased = Stored(c.Types, holder).ToArray();
            Assert.Equal(erased.Length, kept.Length);
            for (var i = 0; i < kept.Length; i++)
            {
                Assert.Same(erased[i], c.Binding.WithoutRawOrigins(kept[i]));
            }

            return kept;
        }
    }

    // SPEC 5.7 Calls, 15.6.4 (DECISIONS BU-41): a layer a Type argument supplies below the holder is read on the instance the holder's kept
    // part reaches, which its first read substitutes: Outer<raw/u8 during x>'s `w` is Wrap<raw/u8 during x>, whose `value` is supplied
    // and steps Exclusive, its members x's borrow by probe's declaration (SPEC 5.7 Origins, Authority; PLAN G148 (1), R7 plan Z6: the
    // referent i32, exclusive for `x: uniq/i32`; FAIL-FIRST: false, no member), where AdtDef's Wrap<raw/u8> reads Unstated, and Opt2's payload reaches the same
    // instance. Each layer, also an Option's payload, an enum Case with a stated and a supplied payload, fixed array elements, a Tuple
    // component and a pair slot's layer, which states no Origin (DECISIONS BU-34, so the holder stores AdtDef's part), is stated by no
    // declaration (RawStated) but E2's and FA's own `raw/u8` (DECISIONS BU-46); a Case without its payload position, a component with no
    // raw layer or one out of range is a broken contract.
    [Fact]
    public void AKeptNestedInstanceReadsItsSuppliedLayers()
    {
        var (c, p) = KeptParts();
        var types = c.Types;
        var members = new List<RawMember>();
        var wrap = Kept(1);
        Assert.Equal(
            (HoldStep.Exclusive, false, true),
            (types.StoredStep(wrap, 0, []), TyCtxt.RawStated(KeptLayer(types, wrap, 0, [])), MembersAt(types, wrap, 0, [], members)));
        Assert.Equal(["i32 X"], members.Select(static x => $"{x.Referent} {(x.Exclusive ? "X" : "S")}"));
        members.Clear();
        Assert.Equal(HoldStep.Address, types.StoredStep(AdtDef.FieldType(c.Types, p[1], 0)!, 0, []));
        Assert.Same(wrap, types.StoredParts(p[3], out _)[0]!.Components[0]);
        var option = Kept(9);
        var some = Array.FindIndex(option.Symbol!.Adt!.Cases, static x => x is { Payload.Length: 1 });
        (BoundType Instance, int Part, int[] Positions)[] layers =
            [(option, some, [0]), (Kept(10), 0, [0]), (Kept(10), 0, [1]), (Kept(11), 0, [1]), (Kept(11), 1, [0]), (Kept(12), 0, [1]), (Kept(13), 0, [])];
        Assert.Equal(
            ["Exclusive False True", "Address True True", "Exclusive False True", "Exclusive False True", "Address True True",
                "Exclusive False True", "Address False True"],
            layers.Select(x => $"{types.StoredStep(x.Instance, x.Part, x.Positions)} {TyCtxt.RawStated(KeptLayer(types, x.Instance, x.Part, x.Positions))} " +
                $"{MembersAt(types, x.Instance, x.Part, x.Positions, members)}"));
        Assert.Equal(["i32 X", "i32 X", "i32 X", "i32 X"], members.Select(static x => $"{x.Referent} {(x.Exclusive ? "X" : "S")}"));
        Assert.Same(AdtDef.FieldType(c.Types, p[13], 0), Kept(13));
        (BoundType Instance, int Part, int[] Positions)[] broken = [(option, some, []), (Kept(12), 0, [0]), (Kept(12), 0, [5])];
        Assert.All(broken, x => Assert.Contains(
            "InternalInvariant", Assert.Throws<InvalidOperationException>(() => types.StoredStep(x.Instance, x.Part, x.Positions)).Message, StringComparison.Ordinal));

        // The first kept part of the probe's parameter i.
        BoundType Kept(int i) => types.StoredParts(p[i], out _)[0]!;
    }

    // SPEC 5.7 Origins ("the outer Origin of a borrow value ... in a body, a local's, to that borrow"), 15.6.5, 5.7 Fits and claims
    // (PLAN G148 (1), the Origin-atom borrows): each Projection or Anchor atom lists the Borrows formed with it as their outer Origin,
    // each Borrow of a local its own atom (SPEC 15.6.5: w's shared and exclusive borrows, S and X; x's two shared borrows), a part's
    // borrow under its root's atom (b.0), a Slice view, a Subject and its by-reference binding, a capture's slot borrow, a raw Place's
    // fresh anchor and the implicit argument borrows; a claim marks the Origin its Type states (s's region). Labels: originpub
    // labels-r1.txt row1 (V); G922 tms labels.md #5.
    [Fact]
    public void ABodyAtomListsTheBorrowsFormedWithIt()
    {
        var c = MinimalEmissionTest.Analyze(BorrowsPrelude + "func row1()\n    var x = 1\n    var w: ref/i32 = x@ref\n    let s = w@ref\n" +
            "    var q: raw/u8 during s = null\n    unsafe => q = w@raw@(raw/u8 during s)\n    inspect(s)\n    var r = w@uniq\n    inspect(r)\n" +
            "    var b = (0, x@ref)\n    var t = b.0@uniq\n    inspect(t)\n    var values: [2 of i32] = [1, 2]\n    let view = values[0..2]\n" +
            "    inspect(view)\n    var o: Option<i32> = .Some(3)\n    let n = match o@ref\n        .Some(let m) => m\n        _ => 0\n" +
            "    var h = 4\n    let c = func [h@ref] () => ()\n    c()\n    let p = values@raw@raw/i32\n    unsafe\n        let e = (*p)@uniq\n" +
            "        inspect(e)\n    inspect(n)\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Equal(ExpectedRow1, BorrowRows(c));
    }

    // SPEC 10.1 (PLAN G148 (1)): a rejected candidate's argument borrow is no Borrow of the program. f(x: ref/i64) and f(x: i32) do not
    // overlap (7.4.3), so the rejected candidate adapts k before its referent fails; k is copied, so P[k] has no row. Labels:
    // originpub labels-r1.txt row2c (V).
    [Fact]
    public void ACandidateTrialFormsNoBorrow()
    {
        var c = MinimalEmissionTest.Analyze(BorrowsPrelude + "func f(x: ref/i64) => ()\nfunc f(x: i32) => ()\nfunc row2()\n    var k = 1\n    f(k)\n    k = 2\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.DoesNotContain(BorrowRows(c), static x => x.StartsWith("var k", StringComparison.Ordinal));
    }

    // SPEC 5.2.1 condition 3, 5.4, 5.7 Typed paths (G147 ruling 66, Z6's R6.1): an address taken past a shared path marks the atoms its
    // Type states SharedAddress, where its path is reached through shared or passes a conformance selection (SPEC 5.4: `x[k]@raw`
    // selects `index` and has its shared authority; 4.7.1; G147 ruling 72). x01's `c@follow@follow@raw` reaches w through c's shared
    // borrow of r and states r's region (the last borrow it is reached through, a `var` region read as itself); t7's `c.0@follow@raw`
    // passes the shared c before a member and a follow. u01's `t[0]@raw`, u01x (a later `t@uniq`), u01r's `r[0]@raw` through an
    // exclusive receiver, u02's Array element `ra[0]@raw`, s01's `s.items[0]@raw` (s: `uniq/G<ref/i32>`) and d01's Dictionary element
    // `rd[1]@raw` pass a conformance selection whatever their receiver's path grants (u01's t row also lists the exclusive receiver
    // borrow of the user index's indexUniq call, which Binding forms with index's; u01r's and d01's stated Origin is a meet of the
    // receiver variable's two regions, each operand marked). The twins take SPEC 5.4's writing pointer,
    // `u@follow@raw` from an exclusive path (`r`, or an `indexUniq` element `u = ...[0]@uniq`), and mark nothing. The address forms no
    // entry (SPEC 5.4).
    // Labels from SPEC (`G147\z6f-b\labels.txt`, before any run): the marked rows V (condition 3: the address has no write authority,
    // so put stores nothing through it), the twins I (5.7 Calls, 15.6.4: put may store z's borrow through the exclusive path, so
    // `z = 5` conflicts with the inspected Loan).
    [Theory]
    [InlineData("var w: ref/i32 = x@ref|var r = w@uniq|let c = r@ref", "c@follow@follow@raw", "w", "", "var r = w@uniq Inference -1:  SharedAddress")]
    [InlineData("var w: ref/i32 = x@ref|var r = w@uniq|let c = r@ref", "r@follow@raw", "w", "", "")]
    [InlineData("var w: ref/i32 = x@ref|var t = (w@uniq, 0)|let c = t@ref", "c.0@follow@raw", "w", "", "var t = (w@uniq, 0) Inference -1:  SharedAddress")]
    [InlineData("var t = Two.init(x@ref)", "t[0]@raw", "t", "", "var t = Two.init(x@ref) Projection 0: Two<ref/i32> X SharedAddress")]
    [InlineData("var t = Two.init(x@ref)", "t[0]@raw", "t", "|let k = t@uniq|inspect(k)", "var t = Two.init(x@ref) Projection 0: Two<ref/i32> X SharedAddress")]
    [InlineData("var t = Two.init(x@ref)|let u = t[0]@uniq", "u@follow@raw", "t", "", "")]
    [InlineData("var t = Two.init(x@ref)|var r = t@uniq", "r[0]@raw", "t", "", "var r = t@uniq Inference -1:  SharedAddress|var r = t@uniq Inference -2:  SharedAddress")]
    [InlineData("var t = Two.init(x@ref)|var r = t@uniq|let u = r[0]@uniq", "u@follow@raw", "t", "", "")]
    [InlineData("var arr: Array<ref/i32> = [x@ref]|var ra = arr@uniq", "ra[0]@raw", "arr", "", "var ra = arr@uniq Inference -1:  SharedAddress")]
    [InlineData("var arr: Array<ref/i32> = [x@ref]|var ra = arr@uniq|let u = ra[0]@uniq", "u@follow@raw", "arr", "", "")]
    [InlineData("var st = G.init([x@ref])|var s = st@uniq", "s.items[0]@raw", "st", "", "var s = st@uniq Inference -1:  SharedAddress")]
    [InlineData("var st = G.init([x@ref])|var s = st@uniq|let u = s.items[0]@uniq", "u@follow@raw", "st", "", "")]
    [InlineData("var d: Dictionary<i64, ref/i32> = [:]|_ = d.tryInsert(1, x@ref)|var rd = d@uniq", "rd[1]@raw", "d", "", "var rd = d@uniq Inference -1:  SharedAddress|var rd = d@uniq Inference -2:  SharedAddress")]
    public void AnAddressPastASharedPathMarksItsAtoms(string setup, string address, string inspected, string tail, string marked)
    {
        var c = MinimalEmissionTest.Analyze(BorrowsPrelude + "struct Wrap<E>\n    var value: E\n    public init(value: E) => self.value = value@move\n" +
            "    public func put<U>(self: uniq/Self, v: U) => ()\nstruct Two<T>\n    Self is UniqIndexable<isize>\n    associate Element is T\n" +
            "    var first: T\n    public init(first: T) => self.first = first@move\n" +
            "    public func index(self, key: ref/isize) -> ref/T during self => self.first@ref\n" +
            "    public func indexUniq(self: uniq/Self, key: ref/isize) -> uniq/T during self => self.first@uniq\nstruct G<T>\n" +
            "    public var items: Array<T>\n    public init(items: Array<T>) => self.items = items@move\nfunc p()\n    var x = 1\n    var z = 3\n" +
            $"    {setup.Replace("|", "\n    ", StringComparison.Ordinal)}\n    var h = Wrap.init({address}@raw/u8)\n    h.put(z@ref)\n    z = 5\n" +
            $"    inspect({inspected}){tail.Replace("|", "\n    ", StringComparison.Ordinal)}\n");
        string[] expected = marked.Length == 0 ? [] : marked.Split('|');
        Assert.Equal(expected, BorrowRows(c).Where(static x => x.EndsWith(nameof(Binding.LimitMarks.SharedAddress), StringComparison.Ordinal)));
    }

    // SPEC 5.7 under O-498 Q1 (i), G147 rulings 75 and 76 (Z6-F2): a value whose raw layer states another Origin, fitted into a raw
    // layer, marks the atom that layer states FittedIntoHolder (the raw fit's target; at swap's invariant position its value's too),
    // whatever form places it: an assignment (t6, t6t, the written-Type t6w, n01), a written-Type initializer (t4k), an argument through
    // Self's E (t6m) or a callee's own Origin (t6c), replace and swap (t6r, t6rx), a generic result (t6g), a Field write (t6f), a write
    // through `h@uniq` (t6p), a conversion restating the Origin outside unsafe (t6x), a constructor's slot (d04, d04t: each operand of its
    // meet), and a callee's clause relating an input raw layer's Origin to another (the raw fit's second source). ce2 isolates the clause
    // (`origin b outlives a`): `a and static` is a (SPEC 15.2.2), so a's solution is kx's Origin and every argument's raw Origin is its
    // parameter's, and x's projection is marked by the clause alone (store2's own body fit marks its `a`); its twin ce2t (`origin a
    // outlives b`, the shorter end on no raw layer) marks none. t6e does not isolate it: the clause also makes a's solution a meet (`a
    // and b`: the atoms of w's two Borrows, s's and the argument's, and of y's argument Borrow, each its own by SPEC 15.6.5), so h's
    // own argument fit marks every operand without it (store's body fit marks its `a`). t6's reassignment also as an overloaded call's
    // argument (t6o) and in a Closure body (t6k). `null` and an equal Origin form none (p02, t08x, x01, d01, t9, t9k). h's layer states
    // the atom `s@follow@raw` states, w's projection (which also carries SharedAddress), and `during s` names s's own region: by SPEC one Origin, the outer Origin of s's borrow (5.4, 15.2.1), which
    // Binding splits into two atoms (PathOrigin reads a `let` borrow's region as its initializer borrow's atom, the Origin expression the
    // let's own region), so t6w's, t6wn's and p10's initializers are raw fits too, a sound over-mark (labels.txt's addendum (2)). The
    // split is a PLAN open issue (Front); its removal unmarks t6wn and p10. Labels from SPEC (`G147\z6f2i\labels.txt`, before any run;
    // t6c's and t6e's forms after their invalid first forms; ce2 and ce2t `G147\z6f\dl\labels.txt`): the marked rows but ce2 I (h's layer
    // may hold r's exclusive address of v, so put may store z's borrow in v), ce2, ce2t, t6wn, x01, d01, t9 and t9k V, p02, p10 and t08x
    // I for other reasons. Z6 Revision 8 (the owner's O-507 (B)): each such atom whose fitted value may write is also FittedExclusive (r's,
    // k's, u's exclusive addresses, the clause's `static` value in ce2); store's and store2's own `a` (their value b is kept only by `kb:
    // ref`) and the written-Type and raw-local twins' s (w's projection, kept only by s's shared borrow) are not.
    [Theory]
    [InlineData("", FitHead + "var h = Wrap.init(s@follow@raw@raw/u8)|if c => h = Wrap.init(r@follow@raw@raw/u8)" + FitTail, FitS)]
    [InlineData("", FitHead + "var h = Wrap.init(s@follow@raw)|if c => h = Wrap.init(r@follow@raw)" + FitTail, FitS)]
    [InlineData("", FitHead + "var h: Wrap<raw/u8 during s> = Wrap.init(s@follow@raw@raw/u8)|if c => h = Wrap.init(r@follow@raw@raw/u8)" + FitTail, "let s = w@ref Inference -1:  FittedIntoHolder, FittedExclusive")]
    [InlineData("", FitHead + "var h = Box.init(s@follow@raw@raw/u8)|if c => h.set(r@follow@raw@raw/u8)" + FitTail, FitS)]
    [InlineData("func setp(h: uniq/Wrap<raw/u8 during o>, q: raw/u8 during o, anchor: ref/i32 during o) => h@follow = Wrap.init(q)|", FitHead + "var h = Wrap.init(s@follow@raw@raw/u8)|if c => setp(h@uniq, r@follow@raw@raw/u8, x@ref)" + FitTail, FitS + "|var x = 1 Projection 0: i32 S FittedIntoHolder, FittedExclusive")]
    [InlineData("", FitHead + "var h = Wrap.init(s@follow@raw@raw/u8)|if c => Kimi.Intrinsics.replace(h@uniq, with: Wrap.init(r@follow@raw@raw/u8))" + FitTail, FitS)]
    [InlineData("", FitHead + "var h = Wrap.init(s@follow@raw@raw/u8)|var g = Wrap.init(r@follow@raw@raw/u8)|if c => Kimi.Intrinsics.swap(h@uniq, g@uniq)" + FitTail, "var r = v@uniq Inference -1:  FittedIntoHolder, FittedExclusive|" + FitS)]
    [InlineData("func pick<T>(a: T, b: T) -> T => a@move|", FitHead + "var h = Wrap.init(pick(s@follow@raw@raw/u8, r@follow@raw@raw/u8))" + FitTail, FitS)]
    [InlineData("", FitHead + "var h = Box.init(s@follow@raw@raw/u8)|if c => h.value = r@follow@raw@raw/u8" + FitTail, FitS)]
    [InlineData("", FitHead + "var h = Wrap.init(s@follow@raw@raw/u8)|if c|    let hr = h@uniq|    hr@follow = Wrap.init(r@follow@raw@raw/u8)" + FitTail, FitS)]
    [InlineData(FitStore, FitHead + "var h = Wrap.init(s@follow@raw@raw/u8)|if c => store(h@uniq, r@follow@raw@raw/u8, w@ref, y@ref)" + FitTail, "func store(h: uniq/Wrap<raw/u8 during a>, q: raw/u8 during b, ka: ref/(ref/i32 during c) during a, kb: ref/i32 during b) Parameter 0:  FittedIntoHolder|var w: ref/i32 = x@ref Projection 0: ref/i32 S FittedIntoHolder, FittedExclusive|" + FitS + "|var y = 2 Projection 0: i32 S FittedIntoHolder, FittedExclusive")]
    [InlineData(FitClause + FitClauseCaller + "store2(kx, ks, hp@uniq, qs)|", "return", "func store2(ka: ref/i32 during a, kb: ref/i32 during b, h: uniq/(raw/u8 during a), q: raw/u8 during b) Parameter 0:  FittedIntoHolder|var x = 1 Projection 0: i32 S SharedAddress, FittedIntoHolder, FittedExclusive")]
    [InlineData(FitClauseT + FitClauseCaller + "store5(kx, ks, hp@uniq, kx@follow@raw@raw/u8)|", "return", "")]
    [InlineData("", FitHead + "var h = Wrap.init(r@follow@raw@(raw/u8 during s))" + FitTail, "let s = w@ref Inference -1:  FittedIntoHolder, FittedExclusive")]
    [InlineData("func g(v: ()) => ()|func g(v: i32) => ()|", FitHead + "var h = Wrap.init(s@follow@raw@raw/u8)|if c => g(h = Wrap.init(r@follow@raw@raw/u8))" + FitTail, FitS)]
    [InlineData("", FitHead + "var h = Wrap.init(s@follow@raw@raw/u8)|let pr = r@follow@raw@raw/u8|let hr = h@uniq|var put2 = func [hr, pr] () => hr@follow = Wrap.init(pr)|if c => put2()" + FitTail, FitS)]
    [InlineData("", "var x = 1|var y = 2|var z = 3|var w: ref/i32 = x@ref|var v: ref/i32 = y@ref|var k = v@uniq|var r = w@uniq|let q: raw/u8 during k = r@follow@raw@raw/u8|var h = Wrap.init(q)|h.put(z@ref)|z = 5|inspect(w)", "var k = v@uniq Inference -1:  FittedIntoHolder, FittedExclusive")]
    [InlineData(FitHolderStruct + "raw/u8 during s|    var k: ref/i32 during s|    public init(k: ref/i32 during s, q: raw/u8 during s)" + FitHolderBody, FitHolderUse + "@raw/u8)" + FitTail, "var x = 1 Projection 0: i32 S FittedIntoHolder, FittedExclusive")]
    [InlineData(FitHolderStruct + "raw/(ref/i32 during s) during s|    var k: ref/i32 during s|    public init(k: ref/i32 during s, q: raw/(ref/i32 during s) during s)" + FitHolderBody, FitHolderUse + ")" + FitTail, "var r = v@uniq Inference -2:  FittedIntoHolder, FittedExclusive|var x = 1 Projection 0: i32 S FittedIntoHolder, FittedExclusive")]
    [InlineData("", "var x = 1|var y = 2|var z = 3|var w: ref/i32 = x@ref|var v: ref/i32 = y@ref|var r = w@uniq|var u = v@uniq|var q = r@follow@raw@raw/u8|if c => q = u@follow@raw@raw/u8|var h = Wrap.init(q)|h.put(z@ref)|z = 5|inspect(w)", "var r = w@uniq Inference -1:  FittedIntoHolder, FittedExclusive")]
    [InlineData("", "var x = 1|var z = 3|var w: ref/i32 = x@ref|let s = w@ref|var h: Wrap<raw/u8 during s> = Wrap.init(s@follow@raw@raw/u8)|h.put(z@ref)|z = 5|inspect(w)", "let s = w@ref Inference -1:  FittedIntoHolder")]
    [InlineData("", "var x = 1|var z = 3|var w: ref/i32 = x@ref|var r = w@uniq|var h = Wrap.init(r@follow@raw@raw/u8)|h.put(z@ref)|z = 5|inspect(w)", "")]
    [InlineData("", "var x = 1|var z = 3|var w: ref/i32 = x@ref|let s = w@ref|let q: raw/u8 during s = s@follow@raw@raw/u8|var h = Wrap.init(q)|h.put(z@ref)|z = 5|inspect(w)", "let s = w@ref Inference -1:  FittedIntoHolder")]
    [InlineData("", "var x = 1|var z = 3|var w: ref/i32 = x@ref|var r = w@uniq|var q: raw/u8 during r = null|unsafe => q = w@raw@(raw/u8 during r)|var h = Wrap.init(q)|h.put(z@ref)|z = 5|inspect(w)", "")]
    [InlineData("", "var x = 1|var z = 3|var w: ref/i32 = x@ref|var r = w@uniq|let c2 = r@ref|var h = Wrap.init(c2@follow@follow@raw@raw/u8)|h.put(z@ref)|z = 5|inspect(w)", "")]
    [InlineData("struct HD {s}|    var p: raw/u8 during s|    var k: ref/i32 during s|    var m: uniq/i32 during s|    public init(k: ref/i32 during s, m: uniq/i32 during s)|        self.p = k@follow@raw@raw/u8|        self.k = k|        self.m = m|    public func put<U>(self: uniq/Self, v: U) => ()|    public func store(self: uniq/Self)|        var z = 3|        self.put(z@ref)|        z = 5|", "return", "")]
    [InlineData(FitCell, "var z = 3|var b = (0, 1)|let s = b.0@ref|let a = s@follow@raw@raw/u8|var h = Cell.init(null, b.0@uniq)|h.put(z@ref)|z = 5|inspect(b)", "")]
    [InlineData(FitCell, "var z = 3|var b = (0, 1)|var k = b.0@uniq|let s = k@ref|let a = s@follow@follow@raw@raw/u8|var h = Cell.init(null, k)|h.put(z@ref)|z = 5", "")]
    public void AFitFromAnotherOriginMarksTheHoldersAtoms(string declarations, string body, string marked)
    {
        string[] expected = marked.Length == 0 ? [] : marked.Split('|');
        Assert.Equal(expected, FitRows(declarations, body, Binding.LimitMarks.FittedIntoHolder));
    }

    // SPEC 5.7 Authority, 5.2.1 condition 3 under O-498 Q1 (i) (Z6 Revision 8; the owner's O-507 (B)): a value that may write (its Origin's
    // members where it is bound do not all keep only shared Loans, or it is itself marked) fitted into a raw layer marks the atom the layer
    // states FittedExclusive, and through a local region's finite sources the region of the holder initialized into the local: d04's x
    // projection and h's region (r's exclusive address of v), the same on a `uniq`-kept slot (d05), through a raw local judged by its
    // source (d04q); a value whose Origin keeps only shared Loans marks none (d04s). The mark is formed whatever the slot's Loan
    // requirement: a `ref`-kept declared layer is capped by its declaration before any mark is read (DeclaredStep). Labels: Z6's
    // `z6r8\labels.txt`, before any run.
    [Theory]
    [InlineData(FitHolderStruct + "raw/u8 during s|    var k: ref/i32 during s|    public init(k: ref/i32 during s, q: raw/u8 during s)" + FitHolderBody, FitHolderUse + "@raw/u8)" + FitTail, "var h = HF.init(x@ref, r@follow@raw@raw/u8) Inference -1:  FittedExclusive|var x = 1 Projection 0: i32 S FittedIntoHolder, FittedExclusive")]
    [InlineData(FitHolderStruct + "raw/u8 during s|    var k: ref/i32 during s|    public init(k: ref/i32 during s, q: raw/u8 during s)" + FitHolderBody, "var x = 1|var y = 2|var z = 3|var v: ref/i32 = y@ref|let sv = v@ref|var h = HF.init(x@ref, sv@follow@raw@raw/u8)" + FitTail, "")]
    [InlineData(FitHolderStruct + "raw/u8 during s|    var k: uniq/i32 during s|    public init(k: uniq/i32 during s, q: raw/u8 during s)" + FitHolderBody, "var x = 1|var y = 2|var z = 3|var v: ref/i32 = y@ref|var r = v@uniq|var h = HF.init(x@uniq, r@follow@raw@raw/u8)" + FitTail, "var h = HF.init(x@uniq, r@follow@raw@raw/u8) Inference -1:  FittedExclusive|var x = 1 Projection 0: i32 X FittedIntoHolder, FittedExclusive")]
    [InlineData(FitHolderStruct + "raw/u8 during s|    var k: ref/i32 during s|    public init(k: ref/i32 during s, q: raw/u8 during s)" + FitHolderBody, "var x = 1|var y = 2|var z = 3|var v: ref/i32 = y@ref|var r = v@uniq|let q = r@follow@raw@raw/u8|var h = HF.init(x@ref, q)" + FitTail, "var h = HF.init(x@ref, q) Inference -1:  FittedExclusive|var x = 1 Projection 0: i32 S FittedIntoHolder, FittedExclusive")]
    public void AValueThatMayWriteMarksTheAtomsItIsFittedInto(string declarations, string body, string marked)
    {
        string[] expected = marked.Length == 0 ? [] : marked.Split('|');
        Assert.Equal(expected, FitRows(declarations, body, Binding.LimitMarks.FittedExclusive));
    }

    // SPEC 8.6.1, 8.2.3, 5.7 Calls (DECISIONS BU-50, PLAN G271): an instance's kept part completes an associated projection of a struct
    // or enum subject by the subject's conformance, as a kept part of the subject: a binding the subject's declaration writes states
    // Unstated (Src's, an enum subject's, DerI's base clause's argument, a nested binding's through Src3<Src>), one the subject's argument
    // supplies carries that argument's mark (Src2<raw/u8>'s and Src3<Src2<raw/u8>>'s and SrcC<raw/u8>'s through a condition, none; below
    // a Field `HA<Src2<raw/u8>>` or `HA<SrcC<raw/u8>>`, Unstated), a closed binding is itself (W<Src>'s i32). A declared part's own
    // projections are completed before the mark: DerQ's `BaseI<Src2<raw/u8>.Item>` and DerF's `BaseF<Src2<raw/u8>.Item>` are written
    // `raw/u8` layers, so `HA<DerQ>`'s and DerF's base Field state Unstated. A Case payload completes too (EH<Src>). AdtDef's view is the
    // completed part WithoutRawOrigins and a body's read (StoredType) the same part without its mark, neither with an abstract part left.
    // A projection whose subject is a parameter (`unbound`'s HA<T>) or has an abstract part (`wrapped`'s HAd<Wrapping<T>>, whose
    // builtin Output the use's premise `T is PrimitiveInteger` decides) stays a projection; its closed instance HAd<Wrapping<i32>> completes it.
    [Fact]
    public void AKeptPartCompletesAnAssociatedProjection()
    {
        var c = MinimalEmissionTest.Analyze("contract HasItem\n    associate Item\nstruct Src\n    Self is HasItem\n    associate HasItem.Item is raw/u8\n" +
            "struct Src2<T>\n    Self is HasItem\n    associate HasItem.Item is T\nopen struct BaseI<P>\n    Self is HasItem\n    associate HasItem.Item is P\n" +
            "struct DerI : BaseI<raw/u8>\n    var m: i32\nstruct Src3\n    Self is HasItem\n    associate HasItem.Item is i32\n" +
            "struct HA<S>\n    S is HasItem\n    var x: S.Item\nstruct HH2\n    var h: HA<Src2<raw/u8>>\nstruct DerQ : BaseI<Src2<raw/u8>.Item>\n" +
            "    var m: i32\nopen struct BaseF<P>\n    var f: P\nstruct DerF : BaseF<Src2<raw/u8>.Item>\n    var m: i32\n" +
            "struct Nest<S>\n    S is HasItem\n    Self is HasItem\n    associate HasItem.Item is S.Item\nstruct SrcC<T>\n    Self is HasItem when T is Copy\n" +
            "        associate HasItem.Item is T\nstruct HHC\n    var h: HA<SrcC<raw/u8>>\nenum ESrc\n    Self is HasItem\n    associate HasItem.Item is raw/u8\n" +
            "    A\nenum EH<S>\n    S is HasItem\n    A(S.Item)\n    B\nstruct HAd<S>\n    S is Addable<S>\n    var x: S.Output\n" +
            "func probe(a: HA<Src>, b: HA<Src2<raw/u8>>, d: HA<DerI>, e: HH2, f: HA<Src3>, q: HA<DerQ>, r: DerF, n: HA<Nest<Src>>, " +
            "n2: HA<Nest<Src2<raw/u8>>>, hc: HHC, c2: HA<SrcC<raw/u8>>, es: HA<ESrc>, w: HAd<Wrapping<i32>>) => ()\n" +
            "func unbound<T>(g: HA<T>, eh: EH<Src>) -> ()\n    T is HasItem\n    return\n" +
            "func wrapped<T>(w: HAd<Wrapping<T>>) -> ()\n    T is PrimitiveInteger\n    return\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var functions = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().ToArray();
        var p = functions.Single(static x => x.Name == "probe").Parameters.Select(static x => x.Type.TypeOf()!).ToArray();
        var u = functions.Single(static x => x.Name == "unbound").Parameters.Select(static x => x.Type.TypeOf()!).ToArray();
        var w = functions.Single(static x => x.Name == "wrapped").Parameters.Select(static x => x.Type.TypeOf()!).ToArray();
        var types = c.Types;
        types.StoredParts(p[6], out var derF);
        BoundType[] holders = [p[0], p[1], p[2], Kept(p[3]), p[4], p[5], derF!, p[7], p[8], Kept(p[9]), p[10], p[11], p[12]];
        Assert.Equal(
            [
                "raw/u8 True", "raw/u8 False", "raw/u8 True", "raw/u8 True", "i32 False", "raw/u8 True", "raw/u8 True", "raw/u8 True", "raw/u8 False",
                "raw/u8 True", "raw/u8 False", "raw/u8 True", "Wrapping<i32> False",
            ],
            holders.Select(x => $"{TyCtxt.Name(Kept(x))} {TyCtxt.RawStated(Kept(x))}"));
        Assert.All(holders, x => Assert.Same(c.Binding.WithoutRawOrigins(Kept(x)), AdtDef.FieldType(c.Types, x, 0)));
        Assert.All(holders, x => Assert.Same(AdtDef.FieldType(c.Types, x, 0), c.Binding.StoredType(x.Symbol!.Adt!.Fields[0], x)));
        Assert.All(holders, x => Assert.False(AdtDef.FieldType(c.Types, x, 0)!.HasAbstractPart));
        var payload = types.StoredParts(u[1], out _)[Array.FindIndex(u[1].Symbol!.Adt!.Cases, static x => x is { Payload.Length: 1 })]!;
        Assert.Equal("(raw/u8) True", $"{TyCtxt.Name(payload)} {TyCtxt.RawStated(payload.Components[0])}");
        Assert.Equal(BoundTypeKind.AssociatedProjection, Kept(u[0]).Kind);
        Assert.Equal("Wrapping<T>.Output", TyCtxt.Name(Kept(w[0])));

        // The first kept part of a struct instance.
        BoundType Kept(BoundType x) => types.StoredParts(x, out _)[0]!;
    }

    // SPEC 8.9.2, 8.2.3 (DECISIONS BU-50, revised by bu55): a projection that two proven conformance paths of its subject bind names their
    // common Type. OwningIterator, BorrowingIterator and a user Wrapper conform to LendingIterator directly, binding
    // `I.(LendingIterator).LentItem(step)`, and through `Self is Iterator when I is Iterator`, whose Iterator binds `LentItem(step) is Item`,
    // so `I.Item`: the bindings differ as written and are both i32 under Counter, as W<Src>'s Item through HasItem and HasMore is, and
    // V<Src>'s through two conditional paths, the nested W<W<Src>>'s and the inherited W<DerS>'s. So the instance a call of `step` makes
    // (TyCtxt.Instance; WK's handed `next` has this shape) is a Prepared Option<i32>, the call's Type too, and each holder's Field and
    // Case payload are i32 (before: Rejected, which threw in WK's retention; the call kept the projection, and the reads in `f` were
    // NoApplicableOverload_Kd and InvalidPattern_Kd; V<Src>, W<W<Src>> and W<DerS> each left a binding unresolved without a record). The
    // twins resolved before: a direct conformer (Cursor), one refinement path (Counter, the generic Gen<i32>, the inherited DerS), and
    // bindings that agree as written (GenWrap's premise `I is Iterator`). Paths that disagree keep Binding's records (InstanceCollectorTest's
    // refinedDisagree).
    [Fact]
    public void ProvenPathsBindTheirCommonType()
    {
        const string Forward = "    associate LendingIterator.LentItem(step) is I.(LendingIterator).LentItem(step)\n    var inner: I\n" +
            "    public init(inner: I) => self.inner = inner@move\n" +
            "    public func next(self: uniq/Self during step) -> Option<I.(LendingIterator).LentItem(step)> => self.inner.next()\n";
        var c = MinimalEmissionTest.Analyze("contract HasItem\n    associate Item\ncontract HasMore: HasItem\n    associate Elem\n" +
            "    associate HasItem.Item is Elem\nstruct Src\n    Self is HasMore\n    associate HasMore.Elem is i32\nstruct W<S>\n    S is HasItem\n" +
            "    Self is HasItem\n    associate HasItem.Item is S.(HasItem).Item\n    Self is HasMore when S is HasMore\n" +
            "        associate HasMore.Elem is S.(HasMore).Elem\nopen struct BaseS\n    Self is HasMore\n    associate HasMore.Elem is i32\n" +
            "struct DerS : BaseS\n    var m: i32\nstruct HA<S>\n    S is HasItem\n    public var x: S.Item\nenum EH<S>\n    S is HasItem\n    A(S.Item)\n" +
            "    B\nstruct V<S>\n    Self is HasItem when S is HasItem\n        associate HasItem.Item is S.(HasItem).Item\n" +
            "    Self is HasMore when S is HasMore\n        associate HasMore.Elem is S.(HasMore).Elem\n" +
            "func f(h: HA<W<Src>>, e: EH<W<Src>>, d: HA<DerS>, v: HA<V<Src>>, ev: EH<V<Src>>, ww: HA<W<W<Src>>>, wd: HA<W<DerS>>) -> i32\n" +
            "    match e\n        .A(let n) => return n + h.x + d.x + v.x + ww.x + wd.x\n        .B => return h.x\nstruct Counter\n    Self is Iterator\n    associate Iterator.Item is i32\n    public init() => ()\n" +
            "    public func next(self: uniq/Self) -> Option<i32> => .None\nstruct Cursor\n    Self is LendingIterator\n" +
            "    associate LentItem(step) is ref/i32 during step\n    var value: i32 = 0\n    public init() => ()\n" +
            "    public func next(self: uniq/Self during step) -> Option<ref/i32 during step> => .Some(self.value@ref)\nstruct Gen<T>\n" +
            "    Self is Iterator\n    associate Iterator.Item is T\n    public init() => ()\n    public func next(self: uniq/Self) -> Option<T> => .None\n" +
            "struct Wrapper<I>\n    I is LendingIterator\n    Self is LendingIterator\n" + Forward +
            "    Self is Iterator when I is Iterator\n        associate Iterator.Item is I.(Iterator).Item\nstruct GenWrap<I>\n    I is Iterator\n" +
            "    Self is LendingIterator\n" + Forward + "    Self is Iterator\n    associate Iterator.Item is I.(Iterator).Item\n" +
            "func step<I>(it: uniq/I during s) -> Option<I.(LendingIterator).LentItem(s)>\n    I is LendingIterator\n    return it.next()\n" +
            "var i1 = Kimi.Iteration.owning(Counter.init())\nlet x1 = step(i1@uniq)\nvar counter = Counter.init()\n" +
            "var i2 = Kimi.Iteration.borrowing(counter@uniq)\nlet x2 = step(i2@uniq)\nvar i3 = Wrapper<Counter>.init(Counter.init())\n" +
            "let x3 = step(i3@uniq)\nvar i4 = GenWrap<Counter>.init(Counter.init())\nlet x4 = step(i4@uniq)\nvar i5 = Counter.init()\n" +
            "let x5 = step(i5@uniq)\nvar i6 = Cursor.init()\nlet x6 = step(i6@uniq)\nvar i7 = Gen<i32>.init()\nlet x7 = step(i7@uniq)\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var calls = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Select(static x => x.CallOf()).OfType<CallPlan>().Where(static x => x.Target.Name == "step").ToArray();
        var declared = ((FunctionKoto)calls[0].Target.Declaration).ReturnType!.TypeOf()!;
        Assert.Equal(
            [
                "OwningIterator<Counter> Prepared Option<i32> Option<i32>", "BorrowingIterator<Counter> Prepared Option<i32> Option<i32>",
                "Wrapper<Counter> Prepared Option<i32> Option<i32>", "GenWrap<Counter> Prepared Option<i32> Option<i32>",
                "Counter Prepared Option<i32> Option<i32>", "Cursor Prepared Option<ref/i32> Option<ref/i32>", "Gen<i32> Prepared Option<i32> Option<i32>",
            ],
            calls.Select(x => (c.Types.SubstOf(x)!, x.ReturnType)).Select(x => (x.Item1.Types[0]!, c.Types.Instance(declared, x.Item1), x.ReturnType))
                .Select(static x => $"{TyCtxt.Name(x.Item1)} {x.Item2.Storage} {TyCtxt.Name(x.Item2.Type!)} {TyCtxt.Name(x.ReturnType)}"));
        var holders = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "f").Parameters.Select(static x => x.Type.TypeOf()!);
        Assert.Equal(
            [
                "HA<W<Src>> Prepared i32", "EH<W<Src>> Prepared (i32)", "HA<DerS> Prepared i32", "HA<V<Src>> Prepared i32", "EH<V<Src>> Prepared (i32)",
                "HA<W<W<Src>>> Prepared i32", "HA<W<DerS>> Prepared i32",
            ],
            holders.Select(x => $"{TyCtxt.Name(x)} {c.Binding.PrepareTypeStorage(x)} {TyCtxt.Name(c.Types.StoredParts(x, out _)[0]!)}"));
    }

    // SPEC 8.6.1 (PLAN G290): a requirement call through a bound reference, in each closed instance, names the conformance whose reference
    // has a projection over the Type's parameter as its argument (InstanceContract): `x.size()` in `use<T>` under `T is Iter<i32>` has
    // B.size as its Body witness for `B<SrcI>` and for `B<Src>`, whose Item HasMore's refinement binds (before: Binding refuted both
    // calls, and once it accepted them Witness found no conformance, sel's InternalInvariant_Kd).
    [Fact]
    public void ABoundReferenceWithAProjectionArgumentHasItsInstancesWitness()
    {
        var c = MinimalEmissionTest.Analyze("contract HasItem\n    associate Item\ncontract HasMore: HasItem\n    associate Elem\n" +
            "    associate HasItem.Item is Elem\nstruct Src\n    Self is HasMore\n    associate HasMore.Elem is i32\nstruct SrcI\n    Self is HasItem\n" +
            "    associate HasItem.Item is i32\ncontract Iter<T>\n    func size(self: ref/Self) -> i32\nstruct B<S>\n    S is HasItem\n" +
            "    Self is Iter<S.(HasItem).Item>\n    var n: i32\n    public init(n: i32) => self.n = n\n" +
            "    public func size(self: ref/Self) -> i32 => self.n\nfunc use<T>(x: ref/T) -> i32\n    T is Iter<i32>\n    return x.size()\n" +
            "let a = B<SrcI>.init(n: 7)\nlet b = B<Src>.init(n: 8)\nrequire use(a@ref) == 7 and use(b@ref) == 8 else => $abort(\"size\")\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Equal(
            ["B<SrcI> Body size", "B<Src> Body size"],
            RequirementCalls(c, c.Kotonoha).Select(x => (Composed(c, x)!.Self!, Witness(c, x)))
                .Select(static x => $"{TyCtxt.Name(x.Item1)} {x.Item2.Kind} {x.Item2.Target?.Name}"));
    }

    // SPEC 5.7 Calls, Fits (DECISIONS BU-46): a kept part states Unstated on each raw layer its declaration writes without `during`, so an
    // instance's kept parts tell such a layer from one a Type argument supplies at any depth below it (RawStated): in a Field, a Tuple, a
    // Type argument of a Field, a base clause (also an intermediate one's), beside a supplied argument, a nested holder, a pair slot's
    // argument and its application `s/U`, a Case payload's Tuple and a `during self` pointee; never where the parameter's Type writes the
    // layer (the twins) or for a layer stating an Origin. Holder2's kept Wrap is another Type than Outer<raw/u8>'s, which is the
    // parameter's own Wrap<raw/u8>. AdtDef's parts are the kept ones WithoutRawOrigins and state no raw Origin, and no MIR local's Type
    // states Unstated (MirValidator's Local row; MirCensusTest asserts it over the census).
    [Fact]
    public void ADeclarationStatesEachUnstatedLayerItWrites()
    {
        var (c, p) = DeclaredLayers();
        var types = c.Types;
        (string Name, BoundType[] Layers)[] rows =
        [
            ("rawBuf", [Part(p["rawBuf"], 0)]), ("holdTuple", [Part(p["holdTuple"], 0).Components[0]]),
            ("holder2", [Part(Part(p["holder2"], 0), 0)]), ("holdOpt", [Payload(Part(p["holdOpt"], 0)).Components[0]]),
            ("derS", [Part(Part(p["derS"], -1), 0)]), ("d2", [Part(Part(Part(p["d2"], -1), -1), 0)]),
            ("d3", [Part(Part(Part(Part(p["d3"], -1), -1), 0), 0)]), ("b3", [Part(Part(Part(p["b3"], -1), 0), 0)]),
            ("m", [Part(Part(p["m"], 0), 0), Part(Part(p["m"], 0), 1)]), ("holdEnum", [Payload(Part(p["holdEnum"], 0)).Components[0]]),
            ("holdTW", [Part(Part(p["holdTW"], 0), 0).Components[0]]), ("h3", [Part(Part(Part(p["h3"], 0), 0), 0)]),
            ("oh", [Part(Part(Part(Part(p["oh"], 0), 0), 0), 0)]), ("hpso", [Part(Part(p["hpso"], 0), 0)]),
            ("hpss", [Part(Part(p["hpss"], 1), 0)]), ("hpst", [Part(Part(p["hpst"], 0), 0)]), ("hpu", [Part(Part(p["hpu"], 0), 0)]),
            ("hpus", [Part(Part(p["hpus"], 1), 0)]), ("hfa", [Part(Part(p["hfa"], 0), 0).Components[0]]),
            ("ep", [Part(Payload(p["ep"]).Components[0].Components[0], 0)]), ("how", [Part(Payload(Part(p["how"], 0)).Components[0], 0)]),
            ("hold2", [Part(p["hold2"], 0).Components[0]]), ("sb", [Part(p["sb"], 0).Components[0], Part(p["sb"], 0)]),
            ("hb2", [Part(Part(p["hb2"], 0), 0).Components[0]]), ("outer", [Part(Part(p["outer"], 0), 0)]), ("wrap", [Part(p["wrap"], 0)]),
            ("m2", [Part(Part(p["m2"], 0), 0), Part(Part(p["m2"], 0), 1)]), ("ps", [Part(p["ps"], 0)]), ("pu", [Part(p["pu"], 0)]),
            ("hold", [Part(Part(p["hold"], 0).Components[0], 0)]), ("opt2", [Part(Payload(p["opt2"]).Components[0], 0)]),
            ("derX", [Part(Part(Part(p["derX"], -1), 0), 0)]), ("arr", [p["arr"].Components[0]]), ("box2", [Part(p["box2"], 0).Components[0]]),
            ("ow", [Part(Part(Part(p["ow"], 0), 0), 0)]),
        ];
        Assert.Equal(
            [
                "rawBuf True", "holdTuple True", "holder2 True", "holdOpt True", "derS True", "d2 True", "d3 True", "b3 True", "m True False",
                "holdEnum True", "holdTW True", "h3 True", "oh True", "hpso True", "hpss True", "hpst True", "hpu True", "hpus True", "hfa True",
                "ep True", "how True", "hold2 True", "sb True False", "hb2 True", "outer False", "wrap False", "m2 False False", "ps False",
                "pu False", "hold False", "opt2 False", "derX False", "arr False", "box2 False", "ow False",
            ],
            rows.Select(static x => $"{x.Name} {string.Join(" ", x.Layers.Select(TyCtxt.RawStated))}"));
        Assert.NotSame(Part(p["holder2"], 0), Part(p["outer"], 0));
        Assert.Same(p["wrap"], Part(p["outer"], 0));
        foreach (var holder in p.Values.Where(static x => AdtDef.IsStruct(x) || AdtDef.IsEnum(x)))
        {
            var erased = Stored(c.Types, holder).ToArray();
            var parts = types.StoredParts(holder, out var parent).ToArray();
            BoundType?[] kept = AdtDef.IsStruct(holder) ? [.. parts, parent] : parts;
            Assert.Equal(erased.Length, kept.Length);
            Assert.All(Enumerable.Range(0, kept.Length), i => Assert.Same(erased[i], c.Binding.WithoutRawOrigins(kept[i])));
            Assert.DoesNotContain(erased, static x => x?.CarriesRawOrigin == true);
        }

        var seen = new HashSet<BoundType>(ReferenceEqualityComparer.Instance);
        Assert.DoesNotContain(c.BuildMir().Bodies.ToArray().SelectMany(static x => x.Locals.ToArray()), x => x.Type is { } type && TyCtxt.StatesUnstated(type, seen));

        // A kept Field (the kept base at -1), or the kept payload Tuple of an enum's Case `part`.
        BoundType Part(BoundType x, int part)
        {
            var parts = types.StoredParts(x, out var parent);
            return (part < 0 ? parent : parts[part])!;
        }

        // The kept payload Tuple of an enum's first Case with a payload.
        BoundType Payload(BoundType x) => Part(x, Array.FindIndex(x.Symbol!.Adt!.Cases, static y => y is { Payload.Length: > 0 }));
    }

    // SPEC 8.1.1, 8.2.3, 8.6.1 (DECISIONS BU-34): a pair slot binds a raw argument without its Origin, so an instance, a nested Type, a
    // Contract reference and an Item over a raw argument stating one intern as those over the unstated pointer; a Type slot keeps it.
    [Fact]
    public void APairSlotInternsARawArgumentUnstated()
    {
        var c = MinimalEmissionTest.Analyze(RawPointerSyntaxTest.PairHolder + RawPointerSyntaxTest.SlotLocal +
            "struct Outer<s/T>\n    public struct Inner\n        public var v: s/T\ncontract K<s/T>\n    func get(self: ref/Self) -> s/T\n" +
            "struct A\n    Self is K<raw/i32 during static>\n    public func get(self: ref/Self) -> raw/i32 => null\n" +
            "struct B\n    Self is K<raw/i32>\n    public func get(self: ref/Self) -> raw/i32 => null\n" +
            "func id<s/T>(v: s/T) -> s/T => v@move\nfunc idt<T>(v: T) -> T => v@move\n" +
            "func probe(x: ref/i32, a: Holder<raw/i32 during x>, b: Holder<raw/i32>, c: Holder<raw/i32 during static>, d: Local<raw/i32 during x>, " +
            "e: Local<raw/i32>, f: Outer<raw/i32 during x>.Inner, g: Outer<raw/i32>.Inner)\n" +
            "    let i = id<raw/i32 during x>\n    let j = id<raw/i32>\n    let k = idt<raw/i32 during x>\n    let l = idt<raw/i32>\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var nodes = KotoTree.Walk(c.Kotonoha.RootKoto).ToArray();
        var p = nodes.OfType<FunctionKoto>().Single(static x => x.Name == "probe").Parameters.Select(static x => x.Type.TypeOf()!).ToArray();
        var items = nodes.OfType<VariableKoto>().Where(static x => x.NameKoto.IdentifierName is "i" or "j" or "k" or "l").Select(static x => x.SymbolOf()!.Type!).ToArray();
        var references = nodes.OfType<IsKoto>().Where(static x => x.ToString()!.StartsWith("Self is K", StringComparison.Ordinal)).Select(static x => x.Right.TypeOf()).ToArray();
        Assert.Equal((true, true, false, true), (ReferenceEquals(p[1], p[2]), ReferenceEquals(p[3], p[2]), ReferenceEquals(p[4], p[5]), ReferenceEquals(p[6], p[7])));
        Assert.Equal((true, false), (ReferenceEquals(items[0], items[1]), ReferenceEquals(items[2], items[3])));
        Assert.Equal(2, references.Length);
        Assert.NotNull(references[0]);
        Assert.Same(references[0], references[1]);
    }

    // design-final §15 (DECISIONS BU-41, BU-46, BU-58): the stored-layer readers and the kept parts allocate nothing when warm, also on a
    // kept nested instance, which its first read substitutes, and on one whose kept part states Unstated (Holder2's Wrap); so do WrittenAt
    // and StatesUnstated over the caller's scratch.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmStoredRawLayerReadAllocatesNothing()
    {
        var (types, p) = StoredLayers();
        var nested = types.StoredParts(p[22], out _)[0]!;
        var (declared, q) = DeclaredLayers();
        var holder2 = q["holder2"];
        var wrap = declared.Types.StoredParts(holder2, out _)[0]!;
        _ = declared.Types.StoredParts(wrap, out _);
        var members = new List<RawMember>();
        var seen = new HashSet<BoundType>(ReferenceEqualityComparer.Instance);
        int[] zero = [0], one = [1];
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            members.Clear();
            if (types.StoredStep(p[3], 0, []) != HoldStep.Own || types.StoredStep(p[6], 1, []) != HoldStep.Shared || types.StoredStep(p[8], 1, one) != HoldStep.Own ||
                types.StoredStep(p[19], 0, zero) != HoldStep.Exclusive || types.StoredParts(p[19], out _).Length != 2 ||
                !ReferenceEquals(types.StoredParts(p[22], out _)[0], nested) || types.StoredStep(nested, 0, []) != HoldStep.Exclusive ||
                TyCtxt.RawStated(types.StoredParts(nested, out _)[0]!) || !MembersAt(types, nested, 0, [], members) ||
                TyCtxt.RawStated(types.StoredParts(p[3], out _)[0]!))
            {
                throw new InvalidOperationException("Stored raw steps changed.");
            }

            if (!ReferenceEquals(declared.Types.StoredParts(holder2, out _)[0], wrap) || declared.Types.StoredStep(wrap, 0, []) != HoldStep.Address ||
                !TyCtxt.RawStated(declared.Types.StoredParts(wrap, out _)[0]!) || !MembersAt(declared.Types, wrap, 0, [], members))
            {
                throw new InvalidOperationException("A declared raw layer's reads changed.");
            }

            if (TyCtxt.WrittenAt(p[8], 1, one) is null || TyCtxt.WrittenAt(p[8], 0, one) is not null || !TyCtxt.StatesUnstated(wrap, seen) ||
                TyCtxt.StatesUnstated(types.StoredParts(p[8], out _)[0]!, seen))
            {
                throw new InvalidOperationException("The written and Unstated views changed.");
            }
        }));
    }

    // DECISIONS BU-24: a layer's stated Origin is one slot whose role its Semantics decides: Origin never reads a raw layer's, RawOrigin
    // only a raw layer's, and a rebuild keeps both. The constructor stores Unstated in the raw role only (a kept part's mark of a layer its
    // declaration writes, which RawStated reads; DECISIONS BU-46, the owner's 「許可して設計 1 (推奨)」 of 2026-10-27), and neither Unstated,
    // Shared nor Holder in the borrow role; a pair binding keeps Unstated and drops a stated Origin; a raw Origin enters no instance key
    // (R5-M39: Erase gives the unstated layer), nor AdtDef's view (WithoutRawOrigins).
    [Fact]
    public void ARawLayerKeepsItsOriginRole()
    {
        var c = MinimalEmissionTest.Analyze("func f(x: ref/i32) -> i32 => x@follow\n");
        var types = c.Types;
        var origin = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "f").Parameters[0].Type.TypeOf()!.Origin!;
        var borrow = types.Reference(SemanticsKind.Ref, BoundType.I32, origin);
        var pointer = types.Pointer(borrow);
        var stated = types.Pointer(BoundType.I32, origin);
        var marked = types.Pointer(BoundType.I32, BoundOrigin.Unstated);
        Assert.Equal((null, BoundOrigin.Unstated, false, true), (marked.Origin, marked.RawOrigin, marked.CarriesOrigin, marked.CarriesRawOrigin));
        var seen = new HashSet<BoundType>(ReferenceEqualityComparer.Instance);
        Assert.Equal(
            (true, false, true, false),
            (TyCtxt.RawStated(marked), TyCtxt.RawStated(stated), TyCtxt.StatesUnstated(types.Tuple([BoundType.I32, marked]), seen), TyCtxt.StatesUnstated(stated, seen)));
        Assert.Equal((marked, types.Pointer(BoundType.I32)), (types.PairBinding(marked), types.PairBinding(stated)));
        Assert.Same(types.Erase(types.Pointer(BoundType.I32)), types.Erase(marked));
        Assert.Same(types.Pointer(BoundType.I32), c.Binding.WithoutRawOrigins(marked));
        Assert.Equal((origin, null, true, false, true), (borrow.Origin, borrow.RawOrigin, borrow.CarriesOrigin, borrow.CarriesRawOrigin, borrow.CarriesAnyOrigin));
        Assert.Equal((null, null, true, false), (pointer.Origin, pointer.RawOrigin, pointer.CarriesOrigin, pointer.CarriesRawOrigin));
        Assert.Equal((null, origin, false, true, true), (stated.Origin, stated.RawOrigin, stated.CarriesOrigin, stated.CarriesRawOrigin, stated.CarriesAnyOrigin));
        Assert.NotSame(types.Pointer(BoundType.I32), stated);
        Assert.Same(pointer, types.Rebuild(pointer, [borrow], pointer.Origin, []));
        Assert.Same(borrow, types.Rebuild(borrow, [BoundType.I32], borrow.Origin, []));
        Assert.Same(stated, types.Rebuild(stated, [BoundType.I32], null, []));
        Assert.Same(types.Erase(types.Pointer(BoundType.I32)), types.Erase(stated));
        Assert.Throws<InvalidOperationException>(() => types.Reference(SemanticsKind.Ref, BoundType.I32, BoundOrigin.Unstated));
        Assert.Throws<InvalidOperationException>(() => types.Reference(SemanticsKind.Ref, BoundType.I32, BoundOrigin.Holder));
    }

    // SPEC 6.1.3, 8.13, 8.15.1: the body of every MIR key symbol is instantiated exactly when the symbol or a declaration enclosing it has a
    // Type, Semantics or length slot, even an unused one; an Origin slot does not count. A named function nested two levels or in a block of a
    // generic function, or in a generic struct's method, is instantiated as one level is; a specialization declares no slot (SPEC 8.13.1), so a
    // top-level one is closed and one in a generic struct, with what it nests, is generic in the struct's slot (DECISIONS R5-M34).
    [Fact]
    public void InstantiatesFollowsTheEnclosingSlots()
    {
        var c = MinimalEmissionTest.Analyze(InstanceSource);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        string[] expected =
        [
            "Fn plain False", "Fn pass True", "Closure pass True", "Fn specialize pass False", "Closure specialize pass False", "Fn size True",
            "DefaultTemplate size True", "Fn outer True", "Fn middle True", "Fn inner True", "Fn scoped True", "Fn host False", "Fn helper False",
            "Fn init True", "Getter current.Get True", "Fn get True", "Fn local True", "Fn f True", "Fn specialize f True",
            "Closure specialize f True", "Fn nested True", "Fn init False", "Getter current.Get False", "StaticInit limit False", "Fn seed False",
        ];
        var keys = c.BuildMir().Bodies.ToArray().Where(static x => x.Key.Decl is not null && x.Source.Module == 0).Select(static x => x.Key).ToArray();
        var actual = keys.Select(x => $"{x.Kind} {Label(x.Decl!.Declaration)} {c.Types.Instantiates(x.Decl!)}");
        Assert.Equal(expected.Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));

        // A closure is labeled by the function enclosing it.
        static string Label(Koto declaration) => declaration switch
        {
            FunctionKoto { IsAnonymous: true } => Label(Enclosing(declaration.Parent!)),
            FunctionKoto { IsSpecialization: true } function => "specialize " + function.Name,
            FunctionKoto function => function.Name,
            _ => ((VariableKoto)declaration).NameKoto.IdentifierName,
        };

        static FunctionKoto Enclosing(Koto node) => node as FunctionKoto ?? Enclosing(node.Parent!);
    }

    // SPEC 6.1.3, 6.3.3, 9.3, 11.3 (PLAN G157 (49), G300; DECISIONS BU-60): the declaration whose instance a nested definition's instance
    // holds is the named function whose body declares it, through Closures and blocks, and in an accessor's body the accessor's executing
    // function, which keys its body: the getter's for a function, a Closure and a function in a Closure there, the setter's for its
    // function; a method's for its function. Code in a container outside every body belongs to the container: a Field initializer's
    // Closure and a function in it hold the declaring Type's instance (before: none), a group static's Closure its group's. A member, an
    // accessor, a top-level function and a static's initializer are declared by a container, so none.
    [Fact]
    public void EnclosingDeclarationIsTheDeclarationWhoseInstanceTheDefinitionHolds()
    {
        var c = MinimalEmissionTest.Analyze(EnclosingSource);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        string[] expected =
        [
            "Fn init none", "Getter got.Get none", "Fn inner got.Get", "Closure keep got.Get", "Fn deep got.Get", "Setter got.Set none",
            "Fn store got.Set", "Fn get none", "Fn local get", "StaticInit limit none", "Closure limit Limits", "Fn outer none",
            "Closure count Box", "Fn size Box",
        ];
        var keys = c.BuildMir().Bodies.ToArray().Where(static x => x.Key.Decl is not null && x.Source.Module == 0).Select(static x => x.Key);
        var actual = keys.Select(x => $"{x.Kind} {Label(x.Decl!.Declaration)} {Enclosing(x.Decl!)}");
        Assert.Equal(expected.Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));

        string Enclosing(BindingSymbol definition) => c.Types.EnclosingDeclaration(definition) is { } declaration ? Label(declaration.Declaration) : "none";

        // A Closure is labeled by the local, Field or static it initializes, a container by its name.
        static string Label(Koto declaration) => declaration switch
        {
            FunctionKoto { IsAnonymous: true } closure => Initialized(closure.Parent!),
            FunctionKoto function => function.Name,
            DeclarationContainerKoto container => container.Name,
            _ => ((VariableKoto)declaration).NameKoto.IdentifierName,
        };

        static string Initialized(Koto node) => node is VariableKoto variable ? variable.NameKoto.IdentifierName : Initialized(node.Parent!);
    }

    // SPEC 6.1.3, 6.3.3 (DECISIONS BU-60): a Field initializer's Closure read through an owner holds the owner's instance modulo Origins as
    // its context's declaring Type, by substitution: one Closure Type for every read of one instance, another for another instance. An
    // owner instance whose erased form has an Origin position, static or not, is held erased, and its readers see no instance
    // (NestedInstance): the Closure's body is its own identity, closed in no instance (PLAN G300). The owner's (a) (2026-10-31): the
    // context is formed from the closed instance modulo Origins, and the gate is the readers' view, not a staged context.
    [Fact]
    public void AFieldInitializersClosureHoldsItsOwnersInstance()
    {
        const string Source = "struct Box<T>\n    public var f = func [] () -> isize => 4\n    public init() => ()\n" +
            "func reads(s: ref/i32 during static, x: ref/i32) -> isize\n    let a = Box<i32>.init().f\n    let b = Box<i64>.init().f\n" +
            "    let c = Box<i32>.init().f\n    let d = Box<ref/i32 during static>.init().f\n    let e = Box<ref/i32>.init().f\n    return 0\n";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var body = c.BuildMir().Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "reads");
        var types = "abcde".Select(name => body.Locals[Enumerable.Range(0, body.Locals.Length).Single(x => body.Locals[x].Name == name.ToString())].Type!).ToArray();
        Assert.Same(types[0], types[2]);
        Assert.NotSame(types[0], types[1]);
        Assert.Equal(["Box<i32>", "Box<i64>"], types[..2].Select(x => TyCtxt.Name(x.InstanceContext!.Declaring!)));
        Assert.Same(types[3], types[4]);
        var held = TyCtxt.HeldType(types[3])!;
        Assert.Same(c.Types.Erase(held), held);
        Assert.Equal("Box<ref/i32>", TyCtxt.Name(held));
        Assert.Null(TyCtxt.NestedInstance(types[3]));
        Assert.True(c.Types.SubstOfClosure(types[3]) is { IsIdentity: true } context && ReferenceEquals(context.Target, types[3].Symbol));
    }

    // DECISIONS BU-71: a Type's height is 0 for a leaf (a Primitive, a struct without arguments, a Type parameter), else 1 + the greatest
    // height of its components: Type arguments, Tuple components, a fixed array's element (its length is no child), a referent or pointee,
    // a Function Type's inputs and result, a projection's self and Contract Type.
    [Theory]
    [InlineData("i32", 0)]
    [InlineData("S", 0)]
    [InlineData("T", 0)]
    [InlineData("Option<i32>", 1)]
    [InlineData("(i32, i64)", 1)]
    [InlineData("ref/i32", 1)]
    [InlineData("raw/i32", 1)]
    [InlineData("rc/S", 1)]
    [InlineData("[64 of i32]", 1)]
    [InlineData("Option<(i32, Option<i64>)>", 3)]
    [InlineData("(i32) -> bool", 2)]
    [InlineData("T.(Addable<i32>).Output", 2)]
    public void ATypesHeightIsOneAboveItsHighestComponent(string type, int height)
    {
        var c = MinimalEmissionTest.Analyze($"struct S\n    public var x: i32\nfunc probe<T>(x: {type}) -> i32\n    T is Addable<i32>\n    return 0\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var probe = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "probe");
        Assert.Equal(height, probe.Parameters[0].Type.TypeOf()!.Height);
    }

    // DECISIONS BU-71: a Closure Type's height counts its whole context as one child: a capture-free Closure in g<T> has no components, yet
    // its instances' heights follow T (an instance context, R5-M36), and a Field initializer's Closure stands one above the instance it holds
    // (BU-60), above its signature. An obj/-recursive non-regular instance grows one level per step through its Field.
    [Fact]
    public void AClosuresHeightCountsItsContext()
    {
        const string Source = "func g<T>() -> isize\n    let c = func [] () -> isize => 1\n    return c()\n" +
            "struct Box<T>\n    public var f = func [] () -> isize => 4\n    public init() => ()\nstruct N<X>\n    public var next: Option<obj/N<(X, X)>>\n" +
            "func reads(n: N<i32>) -> isize\n    let a = Box<i32>.init().f\n    let b = Box<(i32, (i32, i64))>.init().f\n" +
            "    return g<i32>() + g<(i32, (i32, i64))>()\n";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var nodes = KotoTree.Walk(c.Kotonoha.RootKoto).ToArray();
        var g = KotoTree.Walk(nodes.OfType<FunctionKoto>().Single(static x => x.Name == "g")).OfType<FunctionKoto>().Single(static x => x.IsAnonymous);
        var closure = g.ClosureOf()!;
        Assert.Equal((null, 0), (closure.EnvironmentType!.InstanceContext, closure.EnvironmentType.Height));
        var instances = nodes.OfType<InvocationKoto>().Select(static x => x.CallOf()).Where(static x => x?.Target.Name == "g")
            .Select(x => c.Types.ReadableInstance(closure.EnvironmentType!, c.Types.StorageSubst(x!))!).ToArray();
        Assert.All(instances, static x => Assert.True(x.Components.Count == 0 && x.Height == x.InstanceContext!.Height + 1));
        Assert.Equal([1, 3], instances.Select(static x => x.Height));
        var reads = c.BuildMir().Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "reads");
        BoundType[] held = [Local(reads, "a"), Local(reads, "b")];
        Assert.Equal([2, 4], held.Select(static x => x.Height));
        Assert.All(held, static x => Assert.Equal(TyCtxt.HeldType(x)!.Height + 1, x.Height));
        Assert.Equal(1, c.Binding.ClosureSignature(held[1])!.Height);
        var n = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "reads").Parameters[0].Type.TypeOf()!;
        var heights = new List<int> { n.Height };
        for (var step = 0; step < 3; step++)
        {
            var next = AdtDef.FieldType(c.Types, n, 0)!;
            n = next.Components[0].Components[0];
            Assert.Equal(n.Height + 2, next.Height);
            heights.Add(n.Height);
        }

        Assert.Equal([1, 2, 3, 4], heights);

        static BoundType Local(MirBody body, string name) => body.Locals.ToArray().Single(x => x.Name == name).Type!;
    }

    // DECISIONS BU-71: a function item's components are its Type arguments, and a substitution's height counts its Self.
    [Fact]
    public void AFunctionItemAndASubstitutionCountTheirTypes()
    {
        var c = MinimalEmissionTest.Analyze("func make<T>() -> i32 => 1\nlet f = make<(i32, (i32, i64))>\nrequire f() == 1 else => $abort(\"make\")\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var item = KotoTree.Walk(c.Kotonoha.RootKoto).Select(static x => x.TypeOf()).First(static x => x is { Kind: BoundTypeKind.FunctionItem })!;
        var tall = Assert.Single(item.Components);
        var subst = c.Types.Synthesized(item.Symbol!, tall, BoundType.I32);
        Assert.Equal((3, 2, tall, 2), (item.Height, tall.Height, subst.Self, subst.Height));
    }

    // DECISIONS R5-M39 (K package 4.1), R4-M180: Erase makes every Origin static and binds none of a substitution's (view's call binds an
    // input Origin), and keeps everything else; two calls whose substitutions differ in Origins only have one erased substitution, and
    // every Type of the callee's body and closure, the closure's context included, erases alike in their instances; erasing is idempotent
    // and keeps a Type without Origins; a binder that does not instantiate has none.
    [Fact]
    public void EraseKeepsEverythingButOrigins()
    {
        var c = MinimalEmissionTest.Analyze(EraseSource);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var types = c.Types;
        var nodes = KotoTree.Walk(c.Kotonoha.RootKoto).ToArray();
        var calls = nodes.OfType<InvocationKoto>().Select(static x => x.CallOf()).Where(static x => x?.Target.Name == "keep")
            .Select(x => types.SubstOf(x!)!).ToArray();
        Assert.Equal(2, calls.Length);
        Assert.NotSame(calls[0], calls[1]);
        var erased = types.Erase(calls[0])!;
        Assert.Same(erased, types.Erase(calls[1]));
        Assert.Same(erased, types.Erase(erased));
        Assert.Equal((SemanticsKind.Ref, BoundOrigin.Static), (erased.Types[0]!.Semantics, erased.Types[0]!.Origin));
        var viewed = types.SubstOf(nodes.OfType<InvocationKoto>().Select(static x => x.CallOf()).Single(static x => x?.Target.Name == "view")!)!;
        Assert.NotEmpty(viewed.Inputs.ToArray());
        Assert.All([erased, types.Erase(viewed)!], static x => Assert.True(x.Origins.IsEmpty && x.Inputs.IsEmpty));
        var locals = c.BuildMir().Bodies.ToArray().Where(static x => x.Key.Decl?.Name == "keep" || (x.Key.Kind == MirBodyKind.Closure && x.Source.Module == 0))
            .SelectMany(static x => x.Locals.ToArray()).Select(static x => x.Type!).ToArray();
        Assert.True(locals.Length > 4);
        foreach (var local in locals)
        {
            var first = types.Erase(types.Apply(local, calls[0]).Type!);
            Assert.Same(first, types.Erase(types.Apply(local, calls[1]).Type!));
            Assert.Same(first, types.Erase(first));
        }

        var closure = Assert.Single(nodes.OfType<FunctionKoto>(), static x => x.IsAnonymous).ClosureOf()!.EnvironmentType!;
        var closed = calls.Select(x => types.Apply(closure, x).Type!).ToArray();
        Assert.NotSame(closed[0], closed[1]);
        Assert.Same(types.Erase(closed[0]), types.Erase(closed[1]));
        Assert.Same(types.Erase(closed[0].InstanceContext!), types.Erase(closed[0]).InstanceContext);
        Assert.Same(BoundType.I32, types.Erase(BoundType.I32));
        Assert.Same(types.Tuple([BoundType.I32, BoundType.Boolean]), types.Erase(types.Tuple([BoundType.I32, BoundType.Boolean])));
        var host = nodes.OfType<InvocationKoto>().Select(static x => x.CallOf()).Single(static x => x?.Target.Name == "host")!;
        Assert.Null(types.Erase(types.SubstOf(host)!));
    }

    // K package 4.1: an erased struct or enum instance stores the erasure of what the instance stores, for every instance the MIR locals of
    // the library and the milestones hold (Layout.Of lays out erased Types).
    [Theory]
    [MemberData(nameof(HirColumnsTest.Corpus), MemberType = typeof(HirColumnsTest))]
    public void AnErasedInstanceStoresTheErasedTypes(string name)
    {
        var (c, _) = SemanticSnapshotTest.Bind(name);
        var instances = new List<BoundType>();
        var problems = ErasedStoreProblems(c, instances);
        Assert.True(problems.Count == 0 && instances.Count > 0, $"{instances.Count} instances; {problems.Count} differ:\n{string.Join('\n', problems.Take(20))}");
    }

    // DECISIONS BU-24 (design-final §15): an instance's stored shapes state no raw Origin, so the property above holds where the erased
    // instance is the user's own Type: a raw Field and payload stating `self` (a Type without Origins erases to itself) and a raw Field
    // stating a slot (its erased instance is the `during static` Type a parameter states).
    [Fact]
    public void AnInstanceStoresNoRawOrigin()
    {
        var c = MinimalEmissionTest.Analyze(RawShapeSource);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var instances = new List<BoundType>();
        var problems = ErasedStoreProblems(c, instances);
        Assert.True(problems.Count == 0, $"{problems.Count} differ:\n{string.Join('\n', problems)}");
        var views = instances.Where(static x => TyCtxt.Name(x) == "View<i32>").ToArray();
        Assert.Equal(2, views.Length);
        var erased = Assert.Single(views, x => ReferenceEquals(c.Types.Erase(x), x));
        Assert.All(views, x => Assert.Same(erased, c.Types.Erase(x)));
        Assert.All(["Block", "Cell"], x => Assert.Contains(instances, y => TyCtxt.Name(y) == x && ReferenceEquals(c.Types.Erase(y), y)));
    }

    // Plan 5.6.2: a call in an omitted default composed with the substitution of the call omitting it instantiates each declared Type of
    // its callee as the two substitutions applied in turn, over the corpus and over generic, length, Origin, member and requirement calls
    // in defaults.
    [Fact]
    public void ComposeIsTheTwoStepInstantiation()
    {
        var problems = new List<string>();
        var corpus = 0;
        foreach (var name in HirColumnsTest.Corpus.Select(static x => x.Data))
        {
            var (c, module) = SemanticSnapshotTest.Bind(name);
            corpus += ComposeProblems(c, module, problems);
        }

        var direct = MinimalEmissionTest.Analyze(ComposeSource);
        Assert.True(direct.Binding.Result.IsComplete, MinimalEmissionTest.Describe(direct));
        var calls = ComposeProblems(direct, direct.Kotonoha, problems);
        Assert.True(corpus == 6 && calls == 7 && problems.Count == 0, $"{corpus} corpus and {calls} direct nested calls; {problems.Count} differ:\n{string.Join('\n', problems.Take(40))}");

        // copy, count, first, same (a requirement, whose Self the composition closes) and get bind a Type, a length, an Origin, Self and
        // a declaring Type of the instance.
        var composed = NestedCalls(direct.Binding, direct.Kotonoha).Select(x => (x.Inner.Target.Name, Subst: direct.Types.Compose(direct.Types.SubstOf(x.Inner)!, direct.Types.SubstOf(x.Outer)!).Subst!)).ToArray();
        Assert.Equal(["i32", "bool"], composed.Where(static x => x.Name == "copy").Select(static x => x.Subst.Types[0]!.Name));
        Assert.Equal(3, composed.Single(static x => x.Name == "count").Subst.Lengths[0]!.Value);
        Assert.Contains(composed.Single(static x => x.Name == "first").Subst.Origins.ToArray().Concat(composed.Single(static x => x.Name == "first").Subst.Inputs.ToArray()), static x => x is not null);
        Assert.Equal(["i32", "bool"], composed.Where(static x => x.Subst.Target.Declaration is FunctionKoto { IsRequirement: true }).Select(static x => x.Subst.Self!.Name));
        Assert.Equal("Box", composed.Single(static x => x.Name == "get").Subst.Declaring!.Name);
        Assert.Same(BoundType.I32, composed.Single(static x => x.Name == "get").Subst.Declaring!.Components[0]);
    }

    // DECISIONS R5-M61 (F package 4.5): every requirement call in a generic body, in the instance a closed call of that body makes, resolves
    // under Witness to a Body, Builtin or Storage target, over every such call of the generic library and the milestones and over
    // WitnessSource's kinds; a bound Contract reference is the substitution's declaring Type. The corpus's string, view and wrapping
    // integer format calls resolve to bodies since R8-S2d1 (114 of them: Builtin 173 -> 59, Body 38 -> 152), and the integers' up to 64
    // bits since R8-S2b1 (56 more: Builtin 59 -> 3, Body 152 -> 208), and u128's since R8-S2b2 (Builtin 3 -> 2, Body 208 -> 209); Array's
    // `first` and `last` call `tryGet` (G1135: Body 209 -> 211, `index.tryResolve` in the instances `tryGet<i32>` and
    // `tryGet<FromEnd<i32>>`). The 3 left are a tuple's Comparable (Milestone25), f64's Equatable in Milestone25's contractEqual
    // (G776-a: its specialization no longer calls `equals` as a member, Builtin 2 -> 3) and Wrapping<u8>'s Equatable (Milestone31).
    [Fact]
    public void WitnessResolvesEveryRequirementCallOfTheCorpus()
    {
        var problems = new List<string>();
        var corpus = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var name in HirColumnsTest.Corpus.Select(static x => x.Data))
        {
            var (c, module) = SemanticSnapshotTest.Bind(name);
            WitnessProblems(c, module, name, corpus, problems);
        }

        var direct = MinimalEmissionTest.Analyze(WitnessSource);
        Assert.True(direct.Binding.Result.IsComplete, MinimalEmissionTest.Describe(direct));
        var kinds = new SortedDictionary<string, int>(StringComparer.Ordinal);
        WitnessProblems(direct, direct.Kotonoha, "direct", kinds, problems);
        var counts = $"corpus {Counts(corpus)}; direct {Counts(kinds)}";
        Assert.True(
            problems.Count == 0 && counts == "corpus Body 211, Builtin 3, Storage 1, bound 3; direct Body 11, Builtin 2, Storage 3, bound 3",
            $"{counts}; {problems.Count} differ:\n{string.Join('\n', problems.Take(40))}");

        static string Counts(SortedDictionary<string, int> kinds) => string.Join(", ", kinds.Select(static x => $"{x.Key} {x.Value}"));
    }

    // DECISIONS R5-M61, R7-M54 (R7-T1 c4): Witness reads no storage verdict, so a Self whose storage nests past Binding's depth bound has
    // what its conformance gives, here none: Wrap^80<i32> is no Shape, so area's target is None, the whole target default (Cause Formed,
    // so no refused resolution), not an exception (fail-before: the Limit target, before any conformance was read); a conforming deep Self has its witness's Body (ARejectedSelfRunsItsWitness's deep rows). A
    // caller's instance that makes a requirement call's Self nest past it composes the substitution, its Self formed (Compose reads no
    // storage, PLAN G396 (3)), so no substitution without its Self reaches Witness (PLAN G157 (4)).
    [Fact]
    public void ADeepSelfHasNoLimitTarget()
    {
        var c = MinimalEmissionTest.Analyze(WitnessSource);
        var (inner, outer) = RequirementCalls(c, c.Kotonoha).First(static x => x.Inner.Target.Name == "area");
        var wrap = KotoTree.Walk(c.Kotonoha.RootKoto).Select(static x => x.TypeOf()).First(static x => x is { Symbol.Name: "Wrap" })!;
        var deep = BoundType.I32;
        for (var i = 0; i < 80; i++)
        {
            deep = c.Types.Intern(wrap.Kind, wrap.Symbol, wrap.Semantics, [deep]);
        }

        var call = new CallPlan();
        call.Set(c.Binding, inner.Target, inner.ReturnType, null, [], [], conformingType: deep);
        Assert.Equal(default, c.Types.Witness(inner.Target, inner.RequirementContract, c.Types.SubstOf(call)!, null));
        Assert.Equal(RequirementTargetKind.Body, Witness(c, (inner, outer)).Kind);

        // Option^40<i32> (storage height 80) is past the bound whatever Witness prepared before it (Wrap^80, whose Type argument no Field
        // stores, is refused too; DECISIONS BU-11, BU-12): the caller's instance composes the substitution, its Self formed.
        var option = KotoTree.Walk(c.Kotonoha.RootKoto).Select(static x => x.TypeOf()).First(static x => x is { Symbol.Name: "Option" })!;
        var nested = BoundType.I32;
        for (var i = 0; i < 40; i++)
        {
            nested = c.Types.Intern(option.Kind, option.Symbol, option.Semantics, [nested]);
        }

        var instance = new CallPlan();
        instance.Set(c.Binding, outer.Target, outer.ReturnType, null, [], [nested]);
        var composed = c.Types.Compose(c.Types.SubstOf(inner)!, c.Types.SubstOf(instance)!);
        Assert.Equal(TyCtxt.Formation.Formed, composed.Cause);
        Assert.Same(nested, composed.Subst!.Self);
    }

    // DECISIONS BU-36, BU-12: storage preparation's verdict depends on the Type alone. A part Binding did not bind ends no storage walk,
    // so a limit beyond it is met whichever Type is queried first (X reaches T's B^64 Field through a handle beside its failed Field: both
    // Limit), and a component reaching such a part is Rejected in every order, a Type storing a member of it too.
    [Theory]
    [InlineData(RetentionTest.Beyond, "T X", "Limit Limit")]
    [InlineData(RetentionTest.Beyond, "X T", "Limit Limit")]
    [InlineData(RejectedRing, "X Y Z", "Rejected Rejected Rejected")]
    [InlineData(RejectedRing, "Y X Z", "Rejected Rejected Rejected")]
    [InlineData(RejectedRing, "Z Y X", "Rejected Rejected Rejected")]
    public void AStorageVerdictDependsOnTheTypeAlone(string source, string order, string expected)
    {
        var c = MirBuildTest.Compile(source.Replace("MISSING", "Missing", StringComparison.Ordinal));
        Assert.False(c.Binding.Result.IsComplete);
        var types = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<StructKoto>().Select(static x => x.SymbolOf()!).ToDictionary(static x => x.Name, static x => x.Type!);
        Assert.Equal(expected, string.Join(' ', order.Split(' ').Select(x => c.Binding.PrepareTypeStorage(types[x]))));
    }

    // DECISIONS BU-36: TyCtxt.Instance gives the instance a substitution makes with its storage verdict, and Apply is that instance by the
    // substitution alone, Formed whatever its storage: f's `uniq/T` at Bad<i32>, whose Field m did not bind, is Rejected, at B^65<i32> a
    // Limit, and under f's identity the declared Type, Prepared.
    [Fact]
    public void AnInstanceCarriesItsStorageVerdict()
    {
        var c = MirBuildTest.Compile(RetentionTest.Box + "struct Bad<T>\n    public var v: T\n    public var m: Missing\nfunc f<T>(b: uniq/T) => ()\n" +
            $"func g(b: uniq/Bad<i32>, t: uniq/{RetentionTest.Box65})\n    f(b)\n    f(t)\n");
        var calls = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Select(static x => x.CallOf()!).Where(static x => x.Target.Name == "f").ToArray();
        var declared = ((FunctionKoto)calls[0].Target.Declaration).Parameters[0].Type.TypeOf()!;
        var outcomes = calls.Select(x => c.Types.Instance(declared, c.Types.SubstOf(x)!)).ToArray();
        Assert.Equal([Binding.StorageOutcome.Rejected, Binding.StorageOutcome.Limit], outcomes.Select(static x => x.Storage));
        Assert.Equal(["Bad", "B"], outcomes.Select(static x => x.Type!.Components[0].Symbol!.Name));
        Assert.Equal(outcomes.Select(static x => (x.Type, TyCtxt.Formation.Formed)), calls.Select(x => c.Types.Apply(declared, c.Types.SubstOf(x)!)));
        var identity = c.Types.Instance(declared, c.Types.Identity(calls[0].Target.Declaration));
        Assert.Same(declared, identity.Type);
        Assert.Equal(Binding.StorageOutcome.Prepared, identity.Storage);
    }

    // DECISIONS R4-M139: ReadableInstance is the instance every reader of one reads, Instance then Readable: f's `uniq/T` at i32 (Prepared)
    // and at Bad<i32> beside its failed Field (Rejected) is the instance, at B^65<i32> (a Limit, which has an instance) null; Rejected
    // storage in a complete compilation, a broken contract, throws (a call there at the failed compilation's Bad<i32>). A warm rebind reads
    // the same instances and allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AReadableInstanceIsTheInstanceReadablePasses()
    {
        var c = MirBuildTest.Compile(RetentionTest.Box + "struct Bad<T>\n    public var v: T\n    public var m: Missing\nfunc f<T>(b: uniq/T) => ()\n" +
            $"func g(i: uniq/i32, b: uniq/Bad<i32>, t: uniq/{RetentionTest.Box65})\n    f(i)\n    f(b)\n    f(t)\n");
        var calls = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Select(static x => x.CallOf()!).Where(static x => x.Target.Name == "f").ToArray();
        var declared = ((FunctionKoto)calls[0].Target.Declaration).Parameters[0].Type.TypeOf()!;
        var outcomes = calls.Select(x => c.Types.Instance(declared, c.Types.SubstOf(x)!)).ToArray();
        var instances = calls.Select(x => c.Types.ReadableInstance(declared, c.Types.SubstOf(x)!)).ToArray();
        Assert.Equal([Binding.StorageOutcome.Prepared, Binding.StorageOutcome.Rejected, Binding.StorageOutcome.Limit], outcomes.Select(static x => x.Storage));
        Assert.Equal([outcomes[0].Type, outcomes[1].Type, null], instances);
        Assert.NotNull(outcomes[2].Type);
        var changed = 0;
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Binding.Bind(BindingMode.Final);
            for (var i = 0; i < calls.Length; i++)
            {
                changed += ReferenceEquals(c.Types.ReadableInstance(declared, c.Types.SubstOf(calls[i])!), instances[i]) ? 0 : 1;
            }
        }));
        Assert.Equal(0, changed);

        var complete = MirBuildTest.Compile("func f<T>(b: uniq/T) => ()\nfunc g(i: uniq/i32) => f(i)\n");
        Assert.True(complete.Binding.Result.IsComplete);
        var target = KotoTree.Walk(complete.Kotonoha.RootKoto).OfType<InvocationKoto>().Select(static x => x.CallOf()!).Single(static x => x.Target.Name == "f");
        var call = new CallPlan();
        call.Set(complete.Binding, target.Target, target.ReturnType, null, [], [c.Types.SubstOf(calls[1])!.Types[0]]);
        var parameter = ((FunctionKoto)target.Target.Declaration).Parameters[0].Type.TypeOf()!;
        Assert.Throws<InvalidOperationException>(() => complete.Types.ReadableInstance(parameter, complete.Types.SubstOf(call)!));
    }

    // DECISIONS BU-36, R4-M139: a reader of an instance's storage may read Prepared and Rejected storage, not a Limit (Binding.Readable,
    // which retention, A4 and MirCases read through TyCtxt.ReadableInstance); Rejected rests on a Binding failure, so in a complete
    // compilation it is a broken contract (the guard, as the collector's Derived): an incomplete compilation reads it, a complete one throws.
    [Fact]
    public void ARejectedVerdictIsReadableOnlyBesideABindingFailure()
    {
        var failed = MirBuildTest.Compile("struct Bad\n    public var m: Missing\nfunc f(b: ref/Bad) => ()\n");
        var complete = MirBuildTest.Compile("struct Good\n    public var m: i32\nfunc f(b: ref/Good) => ()\n");
        Assert.False(failed.Binding.Result.IsComplete);
        Assert.True(complete.Binding.Result.IsComplete);
        Assert.Equal([true, true, false], new[] { Binding.StorageOutcome.Prepared, Binding.StorageOutcome.Rejected, Binding.StorageOutcome.Limit }.Select(x => failed.Binding.Readable(BoundType.I32, x)));
        Assert.Equal([true, false], new[] { Binding.StorageOutcome.Prepared, Binding.StorageOutcome.Limit }.Select(x => complete.Binding.Readable(BoundType.I32, x)));
        Assert.Throws<InvalidOperationException>(() => complete.Binding.Readable(BoundType.I32, Binding.StorageOutcome.Rejected));
    }

    // SPEC 23.1.2 (DECISIONS BU-38, R7-M54 (R7-T1 c4)): what a requirement call runs does not depend on its Self's storage, so a conforming
    // Self whose storage is Rejected, nests past the bound beside its failed Field, or nests past it alone has its witness's target
    // (fail-before: Limit for the two deep rows, which the collector published as a gap).
    [Theory]
    [InlineData("Missing", false, "Body")]
    [InlineData("Missing", true, "Body")]
    [InlineData("i32", true, "Body")]
    public void ARejectedSelfRunsItsWitness(string field, bool deep, string expected)
    {
        var c = MirBuildTest.Compile(RetentionTest.Box + $"contract Zero\n    func zero() -> i32\nstruct Bad\n    Self is Zero\n    public var m: {field}\n" +
            (deep ? $"    public var big: {RetentionTest.Box64}\n" : string.Empty) + "    public func zero() -> i32 => 1\n");
        var zero = c.Kotonoha.RootKoto.NestedDeclarationContainers.OfType<ContractKoto>().Single().SymbolOf()!;
        var bad = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<StructKoto>().Single(static x => x.Name == "Bad").SymbolOf()!.Type!;
        var requirement = zero.Contract!.OwnRequirement()!;
        Assert.Equal(expected, c.Types.Witness(requirement, zero, c.Types.Synthesized(requirement, bad, zero.Type!), null).Kind.ToString());
    }

    // DECISIONS R7-M54 (PLAN G396 (3)): Compose forms a substitution by the substitution alone, whatever its parts' storage in the outer
    // instance: inner's call in outer's instance at Bad<i32>, whose Field m did not bind, beside Bad<i64> or a Type argument nested past
    // Binding's storage bound, in either order, is Formed with the outer instance's Types: a null Subst is only a part that does not form,
    // with its cause (Formation).
    [Theory]
    [InlineData("i32", "i32")]
    [InlineData("Bad<i32>", "i32")]
    [InlineData("Bad<i32>", "Bad<i64>")]
    [InlineData("Bad<i32>", RetentionTest.Box65)]
    [InlineData(RetentionTest.Box65, "Bad<i32>")]
    public void ComposeFormsWhateverItsPartsStorage(string first, string second)
    {
        var c = MirBuildTest.Compile(RetentionTest.Box + "struct Bad<T>\n    public var v: T\n    public var m: Missing\nfunc inner<A, B>(a: uniq/A, b: uniq/B) => ()\n" +
            $"func outer<A, B>(a: uniq/A, b: uniq/B) => inner(a, b)\nfunc g(x: uniq/{first}, y: uniq/{second}) => outer(x, y)\n");
        var calls = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Select(static x => x.CallOf()!).ToDictionary(static x => x.Target.Name);
        var (subst, cause) = c.Types.Compose(c.Types.SubstOf(calls["inner"])!, c.Types.SubstOf(calls["outer"])!);
        Assert.Equal(TyCtxt.Formation.Formed, cause);
        Assert.Equal(c.Types.SubstOf(calls["outer"])!.Types.ToArray(), subst!.Types.ToArray());
    }

    // SPEC 8.6.1 (DECISIONS R5-M126; PLAN G157 (16)): the Contract that declares a requirement, also of the built-in target a requirement
    // call on a built-in Type runs, which keeps its requirement's scope: Equatable's equals, Comparable's compare and Addable's added on i32.
    [Fact]
    public void ContractDeclaresARequirementAndItsBuiltinTarget()
    {
        var c = MinimalEmissionTest.Analyze("let x = 1\n");
        foreach (var id in new[] { KimiDeclarationId.Equatable, KimiDeclarationId.Comparable, KimiDeclarationId.Addable })
        {
            var contract = c.Library.GetSymbol(id)!;
            var requirement = contract.Contract!.OwnRequirement()!;
            var target = c.Types.Witness(requirement, contract, c.Types.Synthesized(requirement, BoundType.I32, contract.Type!), null);
            Assert.Equal(RequirementTargetKind.Builtin, target.Kind);
            Assert.NotSame(requirement, target.Target);
            Assert.Same(contract, TyCtxt.Contract(requirement));
            Assert.Same(contract, TyCtxt.Contract(target.Target!));
        }
    }

    // SPEC 8.11.3, 8.13.3, 22.1 (PLAN G589, G663): a requirement on a Type of the library witness table runs the group's function at the
    // Self's arguments, the instance A4 is given (Witness's Body without its Origins, MirEffects' Require), and that closed instance
    // selects the specialization its key names, the original elsewhere: IntegerPosition's tryResolve at i32 and isize its
    // specializations, at u8 its original (fail-before: no target); each one's Original is the target, whose call contract and ABI it
    // keeps (R8-S2b2; fail-before: no Original).
    [Theory]
    [InlineData("i32", true)]
    [InlineData("isize", true)]
    [InlineData("u8", false)]
    public void AWitnessOfTheTableIsGivenItsInstance(string name, bool specialized)
    {
        var c = MinimalEmissionTest.Analyze("let x = 1\n");
        var self = BoundType.Primitives[name];
        var contract = c.Library.GetSymbol(KimiDeclarationId.Position)!;
        var requirement = contract.Contract!.OwnRequirement()!;
        var runs = c.Types.Witness(requirement, contract, c.Types.Synthesized(requirement, self, contract.Type!), null);
        Assert.Equal(RequirementTargetKind.Body, runs.Kind);
        var target = runs.Target!;
        var instance = c.Types.Synthesized(target, runs.Subst!.Declaring, runs.Subst.Types, runs.Subst.Lengths);
        Assert.Equal([self], instance.Types);
        var selected = c.Types.Implementation(target, instance, 0)!;
        Assert.Equal(specialized, selected.Declaration is FunctionKoto { IsSpecialization: true });
        Assert.Equal(specialized, !ReferenceEquals(selected, target));
        Assert.Same(target, c.Types.Original(selected)); // SPEC 8.13.2: the call contract and ABI a specialization's body keeps (R8-S2b2).
        Assert.Null(c.Types.Implementation(target, instance, 1));
    }

    // SPEC 8.6.3 (PLAN G663): the instance A4 is given for a conformance witness (Witness's Body without its Origins, MirEffects' Require)
    // under a requirement call's substitution is the witness under its declaring instance at the call's own Types and lengths by
    // position, a renamed or reordered slot and a length slot included, as Witness's Conformance binds them; a requirement no call names
    // (Synthesized on the Self) leaves the witness's own slots unbound (fail-before: the instance bound none for any call). A call in a
    // view (InView) is the call at the view's Self and reference with its own Types and lengths and no Origins, which A4 never reads, so
    // the same Subst whatever Origins the call has.
    [Theory]
    [InlineData("f<T, U>(self: ref/Self, x: T, y: U)", "f<U, T>(self: ref/Self, x: U, y: T)", "1, true")]
    [InlineData("f<T, length N>(self: ref/Self, x: [N of T])", "f<U, length M>(self: ref/Self, x: [M of U])", "[1, 2]")]
    public void AConformanceWitnessIsGivenItsCallsOwnTypesAndLengths(string requirement, string witness, string arguments)
    {
        var c = MinimalEmissionTest.Analyze(
            $"contract Gen\n    func {requirement} -> bool\nstruct S\n    Self is Gen\n    public init() => ()\n    public func {witness} -> bool => true\n" +
            $"func use<C>(c: ref/C) -> bool\n    C is Gen\n    return c.f({arguments})\nlet s = S.init()\n_ = use(s)\n");
        var call = Assert.Single(RequirementCalls(c, c.Kotonoha));
        var subst = Composed(c, call)!;
        var body = Witness(c, call);
        Assert.Equal(RequirementTargetKind.Body, body.Kind);
        Assert.Equal(2, subst.Types.Length);
        Assert.True(body.Subst!.Types.SequenceEqual(subst.Types) && body.Subst.Lengths.SequenceEqual(subst.Lengths));
        var requires = call.Inner.Target;
        var unnamed = c.Types.Witness(requires, call.Inner.RequirementContract, c.Types.Synthesized(requires, subst.Self!, TyCtxt.Contract(requires).Type!), null);
        Assert.Equal(RequirementTargetKind.Body, unnamed.Kind);
        Assert.Same(body.Target, unnamed.Target);
        Assert.Same(body.Subst.Declaring, unnamed.Subst!.Declaring);
        Assert.Equal(0, unnamed.Subst.Types.Length);
        var view = c.Types.InView(subst, subst.Self!, subst.Declaring);
        Assert.True(ReferenceEquals(view.Self, subst.Self) && view.Types.SequenceEqual(subst.Types) && view.Origins.IsEmpty && view.Inputs.IsEmpty);
        Assert.Same(view, c.Types.InView(view, subst.Self!, subst.Declaring));
        var moved = c.Types.InView(subst, BoundType.I32, null);
        Assert.True(ReferenceEquals(moved.Self, BoundType.I32) && moved.Types.SequenceEqual(subst.Types) && moved.Lengths.SequenceEqual(subst.Lengths));
    }

    // SPEC 4.9.6, 8.8.2, 8.11.3 (R7-P5b N1): Witness's key holds the reading definition's premise scope only for a Self with an abstract
    // part: one Subst on Box<A>, A inside's, runs Box's equals under inside's `A is Equatable` (Body) and nothing under outside's premises
    // (None); Box<i32> runs it in inside, in outside and at the library root, one memoized answer. Labelled from SPEC before any run.
    [Fact]
    public void AWitnessKeyHoldsThePremiseScopeOnlyForAnAbstractPart()
    {
        var c = MinimalEmissionTest.Analyze(
            "struct Box<A>\n    Self is Equatable when A is Equatable\n        public func equals(self: ref/Self, other: ref/Self) -> bool => true\n    public init() => ()\n" +
            "func inside<A>(b: Box<A>, k: Box<i32>)\n    A is Equatable\n    _ = 1\nfunc outside<A>(b: Box<A>)\n    _ = 1\n");
        var functions = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Where(static x => x.Name is "inside" or "outside").ToDictionary(static x => x.Name);
        var contract = c.Library.GetSymbol(KimiDeclarationId.Equatable)!;
        var requirement = contract.Contract!.OwnRequirement()!;
        var open = functions["inside"].Parameters[0].Type.TypeOf()!;
        var closed = functions["inside"].Parameters[1].Type.TypeOf()!;
        Assert.Equal(RequirementTargetKind.Body, At(open, "inside").Kind);
        Assert.Equal(RequirementTargetKind.None, At(open, "outside").Kind);
        var answers = new[] { At(closed, "inside"), At(closed, "outside"), At(closed, null) };
        Assert.All(answers, x => Assert.Equal(RequirementTargetKind.Body, x.Kind));
        Assert.All(answers, x => Assert.Equal(answers[0], x));
        Assert.All(answers, x => Assert.Same(answers[0].Subst, x.Subst));

        RequirementTarget At(BoundType self, string? definition)
            => c.Types.Witness(requirement, contract, c.Types.Synthesized(requirement, self, contract.Type!), definition is null ? null : functions[definition].SymbolOf());
    }

    // PLAN G157 (5) (R7-P5b): TyCtxt is the compilation's and outlives an edit, which discards Witness's memo with the other views
    // (Binding's ResetViews): Box runs its equals (Body), and after an edit that removes its conformance the same TyCtxt answers None for
    // the edited Box. Labelled from SPEC 8.6.1 before any run.
    [Fact]
    public void AnEditDiscardsTheWitnessMemo()
    {
        const string Conforming = "struct Box\n    Self is Equatable\n    public init() => ()\n    public func equals(self: ref/Self, other: ref/Self) -> bool => true\nfunc use(b: Box) => ()\n";
        var c = MinimalEmissionTest.Analyze(Conforming);
        var types = c.Types;
        var contract = c.Library.GetSymbol(KimiDeclarationId.Equatable)!;
        var requirement = contract.Contract!.OwnRequirement()!;
        Assert.Equal(RequirementTargetKind.Body, At().Kind);
        c.Kotonoha.RootKoto.Clear();
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, Conforming.Replace("    Self is Equatable\n", string.Empty, StringComparison.Ordinal));
        c.Binding.Bind(BindingMode.Final);
        Assert.Same(types, c.Types);
        Assert.Equal(RequirementTargetKind.None, At().Kind);

        RequirementTarget At()
        {
            var box = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "use").Parameters[0].Type.TypeOf()!;
            return c.Types.Witness(requirement, contract, c.Types.Synthesized(requirement, box, contract.Type!), null);
        }
    }

    // SPEC 6.5.1, 23.1.2 (G242; DECISIONS BU-37): an enum instance publishes each Case its substitution forms, and a Case whose payload
    // does not substitute is null, as a Field is: E's B and C beside its failed A, and G<i32>'s B, whose growth the storage verdict then
    // meets beside the failed A (fail-before: no Case published, and G<i32> Rejected).
    [Fact]
    public void AnEnumPublishesTheCasesItSubstitutes()
    {
        var c = MirBuildTest.Compile("enum E\n    A(Missing)\n    B(i32)\n    C\nenum G<T>\n    A(Missing)\n    B(rc/G<(T, T)>)\nfunc f(e: E, g: G<i32>) => ()\n");
        Assert.False(c.Binding.Result.IsComplete);
        var parameters = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "f").Parameters.Select(static x => x.Type.TypeOf()!).ToArray();
        Assert.Equal([null, c.Types.Tuple([BoundType.I32]), c.Types.Tuple([])], AdtDef.CaseTypes(c.Types, parameters[0]) ?? []);
        Assert.Equal([Binding.StorageOutcome.Rejected, Binding.StorageOutcome.Limit], parameters.Select(c.Binding.PrepareTypeStorage));
    }

    // A warm rebind keeps every call's substitution the same object and looking it up allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmSubstitutionsAreTheSameAndAllocateNothing()
    {
        var c = SemanticSnapshotTest.Bind("milestone/Milestone34").Compilation;
        var calls = KotoTree.Walk(c.Kotonoha.RootKoto).Concat(KotoTree.Walk(c.Library.Kotonoha.RootKoto))
            .SelectMany(x => Enumerable.Range(0, 8).Select(i => (Node: x, Ordinal: i)).TakeWhile(y => CallPlan.Of(c.Binding, y.Node, y.Ordinal) is not null))
            .Where(x => c.Types.SubstOf(CallPlan.Of(c.Binding, x.Node, x.Ordinal)!) is not null).ToArray();
        var substs = calls.Select(x => c.Types.SubstOf(CallPlan.Of(c.Binding, x.Node, x.Ordinal)!)).ToArray();
        var changed = 0;
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Binding.Bind(BindingMode.Final);
            for (var i = 0; i < calls.Length; i++)
            {
                changed += ReferenceEquals(c.Types.SubstOf(CallPlan.Of(c.Binding, calls[i].Node, calls[i].Ordinal)!), substs[i]) ? 0 : 1;
            }
        }));
        Assert.Equal(0, changed);
        Assert.Contains(substs, static x => x is { IsIdentity: false, Types.Length: > 0 });
    }

    // A warm rebind composes each call in a default to the same substitution and allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmComposeIsTheSameAndAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze(ComposeSource);
        var pairs = NestedCalls(c.Binding, c.Kotonoha).ToArray();
        var composed = pairs.Select(x => c.Types.Compose(c.Types.SubstOf(x.Inner)!, c.Types.SubstOf(x.Outer)!).Subst).ToArray();
        var changed = 0;
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Binding.Bind(BindingMode.Final);
            for (var i = 0; i < pairs.Length; i++)
            {
                changed += ReferenceEquals(c.Types.Compose(c.Types.SubstOf(pairs[i].Inner)!, c.Types.SubstOf(pairs[i].Outer)!).Subst, composed[i]) ? 0 : 1;
            }
        }));
        Assert.Equal(0, changed);
        Assert.Contains(composed, static x => x is { IsIdentity: false, Types.Length: > 0 });
    }

    // A warm rebind gives each requirement call of WitnessSource the same target in each instance, and asking again allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmWitnessIsTheSameAndAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze(WitnessSource);
        var calls = RequirementCalls(c, c.Kotonoha).ToArray();
        var targets = calls.Select(x => Witness(c, x)).ToArray();
        var changed = 0;
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Binding.Bind(BindingMode.Final);
            for (var i = 0; i < calls.Length; i++)
            {
                changed += Witness(c, calls[i]) == targets[i] ? 0 : 1;
            }
        }));
        Assert.Equal(0, changed);
        Assert.Contains(targets, static x => x.Kind == RequirementTargetKind.Body);
    }

    // Erasing the Types of a program's MIR locals again allocates nothing: each erased form is memoized like the interning.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmEraseAllocatesNothing()
    {
        var c = SemanticSnapshotTest.Bind("milestone/Milestone34").Compilation;
        var locals = c.BuildMir().Bodies.ToArray().SelectMany(static x => x.Locals.ToArray()).Select(static x => x.Type!).Distinct().ToArray();
        var erased = locals.Select(c.Types.Erase).ToArray();
        var changed = 0;
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            for (var i = 0; i < locals.Length; i++)
            {
                changed += ReferenceEquals(c.Types.Erase(locals[i]), erased[i]) ? 0 : 1;
            }
        }));
        Assert.Equal(0, changed);
        Assert.Contains(locals, x => !ReferenceEquals(c.Types.Erase(x), x));
    }

    // A warm rebind keeps whether each MIR key symbol is instantiated, and asking again allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmInstantiatesIsTheSameAndAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze(InstanceSource);
        var definitions = c.BuildMir().Bodies.ToArray().Where(static x => x.Key.Decl is not null).Select(static x => x.Key.Decl!).ToArray();
        var instantiates = definitions.Select(c.Types.Instantiates).ToArray();
        var changed = 0;
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Binding.Bind(BindingMode.Final);
            for (var i = 0; i < definitions.Length; i++)
            {
                changed += c.Types.Instantiates(definitions[i]) == instantiates[i] ? 0 : 1;
            }
        }));
        Assert.Equal(0, changed);
        Assert.Contains(true, instantiates);
        Assert.Contains(false, instantiates);
    }

    // A struct's Fields and base, or an enum's Case payloads.
    // The Type context and the parameter Types of `probe` in RawShapeSource with a `uniq` remainder and inline raw parts: x, y, z, then a
    // Block, a Cell, Views under x and `static`, a remainder under y and the inline parts; then (9-21) raw layers Type arguments supply,
    // and (22) one below the holder.
    private static (TyCtxt Types, BoundType[] Parameters) StoredLayers()
    {
        var c = MinimalEmissionTest.Analyze(RawShapeSource + "struct R<E> {source}\n    internal let at: raw/E during source\n    internal let loan: Loan<uniq/E during source>\n" +
            "struct Parts\n    internal var pair: (raw/u8 during self, isize)\n    internal var cells: [2 of raw/u8 during self]\n" +
            "    public init()\n        self.pair = (null, 0)\n        self.cells = [null, null]\n" +
            "struct Local<T>\n    internal let value: T\nstruct Holder<s/T>\n    internal let value: s/T\n" +
            "struct Anchored<s/T, U> {a}\n    s is owner or ref or uniq or raw\n    internal let anchor: ref/i32 during a\n" +
            "    internal let other: s/U during a\nstruct InTuple<T>\n    internal let v: (T, i32)\nstruct InArray<T>\n    internal let cells: [2 of T]\n" +
            "enum Choice<s/T, U> {a}\n    s is owner or ref or raw\n    First(s/T)\n    Second(s/U during a)\n" +
            "func probe<T>(x: ref/i32, y: uniq/i32, z: i32, b: Block, c: Cell, v: View<i32> during x, s: View<i32> during static, r: R<i32> during y, " +
            "p: Parts, ta: Local<raw/i32>, tb: Local<raw/i32 during y>, tc: Local<raw/i32 during static>, td: Local<raw/T>, te: Holder<raw/i32>, " +
            "tf: Anchored<raw/i32, i64> during x, tg: Choice<raw/i32, i64> during x, th: InTuple<raw/u8>, ti: InArray<raw/u8>, " +
            "tj: Local<(raw/u8 during y, isize)>, tk: Option<raw/u8 during y>, tn: Local<i32>, to: Holder<raw/i32 during y>, tp: Local<Local<raw/u8 during y>>) => ()\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var probe = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "probe");
        return (c.Types, probe.Parameters.Select(static x => x.Type.TypeOf()!).ToArray());
    }

    // The compilation and the parameter Types of a probe over 64 pair slots (each `is owner`) before a `ref or uniq` binder: r, q, then
    // Outer<…>.Inner (its raw Field stating Outer's slot) under that binder's uniq and ref cases, then H's (its own raw Field) likewise.
    private static (Compilation C, BoundType[] Parameters) WideLayers()
    {
        var pairs = string.Concat(Enumerable.Range(0, 64).Select(static i => $"p{i}/V{i}, "));
        var premises = string.Concat(Enumerable.Range(0, 64).Select(static i => $"    p{i} is owner\n"));
        var arguments = string.Concat(Enumerable.Repeat("i32, ", 64));
        var c = MinimalEmissionTest.Analyze($"struct Outer<{pairs}s/T> {{a}}\n{premises}    s is ref or uniq\n    internal let item: s/T during a\n" +
            "    internal struct Inner<U>\n        internal let u: U\n        internal var p: raw/i32 during a\n" +
            $"struct H<{pairs}s/T> {{a}}\n{premises}    s is ref or uniq\n    internal let item: s/T during a\n    internal var at: raw/i32 during a\n" +
            $"func probe(r: uniq/i32, q: ref/i32, iu: Outer<{arguments}uniq/i32 during r>.Inner<ref/i64 during q>, ir: Outer<{arguments}ref/i32 during q>.Inner<uniq/i64 during r>, " +
            $"hu: H<{arguments}uniq/i32 during r>, hr: H<{arguments}ref/i32 during q>) => ()\npublic func main() => ()\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var probe = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "probe");
        return (c, probe.Parameters.Select(static x => x.Type.TypeOf()!).ToArray());
    }

    // The Type context and the parameter Types of ConditionalSource's `probe`.
    private static (TyCtxt Types, BoundType[] Parameters) ConditionalLayers()
    {
        var c = MinimalEmissionTest.Analyze(ConditionalSource);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var probe = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "probe");
        return (c.Types, probe.Parameters.Select(static x => x.Type.TypeOf()!).ToArray());
    }

    // The compilation of KeptSource and the parameter Types of its `probe`: x, then Outer, Pair2, Opt2, Hold, OP, Box1 and Der under x's
    // Origin, Outer under none, then OuterE, OuterE2, OuterF, OuterT and OuterH under x's.
    private static (Compilation C, BoundType[] Parameters) KeptParts()
    {
        var c = MinimalEmissionTest.Analyze(KeptSource);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var probe = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "probe");
        return (c, probe.Parameters.Select(static x => x.Type.TypeOf()!).ToArray());
    }

    // A Type for a row: its diagnostic name, a raw layer's with its Origin's kind; "-" for none.
    private static string Shown(BoundType? type) => type switch
    {
        null => "-",
        { RawOrigin: { } raw } => $"{Binding.DiagnosticTypeName(type)} during {raw.Kind}",
        _ => Binding.DiagnosticTypeName(type),
    };

    // The raw layer an instance stores at a part and inline positions, as StoredStep reads it: its kept part (an enum Case's payload Tuple),
    // then per position a Tuple's component or a fixed array's element.
    private static BoundType KeptLayer(TyCtxt types, BoundType instance, int part, int[] positions)
    {
        var layer = types.StoredParts(instance, out _)[part]!;
        foreach (var position in positions)
        {
            layer = layer.Kind == BoundTypeKind.Tuple ? layer.Components[position] : layer.Components[0];
        }

        return layer;
    }

    // RawMembers of the raw layer an instance stores at a part and inline positions, by the key a Holdings read gives it.
    private static bool MembersAt(TyCtxt types, BoundType instance, int part, int[] positions, List<RawMember> members)
        => types.RawMembers(KeptLayer(types, instance, part, positions), TyCtxt.WrittenAt(instance, part, positions), instance, members);

    // The compilation of DeclaredSource and its `probe`'s parameter Types by name.
    private static (Compilation C, Dictionary<string, BoundType> Parameters) DeclaredLayers()
    {
        var c = MinimalEmissionTest.Analyze(DeclaredSource);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var probe = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "probe");
        return (c, probe.Parameters.ToDictionary(static x => x.InternalName, static x => x.Type.TypeOf()!));
    }

    // The struct and enum instances the MIR locals hold (the library's bodies' included: the library check's root range, PLAN G951 U2-T),
    // added to `instances`, and the names of those whose erased instance does not store the erasure of what they store.
    private static List<string> ErasedStoreProblems(Compilation c, List<BoundType> instances)
    {
        var types = c.Types;
        var seen = new HashSet<BoundType>();
        var problems = new List<string>();
        foreach (var body in new MirBuilder(c).Build(SyntaxTable.LibraryModule + 1).Bodies)
        {
            foreach (var local in body.Locals)
            {
                // A local of no Type (a failed slot or a temp derived from one, MirLocal; PLAN G1073) stores nothing to erase.
                if (local.Type is { } type)
                {
                    Visit(type);
                }
            }
        }

        return problems;

        void Visit(BoundType type)
        {
            if (!seen.Add(type))
            {
                return;
            }

            foreach (var part in type.Components)
            {
                Visit(part);
            }

            if (AdtDef.IsStruct(type) || AdtDef.IsEnum(type))
            {
                instances.Add(type);
                if (!Stored(types, type).Select(x => x is null ? null : types.Erase(x)).SequenceEqual(Stored(types, types.Erase(type))))
                {
                    problems.Add(TyCtxt.Name(type));
                }
            }
        }
    }

    private static IEnumerable<BoundType?> Stored(TyCtxt types, BoundType type) => AdtDef.IsStruct(type)
        ? Enumerable.Range(0, AdtDef.Count(type)).Select(x => AdtDef.FieldType(types, type, x)).Append(AdtDef.Base(types, type)) : AdtDef.CaseTypes(types, type) ?? [];

    // Each call in a default a call omits, with that call.
    private static IEnumerable<(CallPlan Inner, CallPlan Outer)> NestedCalls(Binding binding, Kotonoha module)
    {
        foreach (var node in KotoTree.Walk(module.RootKoto))
        {
            for (var i = 0; CallPlan.Of(binding, node, i) is { } outer; i++)
            {
                foreach (var omitted in outer.DefaultArguments.ToArray())
                {
                    foreach (var nested in KotoTree.Walk(DefaultDecl.Of(omitted).Expression))
                    {
                        for (var k = 0; CallPlan.Of(binding, nested, k) is { } inner; k++)
                        {
                            if (inner.Kind != CalleeKind.Value)
                            {
                                yield return (inner, outer);
                            }
                        }
                    }
                }
            }
        }
    }

    // Each nested call composes, and the composed substitution instantiates each declared Type of the callee as applying the call's and
    // then the context's substitution does.
    private static int ComposeProblems(Compilation c, Kotonoha module, List<string> problems)
    {
        var count = 0;
        foreach (var (inner, outer) in NestedCalls(c.Binding, module))
        {
            count++;
            var composed = c.Types.Compose(c.Types.SubstOf(inner)!, c.Types.SubstOf(outer)!).Subst;
            var where = $"{module.Compilation.Kotonoha.Name} {outer.Target.Name} > {inner.Target.Name}";
            foreach (var declared in ((FunctionKoto)inner.Target.Declaration).Parameters.Select(x => x.Type.TypeOf(c)).Append(inner.Target.Type))
            {
                var expected = declared is null ? null
                    : c.Types.Apply(declared, c.Types.SubstOf(inner)!).Type is { } once ? c.Types.Apply(once, c.Types.SubstOf(outer)!).Type : null;
                if (composed is null || !ReferenceEquals(declared is null ? null : c.Types.Apply(declared, composed).Type, expected))
                {
                    problems.Add($"{where} {declared}: {expected}");
                }
            }
        }

        return count;
    }

    private static void CheckInstantiations(Compilation c, Koto root)
    {
        var problems = new List<string>();

        // SPEC 6.4.4: a direct base call uses the slot's public contract, so the inputs other than the receiver and the result of the
        // implementation it runs, under its substitution, are the slot's under the call's instantiation.
        void Contract(Koto node, FunctionKoto implementation, FunctionKoto slot, Subst subst, CallPlan call)
        {
            var count = slot.Parameters.Count;
            if (implementation.Parameters.Count != count)
            {
                problems.Add($"{node.CodeContext.SourceDocument?.Path}:{node.Span.Start} contract arity");
                return;
            }

            for (var i = 0; i <= count; i++)
            {
                var own = i < count ? implementation.Parameters[i].Type.TypeOf() : subst.Target.Type;
                var contract = i < count ? slot.Parameters[i].Type.TypeOf() : call.Target.Type;
                if (i != call.Target.ReceiverIndex && (own is null || contract is null ||
                    c.Types.Apply(own, subst).Type is not { } applied || !ReferenceEquals(applied, c.Types.ReadableInstance(contract, c.Types.StorageSubst(call)))))
                {
                    problems.Add($"{node.CodeContext.SourceDocument?.Path}:{node.Span.Start} contract {(i < count ? $"slot {i}" : "result")}");
                }
            }
        }

        void Check(Koto node, BoundType? declared, Subst subst, CallPlan context)
        {
            var applied = declared is null ? null : c.Types.Apply(declared, subst).Type;
            var instance = declared is null ? null : Instance(c, declared, subst, context);
            if (!ReferenceEquals(applied, instance) ||
                (applied is { Kind: BoundTypeKind.Closure, Symbol.Declaration: FunctionKoto function } && function.ClosureOf() is { } closure &&
                    !ReferenceEquals(c.Binding.ClosureSignature(applied), c.Types.Apply(closure.Signature!, c.Types.SubstOfClosure(applied)!).Type)))
            {
                problems.Add($"{node.CodeContext.SourceDocument?.Path}:{node.Span.Start} {node.Akind} {declared}: {applied}");
            }
        }

        var calls = 0;
        foreach (var node in KotoTree.Walk(root))
        {
            for (var i = 0; CallPlan.Of(c.Binding, node, i) is { } call; i++)
            {
                if (c.Types.SubstOf(call) is { } subst && subst.Target.Declaration is FunctionKoto callee)
                {
                    calls++;
                    foreach (var parameter in callee.Parameters)
                    {
                        Check(node, parameter.Type.TypeOf(c), subst, call);
                    }

                    Check(node, subst.Target.Type, subst, call);
                    if (call.Target.Declaration is FunctionKoto slot && !ReferenceEquals(callee, slot))
                    {
                        Contract(node, callee, slot, subst, call);
                    }
                }
            }

            if (node.TypeOf(c) is { Kind: BoundTypeKind.FunctionItem, Symbol.Declaration: FunctionKoto function } item && c.Binding.FunctionItemContext(item) is { } context)
            {
                foreach (var parameter in function.Parameters)
                {
                    Check(node, parameter.Type.TypeOf(c), c.Types.SubstOfItem(item)!, context);
                }
            }
        }

        Assert.True(calls > 0 && problems.Count == 0, $"{calls} calls; {problems.Count} differ:\n{string.Join('\n', problems.Take(40))}");
    }

    // Binding's instantiation for a call's context. A requirement's, under its conforming Self and bound Contract reference, and a direct
    // base call's into an implementation other than the slot's original, of that implementation in the implementing Type, have no
    // CallPlan form: theirs is Binding's instantiation under the substitution once it names the
    // call's target and conforming Self (a base call's: the implementation, no Self, the implementing Type) and binds the call's Type and
    // length arguments and Origins. Any other call's is its CallPlan form.
    private static BoundType? Instance(Compilation c, BoundType declared, Subst subst, CallPlan context) => context switch
    {
        { Kind: CalleeKind.Requirement } when ReferenceEquals(subst.Target, context.Target) && ReferenceEquals(subst.Self, context.ConformingType) &&
            Binds(subst, context) => c.Types.ReadableInstance(declared, subst),
        { Kind: CalleeKind.Requirement } => null,
        { Kind: CalleeKind.Virtual, VirtualIsDirect: true, VirtualImplementation: { } body } when !ReferenceEquals(body, context.Target.Declaration)
            => ReferenceEquals(subst.Binder, body) && subst.Self is null && ReferenceEquals(subst.Declaring, context.VirtualImplementingType) &&
                Binds(subst, context) ? c.Types.ReadableInstance(declared, subst) : null,
        _ => c.Types.ReadableInstance(declared, c.Types.StorageSubst(context)),
    };

    // The substitution binds the call's Type and length arguments, declared Origins and input Origins.
    private static bool Binds(Subst subst, CallPlan call)
        => subst.Types.SequenceEqual(call.TypeArguments) && subst.Lengths.SequenceEqual(call.LengthArguments) &&
            subst.Origins.SequenceEqual(call.Origins) && subst.Inputs.SequenceEqual(call.InputOrigins);

    // Each requirement call in the body of a generic function, with each call of that function in a module whose instance closes the
    // requirement's Self (a call in a nested function or closure is its own binder's).
    private static IEnumerable<(CallPlan Inner, CallPlan Outer)> RequirementCalls(Compilation c, Kotonoha module)
    {
        var bodies = new Dictionary<FunctionKoto, CallPlan[]>();
        foreach (var node in KotoTree.Walk(module.RootKoto))
        {
            for (var i = 0; CallPlan.Of(c.Binding, node, i) is { } outer; i++)
            {
                if (outer.Kind != CalleeKind.Function || outer.Target.Declaration is not FunctionKoto callee || !c.Types.Instantiates(outer.Target))
                {
                    continue;
                }

                if (!bodies.TryGetValue(callee, out var inner))
                {
                    inner = KotoTree.Walk(callee).Where(x => ReferenceEquals(Binder(x), callee))
                        .SelectMany(x => Enumerable.Range(0, 8).Select(k => CallPlan.Of(c.Binding, x, k)).TakeWhile(static y => y is not null).OfType<CallPlan>())
                        .Where(static x => x.Kind == CalleeKind.Requirement).ToArray();
                    bodies.Add(callee, inner);
                }

                foreach (var call in inner)
                {
                    if (Composed(c, (call, outer))?.Self is { HasAbstractPart: false })
                    {
                        yield return (call, outer);
                    }
                }
            }
        }

        static FunctionKoto? Binder(Koto node) => node.Parent is not { } parent ? null : parent as FunctionKoto ?? Binder(parent);
    }

    // A requirement call's substitution in its caller's instance, and its target there.
    private static Subst? Composed(Compilation c, (CallPlan Inner, CallPlan Outer) call)
        => c.Types.Compose(c.Types.SubstOf(call.Inner)!, c.Types.SubstOf(call.Outer)!).Subst;

    private static RequirementTarget Witness(Compilation c, (CallPlan Inner, CallPlan Outer) call)
        => c.Types.Witness(call.Inner.Target, call.Inner.RequirementContract, Composed(c, call)!, null);

    // Counts each Witness of RequirementCalls by kind, which the caller pins (Refused and None occur for no closed Self of an accepted
    // program), and the bound Contract references, each the substitution's declaring Type. The views leave Binding's records and
    // the HIR version unchanged.
    private static void WitnessProblems(Compilation c, Kotonoha module, string label, IDictionary<string, int> kinds, List<string> problems)
    {
        var version = HirTables.Publish(c);
        var issues = c.Binding.Issues.Count;
        var calls = RequirementCalls(c, module).Select(x => (x.Inner, x.Outer, Subst: Composed(c, x)!, Target: Witness(c, x))).ToArray();
        if (HirTables.Publish(c) != version || c.Binding.Issues.Count != issues)
        {
            problems.Add($"{label}: a view changed Binding's state");
        }

        foreach (var (inner, outer, subst, target) in calls)
        {
            Count(target.Kind.ToString());
            var where = $"{label} {outer.Target.Name} > {inner.Target.Name} on {TyCtxt.Name(subst.Self!)}: {target.Kind}";
            if (inner.RequirementContract is { Type: { } bound } contract && !ReferenceEquals(bound.Symbol, contract))
            {
                Count("bound");
                if (!ReferenceEquals(subst.Declaring, c.Types.ReadableInstance(bound, c.Types.StorageSubst(outer))))
                {
                    problems.Add($"{where} bound reference");
                }
            }
        }

        void Count(string key) => kinds[key] = kinds.TryGetValue(key, out var count) ? count + 1 : 1;
    }

    // The rows of a program in FitsPrelude's frame (Z6-F2's and Revision 8's) that carry `mark`.
    private static string[] FitRows(string declarations, string body, Binding.LimitMarks mark)
    {
        var c = MinimalEmissionTest.Analyze(BorrowsPrelude + FitsPrelude + declarations.Replace("|", "\n", StringComparison.Ordinal) +
            $"func p(c: bool)\n    {body.Replace("|", "\n    ", StringComparison.Ordinal)}\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        return [.. BorrowRows(c).Where(x => x.Contains(mark.ToString(), StringComparison.Ordinal))];
    }

    // The program part's rows with an entry or a mark: the atom's binder (its first line) and kind, then each entry's referent and mode.
    private static string[] BorrowRows(Compilation c) => [.. c.Binding.ProgramBorrows
        .Where(static x => x.Value.Count > 0 || x.Value.Marks != default)
        .Select(static x => $"{x.Key.Binder?.ToString()?.Split('\n')[0].Trim()} {x.Key.Kind} {x.Key.Slot}: " +
            string.Join(", ", x.Value.Items.ToArray().Select(static m => $"{m.Referent} {(m.Exclusive ? "X" : "S")}")) + (x.Value.Marks != default ? $" {x.Value.Marks}" : string.Empty))
        .Order(StringComparer.Ordinal)];
}
