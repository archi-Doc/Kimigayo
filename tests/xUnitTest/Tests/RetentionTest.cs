// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Reflection;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>Call retention, the Binding view A5's Loan flow reads for calls (<c>TyCtxt.Retains</c>, design A3 4.3 C1+C8, DECISIONS
/// R4-M51): which outputs of a call may retain which arguments under the premises the callee's body is checked under (SPEC 15.6.4), as
/// rows <c>input kind output</c> (the result, or <c>store j</c>, the storage input j's exclusive, raw or invariant layers let the call
/// write), whether the result is untied, the untied stores and the stores written only in the receiver's referent (<c>referent store
/// j</c>, DECISIONS BU-42).</summary>
public sealed partial class RetentionTest
{
    // A struct B that stores its Type argument, and B^64<i32> and B^65<i32>, whose storage heights are 64 and 65 (DECISIONS BU-12's bound
    // is 64).
    internal const string Box = "struct B<T>\n    public var item: T\n    public init(item: T) => self.item = item@move\n";

    internal const string Box64 = "B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<B<" +
        "i32>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>";

    internal const string Box65 = "B<" + Box64 + ">";

    // G240's forms (the designs' and R4's probes; the rejected spelling MISSING is a Binding failure, its repair valid). Bad's Field;
    // Sx holding Bad through a handle; keep2's result during x beside a Bad input, then v written while the result lives; stash storing
    // x's borrow into slot beside a Bad input, then x written while slot lives; Pick's payload Origin, with a conflict beside its call;
    // Bad beside a Field of height 65; G growing beside a failed Field.
    internal const string Bad = "struct Bad\n    public var m: MISSING\n";

    internal const string Handle = Bad + "struct Sx\n    public var x: rc/Bad\nfunc f(s: uniq/Sx, p: raw/i32) => ()\nfunc g(s: uniq/Sx) => f(s, null)\n";

    internal const string Keep2 = Bad + "func keep2(x: ref/i32, b: uniq/Bad) -> ref/i32 during x => x\nfunc g(b: uniq/Bad)\n    var v = 1\n" +
        "    let r = keep2(v@ref, b)\n    v = 2\n    let k = r@follow\n";

    internal const string Stash = Bad + "func stash(slot: uniq/(ref/i32 during a), value: ref/i32 during a, b: uniq/Bad) => slot@follow = value\n" +
        "func g(b: uniq/Bad)\n    var x = 1\n    var y = 2\n    var slot = y@ref\n    stash(slot@uniq, x@ref, b)\n    x = 3\n    let k = slot@follow\n";

    internal const string Pick = "enum Pick {s}\n    First(ref/i32 during MISSING)\n    Second(ref/i32 during s)\n" +
        "    public func choose(self: ref/Self, useFirst: bool) -> i32 => 0\nfunc g(pair: uniq/Pick, useFirst: bool) -> i32\n    var x = 1\n" +
        "    let r = x@ref\n    let n = pair.choose(useFirst)\n    x = 2\n    let k = r@follow\n    return n\n";

    internal const string Tall = Box + Bad + "    public var big: " + Box65 + "\nfunc f(b: uniq/Bad, p: raw/i32) => ()\nfunc g(b: uniq/Bad) => f(b, null)\n";

    internal const string Growing = "struct G<T>\n    public var m: MISSING\n    public var next: raw/G<(T, T)>\nfunc f(g: uniq/G<i32>, p: raw/i32) => ()\n" +
        "func h(g: uniq/G<i32>) => f(g, null)\n";

    // Site 3 of DECISIONS R4-M118: Selection's payload Origin MISSING spells (missing: a Binding failure; source: its repair).
    internal const string Selection = "struct Cell\n    public var value: i32\n    public init(value: i32) => self.value = value\nenum Selection {source}\n" +
        "    Found(ref/Cell during MISSING)\n    Missing\nstruct Pair {left, right}\n    let first: ref/Cell during left\n    let second: ref/Cell during right\n" +
        "    public init(first: ref/Cell during left, second: ref/Cell during right)\n        self.first = first\n        self.second = second\n" +
        "    public func choose(self: ref/Self, useFirst: bool) -> Selection{selection}\n        origin selection.source == self.left and self.right\n" +
        "        if useFirst\n            return .Found(self.first)\n        return .Found(self.second)\nfunc run(useFirst: bool)\n" +
        "    var first = Cell.init(10)\n    var second = Cell.init(20)\n    let pair = Pair.init(first@ref, second@ref)\n    let result = pair.choose(useFirst)\n";

    // R4's i-field forms: D's Field h, whose Type MISSING spells, holds the C a constructor call takes.
    internal const string Fielded = "struct C {s}\n    public let r: ref/i32 during s\n    public init(r: ref/i32 during s) => self.r = r\nstruct D {s}\n" +
        "    public let h: MISSING\n    public init(h: C during s) => self.h = h@move\nfunc f(a: ref/i32) -> ref/i32 during a\n    let l = 5\n" +
        "    let d = D.init(C.init(l@ref))\n    return a\n";

    // X reaches T's B^64 Field through a handle beside a Field whose Type MISSING spells (T's storage height is 65).
    internal const string Beyond = Box + "struct X\n    public var t: Option<rc/T>\n    public var f: MISSING\nstruct T\n    public var x: Option<rc/X>\n" +
        "    public var y: " + Box64 + "\nfunc ft(t: uniq/T, p: raw/i32) => ()\nfunc fx(x: uniq/X, p: raw/i32) => ()\n";

    private const string Cell = "struct Cell<T> {s}\n    var item: uniq/T during s\n";

    private const string View = "struct View<T> {source}\n    let data: ref/T during source\n    public init(data: ref/T during source) => self.data = data\n" +
        "    public func read(self: ref/Self) -> ref/T during self.source => self.data\n    public func whole(self: ref/Self) -> ref/Self during self => self\n";

    private const string Slot = "struct Slot<T> {source}\n    var data: ref/T during source\n    public init(data: ref/T during source) => self.data = data\n" +
        "    public func put(self: uniq/Self, value: ref/T during source) => self.data = value\n" +
        "    public func item(self: uniq/Self) -> ref/T during self.source => self.data\n";

    // A slot kept by an exclusive Loan Field (design review A-B3: a raw parameter during `v.source` may store what the Dictionary admits).
    private const string KeptView = "struct View<K, V> {source}\n    K is Equatable\n    let loan: Loan<uniq/Dictionary<K, V> during source>\n" +
        "    public init(d: uniq/Dictionary<K, V> during source) => self.loan = Loan.init(d)\n";

    // An object payload that holds a Loan, and a local to lend it.
    private const string Lent = "struct Lent {a}\n    public var r: ref/i32 during a\n    public init(r: ref/i32 during a) => self.r = r\n" +
        "var x = 1\n";

    // A struct with an unstated raw Field beside a Loan-carrying one (the design review's S2 and R-B2 probes).
    private const string Hid = "struct Hid {a}\n    public var r: ref/i32 during a\n    public var p: raw/(ref/i32 during a)\n" +
        "    public init(r: ref/i32 during a, p: raw/(ref/i32 during a))\n        self.r = r\n        self.p = p\n";

    // A struct with an unstated raw Field, which a Field holds through an rc or arc handle (the G160-2c review's R2).
    private const string Shared = "struct Bx\n    public var v: raw/i32\n    public init(v: raw/i32) => self.v = v\n";

    // A derived struct whose base stores a Type argument at a Field (P13's U0 review: a supplied raw layer in a base).
    private const string Derived = "open struct BaseW<P>\n    public var v: P\nstruct Der<P> : BaseW<P>\n    public var n: i32\n";

    // A struct whose Field states no Origin for a raw-of-raw pointer (the N2 review's getSR).
    private const string Pp = "struct Pp\n    public var p: raw/(raw/u8)\n    public init(p: raw/(raw/u8)) => self.p = p\n";

    // A user stack of raw items, a user Iterator over them and a LendingIterator-only twin (the N2 designs' userPop, userIter and userLend).
    private const string RawStack = "struct RawStack\n    var items: Array<raw/u8>\n    public init(items: Array<raw/u8>) => self.items = items@move\n" +
        "    public func pop(self: uniq/Self) -> Option<raw/u8> => self.items.pop()\n";

    private const string RawIt = "struct RawIt\n    Self is Iterator\n    associate Iterator.Item is raw/u8\n    var items: Array<raw/u8>\n" +
        "    public init(items: Array<raw/u8>) => self.items = items@move\n    public func next(self: uniq/Self) -> Option<raw/u8> => self.items.pop()\n";

    private const string RawLend = "struct RawLend\n    Self is LendingIterator\n    associate LendingIterator.LentItem(step) is raw/u8\n    var items: Array<raw/u8>\n" +
        "    public init(items: Array<raw/u8>) => self.items = items@move\n    public func next(self: uniq/Self during step) -> Option<raw/u8> => self.items.pop()\n";

    // A user Type whose `drop` writes through a raw layer it holds (the C3 designs' clearDrop2 forms).
    private const string Writer = "struct Writer\n    public var p: raw/(raw/u8)\n    public var q: raw/u8\n    public init(q: raw/u8)\n" +
        "        self.p = null\n        self.q = q\n    drop\n        unsafe => *self.p = self.q\n";

    // Writer's and Holder's `drop` as a method twin over `self: uniq/Self` with the same body (PLAN G264's design probe d5).
    private const string Flush = "    public func flush(self: uniq/Self)\n        unsafe => *self.p = self.q\n";

    // A struct whose `drop` writes through a borrow Field, with its method twin (the G264 design's HRD).
    private const string Hrd = "struct HRD {a}\n    public var r: uniq/(uniq/i32 during a) during a\n    public init(r: uniq/(uniq/i32 during a) during a) => self.r = r\n" +
        "    drop\n        self.r@follow@follow = 0\n    public func flush(self: uniq/Self)\n        self.r@follow@follow = 0\n";

    // A generic layer whose `drop` writes through its unstated raw Fields, with its method twin.
    private const string Holder = "struct Holder<T>\n    public var item: T\n    public var p: raw/(raw/u8)\n    public var q: raw/u8\n" +
        "    public init(item: T, q: raw/u8)\n        self.item = item@move\n        self.p = null\n        self.q = q\n    drop\n        unsafe => *self.p = self.q\n" + Flush;

    // Callable-bounded values of the erased call `(uniq/P) -> ()`, each called on its part: an open struct, an object handle of it, an
    // abstract part and a Function value (the G264 design's probe d9).
    private const string ErasedParts = "open struct WB\n    public var p: raw/(raw/u8)\n    public var q: raw/u8\n" +
        "func callWB<F>(x: uniq/WB, f: F)\n    F is Callable<(uniq/WB) -> ()>\n    f(x)\nfunc callOWB<F>(x: uniq/(obj/WB), f: F)\n" +
        "    F is Callable<(uniq/(obj/WB)) -> ()>\n    f(x)\nfunc callT<T, F>(x: uniq/T, f: F)\n    F is Callable<(uniq/T) -> ()>\n    f(x)\n" +
        "func callFn<F>(x: uniq/(() -> ()), f: F)\n    F is Callable<(uniq/(() -> ())) -> ()>\n    f(x)\n";

    // A function whose body relies on its enclosing function's premise c outlives o (y's well-formedness), called while *q is written.
    private const string Enclosing = "func outer(y: ref/(ref/i32 during c) during o, q: uniq/i32 during c) -> i32\n";

    private const string Write = "    let x = q@follow@ref\n";

    // Review B2's setup: Function Type fields with fixed Origins, converted under a premise relating them, and a function applying one.
    private const string Erased = "func second2(y: ref/i32, z: ref/i32 during p) -> ref/i32 during q\n    origin p outlives q\n    return z\n" +
        "func first2(z: ref/i32 during p) -> ref/i32 during q\n    origin p outlives q\n    return z\n" +
        "struct H {a, b}\n    public var g: (ref/i32, ref/i32 during a) -> ref/i32 during b\n    public var k: (ref/i32 during a) -> ref/i32 during b\n" +
        "func make(h: uniq/H{s})\n    origin s.a outlives s.b\n    h.g = second2\n    h.k = first2\n" +
        "func apply(f: ref/((ref/i32 during a) -> ref/i32 during b), x: ref/i32 during a) -> ref/i32 during b => f(x)\n";

    // SPEC 15.6.4, 15.6.1, 15.3.3: a result during an input retains it, a meet retains each operand, a declared `outlives` clause or the
    // well-formedness of a borrowed input makes another input reachable, and an Origin of no output's atoms' closure is not retained. A
    // result naming an abstract part of a borrowed input's referent retains it as Content (step 4). A reachable input is retained by
    // Value only where the result can hold its value or a borrow formed through it: `ref/(ref/i32 during c) during o -> ref/i32 during
    // o` keeps `b`'s Content (`**b` lies past a second shared layer; the owner's 「SPEC 15.6.4 の読み (iii) と (β)」, DECISIONS R4-M154).
    [Theory]
    [InlineData("func f<T>(x: ref/T, y: ref/T) -> ref/T during x => x", "result {0 Value, 1 Content}")]
    [InlineData("func f(x: ref/i32, y: ref/i32) -> ref/i32 => y", "result {0 Value, 1 Value}")]
    [InlineData("func f(x: ref/i32, y: ref/i32) -> ref/i32 during x => x", "result {0 Value}")]
    [InlineData("func f(x: ref/i32 during a, y: ref/i32 during b) -> ref/i32 during b\n    origin a outlives b\n    return x", "result {0 Value, 1 Value}")]
    [InlineData("func f(x: ref/i32 during a, y: ref/i32 during b) -> ref/i32 during b => y", "result {1 Value}")]
    [InlineData("func f(b: ref/(ref/i32 during c) during o) -> ref/i32 during o => b@follow", "result {0 Content}")]
    [InlineData("func f(b: ref/(ref/i32 during c) during o, x: ref/i32 during c) -> ref/i32 during o => x", "result {0 Content, 1 Value}")]
    [InlineData("func f(x: ref/i32, n: i32) -> i32 => n", "")]
    [InlineData("func f<s/T>(value: s/T, count: s/i32) -> s/T\n    s is owner or uniq\n    T is Copy\n    _ = count\n    return value", "result {0 Value}, store 0: own {0 Content}, reached {}")]
    [InlineData("func f<s/T>(value: s/T, count: s/i32) -> s/T\n    s is owner or ref\n    T is Copy\n    _ = count\n    return value", "result {0 Value}")]
    public void DeclaredRelationsDecideTheResult(string function, string expected)
        => Assert.Equal(expected, Declared(Bind(function + "\n"), "f"));

    // SPEC 15.6.4, 15.6.1, 15.3.4: a nested function or closure is read under the premises its body is checked under, its enclosing
    // function's included: an input's well-formedness (c outlives o) there makes `value` reach the result, through a direct call, a
    // Closure called by its declared signature (C8; its parameter after its call receiver at input 0, DECISIONS R4-M148) and an enclosing
    // result's well-formedness alike.
    [Fact]
    public void EnclosingPremisesAreTheBodysPremises()
    {
        var nested = Bind(Enclosing + "    func pass(value: ref/i32 during c) -> ref/i32 during o => value\n" + Write + "    let r = pass(x)\n    q@follow = 5\n    return r@follow\n");
        Assert.Equal("result {0 Value}", Declared(nested, "pass"));
        Assert.Equal(["result {0 Value}"], Calls(nested, "pass"));
        var closure = Bind(Enclosing + "    let k = func (value: ref/i32 during c) -> ref/i32 during o => value\n" + Write + "    let r = k(x)\n    q@follow = 5\n    return r@follow\n");
        Assert.Equal(["result {1 Value}"], ValueCalls(closure));
        var result = Bind("func outer(q: uniq/i32 during c) -> Option<ref/(ref/i32 during c) during o>\n    func pass(value: ref/i32 during c) -> ref/i32 during o => value\n" +
            Write + "    let r = pass(x)\n    q@follow = 5\n    _ = r@follow\n    return .None\n");
        Assert.Equal("result {0 Value}", Declared(result, "pass"));
    }

    // SPEC 15.6.4, 15.6.3: a receiver retained through its referent's Origins only is Content (a read through `self`), through its own
    // outer Origin Value; a constructor's result retains the borrow it stores.
    [Theory]
    [InlineData("init", "result {0 Value}")]
    [InlineData("read", "result {0 Content}")]
    [InlineData("whole", "result {0 Value}")]
    public void ReceiversRetainByTheirLayer(string method, string expected)
        => Assert.Equal(expected, Declared(Bind(View), method));

