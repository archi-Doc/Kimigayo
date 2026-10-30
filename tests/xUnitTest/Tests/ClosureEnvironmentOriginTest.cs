// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Verification;
using Xunit;

namespace XunitTest;

// SPEC 7.7.2, 15.8.2: an environment binding is a Place of its closure, so a closure body checks the Loans on its environment as an
// ordinary body checks its locals, and a borrow of an environment binding's own slot depends on that binding.
public class ClosureEnvironmentOriginTest
{
    private const string Main = "public func main() -> ()\n";

    // BU's B3 family (PLAN G227): a Closure stores its own borrowed argument into its `[var arr@move]` capture and reads it in a later call.
    private const string StoreHead = "func leak() -> i32\n    ";

    private const string StoreTail = "\n    var f = func [var arr@move] (x: ref/i32) -> i32\n        if arr.length > 0\n            let old: i32 = arr[0]@follow\n" +
        "            return old\n        arr.append(x)\n        return 0\n    var first: i32 = 0\n    if true\n        let inner: i32 = 41\n" +
        "        first = f@uniq(inner@ref)\n    let other: i32 = 5\n    return first + f@uniq(other@ref)\npublic func main() => ()\n";

    private const string VarStore = StoreHead + "var arr: Array<ref/i32> = []" + StoreTail;

    private const string Generic = "func leak<T>(t: T) -> i32\n    var arr: Array<ref/i32> = []\n    var f = func [var arr@move] (x: ref/i32) -> i32\n" +
        "        arr.append(x)\n        return 0\n    let a: i32 = 1\n    return f@uniq(a@ref)\npublic func main()\n    let r = leak(1)\n";

    private const string Nested = "func leak() -> i32\n    var arr: Array<ref/i32> = []\n    var f = func [var arr@move] (x: ref/i32) -> i32\n" +
        "        var g = func [arr@uniq] (y: ref/i32) -> i32\n            arr@follow.append(y)\n            return 0\n        return g@uniq(x)\n" +
        "    let local: i32 = 1\n    return f@uniq(local@ref)\npublic func main() => ()\n";

    private const string RawStore = "func leak(x: ref/i32) -> i32\n    var arr: Array<ref/i32> = []\n    let q = arr[0]@follow@raw\n" +
        "    var f = func [var q@move] (y: ref/i32) -> i32\n        q = y@follow@raw\n        return 0\n    return f@uniq(x)\npublic func main() => ()\n";

    private const string LocalStore = "func leak() -> i32\n    var arr: Array<ref/i32> = []\n    var f = func [var arr@move] () -> i32\n" +
        "        let local: i32 = 1\n        arr.append(local@ref)\n        return 0\n    let first = f@uniq()\n    return first + f@uniq()\n" +
        "public func main() => ()\n";

    private const string CopyStore = "func leak() -> i32\n    let o: Option<ref/i32> = .None\n    var f = func [var o] (n: i32) -> i32\n" +
        "        let local: i32 = n\n        o = .Some(local@ref)\n        return 0\n    let first = f@uniq(41)\n    return first + f@uniq(7)\n" +
        "public func main() => ()\n";

    private const string MovedStore = "func leak() -> i32\n    var o: Option<Array<ref/i32>> = .None\n    var f = func [var o@move] () -> i32\n" +
        "        let local: i32 = 1\n        var inner: Array<ref/i32> = []\n        inner.append(local@ref)\n        o = .Some(inner@move)\n" +
        "        return 0\n    return f@uniq()\npublic func main() => ()\n";

    private const string TwoRegions = "func g(p: ref/i32) -> isize\n    var a1: Array<ref/i32> = []\n    a1.append(p)\n    var a2: Array<ref/i32> = []\n" +
        "    var f = func [var a1@move, var a2@move] () -> isize\n        a2.append(a1[0])\n        return a2.length\n    return f@uniq()\n" +
        "public func main() => ()\n";

    private const string InputDuringP = "func g(p: ref/i32) -> isize\n    var arr: Array<ref/i32> = []\n    arr.append(p)\n" +
        "    var f = func [var arr@move] (x: ref/i32 during p) -> isize\n        arr.append(x)\n        return arr.length\n    return f@uniq(p)\n" +
        "public func main() => ()\n";

    private const string CapturedParameter = "func g(p: ref/i32) -> isize\n    var arr: Array<ref/i32> = []\n    var f = func [var arr@move, p] () -> isize\n" +
        "        arr.append(p)\n        return arr.length\n    return f@uniq()\npublic func main() => ()\n";

    private const string CopiedOption = "func ok(p: ref/i32) -> i32\n    let o: Option<ref/i32> = .None\n    var f = func [var o, p] (n: i32) -> i32\n" +
        "        o = .Some(p)\n        return n\n    return f@uniq(1)\npublic func main() => ()\n";

    private const string EntryBorrow = "func ok() -> isize\n    let outer: i32 = 3\n    let arr: Array<ref/i32> = []\n" +
        "    var f = func [var arr@move, outer@ref] () -> isize\n        arr.append(outer)\n        return arr.length\n    return f@uniq()\n" +
        "public func main() => ()\n";

    private const string CapturedBorrow = "func ok() -> isize\n    let a: i32 = 1\n    var arr: Array<ref/i32> = []\n    let r = a@ref\n" +
        "    var f = func [var arr@move, r] () -> isize\n        arr.append(r)\n        return arr.length\n    return f@uniq()\npublic func main() => ()\n";

    private const string StaticThroughLocal = "func leak() -> i32\n    var arr: Array<ref/i32> = []\n    let f = func [arr@move] () -> ref/i32\n" +
        "        var tmp: Array<ref/i32> = []\n        tmp.append(arr[0])\n        return tmp[0]\n    return f()@follow\npublic func main() => ()\n";

    private const string StaticDirect = "func leak() -> i32\n    var arr: Array<ref/i32> = []\n    let f = func [arr@move] () -> ref/i32 => arr[0]\n" +
        "    return f()@follow\npublic func main() => ()\n";

    private const string BodyError = "func ok() -> isize\n    let arr: Array<ref/i32> = []\n    var f = func [var arr@move] () -> isize\n" +
        "        let wrong: bool = 5\n        return arr.length\n    return f@uniq()\npublic func main() => ()\n";

    // A local clause in a Closure body relating the local's slot to a capture's Origin (the final review's k06, k07, k09).
    private const string Holder = "struct H {s}\n    public let item: ref/i32 during s\n    public init(item: ref/i32 during s) => self.item = item\n";

    private const string ClauseOverParameter = Holder + "func g(p: ref/i32) -> i32\n    let a: i32 = 1\n    let r = a@ref\n    let f = func [r, p] () -> i32\n" +
        "        let h: H{x} = H.init(r)\n            origin x.s outlives p\n        return h.item@follow\n    return f()\npublic func main() => ()\n";

    private const string ClauseOverCapture = Holder + "func g(p: ref/i32) -> i32\n    let a: i32 = 1\n    let b: i32 = 2\n    let r = a@ref\n    let q = b@ref\n" +
        "    let f = func [r, q] () -> i32\n        let h: H{x} = H.init(r)\n            origin x.s outlives q\n        return h.item@follow\n    return f()\n" +
        "public func main() => ()\n";

    private const string ClauseOverCaptureFromInput = Holder + "func g(p: ref/i32) -> i32\n    let b: i32 = 2\n    let q = b@ref\n" +
        "    let f = func [q] (y: ref/i32) -> i32\n        let h: H{x} = H.init(y)\n            origin x.s outlives q\n        return h.item@follow\n" +
        "    let a: i32 = 1\n    return f(a@ref)\npublic func main() => ()\n";

    // PLAN G230's d6 family (G227's design B): `leak` and a `var w` whose Type names the set `x`; the nested functions name `x.s` or `w.s`.
    private const string WStruct = "struct W {s}\n    public var items: Array<ref/i32 during s>\n" +
        "    public init(items: Array<ref/i32 during s>) => self.items = items@move\n";

    private const string Leak = "func leak(a: ref/i32) -> i32\n    var arr: Array<ref/i32> = []\n    arr.append(a)\n";

    private const string NestedHead = WStruct + Leak + "    var w: W{x} = W.init(items: arr@move)\n";

    // d6's `put` stores its own input into `x.s`; d6c's input is `during x.s` too.
    private const string PutSignatureStore = "(slot: uniq/Array<ref/i32 during x.s>, v: ref/i32) => slot@follow.append(v)\n";

    private const string PutSignatureBoth = "(slot: uniq/Array<ref/i32 during x.s>, v: ref/i32 during x.s) => slot@follow.append(v)\n";

    private const string PutStore = "    func put" + PutSignatureStore;

    private const string PutBoth = "    func put" + PutSignatureBoth;

    // d6's store of a block's `inner` and the later read of it.
    private const string NestedStore = "    if true\n        let inner: i32 = 41\n        put(w.items@uniq, inner@ref)\n    let items = w.items@move\n" +
        "    let old: i32 = items[1]@follow\n    return old\npublic func main() => ()\n";

    private const string NestedValid = "    put(w.items@uniq, a)\n    let items = w.items@move\n    let old: i32 = items[1]@follow\n    return old\n" +
        "public func main() => ()\n";

    private const string OtherStore = "    var other: Array<ref/i32> = []\n    put(other@uniq, a)\n    return 0\npublic func main() => ()\n";

    // G6's two forms of a function whose Closure stores through a captured `q` into an Origin of the enclosing body (2c-D design B's g6).
    private const string G6Single = "func use(p: uniq/i32, q: uniq/(uniq/i32 during p)) -> i32\n";

    private const string G6Array = "func use(p: uniq/Array<i32>, q: uniq/(uniq/i32 during p)) -> i32\n";

