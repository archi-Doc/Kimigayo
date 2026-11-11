// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// SPEC 7.7.3, 13.5.3, 15.1.6: a closure's receiver requirement follows its resolved operations. Only an explicit transfer of
// a capture is Consuming; bare Subjects, element reads, shared receivers and borrowed arguments only borrow the capture.
public class ClosureReceiverClassificationTest
{
    private const string Bag = "struct Bag\n    public var items: Array<i32>\n    public var label: Option<string>\n    public var n: i32 = 0\n" +
        "    public init(items: Array<i32>, label: Option<string>)\n        self.items = items@move\n        self.label = label@move\n" +
        "func total(values: ref/Array<i32>) -> i32 => 7\n";

    // The valid bodies of f (PairHead) whose Closures TheMarksAreThePublishedByCaseTakes reads: by-value positions, a nested bare entry,
    // the discarded forms, a shared use and none. G254a (PLAN G254): an assignment's, a value call's and a Dictionary entry's by-value use
    // are the valid twins of the forms AByValueUseOfAPairLayerCaptureIsExclusiveInItsReborrowCases now rejects: a `var` local whose Type
    // is the Reborrow's (its met slot), a value call in a Consuming call (no receiver borrow contains the Reborrow, SPEC 15.8.2) and an
    // unannotated Dictionary literal (its element Type is the Reborrow's). Their former forms store the Reborrow, contained in the call
    // receiver, at the binder's `o` (SPEC 8.1.1, 8.1.2): class 1, ClosureEnvironmentOriginTest and the AByValueUse rows.
    public static TheoryData<string> MarkedPrograms => new()
    {
        "    var h = func [value] () -> ()\n        let local = value\n        _ = local\n    h()\n",
        "    var h = func [value] () -> ()\n        take(value)\n        take((value))\n    h()\n",
        "    var h = func [value] () -> ()\n        var local = value\n        local = value\n        _ = local\n    h()\n",
        "    let g = func (y: s/T) -> () => ()\n    var h = func [value, g] () -> ()\n        g(value)\n        let k = g@move\n",
        "    var h = func [value] () -> ()\n        let d = [1: value]\n        let k = d.length\n",
        "    var h = func [value] () -> ()\n        var g = func [value] () -> ()\n            let local = value\n            _ = local\n        g()\n    h()\n",
        "    var h = func [value] (c: bool) -> ()\n        value\n        (value)\n        if c\n            value\n        match c\n            true => value\n            false => ()\n    h(true)\n",
        "    var h = func [value] () -> ()\n        let k = value@ref\n        _ = k\n    h()\n",
        "    var h = func [value] () -> ()\n        _ = 1\n",
    };

    [Theory]
    [InlineData("ForSubject", Bag + "let b = Bag.init([1, 2], .Some(\"x\"))\nlet sum = func [b@move] () -> i32\n    var s: i32 = 0\n    for v in b.items\n        s += v@follow\n    return s\nrequire sum() == 3 and sum() == 3 else => $abort(\"sum\")")]
    [InlineData("MatchSubject", Bag + "let c = Bag.init([1], .Some(\"x\"))\nlet has = func [c@move] () -> bool\n    match c.label\n        .Some(let t) => return true\n        .None => return false\nrequire has() and has() else => $abort(\"has\")")]
    [InlineData("BorrowedArgument", Bag + "let d = Bag.init([1], .None)\nlet arg = func [d@move] () => total(d.items)\nrequire arg() == 7 and arg() == 7 else => $abort(\"arg\")")]
    [InlineData("ExclusiveUpdate", Bag + "let c = Bag.init([1, 2], .None)\nvar bump = func [var c@move] () -> i32\n    c.items[0] += 5\n    c.n += 1\n    return c.n\nrequire bump() == 1 and bump() == 2 else => $abort(\"bump\")")]
    [InlineData("ExclusiveReceiver", Bag + "let b = Bag.init([1], .None)\nvar grow = func [var b@move] () -> i32\n    b.items.append(3)\n    b.n += 1\n    return b.n\nrequire grow() == 1 and grow() == 2 else => $abort(\"grow\")")]
    public void BorrowingBodiesAreNotConsuming(string name, string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        ScalarEmissionTest.EmitFixture("ClosureReceiver" + name, source, string.Empty);
    }