    // SPEC 15.6, 15.6.4, 15.3.5: an input written through an exclusive or raw layer, also one an owned aggregate holds (an invariant slot),
    // is an output; another input whose Origin outlives the written positions is stored there, the target's own content too. An exclusive
    // input with no Origin below its layer stores nothing. What lies behind a shared layer reached before a writable slot is only read
    // (SPEC 15.6.2: a shared input, an rc payload held by value), a raw pointer read past it included (SPEC 5.7, 8.11.4 rule 1; the
    // expectation the SPEC revision R4-M95 changed: `ref/(raw/…)` stores nothing); a value stored into a writable slot brings its whole
    // Type, so an Origin below an rc or arc layer there, or below a pair layer admitting only them, is stored (DECISIONS BU-1). A raw
    // input's own pointee retains it as a read through it, never its own value (SPEC 15.6.4 as DECISIONS R4-M116 revised it), as a
    // by-value input's own exclusive referent does (`Cell`'s `item` cannot hold the Cell; DECISIONS R4-M154).
    [Theory]
    [InlineData("func f(dst: uniq/(ref/i32 during q), v: ref/i32 during q) => dst@follow = v", "store 0: own {0 Content, 1 Value}, reached {}")]
    [InlineData("func f(dst: uniq/i32, v: ref/i32) => dst@follow = v@follow", "")]
    [InlineData("func f(dst: ref/(ref/i32 during q), v: ref/i32 during q) -> i32 => v@follow", "")]
    [InlineData("func f<T>(dst: uniq/T, value: T) => dst@follow = value@move", "store 0: own {0 Content, 1 Value}, reached {}")]
    [InlineData("func f<T>(storage: raw/T, value: T) => ()", "store 0: own {0 Content, 1 Value}, reached {}")]
    [InlineData("func f(storage: ref/(raw/(ref/i32 during q)), v: ref/i32 during q) => ()", "")]
    [InlineData(Cell + "func f(dst: Cell<ref/i32 during q>, v: ref/i32 during q) => ()", "store 0: own {0 Content, 1 Value}, reached {}")]
    [InlineData(Cell + "func f(dst: ref/Cell<ref/i32 during q>, v: ref/i32 during q) => ()", "")]
    [InlineData(Box + "func f(dst: uniq/(rc/B<ref/i32 during q>), v: rc/B<ref/i32 during q>) => dst@follow = v@move", "store 0: own {0 Content, 1 Value}, reached {}")]
    [InlineData(Box + "func f(dst: uniq/(arc/B<ref/i32 during q>), v: ref/i32 during q) => ()", "store 0: own {0 Content, 1 Value}, reached {}")]
    [InlineData(Cell + Box + "func f(dst: Cell<rc/B<ref/i32 during q>>, v: rc/B<ref/i32 during q>) => ()", "store 0: own {0 Content, 1 Value}, reached {}")]
    [InlineData(Box + "func f<s/T>(dst: uniq/Option<s/B<ref/i32 during q>>, v: ref/i32 during q)\n    s is rc or arc\n    return", "store 0: own {0 Content, 1 Value}, reached {}")]
    [InlineData(Cell + "func f(dst: rc/Cell<ref/i32 during q>, v: ref/i32 during q) => ()", "")]
    [InlineData(Box + "func f(dst: ref/(rc/B<ref/i32 during q>), v: rc/B<ref/i32 during q>) => ()", "")]
    public void ExclusiveInputsAreStores(string function, string expected)
        => Assert.Equal(expected, Declared(Bind(function + "\n"), "f"));

    // SPEC 15.6.4, 5.2.1 (DECISIONS BU-1): an element appended to an Array brings its whole Type into the receiver's writable storage, so
    // an Origin below an rc or arc element's layer is stored there as one in an owned or obj element is.
    [Fact]
    public void HandleElementsAreStores()
    {
        var c = Bind("struct S {a}\n    public var r: ref/i32 during a\n    public init(r: ref/i32 during a) => self.r = r\nvar x = 1\n" +
            "var e: Array<S> = []\ne.append(S.init(r: x@ref))\nvar o: Array<obj/S> = []\no.append(Kimi.Intrinsics.makeObj(S.init(r: x@ref)))\n" +
            "var p: Array<rc/S> = []\np.append(Kimi.Intrinsics.makeRc(S.init(r: x@ref)))\n" +
            "var q: Array<arc/S> = []\nq.append(Kimi.Intrinsics.makeArc(S.init(r: x@ref)))\n");
        Assert.Equal(Enumerable.Repeat("store 0: own {0 Content, 1 Value}, reached {}, referent store 0", 4), Calls(c, "append"));
    }

    // SPEC 15.6.4: a method writing through `uniq/Self` stores an input into the receiver's referent; one reading through it is
    // Content into its result.
    [Theory]
    [InlineData("put", "store 0: own {0 Content, 1 Value}, reached {}")]
    [InlineData("item", "result {0 Content}, store 0: own {0 Content}, reached {}")]
    public void ExclusiveReceiversAreStores(string method, string expected)
        => Assert.Equal(expected, Declared(Bind(Slot), method));

    // SPEC 15.3.5: a call whose contract instantiates a growing recursive Type (a Nest<T> stores a Nest<(T, T)>) passes the bound of
    // Binding's storage instantiation, so its retention is the Limit, with no rows (A5 records Unsupported_Kd), as A4's effects are; a
    // repeating recursive Type (a List<T> stores a List<T>) still gives rows.
    [Fact]
    public void GrowingRecursiveContractsAreTheLimit()
    {
        var c = Bind("struct Nest<T>\n    public var value: T\n    public var next: Option<rc/Nest<(T, T)>> = .None\n    public init(value: T) => self.value = value\n" +
            "struct List<T>\n    public var value: T\n    public var next: Option<rc/List<T>> = .None\n    public init(value: T) => self.value = value\n" +
            "func inspect<T>(value: ref/T) => ()\nvar n = 1\nlet nest = Nest<ref/i32>.init(value: n@ref)\ninspect(nest@ref)\n" +
            "let list = List<ref/i32>.init(value: n@ref)\ninspect(list@ref)\n");
        Assert.Equal(["limit", "result {0 Value}"], Calls(c, "init"));
        Assert.Equal(["limit", string.Empty], Calls(c, "inspect"));
        var call = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Select(static x => x.CallOf()!).First(static x => x.Target.Name == "inspect");
        Assert.Same(c.Types.Retains(call.Target, c.Types.SubstOf(call), null), c.Types.Retains(call.Target, c.Types.SubstOf(call), null));
        Assert.Throws<InvalidOperationException>(() => c.Types.Retains(call.Target, c.Types.SubstOf(call), null).Result);
    }

    // SPEC 4.8.1, 4.8.2, 15.6.4 step 4: a Slice or UniqSlice input is a borrow of its elements, its outer Origin Value and the elements'
    // Origins Content, which a UniqSlice lets the call write: `toArray` keeps the dependencies inside `T` but not the outer borrow, and
    // `appendCopies` stores the elements' only.
    [Fact]
    public void ViewsAreBorrowedInputs()
    {
        var c = Bind("var n = 1\nvar refs: [_ of ref/i32] = [n@ref]\nlet copied = refs.slice().toArray()\nvar all: Array<ref/i32> = []\nall.appendCopies(refs.slice())\n" +
            "func keep(view: Slice<ref/i32 during q>, v: ref/i32 during q) -> Option<ref/i32 during q> => .None\n" +
            "func put(view: UniqSlice<ref/i32 during q>, v: ref/i32 during q) => ()\nfunc first(view: UniqSlice<ref/i32 during q>) -> Option<ref/i32 during q> => .None\n");
        Assert.Equal(["result {0 Content}"], Calls(c, "toArray"));
        Assert.Equal(["store 0: own {0 Content, 1 Content}, reached {}, referent store 0"], Calls(c, "appendCopies"));
        Assert.Equal("result {0 Content, 1 Value}", Declared(c, "keep"));
        Assert.Equal("store 0: own {0 Content, 1 Value}, reached {}", Declared(c, "put"));
        Assert.Equal("result {0 Content}, store 0: own {0 Content}, reached {}", Declared(c, "first"));
    }

    // SPEC 15.6.4 step 4, 8.1.2 (DECISIONS BU-3): a pair layer whose binder admits only borrows is a borrowed input in every admitted
    // case, its slot Value and its target Content, which an admitted exclusive borrow lets the call write, as for a concrete `uniq/T`;
    // the original `s/T` and an application alike; the pair's slot still reaches storage that holds the pair by value. A pair admitting
    // `owner`, raw or an object owner is no borrow in that case, so its target stays Value: a Value source for the result and for the
    // stores into other inputs. In its exclusive cases the call writes through it the referent of the argument's own borrow, which holds
    // other inputs' values (`x`) and other occurrences of its own input (a Tuple's second element) and carries what a read through the
    // argument carries, never the pair value itself (SPEC 15.6.4: the argument's own value is retained only by storage that can hold a
    // value of its Type; DECISIONS BU-49, PLAN G147 (8i), A5 step 2 plan 2c-4): `store 0: own {0 Content}, reached {}`, as the
    // borrow-only twins read, never Value; the owner-only twin writes no caller storage.
    [Theory]
    [InlineData("func f<s/T>(a: s/T, b: ref/i32)\n    s is ref or uniq\n    return", "store 0: own {0 Content}, reached {}")]
    [InlineData("func f<s/T>(a: s/T, b: ref/i32)\n    s is uniq\n    return", "store 0: own {0 Content}, reached {}")]
    [InlineData("func f<s/T>(a: s/T, b: ref/i32)\n    s is objref or objuniq\n    return", "store 0: own {0 Content}, reached {}")]
    [InlineData("func f<s/T>(a: s/T, b: ref/i32)\n    s is ref\n    return", "")]
    [InlineData("func f<s/T>(a: s/Option<T>, b: ref/i32)\n    s is ref or uniq\n    return", "store 0: own {0 Content}, reached {}")]
    [InlineData("func f<s/T>(a: s/T, b: ref/i32) -> s/T\n    s is ref or uniq\n    return a", "result {0 Value}, store 0: own {0 Content}, reached {}")]
    [InlineData(
        "func f<s/T>(a: s/T, b: uniq/Array<s/T>)\n    s is ref or uniq\n    return",
        "store 0: own {0 Content, 1 Content}, reached {}, store 1: own {0 Value, 1 Content}, reached {0 Content, 1 Content}")]
    [InlineData("func f<s/T>(a: s/T, b: ref/i32)\n    s is owner or uniq\n    return", "store 0: own {0 Content}, reached {}")]
    [InlineData("func f<s/T>(a: s/T, b: ref/i32)\n    s is uniq or raw\n    return", "store 0: own {0 Content}, reached {}")]
    [InlineData("func f<s/T>(a: s/T, b: ref/i32)\n    s is objuniq or obj\n    return", "store 0: own {0 Content}, reached {}")]
    [InlineData("func f<s/T>(a: s/T, b: ref/i32)\n    s is owner\n    return", "")]
    [InlineData("func f<s/T>(a: s/T, x: T)\n    s is owner or uniq\n    T is Copy\n    return", "store 0: own {0 Content, 1 Value}, reached {}")]
    [InlineData("func f<s/T>(a: (s/T, T), b: ref/i32)\n    s is owner or uniq\n    T is Copy\n    return", "store 0: own {0 Value}, reached {}")]
    [InlineData("func f<s/T>(a: s/T, b: ref/i32) -> s/T\n    s is owner or uniq\n    T is Copy\n    return a", "result {0 Value}, store 0: own {0 Content}, reached {}")]
    public void BorrowPairLayersAreBorrowedInputs(string function, string expected)
        => Assert.Equal(expected, Declared(Bind(function + "\n"), "f"));

    // The same rule for every exclusive borrow layer a by-value input holds (DECISIONS BU-49; RetentionWalk): a concrete `uniq` layer and
    // a pair layer admitting one, nested in a Tuple or an Option, borrow-only or owner-admitting, address storage that carries what a read
    // through the input carries, a Tuple's other element's referent included (`*a.1` written into `*a.0`), never the input's own value:
    // `store 0: own {0 Content}, reached {}`, each pair form beside its concrete twin. Through no exclusive layer (`(T, i32)`) the call
    // writes nothing. The referent `ref/i32` can hold neither the layer's borrow nor a borrow formed through it, whatever its Origins, so
    // the input's own value is not retained there, `1 Content`, in an Option and a Tuple as at the top level (the owner's WK decision,
    // DECISIONS R4-M154, superseding BU-49's own-Origin row, which kept `1 Value store 1` with `during q` on both layers).
    [Theory]
    [InlineData("func f(a: ref/i32 during q, b: Option<uniq/(ref/i32 during q) during q>) => ()", "store 1: own {0 Value, 1 Content}, reached {}")]
    [InlineData("func f(a: ref/i32 during q, b: (uniq/(ref/i32 during q) during q, i32)) => ()", "store 1: own {0 Value, 1 Content}, reached {}")]
    [InlineData("func f(a: ref/i32 during q, b: uniq/(ref/i32 during q) during q) => ()", "store 1: own {0 Value, 1 Content}, reached {}")]
    [InlineData("func f(a: ref/i32 during o, b: Option<uniq/(ref/i32 during q) during o>) => ()", "store 1: own {1 Content}, reached {}")]
    [InlineData("func f<s/T>(a: (s/T, i32), b: ref/i32)\n    s is uniq\n    return", "store 0: own {0 Content}, reached {}")]
    [InlineData("func f<T>(a: (uniq/T during p, i32), b: ref/i32) => ()", "store 0: own {0 Content}, reached {}")]
    [InlineData("func f<s/T>(a: Option<s/T>, b: ref/i32)\n    s is uniq\n    return", "store 0: own {0 Content}, reached {}")]
    [InlineData("func f<T>(a: Option<uniq/T during p>, b: ref/i32) => ()", "store 0: own {0 Content}, reached {}")]
    [InlineData("func f<s/T>(a: (s/T, s/T), b: ref/i32)\n    s is uniq\n    return", "store 0: own {0 Content}, reached {}")]
    [InlineData("func f<s/T>(a: (s/T, s/T), b: ref/i32)\n    s is owner or uniq\n    return", "store 0: own {0 Content}, reached {}")]
    [InlineData("func f<T>(a: (uniq/T during p, uniq/T during p), b: ref/i32) => ()", "store 0: own {0 Content}, reached {}")]
    [InlineData("func f<T>(a: (T, i32), b: ref/i32) => ()", "")]
    public void ExclusiveLayersHeldByValueStoreTheirContent(string function, string expected)
        => Assert.Equal(expected, Declared(Bind(function + "\n"), "f"));

    // The same at calls over exclusive layers held by value (PLAN G147 (8i)'s programs; A5 step 2 plan 2c-4; DECISIONS BU-49): a generic
    // caller passing its `s/T` by value, directly, as a bare capture of a by-value Closure (owner-or-uniq or uniq-only) or as a result's
    // input, and a Closure capturing `y@uniq`, store other inputs into the referent and what a read through the argument carries, keep
    // the value in the result, and never store the argument's own value in its referent; a pair the callee reads through its exclusive
    // receiver keeps rule G's `0 Content store 0` (DECISIONS R4-M148).
    [Fact]
    public void CallsOverExclusiveLayersHeldByValueStoreTheirContent()
    {
        var c = Bind("func put<U>(slot: uniq/U, y: U) -> () => slot@follow = y@move\nfunc keep<F>(f: F) -> F => f@move\n" +
            "func second<A, B>(a: A, b: B) -> B => b@move\n" +
            "func use<s/T, t/U>(value: s/T, other: s/T, third: t/U, x: T) -> ()\n    s is owner or uniq\n    t is owner or uniq\n" +
            "    T is Copy\n    U is Copy\n    var v = value@move\n    var slot = other@move\n    let h = func [v] () -> ()\n        _ = v@follow\n" +
            "    let kept = keep(h@move)\n    kept()\n    let child = v\n    put(slot@uniq, child@move)\n    _ = slot@follow\n" +
            "    let r = second(x, third@move)\n    _ = r@follow\n" +
            "func useUniq<s/T>(value: s/T) -> ()\n    s is uniq\n    T is Copy\n    var v = value@move\n    let h = func [v] () -> ()\n" +
            "        _ = v@follow\n    let kept = keep(h@move)\n    kept()\n" +
            "func useCaptured<T>(other: T) -> ()\n    T is Copy\n    var y = other\n    let g = func [y@uniq] () -> ()\n        _ = y@follow\n" +
            "    let kept = keep(g@move)\n");
        Assert.Equal(["store 0: own {0 Content, 1 Value}, reached {0 Content, 1 Content}, store 1: own {0 Content, 1 Content}, reached {}"], Calls(c, "put"));
        Assert.Equal(["result {0 Value}, store 0: own {0 Content}, reached {}", "result {0 Value}, store 0: own {0 Content}, reached {}", "result {0 Value}, store 0: own {0 Content}, reached {}"], Calls(c, "keep"));
        Assert.Equal(["result {1 Value}, store 1: own {1 Content}, reached {}"], Calls(c, "second"));
    }

    // The same for a generic caller passing its own `s/T` (A5's case 139c, a non-Copy T): the call's Subst keeps the caller's pair, whose
    // binder admits only borrows, so the call stores only the argument's referent into it; and for an erased callable over such a pair.
    [Fact]
    public void CallsOverBorrowPairLayersAreBorrowedInputs()
    {
        var c = Bind("func inner<s/T>(a: s/T, b: i32) -> ()\n    s is ref or uniq\n    return\nfunc peek<T>(x: ref/T) -> i32 => 1\n" +
            "func use<s/T>(p: s/T, item: T) -> ()\n    s is ref or uniq\n    var x = item\n    inner(x@(s/T), peek(x@ref))\n" +
            "func apply<s/T>(g: (s/Option<T>) -> (), x: s/Option<T>) -> ()\n    s is ref or uniq\n    g(x)\n");
        Assert.Equal(["store 0: own {0 Content}, reached {}"], Calls(c, "inner"));
        Assert.Equal(["store 0: own {0 Content}, reached {0 Content}"], ValueCalls(c));
    }