    // A local exclusive reference a Closure captures bare (2x-P7's t4 and n2 forms).
    private const string LocalView = "func use() -> i32\n    var x: i32 = 1\n    let u = x@uniq\n";

    private const string Consume = "func consumeText(value: string) => ()\n";

    private const string Inspect = "func inspect(value: ref/i32) => ()\n";

    // An exclusive input whose clause is its own well-formedness, so a call judges `a outlives b` at its principal solution.
    private const string ExclusiveClause = "func fu(x: uniq/(ref/i32 during a) during b)\n    origin a outlives b\n    _ = x\n";

    private const string TwoCaptures = "func use(p: ref/i32) -> i32\n    let a = p\n    let b = p\n";

    private const string IndependentCaptures = "func use(p: ref/i32, q: ref/i32) -> i32\n    let a = p\n    let b = q\n";

    private const string CallF = "    f()\n    return 0\n";

    private const string Resource = "struct Resource\n    public var n: i32\n    public init(n: i32) => self.n = n\nfunc makeResource() -> Resource => Resource.init(1)\n" +
        "func consume(r: Resource) => ()\n";

    // g6a (2c-D design B's G6 probe): the first call stores `p[0]@uniq` into `*q`, the second appends to `p` and writes through `*q`.
    private const string G6a = G6Array + "    var first = true\n    var c = func [p@move, q@move, var first] () -> i32\n        if first\n" +
        "            q@follow = p[0]@uniq\n            first = false\n            return 0\n        p.append(7)\n        q@follow@follow = 5\n        return 1\n" +
        "    _ = c()\n    _ = c()\n    return 0\n";

    // g6c, G6's Consuming control: the same store under `owner/Self`, valid by SPEC.
    private const string G6c = Consume + G6Single + "    let t = \"a\"\n    var c = func [p@move, q@move, t@move] () -> i32\n        consumeText(t@move)\n" +
        "        q@follow = p\n        return 0\n    return c@move()\n";

    // D-13's third form under an Exclusive receiver: an `Option` of a reference stored through an exclusive Borrow of a `var` binding.
    private const string OptionStore = "func use(p: ref/i32) -> i32\n    let a: Option<ref/i32 during p> = .Some(p)\n    let r = p\n" +
        "    var c = func [var a, r] () -> ()\n        let q = a@uniq\n        q@follow = .Some(r)\n    c()\n    return 0\n";

    // G6's store in a Closure whose inferred result fails (its returns have no common Type), and a head whose pair-layer capture a bare
    // use marks (BoundCapture.BareReborrows).
    private const string FailedReturns = "        q@follow = p\n        if k\n            return 1\n        return \"a\"\n";

    private const string FailedResult = "    var c = func [p@move, q@move] (k: bool)\n" + FailedReturns;

    private const string MarkedHead = "func use<s/T>(value: s/T, p: uniq/i32, q: uniq/(uniq/i32 during p)) -> ()\n    s is owner or uniq\n    T is Copy\n";

    // G6's store in a closure argument bound under its call's consultation (the slot T is fixed before the body binds).
    private const string ConsultedStore = "func run<T, F>(value: T, action: F) -> T\n    F is Callable<uniq, (T) -> ()>\n    return value\n" + G6Single +
        "    let n = run(1, func [p@move, q@move] (y)\n        q@follow = p\n        let k = y\n    )\n    return n\n";

    private const string Unproven = nameof(DiagnosticCode.UnprovenOriginRelation_Kd);

    private const string InvalidOrigin = nameof(DiagnosticCode.InvalidOriginBinding_Kd);

    // The forms whose records name the call receiver ρ or a Borrow resolved to it (ARecordNamingTheCallReceiverShowsTheClosure).
    private const string EnvBorrow = "func use(p: ref/i32) -> i32\n    let n: i32 = 1\n";
    private const string LocalAnnotated = EnvBorrow + "    let c = func [n] () -> i32\n        let r: ref/i32 during p = n@ref\n        return 0\n    return 0\n";
    private const string G6bStore = G6Single + "    var c = func [p@move, q@move] () -> i32\n        q@follow = p\n        return 0\n    return 0\n";
    private const string FailedClauseStore = G6Single + "    origin p outlives zz\n    var c = func [p@move, q@move] () -> i32\n        q@follow = p\n        return 0\n    return 0\n";
    private const string LocalReturned = EnvBorrow + "    let c = func [n] () -> ref/i32 during p\n        let r = n@ref\n        return r\n    return 0\n";

    // BU's re-check R1: a Borrow of an environment binding reaching the record through a call, a Tuple or a stored local.
    private const string CallReturned = "func pass(x: ref/i32) -> ref/i32 => x\n" + EnvBorrow +
        "    let c = func [n] () -> ref/i32 during p => pass(n@ref)\n    return 0\n";

    private const string TupleReturned = EnvBorrow + "    let c = func [n] () -> ref/i32 during p\n        let t = (n@ref, 1)\n" +
        "        return t.0\n    return 0\n";

    private const string LocalStored = "func use(p: uniq/i32, q: uniq/(ref/i32 during p)) -> i32\n    let n: i32 = 1\n" +
        "    var c = func [q@move, n] () -> i32\n        let r = n@ref\n        q@follow = r\n        return 0\n    return 0\n";

    private const string LocalAnnotatedConsuming = Consume + EnvBorrow + "    let t = \"a\"\n    let c = func [n, t@move] () -> i32\n        consumeText(t@move)\n" +
        "        let r: ref/i32 during p = n@ref\n        return 0\n    return 0\n";

    private const string SpecInvalid = "func makeText() -> string => \"a\"\nfunc use() -> i32\n    let other = makeText()\n" +
        "    let invalid = func [other@move] () -> ref/string => other@ref\n    return 0\n";

    private const string N2bStore = LocalView + "    var outer = func [u] () -> i32\n        let inner = func [u] () -> uniq/i32 during u => u@move\n        return 0\n" +
        "    return 0\n";