    [Fact]
    public void AnExplicitTransferIsConsuming()
    {
        const string Source = Bag + "let b = Bag.init([1, 2], .None)\nlet take = func [b@move] () -> Array<i32> => b.items@move\nlet items = take@move()\nrequire items.length == 2 else => $abort(\"take\")";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete && c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        var closure = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.IsAnonymous);
        Assert.Equal(SemanticsKind.Owner, closure.ClosureOf()!.Receiver);
    }

    // SPEC 7.7.3, 3.5, 23.1.2 (R7-Y2b): a Consuming call Copies a captured Copy Closure, which only reads it, so the outer Closure stays
    // Shared and `ref/F` supplies its call; a Non-Copy one needs `g@move()`, a Move out of the environment (Consuming), whose call through
    // `ref/F` is invalid. The bare call of a Non-Copy one fails, and its failure explains its callee: no Consuming need rests on it.
    [Theory]
    [InlineData("    let n: i32 = 1\n    let g = func [n] () -> i32 => n@move\n    let f = func [g] () -> i32 => g()\n", SemanticsKind.Ref, "")]
    [InlineData("    let s = \"a\"\n    let g = func [s@move] () -> string => s@move\n    let f = func [g@move] () -> string => g@move()\n", SemanticsKind.Owner, "InvalidAssignment_Kd r")]
    [InlineData("    let s = \"a\"\n    let g = func [s@move] () -> string => s@move\n    let f = func [g@move] () -> string => g()\n", SemanticsKind.Ref, "TransferRequired_Kd g")]
    public void AConsumingCallIsConsumingWhereItMovesTheCapturedClosure(string head, SemanticsKind receiver, string records)
    {
        var source = "public func main() -> ()\n" + head + "    let r = f@ref\n    let v = r()\n";
        var c = MinimalEmissionTest.Analyze(source);
        var f = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Last(static x => x.IsAnonymous).ClosureOf()!;
        Assert.Equal((receiver, records), (f.Receiver, Records(source)));
    }

    [Fact]
    public void AnExclusiveBodyNeedsAWritableClosure()
    {
        var c = MinimalEmissionTest.Analyze(Bag + "let c = Bag.init([1, 2], .None)\nlet bump = func [var c@move] () => c.items[0] += 5\nbump()");
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        Assert.Equal(nameof(DiagnosticCode.InvalidAssignment_Kd), Assert.Single(TestDiagnostics.Of(c)).Code);
    }

    private const string Places = "func bump(x: uniq/i32) => x@follow += 50\nstruct Counter\n    public var value: i32 = 0\n" +
        "    public computed level: i32\n        get() -> i32 => self.value\n        set(value: i32) -> () => self.value = value\n" +
        "    public func add(self: uniq/Self, n: i32) => self.value = self.value + n\n    public func read(self: ref/Self) -> i32 => self.value\n" +
        "struct ObjCell\n    public var value: i32 = 1\n    public func put(self: objuniq/Self, n: i32)\n        let p = self@follow@uniq\n        p.value = n\n" +
        "    public func set(self: uniq/Self, n: i32) => self.value = n\n" +
        "open struct Base\n    public var value: i32\n    protected init(value: i32) => self.value = value\n    public func look(self: ref/Self) -> i32 => self.value\n" +
        "struct Leaf: Base\n    public init(): base(10) => ()\nfunc make() -> obj/Leaf => Kimi.Intrinsics.makeObj(Leaf.init())\n" +
        "public func main() -> ()\n    let n = 7\n    let t = (1, 2)\n    let fa: [2 of i32] = [1, 2]\n    var c = Counter.init()\n    let r = c@uniq\n" +
        "    var a: Array<Counter> = []\n    var x = 1\n    var u: Array<uniq/i32> = []\n    u.append(x@uniq)\n    var o = Kimi.Intrinsics.makeObj(ObjCell.init())\n" +
        "    var ol = make()\n    var other = make()\n    let q = other@objuniq\n    var items: Array<i32> = [1, 2]\n    let view = items@uniq\n" +
        "    var option: Option<i32> = .Some(1)\n    let ov = option@uniq\n    let owned: Array<i32> = [1]\n    let oo: Option<i32> = .Some(2)\n    var y = 1\n" +
        "    let p = y@raw\n    var more: Array<i32> = [1, 2]\n    var span = more.indices\n    let rv = span@uniq\n    let meter = Counter.init()\n" +
        "    let f = func [var n, var t, var fa, r, var a@move, var u@move, var o@move, var ol@move, q, view, ov, owned@move, oo, p, rv, var meter@move] () -> i32\n";

    // SPEC 7.7.3, 7.7.2 (PLAN G147 (3)): a Place reached from a capture, the capture itself, a part of it, a captured referent or a raw
    // pointer's referent, that the body writes, borrows exclusively (an exclusive Borrow or an upcast to an `objuniq` view, SPEC 13.5.6),
    // lends to an exclusive parameter as an argument or receiver (a setter's and `sliceUniq`'s included), or takes as an Exclusive-mode
    // `for` or `match` Subject (SPEC 14.6.2, 15.1.6, whatever the body then does, a resolved range's included) makes the call
    // Exclusive, so a call of a `let` Closure is invalid and of a `var` one valid; the Shared twins read or borrow shared (a getter, an
    // index range slice).
    [Theory]
    [InlineData("        let s = n@uniq\n        s@follow = 3\n", true)]
    [InlineData("        let s = (n)@uniq\n        s@follow = 3\n", true)]
    [InlineData("        let s = t.0@uniq\n        s@follow = 3\n", true)]
    [InlineData("        let s = fa[0]@uniq\n        s@follow = 3\n", true)]
    [InlineData("        r@follow.add(1)\n", true)]
    [InlineData("        a[0].add(1)\n", true)]
    [InlineData("        bump(u[0])\n", true)]
    [InlineData("        let s = o@objuniq\n        s.put(2)\n", true)]
    [InlineData("        o.put(2)\n", true)]
    [InlineData("        let s = n@ref\n        let k = s@follow\n", false)]
    [InlineData("        let k = t.0@ref\n", false)]
    [InlineData("        let k = r@follow.read()\n", false)]
    [InlineData("        let k = a[0].read()\n", false)]
    [InlineData("        let s = ol@objuniq/Base\n", true)]
    [InlineData("        let s = q@objuniq/Base\n", true)]
    [InlineData("        let s = ol@objref/Base\n", false)]
    [InlineData("        for e in view\n            e@follow += 1\n", true)]
    [InlineData("        var k = 0\n        for e in view\n            k += e@follow\n", true)]
    [InlineData("        var k = 0\n        for e in owned\n            k += e@follow\n", false)]
    [InlineData("        var k: isize = 0\n        for i in rv\n            k += i\n", true)]
    [InlineData("        meter.level = 3\n", true)]
    [InlineData("        let k = meter.level\n", false)]
    [InlineData("        var s = a.sliceUniq(0..1)\n        s[0].add(1)\n", true)]
    [InlineData("        let k = a[0..1].length\n", false)]
    [InlineData("        match ov\n            .Some(let w) => w@follow = 3\n            .None => ()\n", true)]
    [InlineData("        var k = 0\n        match ov\n            .Some(let w) => k = w@follow\n            .None => ()\n", true)]
    [InlineData("        var k = 0\n        match oo\n            .Some(let w) => k = w\n            .None => ()\n", false)]
    [InlineData("        unsafe => *p = 3\n", true)]
    [InlineData("        unsafe => p[0] = 3\n", true)]
    [InlineData("        var k = 0\n        unsafe => k = *p\n", false)]
    [InlineData("        let k = ol.look()\n", false)]
    [InlineData("        o.set(2)\n", true)]
    public void AnExclusiveUseOfAPlaceReachedFromACaptureIsAnExclusiveCall(string body, bool exclusive)
        => AssertCallReceiver(Places + body + "        return 0\n    let v = f()\n", exclusive);

    // SPEC 7.7.3, 5.2.1 condition 3, 5.3, 5.4 (DECISIONS R4-M134; PLAN G251 (1); the owner's O-76 (c) (i)): an access that needs write
    // capability through a raw pointer read from the environment, through a raw Place it reaches included, or a pointer derived from one
    // (a displacement, a raw pointer conversion), a write, a Take or an in-place destruction, makes the call Exclusive; a read leaves it
    // Shared. Labels from SPEC: each `true` row's `let` call is invalid, its `var` twin valid.
    [Theory]
    [InlineData(RawProbe + "    let f = func [p] () => unsafe => consume((*p)@move)\n    f()\n", true)]
    [InlineData(RawProbe + "    let f = func [p] () => unsafe => _ = (*p)@move\n    f()\n", true)]
    [InlineData(RawProbe + "    let f = func [p] () => unsafe => (p + 0)[0] = Probe.init(2)\n    f()\n", true)]
    [InlineData(RawProbe + "    let f = func [p] () => unsafe => (p - 0)[0] = Probe.init(2)\n    f()\n", true)]
    [InlineData(RawProbe + "    let f = func [p] () => unsafe => ((p + 1) - 1)[0] = Probe.init(2)\n    f()\n", true)]
    [InlineData(RawProbe + "    let f = func [p] () => unsafe => (p@raw/i32)[0] = 5\n    f()\n", true)]
    [InlineData(RawPointerProbe + "    let f = func [pp] () => unsafe => (*pp + 0)[0] = Probe.init(2)\n    f()\n", true)]
    [InlineData(RawPointerProbe + "    let f = func [pp] () => unsafe => ((*pp)@raw/i32)[0] = 5\n    f()\n", true)]
    [InlineData(RawPointerProbe + "    let f = func [pp] () => unsafe => consume((*(*pp))@move)\n    f()\n", true)]
    [InlineData(RawProbe + "    let f = func [p] () => unsafe => Raw.initialize(p, Probe.init(2))\n    f()\n", true)]
    [InlineData(RawProbe + "    let f = func [p] () => unsafe => Raw.initialize(p + 0, Probe.init(2))\n    f()\n", true)]
    [InlineData(RawProbe + "    let f = func [p] () => unsafe => Raw.initialize(p@raw/i32, 5)\n    f()\n", true)]
    [InlineData(RawPointerProbe + "    let f = func [pp] () => unsafe => Raw.initialize(*pp, Probe.init(2))\n    f()\n", true)]
    [InlineData(RawPointerProbe + "    let f = func [pp] () => unsafe => Raw.initialize(*pp + 0, Probe.init(2))\n    f()\n", true)]
    [InlineData(RawPointerProbe + "    let f = func [pp] () => unsafe => Raw.initialize(*(pp + 0), Probe.init(2))\n    f()\n", true)]
    [InlineData(RawProbe + "    let f = func [p] () -> i32\n        unsafe => return (*p).v\n    let v = f()\n", false)]
    [InlineData(RawProbe + "    let f = func [p] () -> i32\n        unsafe => return (p + 0)[0].v\n    let v = f()\n", false)]
    [InlineData(RawPointerProbe + "    let f = func [pp] () -> i32\n        unsafe => return (*pp)[0].v\n    let v = f()\n", false)]
    [InlineData(RawProbe + "    let f = func [p] () => unsafe => Raw.release(p)\n    f()\n", false)]
    public void AWriteCapabilityAccessThroughAnEnvironmentPointerIsAnExclusiveCall(string source, bool exclusive)
        => AssertCallReceiver(source, exclusive);

    // SPEC 5.2.1 condition 3 ("every pointer derived from it (copies, displacements, ...)"), 5.3, 5.4, 7.7.3 (R7-Y2c; the owner's O-76): a
    // body local that every definition derives from an environment pointer, a copy, a displacement or a conversion of one, or a pointer read
    // through a raw Place the environment reaches, is that pointer; a call's result (O-76 (b) (i)) and an allocation are their own. Labels
    // from SPEC: each `true` row's `let` call is invalid, its `var` twin valid; each `false` row is valid.
    [Theory]
    [InlineData(RawProbe + "    let f = func [p] ()\n        unsafe\n            let q = p + 0\n            q[0] = Probe.init(2)\n    f()\n", true)]
    [InlineData(RawProbe + "    let f = func [p] ()\n        unsafe\n            let q1 = p\n            let q2 = q1 + 1\n            let q3 = q2 - 1\n            q3[0] = Probe.init(2)\n    f()\n", true)]
    [InlineData(RawProbe + "    let f = func [p] ()\n        unsafe\n            var q = p\n            q += 1\n            q[0] = Probe.init(2)\n    f()\n", true)]
    [InlineData(RawProbe + "    let f = func [p] ()\n        unsafe\n            let q = p@raw/i32\n            q[0] = 5\n    f()\n", true)]
    [InlineData(RawProbe + "    let f = func [p] ()\n        unsafe\n            let q = p\n            consume((*q)@move)\n    f()\n", true)]
    [InlineData(RawProbe + "    let f = func [p] ()\n        unsafe\n            let q = p + 0\n            Raw.initialize(q, Probe.init(2))\n    f()\n", true)]
    [InlineData(RawPointerProbe + "    let f = func [pp] ()\n        unsafe\n            let q = *pp\n            q[0] = Probe.init(2)\n    f()\n", true)]
    [InlineData(RawProbe + "    let f = func [p] () -> i32\n        unsafe\n            let q = p + 0\n            return q[0].v\n    let v = f()\n", false)]
    [InlineData(RawProbe + "    let f = func [p] () -> i32\n        unsafe\n            var q = p + 0\n            q = q + 1\n            return q[0].v\n    let v = f()\n", false)]
    [InlineData(RawProbe + "    let f = func [p] ()\n        unsafe\n            let q = Raw.allocate<Probe>(1)\n            q[0] = Probe.init(2)\n    f()\n", false)]
    [InlineData(NextProbe + "    let f = func [p] ()\n        unsafe\n            let q = next(p)\n            q[0] = Probe.init(2)\n    f()\n", false)]
    public void AWriteThroughALocalDerivedFromAnEnvironmentPointerIsAnExclusiveCall(string source, bool exclusive)
        => AssertCallReceiver(source, exclusive);

    // SPEC 5.2.1 condition 3, 7.7.3 (R7-Y2c; AGENTS: a valid form no common rule covers stays Unsupported_Kd): a write through a body local
    // that only some of its definitions derive from an environment pointer needs the definition reaching it, which no flow fact states: a
    // located Unsupported_Kd at the use, whether the program is valid (the write precedes the derived assignment) or invalid (one branch).
    [Theory]
    [InlineData("            q[0] = Probe.init(2)\n            q = p\n")]
    [InlineData("            if c => q = p\n            q[0] = Probe.init(2)\n")]
    public void AWriteThroughALocalSomeDefinitionsDeriveIsUnsupported(string tail)
    {
        var source = RawProbe + "    let c = true\n    let f = func [p, c] ()\n        unsafe\n            var q = Raw.allocate<Probe>(1)\n" + tail + "    f()\n";
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.Unsupported_Kd), error.Code);
    }

    private const string NextProbe = RawDeclarations + "func next(q: raw/Probe) -> raw/Probe\n    unsafe => return q + 1\n" +
        "public func main() -> ()\n    let p = Raw.allocate<Probe>(1)\n";

    // SPEC 5.2, 7.7.3, 22.6 (DECISIONS R7-Y2; AGENTS: a valid form no common rule covers stays Unsupported_Kd): a foreign import's operation
    // contract is prose, so a call passing it a pointer the environment reaches leaves the receiver undecided: the located Unsupported_Kd
    // at the call, in a `var` Closure too.
    [Fact]
    public void AForeignImportPassedAnEnvironmentPointerIsUnsupported()
    {
        const string Source = "group Native\n    #LibraryImport(\"kernel32\", \"QueryPerformanceCounter\")\n    public unsafe func query(value: raw/i64) -> i32\n" +
            "public func main() -> ()\n    let p = Raw.allocate<i64>(1)\n    var f = func [p] () -> i32\n        unsafe => return Native.query(p + 0)\n    let v = f()\n";
        var error = Assert.Single(DiagnosticCorpus.Check(Source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.Unsupported_Kd), "Native.query(p + 0)"), (error.Code, error.Span is { } span ? Source.Substring(span.Start, span.Length) : string.Empty));
    }

    private const string RawDeclarations = "struct Probe\n    public var v: i32\n    public init(v: i32) => self.v = v\nfunc consume(x: Probe) => ()\n";
    private const string RawProbe = RawDeclarations + "public func main() -> ()\n    let p = Raw.allocate<Probe>(1)\n";
    private const string RawPointerProbe = RawDeclarations + "public func main() -> ()\n    let pp = Raw.allocate<raw/Probe>(1)\n";

    // SPEC 8.14, 13.5.6, 7.7.3: a case adaptation of a capture that borrows it exclusively in an admitted case makes the call Exclusive,
    // the strongest requirement over the cases; one whose borrowing cases are all shared stays Shared.
    [Theory]
    [InlineData("owner or uniq", true)]
    [InlineData("owner or ref", false)]
    public void ACaseAdaptationThatBorrowsExclusivelyInSomeCaseIsAnExclusiveCall(string admitted, bool exclusive)
        => AssertCallReceiver(
            "func gen<s/T>(value: s/i32) -> i32\n    s is " + admitted + "\n    var local: i32 = 1\n    let f = func [var local] () -> i32\n" +
                "        let k = local@s/i32\n        return 0\n    return f()\npublic func main() -> ()\n    var y = 1\n",
            exclusive);

    private const string CapturedView = "public func main() -> ()\n    var n = 7\n    let view = n@uniq\n";

    // SPEC 3.5, 7.7.3, 14.9.2: an expression body without a written or expected result Type is a result source acquired by bare
    // acquisition, so a bare captured exclusive reference is Reborrowed exclusively and the call is Exclusive. The Note names that
    // Reborrow.
    [Theory]
    [InlineData(CapturedView + "    let f = func [view] () => view\n    f()\n", "f")]
    [InlineData(CapturedView + "    let f = func [view] () => view\n    let g = f@ref\n    g()\n", "g")]
    public void ABareInferredResultReborrowIsAnExclusiveCall(string source, string callee)
    {
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.InvalidAssignment_Kd), callee), (error.Code, error.Span is { } span ? source.Substring(span.Start, span.Length) : string.Empty));
        Assert.Equal("The call is Exclusive (SPEC 7.7.3): it borrows the callee exclusively, because the callee Reborrows the captured exclusive reference view exclusively", error.Note);
    }

    private const string ExclusiveCallNote = "The call is Exclusive (SPEC 7.7.3): it borrows the callee exclusively, because a call of the callee may change its environment, or use it or what it reaches with exclusive access";

    // G254a: the first exclusive use is Binding's recorded same-Semantics Reborrow of the captured pair layer `x` (an assignment source,
    // a Dictionary entry), so the Note names it, as for a captured `uniq` (BoundClosure.ExclusiveReborrow).
    private const string ReborrowCallNote = "The call is Exclusive (SPEC 7.7.3): it borrows the callee exclusively, because the callee Reborrows the captured exclusive reference x exclusively";

    // SPEC 7.7.3, 8.10: a call's Note holds for every cause of its receiver (7.7.3's Exclusive row also names a use with exclusive
    // access): a body that only reads through an exclusive borrow of a capture, and a generic callee, whose receiver is its declared
    // Callable receiver (uniq or owner, 7.7.3's last paragraph) with no body known.
    [Theory]
    [InlineData("public func main() -> ()\n    var n = 7\n    let f = func [var n] () -> i32\n        let s = n@uniq\n        return s@follow\n    let v = f()\n", ExclusiveCallNote)]
    [InlineData("func callIt<F>(f: F) -> i32\n    F is Callable<uniq, () -> i32>\n    return f()\npublic func main() -> ()\n    let v = 0\n", ExclusiveCallNote)]
    [InlineData("func callIt<F>(f: ref/F) -> i32\n    F is Callable<owner, () -> i32>\n    return f()\npublic func main() -> ()\n    let v = 0\n", "The call is Consuming (SPEC 7.7.3): it may move values out of the callee's environment, which a reference cannot supply, since a callable value is no read Type (SPEC 3.5.3, 7.3)")]
    public void ACallNoteHoldsForEveryCauseOfItsReceiver(string source, string note)
    {
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.InvalidAssignment_Kd), "f"), (error.Code, error.Span is { } span ? source.Substring(span.Start, span.Length) : string.Empty));
        Assert.Equal(note, error.Note);
    }

    // SPEC 3.5.1, 10.2.2, 7.7.3 (PLAN G232): a captured exclusive reference, `uniq/T` or `objuniq/T`, at a fixed expected Type that it fits is
    // Reborrowed exclusively, as at a bare acquisition position, so the call is Exclusive and a `let` Closure's call invalid; the
    // Reborrow is contained in the call's receiver, which `during r` does not prove. Each row: the records.
    [Theory]
    [InlineData("    var n: i32 = 1\n    let r = n@uniq\n", "uniq/i32")]
    [InlineData("    var o = Kimi.Intrinsics.makeObj(P.init())\n    let r = o@objuniq\n", "objuniq/P")]
    public void AFittingFixedExpectedTypeReborrowsACapturedExclusiveReference(string head, string type)
    {
        var source = "struct P\n    public var x: i32 = 0\n    public init() => ()\npublic func main() -> ()\n" + head +
            "    let f = func [r] () -> i32\n        let s: " + type + " during r = r\n        return 0\n    let v = f()\n";
        Assert.Equal("UnprovenOriginRelation_Kd r; InvalidAssignment_Kd f", Records(source));
    }

    // SPEC 7.3.3 (check 1), 7.7.3, 13.5.5: an object callee supplies only its payload authority. rc, arc and objref grant shared access,
    // a let obj handle is not writable, a reference supplies no owning receiver and a payload offers no Take; a temporary callee, a
    // callee reached through a reference or an element and an Exclusive call of an unproven Sealed payload are located limits before
    // generation, and an environment-borrowing result keeps the handle lent: since the MIR decides (R6-check H5; H3b T48, was the old
    // analysis's located Unsupported_Kd at the call), moving the handle while the result lives is A5's ComparisonLoanConflict_Kd at
    // `h@move`'s `h` (`at` in `context`).
    [Theory]
    [InlineData("let count = 0\nvar next = func [var count] () -> i32\n    count += 1\n    return count\nlet h = Kimi.Intrinsics.makeRc(next@move)\nlet a = h()", "SharedPathAccess_Kd", "h", "The call is Exclusive (SPEC 7.7.3)", true)]
    [InlineData("let count = 0\nvar next = func [var count] () -> i32\n    count += 1\n    return count\nlet h = Kimi.Intrinsics.makeArc(next@move)\nlet a = h()", "SharedPathAccess_Kd", "h", "The call is Exclusive (SPEC 7.7.3)", true)]
    [InlineData("let count = 0\nvar next = func [var count] () -> i32\n    count += 1\n    return count\nlet h = Kimi.Intrinsics.makeRc(next@move)\nlet a = h@follow()", "SharedPathAccess_Kd", "h@follow", "The call is Exclusive (SPEC 7.7.3)", true)]
    [InlineData("let count = 0\nvar next = func [var count] () -> i32\n    count += 1\n    return count\nvar o = Kimi.Intrinsics.makeObj(next@move)\nlet v = o@objref\nlet a = v()", "SharedPathAccess_Kd", "v", "The call is Exclusive (SPEC 7.7.3)", false)]
    [InlineData("let count = 0\nvar next = func [var count] () -> i32\n    count += 1\n    return count\nvar h = Kimi.Intrinsics.makeObj(next@move)\nlet u = h@objuniq\nlet r = u@ref\nlet a = r@follow()", "SharedPathAccess_Kd", "r@follow", "The call is Exclusive (SPEC 7.7.3)", false)]
    [InlineData("func call<F>(v: rc/F) -> i32\n    F is Callable<uniq, () -> i32> and ObjectPayload and Sealed\n    return v()\n()", "SharedPathAccess_Kd", "v", "The call is Exclusive (SPEC 7.7.3)", true)]
    [InlineData("let count = 0\nvar next = func [var count] () -> i32\n    count += 1\n    return count\nlet o = Kimi.Intrinsics.makeObj(next@move)\nlet a = o()", "InvalidAssignment_Kd", "o", "The call is Exclusive (SPEC 7.7.3)", false)]
    [InlineData("let count = 0\nvar next = func [var count] () -> i32\n    count += 1\n    return count\nvar o = Kimi.Intrinsics.makeObj(next@move)\nlet outer = func [var o@move] () -> i32 => o@follow()\nlet a = outer()", "InvalidAssignment_Kd", "outer", "The call is Exclusive (SPEC 7.7.3)", false)]
    [InlineData("let text = \"abc\"\nlet h = Kimi.Intrinsics.makeArc(func [text@move] () -> string => text@move)\nlet a = h()", "SharedPathAccess_Kd", "h", "The call is Consuming (SPEC 7.7.3)", true)]
    [InlineData("let n: i32 = 4\nlet h = Kimi.Intrinsics.makeRc(func [n] () -> i32 => n@move)\nlet a = h()", "SharedPathAccess_Kd", "h", "The call is Consuming (SPEC 7.7.3)", true)]
    [InlineData("let text = \"abc\"\nvar h = Kimi.Intrinsics.makeObj(func [text@move] () -> string => text@move)\nlet a = h()", "InvalidAssignment_Kd", "h", "The call is Consuming (SPEC 7.7.3)", false)]
    [InlineData("let text = \"abc\"\nvar h = Kimi.Intrinsics.makeObj(func [text@move] () -> string => text@move)\nlet u = h@objuniq\nlet a = u()", "InvalidAssignment_Kd", "u", "The call is Consuming (SPEC 7.7.3)", false)]
    [InlineData("func call(r: ref/(() -> i32)) -> i32 => 1\nlet n: i32 = 4\nlet f = func [n] () -> i32 => n@move\nlet r = f@ref\nlet a = r()", "InvalidAssignment_Kd", "r", "The call is Consuming (SPEC 7.7.3)", false)]
    [InlineData("let n: i32 = 4\nlet h = Kimi.Intrinsics.makeRc(func [n] () => n@ref)\nlet r = h()\nlet moved = h@move\nrequire r@follow == 4 else => $abort(\"result\")", "ComparisonLoanConflict_Kd", "h", null, false, "h@move")]
    [InlineData("let n: i32 = 4\nlet h = Kimi.Intrinsics.makeRc(func [n] () => n@ref)\nlet r = h@follow()\nlet moved = h@move\nrequire r@follow == 4 else => $abort(\"result\")", "ComparisonLoanConflict_Kd", "h", null, false, "h@move")]
    [InlineData("func call<F>(v: objuniq/F) -> i32\n    F is Callable<uniq, () -> i32> and ObjectPayload\n    return v()\n()", "Unsupported_Kd", "v", null, false)]
    [InlineData("struct Holder\n    public var callback: rc/(() -> i32)\n    public init(callback: rc/(() -> i32)) => self.callback = callback@move\nfunc call(holder: ref/Holder) -> i32 => holder.callback()\nlet f: () -> i32 = func [] () -> i32 => 7\nlet holder = Holder.init(Kimi.Intrinsics.makeRc(f@move))\nrequire call(holder@ref) == 7 else => $abort(\"field\")", "Unsupported_Kd", "holder.callback", null, false)]
    [InlineData("func call(r: ref/(rc/(() -> i32))) -> i32 => r@follow()\nlet f: () -> i32 = func [] () -> i32 => 7\nlet h = Kimi.Intrinsics.makeRc(f@move)\nrequire call(h@ref) == 7 else => $abort(\"follow\")", "Unsupported_Kd", "r@follow", null, false)]
    [InlineData("let f: () -> i32 = func [] () -> i32 => 7\nlet hs = [Kimi.Intrinsics.makeRc(f@move)]\nrequire hs[0]() == 7 else => $abort(\"element\")", "Unsupported_Kd", "hs[0]", null, false)]
    public void ObjectCalleesSupplyOnlyTheirPayloadAuthority(string source, string code, string at, string? note, bool authority, string? context = null)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Generate(TextWriter.Null));
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((code, at), (error.Code, error.Span is { } span ? source.Substring(span.Start, span.Length) : string.Empty));
        if (context is not null)
        {
            Assert.Equal(MirTargetSide.At(context, at).Resolve(source).Start, error.Span!.Value.Start);
        }

        if (note is not null)
        {
            Assert.Contains(note, error.Note, StringComparison.Ordinal);
        }

        Assert.Equal(authority, error.Note?.Contains("rc and arc provide shared payload access only", StringComparison.Ordinal) == true);
        Assert.Null(error.Repairs);
    }

    // A closure that calls its captured object's payload exclusively is itself Exclusive.
    [Fact]
    public void AnExclusivePayloadCallMakesTheCaptureExclusive()
        => ScalarEmissionTest.EmitFixture("ClosureReceiverPayloadFollow", "let count = 0\nvar next = func [var count] () -> i32\n    count += 1\n    return count\nvar o = Kimi.Intrinsics.makeObj(next@move)\nvar outer = func [var o@move] () -> i32 => o@follow()\nrequire outer() == 1 and outer() == 2 else => $abort(\"captured follow\")", string.Empty);

    [Fact]
    public void AScalarReadThroughACapturedExclusiveReferenceIsShared()
        => ScalarEmissionTest.EmitFixture("ClosureReceiverScalarRead", "var n = 7\nlet view = n@uniq\nlet f = func [view] () => view@follow + 1\nlet r = f()\nrequire r == 8 and f() == 8 else => $abort(\"r\")", string.Empty);

    private const string PairHead = "func take<t/U>(y: t/U)\n    t is owner or uniq\n    U is Copy\n    return\nfunc f<s/T>(value: s/T) -> ()\n    s is owner or uniq\n    T is Copy\n";
    private const string PairMain = "public func main() -> ()\n    var n: i32 = 1\n    f(n)\n";
    private const string PairCall = "func pair<t/U>(a: i32, y: t/U)\n    t is owner or uniq\n    U is Copy\n    return\n";
    private const string UniqHead = "public func main() -> ()\n    var n: i32 = 1\n    let value = n@uniq\n";
    private const string MissingAndExclusive = "UnresolvedBinding_Kd missing; InvalidAssignment_Kd h";
    private const string PendingGxs = "Unsupported_Kd let g = func (z: s/T) -> () => (); ComparisonLoanConflict_Kd let g = func (z: s/T) -> () => ()";
    private const string Node = "contract Loaded\n    func load(self: ref/Self) -> i32\ncontract Bump\n    func bump(self: uniq/Self)\n" +
        "struct Node\n    Self is Copy\n    Self is Loaded\n    Self is Bump\n    public var weight: i32\n    public init(weight: i32) => self.weight = weight\n" +
        "    public func load(self: ref/Self) -> i32 => self.weight\n    public func bump(self: uniq/Self) => self.weight += 1\n";

    // SPEC 7.7.3, 8.14 (DECISIONS R4-M126 PD13): a bare acquisition of a captured pair layer Reborrows exactly in its exclusive cases,
    // so the call is Exclusive there. A Closure publishes the receiver every case has and marks such captures; a case's receiver is
    // the case-free one, raised to Exclusive where a marked capture's case Type is an exclusive layer, and Receiver is the strongest
    // over the cases. Each row: per Closure in source order, the case-free receiver, the owner and uniq cases' receivers and the
    // marked captures. A value position its consumer takes by case (ClosureEffects.TakenByCase: an argument of a call or a value
    // call, an assignment's source, a Dictionary entry, through parentheses too) acquires as a bare use does; a Reborrow of a Place reached
    // through a capture, and an explicit case adaptation of a capture that is no pair layer, count in every case. An explicit transfer of
    // the capture moves it out of the environment in every case (Consuming, R7-Y2b).
    [Theory]
    [InlineData("    var h = func [value] () -> ()\n        let local = value\n        _ = local\n    h()\n    h()\n", "Ref Ref Uniq [value]")]
    [InlineData("    let h = func [value] () -> ()\n        let local = value\n        _ = local\n    h()\n    h()\n", "Ref Ref Uniq [value]")]
    [InlineData("    var h = func [value] () -> ()\n        var g = func [value] () -> ()\n            _ = 1\n        g()\n    h()\n", "Ref Ref Uniq [value] | Ref Ref Ref []")]
    [InlineData("    var h = func [value] () -> ()\n        var g = func [value] () -> ()\n            let local = value\n            _ = local\n        g()\n    h()\n", "Ref Ref Uniq [value] | Ref Ref Uniq [value]")]
    [InlineData("    var k: i32 = 0\n    var h = func [value, var k] () -> ()\n        let local = value\n        k += 1\n", "Uniq Uniq Uniq [value]")]
    [InlineData("    let text = \"abc\"\n    let h = func [value, text@move] () -> ()\n        let local = value\n        let t = text@move\n", "Owner Owner Owner [value]")]
    [InlineData("    var h = func [value] () -> ()\n        take(value)\n", "Ref Ref Uniq [value]")]
    [InlineData("    var h = func [value] (q: s/T) -> ()\n        var local = q\n        local = value\n        _ = local\n", "Ref Ref Uniq [value]")]
    [InlineData("    let g = func (y: s/T) -> () => ()\n    var h = func [value, g@ref] () -> ()\n        g(value)\n", "Ref Ref Ref [] | Ref Ref Uniq [value]")]
    [InlineData("    var h = func [value] () -> ()\n        let d: Dictionary<i32, s/T> = [1: value]\n", "Ref Ref Uniq [value]")]
    [InlineData("    var h = func [value] () -> ()\n        take((value))\n", "Ref Ref Uniq [value]")]
    [InlineData("    var h = func [value] () -> ()\n        _ = 1\n", "Ref Ref Ref []")]
    [InlineData("    let pair = (value, 1)\n    var h = func [pair@move] () -> ()\n        let local = pair.0\n        _ = local\n", "Uniq Uniq Uniq []")]
    [InlineData("    var local: i32 = 1\n    var h = func [var local] () -> ()\n        let k = local@s/i32\n", "Uniq Uniq Uniq []")]
    [InlineData("    let h = func [value] () -> ()\n        take(value@move)\n", "Owner Owner Owner []")]
    public void EachAdmittedCaseHasTheReceiverItsFactsGive(string body, string expected)
    {
        var c = MinimalEmissionTest.Analyze(PairHead + body + PairMain);
        var f = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => !x.IsAnonymous && x.Name == "f");
        var target = f.SymbolOf()!.Schema!.GenericSlots[0].Symbol;
        var facts = new List<string>();
        foreach (var closure in KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Where(x => x.IsAnonymous).Select(x => x.ClosureOf()!))
        {
            var owner = CaseReceiver(c, closure, new(target, SemanticsKind.Owner));
            var uniq = CaseReceiver(c, closure, new(target, SemanticsKind.Uniq));
            Assert.True(closure.Classified);
            Assert.Equal(uniq, closure.Receiver); // The strongest case here: only an exclusive case raises the receiver.
            facts.Add($"{closure.CaseFreeReceiver} {owner} {uniq} [{string.Join(",", closure.Captures.Where(x => x.BareReborrows).Select(x => x.Environment.Name))}]");
        }

        Assert.Equal(expected, string.Join(" | ", facts));
    }

    // SPEC 3.5, 8.14, 7.7.3: a value position its consumer takes by value (an argument of a call or a value call, an assignment's
    // source, a Dictionary entry) acquires a captured pair layer as each case's Type does, so it Reborrows in the exclusive cases and
    // the call is Exclusive there: the strongest receiver is Exclusive, and a `let` Closure's call is invalid; under admitted cases that
    // only Copy it stays Shared. The value call, the assignment and the Dictionary entry were Shared in every case (2c-2 review B1).
    // The exclusive rows' `var` twin is valid, but the Exclusive call `f(y)` stores the receiver's Content, which leaves the shared capture
    // `g@ref`'s Loan where `g`'s storage ends, so since the MIR decides (R6-check H5; H3b T30) it publishes, under `s = uniq`, a located
    // Unsupported_Kd and a ComparisonLoanConflict_Kd at `let g = ...` (PendingGxs). H5's (e) list (the owner, 2026-11-03, 「推奨どおり」,
    // board O-1): PLAN G148 (2), GXS's proposal, which reconciles SPEC 15.6.4 with 15.8.2's retention, restores the acceptance and
    // re-points these rows back to no record. G254a (PLAN G254; Q17 class 1; SPEC 8.1.1, 8.1.2, 15.4.2, 15.6.3, 15.8.2): under `s = uniq`
    // the use Reborrows the captured `x` within the call receiver, and `g`'s parameter and the annotated Dictionary element are the
    // binder's `o`, so `g(x)` and the entry are UnprovenOriginRelation_Kd at the use on both twins (the literal's fit and the entry's); the
    // assignment into `var local = q` is valid, a `var` local's pair slot being a region of the local (PLAN G758, which removed G254a's
    // interim `Unsupported_Kd`). Their valid twins are MarkedPrograms' bodies 3-5.
    [Theory]
    [InlineData("        take(x)\n", "owner or uniq", true, PendingGxs)]
    [InlineData("        take(x)\n", "owner or ref", false)]
    [InlineData("        var local = q\n        local = x\n        _ = local\n", "owner or uniq", true, PendingGxs, "", true)]
    [InlineData("        var local = q\n        local = x\n        _ = local\n", "owner or ref", false)]
    [InlineData("        g(x)\n", "owner or uniq", true, PendingGxs + "; UnprovenOriginRelation_Kd x", "UnprovenOriginRelation_Kd x; ")]
    [InlineData("        g(x)\n", "owner or ref", false)]
    [InlineData("        let d: Dictionary<i32, s/T> = [1: x]\n        let k = d.length\n", "owner or uniq", true, PendingGxs + "; UnprovenOriginRelation_Kd [1: x]; UnprovenOriginRelation_Kd x", "UnprovenOriginRelation_Kd [1: x]; UnprovenOriginRelation_Kd x; ", true)]
    [InlineData("        let d: Dictionary<i32, s/T> = [1: x]\n        let k = d.length\n", "owner or ref", false)]
    public void AByValueUseOfAPairLayerCaptureIsExclusiveInItsReborrowCases(string use, string admitted, bool exclusive, string varRecords = "", string letBefore = "", bool reborrowNote = false)
        => AssertCallReceiver(
            "func take<t/U>(y: t/U)\n    t is " + admitted + "\n    U is Copy\n    return\nfunc outer<s/T>(x: s/T, y: s/T)\n    s is " + admitted + "\n" +
                "    T is Copy\n    let g = func (z: s/T) -> () => ()\n    let f = func [x, g@ref] (q: s/T) -> i32\n" + use + "        return 0\n    let v = f(y)\n" +
                "public func main() -> ()\n    var n = 1\n    var m = 2\n    outer(n, m)\n",
            exclusive,
            varRecords,
            letBefore,
            reborrowNote ? ReborrowCallNote : ExclusiveCallNote);

    // SPEC 3.5, 14.2.2, 23.1.2 (DECISIONS R4-M145; the owner, 2026-10-26): a captured pair layer in Discard Context (a statement, in
    // parentheses, a discarded selection body) is acquired by bare acquisition, as at a value position, so it Reborrows in its exclusive
    // cases and a `let` Closure's call is Exclusive. A use is explained only by its own failure or by the failure or dependency of the
    // consumer whose operation takes it: an aggregate element's sibling explains nothing, so `(value, missing)`, `[value, missing]` and
    // `(take(value), missing)` keep the mark and a `let` Closure's call stays Exclusive, and a dependent call `pair(missing, value)`
    // decides only a bare acquisition position, which its argument is not; a discarded use has no consumer whose failure explains it,
    // so a failed sibling arm leaves the mark (BU's re-check 3, N1); an assignment's failed target fixes its source's Type and explains
    // it (the concrete twin is Shared); a later use of the Case value that takes it explains nothing: after `.Some(value)`, the bare
    // `_ = o` is its own TransferRequired_Kd in the uniq case (SPEC 3.5.1; DECISIONS BU-84). Each row: the records under `let h`, then
    // under `var h`.
    [Theory]
    [InlineData("        value\n", "InvalidAssignment_Kd h", "")]
    [InlineData("        (value)\n", "InvalidAssignment_Kd h", "")]
    [InlineData("        if c\n            value\n", "InvalidAssignment_Kd h", "")]
    [InlineData("        let t = (value, missing)\n        _ = t\n", MissingAndExclusive, "UnresolvedBinding_Kd missing")]
    [InlineData("        let a = [value, missing]\n        _ = a\n", MissingAndExclusive, "UnresolvedBinding_Kd missing")]
    [InlineData("        let t = (take(value), missing)\n        _ = t\n", MissingAndExclusive, "UnresolvedBinding_Kd missing")]
    [InlineData("        pair(missing, value)\n", "UnresolvedBinding_Kd missing", "UnresolvedBinding_Kd missing")]
    [InlineData("        missing = value\n", "UnresolvedBinding_Kd missing", "UnresolvedBinding_Kd missing")]
    [InlineData("        match c\n            true => value\n            false => missing\n", MissingAndExclusive, "UnresolvedBinding_Kd missing")]
    // G254a (PLAN G254; Q17 class 1; SPEC 8.1.1, 8.1.2, 15.6.3, 15.8.2): the payload, a Reborrow of `value` contained in the call receiver,
    // is fitted to `Option<s/T>`, the binder's `o`: UnprovenOriginRelation_Kd at it (its valid twin: the payload outside the Closure,
    // ClosureEnvironmentOriginTest's nst row).
    [InlineData("        let o: Option<s/T> = .Some(value)\n        _ = o\n", "UnprovenOriginRelation_Kd value; TransferRequired_Kd o; InvalidAssignment_Kd h", "UnprovenOriginRelation_Kd value; TransferRequired_Kd o")]
    // DECISIONS: G572 U2a (Q13, SPEC 23.1.2): t's Type string holds with the write's obligation pending, so `_ = t`, a bare acquisition, is
    // judged on its own.
    [InlineData("        let t = \"\\(value)\"\n        _ = t\n", "UnprovenConstraint_Kd \"\\(value)\"; TransferRequired_Kd t", "UnprovenConstraint_Kd \"\\(value)\"; TransferRequired_Kd t")]
    [InlineData("        let b = Box(value)\n        _ = b\n", "UnresolvedBinding_Kd Box", "UnresolvedBinding_Kd Box")]
    [InlineData("        let k = value@copy\n        _ = k\n", "NonCopyOperand_Kd value@copy", "NonCopyOperand_Kd value@copy")]
    [InlineData("        let a: [2 of i32] = [1, 2]\n        let k = a[value]\n        _ = k\n", "TypeMismatch_Kd value", "TypeMismatch_Kd value")]
    public void ADiscardedUseIsABareAcquisitionAndOnlyItsDecidingConsumerExplainsAUse(string use, string letRecords, string varRecords)
    {
        var source = PairCall + PairHead + "    var h = func [value] (c: bool) -> ()\n" + use + "    h(true)\n" + PairMain;
        Assert.Equal(letRecords, Records(source.Replace("    var h = func", "    let h = func", StringComparison.Ordinal)));
        Assert.Equal(varRecords, Records(source));
    }

    // SPEC 14.2.2, 3.5, 3.5.1, 7.7.3 (DECISIONS R4-M145; PLAN G251 (7)): a captured Place in Discard Context is acquired as at a value
    // position: a stored exclusive reference Reborrows, so its Closure is Exclusive and a `let` Closure's call invalid, as a statement,
    // in parentheses and as a discarded `if` or `match` body, as `let k = value` is; a Copy capture is read and stays Shared; a bare
    // Non-Copy capture needs its Move spelled, and `title@move` makes the call Consuming, so it needs `h@move`; so does the bare rule's
    // Move row of `title` (R7-Y2b: a record dependent on the failed acquisition, DECISIONS Q13). Each row: the records
    // under `let h`, under `var h`, and of the same use outside a Closure (a discarded Copy local's lint, DiscardedValue_Kd, there).
    [Theory]
    [InlineData("value\n", "InvalidAssignment_Kd h", "", "")]
    [InlineData("(value)\n", "InvalidAssignment_Kd h", "", "")]
    [InlineData("if c\n    value\n", "InvalidAssignment_Kd h", "", "")]
    [InlineData("match c\n    true => value\n    false => ()\n", "InvalidAssignment_Kd h", "", "")]
    [InlineData("let k = value\n_ = k@move\n", "InvalidAssignment_Kd h", "", "")]
    [InlineData("count\n", "", "", "DiscardedValue_Kd count")]
    [InlineData("title\n", "TransferRequired_Kd title; TransferRequired_Kd h", "TransferRequired_Kd title; TransferRequired_Kd h", "TransferRequired_Kd title")]
    [InlineData("title@move\n", "TransferRequired_Kd h", "TransferRequired_Kd h", "")]
    public void ADiscardedCapturedPlaceIsAcquiredAsAtAValuePosition(string use, string letRecords, string varRecords, string freeRecords)
    {
        var lines = use.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var source = "func run(value: uniq/i32, count: i32, title: string)\n    var h = func [value, count, title@move] (c: bool) -> ()\n" +
            string.Concat(lines.Select(static x => "        " + x + "\n")) + "    h(true)\npublic func main() => ()\n";
        var free = "func run(value: uniq/i32, count: i32, title: string, c: bool)\n" + string.Concat(lines.Select(static x => "    " + x + "\n")) +
            "public func main() => ()\n";
        Assert.Equal(
            (letRecords, varRecords, freeRecords),
            (Records(source.Replace("    var h = func", "    let h = func", StringComparison.Ordinal)), Records(source), Records(free)));
    }

    // SPEC 7.3.3, 3.5 (DECISIONS R4-M145, C1): a method call's receiver is its call's input, taken by the call's receiver operation: a
    // `ref/Self` receiver borrows the captured pair layer, so its Closure stays Shared under `let` and `var`, also in parentheses; a
    // `uniq/Self` receiver that the pair layer cannot supply is only the call's NoApplicableOverload_Kd, with no net record.
    [Theory]
    [InlineData("        let k = value.load()\n        _ = k\n", "")]
    [InlineData("        let k = (value).load()\n        _ = k\n", "")]
    [InlineData("        value.bump()\n", "NoApplicableOverload_Kd value.bump()")]
    public void AReceiverIsTakenByItsCallsReceiverOperation(string use, string records)
    {
        var source = Node + "func f<s/T>(value: s/T) -> ()\n    s is owner or uniq\n    T is Copy and Loaded and Bump\n    var h = func [value] () -> ()\n" + use +
            "    h()\npublic func main() -> ()\n    var n = Node.init(1)\n    f(n)\n";
        Assert.Equal((records, records), (Records(source.Replace("    var h = func", "    let h = func", StringComparison.Ordinal)), Records(source)));
    }

    // SPEC 3.5, 8.14 (DECISIONS R4-M145; R5's condition 2): the captures a Closure's classification marks are those the published HIR
    // takes by case, under a pair layer that Reborrows in some admitted case: a use of the binding itself whose Adjust column or plan row
    // (an earlier call's, a discarded Place's) is ByCase, or a nested Closure's bare entry.
    [Theory]
    [MemberData(nameof(MarkedPrograms))]
    public void TheMarksAreThePublishedByCaseTakes(string body)
    {
        var c = MinimalEmissionTest.Analyze(PairHead + body + PairMain);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        HirTables.Publish(c);
        foreach (var function in KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Where(static x => x.IsAnonymous))
        {
            var taken = new HashSet<BindingSymbol>();
            foreach (var node in KotoTree.Walk(function).Where(x => !ReferenceEquals(x, function) && ReferenceEquals(Enclosing(x), function)))
            {
                if (HirTables.ResOf(node) == HirRes.Capture && (HirTables.AdjustOf(node).Acquire == HirAcquire.ByCase || HirTables.PlanOf(node).Adjust.Acquire == HirAcquire.ByCase))
                {
                    taken.Add(HirTables.Symbol(node)!);
                }
                else if (node is FunctionKoto { IsAnonymous: true } nested)
                {
                    taken.UnionWith(nested.ClosureOf()!.Captures.Where(static x => x.Adjust.Acquire == HirAcquire.ByCase).Select(static x => x.Source));
                }
            }

            var captures = function.ClosureOf()!.Captures;
            Assert.Equal(
                captures.Where(static x => x.BareReborrows).Select(static x => x.Environment.Name),
                captures.Where(x => taken.Contains(x.Environment) && c.Binding.BareReborrows(x.Environment.Type!, function)).Select(static x => x.Environment.Name));
        }

        static FunctionKoto? Enclosing(Koto node)
        {
            for (var parent = node.Parent; parent is not null; parent = parent.Parent)
            {
                if (parent is FunctionKoto { IsAnonymous: true } function)
                {
                    return function;
                }
            }

            return null;
        }
    }

    // SPEC 3.5, 3.5.1, 12.4.3, 23.1.2 (PLAN G256): a Dictionary literal's key and value are bare acquisition positions, as a tuple or
    // array element is, so a failed key, value or sibling entry explains nothing: the pair layer and its concrete twin over `uniq/i32`
    // both keep the use's acquisition, and a `let` Closure's call stays Exclusive (the valid literal: OriginRelationDiagnosticTest). An
    // independent array literal binds every element after a failed one, so its later element is acquired too (SPEC 4.3).
    // Each row: the records under `let h`, then under `var h`, the same for both twins.
    [Theory]
    [InlineData("        let d = [missing: value]\n        _ = d@move\n", MissingAndExclusive, "UnresolvedBinding_Kd missing")]
    [InlineData("        let d = [1: value, 2: missing]\n        _ = d@move\n", MissingAndExclusive, "UnresolvedBinding_Kd missing")]
    [InlineData("        let d = [1: missing, 2: value]\n        _ = d@move\n", MissingAndExclusive, "UnresolvedBinding_Kd missing")]
    [InlineData("        let d = [1: value, 2: true]\n        _ = d@move\n", "TypeMismatch_Kd true; InvalidAssignment_Kd h", "TypeMismatch_Kd true")]
    [InlineData("        let a = [[missing: value]]\n        _ = a@move\n", MissingAndExclusive, "UnresolvedBinding_Kd missing")]
    [InlineData("        let a = [missing, value]\n        _ = a@move\n", MissingAndExclusive, "UnresolvedBinding_Kd missing")]
    public void AFailedPartOfAnAggregateLiteralExplainsNoSiblingOnEitherTwin(string use, string letRecords, string varRecords)
    {
        foreach (var (head, main) in new[] { (PairCall + PairHead, PairMain), (UniqHead, string.Empty) })
        {
            var source = head + "    var h = func [value] (c: bool) -> ()\n" + use + "    h(true)\n" + main;
            Assert.Equal((letRecords, varRecords), (Records(source.Replace("    var h = func", "    let h = func", StringComparison.Ordinal)), Records(source)));
        }
    }

    // SPEC 3.5 (2c-2 re-check R2, R3): an enum Case's payload is a listed input, so `.Some(value)` of a captured pair layer Reborrows in
    // the uniq case: the capture is marked and a `let` Closure's call is Exclusive (SPEC 7.7.3), which its `let` slot cannot lend
    // (InvalidAssignment_Kd at `h`, as for `take(value)`). The payload is acquired by case (SPEC 6.5.2, 8.14; DECISIONS BU-45), as outside a
    // Closure, so it has no TransferRequired_Kd; Binding's records. G254a (PLAN G254; Q17 class 1; SPEC 8.1.1, 8.1.2, 15.6.3): that
    // Reborrow is contained in the call receiver and fitted to `Option<s/T>`, the binder's `o`, so it is UnprovenOriginRelation_Kd at
    // `value` (its valid twin: a Reborrow kept in the call and a store outside a Closure, ClosureEnvironmentOriginTest's loc and nst rows).
    [Theory]
    [InlineData("let", "UnprovenOriginRelation_Kd value; InvalidAssignment_Kd h")]
    [InlineData("var", "UnprovenOriginRelation_Kd value")]
    public void AnEnumPayloadOfAPairLayerCaptureIsAListedInput(string binding, string records)
    {
        var source = PairHead + "    " + binding + " h = func [value] () -> ()\n        let o: Option<s/T> = .Some(value)\n        _ = o@move\n    h()\n" + PairMain;
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", source));
        c.Bind();
        Assert.Equal(records, string.Join("; ", CompilationTestHelper.BindingErrors(c).Select(x => $"{x.Code} {source.Substring(x.Span.Start, x.Span.Length)}")));
        var closure = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), static x => x.IsAnonymous).ClosureOf()!;
        Assert.Equal((SemanticsKind.Ref, SemanticsKind.Uniq, "value"), (closure.CaseFreeReceiver, closure.Receiver, string.Join(",", closure.Captures.Where(x => x.BareReborrows).Select(x => x.Environment.Name))));
    }

    // DECISIONS R4-M126 (N4 (a)): each binding of a Closure classifies its receiver once its body is bound, also when the Closure then
    // fails, as an inferred result that borrows the call's own storage does; a rebinding classifies it again with the same facts.
    [Theory]
    [InlineData("var n = 1\nvar h = func [var n] () -> ()\n    n += 1\nh()", true)]
    [InlineData("var n = 1\nvar h = func [var n] (x: i32)\n    n += 1\n    return x@ref", false)]
    public void EveryBindingThatBoundTheBodyIsClassified(string source, bool complete)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Equal(complete, c.Binding.Result.IsComplete);
        var function = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.IsAnonymous);
        for (var pass = 0; pass < 2; pass++)
        {
            var closure = BoundClosure.Of(function)!;
            Assert.Equal((true, SemanticsKind.Uniq, SemanticsKind.Uniq, "n"), (closure.Classified, closure.CaseFreeReceiver, closure.Receiver, closure.ExclusiveUse?.ToString()));
            Assert.Equal(complete, c.Bind().IsComplete);
        }
    }

    // Warm rebinding classifies every Closure again from reused storage: the receiver facts allocate nothing and stay the same.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmClassificationAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze(PairHead + "    let text = \"abc\"\n    var k: i32 = 0\n    var h = func [value, var k, text@move] () -> ()\n" +
            "        var g = func [value] () -> ()\n            let local = value\n            _ = local\n        g()\n        k += 1\n        let t = text@move\n" +
            "    var e = func [value] () -> ()\n        let local = value\n        _ = local\n    e()\n" + PairMain);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(valid, MinimalEmissionTest.Describe(c));
        var closures = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Where(x => x.IsAnonymous).Select(x => x.ClosureOf()!).ToArray();
        Assert.Equal(
            ["Owner Owner value", "Ref Uniq value", "Ref Uniq value"],
            closures.Select(x => $"{x.CaseFreeReceiver} {x.Receiver} {string.Join(",", x.Captures.Where(y => y.BareReborrows).Select(y => y.Environment.Name))}"));
        Assert.All(closures, x => Assert.True(x.Classified));
    }

    // R7-Y2c: Derive's passes read every definition, and the passes are bounded by the locals. A chain of N body locals in source order,
    // each derived from the one before (a valid `var` Closure, SPEC 5.2.1 condition 3, 7.7.3), converges in two passes: work linear in N.
    // A chain assigned in reverse order (N locals from their own allocations, then `q{N} = q{N-1}` ... `q1 = p`, only read: valid, Shared)
    // takes one pass per local, Derive's worst case: work within the bound, quadratic in N (design review 1's N1; a worklist over the
    // definitions by root would make it linear).
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClassificationWorkOfAChainOfDerivedLocalsStaysWithinItsBound(bool reverse)
    {
        var work = new[] { 200, 400, 800 }.Select(n => Work(reverse, n)).ToArray();
        Assert.True(work[0] < work[1] && MirBorrowsTest.Grows(work, reverse ? 4 : 2), $"work {string.Join(", ", work)}");

        static long Work(bool reverse, int count)
        {
            var chain = reverse
                ? string.Concat(Enumerable.Range(1, count).Select(static i => $"            var q{i} = Raw.allocate<Probe>(1)\n")) +
                    string.Concat(Enumerable.Range(2, count - 1).Reverse().Select(static i => $"            q{i} = q{i - 1}\n")) + "            q1 = p\n"
                : "            let q0 = p\n" + string.Concat(Enumerable.Range(1, count).Select(static i => $"            let q{i} = q{i - 1} + 0\n"));
            var tail = reverse ? $"            return q{count}[0].v\n    let v = f()\n" : $"            q{count}[0] = Probe.init(2)\n    f()\n";
            var c = MinimalEmissionTest.Analyze(RawProbe + $"    var f = func [p] (){(reverse ? " -> i32" : string.Empty)}\n        unsafe\n" + chain + tail);
            Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
            return c.Binding.ClosureWork;
        }
    }

    // R7-Y2a (review 1's N1): ClosureEffects records a consumer's takes once per Classify and selection, so its work grows linearly in one
    // consumer's capture uses, a literal's elements or a call's arguments (valid programs: reads of a Copy capture, SPEC 7.7.3 Shared), not
    // as every use times every child.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ClassificationWorkGrowsLinearlyInAConsumersCaptureUses(bool literal)
    {
        var work = new[] { 200, 400, 800 }.Select(n => Work(literal, n)).ToArray();
        Assert.True(work[0] < work[1] && MirBorrowsTest.Grows(work, 2), $"work {string.Join(", ", work)}");

        static long Work(bool literal, int count)
        {
            var uses = string.Join(", ", Enumerable.Repeat("c", count));
            var source = literal
                ? "func g() -> isize\n    let c: i32 = 1\n    let f = func [c] () -> isize\n        let xs: Array<i32> = [" + uses + "]\n        return xs.length\n    return f()\n"
                : "func many(" + string.Join(", ", Enumerable.Range(0, count).Select(static i => $"a{i}: i32")) + ") -> i32 => 0\nfunc g() -> i32\n" +
                    "    let c: i32 = 1\n    let f = func [c] () -> i32 => many(" + uses + ")\n    return f()\n";
            var c = MinimalEmissionTest.Analyze(source + "()\n");
            Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
            return c.Binding.ClosureWork;
        }
    }

    // The records of a checked source: each one's code and the text at its span.
    private static string Records(string source) => string.Join("; ", DiagnosticCorpus.Check(source).Diagnostics.Select(x => $"{x.Code} {(x.Span is { } span ? source.Substring(span.Start, span.Length) : string.Empty)}"));

    // SPEC 7.7.3: the receiver of one admitted case, read from the published facts as a view of that case reads it.
    private static SemanticsKind CaseReceiver(Compilation c, BoundClosure closure, PairCase semantics)
    {
        if (closure.CaseFreeReceiver != SemanticsKind.Ref)
        {
            return closure.CaseFreeReceiver;
        }

        ReadOnlySpan<PairCase> cases = [semantics];
        foreach (var capture in closure.Captures)
        {
            if (capture.BareReborrows && c.Binding.CaseType(capture.Environment.Type!, cases) is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq or SemanticsKind.ObjUniq })
            {
                return SemanticsKind.Uniq;
            }
        }

        return SemanticsKind.Ref;
    }

    // The one Closure's call receiver, and its `let f` call: an `InvalidAssignment_Kd` with the general Note when Exclusive, after the
    // body's own records `letBefore` (G254a's class 1 rows), valid otherwise; the same program with `var f` has `varRecords` (none but an
    // (e) row's and those records).
    private static void AssertCallReceiver(string source, bool exclusive, string varRecords = "", string letBefore = "", string note = ExclusiveCallNote)
    {
        var c = MinimalEmissionTest.Analyze(source);
        var closure = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), static x => x.IsAnonymous && x.Parent is VariableKoto { NameKoto: var name } && name.ToString() == "f");
        Assert.Equal(exclusive ? SemanticsKind.Uniq : SemanticsKind.Ref, closure.ClosureOf()!.Receiver);
        Assert.Equal(varRecords, Records(source.Replace("    let f = func", "    var f = func", StringComparison.Ordinal)));
        var errors = DiagnosticCorpus.Check(source).Diagnostics;
        if (!exclusive)
        {
            Assert.Empty(errors);
            return;
        }

        Assert.Equal(letBefore + "InvalidAssignment_Kd f", Records(source));
        var error = Assert.Single(errors, static x => x.Code == nameof(DiagnosticCode.InvalidAssignment_Kd));
        Assert.Equal((nameof(DiagnosticCode.InvalidAssignment_Kd), "f"), (error.Code, error.Span is { } span ? source.Substring(span.Start, span.Length) : string.Empty));
        Assert.Equal(note, error.Note);
    }

    [Collection<AllocationIsolationCollection>]
    [Trait("Purpose", "Allocation")]
    public class AllocationTests(ITestOutputHelper output)
    {
        // G622: ClosureEffects' store is sized by the nodes a Classify meets, never by the module. A cold Bind's bytes of a program with a
        // capturing Closure, less those of its twin that classifies no capture use, are what the classification pays (valid programs:
        // plain functions, SPEC 7.1; a Copy capture or a local read, SPEC 7.7.2, 7.7.3 Shared). Their growth from the small size to the
        // large one is what the added units cost:
        // - A function beside the Closure adds nothing (one capture use, 500 and 8,000 plain functions; the twin has no Closure, since
        //   any Closure's Classify would pay a store by the module): 0 B, bounded by 4 B a function either way. A store by the module's
        //   syntax ids costs eight ids a function (1 B an id: 60,000 B).
        // - A capture use of one consumer adds its node's row, 32 B and a 4-byte bucket, which the table's doubling keeps under 96 B a use
        //   (4,000 and 8,000 elements of one literal, 303,136 B, 76 B a use; the twin's `c` is the Closure's body local).
        // The series opens with the large capturing program, unread: a process's first Bind of a size also pays what it keeps across
        // compilations. The sizes give the bounds room for the one reading a series cannot hold still: the kept compilations grow the
        // heap into a background collection, and the Bind its pause falls in is charged up to one allocation context's tail (8 KB) more
        // or less. Without the concurrent collector every reading is the same in every run.
        [Theory]
        [InlineData(false, 500, 8000, -4, 4)]
        [InlineData(true, 4000, 8000, 36, 96)]
        public void AColdClassificationPaysByTheNodesItMeets(bool uses, int small, int large, int least, int most)
        {
            var compilations = new Compilation[5];
            var bytes = AllocationMeasurement.MeasureSeries(compilations.Length, i =>
            {
                var c = compilations[i] = CompilationTestHelper.Parse(Source(uses, capture: (i & 1) == 0, i is 1 or 2 ? small : large), "Hello.kimi");
                return () => c.Bind();
            });
            Assert.All(compilations, static x => Assert.True(x.Binding.Result.IsComplete, MinimalEmissionTest.Describe(x)));
            var growth = (bytes[4] - bytes[3]) - (bytes[2] - bytes[1]);
            var units = large - small;
            output.WriteLine($"cold Bind bytes {string.Join(", ", bytes)}; the store's growth {growth} B over {units} units");
            Assert.True(growth >= (least * units) - 64 && growth <= (most * units) + 64, $"cold Bind bytes {string.Join(", ", bytes)}; the store's growth {growth} B over {units} units");

            static string Source(bool uses, bool capture, int size)
            {
                if (uses)
                {
                    var (outer, head, inner) = capture ? ("    let c: i32 = 1\n", "[c] ", string.Empty) : (string.Empty, string.Empty, "        let c: i32 = 1\n");
                    return $"func g() -> isize\n{outer}    let f = func {head}() -> isize\n{inner}        let xs: Array<i32> = [{string.Join(", ", Enumerable.Repeat("c", size))}]\n        return xs.length\n    return f()\n()\n";
                }

                return string.Concat(Enumerable.Range(0, size).Select(static k => $"func f{k}(a: i32) -> i32 => a + {k} * 2\n")) +
                    (capture ? "func g() -> i32\n    let c: i32 = 1\n    let f = func [c] () -> i32\n        return c\n    return f()\n()\n" : "func g() -> i32\n    let c: i32 = 1\n    return c\n()\n");
            }
        }
    }
}
