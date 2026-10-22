// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Reflection;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>Call retention, the Binding view A5's Loan flow reads for calls (<c>TyCtxt.Retains</c>, design A3 4.3 C1+C8, DECISIONS
/// R4-M51): which outputs of a call may retain which arguments under the premises the callee's body is checked under (SPEC 15.6.4), as
/// rows <c>input kind output</c> (the result, or <c>store j</c>, the storage input j's exclusive, raw or invariant layers let the call
/// write), and whether the result is untied.</summary>
public sealed class RetentionTest
{
    private const string Cell = "struct Cell<T> {s}\n    var item: uniq/T during s\n";

    private const string Box = "struct B<T>\n    public var item: T\n    public init(item: T) => self.item = item@move\n";

    private const string View = "struct View<T> {source}\n    let data: ref/T during source\n    public init(data: ref/T during source) => self.data = data\n" +
        "    public func read(self: ref/Self) -> ref/T during self.source => self.data\n    public func whole(self: ref/Self) -> ref/Self during self => self\n";

    private const string Slot = "struct Slot<T> {source}\n    var data: ref/T during source\n    public init(data: ref/T during source) => self.data = data\n" +
        "    public func put(self: uniq/Self, value: ref/T during source) => self.data = value\n" +
        "    public func item(self: uniq/Self) -> ref/T during self.source => self.data\n";

    // An object payload that holds a Loan, and a local to lend it.
    private const string Lent = "struct Lent {a}\n    public var r: ref/i32 during a\n    public init(r: ref/i32 during a) => self.r = r\n" +
        "var x = 1\n";

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
    // result naming an abstract part of a borrowed input's referent retains it as Content (step 4).
    [Theory]
    [InlineData("func f<T>(x: ref/T, y: ref/T) -> ref/T during x => x", "0 Value result, 1 Content result")]
    [InlineData("func f(x: ref/i32, y: ref/i32) -> ref/i32 => y", "0 Value result, 1 Value result")]
    [InlineData("func f(x: ref/i32, y: ref/i32) -> ref/i32 during x => x", "0 Value result")]
    [InlineData("func f(x: ref/i32 during a, y: ref/i32 during b) -> ref/i32 during b\n    origin a outlives b\n    return x", "0 Value result, 1 Value result")]
    [InlineData("func f(x: ref/i32 during a, y: ref/i32 during b) -> ref/i32 during b => y", "1 Value result")]
    [InlineData("func f(b: ref/(ref/i32 during c) during o) -> ref/i32 during o => b@follow", "0 Value result")]
    [InlineData("func f(b: ref/(ref/i32 during c) during o, x: ref/i32 during c) -> ref/i32 during o => x", "0 Value result, 1 Value result")]
    [InlineData("func f(x: ref/i32, n: i32) -> i32 => n", "")]
    [InlineData("func f<s/T>(value: s/T, count: s/i32) -> s/T\n    s is owner or uniq\n    T is Copy\n    _ = count\n    return value", "0 Value result, 0 Value store 0")]
    [InlineData("func f<s/T>(value: s/T, count: s/i32) -> s/T\n    s is owner or ref\n    T is Copy\n    _ = count\n    return value", "0 Value result")]
    public void DeclaredRelationsDecideTheResult(string function, string expected)
        => Assert.Equal(expected, Declared(Bind(function + "\n"), "f"));

    // SPEC 15.6.4, 15.6.1, 15.3.4: a nested function or closure is read under the premises its body is checked under, its enclosing
    // function's included: an input's well-formedness (c outlives o) there makes `value` reach the result, through a direct call, a
    // Closure called by its declared signature (C8) and an enclosing result's well-formedness alike.
    [Fact]
    public void EnclosingPremisesAreTheBodysPremises()
    {
        var nested = Bind(Enclosing + "    func pass(value: ref/i32 during c) -> ref/i32 during o => value\n" + Write + "    let r = pass(x)\n    q@follow = 5\n    return r@follow\n");
        Assert.Equal("0 Value result", Declared(nested, "pass"));
        Assert.Equal(["0 Value result"], Calls(nested, "pass"));
        var closure = Bind(Enclosing + "    let k = func (value: ref/i32 during c) -> ref/i32 during o => value\n" + Write + "    let r = k(x)\n    q@follow = 5\n    return r@follow\n");
        Assert.Equal(["0 Value result"], ValueCalls(closure));
        var result = Bind("func outer(q: uniq/i32 during c) -> Option<ref/(ref/i32 during c) during o>\n    func pass(value: ref/i32 during c) -> ref/i32 during o => value\n" +
            Write + "    let r = pass(x)\n    q@follow = 5\n    _ = r@follow\n    return .None\n");
        Assert.Equal("0 Value result", Declared(result, "pass"));
    }

