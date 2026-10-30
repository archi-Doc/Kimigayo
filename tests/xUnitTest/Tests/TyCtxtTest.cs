// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>The Type context (<c>Compilation.Types</c>, docs/dev/COMPILER_ARCHITECTURE.md, R2b): a substitution is interned, so a warm
/// lookup returns the same object and allocates nothing, and a call's, closure's or function item's substitution applied to a declared
/// Type is the instantiation Binding makes for it, over the Kimi library and every milestone program.</summary>
public sealed class TyCtxtTest
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
    private const string ClosureSource = "func identity<length N>(value: [N of i32]) -> [N of i32]\n    let action = func [] (inner: [N of i32]) -> [N of i32] => inner\n" +
        "    return action(value)\n_ = identity([1, 2])\n_ = identity([5])\n";

    // A generic function with a closure, called with two borrows of different Origins, and a nongeneric function with Origins.
    private const string EraseSource = "func keep<T>(value: T) -> T\n    T is Copy\n    let probe = func [] (x: T) -> T => x\n    return probe(value)\n" +
        "func host(a: ref/i32, b: ref/i32) -> i32\n    _ = keep(a)\n    _ = keep(b)\n    return 0\nlet x = 1\nlet y = 2\n_ = host(x@ref, y@ref)\n";

    // A raw Field and a Case payload stating `self`, and a raw Field stating a slot, held under a local's Origin and under `static`.
    private const string RawShapeSource = "struct Block\n    internal let data: raw/u8 during self\n    public init() => self.data = null\n" +
        "enum Cell\n    Some(raw/u8 during self)\n    None\n" +
        "struct View<T> {source}\n    internal let loan: Loan<ref/T during source>\n    internal let at: raw/T during source\n" +
        "    public init(target: ref/T during source)\n        self.at = target@follow@raw\n        self.loan = Loan.init(target)\n" +
        "func held(view: View<i32> during static) -> View<i32> during static => view\n" +
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

    // Nested definitions in a generic struct's getter (a function, a Closure and a function in it) and setter, in its method, and a Closure
    // in a static's initializer.
    private const string EnclosingSource = "struct Box<T>\n    T is Copy\n    public var value: T\n    public init(value: T) => self.value = value\n" +
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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var calls = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Select(static x => x.CallOf())
            .Where(static x => x is { Kind: CalleeKind.Virtual, VirtualIsDirect: true }).ToArray();
        Assert.Equal(direct, calls.Length);
        Assert.Equal(intoOverrides, calls.Count(static x => !ReferenceEquals(x!.VirtualImplementation, x.Target.Declaration)));
        CheckInstantiations(c, c.Kotonoha.RootKoto);
    }

    // SPEC 6.1.3, 8.1.3: a closure an instance closed is in that instance's substitution and has its signature; a closure no instance closed is
    // in the identity of the function enclosing it, which keeps every Type.
    [Fact]
    public void AClosureIsInItsInstancesSubstitution()
    {
        var c = MinimalEmissionTest.Analyze(ClosureSource);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var nodes = KotoTree.Walk(c.Kotonoha.RootKoto).ToArray();
        var function = nodes.OfType<FunctionKoto>().Single(static x => x.Name == "identity");
        var closure = nodes.OfType<FunctionKoto>().Single(static x => x.IsAnonymous).ClosureOf()!;
        var calls = nodes.OfType<InvocationKoto>().Where(static x => x.CallOf()?.Target.Name == "identity").Select(static x => x.CallOf()!).ToArray();
        var closed = calls.Select(x => c.Binding.InstantiateStorageType(closure.EnvironmentType!, x)!).ToArray();
        Assert.All(closed, static x => Assert.NotNull(x.ClosureContext));
        Assert.Equal(calls.Select(c.Types.SubstOf), closed.Select(c.Types.SubstOfClosure));
        Assert.NotSame(c.Types.SubstOf(calls[0]), c.Types.SubstOf(calls[1]));
        Assert.All(closed, x => Assert.Same(c.Binding.ClosureSignature(x), c.Types.Apply(closure.Signature, c.Types.SubstOfClosure(x)!)));
        var identity = c.Types.SubstOfClosure(closure.EnvironmentType!)!;
        Assert.Same(c.Types.Identity(function), identity);
        Assert.Same(closure.Signature, c.Types.Apply(closure.Signature, identity));
    }

    // SPEC 6.1.3, 8.1.3, PLAN G142: a closure's context is the whole substitution of the instance that closed it, its conforming Self included, so two
    // substitutions that differ only in Self close it twice. No source call gives a body with a closure a Self (a Contract declares no default
    // implementation, SPEC 8.6.1), so the test synthesizes the two calls.
    [Fact]
    public void AClosureContextKeepsTheConformingSelf()
    {
        var c = MinimalEmissionTest.Analyze(ClosureSource);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var nodes = KotoTree.Walk(c.Kotonoha.RootKoto).ToArray();
        var closure = nodes.OfType<FunctionKoto>().Single(static x => x.IsAnonymous).ClosureOf()!;
        var call = nodes.OfType<InvocationKoto>().Select(static x => x.CallOf()).First(static x => x?.Target.Name == "identity")!;
        var substs = new[] { BoundType.Boolean, BoundType.I32 }.Select(self => c.Types.SubstOf(Conforming(call, self))!).ToArray();
        var closed = substs.Select(x => c.Types.Apply(closure.EnvironmentType!, x)!).ToArray();
        Assert.Equal(substs, closed.Select(c.Types.SubstOfClosure));
        Assert.NotSame(closed[0], closed[1]);

        static CallPlan Conforming(CallPlan call, BoundType self)
        {
            var synthesized = new CallPlan();
            synthesized.Set(
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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var nodes = KotoTree.Walk(c.Kotonoha.RootKoto).ToArray();
        var call = nodes.OfType<InvocationKoto>().Select(static x => x.CallOf()).Single(static x => x is { Kind: CalleeKind.Function, Target.Name: "make" })!;
        var item = nodes.Select(static x => x.TypeOf()).First(static x => x is { Kind: BoundTypeKind.FunctionItem })!;
        var subst = c.Types.SubstOf(call)!;
        Assert.Equal(1, subst.Lengths.Length);
        Assert.Same(subst, c.Types.SubstOfItem(item));
        Assert.Same(subst, c.Types.Synthesized(call.Target, null, call.TypeArguments));
    }

    // SPEC 3.4.3, 13.5.5: selection follows a reference or object handle as a Deref and a pair layer, the original s/T or an application
    // s/U, as a PairLayer of the binder in scope, never a raw pointer or an owned value. SPEC 23.1: a Type is named as diagnostics spell it.
    [Fact]
    public void LayersAreReferencesHandlesAndPairLayers()
    {
        var c = MinimalEmissionTest.Analyze("func f<s/T>(value: s/T, count: s/i32) -> s/T\n    s is owner or uniq\n    T is Copy\n    _ = count\n    return value\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var types = c.Types;
        var function = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "f");
        var pair = function.Parameters[0].Type.TypeOf()!;
        var application = function.Parameters[1].Type.TypeOf()!;
        var binders = new List<PairBinder>();
        PairBinder.Collect(function, binders);
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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var p = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "probe").Parameters.Select(static x => x.Type.TypeOf()!).ToArray();
        Assert.Equal([RawStep.Static, RawStep.Shared, RawStep.Exclusive], [c.Types.RawStep(p[1], 0, []), c.Types.RawStep(p[2], 1, []), c.Types.RawStep(p[3], 1, [])]);
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
            Assert.True(types.RawMembers(p[at], part, [], members));
            rows.Add($"{types.RawStep(p[at], part, [])} [{string.Join(", ", members.Select(static x => x.Referent.Name + (x.Exclusive ? " excl" : string.Empty)))}]");
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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var p = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "probe").Parameters.Select(static x => x.Type.TypeOf()!).ToArray();
        Assert.Equal(
            [RawStep.Exclusive, RawStep.Shared, RawStep.Exclusive, RawStep.Shared, RawStep.Exclusive, RawStep.Shared],
            [c.Types.RawStep(p[3], 1, []), c.Types.RawStep(p[4], 1, []), c.Types.RawStep(p[5], 1, []), c.Types.RawStep(p[6], 1, []), c.Types.RawStep(p[7], 0, []), c.Types.RawStep(p[8], 0, [])]);
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
            Assert.True(types.RawMembers(p[at], part, [], members));
            rows.Add($"{types.RawStep(p[at], part, [])} [{string.Join(", ", members.Select(static x => x.Referent.Name + (x.Exclusive ? " excl" : string.Empty)))}]");
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
            if (types.RawStep(p[2], 1, []) != RawStep.Exclusive || types.RawStep(p[5], 1, []) != RawStep.Shared || !types.RawMembers(p[4], 1, [], members))
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
            steps += (int)types.RawStep(p[at], part, []);
            types.RawMembers(p[at], part, [], members);
        }

        var count = members.Count;
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            members.Clear();
            var again = 0;
            foreach (var (at, part) in ConditionalRows)
            {
                again += (int)types.RawStep(p[at], part, []);
                types.RawMembers(p[at], part, [], members);
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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var p = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "probe").Parameters.Select(static x => x.Type.TypeOf()!).ToArray();
        var members = new List<RawMember>();
        Assert.True(c.Types.RawMembers(p[1], 0, [], members));
        Assert.Empty(members);
        Assert.True(c.Types.RawMembers(p[2], 1, [], members) && c.Types.RawMembers(p[3], 1, [], members));
        Assert.Equal([new(0, BoundType.I32, false), new(0, BoundType.I32, true)], members);
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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var p = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "f").Parameters.Select(static x => x.Type.TypeOf()!).ToArray();
        var members = new List<RawMember>();
        Assert.True(c.Types.RawMembers(p[1], 0, [], members));
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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var nodes = KotoTree.Walk(c.Kotonoha.RootKoto).ToArray();
        var closure = Assert.Single(nodes.OfType<FunctionKoto>(), static x => x.IsAnonymous);
        var r = nodes.OfType<FieldKoto>().Single(static x => x.NameKoto.IdentifierName == "r").SymbolOf()!.Type!.Origin;
        BoundType[] layers =
        [
            closure.Parameters[1].Type.TypeOf()!,
            nodes.OfType<VariableKoto>().Single(static x => x.NameKoto.IdentifierName == "a").SymbolOf()!.Type!,
            nodes.OfType<VariableKoto>().Single(static x => x.NameKoto.IdentifierName == "b").SymbolOf()!.Type!,
        ];
        Assert.Same(r, layers[1].RawOrigin);
        Assert.Same(r, layers[2].RawOrigin);
        Assert.Contains(layers[0].RawOrigin!.Kind, new[] { OriginKind.Inference, OriginKind.Projection });
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
            [RawStep.Own, RawStep.Own, RawStep.Shared, RawStep.Shared, RawStep.Exclusive, RawStep.Own, RawStep.Own],
            layers.Select(x => types.RawStep(x.Instance, x.Part, x.Positions)));
        Assert.All(layers, x => Assert.False(TyCtxt.RawStated(KeptLayer(types, x.Instance, x.Part, x.Positions))));
        Assert.All([AdtDef.FieldType(p[3], 0)!, AdtDef.CaseTypes(p[4])![0]!.Components[0], AdtDef.FieldType(p[5], 1)!], static x => Assert.Null(x.RawOrigin));
    }

    // SPEC 5.7, 5.2.1 condition 3 (DECISIONS BU-24; R4's constraint 2): a raw layer a Type argument supplies, where the declaration states a
    // Type parameter or a pair layer (a Field, a Case payload, a Tuple or array element, or above the layer in a Tuple argument), is no
    // declared layer: it has the kind of the Origin the argument states (none Unstated, `static` Static, any other Exclusive), never the
    // stored cap, and a pair's raw case states none (SPEC 8.1.2), nor does a raw argument a pair slot binds (SPEC 8.1.1, DECISIONS BU-34:
    // `Holder<raw/i32 during y>` is `Holder<raw/i32>`). Its Origin's borrows are not the instance's, so an Exclusive one's members are
    // incomplete. No declaration states such a layer (RawStated). A path whose substituted part is no raw layer stays a broken contract, as
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
            [RawStep.Unstated, RawStep.Exclusive, RawStep.Static, RawStep.Unstated, RawStep.Unstated, RawStep.Unstated, RawStep.Unstated,
                RawStep.Unstated, RawStep.Unstated, RawStep.Unstated, RawStep.Exclusive, RawStep.Exclusive, RawStep.Unstated],
            layers.Select(x => types.RawStep(x.Instance, x.Part, x.Positions)));
        Assert.All(layers, x => Assert.False(TyCtxt.RawStated(KeptLayer(types, x.Instance, x.Part, x.Positions))));
        var members = new List<RawMember>();
        Assert.Equal((false, true, true), (types.RawMembers(p[10], 0, [], members), types.RawMembers(p[9], 0, [], members), types.RawMembers(p[21], 0, [], members)));
        Assert.Empty(members);
        Assert.Same(p[13], p[21]);
        Assert.Contains("InternalInvariant", Assert.Throws<InvalidOperationException>(() => types.RawStep(p[20], 0, [])).Message, StringComparison.Ordinal);
        var supplied = types.Intern(BoundTypeKind.Constructed, p[9].Symbol, SemanticsKind.Owner, [types.Pointer(BoundType.I32, BoundOrigin.Holder)]);
        Assert.Contains("InternalInvariant", Assert.Throws<InvalidOperationException>(() => types.RawStep(supplied, 0, [])).Message, StringComparison.Ordinal);
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
    // kept Origin's kind alone and has no member (an Exclusive one's members are incomplete).
    [Fact]
    public void WrittenAtTellsApartThePositionsOfOneKeptPart()
    {
        var c = MirBuildTest.Compile(
            "enum E<T> {s}\n    A(raw/i32 during s, i32)\n    B(T, i32)\n    C(Loan<ref/i32 during s>)\nstruct P<T> {s}\n    internal let loan: Loan<ref/i32 during s>\n" +
            "    internal var x: (raw/i32 during s, i32)\n    internal var y: T\nenum E3 {s}\n    A(raw/(ref/i32 during s) during s, i32)\n" +
            "    B(raw/(ref/i32 during static) during static, i32)\n    C(ref/i32 during s)\n" +
            "func probe(y: uniq/i32, e: E<raw/i32 during y> during y, p: P<(raw/i32 during y, i32)> during y, e3: E3 during static) => ()\npublic func main() => ()\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var types = c.Types;
        var q = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "probe").Parameters.Select(static x => x.Type.TypeOf()!).ToArray();
        (BoundType Instance, int Part, int Other)[] twins = [(q[1], 0, 1), (q[2], 1, 2), (q[3], 0, 1)];
        Assert.All(twins, x => Assert.Same(types.StoredParts(x.Instance, out _)[x.Part], types.StoredParts(x.Instance, out _)[x.Other]));
        (BoundType Instance, int Part, int[] Positions)[] rows = [(q[1], 0, [0]), (q[1], 1, [0]), (q[2], 1, [0]), (q[2], 2, [0]), (q[3], 0, [0]), (q[3], 1, [0])];
        var members = new List<RawMember>();
        Assert.Equal(
            ["raw/i32 during Parameter Shared True 1", "- Exclusive False 0", "raw/i32 during Parameter Shared True 1", "- Exclusive False 0",
                "raw/ref/i32 during Parameter Shared True 0", "raw/ref/i32 during Static Static True 0"],
            rows.Select(x => $"{Shown(TyCtxt.WrittenAt(x.Instance, x.Part, x.Positions))} {types.RawStep(x.Instance, x.Part, x.Positions)} " +
                $"{types.RawMembers(x.Instance, x.Part, x.Positions, Cleared(members))} {members.Count}"));
        Assert.Equal(
            [Binding.StepOf(KeptLayer(types, q[1], 1, [0]).RawOrigin), Binding.StepOf(KeptLayer(types, q[2], 2, [0]).RawOrigin)],
            [types.RawStep(q[1], 1, [0]), types.RawStep(q[2], 2, [0])]);
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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var holder = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "pv").Parameters[1].Type.TypeOf()!;
        var kept = c.Types.StoredParts(holder, out _)[1]!;
        var path = new int[8];
        for (var k = 0; k <= 8; k++)
        {
            Assert.Same(kept, TyCtxt.WrittenAt(holder, 1, path.AsSpan(0, k)));
            kept = k < 8 ? kept.Components[0] : kept;
        }

        Assert.Equal(RawStep.Static, c.Types.RawStep(holder, 1, path));
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
            Assert.DoesNotContain(Stored(holder), static x => x?.CarriesRawOrigin == true);
        }

        var plain = Kept(c, p[8]);
        Assert.Same(AdtDef.FieldType(p[8], 0), plain[0]);
        var failed = MirBuildTest.Compile("struct Bad<P>\n    public var v: P\n    public var m: Missing\nenum E<P>\n    A(Missing, P)\n    B(P)\n" +
            "func f(x: uniq/i32, b: Bad<raw/u8 during x>, e: E<raw/u8 during x>) => ()\n");
        Assert.False(failed.Binding.Result.IsComplete);
        var q = KotoTree.Walk(failed.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "f").Parameters.Select(static x => x.Type.TypeOf()!).ToArray();
        Assert.Equal([false, true, true, true, false], Kept(failed, q[1]).Concat(Kept(failed, q[2])).Select(static x => x is null));
        Assert.True(Kept(failed, q[1])[0]!.CarriesRawOrigin && Kept(failed, q[2])[1]!.CarriesRawOrigin);
        Assert.Contains("InternalInvariant", Assert.Throws<InvalidOperationException>(() => failed.Types.RawStep(q[2], 0, [1])).Message, StringComparison.Ordinal);

        // A struct's kept Fields and kept base, or an enum's kept Case payload Tuples, each AdtDef's WithoutRawOrigins.
        static BoundType?[] Kept(Compilation c, BoundType holder)
        {
            var parts = c.Types.StoredParts(holder, out var parent).ToArray();
            BoundType?[] kept = AdtDef.IsStruct(holder) ? [.. parts, parent] : parts;
            var erased = Stored(holder).ToArray();
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
    // and steps Exclusive with incomplete members, where AdtDef's Wrap<raw/u8> reads Unstated, and Opt2's payload reaches the same
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
            (RawStep.Exclusive, false, false),
            (types.RawStep(wrap, 0, []), TyCtxt.RawStated(KeptLayer(types, wrap, 0, [])), types.RawMembers(wrap, 0, [], members)));
        Assert.Equal(RawStep.Unstated, types.RawStep(AdtDef.FieldType(p[1], 0)!, 0, []));
        Assert.Same(wrap, types.StoredParts(p[3], out _)[0]!.Components[0]);
        var option = Kept(9);
        var some = Array.FindIndex(option.Symbol!.Adt!.Cases, static x => x is { Payload.Length: 1 });
        (BoundType Instance, int Part, int[] Positions)[] layers =
            [(option, some, [0]), (Kept(10), 0, [0]), (Kept(10), 0, [1]), (Kept(11), 0, [1]), (Kept(11), 1, [0]), (Kept(12), 0, [1]), (Kept(13), 0, [])];
        Assert.Equal(
            ["Exclusive False False", "Unstated True True", "Exclusive False False", "Exclusive False False", "Unstated True True",
                "Exclusive False False", "Unstated False True"],
            layers.Select(x => $"{types.RawStep(x.Instance, x.Part, x.Positions)} {TyCtxt.RawStated(KeptLayer(types, x.Instance, x.Part, x.Positions))} " +
                $"{types.RawMembers(x.Instance, x.Part, x.Positions, members)}"));
        Assert.Empty(members);
        Assert.Same(AdtDef.FieldType(p[13], 0), Kept(13));
        (BoundType Instance, int Part, int[] Positions)[] broken = [(option, some, []), (Kept(12), 0, [0]), (Kept(12), 0, [5])];
        Assert.All(broken, x => Assert.Contains(
            "InternalInvariant", Assert.Throws<InvalidOperationException>(() => types.RawStep(x.Instance, x.Part, x.Positions)).Message, StringComparison.Ordinal));

        // The first kept part of the probe's parameter i.
        BoundType Kept(int i) => types.StoredParts(p[i], out _)[0]!;
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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
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
        Assert.All(holders, x => Assert.Same(c.Binding.WithoutRawOrigins(Kept(x)), AdtDef.FieldType(x, 0)));
        Assert.All(holders, x => Assert.Same(AdtDef.FieldType(x, 0), c.Binding.StoredType(x.Symbol!.Adt!.Fields[0], x)));
        Assert.All(holders, x => Assert.False(AdtDef.FieldType(x, 0)!.HasAbstractPart));
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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
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
            var erased = Stored(holder).ToArray();
            var parts = types.StoredParts(holder, out var parent).ToArray();
            BoundType?[] kept = AdtDef.IsStruct(holder) ? [.. parts, parent] : parts;
            Assert.Equal(erased.Length, kept.Length);
            Assert.All(Enumerable.Range(0, kept.Length), i => Assert.Same(erased[i], c.Binding.WithoutRawOrigins(kept[i])));
            Assert.DoesNotContain(erased, static x => x?.CarriesRawOrigin == true);
        }

        var seen = new HashSet<BoundType>(ReferenceEqualityComparer.Instance);
        Assert.DoesNotContain(c.BuildMir().Bodies.ToArray().SelectMany(static x => x.Locals.ToArray()), x => TyCtxt.StatesUnstated(x.Type, seen));

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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
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
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (types.RawStep(p[3], 0, []) != RawStep.Own || types.RawStep(p[6], 1, []) != RawStep.Shared || types.RawStep(p[8], 1, [1]) != RawStep.Own ||
                types.RawStep(p[19], 0, [0]) != RawStep.Exclusive || types.StoredParts(p[19], out _).Length != 2 ||
                !ReferenceEquals(types.StoredParts(p[22], out _)[0], nested) || types.RawStep(nested, 0, []) != RawStep.Exclusive ||
                TyCtxt.RawStated(types.StoredParts(nested, out _)[0]!) || types.RawMembers(nested, 0, [], members) ||
                TyCtxt.RawStated(types.StoredParts(p[3], out _)[0]!))
            {
                throw new InvalidOperationException("Stored raw steps changed.");
            }

            if (!ReferenceEquals(declared.Types.StoredParts(holder2, out _)[0], wrap) || declared.Types.RawStep(wrap, 0, []) != RawStep.Unstated ||
                !TyCtxt.RawStated(declared.Types.StoredParts(wrap, out _)[0]!) || !declared.Types.RawMembers(wrap, 0, [], members))
            {
                throw new InvalidOperationException("A declared raw layer's reads changed.");
            }

            if (TyCtxt.WrittenAt(p[8], 1, [1]) is null || TyCtxt.WrittenAt(p[8], 0, [1]) is not null || !TyCtxt.StatesUnstated(wrap, seen) ||
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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
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

    // SPEC 6.1.3, 9.3, 11.3 (PLAN G157 (49)): the function whose instance a nested definition's instance holds is the named function whose
    // body declares it, through Closures and blocks, and in an accessor's body the accessor's executing function, which keys its body:
    // the getter's for a function, a Closure and a function in a Closure there, the setter's for its function (before: none, so the
    // collector's EnclosingInstance check never saw them); a method's for its function. A member, an accessor, a top-level function and a
    // static's initializer, with a Closure in it, are declared in a container, so none.
    [Fact]
    public void EnclosingFunctionIsTheFunctionWhoseBodyDeclaresTheDefinition()
    {
        var c = MinimalEmissionTest.Analyze(EnclosingSource);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        string[] expected =
        [
            "Fn init none", "Getter got.Get none", "Fn inner got.Get", "Closure keep got.Get", "Fn deep got.Get", "Setter got.Set none",
            "Fn store got.Set", "Fn get none", "Fn local get", "StaticInit limit none", "Closure limit none", "Fn outer none",
        ];
        var keys = c.BuildMir().Bodies.ToArray().Where(static x => x.Key.Decl is not null && x.Source.Module == 0).Select(static x => x.Key);
        var actual = keys.Select(x => $"{x.Kind} {Label(x.Decl!.Declaration)} {Enclosing(x.Decl!)}");
        Assert.Equal(expected.Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));

        string Enclosing(BindingSymbol definition) => c.Types.EnclosingFunction(definition) is { } function ? Label(function.Declaration) : "none";

        // A Closure is labeled by the local or static it initializes.
        static string Label(Koto declaration) => declaration switch
        {
            FunctionKoto { IsAnonymous: true } closure => Initialized(closure.Parent!),
            FunctionKoto function => function.Name,
            _ => ((VariableKoto)declaration).NameKoto.IdentifierName,
        };

        static string Initialized(Koto node) => node is VariableKoto variable ? variable.NameKoto.IdentifierName : Initialized(node.Parent!);
    }

    // DECISIONS R5-M39 (K package 4.1): Erase makes every Origin static and keeps everything else; two calls whose substitutions differ in
    // Origins only have one erased substitution, and every Type of the callee's body and closure, the closure's context included, erases
    // alike in their instances; erasing is idempotent and keeps a Type without Origins; a binder that does not instantiate has none.
    [Fact]
    public void EraseKeepsEverythingButOrigins()
    {
        var c = MinimalEmissionTest.Analyze(EraseSource);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
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
        Assert.All(erased.Inputs.ToArray().Concat(erased.Origins.ToArray()), static x => Assert.Same(BoundOrigin.Static, x));
        var locals = c.BuildMir().Bodies.ToArray().Where(static x => x.Key.Decl?.Name == "keep" || (x.Key.Kind == MirBodyKind.Closure && x.Source.Module == 0))
            .SelectMany(static x => x.Locals.ToArray()).Select(static x => x.Type).ToArray();
        Assert.True(locals.Length > 4);
        foreach (var local in locals)
        {
            var first = types.Erase(types.Apply(local, calls[0])!);
            Assert.Same(first, types.Erase(types.Apply(local, calls[1])!));
            Assert.Same(first, types.Erase(first));
        }

        var closure = Assert.Single(nodes.OfType<FunctionKoto>(), static x => x.IsAnonymous).ClosureOf()!.EnvironmentType!;
        var closed = calls.Select(x => types.Apply(closure, x)!).ToArray();
        Assert.NotSame(closed[0], closed[1]);
        Assert.Same(types.Erase(closed[0]), types.Erase(closed[1]));
        Assert.Same(types.Erase(closed[0].ClosureContext!), types.Erase(closed[0]).ClosureContext);
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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var instances = new List<BoundType>();
        var problems = ErasedStoreProblems(c, instances);
        Assert.True(problems.Count == 0, $"{problems.Count} differ:\n{string.Join('\n', problems)}");
        var views = instances.Where(static x => TyCtxt.Name(x) == "View<i32>").ToArray();
        Assert.Equal(2, views.Length);
        var erased = Assert.Single(views, x => ReferenceEquals(c.Types.Erase(x), x));
        Assert.All(views, x => Assert.Same(erased, c.Types.Erase(x)));
        Assert.All(["Block", "Cell"], x => Assert.Contains(instances, y => TyCtxt.Name(y) == x && ReferenceEquals(c.Types.Erase(y), y)));
    }

    // Plan 5.6.2: a call in an omitted default composed with the substitution of the call omitting it is the call Binding forwards into that
    // instance (InstantiateForwardedCall), over the corpus and over generic, length, Origin, member and requirement calls in defaults.
    [Fact]
    public void ComposeIsTheForwardedInstantiation()
    {
        var problems = new List<string>();
        var corpus = 0;
        foreach (var name in HirColumnsTest.Corpus.Select(static x => x.Data))
        {
            var (c, module) = SemanticSnapshotTest.Bind(name);
            corpus += ComposeProblems(c, module, problems);
        }

        var direct = MinimalEmissionTest.Analyze(ComposeSource);
        Assert.True(direct.Binding.Result.IsComplete, MinimalEmissionTest.Describe(direct, null));
        var calls = ComposeProblems(direct, direct.Kotonoha, problems);
        Assert.True(corpus == 6 && calls == 7 && problems.Count == 0, $"{corpus} corpus and {calls} direct nested calls; {problems.Count} differ:\n{string.Join('\n', problems.Take(40))}");

        // copy, count, first, same (a requirement, so the forwarding resolves its witness) and get bind a Type, a length, an Origin, Self and
        // a declaring Type of the instance.
        var composed = NestedCalls(direct.Kotonoha).Select(x => (x.Inner.Target.Name, Subst: direct.Types.Compose(direct.Types.SubstOf(x.Inner)!, direct.Types.SubstOf(x.Outer)!).Subst!)).ToArray();
        Assert.Equal(["i32", "bool"], composed.Where(static x => x.Name == "copy").Select(static x => x.Subst.Types[0]!.Name));
        Assert.Equal(3, composed.Single(static x => x.Name == "count").Subst.Lengths[0]!.Value);
        Assert.Contains(composed.Single(static x => x.Name == "first").Subst.Origins.ToArray().Concat(composed.Single(static x => x.Name == "first").Subst.Inputs.ToArray()), static x => x is not null);
        Assert.Equal(["i32", "bool"], composed.Where(static x => x.Subst.Target.Declaration is FunctionKoto { IsRequirement: true }).Select(static x => x.Subst.Self!.Name));
        Assert.Equal("Box", composed.Single(static x => x.Name == "get").Subst.Declaring!.Name);
        Assert.Same(BoundType.I32, composed.Single(static x => x.Name == "get").Subst.Declaring!.Components[0]);
    }

    // DECISIONS R5-M61 (F package 4.5): a requirement call in a generic body, in the instance a closed call of that body makes, has under
    // Witness the target Binding's forwarding into that instance materializes (InstantiateForwardedCall), over every such call of the
    // generic library and the milestones and over WitnessSource's kinds; a bound Contract reference is the substitution's declaring Type.
    [Fact]
    public void WitnessIsTheForwardedRequirementCall()
    {
        var problems = new List<string>();
        var corpus = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var name in HirColumnsTest.Corpus.Select(static x => x.Data))
        {
            var (c, module) = SemanticSnapshotTest.Bind(name);
            WitnessProblems(c, module, name, corpus, problems);
        }

        var direct = MinimalEmissionTest.Analyze(WitnessSource);
        Assert.True(direct.Binding.Result.IsComplete, MinimalEmissionTest.Describe(direct, null));
        var kinds = new SortedDictionary<string, int>(StringComparer.Ordinal);
        WitnessProblems(direct, direct.Kotonoha, "direct", kinds, problems);
        var counts = $"corpus {Counts(corpus)}; direct {Counts(kinds)}";
        Assert.True(
            problems.Count == 0 && counts == "corpus Body 34, Builtin 2, Storage 1, bound 3; direct Body 11, Builtin 2, Storage 3, bound 3",
            $"{counts}; {problems.Count} differ:\n{string.Join('\n', problems.Take(40))}");

        static string Counts(SortedDictionary<string, int> kinds) => string.Join(", ", kinds.Select(static x => $"{x.Key} {x.Value}"));
    }

    // DECISIONS R5-M61: a conforming Self whose storage nests past Binding's depth bound has the Limit target, not an exception. A caller's
    // instance that makes a requirement call's Self nest past it has no composed substitution, as it has no forwarded call (PLAN G157 (4)),
    // so no substitution without its Self reaches Witness.
    [Fact]
    public void ADeepSelfHasTheLimitTarget()
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
        call.Set(inner.Target, inner.ReturnType, null, [], [], conformingType: deep);
        Assert.Equal(RequirementTargetKind.Limit, c.Types.Witness(inner.Target, inner.RequirementContract, c.Types.SubstOf(call)!).Kind);
        Assert.Equal(RequirementTargetKind.Body, Witness(c, (inner, outer)).Kind);

        // Option^40<i32> (storage height 80) is past the bound whatever Witness prepared before it (Wrap^80, whose Type argument no Field
        // stores, is refused too; DECISIONS BU-11, BU-12), so the caller's instance composes no substitution.
        var option = KotoTree.Walk(c.Kotonoha.RootKoto).Select(static x => x.TypeOf()).First(static x => x is { Symbol.Name: "Option" })!;
        var nested = BoundType.I32;
        for (var i = 0; i < 40; i++)
        {
            nested = c.Types.Intern(option.Kind, option.Symbol, option.Semantics, [nested]);
        }

        var instance = new CallPlan();
        instance.Set(outer.Target, outer.ReturnType, null, [], [nested]);
        var composed = c.Types.Compose(c.Types.SubstOf(inner)!, c.Types.SubstOf(instance)!);
        Assert.Equal((null, Binding.StorageOutcome.Limit), (composed.Subst, composed.Storage));
        Assert.Same(nested, composed.Part);
        Assert.Null(c.Binding.InstantiateForwardedCall(inner, instance));
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

    // DECISIONS BU-36: TyCtxt.Instance gives the instance a substitution makes with its storage verdict, and Apply is that instance when
    // Prepared: f's `uniq/T` at Bad<i32>, whose Field m did not bind, is Rejected, at B^65<i32> a Limit, and under f's identity the
    // declared Type, Prepared.
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
        Assert.All(calls, x => Assert.Null(c.Types.Apply(declared, c.Types.SubstOf(x)!)));
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
        call.Set(target.Target, target.ReturnType, null, [], [c.Types.SubstOf(calls[1])!.Types[0]]);
        var parameter = ((FunctionKoto)target.Target.Declaration).Parameters[0].Type.TypeOf()!;
        Assert.Throws<InvalidOperationException>(() => complete.Types.ReadableInstance(parameter, complete.Types.SubstOf(call)!));
    }

    // DECISIONS BU-36, R4-M139: a reader of an instance may read Prepared and Rejected storage, not a Limit (Readable, which retention,
    // the MIR builder, A4 and MirCases read); Rejected rests on a Binding failure, so in a complete compilation it is a broken contract
    // (the guard, as the collector's Derived): an incomplete compilation reads it, a complete one throws.
    [Fact]
    public void ARejectedVerdictIsReadableOnlyBesideABindingFailure()
    {
        var failed = MirBuildTest.Compile("struct Bad\n    public var m: Missing\nfunc f(b: ref/Bad) => ()\n");
        var complete = MirBuildTest.Compile("struct Good\n    public var m: i32\nfunc f(b: ref/Good) => ()\n");
        Assert.False(failed.Binding.Result.IsComplete);
        Assert.True(complete.Binding.Result.IsComplete);
        Assert.Equal([true, true, false], new[] { Binding.StorageOutcome.Prepared, Binding.StorageOutcome.Rejected, Binding.StorageOutcome.Limit }.Select(x => failed.Types.Readable(BoundType.I32, x)));
        Assert.Equal([true, false], new[] { Binding.StorageOutcome.Prepared, Binding.StorageOutcome.Limit }.Select(x => complete.Types.Readable(BoundType.I32, x)));
        Assert.Throws<InvalidOperationException>(() => complete.Types.Readable(BoundType.I32, Binding.StorageOutcome.Rejected));
    }

    // SPEC 23.1.2 (DECISIONS BU-38): what a requirement call runs does not depend on a stored part Binding rejected, so a conforming Self
    // whose storage is Rejected has its witness's target (fail-before: Limit, which the collector published as a gap), while a Self whose
    // storage also nests past the bound keeps the Limit target beside its failed Field, as its repair does: the limit wins.
    [Theory]
    [InlineData("Missing", false, "Body")]
    [InlineData("Missing", true, "Limit")]
    [InlineData("i32", true, "Limit")]
    public void ARejectedSelfRunsItsWitness(string field, bool deep, string expected)
    {
        var c = MirBuildTest.Compile(RetentionTest.Box + $"contract Zero\n    func zero() -> i32\nstruct Bad\n    Self is Zero\n    public var m: {field}\n" +
            (deep ? $"    public var big: {RetentionTest.Box64}\n" : string.Empty) + "    public func zero() -> i32 => 1\n");
        var zero = c.Kotonoha.RootKoto.NestedDeclarationContainers.OfType<ContractKoto>().Single().SymbolOf()!;
        var bad = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<StructKoto>().Single(static x => x.Name == "Bad").SymbolOf()!.Type!;
        var requirement = zero.Contract!.OwnRequirement()!;
        Assert.Equal(expected, c.Types.Witness(requirement, zero, c.Types.Synthesized(requirement, bad, zero.Type!)).Kind.ToString());
    }

    // DECISIONS BU-38: Compose carries the worst verdict of its parts' storage in the outer instance. inner's call in outer's instance at
    // Bad<i32>, whose Field m did not bind, is Rejected with the substitution of Binding's forwarded call; beside a Type argument nested past
    // the bound it is a Limit without a substitution in either order, as its repair's is (before: null for each, which the collector
    // published as the storage-nesting gap). The verdict's deciding part (R4-M139) is the first part reaching it, none when Prepared: Bad<i32>
    // when Rejected, also before Bad<i64>, and the nested Type beside it in either order; a Rejected composition always has one.
    [Theory]
    [InlineData("i32", "i32", "Prepared", -1)]
    [InlineData("Bad<i32>", "i32", "Rejected", 0)]
    [InlineData("Bad<i32>", "Bad<i64>", "Rejected", 0)]
    [InlineData("Bad<i32>", RetentionTest.Box65, "Limit", 1)]
    [InlineData(RetentionTest.Box65, "Bad<i32>", "Limit", 0)]
    public void ComposeHasTheWorstVerdictOfItsParts(string first, string second, string expected, int part)
    {
        var c = MirBuildTest.Compile(RetentionTest.Box + "struct Bad<T>\n    public var v: T\n    public var m: Missing\nfunc inner<A, B>(a: uniq/A, b: uniq/B) => ()\n" +
            $"func outer<A, B>(a: uniq/A, b: uniq/B) => inner(a, b)\nfunc g(x: uniq/{first}, y: uniq/{second}) => outer(x, y)\n");
        var calls = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Select(static x => x.CallOf()!).ToDictionary(static x => x.Target.Name);
        var (subst, storage, decided) = c.Types.Compose(c.Types.SubstOf(calls["inner"])!, c.Types.SubstOf(calls["outer"])!);
        Assert.Equal(expected, storage.ToString());
        Assert.Same(part < 0 ? null : c.Types.SubstOf(calls["outer"])!.Types[part], decided);
        Assert.True(storage != Binding.StorageOutcome.Rejected || decided is not null);
        Assert.Same(subst is null ? null : c.Types.SubstOf(c.Binding.InstantiateForwardedCall(calls["inner"], calls["outer"])!), subst);
        Assert.Equal(storage == Binding.StorageOutcome.Limit, subst is null);
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
            var target = c.Types.Witness(requirement, contract, c.Types.Synthesized(requirement, BoundType.I32, contract.Type!));
            Assert.Equal(RequirementTargetKind.Builtin, target.Kind);
            Assert.NotSame(requirement, target.Target);
            Assert.Same(contract, TyCtxt.Contract(requirement));
            Assert.Same(contract, TyCtxt.Contract(target.Target!));
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
        Assert.Equal([null, c.Types.Tuple([BoundType.I32]), c.Types.Tuple([])], AdtDef.CaseTypes(parameters[0]) ?? []);
        Assert.Equal([Binding.StorageOutcome.Rejected, Binding.StorageOutcome.Limit], parameters.Select(c.Binding.PrepareTypeStorage));
    }

    // A warm rebind keeps every call's substitution the same object and looking it up allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmSubstitutionsAreTheSameAndAllocateNothing()
    {
        var c = SemanticSnapshotTest.Bind("milestone/Milestone34").Compilation;
        var calls = KotoTree.Walk(c.Kotonoha.RootKoto).Concat(KotoTree.Walk(c.Library.Kotonoha.RootKoto))
            .SelectMany(static x => Enumerable.Range(0, 8).Select(i => (Node: x, Ordinal: i)).TakeWhile(static y => CallPlan.Of(y.Node, y.Ordinal) is not null))
            .Where(x => c.Types.SubstOf(CallPlan.Of(x.Node, x.Ordinal)!) is not null).ToArray();
        var substs = calls.Select(x => c.Types.SubstOf(CallPlan.Of(x.Node, x.Ordinal)!)).ToArray();
        var changed = 0;
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Binding.Bind(BindingMode.Final);
            for (var i = 0; i < calls.Length; i++)
            {
                changed += ReferenceEquals(c.Types.SubstOf(CallPlan.Of(calls[i].Node, calls[i].Ordinal)!), substs[i]) ? 0 : 1;
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
        var pairs = NestedCalls(c.Kotonoha).ToArray();
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
        var locals = c.BuildMir().Bodies.ToArray().SelectMany(static x => x.Locals.ToArray()).Select(static x => x.Type).Distinct().ToArray();
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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var probe = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "probe");
        return (c, probe.Parameters.Select(static x => x.Type.TypeOf()!).ToArray());
    }

    // The Type context and the parameter Types of ConditionalSource's `probe`.
    private static (TyCtxt Types, BoundType[] Parameters) ConditionalLayers()
    {
        var c = MinimalEmissionTest.Analyze(ConditionalSource);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var probe = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "probe");
        return (c.Types, probe.Parameters.Select(static x => x.Type.TypeOf()!).ToArray());
    }

    // The compilation of KeptSource and the parameter Types of its `probe`: x, then Outer, Pair2, Opt2, Hold, OP, Box1 and Der under x's
    // Origin, Outer under none, then OuterE, OuterE2, OuterF, OuterT and OuterH under x's.
    private static (Compilation C, BoundType[] Parameters) KeptParts()
    {
        var c = MinimalEmissionTest.Analyze(KeptSource);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
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

    // The raw layer an instance stores at a part and inline positions, as RawStep reads it: its kept part (an enum Case's payload Tuple),
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

    // The compilation of DeclaredSource and its `probe`'s parameter Types by name.
    private static (Compilation C, Dictionary<string, BoundType> Parameters) DeclaredLayers()
    {
        var c = MinimalEmissionTest.Analyze(DeclaredSource);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var probe = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "probe");
        return (c, probe.Parameters.ToDictionary(static x => x.InternalName, static x => x.Type.TypeOf()!));
    }

    // The struct and enum instances the MIR locals hold, added to `instances`, and the names of those whose erased instance does not store
    // the erasure of what they store.
    private static List<string> ErasedStoreProblems(Compilation c, List<BoundType> instances)
    {
        var types = c.Types;
        var seen = new HashSet<BoundType>();
        var problems = new List<string>();
        foreach (var body in c.BuildMir().Bodies)
        {
            foreach (var local in body.Locals)
            {
                Visit(local.Type);
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
                if (!Stored(type).Select(x => x is null ? null : types.Erase(x)).SequenceEqual(Stored(types.Erase(type))))
                {
                    problems.Add(TyCtxt.Name(type));
                }
            }
        }
    }

    private static IEnumerable<BoundType?> Stored(BoundType type) => AdtDef.IsStruct(type)
        ? Enumerable.Range(0, AdtDef.Count(type)).Select(x => AdtDef.FieldType(type, x)).Append(AdtDef.Base(type)) : AdtDef.CaseTypes(type) ?? [];

    // Each call in a default a call omits, with that call.
    private static IEnumerable<(CallPlan Inner, CallPlan Outer)> NestedCalls(Kotonoha module)
    {
        foreach (var node in KotoTree.Walk(module.RootKoto))
        {
            for (var i = 0; CallPlan.Of(node, i) is { } outer; i++)
            {
                foreach (var omitted in outer.DefaultArguments.ToArray())
                {
                    foreach (var nested in KotoTree.Walk(DefaultDecl.Of(omitted).Expression))
                    {
                        for (var k = 0; CallPlan.Of(nested, k) is { } inner; k++)
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

    // The forwarded call has the composed substitution and instantiates each declared Type of the callee as Apply does; a requirement call,
    // which the forwarding resolves to its witness, composes as applying the two substitutions in turn.
    private static int ComposeProblems(Compilation c, Kotonoha module, List<string> problems)
    {
        var count = 0;
        foreach (var (inner, outer) in NestedCalls(module))
        {
            count++;
            var composed = c.Types.Compose(c.Types.SubstOf(inner)!, c.Types.SubstOf(outer)!).Subst;
            var forwarded = inner.Kind == CalleeKind.Requirement ? null : c.Binding.InstantiateForwardedCall(inner, outer);
            var where = $"{module.Compilation.Kotonoha.Name} {outer.Target.Name} > {inner.Target.Name}";
            if (inner.Kind != CalleeKind.Requirement && (forwarded is null || !ReferenceEquals(c.Types.SubstOf(forwarded), composed)))
            {
                problems.Add($"{where}: {composed is null} {forwarded is null}");
                continue;
            }

            foreach (var declared in ((FunctionKoto)inner.Target.Declaration).Parameters.Select(static x => x.Type.TypeOf()).Append(inner.Target.Type))
            {
                var expected = declared is null ? null : forwarded is not null ? c.Binding.InstantiateStorageType(declared, forwarded)
                    : c.Types.Apply(declared, c.Types.SubstOf(inner)!) is { } once ? c.Types.Apply(once, c.Types.SubstOf(outer)!) : null;
                if (composed is null || !ReferenceEquals(declared is null ? null : c.Types.Apply(declared, composed), expected))
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
                    c.Types.Apply(own, subst) is not { } applied || !ReferenceEquals(applied, c.Binding.InstantiateStorageType(contract, call))))
                {
                    problems.Add($"{node.CodeContext.SourceDocument?.Path}:{node.Span.Start} contract {(i < count ? $"slot {i}" : "result")}");
                }
            }
        }

        void Check(Koto node, BoundType? declared, Subst subst, CallPlan context)
        {
            var applied = declared is null ? null : c.Types.Apply(declared, subst);
            var instance = declared is null ? null : Instance(c, declared, subst, context);
            if (!ReferenceEquals(applied, instance) ||
                (applied is { Kind: BoundTypeKind.Closure, Symbol.Declaration: FunctionKoto function } && function.ClosureOf() is { } closure &&
                    !ReferenceEquals(c.Binding.ClosureSignature(applied), c.Types.Apply(closure.Signature, c.Types.SubstOfClosure(applied)!))))
            {
                problems.Add($"{node.CodeContext.SourceDocument?.Path}:{node.Span.Start} {node.Akind} {declared}: {applied}");
            }
        }

        var calls = 0;
        foreach (var node in KotoTree.Walk(root))
        {
            for (var i = 0; CallPlan.Of(node, i) is { } call; i++)
            {
                if (c.Types.SubstOf(call) is { } subst && subst.Target.Declaration is FunctionKoto callee)
                {
                    calls++;
                    foreach (var parameter in callee.Parameters)
                    {
                        Check(node, parameter.Type.TypeOf(), subst, call);
                    }

                    Check(node, subst.Target.Type, subst, call);
                    if (call.Target.Declaration is FunctionKoto slot && !ReferenceEquals(callee, slot))
                    {
                        Contract(node, callee, slot, subst, call);
                    }
                }
            }

            if (node.TypeOf() is { Kind: BoundTypeKind.FunctionItem, Symbol.Declaration: FunctionKoto function } item && c.Binding.FunctionItemContext(item) is { } context)
            {
                foreach (var parameter in function.Parameters)
                {
                    Check(node, parameter.Type.TypeOf(), c.Types.SubstOfItem(item)!, context);
                }
            }
        }

        Assert.True(calls > 0 && problems.Count == 0, $"{calls} calls; {problems.Count} differ:\n{string.Join('\n', problems.Take(40))}");
    }

    // Binding's instantiation for a call's context. A requirement's, under its conforming Self and bound Contract reference, and a direct
    // base call's into an implementation other than the slot's original, of that implementation in the implementing Type (as
    // Binding.EffectBounds forms that call), have no CallPlan form: theirs is Binding's instantiation under the substitution once it names the
    // call's target and conforming Self (a base call's: the implementation, no Self, the implementing Type) and binds the call's Type and
    // length arguments and Origins. Any other call's is its CallPlan form.
    private static BoundType? Instance(Compilation c, BoundType declared, Subst subst, CallPlan context) => context switch
    {
        { Kind: CalleeKind.Requirement } when ReferenceEquals(subst.Target, context.Target) && ReferenceEquals(subst.Self, context.ConformingType) &&
            Binds(subst, context) => c.Binding.InstantiateStorageType(declared, subst),
        { Kind: CalleeKind.Requirement } => null,
        { Kind: CalleeKind.Virtual, VirtualIsDirect: true, VirtualImplementation: { } body } when !ReferenceEquals(body, context.Target.Declaration)
            => ReferenceEquals(subst.Binder, body) && subst.Self is null && ReferenceEquals(subst.Declaring, context.VirtualImplementingType) &&
                Binds(subst, context) ? c.Binding.InstantiateStorageType(declared, subst) : null,
        _ => c.Binding.InstantiateStorageType(declared, context),
    };

    // The substitution binds the call's Type and length arguments, declared Origins and input Origins.
    private static bool Binds(Subst subst, CallPlan call)
        => subst.Types.SequenceEqual(call.TypeArguments) && subst.Lengths.SequenceEqual(call.LengthArguments) &&
            subst.Origins.SequenceEqual(call.Origins) && subst.Inputs.SequenceEqual(call.InputOrigins);

    // Each requirement call in the body of a generic function, with each call of that function in a module whose instance closes the
    // requirement's Self, as InstantiateForwardedCall pairs them (a call in a nested function or closure is its own binder's).
    private static IEnumerable<(CallPlan Inner, CallPlan Outer)> RequirementCalls(Compilation c, Kotonoha module)
    {
        var bodies = new Dictionary<FunctionKoto, CallPlan[]>();
        foreach (var node in KotoTree.Walk(module.RootKoto))
        {
            for (var i = 0; CallPlan.Of(node, i) is { } outer; i++)
            {
                if (outer.Kind != CalleeKind.Function || outer.Target.Declaration is not FunctionKoto callee || !c.Types.Instantiates(outer.Target))
                {
                    continue;
                }

                if (!bodies.TryGetValue(callee, out var inner))
                {
                    inner = KotoTree.Walk(callee).Where(x => ReferenceEquals(Binder(x), callee))
                        .SelectMany(static x => Enumerable.Range(0, 8).Select(k => CallPlan.Of(x, k)).TakeWhile(static y => y is not null).OfType<CallPlan>())
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
        => c.Types.Witness(call.Inner.Target, call.Inner.RequirementContract, Composed(c, call)!);

    // Each Witness of RequirementCalls agrees with the forwarded call (the CallPlan materializer's): a Body is the function the forwarded call
    // runs under its substitution; a Builtin the same compiler function, and a Storage witness the Field its bridge reads with the bridge's
    // operation, each under the requirement call's own substitution, which binds what the forwarded call binds; Refused has no forwarded
    // call; None and Limit occur for no closed Self of an accepted program. A bound Contract reference is the substitution's declaring Type.
    // The views leave Binding's records and the HIR version unchanged. Counts the targets by kind and the bound references.
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
                if (!ReferenceEquals(subst.Declaring, c.Binding.InstantiateStorageType(bound, outer)))
                {
                    problems.Add($"{where} bound reference");
                }
            }

            var forwarded = c.Binding.InstantiateForwardedCall(inner, outer);
            var agrees = target.Kind switch
            {
                RequirementTargetKind.Body => ReferenceEquals(target.Target, forwarded?.Target) && ReferenceEquals(target.Subst, c.Types.SubstOf(forwarded!)),
                RequirementTargetKind.Builtin => ReferenceEquals(target.Target, forwarded?.Target) && ReferenceEquals(target.Subst, subst) &&
                    Same(c.Types.SubstOf(forwarded!)!, subst),
                RequirementTargetKind.Storage => forwarded?.Target.Declaration is FunctionKoto { IsPropertyWitness: true, Accessor: { } bridge } function &&
                    ReferenceEquals(target.Target, bridge.Property.Symbol) && target.Access == Access(function) && ReferenceEquals(target.Subst, subst) &&
                    c.Types.SubstOf(forwarded) is { } storage && ReferenceEquals(storage.Declaring, subst.Self) && Same(storage, subst, self: false),
                RequirementTargetKind.Refused => forwarded is null,
                _ => false,
            };

            if (!agrees)
            {
                problems.Add($"{where}; forwarded {forwarded?.Target.Name}");
            }
        }

        void Count(string key) => kinds[key] = kinds.TryGetValue(key, out var count) ? count + 1 : 1;

        // A standard bridge's operation, by the body PropertyWitnessFunction gives it.
        static PropertyWitnessKind Access(FunctionKoto bridge) => bridge.ExpressionBody switch
        {
            ConversionKoto => PropertyWitnessKind.StorageBorrow,
            BinaryKoto { Akind: KotoKind.Equals } => PropertyWitnessKind.StorageSet,
            _ => PropertyWitnessKind.StorageCopy,
        };

        // Two substitutions bind the same Self (when asked), Types, lengths, Origins and inputs.
        static bool Same(Subst a, Subst b, bool self = true) => (!self || ReferenceEquals(a.Self, b.Self)) && a.Types.SequenceEqual(b.Types) &&
            a.Lengths.SequenceEqual(b.Lengths) && a.Origins.SequenceEqual(b.Origins) && a.Inputs.SequenceEqual(b.Inputs);
    }
}