    // SPEC 15.6.4 step 1, 15.3.5 (DECISIONS R4-M36): an abstract carrier may hold borrows its Origins do not show, so an input naming a
    // Type parameter the result names is retained, as is one whose carrier lies below a layer whose Origin the result's outlives; a call
    // instantiating the parameter retains by the instance, nothing for an Origin-free one.
    [Fact]
    public void AbstractCarriersRetainUnderTheirInstances()
    {
        var c = Bind("func pick<T>(a: T, b: ref/i32) -> T => a@move\nfunc hidden<T>(x: ref/T during p, y: T) -> Option<ref/i32 during p> => .None\n" +
            "var a = 1\nvar b = 2\nlet r = pick(a@ref, b@ref)\nlet n = pick(3, b@ref)\n");
        Assert.Equal("result {0 Value}", Declared(c, "pick"));
        Assert.Equal("result {0 Value, 1 Value}", Declared(c, "hidden"));
        Assert.Equal(["result {0 Value}", string.Empty], Calls(c, "pick"));
    }

    // SPEC 15.6, design A3 3 (Φ_E): a result Origin tied to no input and not static, or a carrier no input names, is untied; a static
    // result is not.
    [Theory]
    [InlineData("func f() -> Option<ref/i32 during s> => .None", "untied")]
    [InlineData("func f(x: ref/i32) -> Option<ref/i32 during s> => .None", "untied")]
    [InlineData("func f(n: i32) -> Option<ref/i32> => .None", "")]
    [InlineData("func f<T>() -> Option<T> => .None", "untied")]
    public void UntiedResultsNameNoInput(string function, string expected)
        => Assert.Equal(expected, Declared(Bind(function + "\n"), "f"));

    // C8 (DECISIONS R4-M41): a Value call is read over the Callable signature its callee declares, a function item's, a Closure's or a
    // Function Type's, whose per-call Origins are the call's fresh regions; a Function Type input stores no per-call Origin of its own. A
    // Closure's parameters follow its call receiver at input 0 (DECISIONS R4-M148).
    [Fact]
    public void CallableSignaturesRetainByTheirOwnOrigins()
    {
        var c = Bind("func pass(value: ref/i32) -> ref/i32 => value\nfunc apply(f: (ref/i32) -> ref/i32, x: ref/i32) -> ref/i32 => f(x)\n" +
            "func meet(f: (ref/i32, ref/i32) -> ref/i32, x: ref/i32, y: ref/i32) -> ref/i32 => f(x, y)\n" +
            "var a = 1\nlet item = pass\nlet r = item(a@ref)\nlet closure = func (z: ref/i32) -> ref/i32 => z\nlet s = closure(a@ref)\n");
        Assert.Equal("result {1 Value}", Declared(c, "apply"));
        Assert.Equal(["result {0 Value}", "result {0 Value, 1 Value}", "result {0 Value}", "result {1 Value}"], ValueCalls(c));
    }

    // SPEC 15.8.2, 7.7.2 (DECISIONS R4-M126, R4-M148): a Closure's contract has its call receiver at input 0, `R/Env during ρ` over the
    // definition's environment (the calling local's Type names the Origins its initializer infers, so a parameter would reach no binding
    // there), its bindings Fields of the referent: a `var` binding of an Exclusive receiver is written (exchange, varPair: `1 Value store 0`
    // and the receiver's own content, `0 Content store 0`), a `let` one never (letPair), what a captured exclusive reference refers to is
    // (holder); a result naming a binding's Origin is the receiver's content (exchange, shared, through `ref/`); a Consuming receiver is
    // the environment by value, whose bindings are not written (consuming). A Closure captured into another is read as its body sees it,
    // also from a `var` local, whose own Type names its initializer's inferred Origins (nestedGet over get, nestedRemember over remember:
    // BU's review B1). No Closure contract has referent stores (a 4.6-4.9 operation's).
    [Fact]
    public void AClosuresCallReceiverIsInputZero()
    {
        var c = Bind("func use(z: uniq/i32, x: ref/i32, w: uniq/i32) -> i32\n    origin z outlives x\n" +
            "    var state: ref/i32 during x = x\n" +
            "    var exchange = func [var state] (value: ref/i32 during x) -> ref/i32 during x\n        let old = state\n        state = value\n        return old\n" +
            "    _ = exchange(z@follow@ref)\n" +
            "    let r: ref/i32 during x = x\n" +
            "    var letPair = func [r, w] (v: ref/i32 during x) -> i32\n        w@follow = v@follow\n        return r@follow\n    _ = letPair(x)\n" +
            "    var varPair = func [var r, w] (v: ref/i32 during x) -> i32\n        w@follow = v@follow\n        return r@follow\n    _ = varPair(x)\n" +
            "    var holder: ref/i32 during x = x\n" +
            "    var store = func [holder@uniq] (b: ref/i32 during x) -> i32\n        holder@follow = b\n        return 0\n    _ = store(x)\n" +
            "    let n: i32 = 1\n    let consuming = func [n] () -> i32 => n@move\n    _ = consuming()\n" +
            "    let shared = func [r] () => r\n    _ = shared()\n    let viaRef = shared@ref\n    _ = viaRef()\n" +
            "    var get = func [r] () -> ref/i32 during x => r\n    let nestedGet = func [get@move] () -> ref/i32 during x => get()\n" +
            "    _ = nestedGet()\n" +
            "    var remember = func [var r] (value: ref/i32 during x) -> ref/i32 during x\n        let old = r\n        r = value\n        return old\n" +
            "    var nestedRemember = func [var remember@move] (b: ref/i32 during x) -> ref/i32 during x => remember(b)\n" +
            "    _ = nestedRemember(x)\n    return 0\n");
        Assert.Equal(
            ["result {0 Content, 1 Value}, store 0: own {0 Content, 1 Value}, reached {}", string.Empty, "store 0: own {0 Content, 1 Value}, reached {}",
                "store 0: own {}, reached {0 Content, 1 Value}", string.Empty, "result {0 Content}", "result {0 Content}", "result {0 Content}",
                "result {0 Content}", "result {0 Content, 1 Value}, store 0: own {0 Content, 1 Value}, reached {}",
                "result {0 Content, 1 Value}, store 0: own {0 Content, 1 Value}, reached {}"],
            ValueCalls(c));
    }

    // SPEC 15.8.2, 8.10, 7.7.3 (DECISIONS R4-M126 D-6, R4-M128, R4-M148): one predicate says whether a Value callee's contract has the call
    // receiver input, reading the callee through one Semantics layer: a Closure, borrowed or not, has it; a function item, a Function value
    // and a Callable-bounded value do not. The receiver a Closure Type's case admits is its definition's case-free receiver unless that is
    // Shared, else Exclusive where a capture whose bare acquisition Reborrows by case has an exclusive layer as its case component: `value`
    // of `s/T` (s is owner or uniq) Copies in the owner case and Reborrows in the uniq case, directly (marked) or through a nested Closure's
    // bare entry (nested), so the owner view admits Shared and the uniq view Exclusive, and the instance gives Receiver, Exclusive.
    [Fact]
    public void AClosureCalleeHasTheReceiverInputAndItsCaseReceiver()
    {
        var c = Bind("func pass(value: ref/i32) -> ref/i32 => value\nfunc apply(f: (ref/i32) -> ref/i32, x: ref/i32) -> ref/i32 => f(x)\n" +
            "func bounded<F>(g: ref/F, x: ref/i32) -> ref/i32\n    F is Callable<(ref/i32) -> ref/i32>\n    return g(x)\n" +
            "var a = 1\nlet item = pass\n_ = item(a@ref)\nvar k = func (z: ref/i32) -> ref/i32 => z\n_ = k(a@ref)\nlet kr = k@ref\n_ = kr(a@ref)\n" +
            "let ku = k@uniq\n_ = ku(a@ref)\n");
        Assert.Equal(
            [false, false, false, true, true, true],
            KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Select(static x => CallPlan.Of(x, 0)).OfType<CallPlan>()
                .Where(static x => x.Kind == CalleeKind.Value).Select(x => c.Types.HasReceiverInput(x.CalleeType)).ToArray());

        var m = MirBuildTest.Compile("func marked<s/T>(value: s/T) -> ()\n    s is owner or uniq\n    T is Copy\n" +
            "    var h = func [value] () -> ()\n        let local = value\n        _ = local\n    h()\n" +
            "func nested<s/T>(value: s/T) -> ()\n    s is owner or uniq\n    T is Copy\n" +
            "    var outer = func [value] () -> ()\n        var inner = func [value] () -> ()\n            let local = value\n            _ = local\n        inner()\n" +
            "    outer()\npublic func main() => ()\n");
        var program = m.BuildMir();
        var cases = new MirCases(m.Types);
        string[] receivers = [.. new[] { "marked", "nested" }.SelectMany(name => Receivers(program.Bodies.ToArray().Single(x => x.Key.Decl?.Name == name)))];
        Assert.Equal(["Ref Uniq Uniq", "Ref Uniq Uniq"], receivers);

        IEnumerable<string> Receivers(MirBody body)
        {
            var local = Enumerable.Range(0, body.Locals.Length).First(i => body.Locals[i].Type is { Kind: BoundTypeKind.Closure });
            var views = new List<string>();
            for (var view = 0L; view < MirCases.Count(body); view++)
            {
                cases.Select(body, view);
                views.Add(m.Types.ClosureReceiverIn(cases.Type(body.Locals[local].Type!)).ToString());
            }

            var definition = BoundClosure.Of(body.Locals[local].Type)!;
            views.Add(m.Types.ClosureReceiverIn(definition.EnvironmentType!).ToString());
            yield return string.Join(' ', views);
        }
    }

    // Owner decision 2026-10-17 (DECISIONS R4-M52), SPEC 15.6.4: a callable whose implementation the call cannot see, a Function Type
    // value or a callable Type a contract names, was compared under premises no view reads, so each fixed Origin of its inputs reaches
    // each fixed Origin of its outputs. `make` stores functions returning their last input under `s.a outlives s.b`, so each `r` is `x`:
    // through a field's signature (its own per-call input aside), with no clause in the calling definition, and through `apply`'s
    // callable input, whose body maps `a` to `b`.
    [Theory]
    [InlineData("func use(h: ref/H{s}, q: uniq/i32 during s.a, w: ref/i32) -> i32\n    origin s.a outlives s.b\n    let x = q@follow@ref\n    let r = h.g(w, x)\n    q@follow = 5\n    return r@follow\n", "result {0 Value} | result {1 Value}")]
    [InlineData("func use(h: ref/H{s}, q: uniq/i32 during s.a) -> i32\n    let x = q@follow@ref\n    let r = h.k(x)\n    q@follow = 5\n    return r@follow\n", "result {0 Value} | result {0 Value}")]
    [InlineData("func use(h: ref/H{s}, q: uniq/i32 during s.a) -> i32\n    let x = q@follow@ref\n    let r = apply(h.k@ref, x)\n    q@follow = 5\n    return r@follow\n", "result {0 Value} | apply result {0 Content, 1 Value}")]
    public void ErasedCallablesRelateTheirFixedOrigins(string use, string expected)
    {
        var c = Bind(Erased + use);
        Assert.Equal(expected, string.Join(" | ", ValueCalls(c).Concat(Calls(c, "apply").Select(static x => "apply " + x))));
    }

    // The same rule for a Callable bound (SPEC 8.10): a value of a bounded Type parameter is erased (C8), and a callee's bounds are
    // erased callables its body may call: with Origin sets its clauses relate to `x` and the result (which retains `g` without the
    // bound too); with carriers, whose hidden Origins are fixed too (R4-M36), `map`'s result retains `x`, in its contract, through `g`
    // in its body and as a call instantiates it.
    [Fact]
    public void CallableBoundsRelateTheirFixedOrigins()
    {
        var sets = Bind("struct View<T> {source}\n    let data: ref/T during source\n    public init(data: ref/T during source) => self.data = data\n" +
            "func f<F>(g: ref/F, x: View<i32>{xs}) -> View<i32>{rs}\n    F is Callable<(View<i32>{a}) -> View<i32>{b}>\n    origin a.source == xs.source\n" +
            "    origin b.source == rs.source\n    return g(x@move)\n");
        Assert.Equal("result {0 Value, 1 Value}", Declared(sets, "f"));
        Assert.Equal(["result {0 Value}"], ValueCalls(sets));
        var map = Bind("func map<F, T, U>(g: ref/F, x: T) -> U\n    F is Callable<(T) -> U>\n    return g(x@move)\n" +
            "var n = 1\nlet k = func (z: ref/i32) -> ref/i32 => z\nlet r = map(k@ref, n@ref)\n");
        Assert.Equal("result {1 Value}", Declared(map, "map"));
        Assert.Equal(["result {0 Value}"], ValueCalls(map));
        Assert.Equal(["result {1 Value}"], Calls(map, "map"));
    }

    // SPEC 15.6.4 (β′; DECISIONS R4-M154): the callables a callee's premises and inputs hand its body are erased callables it may call: each
    // requirement of a Contract premise on its subject with the Contract reference's bindings (a builder of Self from a Contract Type
    // argument, a writer through `uniq/Self`, one returning an associated Type) and a Function value in a nominal Field retain `y` in
    // `slot`'s referent beside its own Content (and `m`'s carriers); a requirement whose borrowed input's Origin is its own relates nothing
    // (SPEC 15.3.7). A requirement call on an abstract Self is erased itself: `T.mk(y)`'s result retains `y`, or nothing, untied.
    [Theory]
    [InlineData("contract MkE<E>\n    func mk(r: E) -> Self\nfunc f<T, E>(slot: uniq/T, y: E) -> ()\n    T is MkE<E>\n    slot@follow = T.mk(y@move)", "store 0: own {0 Content, 1 Value}, reached {}", "result {0 Value}")]
    [InlineData("contract Absorb<E>\n    func absorb(self: uniq/Self, r: E) -> ()\nfunc f<T, E>(slot: uniq/T, y: E) -> ()\n    T is Absorb<E>\n    slot.absorb(y@move)", "store 0: own {0 Content, 1 Value}, reached {0 Content, 1 Value}", "")]
    [InlineData("contract Holds<E>\n    associate Box\n    func wrap(r: E) -> Self.Box\nfunc f<T, E>(slot: uniq/T.Box, y: E) -> ()\n    T is Holds<E>\n    slot@follow = T.wrap(y@move)", "store 0: own {0 Content, 1 Value}, reached {}", "")]
    [InlineData("struct Maker<E, T>\n    public var make: (E) -> T\nfunc f<T, E>(slot: uniq/T, y: E, m: Maker<E, T>) -> ()\n    slot@follow = m.make(y@move)", "store 0: own {0 Content, 1 Value, 2 Value}, reached {}", "")]
    [InlineData("contract Mk\n    func mk(r: ref/i32) -> Self\nfunc f<T>(slot: uniq/T, y: ref/i32 during o) -> ()\n    T is Mk\n    slot@follow = T.mk(y)", "store 0: own {0 Content}, reached {}", "untied")]
    public void PremisesAndInputsHandTheirCallables(string function, string expected, string call)
    {
        var c = Bind(function + "\n");
        Assert.Equal((expected, call), (Declared(c, "f"), string.Join(" | ", Calls(c, "mk"))));
    }

    // Review r3's B3: an erased callable's carrier output is a fixed Origin its fixed inputs reach, so `mk`'s result `a` retains the
    // View borrowing `*q` (under `v.source == c`, `use`'s result retains `q`), and a generic caller passing its own carriers to `map`
    // retains `a` through the bound `map`'s call instantiates.
    [Fact]
    public void ErasedCarriersAreFixedOrigins()
    {
        var c = Bind(View + "func use<M, A>(mk: ref/M, q: uniq/i32 during c) -> A\n    M is Callable<(View<i32>{v}) -> A>\n    origin v.source == c\n" +
            "    let a = mk(View<i32>.init(data: q@follow@ref))\n    q@follow = 5\n    return a@move\n");
        Assert.Equal(["result {0 Value}"], ValueCalls(c));
        Assert.Equal("result {1 Value}", Declared(c, "use"));
        var generic = Bind(View + "func map<F, T, U>(g: ref/F, x: T) -> U\n    F is Callable<(T) -> U>\n    return g(x@move)\n" +
            "func use<M, G, A, B>(mk: ref/M, g: ref/G, q: uniq/i32 during c) -> B\n    M is Callable<(View<i32>{v}) -> A>\n    G is Callable<(A) -> B>\n" +
            "    origin v.source == c\n    let a = mk(View<i32>.init(data: q@follow@ref))\n    let r = map(g, a@move)\n    q@follow = 5\n    return r@move\n");
        Assert.Equal(["result {1 Value}"], Calls(generic, "map"));
        Assert.Equal(["result {0 Value}", "result {0 Value}"], ValueCalls(generic));
    }

