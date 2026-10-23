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

    private const string Unproven = nameof(DiagnosticCode.UnprovenOriginRelation_Kd);

    private const string Unsatisfied = nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd);

    // S1 (PLAN G65): these bodies were accepted, and the first aborted at run time with the write observed through `r`.
    [Theory]
    [InlineData("    let n = 7\n    var f = func [var n] () -> i32\n        let r = n@ref\n        n = 1\n        return r@follow\n    let v = f()\n", "n = 1")]
    [InlineData("    let n = 7\n    var f = func [var n] () -> i32\n        let r = id(n@ref)\n        n = 1\n        return r@follow\n    let v = f()\n", "n = 1")]
    [InlineData("    let n = 7\n    var f = func [var n] () -> i32\n        let r = n@uniq\n        let q = n\n        r@follow = 3\n        return q\n    let v = f()\n", "n")]
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
    // being PLAN's open issue (a). The body is bound, so its own errors stay (BodyError, BU's N15).
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