    // SPEC 15.6.4, 15.6.3: a receiver retained through its referent's Origins only is Content (a read through `self`), through its own
    // outer Origin Value; a constructor's result retains the borrow it stores.
    [Theory]
    [InlineData("init", "0 Value result")]
    [InlineData("read", "0 Content result")]
    [InlineData("whole", "0 Value result")]
    public void ReceiversRetainByTheirLayer(string method, string expected)
        => Assert.Equal(expected, Declared(Bind(View), method));

    // SPEC 15.6, 15.6.4, 15.3.5: an input written through an exclusive or raw layer, also one an owned aggregate holds (an invariant slot),
    // is an output; another input whose Origin outlives the written positions is stored there, the target's own content too. An exclusive
    // input with no Origin below its layer stores nothing. What lies behind a shared layer reached before a writable slot is only read
    // (SPEC 15.6.2: a shared input, an rc payload held by value) unless a raw pointer is followed again (raw access within its authority,
    // SPEC 5.2.1); a value stored into a writable slot brings its whole Type, so an Origin below an rc or arc layer there, or below a
    // pair layer admitting only them, is stored (DECISIONS BU-1).
    [Theory]
    [InlineData("func f(dst: uniq/(ref/i32 during q), v: ref/i32 during q) => dst@follow = v", "0 Content store 0, 1 Value store 0")]
    [InlineData("func f(dst: uniq/i32, v: ref/i32) => dst@follow = v@follow", "")]
    [InlineData("func f(dst: ref/(ref/i32 during q), v: ref/i32 during q) -> i32 => v@follow", "")]
    [InlineData("func f<T>(dst: uniq/T, value: T) => dst@follow = value@move", "0 Content store 0, 1 Value store 0")]
    [InlineData("func f<T>(storage: raw/T, value: T) => ()", "0 Value store 0, 1 Value store 0")]
    [InlineData("func f(storage: ref/(raw/(ref/i32 during q)), v: ref/i32 during q) => ()", "0 Content store 0, 1 Value store 0")]
    [InlineData(Cell + "func f(dst: Cell<ref/i32 during q>, v: ref/i32 during q) => ()", "0 Value store 0, 1 Value store 0")]
    [InlineData(Cell + "func f(dst: ref/Cell<ref/i32 during q>, v: ref/i32 during q) => ()", "")]
    [InlineData(Box + "func f(dst: uniq/(rc/B<ref/i32 during q>), v: rc/B<ref/i32 during q>) => dst@follow = v@move", "0 Content store 0, 1 Value store 0")]
    [InlineData(Box + "func f(dst: uniq/(arc/B<ref/i32 during q>), v: ref/i32 during q) => ()", "0 Content store 0, 1 Value store 0")]
    [InlineData(Cell + Box + "func f(dst: Cell<rc/B<ref/i32 during q>>, v: rc/B<ref/i32 during q>) => ()", "0 Value store 0, 1 Value store 0")]
    [InlineData(Box + "func f<s/T>(dst: uniq/Option<s/B<ref/i32 during q>>, v: ref/i32 during q)\n    s is rc or arc\n    return", "0 Content store 0, 1 Value store 0")]
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
        Assert.Equal(Enumerable.Repeat("0 Content store 0, 1 Value store 0", 4), Calls(c, "append"));
    }

    // SPEC 15.6.4: a method writing through `uniq/Self` stores an input into the receiver's referent; one reading through it is
    // Content into its result.
    [Theory]
    [InlineData("put", "0 Content store 0, 1 Value store 0")]
    [InlineData("item", "0 Content result, 0 Content store 0")]
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
        Assert.Equal(["limit", "0 Value result"], Calls(c, "init"));
        Assert.Equal(["limit", string.Empty], Calls(c, "inspect"));
        var call = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Select(static x => x.CallOf()!).First(static x => x.Target.Name == "inspect");
        Assert.Same(c.Types.Retains(call.Target, c.Types.SubstOf(call), null), c.Types.Retains(call.Target, c.Types.SubstOf(call), null));
        Assert.Throws<InvalidOperationException>(() => c.Types.Retains(call.Target, c.Types.SubstOf(call), null).Rows.Length);
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
        Assert.Equal(["0 Content result"], Calls(c, "toArray"));
        Assert.Equal(["0 Content store 0, 1 Content store 0"], Calls(c, "appendCopies"));
        Assert.Equal("0 Content result, 1 Value result", Declared(c, "keep"));
        Assert.Equal("0 Content store 0, 1 Value store 0", Declared(c, "put"));
        Assert.Equal("0 Content result, 0 Content store 0", Declared(c, "first"));
    }

    // SPEC 15.6.4 step 4, 8.1.2 (DECISIONS BU-3): a pair layer whose binder admits only borrows is a borrowed input in every admitted
    // case, its slot Value and its target Content, which an admitted exclusive borrow lets the call write, as for a concrete `uniq/T`;
    // the original `s/T` and an application alike; the pair's slot still reaches storage that holds the pair by value. A pair admitting
    // `owner`, raw or an object owner is no borrow in that case, so its target stays Value.
    [Theory]
    [InlineData("func f<s/T>(a: s/T, b: ref/i32)\n    s is ref or uniq\n    return", "0 Content store 0")]
    [InlineData("func f<s/T>(a: s/T, b: ref/i32)\n    s is uniq\n    return", "0 Content store 0")]
    [InlineData("func f<s/T>(a: s/T, b: ref/i32)\n    s is objref or objuniq\n    return", "0 Content store 0")]
    [InlineData("func f<s/T>(a: s/T, b: ref/i32)\n    s is ref\n    return", "")]
    [InlineData("func f<s/T>(a: s/Option<T>, b: ref/i32)\n    s is ref or uniq\n    return", "0 Content store 0")]
    [InlineData("func f<s/T>(a: s/T, b: ref/i32) -> s/T\n    s is ref or uniq\n    return a", "0 Value result, 0 Content store 0")]
    [InlineData(
        "func f<s/T>(a: s/T, b: uniq/Array<s/T>)\n    s is ref or uniq\n    return",
        "0 Content store 0, 1 Content store 0, 0 Value store 1, 1 Content store 1")]
    [InlineData("func f<s/T>(a: s/T, b: ref/i32)\n    s is owner or uniq\n    return", "0 Value store 0")]
    [InlineData("func f<s/T>(a: s/T, b: ref/i32)\n    s is uniq or raw\n    return", "0 Value store 0")]
    [InlineData("func f<s/T>(a: s/T, b: ref/i32)\n    s is objuniq or obj\n    return", "0 Value store 0")]
    public void BorrowPairLayersAreBorrowedInputs(string function, string expected)
        => Assert.Equal(expected, Declared(Bind(function + "\n"), "f"));

    // The same for a generic caller passing its own `s/T` (A5's case 139c, a non-Copy T): the call's Subst keeps the caller's pair, whose
    // binder admits only borrows, so the call stores only the argument's referent into it; and for an erased callable over such a pair.
    [Fact]
    public void CallsOverBorrowPairLayersAreBorrowedInputs()
    {
        var c = Bind("func inner<s/T>(a: s/T, b: i32) -> ()\n    s is ref or uniq\n    return\nfunc peek<T>(x: ref/T) -> i32 => 1\n" +
            "func use<s/T>(p: s/T, item: T) -> ()\n    s is ref or uniq\n    var x = item\n    inner(x@(s/T), peek(x@ref))\n" +
            "func apply<s/T>(g: (s/Option<T>) -> (), x: s/Option<T>) -> ()\n    s is ref or uniq\n    g(x)\n");
        Assert.Equal(["0 Content store 0"], Calls(c, "inner"));
        Assert.Equal(["0 Content store 0"], ValueCalls(c));
    }

    // SPEC 15.6.4 step 1, 15.3.5 (DECISIONS R4-M36): an abstract carrier may hold borrows its Origins do not show, so an input naming a
    // Type parameter the result names is retained, as is one whose carrier lies below a layer whose Origin the result's outlives; a call
    // instantiating the parameter retains by the instance, nothing for an Origin-free one.
    [Fact]
    public void AbstractCarriersRetainUnderTheirInstances()
    {
        var c = Bind("func pick<T>(a: T, b: ref/i32) -> T => a@move\nfunc hidden<T>(x: ref/T during p, y: T) -> Option<ref/i32 during p> => .None\n" +
            "var a = 1\nvar b = 2\nlet r = pick(a@ref, b@ref)\nlet n = pick(3, b@ref)\n");
        Assert.Equal("0 Value result", Declared(c, "pick"));
        Assert.Equal("0 Value result, 1 Value result", Declared(c, "hidden"));
        Assert.Equal(["0 Value result", string.Empty], Calls(c, "pick"));
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
    // Function Type's, whose per-call Origins are the call's fresh regions; a Function Type input stores no per-call Origin of its own.
    [Fact]
    public void CallableSignaturesRetainByTheirOwnOrigins()
    {
        var c = Bind("func pass(value: ref/i32) -> ref/i32 => value\nfunc apply(f: (ref/i32) -> ref/i32, x: ref/i32) -> ref/i32 => f(x)\n" +
            "func meet(f: (ref/i32, ref/i32) -> ref/i32, x: ref/i32, y: ref/i32) -> ref/i32 => f(x, y)\n" +
            "var a = 1\nlet item = pass\nlet r = item(a@ref)\nlet closure = func (z: ref/i32) -> ref/i32 => z\nlet s = closure(a@ref)\n");
        Assert.Equal("1 Value result", Declared(c, "apply"));
        Assert.Equal(["0 Value result", "0 Value result, 1 Value result", "0 Value result", "0 Value result"], ValueCalls(c));
    }

    // Owner decision 2026-10-17 (DECISIONS R4-M52), SPEC 15.6.4: a callable whose implementation the call cannot see, a Function Type
    // value or a callable Type a contract names, was compared under premises no view reads, so each fixed Origin of its inputs reaches
    // each fixed Origin of its outputs. `make` stores functions returning their last input under `s.a outlives s.b`, so each `r` is `x`:
    // through a field's signature (its own per-call input aside), with no clause in the calling definition, and through `apply`'s
    // callable input, whose body maps `a` to `b`.
    [Theory]
    [InlineData("func use(h: ref/H{s}, q: uniq/i32 during s.a, w: ref/i32) -> i32\n    origin s.a outlives s.b\n    let x = q@follow@ref\n    let r = h.g(w, x)\n    q@follow = 5\n    return r@follow\n", "0 Value result | 1 Value result")]
    [InlineData("func use(h: ref/H{s}, q: uniq/i32 during s.a) -> i32\n    let x = q@follow@ref\n    let r = h.k(x)\n    q@follow = 5\n    return r@follow\n", "0 Value result | 0 Value result")]
    [InlineData("func use(h: ref/H{s}, q: uniq/i32 during s.a) -> i32\n    let x = q@follow@ref\n    let r = apply(h.k@ref, x)\n    q@follow = 5\n    return r@follow\n", "0 Value result | apply 0 Content result, 1 Value result")]
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
        Assert.Equal("0 Value result, 1 Value result", Declared(sets, "f"));
        Assert.Equal(["0 Value result"], ValueCalls(sets));
        var map = Bind("func map<F, T, U>(g: ref/F, x: T) -> U\n    F is Callable<(T) -> U>\n    return g(x@move)\n" +
            "var n = 1\nlet k = func (z: ref/i32) -> ref/i32 => z\nlet r = map(k@ref, n@ref)\n");
        Assert.Equal("1 Value result", Declared(map, "map"));
        Assert.Equal(["0 Value result"], ValueCalls(map));
        Assert.Equal(["1 Value result"], Calls(map, "map"));
    }

    // Review r3's B3: an erased callable's carrier output is a fixed Origin its fixed inputs reach, so `mk`'s result `a` retains the
    // View borrowing `*q` (under `v.source == c`, `use`'s result retains `q`), and a generic caller passing its own carriers to `map`
    // retains `a` through the bound `map`'s call instantiates.
    [Fact]
    public void ErasedCarriersAreFixedOrigins()
    {
        var c = Bind(View + "func use<M, A>(mk: ref/M, q: uniq/i32 during c) -> A\n    M is Callable<(View<i32>{v}) -> A>\n    origin v.source == c\n" +
            "    let a = mk(View<i32>.init(data: q@follow@ref))\n    q@follow = 5\n    return a@move\n");
        Assert.Equal(["0 Value result"], ValueCalls(c));
        Assert.Equal("1 Value result", Declared(c, "use"));
        var generic = Bind(View + "func map<F, T, U>(g: ref/F, x: T) -> U\n    F is Callable<(T) -> U>\n    return g(x@move)\n" +
            "func use<M, G, A, B>(mk: ref/M, g: ref/G, q: uniq/i32 during c) -> B\n    M is Callable<(View<i32>{v}) -> A>\n    G is Callable<(A) -> B>\n" +
            "    origin v.source == c\n    let a = mk(View<i32>.init(data: q@follow@ref))\n    let r = map(g, a@move)\n    q@follow = 5\n    return r@move\n");
        Assert.Equal(["1 Value result"], Calls(generic, "map"));
        Assert.Equal(["0 Value result", "0 Value result"], ValueCalls(generic));
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
        Assert.Equal(["0 Value result"], Calls(c, "read"));
        Assert.Equal(["0 Content result, 0 Content store 0"], Calls(c, "nextOf"));
        Assert.Equal(["0 Content result, 0 Content store 0", "0 Content result, 0 Content store 0"], Calls(c, "next"));
    }

    // SPEC 22.2.3, 15.6.3, 8.4: a requirement on the caller's abstract Self is read as the caller's premises make its contract: under
    // `I is Iterator`, `next`'s item is `I.Item`, which names no `step`, so it descends from the receiver's referent (Content) and two
    // items stay valid together; with no calling definition the same Subst reads `LentItem(step)`, which retains the receiver Borrow.
    [Fact]
    public void RequirementsReadUnderTheCallersPremises()
    {
        var c = Bind("func nextPair<I>(iterator: uniq/I) -> (Option<I.Item>, Option<I.Item>)\n    I is Iterator\n" +
            "    let first = iterator.next()\n    let second = iterator.next()\n    return (first@move, second@move)\n");
        Assert.Equal(["0 Content result, 0 Content store 0", "0 Content result, 0 Content store 0"], Calls(c, "next"));
        var call = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Select(static x => x.CallOf()!).First(static x => x.Target.Name == "next");
        Assert.Equal("0 Value result, 0 Content store 0", Show(c.Types.Retains(call.Target, c.Types.SubstOf(call), null)));
    }

    // SPEC 15.6.4 (A3 E14): an intrinsic's call retains as its library declaration says, as any call does: an object creation its value,
    // clone its handle's payload only (Content), Raw.initialize its value into the raw storage, Text.writer its destination; Raw.slice's
    // and Raw.allocate's results are untied (a fresh anchor), and a row into a raw pointer's own storage carries nothing (it holds no Loan).
    [Theory]
    [InlineData(Lent + "let h = Kimi.Intrinsics.makeObj(Lent.init(r: x@ref))\n", "makeObj", "0 Value result")]
    [InlineData(Lent + "let h = Kimi.Intrinsics.makeRc(Lent.init(r: x@ref))\n", "makeRc", "0 Value result")]
    [InlineData(Lent + "let h = Kimi.Intrinsics.makeArc(Lent.init(r: x@ref))\n", "makeArc", "0 Value result")]
    [InlineData(Lent + "let h = Kimi.Intrinsics.makeRc(Lent.init(r: x@ref))\nlet c = Kimi.Intrinsics.clone(h@ref)\n", "clone", "0 Content result")]
    [InlineData("var x = 1\nlet p: raw/(ref/i32) = Raw.allocate(1)\nunsafe => Raw.initialize(p, x@ref)\n", "initialize", "0 Value store 0, 1 Value store 0")]
    [InlineData("let p: raw/i32 = Raw.allocate(1)\nunsafe\n    let s = Raw.slice(p, 1)\n", "slice", "untied")]
    [InlineData("var b = Text.heap(8)\nlet w = Text.writer(b@uniq)\n", "writer", "0 Value result")]
    [InlineData("let p: raw/(ref/i32) = Raw.allocate(1)\nunsafe => Raw.release(p)\n", "allocate", "untied")]
    [InlineData("let p: raw/(ref/i32) = Raw.allocate(1)\nunsafe => Raw.release(p)\n", "release", "0 Value store 0")]
    public void IntrinsicsRetainAsTheirDeclarationsSay(string source, string name, string expected)
        => Assert.Equal([expected], Calls(Bind(source), name));

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
                Assert.All(retention.Rows.ToArray(), x => Assert.True(x.Input >= 0 && x.Input < count && x.Output >= Retention.Result && x.Output < count, $"{name}: {callee.Name}"));
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
    [InlineData("origin a outlives b", "origin b outlives a", "0 Value result, 1 Value result", "1 Value result", false)]
    [InlineData("origin b outlives a", "origin a outlives b", "1 Value result", "0 Value result, 1 Value result", true)]
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
        Assert.True(KotoHelper.Replace(relation.Parent!, relation, KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<OriginRelationKoto>().Single()));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal([after], Calls(c, "f"));
    }

    // A warm rebind that reads every call's retention again, C1 (also under a calling definition's premises) and C8, allocates nothing:
    // an unchanged pass reuses the rows.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmRebindReusesRetentions()
    {
        var c = Bind(Erased + "func use(h: ref/H{s}, q: uniq/i32 during s.a) -> i32\n    let x = q@follow@ref\n    let r = apply(h.k@ref, x)\n    return h.k(r)@follow\n" +
            "func first<T>(x: ref/T, y: ref/T) -> ref/T during x => x\nfunc both<T>(v: ref/T) -> ref/T => first(v, v)\nvar a = 1\nlet p = first(a@ref, a@ref)\n");
        var calls = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Select(static x => (Node: x, Definition: Definition(x))).ToArray();
        Assert.True(calls.Length >= 4);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Binding.Bind(BindingMode.Final);
            foreach (var (node, definition) in calls)
            {
                var call = CallPlan.Of(node, 0)!;
                _ = call.Kind == CalleeKind.Value ? c.Types.Retains(call.CalleeType, call.DeclaredSignature, definition)
                    : c.Types.Retains(call.Target, c.Types.SubstOf(call), definition);
            }
        }));
    }

    // A retention is immutable: its rows are read-only and nothing assigns its fields after construction.
    [Fact]
    public void RetentionsAreImmutable()
    {
        Assert.All(typeof(CallRetention).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public), static x => Assert.True(x.IsInitOnly, x.Name));
        Assert.True(typeof(Retention).IsValueType && typeof(Retention).GetFields(BindingFlags.Instance | BindingFlags.NonPublic).All(static x => x.IsInitOnly));
        Assert.DoesNotContain(typeof(CallRetention).GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public), static x => x.CanWrite);
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
            var rows = c.Types.Retains(Function(c, "f").SymbolOf()!, null, null).Rows.Length;
            Assert.Equal(size * (size + 1) / 2, rows);
            Assert.Equal((size, 0L), (metrics.Closures, metrics.BoundViolations));
            Assert.True(metrics.Insertions + metrics.Activations <= (2 * rows) + (4 * size), $"{metrics.Insertions + metrics.Activations} for {rows} rows at {size}");
        }
    }

    private static Compilation Bind(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        return c;
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

    private static string Show(CallRetention retention)
        => retention.IsLimit ? "limit" : string.Join(", ", retention.Rows.ToArray().Select(static x => $"{x.Input} {x.Kind} {(x.Output == Retention.Result ? "result" : $"store {x.Output}")}")
            .Append(retention.Untied ? "untied" : null).OfType<string>());
}