    // SPEC 15.6.4, 15.6.3, 8.11.3: a generic callee and a requirement are read under the call's Subst: an Iterator's `next` retains its
    // receiver's referent only (descent), the instance a generic call makes too, and a requirement under an abstract Self keeps the
    // relation its contract declares.
    [Fact]
    public void SubstitutedCalleesRetainByTheirInstance()
    {
        var c = Bind("contract Source\n    func read(self: ref/Self) -> ref/i32 during self\n" +
            "func use<S>(s: ref/S) -> ref/i32 during s\n    S is Source\n    return s.read()\n" +
            "func nextOf<I>(it: uniq/I) -> Option<I.(Iterator).Item>\n    I is Iterator\n    return it.next()\n" +
            "var xs = [1, 2, 3]\nvar it = xs.iterate()\nlet first = it.next()\nlet second = nextOf(it@uniq)\n");
        Assert.Equal(["result {0 Value}"], Calls(c, "read"));
        Assert.Equal(["result {0 Content}, store 0: own {0 Content}, reached {}"], Calls(c, "nextOf"));
        Assert.Equal(["result {0 Content}, store 0: own {0 Content}, reached {0 Content}", "result {0 Content}, store 0: own {0 Content}, reached {}"], Calls(c, "next"));
    }

    // DECISIONS R4-M154 (rule 1's All): All joins every entry with its pairs, the Abstract one's too. A `during self` layer whose holder has
    // an Origin argument the Subst brings (`H during o`) lets the call write any value at that Origin, a position in the root's storage and
    // in the Abstract one, so a source whose fit relates two Subst Origins (`p > o`) lists its pair in both, and in All.
    [Fact]
    public void AllJoinsTheAbstractEntrysPairs()
    {
        var c = Bind("struct H {a}\n    public var data: raw/i32 during self\n    public var peer: ref/i32 during a\n" +
            "func f<T, E>(h: uniq/T, y: E) => ()\nfunc g(h: uniq/(H during o), y: uniq/i32 during p) => f(h, y)\n");
        Assert.Equal(["store 0: own {0 Content} if {1 Value: p > o}, reached {0 Content} if {1 Value: p > o}"], Calls(c, "f"));
    }

    // DECISIONS R4-M154, Revision 10: a source's fit is the conjunction over the layers of its path, but those the premise catalog's
    // well-formedness relates to the layer above, so a borrow formed through `x`'s raw layer lists one pair per layer the position's
    // closure misses: into `*w`, `p`, `r` (the raw layer's, which no well-formedness relates to `p`) and `a` (none relates it to `r`);
    // into Box2's `uniq/i32 during a` past x's referent, `p` and `r`.
    [Fact]
    public void ASourcesPairsAreOneConjunctionOverItsPath()
    {
        var c = Bind("struct Box2 {a}\n    public var inner: uniq/i32 during a\n    public init(inner: uniq/i32 during a) => self.inner = inner\n" +
            "func f<T, E>(x: T, w: uniq/E) => ()\n" +
            "func g(zr: ref/i32 during r, x: uniq/(raw/(Box2 during a) during r) during p, w: uniq/(uniq/i32 during s) during q) => f(x, w)\n");
        Assert.Equal(["store 0: own {0 Content} if {0 Value: p > r}, reached {0 Content} if {0 Value: p > a & 0 Value: r > a}, store 1: own {1 Content} if {0 Value: p > s & 0 Value: r > s & 0 Value: a > s}, reached {}"], Calls(c, "f"));
    }

    // SPEC 22.2.3, 15.6.3, 8.4: a requirement on the caller's abstract Self is read as the caller's premises make its contract: under
    // `I is Iterator`, `next`'s item is `I.Item`, which names no `step`, so it descends from the receiver's referent (Content) and two
    // items stay valid together; with no calling definition the same Subst reads `LentItem(step)`, which retains the receiver Borrow.
    [Fact]
    public void RequirementsReadUnderTheCallersPremises()
    {
        var c = Bind("func nextPair<I>(iterator: uniq/I) -> (Option<I.Item>, Option<I.Item>)\n    I is Iterator\n" +
            "    let first = iterator.next()\n    let second = iterator.next()\n    return (first@move, second@move)\n");
        Assert.Equal(["result {0 Content}, store 0: own {0 Content}, reached {0 Content}", "result {0 Content}, store 0: own {0 Content}, reached {0 Content}"], Calls(c, "next"));
        var call = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Select(static x => x.CallOf()!).First(static x => x.Target.Name == "next");
        Assert.Equal("result {0 Value}, store 0: own {0 Content}, reached {0 Content}", Show(c.Types.Retains(call.Target, c.Types.SubstOf(call), null)));
    }

    // SPEC 5.7 Calls, 15.6.4 (D6, D15; design-final §6): a raw result layer with an Origin retains as that borrow would; one without
    // retains every input with its chain and is untied (DECISIONS R4-M131, BU-40: rule A). A raw parameter layer without an Origin, or with
    // `static`, lets the call write its pointee; one with another Origin lets it write what each borrow that Origin belongs to admits (an
    // input's referent, the keepers of a slot), as the raw input's storage, unless only shared input borrows of the signature state that
    // Origin (SPEC 5.2.1 condition 3, the owner's 「シグネチャ版も入れる (推奨)」; each statement a `ref` input's outer Origin or the argument of a slot
    // whose Loan requirement is `ref` in every case its Type selects, as `W`'s alone and a conditional slot's of a `ref` instance, not of a
    // `uniq` one, SPEC 15.3.5, DECISIONS BU-44; never a borrow the body forms): then a pointer read from the layer has read
    // authority only and the call writes nothing through it; a universal stated by a `ref` and a `uniq` input, an Option's `uniq` borrow
    // and a projection a `uniq` input states are no such Origin. A raw input's pointee and its Origin's members are Content, and its own
    // layer's Origin gives no row (SPEC 15.6.4 as DECISIONS R4-M116 revised it, R-B3). An Option or a Holder holding a `uniq` borrow by
    // value stores what a read through it carries into that borrow's referent (`1 Content store 1`, DECISIONS BU-49), and only that
    // into the storage the raw layer addresses, whose `ref/i32` members cannot hold its value (`1 Content` at store 2; DECISIONS R4-M154).
    [Theory]
    [InlineData("func f(x: ref/i32, n: i32) -> raw/i32 => null", "result {0 Value Chain, 1 Value Chain}, untied")]
    [InlineData("func f(x: ref/i32, n: isize) -> (raw/u8, isize) => (null, n)", "result {0 Value Chain, 1 Value Chain}, untied")]
    [InlineData("func f(b: ref/i32, c: ref/i32, p: raw/i32 during b) -> raw/i32 during b => p", "result {0 Value, 2 Value}")]
    [InlineData("func f(p: raw/(ref/i32 during q) during static, v: ref/i32 during q) => ()", "store 0: own {0 Content, 1 Value}, reached {}")]
    [InlineData("func f(b: uniq/(ref/i32 during q), p: raw/(ref/i32 during q) during b, v: ref/i32 during q) => ()", "store 0: own {0 Content, 1 Content, 2 Value}, reached {}, store 1: own {0 Content, 1 Content, 2 Value}, reached {}")]
    [InlineData("func f(b: ref/(ref/i32 during q), p: raw/(ref/i32 during q) during b, v: ref/i32 during q) => ()", "")]
    [InlineData("func f(a: ref/(ref/i32 during q) during o, b: ref/(ref/i32 during q) during o, p: raw/(ref/i32 during q) during o, v: ref/i32 during q) => ()", "")]
    [InlineData("func f(a: ref/(ref/i32 during q) during o, b: uniq/(ref/i32 during q) during o, p: raw/(ref/i32 during q) during o, v: ref/i32 during q) => ()", "store 1: own {0 Content, 1 Content, 2 Content, 3 Value}, reached {}, store 2: own {0 Content, 1 Content, 2 Content, 3 Value}, reached {}")]
    [InlineData(KeptView + "func f<K, V>(v: ref/View<K, V>, p: raw/u8 during v.source, key: K)\n    K is Equatable\n    return", "store 1: own {0 Content, 1 Content, 2 Value}, reached {}")]
    [InlineData(
        "struct W {source, q}\n    origin q outlives source\n    internal let r: ref/(ref/i32 during q) during source\n" +
        "    public init(r: ref/(ref/i32 during q) during source) => self.r = r\nfunc f(v: ref/W, p: raw/u8 during v.source, w: ref/i32 during v.q) => ()",
        "")]
    [InlineData(RawPointerSyntaxTest.KeptView + "func f(v: ref/View, x: uniq/(ref/i32 during q) during v.source, p: raw/u8 during v.source, w: ref/i32 during q) => ()", "store 1: own {1 Content, 2 Content, 3 Value}, reached {}, store 2: own {1 Content, 2 Content, 3 Value}, reached {}")]
    [InlineData("func f(a: ref/(ref/i32 during q) during o, b: Option<uniq/(ref/i32 during q) during o>, p: raw/u8 during o, v: ref/i32 during q) => ()", "store 1: own {0 Content, 1 Content, 2 Content, 3 Value}, reached {}, store 2: own {0 Content, 1 Content, 2 Content, 3 Value}, reached {}")]
    [InlineData("func f(a: ref/(ref/i32 during q) during o, p: raw/(ref/i32 during q) during o, v: ref/i32 during q)\n    unsafe\n        let w: uniq/(ref/i32 during q) during o = (*p)@uniq", "")]
    [InlineData(
        PairAnnotationBindingTest.HolderRefUniq + "func f(r: uniq/(ref/i32 during q), h: Holder<uniq/(ref/i32 during q) during r> during o, " +
        "p: raw/(ref/i32 during q) during o, v: ref/i32 during q) => ()",
        "store 0: own {0 Content, 1 Content, 2 Content, 3 Value}, reached {}, store 1: own {0 Content, 1 Content, 2 Content, 3 Value}, reached {}, store 2: own {0 Content, 1 Content, 2 Content, 3 Value}, reached {}")]
    [InlineData(
        PairAnnotationBindingTest.HolderRefUniq + "func f(r: ref/(ref/i32 during q), h: Holder<ref/(ref/i32 during q) during r> during o, " +
        "p: raw/(ref/i32 during q) during o, v: ref/i32 during q) => ()",
        "")]
    public void RawLayersRetainByTheirOrigins(string function, string expected)
        => Assert.Equal(expected, Declared(Bind(function + "\n"), "f"));

    // SPEC 5.7 Calls (D15; DECISIONS BU-24, the delta review's C4 item): a raw layer's Origin reaches its slot's keepers however deep the
    // slot is forwarded through nested Types, so ten forwarding levels keep the innermost Loan's referent writable through `p`, as one
    // does (a walk bounded at eight lost it), and a slot kept again through a raw pointer to its own Type keeps its rows.
    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public void AKeeperChainIsWalkedToItsEnd(int levels)
    {
        var source = "struct A0<K, V> {s}\n    K is Equatable\n    let loan: Loan<uniq/Dictionary<K, V> during s>\n" +
            "    public init(d: uniq/Dictionary<K, V> during s) => self.loan = Loan.init(d)\n";
        for (var i = 1; i <= levels; i++)
        {
            source += $"struct A{i}<K, V> {{s}}\n    K is Equatable\n    let inner: A{i - 1}<K, V> during s\n    public init(inner: A{i - 1}<K, V> during s) => self.inner = inner@move\n";
        }

        source += $"func f<K, V>(v: ref/A{levels}<K, V>, p: raw/u8 during v.s, key: K)\n    K is Equatable\n    return\n";
        Assert.Equal("store 1: own {0 Content, 1 Content, 2 Value}, reached {}", Declared(Bind(source), "f"));
    }

    // SPEC 5.7 Calls (DECISIONS BU-24, U2): each slot keeper is walked once per slot, owner Type, input and mode, so a keeper cycle through
    // a raw pointer to its own Type keeps the rows of its one-level unrolling, and a raw layer whose referent states the Origin being walked
    // again ends there, where `key`'s own value can be stored, as a raw/i32 the member holds (SPEC 15.6.4, DECISIONS R4-M116).
    [Fact]
    public void AKeeperCycleEndsWhereItStarted()
    {
        const string Loan = "    K is Equatable\n    let loan: Loan<uniq/Dictionary<K, V> during s>\n";
        const string Init = "    public init(d: uniq/Dictionary<K, V> during s)\n        self.loan = Loan.init(d)\n        self.next = null\n";
        const string Use = "func f<K, V>(v: ref/N<K, V>, p: raw/u8 during v.s, key: K)\n    K is Equatable\n    return\n";
        var rows = Declared(Bind("struct N<K, V> {s}\n" + Loan + "    let next: raw/(N<K, V> during s)\n" + Init + Use), "f");
        Assert.Equal("store 1: own {0 Content, 1 Content, 2 Value}, reached {}", rows);
        var unrolled = "struct L<K, V> {s}\n" + Loan + "    public init(d: uniq/Dictionary<K, V> during s) => self.loan = Loan.init(d)\n" +
            "struct N<K, V> {s}\n" + Loan + "    let next: raw/(L<K, V> during s)\n" + Init + Use;
        Assert.Equal(rows, Declared(Bind(unrolled), "f"));
        Assert.Equal("store 1: own {0 Content, 1 Content, 2 Value}, reached {0 Content, 1 Content, 2 Value}", Declared(Bind("struct R {s}\n    let at: uniq/(raw/i32 during s) during s\nfunc f(v: ref/R, p: raw/u8 during v.s, key: raw/i32) => ()\n"), "f"));
    }

    // SPEC 5.7 Calls (DECISIONS BU-24, U2; the design review's item 3): a keeper cycle that changes its Type arguments, raw or not, through
    // a Field or a Case payload, is valid at its declaration; a contract whose raw layer's walk reaches one is CallRetention.Limit, never a
    // silent stop (A5 records a located Unsupported_Kd at the call).
    [Theory]
    [InlineData("struct P<K, V> {s}\n    K is Equatable\n    let loan: Loan<uniq/Dictionary<K, V> during s>\n    let next: raw/(P<K, (V, V)> during s)\n", "func f<K, V>(v: ref/P<K, V>, p: raw/u8 during v.s, key: K)\n    K is Equatable\n    return\n")]
    [InlineData("struct N<T> {s}\n    internal let at: uniq/T during s\n    internal let next: ref/(N<(T, T)> during s) during s\n", "func f(n: uniq/(N<i32> during a), p: raw/u8 during a) => ()\n")]
    [InlineData("struct N<T> {s}\n    internal let at: uniq/T during s\n    internal let next: ref/(N<() -> T> during s) during s\n", "func f(n: uniq/(N<i32> during a), p: raw/u8 during a) => ()\n")]
    [InlineData("enum P<T> {s}\n    Leaf(uniq/T during s)\n    Node(raw/(P<(T, T)> during s))\n", "func f(n: uniq/(P<i32> during a), p: raw/u8 during a) => ()\n")]
    public void AKeeperCycleChangingItsTypeArgumentsIsALimit(string declaration, string use)
    {
        Assert.Empty(RawPointerSyntaxTest.Records(declaration));
        Assert.Equal("limit", Declared(Bind(declaration + use), "f"));
    }

    // SPEC 5.7 Calls (the design review's item 3): two raw layers stating one Origin each walk its members as their own input's content
    // (the keeper key holds the content atom), so neither loses its rows to the other: `p`'s store 1 and `q`'s store 2 alike; the
    // abstract `T` they and `n` reach may hold the other's own value (SPEC 15.6.4, DECISIONS R4-M116).
    [Fact]
    public void TwoRawLayersOfOneOriginKeepTheirRows()
        => Assert.Equal(
            "store 0: own {0 Content, 1 Value Content, 2 Value Content}, reached {0 Content, 1 Value Content, 2 Value Content}, " +
            "store 1: own {0 Content, 1 Content, 2 Value Content}, reached {2 Value}, store 2: own {0 Content, 1 Value Content, 2 Content}, reached {1 Value}",
            Declared(Bind("struct K<T> {s}\n    internal let at: uniq/T during s\nfunc f<T>(n: uniq/(K<T> during a), p: raw/u8 during a, q: raw/u8 during a) => ()\n"), "f"));

    // SPEC 5.7 Calls, 15.6.4 (R4-M52): an erased callable's signature is its own scope for its raw layers, also when it has fewer inputs
    // than the contract naming it (U1 read the contract's input count there, out of range).
    [Fact]
    public void AnErasedSignatureIsItsOwnRawScope()
        => Assert.Equal(string.Empty, Declared(Bind("func f(a: i32, b: i32, g: (raw/i32) -> ()) => ()\n"), "f"));

    // SPEC 5.2.1 condition 3, 5.7 Calls (the final review's B2; R4's constraint 2): the signature cap is a function's own. A Function
    // Type's raw layer reads its Origin's kind, so a call of a Function Type value whose `ref` input alone states that Origin (the
    // reviewer's x_ft_erased with a Loan-carrying referent) may write through the layer what the borrow admits, where the same layer in
    // a function's own signature stores nothing (RawLayersRetainByTheirOrigins); `b` keeps its Content there, the `ref/i32` pointee
    // unable to hold its value (DECISIONS R4-M154).
    [Fact]
    public void AnErasedSignatureReadsNoSignatureCap()
    {
        const string Inputs = "ref/(ref/i32 during q) during o, raw/(ref/i32 during q) during o, ref/i32 during q";
        var c = Bind($"func apply(g: ({Inputs}) -> (), b: ref/(ref/i32 during q) during o, p: raw/(ref/i32 during q) during o, v: ref/i32 during q)\n    g(b, p, v)\n");
        Assert.Equal(["store 1: own {0 Content, 1 Content, 2 Value}, reached {}"], ValueCalls(c));
        Assert.Equal(string.Empty, Declared(c, "apply"));
    }

