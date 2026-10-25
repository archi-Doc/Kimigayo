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

    // G6's two forms of a function whose Closure stores through a captured `q` into an Origin of the enclosing body (2c-D design B's g6).
    private const string G6Single = "func use(p: uniq/i32, q: uniq/(uniq/i32 during p)) -> i32\n";

    private const string G6Array = "func use(p: uniq/Array<i32>, q: uniq/(uniq/i32 during p)) -> i32\n";

    // A local exclusive reference a Closure captures bare (2x-P7's t4 and n2 forms).
    private const string LocalView = "func use() -> i32\n    var x: i32 = 1\n    let u = x@uniq\n";

    private const string Consume = "func consumeText(value: string) => ()\n";

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

    // The forms whose records name the call receiver ρ or a Borrow resolved to it (ARecordNamingTheCallReceiverShowsTheClosure).
    private const string EnvBorrow = "func use(p: ref/i32) -> i32\n    let n: i32 = 1\n";
    private const string G6bStore = G6Single + "    var c = func [p@move, q@move] () -> i32\n        q@follow = p\n        return 0\n    return 0\n";

    private const string SpecInvalid = "func makeText() -> string => \"a\"\nfunc use() -> i32\n    let other = makeText()\n" +
        "    let invalid = func [other@move] () -> ref/string => other@ref\n    return 0\n";

    private const string N2bStore = LocalView + "    var outer = func [u] () -> i32\n        let inner = func [u] () -> uniq/i32 during u => u@move\n        return 0\n" +
        "    return 0\n";

    private const string Unsatisfied = nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd);

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
    // result; it was accepted and outlived the Move of the closure (SPEC 15.8.2). A receiver-dependent result is unsupported.
    [Fact]
    public void ASlotBorrowOfAnEnvironmentBindingIsReceiverDependent()
    {
        var source = Main + "    let n = 7\n    let f = func [n@ref] () => n@ref\n    let r = f()\n    let g = f@move\n    require r@follow@follow == 7 else => $abort(\"r\")\n";
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.Unsupported_Kd), "f()"), (error.Code, error.Span is { } span ? source.Substring(span.Start, span.Length) : string.Empty));
    }

    // SPEC 15.8.2, 15.6.1, 23.1: a result inferred from the body that borrows the call's own storage, a parameter, a body local or
    // an environment binding that a Consuming call consumes, is Refuted at the returned value, with the borrow as the longer end and the
    // closure's call result as the shorter, related at the Borrow and at the header. These were a Loan conflict at the closure or
    // accepted (the Consuming forms).
    [Theory]
    [InlineData("func keep(s: string, r: ref/string) -> ref/string during r => r\n" + Main + "    let a = \"x\"\n    let b = \"y\"\n    let take = func [a@move, b@move] () => keep(a@move, b@ref)\n", "keep(a@move, b@ref)", "b@ref")]
    [InlineData("func keep(s: string, r: ref/i32) -> ref/i32 during r => r\n" + Main + "    let a = \"x\"\n    let n = 7\n    let take = func [a@move, n] () => keep(a@move, n@ref)\n", "keep(a@move, n@ref)", "n@ref")]
    [InlineData(Main + "    let s = \"x\"\n    let n = 7\n    let take = func [s@move, n] () => (s@move, n@ref)\n", "(s@move, n@ref)", "n@ref")]
    [InlineData(Main + "    let f = func (x: i32) => x@ref\n    let r = f(1)\n", "x@ref", "x@ref")]
    public void AResultOverCallLocalStorageIsRefuted(string source, string text, string borrow)
    {
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), text), (error.Code, error.Span is { } span ? source.Substring(span.Start, span.Length) : string.Empty));
        var related = error.Related!.Select(x => x.Span is { } at ? source.Substring(at.Start, at.Length) : string.Empty).ToArray();
        Assert.Contains(borrow, related);
        Assert.Contains(related, static x => x.StartsWith("func", StringComparison.Ordinal) && x.EndsWith(')'));
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
        foreach (var pipeline in new[] { CompilerPipeline.Legacy, CompilerPipeline.Mir })
        {
            var error = Assert.Single(ContextualCaptureTest.PublishedRecords(source, pipeline));
            Assert.Equal(code, error.Code);
            Assert.StartsWith(at, source[error.Span.Start..], StringComparison.Ordinal);
        }
    }

    // SPEC 23.1 (DECISIONS BU-28): an enclosing local's region is shown as that local's omitted Origin, never by its atom's kind.
    [Fact]
    public void AnEnclosingRegionIsNamedByItsLocal()
    {
        var error = Assert.Single(ContextualCaptureTest.PublishedRecords(VarStore, CompilerPipeline.Legacy));
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
    public void AnEnvironmentReadOrItsOwnOriginStaysValid(string function)
    {
        foreach (var pipeline in new[] { CompilerPipeline.Legacy, CompilerPipeline.Mir })
        {
            Assert.Empty(ContextualCaptureTest.PublishedRecords(function + "public func main() => ()\n", pipeline));
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
            foreach (var pipeline in new[] { CompilerPipeline.Legacy, CompilerPipeline.Mir })
            {
                var records = ContextualCaptureTest.PublishedRecords(source, pipeline).Where(static x => x.Severity == DiagnosticSeverity.Error).ToArray();
                var error = Assert.Single(records, static x => x.Code != nameof(DiagnosticCode.UnresolvedBinding_Kd));
                Assert.Equal(Unproven, error.Code);
                Assert.StartsWith(at, source[error.Span.Start..], StringComparison.Ordinal);
                Assert.Equal(beside ? 2 : 1, records.Length);
            }
        }
    }

    // SPEC 23.1 (DECISIONS R4-M126 §8; 2c-3B reviews B2, F1): a record's end ρ shows as the closure's call receiver, related at the
    // Closure literal; a Borrow of an environment binding, whose Origin resolves to ρ at its use, stays the shown and related borrow (g65a,
    // SPEC 15.8.2's `invalid`); G6's store (g6b) and n2b name ρ. Each was the Closure literal's text.
    [Theory]
    [InlineData(EnvBorrow + "    let c = func [n] () -> ref/i32 during p => n@ref\n    return 0\n", "n@ref", "the borrow n@ref outlives p", "n@ref")]
    [InlineData(SpecInvalid, "other@ref", "the borrow other@ref outlives static", "other@ref")]
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

    // SPEC 15.6.3, 23.1.2 (the package's r1a–r1c; R4's 2c-3B review O2): a result `during p` through the environment is Unknown in the
    // body (its record at the returned value), independent of R1's located Unsupported_Kd at each call of the receiver-dependent result.
    [Theory]
    [InlineData("    var c = func [p@move] () -> uniq/i32 during p => p\n    let a = c()\n    let b = c()\n    a@follow = 1\n    b@follow = 2\n", "p\n", 2)]
    [InlineData("    var c = func [p@move] () -> uniq/i32 during p => p\n    let a = c()\n    a@follow = 1\n", "p\n", 1)]
    [InlineData("    let c = func [p@move] () -> ref/i32 during p => p@follow@ref\n    let a = c()\n    let m = c@move\n    return a@follow\n", "p@follow@ref", 1)]
    public void AResultThroughTheEnvironmentKeepsTheBodysRecordBesideItsCalls(string body, string at, int calls)
    {
        var source = "func use(p: uniq/i32) -> i32\n" + body + (body.Contains("return", StringComparison.Ordinal) ? string.Empty : "    return 0\n") + "public func main() => ()\n";
        var records = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.Equal([Unproven, .. Enumerable.Repeat(nameof(DiagnosticCode.Unsupported_Kd), calls)], records.Select(static x => x.Code));
        Assert.StartsWith(at, source[records[0].Span!.Value.Start..], StringComparison.Ordinal);
        Assert.All(records.Skip(1), x => Assert.StartsWith("c()", source[x.Span!.Value.Start..], StringComparison.Ordinal));
    }

    // The valid twins (DECISIONS R4-M126 §1): under `owner/Self` ρ is no borrow, so a Reborrow through a captured reference keeps its Origin
    // (t4e, t4f, the Consuming outer of n2b, a result over it, which ρ never makes call-local); a nested exclusive slot entry, a Consuming
    // body's nested entry and SPEC 7.7.3's example; D-13's three forms under Shared and Exclusive receivers (the receiver's well-formedness
    // is no row: no premise is missing); a nested bare entry of a pair capture, whose binding meets ρ on the pair layer's outer slot.
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
    public void AReceiverContainmentTheBodyNeedsNotStaysValid(string function)
    {
        foreach (var pipeline in new[] { CompilerPipeline.Legacy, CompilerPipeline.Mir })
        {
            Assert.Empty(ContextualCaptureTest.PublishedRecords(function + "public func main() => ()\n", pipeline));
        }
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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
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
        foreach (var pipeline in new[] { CompilerPipeline.Legacy, CompilerPipeline.Mir })
        {
            var records = ContextualCaptureTest.PublishedRecords(source, pipeline).Where(static x => x.Severity == DiagnosticSeverity.Error).ToArray();
            Assert.Equal(count, records.Length);
            var error = Assert.Single(records, static x => x.Code == Unproven);
            Assert.StartsWith("p\n", source[error.Span.Start..], StringComparison.Ordinal);
        }

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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmEnvironmentBorrowsAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(VerificationWorkloads.EnvironmentBorrows);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }
}