    private const string Unsatisfied = nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd);

    // BU's re-check 2 (B1-B3, g3): two environment Borrows, an implicit Borrow argument, composite destinations and local regions.
    private const string Second = "func second(x: ref/i32, y: ref/i32) -> ref/i32 during y => y\n";

    private const string TwoEnv = Second + EnvBorrow + "    let m: i32 = 2\n";

    private const string Pass = "func pass(x: ref/i32) -> ref/i32 => x\n";

    private const string EnvStore = "func use(p: uniq/i32, q: uniq/(ref/i32 during p)) -> i32\n    let n: i32 = 1\n";

    private const string G6SecondStore = "func second(x: ref/i32, y: uniq/i32) -> uniq/i32 during y => y\n" + G6Single + "    let n: i32 = 1\n" +
        "    var c = func [p@move, q@move, n] () -> i32\n        q@follow = second(n@ref, p)\n        return 0\n    return 0\n";

    private const string OptionStoreInto = "func use(p: uniq/i32, q: uniq/Option<ref/i32 during p>) -> i32\n    let n: i32 = 1\n" +
        "    var c = func [q@move, n] () -> i32\n        let o: Option<ref/i32> = .Some(n@ref)\n        q@follow = o\n        return 0\n    return 0\n";

    private const string RegionStore = "func use(p: uniq/i32, q: uniq/(ref/i32 during p), s: ref/i32 during p) -> i32\n    let n: i32 = 1\n" +
        "    var c = func [q@move, n, s] () -> i32\n        var r = s\n        r = n@ref\n        q@follow = r\n        return 0\n    return 0\n";

    private const string ClauseHolder = "struct H {s}\n    public let item: ref/i32 during s\n    public init(item: ref/i32 during s) => self.item = item\n";

    private const string ClauseHolder2 = "struct H2 {s, t}\n    public let a: ref/i32 during s\n    public let b: ref/i32 during t\n" +
        "    public init(a: ref/i32 during s, b: ref/i32 during t)\n        self.a = a\n        self.b = b\n";

    private const string ClauseOverEnv = ClauseHolder + EnvBorrow + "    let c = func [n] () -> i32\n        let h: H{x} = H.init(n@ref)\n" +
        "            origin x.s outlives p\n        return h.item@follow\n    return c()\n";

    private const string ClauseOverMovedLocal = ClauseHolder + EnvBorrow + "    let c = func [n] () -> i32\n        let h0 = H.init(n@ref)\n" +
        "        let h: H{x} = h0@move\n            origin x.s outlives p\n        return h.item@follow\n    return c()\n";

    private const string ClauseOverSecondEnv = ClauseHolder2 + EnvBorrow + "    let m: i32 = 2\n    let c = func [n, m] () -> i32\n" +
        "        let h: H2{x} = H2.init(n@ref, m@ref)\n            origin x.t outlives p\n        return h.a@follow\n    return c()\n";

    private const string ClauseOverPlain = ClauseHolder + EnvBorrow + "    let h: H{x} = H.init(n@ref)\n        origin x.s outlives p\n" +
        "    return h.item@follow\n";

    private const string ClauseOverSecondPlain = ClauseHolder2 + EnvBorrow + "    let m: i32 = 2\n    let h: H2{x} = H2.init(n@ref, m@ref)\n" +
        "        origin x.t outlives p\n    return h.a@follow\n";

    private const string ClauseOverEnvConsuming = Consume + ClauseHolder + EnvBorrow + "    let t = \"a\"\n    let c = func [n, t@move] () -> i32\n" +
        "        consumeText(t@move)\n        let h: H{x} = H.init(n@ref)\n            origin x.s outlives p\n        return h.item@follow\n    return 0\n";

    private const string FieldHolder = "struct S\n    public var f: i32\n    public init(f: i32) => self.f = f\n" + "func pass(x: ref/i32) -> ref/i32 => x\n";

    private const string ImplicitFieldPlain = FieldHolder + "func use(p: ref/i32) -> ref/i32 during p\n    let s = S.init(1)\n    return pass(s.f)\n";

    private const string ImplicitFieldEnv = FieldHolder + "func use(p: ref/i32) -> i32\n    let s = S.init(1)\n" +
        "    let c = func [s@move] () -> ref/i32 during p => pass(s.f)\n    return 0\n";

    private const string RegionMeet = "func use(p: ref/i32, b: bool) -> i32\n    let n: i32 = 1\n    let c = func [n, p, b] () -> ref/i32 during p\n" +
        "        var r = if b => n@ref else => p\n        return r\n    return 0\n";

    // S1 (PLAN G65): these bodies were accepted, and the first aborted at run time with the write observed through `r`.
    [Theory]
    [InlineData("    let n = 7\n    var f = func [var n] () -> i32\n        let r = n@ref\n        n = 1\n        return r@follow\n    let v = f()\n", "n = 1")]
    [InlineData("    let n = 7\n    var f = func [var n] () -> i32\n        let r = id(n@ref)\n        n = 1\n        return r@follow\n    let v = f()\n", "n = 1")]
    [InlineData("    var n = 7\n    let view = n@uniq\n    var f = func [view] () -> i32\n        let c = view\n        view@follow = 1\n        c@follow = 2\n        return view@follow\n    let v = f()\n", "view")]
    [InlineData("    var n = 7\n    let view = n@uniq\n    var f = func [view] () -> i32\n        let c = view@follow@uniq\n        view@follow = 1\n        c@follow = 2\n        return 0\n    let v = f()\n", "view")]
    [InlineData("    var n = 7\n    var f = func [n@uniq] () -> i32\n        let q = n@follow@ref\n        let s = q@follow\n        n@follow = 3\n        return s * 10 + q@follow\n    let v = f()\n", "n")]
    [InlineData("    var n = 7\n    let view = n@uniq\n    var outer = func [view] () -> i32\n        var g = func [view] () => view@follow += 1\n        view@follow = 100\n        g()\n        return view@follow\n    let v = outer()\n", "view")]
    [InlineData("    var a: Array<i32> = [1, 2]\n    let view = a@uniq\n    var f = func [view] () -> i32\n        let r = view[0]@ref\n        view.append(3)\n        return r@follow\n    let v = f()\n", "view.append(3)", nameof(DiagnosticCode.CallActivationConflict_Kd))]
    [InlineData("    var n = 7\n    let view = n@uniq\n    var f = func [view] () -> i32\n        let d = keep(view)\n        bump(view)\n        d@follow += 4\n        return view@follow\n    let v = f()\n", "bump(view)", nameof(DiagnosticCode.CallActivationConflict_Kd))]
    public void EnvironmentLoansAreCheckedInTheBody(string body, string text, string code = nameof(DiagnosticCode.ComparisonLoanConflict_Kd))
    {
        var source = "func id(x: ref/i32) -> ref/i32 during x => x\nfunc keep(x: uniq/i32) -> uniq/i32 during x => x\nfunc bump(x: uniq/i32) => x@follow += 50\n" + Main + body;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((code, text), (error.Code, error.Span is { } span ? source.Substring(span.Start, span.Length) : string.Empty));
    }

    private const string Bindings = "    let n = 7\n    let t = (1, 2)\n    var f = func [var n, var t] () -> i32\n";

    private const string View = "    var a: Array<i32> = [1, 2]\n    let view = a@uniq\n    var f = func [view] () -> isize\n";

    private const string OptionView = "    var o: Option<i32> = .Some(1)\n    let view = o@uniq\n    var f = func [view] () -> i32\n";

    // SPEC 7.7.3 (PLAN G147 (3), ledger row 13972.1438): an exclusive Borrow of a `var` environment binding or of a part of it, or an
    // Exclusive-mode `for` or `match` Subject that is a captured exclusive reference (SPEC 14.6.2, 15.1.6), makes the call Exclusive,
    // so the body is checked against `uniq/Self` by both the old analysis and the MIR's: using the binding while the child lives is a
    // Loan conflict, and the twin that uses it before or after the child is valid. The MIR accepted the conflicts, its body borrowing
    // the binding exclusively through a shared receiver.
    [Theory]
    [InlineData(Bindings + "        let r = n@uniq\n        let q = n\n        r@follow = 3\n        return q\n", "n\n        r@follow")]
    [InlineData(Bindings + "        let r = n@uniq\n        r@follow = 3\n        let q = n\n        return q\n", null)]
    [InlineData(Bindings + "        let r = (n)@uniq\n        let q = n\n        r@follow = 3\n        return q\n", "n\n        r@follow")]
    [InlineData(Bindings + "        let r = t.0@uniq\n        let q = t.0\n        r@follow = 3\n        return q\n", "t.0\n        r@follow")]
    [InlineData(View + "        var n: isize = 0\n        for x in view\n            n = view@follow.length\n            x@follow += 1\n        return n\n", "view@follow.length")]
    [InlineData(View + "        let n = view@follow.length\n        for x in view\n            x@follow += 1\n        return n\n", null)]
    [InlineData(OptionView + "        match view\n            .Some(let w) => w@follow = peek(view@follow@ref)\n            .None => return 1\n        return 0\n", "view@follow@ref")]
    [InlineData(OptionView + "        let k = peek(view@follow@ref)\n        match view\n            .Some(let w) => w@follow = k\n            .None => return 1\n        return 0\n", null)]
    public void AnExclusiveUseOfAnEnvironmentBindingIsCheckedAgainstTheExclusiveReceiver(string body, string? at)
    {
        var source = "func peek(o: ref/Option<i32>) -> i32 => 1\n" + Main + body + "    let v = f()\n";
        var c = VerifiedSpecExampleTest.Bind(source);
        c.Analyze();
        var (common, old, passed, mir) = MirDifferenceLedger.Sides(c, c.BuildMir());
        foreach (var side in new List<MirDifferenceLedger.Entry>[] { [.. common, .. old, .. passed], [.. common, .. mir] })
        {
            var errors = side.Where(static x => DiagnosticEntries.TryGet(x.Code, out var entry) && entry.Severity == DiagnosticSeverity.Error).ToArray();
            if (at is null)
            {
                Assert.Empty(errors);
                continue;
            }

            var error = Assert.Single(errors);
            Assert.Equal(DiagnosticCode.ComparisonLoanConflict_Kd, error.Code);
            Assert.StartsWith(at, source[error.Node.Span.Start..], StringComparison.Ordinal);
        }
    }

    // Loans on environment bindings end at their last use and stay per Field, as those of locals do.
    [Theory]
    [InlineData("LastUse", "let n = 7\nvar f = func [var n] () -> i32\n    let r = n@ref\n    let v = r@follow\n    n = 1\n    return v + n\nrequire f() == 8 else => $abort(\"last use\")")]
    [InlineData("Fields", "struct P\n    public var a: i32\n    public var b: i32\n    public init(a: i32, b: i32)\n        self.a = a\n        self.b = b\nlet p = P.init(a: 1, b: 2)\nvar f = func [var p@move] () -> i32\n    let r = p.a@ref\n    p.b = 5\n    return r@follow + p.b\nrequire f() == 6 else => $abort(\"fields\")")]
    [InlineData("ExclusiveAfterChild", "var n = 7\nlet view = n@uniq\nvar f = func [view] () -> i32\n    let c = view@follow@uniq\n    c@follow = 2\n    view@follow += 1\n    return view@follow\nrequire f() == 3 else => $abort(\"exclusive\")")]
    [InlineData("String", "let message = \"env\"\nlet f = func [message@move] () => Console.writeLine(message)\nf()")]
    [InlineData("ElementBorrow", "var a: Array<i32> = [1, 2]\nlet view = a@uniq\nlet f = func [view] () -> i32\n    let r = view@follow[0]@ref\n    let s = view[1]@ref\n    return r@follow + s@follow\nrequire f() == 3 else => $abort(\"element\")")]
    public void EnvironmentBorrowsKeepLocalPrecision(string name, string source)
        => ScalarEmissionTest.EmitFixture("ClosureEnvironment" + name, source, name == "String" ? "env\n" : string.Empty);

    // S2: the result borrows the environment binding's own slot, so it depends on the closure's receiver and is not a fixed external
    // result; it was accepted and outlived the Move of the closure (SPEC 15.8.2). Binding binds it with the receiver's Origin (DECISIONS
    // R4-M152), and the old analysis's gate keeps R1's located Unsupported_Kd at the call until R6-check.
    [Fact]
    public void ASlotBorrowOfAnEnvironmentBindingIsReceiverDependent()
    {
        var source = Main + "    let n = 7\n    let f = func [n@ref] () => n@ref\n    let r = f()\n    let g = f@move\n    require r@follow@follow == 7 else => $abort(\"r\")\n";
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.Unsupported_Kd), "f()"), (error.Code, error.Span is { } span ? source.Substring(span.Start, span.Length) : string.Empty));
    }

    // SPEC 15.8.2, 15.6.1, 23.1: a result inferred from the body that borrows the call's own storage, a parameter, a body local or
    // an environment binding that a Consuming call consumes, is Refuted at the returned value (an expression body, or a `return`'s
    // operand), with the borrow as the longer end and the closure's call result as the shorter, related at the Borrow and at the header.
    // The Closure keeps its Type and its calls bind (DECISIONS R4-M152, R4-M126 P8; OQ-F7-2 (a)): an indented body's record was at the
    // Closure, which failed with a `Never` result, NotCallable_Kd or UnresolvedBinding_Kd at its calls and two MIR validator findings.
    [Theory]
    [InlineData("func keep(s: string, r: ref/string) -> ref/string during r => r\n" + Main + "    let a = \"x\"\n    let b = \"y\"\n    let take = func [a@move, b@move] () => keep(a@move, b@ref)\n", "keep(a@move, b@ref)", "b@ref")]
    [InlineData("func keep(s: string, r: ref/i32) -> ref/i32 during r => r\n" + Main + "    let a = \"x\"\n    let n = 7\n    let take = func [a@move, n] () => keep(a@move, n@ref)\n", "keep(a@move, n@ref)", "n@ref")]
    [InlineData(Main + "    let s = \"x\"\n    let n = 7\n    let take = func [s@move, n] () => (s@move, n@ref)\n", "(s@move, n@ref)", "n@ref")]
    [InlineData(Main + "    let f = func (x: i32) => x@ref\n    let r = f(1)\n", "x@ref", "x@ref")]
    [InlineData(Main + "    let f = func (x: i32)\n        return x@ref\n    let r = f(1)\n", "x@ref", "x@ref")]
    [InlineData(Inspect + Main + "    let c = func ()\n        let l: i32 = 1\n        return l@ref\n    inspect(c())\n", "l@ref", "l@ref")]
    [InlineData(
        Consume + "func inspectText(value: ref/string) => ()\n" + Main + "    let text = \"a\"\n    let other = \"b\"\n" +
            "    let c = func [text@move, other@move] ()\n        consumeText(other@move)\n        return text@ref\n    inspectText(c@move())\n",
        "text@ref",
        "text@ref")]
    public void AResultOverCallLocalStorageIsRefuted(string source, string text, string borrow)
    {
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), text), (error.Code, error.Span is { } span ? source.Substring(span.Start, span.Length) : string.Empty));
        var related = error.Related!.Select(x => x.Span is { } at ? source.Substring(at.Start, at.Length) : string.Empty).ToArray();
        Assert.Contains(borrow, related);
        Assert.Contains(related, static x => x.StartsWith("func", StringComparison.Ordinal) && x.EndsWith(')'));
    }

    // On the MIR the same record is one (DECISIONS R4-M152): the returned value's twin row, whose Refuted relation W derives the storage
    // end's Loan conflict from (R4-M132), and no validator finding. The MIR had the conflict beside the Closure's record (t7d, e3) or two
    // validator findings (an indented body).
    [Theory]
    [InlineData(Main + "    let f = func (x: i32)\n        return x@ref\n    let r = f(1)\n", "return x@ref")]
    [InlineData(Inspect + Main + "    let c = func ()\n        let l: i32 = 1\n        return l@ref\n    inspect(c())\n", "return l@ref")]
    [InlineData(Inspect + Main + "    let n: i32 = 4\n    let c = func [n] () => (1 + n)@ref\n    inspect(c())\n", "(1 + n)@ref")]
    [InlineData(Main + "    let f = func (x: i32)\n        let y = x\n        return y@ref\n", "return y@ref")]
    public void AResultOverCallLocalStorageIsOneRecordOnTheMir(string source, string text)
    {
        var c = MirBuildTest.Compile(source);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var (common, _, _, mir) = MirDifferenceLedger.Sides(c, program);
        var error = Assert.Single([.. common, .. mir], static x => DiagnosticEntries.TryGet(x.Code, out var entry) && entry.Severity == DiagnosticSeverity.Error);
        Assert.Equal((DiagnosticCode.UnsatisfiedOriginRelation_Kd, text), (error.Code, error.Node.ToString()!.Split('\n')[0].Trim()));
    }

    // DECISIONS R4-M126 P8, R4-M152: each published position is the meet of its admissible sources, so where one returned value is a
    // Borrow of an environment binding (ρ under the Shared receiver) and another a body local's, the position is ρ and only the local's
    // Borrow is Refuted, at its returned value; the call carries the receiver's Origin.
    [Fact]
    public void ACallLocalSourceBesideAnAdmissibleOneIsRefutedAgainstIt()
    {
        var source = Inspect + Main + "    let n: i32 = 4\n    let c = func [n] (flag: bool)\n        if flag\n            return n@ref\n        let l: i32 = 1\n" +
            "        return l@ref\n    inspect(c(true))\n";
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "l@ref"), (error.Code, error.Span is { } span ? source.Substring(span.Start, span.Length) : string.Empty));
    }

    // A Reborrow or Copy of a captured reference keeps its external Origin, and a moved-out owned value is no borrow.
    [Theory]
    [InlineData("    let text = \"abc\"\n    let view = text@ref\n    let item = \"x\"\n    let take = func [view, item@move] () => view\n    let r = take@move()\n")]
    [InlineData("    let text = \"abc\"\n    let take = func [text@move] () => text@move\n    let r = take@move()\n")]
    [InlineData("    var n = 7\n    let view = n@uniq\n    let s = \"x\"\n    let take = func [view@move, s@move] () => keep2(s@move, view)\n")]
    public void AResultOverExternalOriginsOutlivesTheCall(string body)
        => Assert.Empty(DiagnosticCorpus.Check("func keep2(s: string, v: uniq/i32) -> uniq/i32 during v => v\n" + Main + body).Diagnostics);

    // SPEC 15.6.5, 15.3.4 (PLAN G227, DECISIONS BU-28): in a Closure body the Origins of its captures are fixed, so the body never bounds
    // them. Storing the call's own input or another capture's Origin into the environment is Unknown, and storing a borrow local to the call
    // is Refuted, on both pipelines. dev accepted the first rows and read the dead argument natively (`leak 41`); the class S rows (TwoRegions
    // to StaticDirect) are memory-safe and rejected because SPEC 15.6.5 stays (the owner, 2026-10-23), their rewrites over a written Origin
    // being PLAN's open issue (a). A local clause over a capture's Origin is Binding's record at the initializer, as one over an enclosing
    // parameter's (the final review's B1). The body is bound, so its own errors stay (BodyError, BU's N15).
    [Theory]
    [InlineData(VarStore, Unproven, "x)")]
    [InlineData(StoreHead + "let arr: Array<ref/i32> = []" + StoreTail, Unproven, "x)")]
    [InlineData(StoreHead + "let a0: Array<ref/i32> = []\n    var arr = a0@move" + StoreTail, Unproven, "x)")]
    [InlineData(StoreHead + "let arr = Array<ref/i32>.init(capacity: 0)" + StoreTail, Unproven, "x)")]
    [InlineData(StoreHead + "var arr = Array<ref/i32>.init(capacity: 0)" + StoreTail, Unproven, "x)")]
    [InlineData(Generic, Unproven, "x)")]
    [InlineData(Nested, Unproven, "y)")]
    [InlineData(RawStore, Unproven, "y@follow@raw")]
    [InlineData(LocalStore, Unsatisfied, "local@ref")]
    [InlineData(CopyStore, Unsatisfied, "local@ref")]
    [InlineData(MovedStore, Unsatisfied, "inner@move")]
    [InlineData(TwoRegions, Unproven, "a1[0]")]
    [InlineData(InputDuringP, Unproven, "x)")]
    [InlineData(CapturedParameter, Unproven, "p)")]
    [InlineData(CopiedOption, Unproven, "p)")]
    [InlineData(EntryBorrow, Unproven, "outer)")]
    [InlineData(CapturedBorrow, Unproven, "r)")]
    [InlineData(StaticThroughLocal, Unproven, "tmp[0]\n")]
    [InlineData(StaticDirect, Unproven, "arr[0]\n")]
    [InlineData(ClauseOverParameter, Unproven, "H.init(r)")]
    [InlineData(ClauseOverCapture, Unproven, "H.init(r)")]
    [InlineData(ClauseOverCaptureFromInput, Unproven, "H.init(y)")]
    [InlineData(BodyError, nameof(DiagnosticCode.TypeMismatch_Kd), "5\n")]
    public void AStoreIntoTheEnvironmentIsJudgedAgainstItsFixedOrigins(string source, string code, string at)
    {
        var error = Assert.Single(ContextualCaptureTest.PublishedRecords(source));
        Assert.Equal(code, error.Code);
        Assert.StartsWith(at, source[error.Span.Start..], StringComparison.Ordinal);
    }

    // SPEC 23.1 (DECISIONS BU-28): an enclosing local's region is shown as that local's omitted Origin, never by its atom's kind.
    [Fact]
    public void AnEnclosingRegionIsNamedByItsLocal()
    {
        var error = Assert.Single(ContextualCaptureTest.PublishedRecords(VarStore));
        Assert.Equal("requires x outlives the omitted Origin of arr, which is not proven", error.Label);
    }

    // The valid twins: reading the environment, also into an inferred result or a body local, storing the environment's own Origin back,
    // an outer store after a Copy capture, two levels of Closures, a raw pointer over a `var` region kept unused and an element written
    // `during static` are accepted on both pipelines.
    [Theory]
    [InlineData("func ok() -> isize\n    let arr: Array<ref/i32> = []\n    var f = func [var arr@move] () -> isize => arr.length\n    return f@uniq()\n")]
    [InlineData("func ok() -> i32\n    let a: Option<ref/i32> = .None\n    var f = func [var a] (n: i32) -> i32 => n\n    return f@uniq(2)\n")]
    [InlineData("func ok(p: ref/i32) -> i32\n    var arr: Array<ref/i32> = []\n    arr.append(p)\n    var f = func [var arr@move] () -> i32\n        let first: i32 = arr[0]@follow\n        return first\n    return f@uniq()\n")]
    [InlineData("func ok(p: ref/i32) -> i32\n    var arr: Array<ref/i32> = []\n    arr.append(p)\n    let f = func [arr@ref] () => arr[0]\n    return f()@follow\n")]
    [InlineData("func ok(p: ref/i32) -> i32\n    var arr: Array<ref/i32> = []\n    arr.append(p)\n    let f = func [arr@move] () => arr[0]\n    let r = f()\n    return r@follow\n")]
    [InlineData("func ok(p: ref/i32) -> isize\n    var arr: Array<ref/i32> = []\n    arr.append(p)\n    var f = func [var arr@move] () -> isize\n        let first = arr[0]\n        arr.append(first)\n        return arr.length\n    return f@uniq()\n")]
    [InlineData("func ok(p: ref/i32) -> i32\n    var arr: Array<ref/i32> = []\n    arr.append(p)\n    let f = func [arr@ref] () -> i32\n        var tmp: Array<ref/i32> = []\n        tmp.append(arr[0])\n        return tmp[0]@follow\n    return f()\n")]
    [InlineData("func ok(p: ref/i32) -> i32\n    var arr: Array<ref/i32> = []\n    arr.append(p)\n    let outer = func [arr@ref] () -> i32\n        let inner = func [arr] () -> i32 => arr[0]@follow\n        return inner()\n    return outer()\n")]
    [InlineData("func ok(p: ref/i32) -> isize\n    var arr: Array<ref/i32> = []\n    arr.append(p)\n    var f = func [var arr@move] () -> isize\n        var g = func [var arr@move] () -> isize => arr.length\n        return g@uniq()\n    return f@move()\n")]
    [InlineData("func ok(p: ref/i32) -> i32\n    let f = func [p] () => p\n    return f()@follow\n")]
    [InlineData("func ok(p: ref/i32) -> i32\n    var o: Option<ref/i32> = .None\n    var f = func [var o] (n: i32) -> i32\n        match o\n            .Some(let r) => return r@follow\n            _ => return n\n    o = .Some(p)\n    return f@uniq(1)\n")]
    [InlineData("func ok() -> i32\n    let n: i32 = 1\n    var v = n@ref\n    let q = v@follow@raw\n    var f = func [var q@move] () -> i32 => 0\n    return f@uniq()\n")]
    [InlineData("func ok() -> i32\n    var arr: Array<ref/i32 during static> = []\n    let f = func [arr@move] () -> ref/i32 => arr[0]\n    return f()@follow\n")]
    [InlineData(TwoEnv + "    let c = func [n, m] () -> i32 => second(n@ref, m@ref)@follow\n    return c()\n")]
    [InlineData(Pass + EnvBorrow + "    let c = func [n] () -> i32 => pass(n)@follow\n    return c()\n")]
    [InlineData(EnvBorrow + "    let c = func [n] () -> i32\n        let o: Option<ref/i32> = .Some(n@ref)\n        return 0\n    return c()\n")]
    [InlineData(EnvBorrow + "    let c = func [n, p] () -> i32\n        var r = p\n        r = n@ref\n        return r@follow\n    return c()\n")]
    public void AnEnvironmentReadOrItsOwnOriginStaysValid(string function)
    {
        Assert.Empty(ContextualCaptureTest.PublishedRecords(function + "public func main() => ()\n"));
    }

    // SPEC 15.3.4 (PLAN G230; DECISIONS BU-56, an interim until G230's (A)): a named nested or root function's signature or clause that
    // names an enclosing body local's set or annotated value and selects a body local's atom is, valid or not, a located Unsupported_Kd
    // at each such written Origin on both pipelines, and nothing rests on it. dev bound the name to an atom of the local's annotation that
    // no body links to the local's region and accepted the store (d6 ran `leak 41`, d6c failed LLVM generation; the root form, a
    // document-local named by a top-level function, ran `leak 41` under MIR code generation); d6v and d6r are the valid twins.
    [Theory]
    [InlineData(NestedHead + PutStore + NestedStore, "x.s>")]
    [InlineData(NestedHead + PutBoth + NestedStore, "x.s>", "x.s)")]
    [InlineData(NestedHead + PutBoth + NestedValid, "x.s>", "x.s)")]
    [InlineData(
        WStruct + Leak + "    var w: W = W.init(items: arr@move)\n    func put(slot: uniq/Array<ref/i32 during w.s>, v: ref/i32) => slot@follow.append(v)\n" +
            NestedStore,
        "w.s>")]
    [InlineData(
        Leak + "    let r: ref/i32 = arr[0]\n    func put(slot: uniq/Array<ref/i32 during r>, v: ref/i32) => slot@follow.append(v)\n" + OtherStore,
        "r>")]
    [InlineData(
        NestedHead + "    func first(slot: ref/Array<ref/i32 during x.s>) -> i32 => slot[0]@follow\n    let got = first(w.items@ref)\n" +
            "    let items = w.items@move\n    return got + items[0]@follow\npublic func main() => ()\n",
        "x.s>")]
    [InlineData(
        NestedHead + "    func put(slot: uniq/Array<ref/i32 during q>, v: ref/i32 during q)\n        origin q == x.s\n        slot@follow.append(v)\n" +
            NestedStore,
        "x.s\n")]
    [InlineData(
        "var arr: Array<ref/i32> = []\nlet z: i32 = 0\narr.append(z@ref)\nlet r: ref/i32 = arr[0]\n" +
            "func put(slot: uniq/Array<ref/i32 during r>, v: ref/i32) => slot@follow.append(v)\nif true\n    let inner: i32 = 41\n" +
            "    put(arr@uniq, inner@ref)\nlet old: i32 = arr[1]@follow\nConsole.writeLine(\"leak \\(old)\")\n",
        "r>")]
    public void ANestedSignatureNamingABodyLocalIsUnsupported(string source, params string[] at)
    {
        var errors = ContextualCaptureTest.PublishedRecords(source).Where(static x => x.Severity == DiagnosticSeverity.Error)
            .OrderBy(static x => x.Span.Start).ToArray();
        Assert.Equal(at.Length, errors.Length);
        for (var i = 0; i < at.Length; i++)
        {
            Assert.Equal(nameof(DiagnosticCode.Unsupported_Kd), errors[i].Code);
            Assert.StartsWith(at[i], source[errors[i].Span.Start..], StringComparison.Ordinal);
        }
    }

    // The forms BU-56 leaves to their judges: an enclosing parameter's Origin, also through a local annotated with it (d6b's store of the
    // call's own input stays Unknown) or through a slot the local's Type completes from it, where the body binds the header again (dev
    // accepted the store `during r.s` of `r: ref/W during a`; it is Unknown as d6b's), an enclosing contract's (SPEC 15.3.4's `pass`),
    // an unannotated local's value, whose Type the body gives it before it binds the header again (d6u; LocalRegionCompletionTest's
    // NestedNamedFinite), a name that selects no Origin (a non-borrow local, an absent slot: InvalidOriginBinding_Kd, SPEC 15.2.1,
    // 15.3.1) and a Closure header naming the same local set, bound in its evaluating body (BU-28's EnclosingAtom: storing the call's own
    // input is Unknown, the valid twin is accepted).
    [Theory]
    [InlineData(
        Leak + "    func put(slot: uniq/Array<ref/i32 during a>, v: ref/i32) => slot@follow.append(v)\n    return 0\npublic func main() => ()\n",
        Unproven,
        "v)")]
    [InlineData(
        Leak + "    let r: ref/i32 during a = arr[0]\n    func put(slot: uniq/Array<ref/i32 during r>, v: ref/i32) => slot@follow.append(v)\n" +
            "    return 0\npublic func main() => ()\n",
        Unproven,
        "v)")]
    [InlineData(
        Leak + "    let r = arr[0]\n    func put(slot: uniq/Array<ref/i32 during r>, v: ref/i32 during r) => slot@follow.append(v)\n" +
            "    var other: Array<ref/i32> = []\n    other.append(a)\n    put(other@uniq, r)\n    return 0\npublic func main() => ()\n",
        null,
        null)]
    [InlineData(
        "func use(source: uniq/i32, bound: ref/i32) -> i32\n    origin source outlives bound\n" +
            "    func pass(value: uniq/i32 during bound) -> uniq/i32 during bound => value\n    let child = pass(source)\n    child@follow = 42\n" +
            "    return source@follow\npublic func main() => ()\n",
        null,
        null)]
    [InlineData(
        WStruct + "func leak(a: ref/W) -> i32\n    let r: ref/W during a = a\n" +
            "    func keep(slot: uniq/Array<ref/i32 during r.s>, v: ref/i32) => slot@follow.append(v)\n    return 0\npublic func main() => ()\n",
        Unproven,
        "v)")]
    [InlineData(NestedHead + "    func get(v: ref/i32 during w) -> i32 => v@follow\n    return 0\npublic func main() => ()\n", InvalidOrigin, "w)")]
    [InlineData(NestedHead + "    func get(v: ref/i32 during w.zz) -> i32 => v@follow\n    return 0\npublic func main() => ()\n", InvalidOrigin, "zz)")]
    [InlineData(NestedHead + "    func get(v: ref/i32 during x.zz) -> i32 => v@follow\n    return 0\npublic func main() => ()\n", InvalidOrigin, "zz)")]
    [InlineData(NestedHead + "    let put = func [] " + PutSignatureBoth + NestedValid, null, null)]
    [InlineData(NestedHead + "    let put = func [] " + PutSignatureStore + NestedStore, Unproven, "v)")]
    public void AnEnclosingContractOriginOrAClosureHeaderKeepsItsJudgment(string source, string? code, string? at)
    {
        var errors = ContextualCaptureTest.PublishedRecords(source).Where(static x => x.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.Equal(code is null ? Array.Empty<string>() : [code], errors.Select(static x => x.Code));
        if (at is not null)
        {
            Assert.StartsWith(at, source[errors[0].Span.Start..], StringComparison.Ordinal);
        }
    }

    // SPEC 15.8.2, 15.6.3, 7.7.2 (DECISIONS R4-M126 P3-P5; PLAN G65's G6 and n2b): an environment binding is a Field of the call receiver's
    // referent, so a Borrow through a layer stored in it is also contained in the receiver Origin ρ (a nested bare entry's binding too), and
    // a Borrow of the binding itself lies in ρ under a borrowed receiver; storing either into an Origin of the enclosing body, or returning
    // it `during` one, is Unknown once the Closure is classified, Binding's one record on both pipelines. The MIR accepted G6's stores
    // (g6a's use-after-free: one call stores `p[0]@uniq` into `*q`, the next appends to `p`), both pipelines the uncalled forms, and the
    // Borrows of `n` were Refuted. Each record stays beside an unrelated Binding error (SPEC 23.1.2).
    [Theory]
    [InlineData(G6a, "p[0]@uniq")]
    [InlineData(G6Single + "    var c = func [p@move, q@move] () -> i32\n        q@follow = p\n        return 0\n    _ = c()\n    _ = c()\n    return 0\n", "p\n")]
    [InlineData(G6Single + "    var c = func [p@move, q@move] () -> i32\n        q@follow = p\n        return 0\n    return 0\n", "p\n")]
    [InlineData(G6Array + "    var c = func [p@move, q@move] () -> i32\n        q@follow = p[0]@uniq\n        p.append(7)\n        q@follow@follow = 5\n        return 1\n    return c()\n", "p[0]@uniq")]
    [InlineData("func use(p: uniq/i32) -> i32\n    var c = func [p@move] () -> uniq/i32 during p => p\n    return 0\n", "p\n")]
    [InlineData("func use(p: uniq/i32) -> i32\n    let c = func [p@move] () -> ref/i32 during p => p@follow@ref\n    return 0\n", "p@follow@ref")]
    [InlineData(LocalView + "    let c = func [u] () -> ref/i32 during u => u@follow@ref\n    return 0\n", "u@follow@ref")]
    [InlineData(LocalView + "    var outer = func [u] () -> i32\n        let inner = func [u] () -> uniq/i32 during u => u@move\n        return 0\n    return 0\n", "u@move")]
    [InlineData("func use(p: ref/i32) -> i32\n    let n: i32 = 1\n    let c = func [n] () -> ref/i32 during p => n@ref\n    return c()@follow\n", "n@ref")]
    [InlineData("func use(p: ref/i32) -> i32\n    let n: i32 = 1\n    let c = func [n] () -> ref/i32 during p => n@ref\n    return 0\n", "n@ref")]
    public void ABorrowThroughTheEnvironmentIsContainedInTheCallReceiver(string function, string at)
    {
        foreach (var beside in new[] { false, true })
        {
            var source = function + (beside ? "func unrelated() -> i32\n    let wrong: Missing = 1\n    return 0\n" : string.Empty) + "public func main() => ()\n";
            var records = ContextualCaptureTest.PublishedRecords(source).Where(static x => x.Severity == DiagnosticSeverity.Error).ToArray();
            var error = Assert.Single(records, static x => x.Code != nameof(DiagnosticCode.UnresolvedBinding_Kd));
            Assert.Equal(Unproven, error.Code);
            Assert.StartsWith(at, source[error.Span.Start..], StringComparison.Ordinal);
            Assert.Equal(beside ? 2 : 1, records.Length);
        }
    }

    // SPEC 23.1 (DECISIONS R4-M126 §8; 2c-3B reviews B2, F1): a record's end ρ shows as the closure's call receiver, related at the
    // Closure literal; a Borrow of an environment binding, whose Origin resolves to ρ at its use, stays the shown and related borrow (g65a,
    // SPEC 15.8.2's `invalid`, a local annotation's fit at its initializer, d1); G6's store (g6b) and n2b name ρ. Each was the Closure
    // literal's text.
    [Theory]
    [InlineData(EnvBorrow + "    let c = func [n] () -> ref/i32 during p => n@ref\n    return 0\n", "n@ref", "the borrow n@ref outlives p", "n@ref")]
    [InlineData(SpecInvalid, "other@ref", "the borrow other@ref outlives static", "other@ref")]
    [InlineData(LocalAnnotated, "n@ref", "the borrow n@ref outlives p", "n@ref")]
    [InlineData(LocalReturned, "r\n", "the borrow n@ref outlives p", "n@ref")]
    [InlineData(CallReturned, "pass(n@ref)", "the borrow n@ref outlives p", "n@ref")]
    [InlineData(TupleReturned, "t.0", "the borrow n@ref outlives p", "n@ref")]
    [InlineData(LocalStored, "r\n", "the borrow n@ref outlives p", "n@ref")]
    [InlineData(G6bStore, "p\n", "the closure's call receiver outlives p", "func [p@move, q@move]")]
    [InlineData(N2bStore, "u@move", "the closure's call receiver outlives the borrow x", "func [u] () -> i32")]
    public void ARecordNamingTheCallReceiverShowsTheClosure(string function, string at, string relation, string related)
    {
        var source = function + "public func main() => ()\n";
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((Unproven, $"requires {relation}, which is not proven"), (error.Code, error.Label));
        Assert.StartsWith(at, From(error.Span), StringComparison.Ordinal);
        Assert.StartsWith(related, From(Assert.Single(error.Related!).Span), StringComparison.Ordinal);

        string From(SourceSpan? span) => span is { } value ? source[value.Start..] : string.Empty;
    }

    // SPEC 23.1, 15.6.5 (DECISIONS R4-M133, the owner's revised rule): a record shows and relates the Borrow that supplied its end, found
    // by identity with the atom the failing check recorded before resolution: of two environment Borrows the one the failing position
    // reads (a Tuple's element, a call's result `during y`); an implicit Borrow argument, shown by its text (directly, through a local,
    // stored, beside a body-local argument); a region's later assignment, its store and a meet's operand; and none when a moved capture's
    // Reborrow supplies ρ (G6's store beside an unrelated Borrow argument). A fit's record names the fit's whole destination. Each is
    // its function's one record, also beside an unrelated Binding error (SPEC 23.1.2).
    [Theory]
    [InlineData(TwoEnv + "    let c = func [n, m] () -> ref/i32 during p\n        let t = (n@ref, m@ref)\n        return t.1\n    return 0\n", "t.1", "the borrow m@ref", "m@ref", "ref/i32 during p")]
    [InlineData(TwoEnv + "    let c = func [n, m] () -> ref/i32 during p => second(n@ref, m@ref)\n    return 0\n", "second(n@ref, m@ref)", "the borrow m@ref", "m@ref", "ref/i32 during p")]
    [InlineData(G6SecondStore, "second(n@ref, p)", "the closure's call receiver", "func [p@move, q@move, n]", "uniq/i32 during p")]
    [InlineData(Pass + EnvBorrow + "    let c = func [n] () -> ref/i32 during p => pass(n)\n    return 0\n", "pass(n)", "the borrow n", "n", "ref/i32 during p")]
    [InlineData(Pass + EnvBorrow + "    let c = func [n] () -> ref/i32 during p\n        let r = pass(n)\n        return r\n    return 0\n", "r\n", "the borrow n", "n", "ref/i32 during p")]
    [InlineData(Pass + EnvStore + "    var c = func [q@move, n] () -> i32\n        q@follow = pass(n)\n        return 0\n    return 0\n", "pass(n)", "the borrow n", "n", "ref/i32 during p")]
    [InlineData(Second + EnvBorrow + "    let c = func [n] () -> ref/i32 during p\n        let k: i32 = 2\n        return second(k, n)\n    return 0\n", "second(k, n)", "the borrow n", "n", "ref/i32 during p")]
    [InlineData(EnvBorrow + "    let c = func [n] () -> Option<ref/i32 during p>\n        let o: Option<ref/i32> = .Some(n@ref)\n        return o\n    return 0\n", "o\n", "the borrow n@ref", "n@ref", "Option<ref/i32 during p>")]
    [InlineData(EnvBorrow + "    let c = func [n] () -> (ref/i32 during p, i32)\n        let t = (n@ref, 1)\n        return t\n    return 0\n", "t\n", "the borrow n@ref", "n@ref", "(ref/i32 during p, i32)")]
    [InlineData(OptionStoreInto, "o\n", "the borrow n@ref", "n@ref", "Option<ref/i32 during p>")]
    public void ARecordShowsTheBorrowThatSuppliedItsEnd(string function, string at, string longer, string related, string destination)
    {
        foreach (var beside in new[] { false, true })
        {
            var source = function + (beside ? "func unrelated() -> i32\n    let wrong: bool = 3\n    return 0\n" : string.Empty) + "public func main() => ()\n";
            var records = DiagnosticCorpus.Check(source).Diagnostics;
            Assert.Equal(beside ? 2 : 1, records.Length);
            AssertSuppliedRecord(source, Assert.Single(records, static x => x.Code != nameof(DiagnosticCode.TypeMismatch_Kd)), at, longer, related, destination);
        }
    }

    // The same for a region's chain, which the old analysis judges until R6-check (a recorded old-path limit: with an unrelated Binding
    // error it does not run, so no record stands beside that error): a region's later assignment, its store and a meet's operand.
    [Theory]
    [InlineData(EnvBorrow + "    let c = func [n, p] () -> ref/i32 during p\n        var r = p\n        r = n@ref\n        return r\n    return 0\n", "r\n", "the borrow n@ref", "n@ref", "ref/i32 during p")]
    [InlineData(RegionStore, "r\n", "the borrow n@ref", "n@ref", "ref/i32 during p")]
    [InlineData(RegionMeet, "r\n", "the borrow n@ref", "n@ref", "ref/i32 during p")]
    public void ARegionsRecordShowsTheBorrowThatSuppliedItsSource(string function, string at, string longer, string related, string destination)
    {
        var source = function + "public func main() => ()\n";
        AssertSuppliedRecord(source, Assert.Single(DiagnosticCorpus.Check(source).Diagnostics), at, longer, related, destination);
    }

    // SPEC 23.1, 15.3.3 (DECISIONS R4-M133): a local clause over an environment Borrow names the Borrow its end resolves to through the
    // local's declaration, before the receiver (the Supplier): directly, through a moved local and at the clause's second slot; the
    // plain and Consuming twins name it too. An implicit Borrow of a Field shows by its Place, `s.f`, plain or through the environment.
    // Each is its function's one record, also beside an unrelated Binding error (SPEC 23.1.2).
    [Theory]
    [InlineData(ClauseOverEnv, Unproven, "H.init(n@ref)", "the borrow n@ref", "n@ref", null)]
    [InlineData(ClauseOverMovedLocal, Unproven, "h0@move", "the borrow n@ref", "n@ref", null)]
    [InlineData(ClauseOverSecondEnv, Unproven, "H2.init(n@ref, m@ref)", "the borrow m@ref", "m@ref", null)]
    [InlineData(ClauseOverPlain, Unsatisfied, "H.init(n@ref)", "the borrow n@ref", "n@ref", null)]
    [InlineData(ClauseOverSecondPlain, Unsatisfied, "H2.init(n@ref, m@ref)", "the borrow m@ref", "m@ref", null)]
    [InlineData(ClauseOverEnvConsuming, Unsatisfied, "H.init(n@ref)", "the borrow n@ref", "n@ref", null)]
    [InlineData(ImplicitFieldPlain, Unsatisfied, "pass(s.f)", "the borrow s.f", "s.f", "ref/i32 during p")]
    [InlineData(ImplicitFieldEnv, Unproven, "pass(s.f)", "the borrow s.f", "s.f", "ref/i32 during p")]
    public void AClauseOrFieldRecordShowsTheBorrowThatSuppliedItsEnd(string function, string code, string at, string longer, string related, string? destination)
    {
        foreach (var beside in new[] { false, true })
        {
            var source = function + (beside ? "func unrelated() -> i32\n    let wrong: bool = 3\n    return 0\n" : string.Empty) + "public func main() => ()\n";
            var records = DiagnosticCorpus.Check(source).Diagnostics;
            Assert.Equal(beside ? 2 : 1, records.Length);
            var error = Assert.Single(records, static x => x.Code != nameof(DiagnosticCode.TypeMismatch_Kd));
            var verdict = code == Unproven ? "not proven" : "false";
            Assert.Equal((code, $"requires {longer} outlives p, which is {verdict}"), (error.Code, error.Label));
            Assert.StartsWith(at, source[error.Span!.Value.Start..], StringComparison.Ordinal);
            Assert.StartsWith(related, source[error.Related![0].Span!.Value.Start..], StringComparison.Ordinal);
            Assert.Equal(destination ?? "declared", destination is null ? error.Reason![3].Value : error.Reason![4].Value);
        }
    }

    // SPEC 23.1.2, DIAGNOSTICS (DECISIONS R4-M133; reviews B1, F3): a pending relation is recorded as a binding-time fit is: a local
    // annotation's fit at its initializer, under a Consuming receiver too (d3, Refuted; and d1 above), and a failure that no chain refutes
    // in a failed Origin environment a dependent of that failure (b1: the clause's record alone, as the same Unknown formed in the
    // function body). They were at the whole declaration and an independent record.
    [Theory]
    [InlineData(LocalAnnotatedConsuming, "UnsatisfiedOriginRelation_Kd n@ref")]
    [InlineData(FailedClauseStore, "MissingOriginBinding_Kd zz")]
    public void APendingRelationIsRecordedAsABindingTimeFit(string function, string records)
    {
        var source = function + "public func main() => ()\n";
        Assert.Equal(records, string.Join("; ", DiagnosticCorpus.Check(source).Diagnostics.Select(x => $"{x.Code} {Text(x.Span)}")));

        string Text(SourceSpan? span) => span is { } value ? source.Substring(value.Start, value.Length) : string.Empty;
    }

    // SPEC 15.6.3, 23.1.2 (the package's r1a–r1c; R4's 2c-3B review O2): a result `during p` through the environment is Unknown in the
    // body (its record at the returned value). Its calls bind (DECISIONS R4-M152), and in a program Binding rejects no gate runs, so the
    // body's record is the program's one; R1's located Unsupported_Kd at each call went with R1.
    [Theory]
    [InlineData("    var c = func [p@move] () -> uniq/i32 during p => p\n    let a = c()\n    let b = c()\n    a@follow = 1\n    b@follow = 2\n", "p\n")]
    [InlineData("    var c = func [p@move] () -> uniq/i32 during p => p\n    let a = c()\n    a@follow = 1\n", "p\n")]
    [InlineData("    let c = func [p@move] () -> ref/i32 during p => p@follow@ref\n    let a = c()\n    let m = c@move\n    return a@follow\n", "p@follow@ref")]
    public void AResultThroughTheEnvironmentKeepsTheBodysRecordAsItsOne(string body, string at)
    {
        var source = "func use(p: uniq/i32) -> i32\n" + body + (body.Contains("return", StringComparison.Ordinal) ? string.Empty : "    return 0\n") + "public func main() => ()\n";
        var record = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(Unproven, record.Code);
        Assert.StartsWith(at, source[record.Span!.Value.Start..], StringComparison.Ordinal);
    }

    // The valid twins (DECISIONS R4-M126 §1): under `owner/Self` ρ is no borrow, so a Reborrow through a captured reference keeps its Origin
    // (t4e, t4f, the Consuming outer of n2b, a result over it, which ρ never makes call-local); a nested exclusive slot entry, a Consuming
    // body's nested entry and SPEC 7.7.3's example; D-13's three forms under Shared and Exclusive receivers; a nested bare entry of a pair
    // capture, whose binding meets ρ on the pair layer's outer slot. A call whose relations need the receiver's well-formedness, `p outlives
    // ρ` (SPEC 15.6.1, 15.8.2; DECISIONS R4-M152, PLAN G270: a premise of the body, R2 of its receiver input): an exclusive input with its
    // clause (also in a nested Closure and through a local Reborrow), and the contextual parts over a captured Borrow that a call judging
    // every position of its inputs reaches (G216 (a)'s probes r03-r06, s04).
    [Theory]
    [InlineData(Consume + LocalView + "    let t = \"a\"\n    let c = func [u, t@move] () -> uniq/i32 during u\n        consumeText(t@move)\n        return u\n    return 0\n")]
    [InlineData(Consume + LocalView + "    let t = \"a\"\n    let c = func [u@move, t@move] () -> uniq/i32 during u\n        consumeText(t@move)\n        return u\n    return 0\n")]
    [InlineData(Consume + LocalView + "    let t = \"a\"\n    var outer = func [u, t@move] () -> i32\n        consumeText(t@move)\n" +
        "        let inner = func [u] () -> uniq/i32 during u => u@move\n        return 0\n    return 0\n")]
    [InlineData(Consume + LocalView + "    let t = \"a\"\n    let c = func [u, t@move] ()\n        consumeText(t@move)\n        return u@follow@ref\n    return 0\n")]
    [InlineData("func use() -> i32\n    var count: i32 = 0\n    var outer = func [var count] () -> i32\n        var inner = func [count@uniq] () => count@follow += 1\n" +
        "        inner()\n        return count\n    return outer()\n")]
    [InlineData(Resource + "func use() -> i32\n    let count: i32 = 0\n    let item = makeResource()\n    let outer = func [var count, item@move] () -> i32\n" +
        "        var inner = func [count@uniq] () => count@follow += 1\n        inner()\n        consume(item@move)\n        return count\n    return outer@move()\n")]
    [InlineData(Resource + "func use() -> i32\n    let count: i32 = 0\n    var next = func [var count] () -> i32\n        count += 1\n        return count\n" +
        "    let first = next()\n    let item = makeResource()\n    let take = func [item@move] () => item@move\n    let taken = take@move()\n    return first\n")]
    [InlineData("func swap<T>(x: uniq/T, y: uniq/T) => ()\nfunc use(p: ref/i32) -> i32\n    let a = p\n    let b = p\n    var c = func [var a, var b] () -> ()\n" +
        "        swap(a@uniq, b@uniq)\n    c()\n    return 0\n")]
    [InlineData("func same<T>(x: ref/T, y: ref/T) => ()\nfunc use(p: ref/i32) -> i32\n    let a = p\n    let b = p\n    let c = func [a, b] () -> ()\n" +
        "        same(a@ref, b@ref)\n    c()\n    return 0\n")]
    [InlineData("func use(p: ref/i32) -> i32\n    let a = p\n    let r = p\n    var c = func [var a, r] () -> ()\n        let q = a@uniq\n        q@follow = r\n    c()\n    return 0\n")]
    [InlineData("func use(p: ref/i32) -> i32\n    let a = p\n    let r = p\n    let c = func [a, r] () -> i32\n        let q = a@ref\n        let v = q@follow\n" +
        "        return v@follow + r@follow\n    return c()\n")]
    [InlineData("func use(p: ref/i32) -> i32\n    let a: Option<ref/i32 during p> = .Some(p)\n    let c = func [a] () -> i32\n        let q = a@ref\n        let w = q@follow\n" +
        "        return 0\n    return c()\n")]
    [InlineData("func use<s/T>(value: s/T) -> ()\n    s is owner or uniq\n    T is Copy\n    var h = func [value] () -> ()\n        var g = func [value] () -> ()\n" +
        "            let local = value\n            _ = local\n        g()\n    h()\n")]
    [InlineData("func use<s/T>(value: s/T) -> ()\n    s is ref or uniq\n    T is Copy\n    var h = func [value] () -> ()\n        var g = func [value] () -> ()\n" +
        "            let local = value\n            _ = local\n        g()\n    h()\n")]
    [InlineData(ExclusiveClause + "func use(p: ref/i32) -> i32\n    let a = p\n    var c = func [var a] () -> ()\n        fu(a@uniq)\n    c()\n    return 0\n")]
    [InlineData(ExclusiveClause + "func use(p: ref/i32) -> i32\n    let a = p\n    var c = func [var a] () -> ()\n        var d = func [var a] () -> ()\n" +
        "            fu(a@uniq)\n        d()\n    c()\n    return 0\n")]
    [InlineData(ExclusiveClause + "func use(p: ref/i32) -> i32\n    let a = p\n    var c = func [var a] () -> ()\n        let r = a@uniq\n        fu(r)\n    c()\n    return 0\n")]
    [InlineData("func same3<T>(x: Option<ref/T during c>, y: ref/T during c) => ()\n" + TwoCaptures + "    let f = func [a, b] () -> ()\n        same3(.Some(a@ref), b@ref)\n" + CallF)]
    [InlineData("func swap3<T>(x: Option<uniq/T during c>, y: uniq/T during c) => ()\n" + TwoCaptures + "    var f = func [var a, var b] () -> ()\n" +
        "        swap3(.Some(a@uniq), b@uniq)\n" + CallF)]
    [InlineData("func same4<T>(x: (ref/T during c, Option<ref/T during c>), y: ref/T during c) => ()\n" + TwoCaptures + "    let f = func [a, b] () -> ()\n" +
        "        same4((a@ref, .None), b@ref)\n" + CallF)]
    [InlineData("func same5<T>(x: [2 of Option<ref/T during c>], y: ref/T during c) => ()\n" + TwoCaptures + "    let f = func [a, b] () -> ()\n" +
        "        same5([.Some(a@ref), .None], b@ref)\n" + CallF)]
    [InlineData("func same9<T>(x: [2 of (ref/T during c, Option<ref/i32 during c>)]) => ()\n" + TwoCaptures + "    let f = func [a, b] () -> ()\n" +
        "        same9([(a@ref, .None), (b@ref, .None)])\n" + CallF)]
    public void AReceiverContainmentTheBodyNeedsNotStaysValid(string function)
    {
        Assert.Empty(ContextualCaptureTest.PublishedRecords(function + "public func main() => ()\n"));
    }

    // The receiver's well-formedness (DECISIONS R4-M152, PLAN G270) gives only that each Origin stored in the environment outlives ρ: two
    // captured references of independent Origins are not equal (`swap` and `swap3` instantiate one `T`), and a Borrow of a Closure local
    // stored into an environment binding does not outlive that binding's Origin. Under `owner/Self` the receiver is the environment by
    // value, with no ρ, so a captured reference returned as a static one stays Unproven (BU's own3, the guard's twin). Each keeps its
    // record on both pipelines.
    [Theory]
    [InlineData(
        "func swap<T>(x: uniq/T, y: uniq/T) => ()\n" + IndependentCaptures + "    var c = func [var a, var b] () -> ()\n        swap(a@uniq, b@uniq)\n" +
            "    c()\n    return 0\n",
        "b@uniq")]
    // D-1 (the owner, 2026-10-29, 「選択肢 (a)：引数に記録し、payload を関連位置に添える」, G216 (a)): the call records the relation that its
    // argument's contextual payload `a@uniq` supplies at the argument `.Some(a@uniq)`, with the payload as a related location.
    [InlineData(
        "func swap3<T>(x: Option<uniq/T during c>, y: uniq/T during c) => ()\n" + IndependentCaptures + "    var c = func [var a, var b] () -> ()\n" +
            "        swap3(.Some(a@uniq), b@uniq)\n    c()\n    return 0\n",
        ".Some(a@uniq)")]
    [InlineData(
        ExclusiveClause + "func use(p: ref/i32) -> i32\n    let a = p\n    var c = func [var a] () -> ()\n        let local: i32 = 1\n" +
            "        var l = local@ref\n        fu(l@uniq)\n        a = l\n    c()\n    return 0\n",
        "l\n")]
    [InlineData(
        Consume + "func use(p: ref/i32) -> i32\n    let a = p\n    let t = \"t\"\n    let c = func [a, t@move] () -> ref/i32\n        consumeText(t@move)\n" +
            "        return a\n    _ = c@move()\n    return 0\n",
        "a\n")]
    public void TheReceiversWellFormednessProvesOnlyItsStoredOrigins(string function, string at)
    {
        var source = function + "public func main() => ()\n";
        var records = ContextualCaptureTest.PublishedRecords(source);
        Assert.Contains(records, x => x.Code is nameof(DiagnosticCode.UnprovenOriginRelation_Kd) or nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd) &&
            source[x.Span.Start..].StartsWith(at, StringComparison.Ordinal));
    }

    // D-13's Option form under an Exclusive receiver and g6c, G6's Consuming control, hold by SPEC: Binding and the MIR accept them; the
    // old analysis keeps its conservative Loan record (a recorded old-path limit until R6-check).
    [Theory]
    [InlineData(OptionStore, "a@uniq")]
    [InlineData(G6c, "q@follow = p")]
    public void OnlyTheOldAnalysisRejectsAReceiverContainmentThatHolds(string function, string at)
    {
        var source = function + "public func main() => ()\n";
        var c = VerifiedSpecExampleTest.Bind(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        c.Analyze();
        var (common, old, passed, mir) = MirDifferenceLedger.Sides(c, c.BuildMir());
        Assert.Empty(Errors([.. common, .. mir]));
        var errors = Errors([.. common, .. old, .. passed]);
        Assert.All(errors, static x => Assert.Equal(DiagnosticCode.ComparisonLoanConflict_Kd, x.Code));
        Assert.Contains(errors, x => source[x.Node.Span.Start..].StartsWith(at, StringComparison.Ordinal));

        static MirDifferenceLedger.Entry[] Errors(List<MirDifferenceLedger.Entry> side)
            => [.. side.Where(static x => DiagnosticEntries.TryGet(x.Code, out var entry) && entry.Severity == DiagnosticSeverity.Error)];
    }

    // DECISIONS R4-M126 §2.5: a pending relation waits for its Closure's classification, which runs on every path that bound the body:
    // beside a failed parameter Type or a failed result (a), the two early returns after the body, also with a pair-layer capture its
    // bare use marks (the receiver from the mark, 2c-2's review N5), for a closure argument bound under a consultation (d), and again
    // after a warm rebind, its record is one, located at its value, on both pipelines.
    [Theory]
    [InlineData(G6Single + "    var c = func [p@move, q@move] (z: Missing) -> i32\n        q@follow = p\n        return 0\n    return 0\n", 2)]
    [InlineData(G6Single + FailedResult + "    return 0\n", 2)]
    [InlineData(MarkedHead + "    var c = func [value, p@move, q@move] (z: Missing) -> ()\n        let local = value\n        _ = local\n        q@follow = p\n", 2)]
    [InlineData(MarkedHead + "    var c = func [value, p@move, q@move] (k: bool)\n        let local = value\n        _ = local\n" + FailedReturns, 2)]
    [InlineData(ConsultedStore, 1)]
    public void APendingRelationIsJudgedOnceOnEveryPathThatBindsTheBody(string function, int count)
    {
        var source = function + "public func main() => ()\n";
        var records = ContextualCaptureTest.PublishedRecords(source).Where(static x => x.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.Equal(count, records.Length);
        var error = Assert.Single(records, static x => x.Code == Unproven);
        Assert.StartsWith("p\n", source[error.Span.Start..], StringComparison.Ordinal);

        var c = MinimalEmissionTest.Analyze(source);
        var first = c.Binding.Issues.Select(static x => $"{x.Code} {x.Node.Span.Start}").Order(StringComparer.Ordinal).ToArray();
        c.Bind();
        Assert.Equal(first, c.Binding.Issues.Select(static x => $"{x.Code} {x.Node.Span.Start}").Order(StringComparer.Ordinal).ToArray());
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmPendingRelationsAllocateNothing()
    {
        var nested = LocalView.Replace("use", "nested", StringComparison.Ordinal) + "    let t = \"a\"\n    var outer = func [u, t@move] () -> i32\n" +
            "        consumeText(t@move)\n        let inner = func [u] () -> uniq/i32 during u => u@move\n        return 0\n    return 0\n";
        var c = MinimalEmissionTest.Analyze(G6c + nested + "public func main() => ()\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(valid, MinimalEmissionTest.Describe(c));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmEnvironmentBorrowsAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(VerificationWorkloads.EnvironmentBorrows);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Generate(TextWriter.Null)));
        Assert.True(valid, MinimalEmissionTest.Describe(c));
    }

    // One record of ARecordShowsTheBorrowThatSuppliedItsEnd: its code, label, location, related Borrow and destination.
    private static void AssertSuppliedRecord(string source, CheckDiagnostic error, string at, string longer, string related, string destination)
    {
        Assert.Equal((Unproven, $"requires {longer} outlives p, which is not proven"), (error.Code, error.Label));
        Assert.StartsWith(at, source[error.Span!.Value.Start..], StringComparison.Ordinal);
        Assert.StartsWith(related, source[Assert.Single(error.Related!).Span!.Value.Start..], StringComparison.Ordinal);
        Assert.Equal(destination, error.Reason![4].Value);
    }
}