    // SPEC 5.7, 8.11.4 rule 1 (D10): an erased callable's input reached past a shared layer stores nothing through a raw pointer there.
    [Fact]
    public void AnErasedSharedRawInputStoresNothing()
        => Assert.Equal([string.Empty], ValueCalls(Bind("func apply(g: (ref/(raw/(ref/i32 during q)), ref/i32 during q) -> (), s: ref/(raw/(ref/i32 during q)), v: ref/i32 during q) => g(s, v)\n")));

    // SPEC 15.6.4 (A3 E14): an intrinsic's call retains as its library declaration says, as any call does: an object creation its value,
    // clone its handle's payload only (Content), Raw.initialize its value into the raw storage, also a raw pointer's own value (`c01`)
    // into an unstated layer, which unties the storage (R-B2), and the storage a read through it (SPEC 15.6.4 as DECISIONS R4-M116
    // revised it: a pointer is never part of its own pointee);
    // Text.writer its destination, its Utf8Writer untied by the unstated raw Fields it stores; Raw.slice's result, a fresh anchor, is
    // untied unless its pointee's Origins tie it, and lies within its storage, which it retains (D14, R4-M116 item 4) with a read through
    // it; Raw.allocate's, a raw pointer without an Origin, retains every argument with its chain and is untied (SPEC 15.6.4's rule A,
    // DECISIONS BU-40), as Raw.initialize's unstated `slot` pointee retains `p` and its own chain. A raw input's pointee is its own
    // writable storage, which retains it as a read through it.
    [Theory]
    [InlineData(Lent + "let h = Kimi.Intrinsics.makeObj(Lent.init(r: x@ref))\n", "makeObj", "result {0 Value}")]
    [InlineData(Lent + "let h = Kimi.Intrinsics.makeRc(Lent.init(r: x@ref))\n", "makeRc", "result {0 Value}")]
    [InlineData(Lent + "let h = Kimi.Intrinsics.makeArc(Lent.init(r: x@ref))\n", "makeArc", "result {0 Value}")]
    [InlineData(Lent + "let h = Kimi.Intrinsics.makeRc(Lent.init(r: x@ref))\nlet c = Kimi.Intrinsics.clone(h@ref)\n", "clone", "result {0 Content}")]
    [InlineData("var x = 1\nlet p: raw/(ref/i32) = Raw.allocate(1)\nunsafe => Raw.initialize(p, x@ref)\n", "initialize", "store 0: own {0 Content, 1 Value}, reached {}")]
    [InlineData("let slot: raw/(raw/i32) = Raw.allocate(1)\nlet p: raw/i32 = Raw.allocate(1)\nunsafe => Raw.initialize(slot, p)\n", "initialize", "store 0: own {0 Chain, 1 Value Chain}, reached {0 Chain, 1 Value Chain} untied")]
    [InlineData("let p: raw/i32 = Raw.allocate(1)\nunsafe\n    let s = Raw.slice(p, 1)\n", "slice", "result {0 Value}, untied")]
    [InlineData("let p: raw/(ref/i32) = Raw.allocate(1)\nunsafe\n    let s = Raw.slice(p, 1)\n", "slice", "result {0 Value Content}, store 0: own {0 Content}, reached {}")]
    [InlineData("var b = Text.heap(8)\nlet w = Text.writer(b@uniq)\n", "writer", "result {0 Value}, untied")]
    [InlineData("let p: raw/(ref/i32) = Raw.allocate(1)\nunsafe => Raw.release(p)\n", "allocate", "result {0 Value Chain}, untied")]
    [InlineData("let p: raw/(ref/i32) = Raw.allocate(1)\nunsafe => Raw.release(p)\n", "release", "store 0: own {0 Content}, reached {}")]
    public void IntrinsicsRetainAsTheirDeclarationsSay(string source, string name, string expected)
        => Assert.Equal([expected], Calls(Bind(source), name));

    // G147 (1), G160-3 (SPEC 5.7, 15.6.4): placeEntry's handle `raw/Dictionary<K, V>` admits K and V, so the call stores its key and value
    // in the Dictionary the handle addresses, and the handle as a read through it, never its own value (DECISIONS R4-M116); the earlier
    // `raw/u8` handle admitted nothing. keyAt's raw result states no Origin, so it retains its slot as a value and by its chain (rule A,
    // DECISIONS BU-40).
    // DictionaryStorage.grow writes the Handle its `raw/Handle` addresses, whose `buffer` states `self`: through it a call stores what
    // the Handle's Type admits, nothing here, so the handle is no untied store (SPEC 5.7 Calls).
    [Theory]
    [InlineData(KimiDeclarationId.StoragePlaceDictionaryEntry, "store 0: own {0 Content, 1 Value, 2 Value}, reached {}")]
    [InlineData(KimiDeclarationId.StorageKeyAt, "result {0 Value Chain}, untied")]
    [InlineData(KimiDeclarationId.DictionaryStorageGrow, "")]
    public void StorageOperationsRetainTheirRawArguments(KimiDeclarationId id, string expected)
    {
        var c = Bind(string.Empty);
        Assert.Equal(expected, Show(c.Types.Retains(c.Library.GetSymbol(id)!, null, null)));
    }

    // SPEC 15.6.4 (OQ-3, DECISIONS R4-M116; G160-2c): a raw argument is retained as a borrowed one, its pointee as the referent. An Origin
    // inside its pointee is Content: the result keeps what a read through `p` carries (a Value row lost it once the addressed holder was
    // replaced: `snap`), and its own layer's Origin gives no row (R-B3: a `ref/i32 during b` result cannot hold the pointer). Its own
    // value is a Value row only of an output that can hold a value of its Type: a raw layer agreeing with it with Origins fitted (R-B4),
    // or an abstract part (Array's `T`); never its own pointee (no `head` row into `head`). An unstated layer the call writes, at a
    // position (`slot`'s pointee, which it unties) or in a struct's stored content (Node's `next`, both ways), retains every argument
    // with its chain and every other argument's own value (rule A, DECISIONS R4-M131, BU-40).
    [Theory]
    [InlineData("func f(p: raw/(ref/i32 during a)) -> ref/i32 during a\n    unsafe => return *p", "result {0 Content}, store 0: own {0 Content}, reached {}")]
    [InlineData("func f(b: ref/i32, p: raw/i32 during b) -> ref/i32 during b => b", "result {0 Value}")]
    [InlineData("func f(slot: raw/(raw/i32), b: ref/i32, p: raw/i32 during b) => ()", "store 0: own {0 Chain, 1 Value Chain, 2 Value Chain}, reached {0 Chain, 1 Value Chain, 2 Value Chain} untied")]
    [InlineData(
        "struct Node\n    public var next: raw/Node\n    public init(next: raw/Node) => self.next = next\nfunc f(head: raw/Node, n: raw/Node) => ()",
        "store 0: own {0 Chain, 1 Value Chain}, reached {0 Chain, 1 Value Chain} untied, store 1: own {0 Value Chain, 1 Chain}, reached {0 Value Chain, 1 Chain} untied")]
    [InlineData("func f<T>(dst: uniq/Array<T>, p: raw/i32) => ()", "store 0: own {0 Content, 1 Value}, reached {1 Value}")]
    public void RawArgumentsRetainAsBorrowedOnes(string function, string expected)
        => Assert.Equal(expected, Declared(Bind(function + "\n"), "f"));

    // SPEC 5.7 Calls, 15.6 (the design review's S2 and R-B2; DECISIONS R4-M116 item 5): a raw pointer in an unstated or `static` Field or
    // Case payload is stated by the Type's Fields alone, so a call that forms it ties it to no argument: a result that stores one, at any
    // depth of its stored content, is untied (`make`, `Hid.init`, an enum's payload, a Tuple element of an Option's struct) and retains
    // every input with its chain (rule A, DECISIONS BU-40), and so is an input whose writable storage stores one (`fill`'s `h`, untied
    // store 0, its own chain), where the call may write such a pointer
    // (`h.p = Raw.allocate(1)`) through which a later `Raw.initialize(h.p, y@ref)` stores. A Field stating a slot is tied by it (`Lens`),
    // and a shared input's referent is no writable storage (`look`).
    [Fact]
    public void AStoredUnstatedRawLayerIsUntied()
    {
        var c = Bind(Hid + "func make(r: ref/i32 during a) -> Hid during a\n    let cell: raw/(ref/i32 during a) = Raw.allocate(1)\n    return Hid.init(r: r, p: cell)\n" +
            "func fill(h: uniq/Hid during a)\n    h.p = Raw.allocate(1)\nfunc look(h: ref/Hid during a) => ()\n" +
            "func hidden()\n    var y: i32 = 1\n    var v: i32 = 2\n    var h = make(v@ref)\n    fill(h@uniq)\n    unsafe => Raw.initialize(h.p, y@ref)\n" +
            "enum E\n    One(raw/u8)\n    Two(i32)\nfunc one() -> E => .Two(1)\n" +
            "struct W\n    public var pair: (i32, raw/u8 during static)\n    public init() => self.pair = (1, null)\nfunc wrapped() -> Option<W> => .None\n" +
            "struct Lens<T> {source}\n    internal let loan: Loan<ref/T during source>\n    internal let at: raw/T during source\n" +
            "    internal init(target: ref/T during source)\n        self.at = target@follow@raw\n        self.loan = Loan.init(target)\n" +
            "func lens(x: ref/i32) -> Lens<i32> during x => Lens<i32>.init(target: x)\n");
        Assert.Equal("result {0 Value Chain}, untied", Declared(c, "make"));
        Assert.Contains("result {0 Value Chain, 1 Value Chain}, untied, store 1: own {0 Value, 1 Content}, reached {}", Calls(c, "init"));
        Assert.Equal("store 0: own {0 Chain}, reached {0 Chain} untied", Declared(c, "fill"));
        Assert.Equal(["store 0: own {0 Chain}, reached {0 Chain} untied"], Calls(c, "fill"));
        Assert.Equal(["store 0: own {0 Content, 1 Value}, reached {}"], Calls(c, "initialize"));
        Assert.Equal(string.Empty, Declared(c, "look"));
        Assert.Equal("untied", Declared(c, "one"));
        Assert.Equal("untied", Declared(c, "wrapped"));
        Assert.Equal("result {0 Value}", Declared(c, "lens"));
    }

    // SPEC 15.6.4, 5.7 (DECISIONS R4-M116 items 2 and 5, R-B2, BU-30; the G160-2c reviews' B1, R2, R4's F1 and B2): a value a call stores
    // at a writable position brings its whole Type (BU-1), so one walk reaches every layer below a hold, in a held struct's stored content
    // as at a position: an exclusive borrow (`S.r`, written as `s.r@follow = p`), a raw layer stepping Unstated or Static (`R.q`, written
    // by `Raw.initialize(r.q, p)`, formed by `f -> R`) or Exclusive (`Cell.at`, kept by a Loan), a shared one (`Sh.r`), an rc or arc
    // handle the call forms (`S.x`, alone or in an Option). A raw layer there stepping Unstated retains every argument with its chain
    // and every argument but the one written through by its own value (rule A, DECISIONS R4-M131, BU-40), and one stepping Unstated or
    // Static unties the output: a direct referent (`uniq/(raw/i32)`, `static` alike) as an Option's payload, an
    // allocated pointer in an rc's payload, a regular recursive Type's own Field. One Type at two positions steps by each (BU-35): H's
    // `at`, written with a `ref` slot, is Shared; the same `raw/i32 during static` its `a` holds through T steps Static and unties. A base
    // is walked as the instance's substitution keeps it (DECISIONS BU-41): a raw layer its Type argument supplies there steps by its Origin
    // as at a Field, Exclusive under a local's (no rule A) and Static under `static`, and an unstated one by rule A.
    [Theory]
    [InlineData("struct S {a}\n    public var r: uniq/(raw/i32) during a\nfunc f(s: uniq/S, p: raw/i32)\n    s.r@follow = p", "store 0: own {0 Chain, 1 Value Chain}, reached {0 Chain, 1 Value Chain} untied")]
    [InlineData("struct R\n    public var q: raw/(raw/i32)\nfunc f(r: uniq/R, p: raw/i32)\n    unsafe => Raw.initialize(r.q, p)", "store 0: own {0 Chain, 1 Value Chain}, reached {0 Chain, 1 Value Chain} untied")]
    [InlineData(
        "struct R\n    public var q: raw/(raw/i32)\n    public init(q: raw/(raw/i32)) => self.q = q\nfunc f(p: raw/i32) -> R\n    let cell: raw/(raw/i32) = Raw.allocate(1)\n    return R.init(q: cell)",
        "result {0 Value Chain}, untied")]
    [InlineData("struct Sh {a}\n    public var r: ref/(raw/i32) during a\nfunc f(s: uniq/Sh, p: raw/i32) => ()", "store 0: own {0 Chain, 1 Value Chain}, reached {} untied")]
    [InlineData(
        "struct Cell {a}\n    public var keep: Loan<uniq/(raw/i32) during a>\n    public var at: raw/(raw/i32) during a\nfunc f(c: uniq/Cell, p: raw/i32)\n    unsafe => Raw.initialize(c.at, p)",
        "store 0: own {0 Chain, 1 Value Chain}, reached {0 Chain, 1 Value Chain} untied")]
    [InlineData(Shared + "struct S\n    public var x: rc/Bx\nfunc f(s: uniq/S, p: raw/i32)\n    s.x = Bx.init(v: p)@rc", "store 0: own {0 Chain, 1 Value Chain}, reached {} untied")]
    [InlineData(Shared + "struct S\n    public var x: rc/Bx\nfunc f(s: uniq/S)\n    let q: raw/i32 = Raw.allocate(1)\n    s.x = Bx.init(v: q)@rc", "store 0: own {0 Chain}, reached {} untied")]
    [InlineData(Shared + "struct S\n    public var x: rc/Bx\n    public init(x: rc/Bx) => self.x = x@move\nfunc f(p: raw/i32) -> S\n    return S.init(x: Bx.init(v: p)@rc)", "result {0 Value Chain}, untied")]
    [InlineData(
        Shared + "struct S\n    public var x: rc/Bx\n    public init(x: rc/Bx) => self.x = x@move\nfunc f() -> S\n    let q: raw/i32 = Raw.allocate(1)\n    return S.init(x: Bx.init(v: q)@rc)",
        "untied")]
    [InlineData(Shared + "struct S\n    public var x: arc/Bx\nfunc f(s: uniq/S, p: raw/i32)\n    s.x = Bx.init(v: p)@arc", "store 0: own {0 Chain, 1 Value Chain}, reached {} untied")]
    [InlineData(Shared + "struct S\n    public var x: Option<rc/Bx>\nfunc f(s: uniq/S, p: raw/i32) => ()", "store 0: own {0 Chain, 1 Value Chain}, reached {} untied")]
    [InlineData("struct G<T>\n    public var v: T\n    public var next: raw/G<T>\nfunc f(g: uniq/G<raw/i32>, p: raw/i32) => ()", "store 0: own {0 Chain, 1 Value Chain}, reached {0 Chain, 1 Value Chain} untied")]
    [InlineData("func f(p: uniq/(raw/i32))\n    p@follow = Raw.allocate(1)", "store 0: own {0 Chain}, reached {0 Chain} untied")]
    [InlineData("func f(p: uniq/(raw/i32 during static)) => ()", "store 0: own {0 Content}, reached {} untied")]
    [InlineData("func f(p: uniq/Option<raw/i32>) => ()", "store 0: own {0 Chain}, reached {0 Chain} untied")]
    [InlineData(RawPointerSyntaxTest.PairHolder + "func f(x: uniq/i32, h: uniq/Holder<raw/i32 during x>, p: raw/i32) => ()", "store 1: own {0 Value Chain, 1 Chain, 2 Value Chain}, reached {0 Value Chain, 1 Chain, 2 Value Chain} untied")]
    [InlineData(
        "struct H<T> {s}\n    internal let loan: Loan<ref/i32 during s>\n    internal var at: raw/i32 during s\n    internal var a: T\n" +
        "func f(y: ref/i32, h: uniq/(H<raw/i32 during static> during static), p: raw/i32) => ()",
        "store 1: own {1 Content, 2 Value}, reached {} untied")]
    [InlineData(Derived + "func f(x: uniq/i32, d: uniq/Der<raw/i32 during x>, p: raw/i32) => ()", "store 1: own {0 Value, 1 Content, 2 Value}, reached {2 Value}")]
    [InlineData(Derived + "func f(x: uniq/i32, d: uniq/Der<raw/i32 during static>, p: raw/i32) => ()", "store 1: own {1 Content, 2 Value}, reached {2 Value} untied")]
    [InlineData(
        Derived + "func f(x: uniq/i32, d: uniq/Der<raw/i32>, p: raw/i32) => ()",
        "store 1: own {0 Value Chain, 1 Chain, 2 Value Chain}, reached {0 Value Chain, 1 Chain, 2 Value Chain} untied")]
    public void OneWalkReachesWhatACallMayWrite(string function, string expected)
        => Assert.Equal(expected, Declared(Bind(function + "\n"), "f"));

    // SPEC 15.6.4, 5.7, 8.1.1 (DECISIONS BU-34, BU-30, BU-40): a raw layer a pair slot supplies states no Origin, so it is an unstated
    // layer, which retains every argument with its chain (rule A) and which its holder's hold walk unties, as `Holder<raw/i32>`'s: a result instance written with the dropped Origin, a call whose
    // result the pair's original `s/T` gives and a call storing through it. A Type slot keeps the argument's Origin and its row.
    [Fact]
    public void APairSuppliedRawLayerIsUnstated()
    {
        var c = Bind(RawPointerSyntaxTest.PairHolder + "func id<s/T>(v: s/T) -> s/T => v@move\nfunc idt<T>(v: T) -> T => v@move\n" +
            "func fill<s/T>(h: uniq/Holder<s/T>, v: s/T) => h.value = v@move\n" +
            "func make(x: uniq/i32, p: raw/i32 during x) -> Holder<raw/i32 during x> => Holder<raw/i32 during x>.init(v: p)\n" +
            "func pass(x: ref/i32, p: raw/i32 during x) -> raw/i32 => id(p)\nfunc keep(x: ref/i32, p: raw/i32 during x) -> raw/i32 during x => idt(p)\n" +
            "func store(x: uniq/i32, q: raw/i32)\n    var h = Holder<raw/i32 during x>.init(v: x@follow@raw)\n    fill(h@uniq, q)\n");
        Assert.Equal("result {0 Value Chain, 1 Value Chain}, untied", Declared(c, "make"));
        Assert.Equal(["result {0 Value Chain}, untied"], Calls(c, "id"));
        Assert.Equal(["result {0 Value}"], Calls(c, "idt"));
        Assert.Equal(["store 0: own {0 Chain, 1 Value Chain}, reached {0 Chain, 1 Value Chain} untied"], Calls(c, "fill"));
    }

    // SPEC 15.6.4, 5.7 Calls (DECISIONS R4-M131, BU-40): a raw layer without an Origin that the call may write, in its result (a nominal
    // Type's Fields included) or in storage it writes through argument j, retains every argument with what any chain of reads through it
    // carries (Chain), and every argument but j by its own value: a Tuple's, an Option's or a struct's raw layer is read through once by
    // the result (fail-before: a Value row only, which keeps no read, or none), as a raw-of-raw and a ref-of-raw argument are, and the
    // pointee of each, a raw layer without an Origin, is storage the call writes through the by-value argument (store 0; the struct's
    // fail-before: none, DECISIONS BU-52); j keeps its chain without its own value. A `static` layer unties without these rows.
    [Theory]
    [InlineData("func f(t: (raw/(raw/u8), i32)) -> raw/u8\n    unsafe => return *t.0", "result {0 Value Chain}, untied, store 0: own {0 Chain}, reached {0 Chain} untied")]
    [InlineData("func f(o: Option<raw/(raw/u8)>) -> raw/u8 => null", "result {0 Value Chain}, untied, store 0: own {0 Chain}, reached {0 Chain} untied")]
    [InlineData(Pp + "func f(s: Pp) -> raw/u8\n    unsafe => return *s.p", "result {0 Value Chain}, untied, store 0: own {0 Chain}, reached {0 Chain} untied")]
    [InlineData(Pp + "func f(x: ref/i32, n: i32) -> Pp => Pp.init(p: null)", "result {0 Value Chain, 1 Value Chain}, untied")]
    [InlineData("func f(pp: raw/(raw/u8)) -> raw/u8\n    unsafe => return *pp", "result {0 Value Chain}, untied, store 0: own {0 Chain}, reached {0 Chain} untied")]
    [InlineData("func f(r: ref/(raw/(raw/u8))) -> raw/u8\n    unsafe => return *r@follow", "result {0 Value Chain}, untied")]
    [InlineData("func f(slot: raw/(raw/u8), p: raw/u8) => ()", "store 0: own {0 Chain, 1 Value Chain}, reached {0 Chain, 1 Value Chain} untied")]
    [InlineData(
        "func f(r: ref/i32, k: i32, slot: uniq/(raw/i32)) => ()",
        "store 2: own {0 Value Chain, 1 Value Chain, 2 Chain}, reached {0 Value Chain, 1 Value Chain, 2 Chain} untied")]
    [InlineData("func f(r: ref/i32, slot: uniq/(raw/u8 during static)) => ()", "store 1: own {1 Content}, reached {} untied")]
    public void AnUnstatedLayerACallWritesRetainsEveryArgument(string function, string expected)
        => Assert.Equal(expected, Declared(Bind(function + "\n"), "f"));

    // SPEC 15.6.4 (DECISIONS BU-52): the storage a call writes through a by-value argument is what the exclusive layers and the raw layers
    // without an Origin it holds refer to, wherever the argument holds them: in a struct's Field, an enum's payload, an Array's element,
    // a Type argument's Field at the instance, or behind a `uniq` Field; a raw layer without an Origin there retains as rule A states
    // (the argument written through by its chain, every other by its own value and chain, untied; fail-before: no row). Past a shared
    // layer the call writes nothing (SPEC 5.2.1), so a `ref/(raw/u8)` Field gives no row.
    [Theory]
    [InlineData(Pp + "func f(s: Pp, q: raw/u8)\n    unsafe => *s.p = q", "store 0: own {0 Chain, 1 Value Chain}, reached {0 Chain, 1 Value Chain} untied")]
    [InlineData(Pp + "func f(o: Option<Pp>, q: raw/u8) => ()", "store 0: own {0 Chain, 1 Value Chain}, reached {0 Chain, 1 Value Chain} untied")]
    [InlineData(Pp + "func f(a: Array<Pp>, q: raw/u8) => ()", "store 0: own {0 Chain, 1 Value Chain}, reached {0 Chain, 1 Value Chain} untied")]
    [InlineData(Box + Pp + "func f(b: B<Pp>, q: raw/u8) => ()", "store 0: own {0 Chain, 1 Value Chain}, reached {0 Chain, 1 Value Chain} untied")]
    [InlineData(
        "struct WU {a}\n    public var r: uniq/(raw/u8) during a\nfunc f(x: WU during a, q: raw/u8) => x.r@follow = q",
        "store 0: own {0 Chain, 1 Value Chain}, reached {0 Chain, 1 Value Chain} untied")]
    [InlineData("struct RU {a}\n    public var r: ref/(raw/u8) during a\nfunc f(x: RU during a, q: raw/u8) => ()", "")]
    public void AByValueArgumentsLayersAreStorageTheCallWrites(string function, string expected)
        => Assert.Equal(expected, Declared(Bind(function + "\n"), "f"));

    // SPEC 15.6.4 ("including callee cleanup"), 16.3.1 (DECISIONS BU-52, BU-47): a callee that destroys a by-value argument runs its drop
    // over that argument, which writes only into the storage the call writes through it, so the call's rows include the drop's: a consuming
    // call of a Writer, concrete and generic, retains as Writer's drop over its receiver at input 0 (fail-before: no row).
    [Fact]
    public void AConsumingCallRetainsItsArgumentsCleanup()
    {
        var c = Bind(Writer + "func consumeW(x: Writer) => ()\nfunc consume<T>(x: T) => ()\nfunc at(w: Writer, v: Writer)\n    consumeW(w@move)\n    consume(v@move)\n");
        var drop = Show(c.Types.Retains(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.IsDestructor).SymbolOf()!, null, null));
        Assert.Equal("store 0: own {0 Chain}, reached {0 Chain} untied", drop);
        Assert.Equal([drop, drop], [.. Calls(c, "consumeW"), .. Calls(c, "consume")]);
    }

    // SPEC 15.6.4 (R′; DECISIONS R4-M131, BU-40): a raw layer without an Origin behind a borrow in the result, a Slice's elements included,
    // is lent, not written: it retains only the raw arguments, each by its own value and its chain, and unties nothing (fail-before: every
    // argument, untied). `lend`'s store 0 is written: rule A. `x`, whose value the result `ref/(raw/u8)` cannot hold, keeps its Content
    // there (DECISIONS R4-M154).
    [Theory]
    [InlineData(
        "func f(p: raw/(raw/u8), x: ref/i32) -> ref/(raw/u8) during x\n    unsafe => return (*p)@ref",
        "result {0 Value Chain, 1 Content}, store 0: own {0 Chain, 1 Value Chain}, reached {0 Chain, 1 Value Chain} untied")]
    [InlineData("func f(t: ref/(raw/u8, i32)) -> ref/(raw/u8) during t => t.0@ref", "result {0 Value}")]
    [InlineData("func f(p: raw/u8, t: ref/(raw/u8, i32)) -> Option<ref/(raw/u8) during t> => .None", "result {0 Value Chain, 1 Value}")]
    public void ALentLayerRetainsTheRawArguments(string function, string expected)
        => Assert.Equal(expected, Declared(Bind(function + "\n"), "f"));

    // PLAN G255's C1 (SPEC 5.7 Calls, 15.6.4's R′, 5.2.1; DECISIONS BU-40): a result raw layer stating an input borrow's Origin retains
    // as that borrow would, so what it addresses is that borrow's storage and the unstated layers there are lent, not written by the
    // result: dictionaryStorage's handle over a V holding one, as the MIR's Dictionary literal calls it, and a user function of its
    // shape, are not untied (fail-before: untied, so each such literal's entries reached storage the body does not own on the MIR); the
    // input's own storage stays an untied store, written by rule A.
    [Theory]
    [InlineData("raw/u8")]
    [InlineData("(raw/u8, i32)")]
    public void AStatedResultLayerLendsWhatItAddresses(string value)
    {
        var c = Bind($"func lit(q: raw/u8) -> Dictionary<i32, {value}> => [1: {(value == "raw/u8" ? "q" : "(q, 1)")}]\n" +
            $"func f(value: uniq/Dictionary<i32, {value}>) -> (raw/Dictionary<i32, {value}> during value, isize) => (null, 0)\n");
        var body = c.BuildMir().Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "lit");
        var call = body.Blocks.ToArray().Select(static x => x.Terminator)
            .Single(static x => x is { Kind: MirTerminatorKind.Call } && x.Callee.Symbol?.LibraryDeclaration == KimiDeclarationId.StorageDictionaryLayout);
        Assert.Equal("result {0 Value}, store 0: own {0 Chain}, reached {0 Chain} untied", Show(c.Types.Retains(call.Callee.Symbol!, call.Callee.Subst, body.Key.Decl)));
        Assert.Equal("result {0 Value}, store 0: own {0 Chain}, reached {0 Chain} untied", Declared(c, "f"));
    }

    // SPEC 15.6.4, 22.2.3, 15.7, 4.9.6 (DECISIONS R4-M131, BU-40): a published operation that transfers values retains a borrowed argument
    // at a raw layer without an Origin only by its chain, never its own value: an Iterator's `next` (a user Iterator, the library's owning
    // adapter under its conditional conformance), `swap`, Array's `pop`, Dictionary's `remove`; `exchange`'s result only its target's
    // chain. A 4.9 operation's element storage keeps, of its arguments other than the receiver, only the added ones (`insert`'s value,
    // `appendAll`'s Array, `appendCopies`' Slice by its chain), no index, key or predicate; construction has no receiver storage, so its
    // result keeps every argument, `repeating` and `count`, by its own value and its chain (fail-before: only `untied`). The keys are
    // declaring identities: a LendingIterator-only `next`, a user `pop` and a user `swap` keep every argument (A's cost, R4-M131 Open), and
    // `exchange` over a borrow keeps its rows. A 4.9 operation and `exchange` form no row and untie nothing at an output they do not write
    // (SPEC 4.9.6's complete published effect; PLAN G255's C4; DECISIONS BU-52): an added `raw/(raw/u8)`'s pointee, a struct's Field and
    // an enum's payload added by value (`append`, `insertOrReplace`), `exchange`'s `with` (fail-before: `store 1: own {}, reached {}
    // untied`, an untied pointer for what the added value addresses; before C4, `0 Chain store 1`).
    // A 4.9 operation writes its receiver's store only in the storage the receiver refers to (`referent store 0`, DECISIONS BU-42);
    // construction, `exchange`, `swap`, an Iterator's `next` and a user `trade` or `pop` have no such store.
    [Theory]
    [InlineData("var x: raw/u8 = null\nvar y: raw/u8 = null\nKimi.Intrinsics.swap(x@uniq, y@uniq)\n", "swap", "store 0: own {0 Chain, 1 Chain}, reached {0 Chain, 1 Chain} untied, store 1: own {0 Chain, 1 Chain}, reached {0 Chain, 1 Chain} untied")]
    [InlineData(
        "func trade<T>(a: uniq/T, b: uniq/T) => ()\nvar x: raw/u8 = null\nvar y: raw/u8 = null\ntrade(x@uniq, y@uniq)\n",
        "trade",
        "store 0: own {0 Chain, 1 Value Chain}, reached {0 Chain, 1 Value Chain} untied, store 1: own {0 Value Chain, 1 Chain}, reached {0 Value Chain, 1 Chain} untied")]
    [InlineData("var x: raw/u8 = null\nlet old = Kimi.Intrinsics.exchange(x@uniq, with: null)\n", "exchange", "result {0 Chain}, untied, store 0: own {0 Chain, 1 Value Chain}, reached {0 Chain, 1 Value Chain} untied")]
    [InlineData("var a = 1\nvar b = 2\nvar x = a@ref\nlet old = Kimi.Intrinsics.exchange(x@uniq, with: b@ref)\n", "exchange", "result {0 Content, 1 Value}, store 0: own {0 Content, 1 Value}, reached {}")]
    [InlineData("var a: Array<raw/u8> = [null]\nlet x = a.pop()\n", "pop", "result {0 Chain}, untied, store 0: own {0 Chain}, reached {0 Chain} untied, referent store 0")]
    [InlineData(
        RawStack + "var s = RawStack.init(items: [null])\nlet x = s.pop()\n",
        "pop",
        "result {0 Chain}, untied, store 0: own {0 Chain}, reached {0 Chain} untied, referent store 0 | result {0 Value Chain}, untied, store 0: own {0 Chain}, reached {0 Chain} untied")]
    [InlineData("var d: Dictionary<i32, raw/u8> = [:]\nlet k = 1\nlet x = d.remove(k@ref)\n", "remove", "result {0 Chain, 1 Chain}, untied, store 0: own {0 Chain}, reached {0 Chain} untied, referent store 0")]
    [InlineData("var a: Array<raw/u8> = []\na.insert(0, null)\n", "insert", "store 0: own {0 Chain, 2 Value Chain}, reached {0 Chain, 2 Value Chain} untied, referent store 0")]
    [InlineData("var a: Array<raw/(raw/u8)> = []\nvar h: raw/u8 = null\na.append(h@raw)\n", "append", "store 0: own {0 Chain, 1 Value Chain}, reached {0 Chain, 1 Value Chain} untied, referent store 0")]
    [InlineData(Pp + "var a: Array<Pp> = []\na.append(Pp.init(p: null))\n", "append", "store 0: own {0 Chain, 1 Value Chain}, reached {0 Chain, 1 Value Chain} untied, referent store 0")]
    [InlineData(
        "enum E\n    A(raw/(raw/u8))\n    B\nvar d: Dictionary<i32, E> = [:]\nlet old = d.insertOrReplace(1, .A(null))\n",
        "insertOrReplace",
        "result {0 Chain, 1 Value Chain, 2 Value Chain}, untied, store 0: own {0 Chain, 1 Value Chain, 2 Value Chain}, reached {0 Chain, 1 Value Chain, 2 Value Chain} untied, referent store 0")]
    [InlineData(
        "var x: raw/(raw/u8) = null\nlet old = Kimi.Intrinsics.exchange(x@uniq, with: null)\n",
        "exchange",
        "result {0 Chain}, untied, store 0: own {0 Chain, 1 Value Chain}, reached {0 Chain, 1 Value Chain} untied")]
    [InlineData("var a: Array<raw/u8> = []\nvar b: Array<raw/u8> = [null]\na.appendAll(b@move)\n", "appendAll", "store 0: own {0 Chain, 1 Value Chain}, reached {0 Chain, 1 Value Chain} untied, referent store 0")]
    [InlineData("var a: Array<raw/u8> = []\nlet b: Array<raw/u8> = [null]\na.appendCopies(b.slice())\n", "appendCopies", "store 0: own {0 Chain, 1 Chain}, reached {0 Chain, 1 Chain} untied, referent store 0")]
    [InlineData("var a: Array<raw/u8> = [null]\nvar bias = 1\nlet b = bias@ref\na.sort(by: func (x, y) => b@follow)\n", "sort", "store 0: own {0 Chain}, reached {0 Chain} untied, referent store 0")]
    [InlineData("let a = Array<raw/u8>.init(repeating: null, count: 2)\n", "initRepeating", "result {0 Value Chain, 1 Value Chain}, untied")]
    [InlineData("let a = Array<(raw/u8, i32)>.init(repeating: (null, 1), count: 2)\n", "initRepeating", "result {0 Value Chain, 1 Value Chain}, untied")]
    [InlineData("let a = Array<raw/u8>.init(capacity: 2)\n", "initCapacity", "result {0 Value Chain}, untied")]
    [InlineData(RawIt + "var it = RawIt.init(items: [null])\nlet x = it.next()\n", "next", "result {0 Chain}, untied, store 0: own {0 Chain}, reached {0 Chain} untied")]
    [InlineData(RawLend + "var it = RawLend.init(items: [null])\nlet x = it.next()\n", "next", "result {0 Value Chain}, untied, store 0: own {0 Chain}, reached {0 Chain} untied")]
    [InlineData("let a: Array<raw/u8> = [null]\nvar it = Iteration.owning(a@move.intoIterator())\nlet x = it.next()\n", "next", "result {0 Chain}, untied, store 0: own {0 Chain}, reached {0 Chain} untied")]
    [InlineData("let a: Array<raw/u8> = [null]\nvar it = a.iterate()\nlet x = it.next()\n", "next", "result {0 Content}, store 0: own {0 Chain}, reached {} untied")]
    public void TransferOperationsRetainBorrowedArgumentsByTheirChains(string source, string callee, string expected)
        => Assert.Equal(expected, string.Join(" | ", Calls(Bind(source), callee)));

    // SPEC 4.9.6, 15.6.4 (DECISIONS BU-42, PLAN G255's C3): an operation of SPEC 4.6 to 4.9 writes the store at its receiver, an
    // exclusive borrow, only in the storage the receiver refers to (CallRetention.ReferentStores, `referent store j`), by declaring
    // identity whatever its element Type: an Array holding no raw layer (`Array<ref/i32>`; `Array<uniq/(ref/i32)>`, whose premise rows
    // at the added input stay ordinary stores), one whose element's `drop` writes through a raw layer (`Array<Writer>`,
    // `Array<raw/Writer>`: the destruction is the callers' to compose, A5's guard), Array `swap`, a 4.6 to 4.8 operation (Slice
    // `iterateUniq`, UniqSlice `sliceUniq` after Array's and `indexUniq`, ResolvedRange `iterateUniq`, whose contract has no row) and the
    // fixed array's (`sliceUniq`, `iterateUniq`, `tryGetPairUniq`). None where the receiver is no exclusive borrow (Array `slice`, a
    // shared one; `intoIterator`, by value, though its elements' pointees are a store) or the callee no such operation (SliceIterator's
    // `next`). Fail-before: no row prints it.
    [Theory]
    [InlineData("var a: Array<ref/i32> = []\nvar x = 1\na.append(x@ref)\n", "append", "store 0: own {0 Content, 1 Value}, reached {}, referent store 0")]
    [InlineData(
        "var a: Array<uniq/(ref/i32)> = []\nvar x = 1\nvar r = x@ref\na.append(r@uniq)\n",
        "append",
        "store 0: own {0 Content, 1 Value}, reached {0 Content, 1 Content}, store 1: own {0 Content, 1 Content}, reached {}, referent store 0")]
    [InlineData(Writer + "var a: Array<Writer> = []\na.clear()\n", "clear", "store 0: own {0 Chain}, reached {0 Chain} untied, referent store 0")]
    [InlineData(Writer + "var a: Array<raw/Writer> = []\na.clear()\n", "clear", "store 0: own {0 Chain}, reached {0 Chain} untied, referent store 0")]
    [InlineData("var a: Array<raw/u8> = [null]\na.swap(0, 0)\n", "swap", "store 0: own {0 Chain}, reached {0 Chain} untied, referent store 0")]
    [InlineData(
        "let a: Array<raw/u8> = [null]\nvar s = a.slice()\nlet it = s.iterateUniq()\n",
        "iterateUniq",
        "result {0 Chain}, untied, store 0: own {0 Chain}, reached {} untied, referent store 0")]
    [InlineData(
        "var a: Array<raw/u8> = [null]\nvar s = a.sliceUniq()\nlet t = s.sliceUniq()\n",
        "sliceUniq",
        "result {0 Value}, store 0: own {0 Chain}, reached {0 Chain} untied, referent store 0 | result {0 Value}, store 0: own {0 Chain}, reached {0 Chain} untied, referent store 0")]
    [InlineData(
        "var a: Array<raw/u8> = [null]\nvar s = a.sliceUniq()\nlet t = s.indexUniq(0)\n",
        "indexUniq",
        "result {0 Value}, store 0: own {0 Chain, 1 Chain}, reached {0 Chain, 1 Chain} untied, referent store 0")]
    [InlineData("var r = (0..2).resolve(5)\nlet it = r.iterateUniq()\n", "iterateUniq", "referent store 0")]
    [InlineData("var f: [2 of raw/u8] = [null, null]\nlet s = f.sliceUniq()\n", "sliceUniq", "result {0 Value}, store 0: own {0 Chain}, reached {0 Chain} untied, referent store 0")]
    [InlineData(
        "var f: [2 of raw/u8] = [null, null]\nlet it = f.iterateUniq()\n",
        "iterateUniq",
        "result {0 Value Chain}, untied, store 0: own {0 Chain}, reached {0 Chain} untied, referent store 0")]
    [InlineData(
        "var f: [2 of raw/u8] = [null, null]\nlet p = f.tryGetPairUniq(first: 0, second: 1)\n",
        "tryGetPairUniq",
        "result {0 Value}, store 0: own {0 Chain, 1 Value Chain, 2 Value Chain}, reached {0 Chain, 1 Value Chain, 2 Value Chain} untied, referent store 0")]
    [InlineData("let a: Array<raw/u8> = [null]\nlet s = a.slice()\n", "slice", "result {0 Value}")]
    [InlineData(
        "let a: Array<raw/(raw/u8)> = [null]\nlet it = a@move.intoIterator()\n",
        "intoIterator",
        "result {0 Value Chain}, untied, store 0: own {0 Chain}, reached {0 Chain} untied")]
    [InlineData("let a: Array<raw/u8> = [null]\nvar it = a.slice().iterate()\nlet x = it.next()\n", "next", "result {0 Content}, store 0: own {0 Chain}, reached {} untied")]
    public void AnOperationWritesItsBorrowedReceiversStoreOnlyInItsReferent(string source, string callee, string expected)
        => Assert.Equal(expected, string.Join(" | ", Calls(Bind(source), callee)));

    // SPEC 15.6.4, 16.3.1 (DECISIONS BU-47; PLAN G264's U0): a destruction retains as a call of each `drop` it runs, whose `self` is a
    // `uniq/Self` receiver referring to the destroyed layer, so a drop's contract has that receiver at input 0, though it declares no
    // parameter, and retains as its twin, a method over `self: uniq/Self` with the same body, declared and at an instance (the twin's call
    // there): Writer's write through its unstated raw Fields (rule A: its chain, untied store 0), HRD's through a borrow Field (Content), and
    // the generic `Holder<T>` declared and at `i32`. The receiver has no Value row: no caller names its Origin. Fail-before: no row.
    [Theory]
    [InlineData(Writer + Flush + "func at(h: uniq/Writer) => h.flush()\n", "store 0: own {0 Chain}, reached {0 Chain} untied", "store 0: own {0 Chain}, reached {0 Chain} untied")]
    [InlineData(Hrd + "func at(h: uniq/HRD) => h.flush()\n", "store 0: own {0 Content}, reached {0 Content}", "store 0: own {0 Content}, reached {0 Content}")]
    [InlineData(Holder + "func at(h: uniq/Holder<i32>) => h.flush()\n", "store 0: own {0 Chain}, reached {0 Chain} untied", "store 0: own {0 Chain}, reached {0 Chain} untied")]
    public void ADropRetainsAsACallOverItsDestructionReceiver(string source, string declared, string instance)
    {
        var c = Bind(source);
        var drop = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.IsDestructor).SymbolOf()!;
        var at = Function(c, "at").SymbolOf()!;
        var layer = TyCtxt.Signature(at, null, 0, out _)!.Components[0];
        Assert.Equal((declared, declared), (Show(c.Types.Retains(drop, null, null)), Declared(c, "flush")));
        Assert.Equal((instance, instance), (Show(c.Types.Retains(drop, c.Types.Synthesized(drop, layer, []), at)), Calls(c, "flush").Single()));
    }

    // SPEC 15.6.4, 16.3.1 (DECISIONS BU-47): the library's drops retain over their destruction receivers as their contracts say: the owning
    // iterators' remainders store through the raw handles their receivers' storage holds (Content), HeapBuffer's release nothing; none
    // has a Value row. Fail-before: no row.
    [Fact]
    public void LibraryDropsRetainOverTheirReceivers()
    {
        var c = Bind(string.Empty);
        var drops = KotoTree.Walk(c.Library.Kotonoha.RootKoto).OfType<FunctionKoto>().Where(static x => x.IsDestructor)
            .Select(x => $"{x.Parent!.SymbolOf()!.Name}: {Show(c.Types.Retains(x.SymbolOf()!, null, null))}");
        Assert.Equal(
            ["HeapBuffer: ", "OwnedRemainder: store 0: own {0 Content}, reached {}", "DictionaryOwnedRemainder: store 0: own {0 Content}, reached {}", "FixedOwnedRemainder: store 0: own {0 Content}, reached {}"],
            drops);
    }

    // SPEC 16.3.3, 15.6.4 (DECISIONS BU-47, R4-M52): an erased destruction, of a part whose `drop` the destruction walk cannot see, retains
    // as the erased call `(uniq/P) -> ()` over its destruction receiver, as its twin does, a Callable-bounded value of that signature called
    // on the part: an open struct and an object handle of it through their unstated raw Fields (rule A), an abstract part in its generic
    // body by its carrier (Content), and a Function value's environment by nothing (R4-M131's open item), memoized per part and scope.
    [Fact]
    public void AnErasedDestructionRetainsAsTheErasedCall()
    {
        var c = Bind(ErasedParts);
        string[] expected = ["store 0: own {0 Chain}, reached {0 Chain} untied", "store 0: own {0 Chain}, reached {0 Chain} untied", "store 0: own {0 Content}, reached {0 Content}", string.Empty];
        Assert.Equal(expected, ValueCalls(c));
        var parts = new[] { "callWB", "callOWB", "callT", "callFn" }.Select(x => Function(c, x).SymbolOf()!)
            .Select(static x => (Part: TyCtxt.Signature(x, null, 0, out _)!.Components[0], Definition: x)).ToArray();
        Assert.Equal(expected, parts.Select(x => Show(c.Types.RetainsErasedDestruction(x.Part, x.Definition))));
        Assert.Same(c.Types.RetainsErasedDestruction(parts[2].Part, parts[2].Definition), c.Types.RetainsErasedDestruction(parts[2].Part, parts[2].Definition));
    }

    // SPEC 15.3.5, 15.6.4 (the G160-2c reviews' R1 and R4's B1): a Type whose storage grows through an indirection (a raw pointer, an
    // exclusive borrow, an object handle) has storage no preparation completes (DECISIONS BU-12's limits), so a contract whose hold walk
    // reaches it is CallRetention.Limit, for an input and a result alike, never an unbounded walk.
    [Theory]
    [InlineData("struct G<T>\n    public var v: T\n    public var next: raw/G<(T, T)>\nfunc f(g: uniq/G<i32>, p: raw/i32) => ()")]
    [InlineData("struct G<T>\n    public var next: raw/G<Option<T>>\n    public var v: i32\nfunc f(g: uniq/G<i32>) => ()")]
    [InlineData("struct G<T> {a}\n    public var next: uniq/(G<Option<T>> during a) during a\n    public var v: i32\nfunc f(g: uniq/G<i32>) => ()")]
    [InlineData("struct G<T>\n    public var next: raw/G<Option<T>>\n    public var v: i32\n    public init(v: i32)\n        self.next = null\n        self.v = v\nfunc f() -> G<i32> => G<i32>.init(v: 1)")]
    [InlineData("struct B<T>\n    public var v: T\n    public var next: Option<obj/B<(T, T)>>\nfunc f(g: uniq/B<i32>, p: raw/i32) => ()")]
    public void AGrowingStoredTypeIsALimit(string function)
        => Assert.Equal("limit", Declared(Bind(function + "\n"), "f"));

    // SPEC 23.1.2, 15.6.4 (G240; DECISIONS BU-36): storage whose part Binding did not bind (a Field or Case payload Type that failed,
    // inline, through a handle, in an instance or a Type argument, or an Origin that did not bind) is Rejected storage, no limit: the part
    // holds nothing, and the call keeps the rows that hold whatever it is, its repaired program's (fail-before: CallRetention.Limit). An
    // enum's other Cases are such rows' storage, also inside a struct (G242, DECISIONS BU-37; fail-before: no row).
    [Theory]
    [InlineData(Handle, "Missing", "i32", "f", "")]
    [InlineData(Bad + "func f(b: uniq/Bad, p: raw/i32) => ()\nfunc g(b: uniq/Bad) => f(b, null)\n", "Missing", "i32", "f", "")]
    [InlineData("enum E\n    A(MISSING)\n    C\nfunc f(e: uniq/E, p: raw/i32) => ()\nfunc g(e: uniq/E) => f(e, null)\n", "Missing", "i32", "f", "")]
    [InlineData("enum E\n    A(MISSING)\n    B(raw/i32)\nfunc f(e: uniq/E, p: raw/i32) => ()\nfunc g(e: uniq/E) => f(e, null)\n", "Missing", "i32", "f", "store 0: own {0 Chain, 1 Value Chain}, reached {0 Chain, 1 Value Chain} untied")]
    [InlineData("enum E\n    A(MISSING)\n    B(raw/i32)\nstruct H\n    public var e: E\nfunc f(h: uniq/H, p: raw/i32) => ()\nfunc g(h: uniq/H) => f(h, null)\n", "Missing", "i32", "f", "store 0: own {0 Chain, 1 Value Chain}, reached {0 Chain, 1 Value Chain} untied")]
    [InlineData("struct Bad<T>\n    public var v: T\n    public var m: MISSING\nfunc f<T>(b: uniq/Bad<T>, p: raw/i32) => ()\nfunc g(b: uniq/Bad<i32>) => f(b, null)\n", "Missing", "i32", "f", "")]
    [InlineData("struct Bad<T>\n    public var v: T\n    public var m: MISSING\nfunc id<T>(x: ref/T) -> ref/T during x => x\nfunc g(b: Bad<i32>)\n    let r = id(b@ref)\n", "Missing", "i32", "id", "result {0 Value}")]
    [InlineData("struct Sx\n    public var m: MISSING\n    public var p: raw/i32\nfunc f(s: uniq/Sx, q: raw/i32) => ()\nfunc g(s: uniq/Sx) => f(s, null)\n", "Missing", "i32", "f", "store 0: own {0 Chain, 1 Value Chain}, reached {0 Chain, 1 Value Chain} untied")]
    [InlineData(Keep2, "Missing", "i32", "keep2", "result {0 Value}")]
    [InlineData(Stash, "Missing", "i32", "stash", "store 0: own {0 Content, 1 Value}, reached {}")]
    [InlineData(Selection, "missing", "source", "choose", "result {0 Content}")]
    [InlineData(Pick, "missing", "s", "choose", "")]
    [InlineData(Fielded, "C{x}\n        origin x.s == zz", "C{x}\n        origin x.s == s", "init", "result {0 Value} | result {0 Value}")]
    [InlineData(Fielded, "Missing", "C{x}\n        origin x.s == s", "init", "result {0 Value} | result {0 Value}")]
    public void RejectedStorageKeepsTheRowsThatHoldWhateverItIs(string source, string failed, string repaired, string callee, string expected)
    {
        var rejected = MinimalEmissionTest.Analyze(source.Replace("MISSING", failed, StringComparison.Ordinal));
        Assert.False(rejected.Binding.Result.IsComplete);
        Assert.Equal(expected, string.Join(" | ", Calls(rejected, callee)));
        Assert.Equal(expected, string.Join(" | ", Calls(Bind(source.Replace("MISSING", repaired, StringComparison.Ordinal)), callee)));
    }

    // SPEC 23.1.1, 15.3.5 (DECISIONS BU-12, BU-36): a genuine limit stays CallRetention.Limit beside a part Binding did not bind, which
    // ends no storage walk, so the limit is met in either Field order and either query order: a Field of height 65 before or after the
    // failed Field, a growing Field beside it, a growing Type in a program failing elsewhere, and X and T, which reach T's Box^64 Field;
    // and growth through an enum's Case beside its failed Case, the repair's limit (G242, DECISIONS BU-37; before: no row, BU-36's
    // residual).
    [Theory]
    [InlineData(Tall, "f")]
    [InlineData("enum E<T>\n    A(Missing)\n    B(rc/E<(T, T)>)\nfunc f(e: uniq/E<i32>, p: raw/i32) => ()\nfunc g(e: uniq/E<i32>) => f(e, null)\n", "f")]
    [InlineData(Box + "struct Bad\n    public var big: " + Box65 + "\n    public var m: Missing\nfunc f(b: uniq/Bad, p: raw/i32) => ()\nfunc g(b: uniq/Bad) => f(b, null)\n", "f")]
    [InlineData(Growing, "f")]
    [InlineData("func inspect<T>(value: ref/T) => ()\nstruct Nest<T>\n    public var value: T\n    public var next: Option<rc/Nest<(T, T)>> = .None\nfunc h(n: ref/Nest<i32>) => inspect(n)\nfunc other() => missing\n", "inspect")]
    [InlineData(Beyond + "func g(t: uniq/T, x: uniq/X)\n    ft(t, null)\n    fx(x, null)\n", "ft fx")]
    [InlineData(Beyond + "func g(t: uniq/T, x: uniq/X)\n    fx(x, null)\n    ft(t, null)\n", "fx ft")]
    public void AGenuineLimitStaysBesideRejectedStorage(string source, string callees)
    {
        var c = MinimalEmissionTest.Analyze(source.Replace("MISSING", "Missing", StringComparison.Ordinal));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.All(callees.Split(' '), x => Assert.Equal(["limit"], Calls(c, x)));
    }

    // DECISIONS BU-37 (G242): the hold walk walks a Case's payloads only where the instance substituted that Case, the part its storage
    // verdict walked, which bounds it: Case A, failed beside two growing payloads, is not walked, so the walk ends with no row (walked by
    // its elements it does not end), while its repair's growth is the limit. The failed Case's other payloads hold nothing, the residual
    // at the element grain (PLAN G242).
    [Fact]
    public async Task AFailedCaseIsWalkedNoFurther()
    {
        const string Source = "enum E<T>\n    A(MISSING, rc/E<(T, T)>, rc/E<Option<T>>)\n    B(i32)\nfunc f(e: uniq/E<i32>, p: raw/i32) => ()\nfunc g(e: uniq/E<i32>) => f(e, null)\n";
        var c = MinimalEmissionTest.Analyze(Source.Replace("MISSING", "Missing", StringComparison.Ordinal));
        var rows = Task.Run(() => Calls(c, "f"), TestContext.Current.CancellationToken);
        Assert.Equal([string.Empty], await rows.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal(["limit"], Calls(Bind(Source.Replace("MISSING", "i32", StringComparison.Ordinal)), "f"));
    }

    // SPEC 15.6.4, V16 (G243; DECISIONS BU-35): a row depends on the Type at a position and the Type its declaration writes there, not on
    // the route to it through the interned Types, so the hold walk visits each position once per output. Chain's Types grow as trees
    // eight wide (8^(n-1) leaves) but are 2n distinct Types, reached through rc handles, Tuples, fixed arrays and enum payloads, raw leaves
    // included, and a result holding them lent and written, which visits each position at most twice (DECISIONS BU-40); walked as trees
    // they take seconds to minutes, which the bound reports instead of hanging the run.
    [Theory]
    [InlineData("struct", 11, "")]
    [InlineData("array", 11, "")]
    [InlineData("enum", 11, "")]
    [InlineData("raw", 10, "store 0: own {0 Chain, 1 Value Chain}, reached {} untied")]
    [InlineData("lent", 10, "result {0 Value Chain, 1 Value Chain}, untied")]
    public async Task AHoldWalkVisitsEachPositionOnce(string form, int n, string expected)
    {
        var c = Bind(Chain(form, n));
        var rows = Task.Run(() => Declared(c, "f"), TestContext.Current.CancellationToken);
        Assert.Equal(expected, await rows.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    // Every call of the Kimi library and the milestone programs, an intrinsic's included, has a retention under its substitution, for the
    // callee MIR names (a direct base call's implementation, an intrinsic's library declaration), or over its declared Callable signature,
    // whose rows name its inputs: each call's Subst instantiates the contract.
    [Theory]
    [MemberData(nameof(HirColumnsTest.Corpus), MemberType = typeof(HirColumnsTest))]
    public void EveryCorpusCallHasARetention(string name)
    {
        var (c, module) = SemanticSnapshotTest.Bind(name);
        var calls = 0;
        foreach (var node in KotoTree.Walk(module.RootKoto))
        {
            for (var i = 0; CallPlan.Of(node, i) is { } call; i++, calls++)
            {
                var value = call.Kind == CalleeKind.Value;
                var callee = call is { Kind: CalleeKind.Virtual, VirtualIsDirect: true } ? call.VirtualImplementation?.SymbolOf() : call.Target;
                if (callee is null || (callee.LibraryDeclaration is null && IntrinsicDisposition.Of(callee.CompilerFunction).Kind == DispositionKind.Intrinsic))
                {
                    continue; // The compiler-only $abort has no declaration MIR names (SPEC 17.4).
                }

                var retention = value ? c.Types.Retains(call.CalleeType, call.DeclaredSignature, Definition(node))
                    : c.Types.Retains(callee, c.Types.SubstOf(call), Definition(node));
                var count = value ? call.DeclaredSignature.Components[0].Components.Count : Slots(callee);
                Assert.True(
                    retention.IsLimit || (retention.Result.Kinds.Length == count &&
                        retention.Writes.ToArray().All(x => x.Output >= 0 && x.Output < count && x.Own.Kinds.Length == count && x.All.Kinds.Length == count)),
                    $"{name}: {callee.Name}");
            }
        }

        Assert.True(calls > 0);
    }

    // One value per callee and Type instantiation: calls whose Types are equal and differ in their Origins only share it; another
    // instantiation and the declared contract have their own; a later pass with no edit reuses it (AnEditRecomputesRetentions).
    [Fact]
    public void RetentionsAreMemoizedPerInstantiationUntilAnEdit()
    {
        var c = Bind("func first<T>(x: ref/T, y: ref/T) -> ref/T during x => x\nvar a = 1\nvar b = 2\nvar t = true\n" +
            "let p = first(a@ref, b@ref)\nlet q = first(b@ref, a@ref)\nlet r = first(t@ref, t@ref)\n");
        var calls = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Select(static x => x.CallOf()!).Where(static x => x.Target.Name == "first").ToArray();
        var retentions = calls.Select(x => c.Types.Retains(x.Target, c.Types.SubstOf(x), null)).ToArray();
        Assert.NotSame(c.Types.SubstOf(calls[0]), c.Types.SubstOf(calls[1]));
        Assert.Same(retentions[0], retentions[1]);
        Assert.NotSame(retentions[0], retentions[2]);
        Assert.NotSame(retentions[0], c.Types.Retains(calls[0].Target, null, null));
        Assert.Same(c.Types.Retains(calls[0].Target, null, null), c.Types.Retains(calls[0].Target, null, null));
        c.Binding.Bind(BindingMode.Final);
        Assert.Same(retentions[0], c.Types.Retains(calls[0].Target, c.Types.SubstOf(calls[0]), null));
    }

    // An edit revokes the retentions with the whole source analysis (Binding.Invalidate), also after a pass that did not finish (review
    // 1a2 B1: a Bind that throws leaves no result): replacing a callee's clause between passes changes its call's row.
    [Theory]
    [InlineData("origin a outlives b", "origin b outlives a", "result {0 Value, 1 Value}", "result {1 Value}", false)]
    [InlineData("origin b outlives a", "origin a outlives b", "result {1 Value}", "result {0 Value, 1 Value}", true)]
    public void AnEditRecomputesRetentions(string clause, string edited, string before, string after, bool unfinished)
    {
        var source = $"func f(x: ref/i32 during a, y: ref/i32 during b) -> ref/i32 during b\n    {clause}\n    return y\nvar m = 1\nvar n = 2\nlet r = f(m@ref, n@ref)\n";
        var c = Bind(source);
        Assert.Equal([before], Calls(c, "f"));
        if (unfinished)
        {
            typeof(Binding).GetProperty(nameof(Binding.Result))!.SetValue(c.Binding, default(BindingResult)); // As a throwing Bind leaves it.
        }

        var relation = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<OriginRelationKoto>().Single();
        var donor = Bind(source.Replace(clause, edited, StringComparison.Ordinal));
        var replacement = KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<OriginRelationKoto>().Single();
        var reader = new TokenReader(replacement.CodeContext, string.Empty, []);
        var placeholder = new OriginRelationKoto(
            ref reader,
            default,
            new CodeBlockKoto(replacement.CodeContext),
            new CodeBlockKoto(replacement.CodeContext),
            equality: true);
        ParseTestHelper.Detach(replacement, placeholder);
        Assert.True(KotoHelper.Replace(relation.Parent!, relation, replacement));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Equal([after], Calls(c, "f"));
    }

    // A warm rebind that reads every call's retention again, C1 (also under a calling definition's premises) and C8, and a destruction's,
    // a drop's at its instance and an erased one's in a generic body (DECISIONS BU-47), allocates nothing: an unchanged pass reuses the
    // rows, and interning an instance, a destruction receiver and an Item whose pair argument states a raw Origin binds it in TyCtxt's
    // reused scratch (DECISIONS BU-34).
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmRebindReusesRetentions()
    {
        var c = Bind(Erased + "func use(h: ref/H{s}, q: uniq/i32 during s.a) -> i32\n    let x = q@follow@ref\n    let r = apply(h.k@ref, x)\n    return h.k(r)@follow\n" +
            "func first<T>(x: ref/T, y: ref/T) -> ref/T during x => x\nfunc both<T>(v: ref/T) -> ref/T => first(v, v)\n" + RawPointerSyntaxTest.PairHolder +
            "func id<s/T>(v: s/T) -> s/T => v@move\nfunc hold(x: ref/i32, q: raw/i32) -> raw/i32\n    let h = Holder<raw/i32 during x>.init(v: q)\n" +
            "    let g = id<raw/i32 during x>\n    return g(h.value)\nvar a = 1\nlet p = first(a@ref, a@ref)\n" + Writer + "func gen<T>(x: uniq/T) => ()\n");
        var calls = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Select(static x => (Node: x, Definition: Definition(x))).ToArray();
        Assert.True(calls.Length >= 4);
        var destructor = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.IsDestructor);
        var drop = destructor.SymbolOf()!;
        var writer = AdtDef.ReceiverType(destructor)!;
        var gen = Function(c, "gen").SymbolOf()!;
        var part = TyCtxt.Signature(gen, null, 0, out _)!.Components[0];
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Binding.Bind(BindingMode.Final);
            foreach (var (node, definition) in calls)
            {
                var call = CallPlan.Of(node, 0)!;
                _ = call.Kind == CalleeKind.Value ? c.Types.Retains(call.CalleeType, call.DeclaredSignature, definition)
                    : c.Types.Retains(call.Target, c.Types.SubstOf(call), definition);
            }

            _ = c.Types.Retains(drop, c.Types.Synthesized(drop, writer, []), null);
            _ = c.Types.RetainsErasedDestruction(part, gen);
        }));
    }

    // A retention is immutable: its records are read-only and nothing assigns their fields after construction.
    [Fact]
    public void RetentionsAreImmutable()
    {
        foreach (var record in new[] { typeof(CallRetention), typeof(OutputRetention), typeof(StorageRetention) })
        {
            Assert.All(record.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public), static x => Assert.True(x.IsInitOnly, x.Name));
            Assert.DoesNotContain(record.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public), static x => x.CanWrite);
        }
    }

    // Each output atom closes the premises once, and a closure's work is what it reaches, each node with its incoming premises: n written
    // inputs whose Origins form a chain give n closures, the i-th reaching i + 1 inputs, so the work grows with the n(n+1)/2 rows, within
    // a constant of rows plus closures at 8 and at 32 inputs.
    [Fact]
    public void ClosureWorkStaysWithinTheRows()
    {
        foreach (var size in new[] { 8, 32 })
        {
            var inputs = string.Join(", ", Enumerable.Range(0, size).Select(static i => $"x{i}: uniq/(ref/i32 during a{i})"));
            var clauses = string.Concat(Enumerable.Range(0, size - 1).Select(static i => $"    origin a{i} outlives a{i + 1}\n"));
            var c = Bind($"func f({inputs}) -> i32\n{clauses}    return 0\n");
            var metrics = new OriginProofMetrics();
            c.Binding.OriginProofMetrics = metrics;
            var rows = c.Types.Retains(Function(c, "f").SymbolOf()!, null, null).Writes.ToArray().Sum(static x => x.Own.Kinds.ToArray().Zip(x.All.Kinds.ToArray()).Count(static y => (y.First | y.Second) != RetentionKind.None));
            Assert.Equal(size * (size + 1) / 2, rows);
            Assert.Equal((size, 0L), (metrics.Closures, metrics.BoundViolations));
            Assert.True(metrics.Insertions + metrics.Activations <= (2 * rows) + (4 * size), $"{metrics.Insertions + metrics.Activations} for {rows} rows at {size}");
        }
    }

    private static Compilation Bind(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        return c;
    }

    // D1 to Dn (structs, or enums whose Case A holds the part), each holding the next through an rc handle at a Type argument eight
    // wide (a Tuple; for `array`, a fixed array and seven), the last holding the argument itself; f's input instantiates D1 at i32, or
    // at `raw/i32` for `raw`; for `lent`, f's result holds D1<raw/i32> behind a borrow and directly (DECISIONS BU-40: each position is
    // walked once lent and once written).
    private static string Chain(string form, int n)
    {
        var argument = form == "array" ? $"([2 of T], {string.Join(", ", Enumerable.Repeat("T", 7))})" : $"({string.Join(", ", Enumerable.Repeat("T", 8))})";
        var types = string.Concat(Enumerable.Range(1, n).Select(i =>
        {
            var part = i < n ? $"rc/D{i + 1}<{argument}>" : form == "array" ? "[2 of T]" : "T";
            return form == "enum" ? $"enum D{i}<T>\n    A({part})\n    B\n" : $"struct D{i}<T>\n    public var x: {part}\n";
        }));
        return types + (form == "lent"
            ? "func f(d: ref/D1<raw/i32>, p: raw/i32) -> (Option<ref/D1<raw/i32> during d>, Option<D1<raw/i32>>) => (.None, .None)\n"
            : $"func f(d: uniq/D1<{(form == "raw" ? "raw/i32" : "i32")}>, p: raw/i32) => ()\n");
    }

    private static FunctionKoto Function(Compilation c, string name)
        => KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == name);

    private static int Slots(BindingSymbol function)
    {
        _ = TyCtxt.Signature(function, null, -1, out var count);
        return count;
    }

    // The definition a call's MIR body belongs to: its enclosing function, Closure or accessor; null for top-level statements.
    private static BindingSymbol? Definition(Koto node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is PropertyAccessorKoto accessor)
            {
                return CallPlan.Accessor(accessor);
            }

            if (current is FunctionKoto function)
            {
                return function.SymbolOf();
            }
        }

        return null;
    }

    // A function's own contract, with no call substitution.
    private static string Declared(Compilation c, string name) => Show(c.Types.Retains(Function(c, name).SymbolOf()!, null, null));

    // Each call of a named callee under its substitution, in its definition, in source order.
    private static string[] Calls(Compilation c, string name)
        => KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Select(static x => (Node: x, Call: x.CallOf())).Where(x => x.Call is { Kind: not CalleeKind.Value } && x.Call.Target.Name == name)
            .Select(x => Show(c.Types.Retains(x.Call!.Target, c.Types.SubstOf(x.Call), Definition(x.Node)))).ToArray();

    // Each Value call over its declared Callable signature, in its definition, in source order.
    private static string[] ValueCalls(Compilation c)
        => KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Select(static x => (Node: x, Call: CallPlan.Of(x, 0))).Where(static x => x.Call is { Kind: CalleeKind.Value })
            .Select(x => Show(c.Types.Retains(x.Call!.CalleeType, x.Call.DeclaredSignature, Definition(x.Node)))).ToArray();

    // A retention as the records spell it (DECISIONS R4-M154): the result's kinds and whether it is untied, then per output it writes its
    // root's kinds (own) and those past it (reached, every Reached entry joined), each with its pairs (`if {i Value: longer > shorter}`),
    // whether it is untied, and the referent stores; each input with its kinds.
    private static string Show(CallRetention retention)
    {
        return retention.IsLimit ? "limit" : string.Join(", ", new[] { Kinds(retention.Result.Kinds) is { Length: > 0 } result ? $"result {{{result}}}{Pairs(retention.Result)}" : null, retention.Result.Untied ? "untied" : null }
            .Concat(retention.Writes.ToArray().Select(static x => $"store {x.Output}: own {{{Kinds(x.Own.Kinds)}}}{Pairs(x.Own)}, reached {{{Kinds(x.All.Kinds)}}}{Pairs(x.All)}" +
                (x.Own.Untied ? " untied" : string.Empty)))
            .Concat(retention.ReferentStores.ToArray().Select(static x => $"referent store {x}")).OfType<string>());

        static string Kinds(ReadOnlySpan<RetentionKind> kinds)
        {
            var parts = new List<string>();
            for (var i = 0; i < kinds.Length; i++)
            {
                if (kinds[i] != RetentionKind.None)
                {
                    parts.Add($"{i} {Flags(kinds[i])}");
                }
            }

            return string.Join(", ", parts);
        }

        static string Flags(RetentionKind kind) => string.Join(' ', new[] { RetentionKind.Value, RetentionKind.Content, RetentionKind.Chain }.Where(x => (kind & x) != 0));

        // A record's pairs: ` if {i Value: longer > shorter, ...}`, the pairs of one conjunction joined by ` & `.
        static string Pairs(StorageRetention record)
            => record.Pairs.IsEmpty ? string.Empty : $" if {{{string.Concat(record.Pairs.ToArray().Select(static (x, i) => $"{(i == 0 ? string.Empty : x.Joined ? " & " : ", ")}{x.Input} Value: {x.Longer.Name} > {x.Shorter.Name}"))}}}";
    }
}
