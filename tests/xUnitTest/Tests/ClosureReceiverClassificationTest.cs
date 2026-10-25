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

    [Theory]
    [InlineData("ForSubject", Bag + "let b = Bag.init([1, 2], .Some(\"x\"))\nlet sum = func [b@move] () -> i32\n    var s: i32 = 0\n    for v in b.items\n        s += v@follow\n    return s\nrequire sum() == 3 and sum() == 3 else => $abort(\"sum\")")]
    [InlineData("MatchSubject", Bag + "let c = Bag.init([1], .Some(\"x\"))\nlet has = func [c@move] () -> bool\n    match c.label\n        .Some(let t) => return true\n        .None => return false\nrequire has() and has() else => $abort(\"has\")")]
    [InlineData("BorrowedArgument", Bag + "let d = Bag.init([1], .None)\nlet arg = func [d@move] () => total(d.items)\nrequire arg() == 7 and arg() == 7 else => $abort(\"arg\")")]
    [InlineData("ExclusiveUpdate", Bag + "let c = Bag.init([1, 2], .None)\nvar bump = func [var c@move] () -> i32\n    c.items[0] += 5\n    c.n += 1\n    return c.n\nrequire bump() == 1 and bump() == 2 else => $abort(\"bump\")")]
    [InlineData("ExclusiveReceiver", Bag + "let b = Bag.init([1], .None)\nvar grow = func [var b@move] () -> i32\n    b.items.append(3)\n    b.n += 1\n    return b.n\nrequire grow() == 1 and grow() == 2 else => $abort(\"grow\")")]
    public void BorrowingBodiesAreNotConsuming(string name, string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        ScalarEmissionTest.EmitFixture("ClosureReceiver" + name, source, string.Empty);
    }

    [Fact]
    public void AnExplicitTransferIsConsuming()
    {
        const string Source = Bag + "let b = Bag.init([1, 2], .None)\nlet take = func [b@move] () -> Array<i32> => b.items@move\nlet items = take@move()\nrequire items.length == 2 else => $abort(\"take\")";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete && c.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        var closure = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.IsAnonymous);
        Assert.Equal(SemanticsKind.Owner, closure.ClosureOf()!.Receiver);
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
        "    public func add(self: uniq/Self, n: i32) => self.value = self.value + n\n    public func read(self: ref/Self) -> i32 => self.value\n" +
        "struct ObjCell\n    public var value: i32 = 1\n    public func put(self: objuniq/Self, n: i32)\n        let p = self@follow@uniq\n        p.value = n\n" +
        "public func main() -> ()\n    let n = 7\n    let t = (1, 2)\n    let fa: [2 of i32] = [1, 2]\n    var c = Counter.init()\n    let r = c@uniq\n" +
        "    var a: Array<Counter> = []\n    var x = 1\n    var u: Array<uniq/i32> = []\n    u.append(x@uniq)\n    var o = Kimi.Intrinsics.makeObj(ObjCell.init())\n" +
        "    let f = func [var n, var t, var fa, r, var a@move, var u@move, var o@move] () -> i32\n";

    // SPEC 7.7.3, 7.7.2 (PLAN G147 (3)): a Place reached from a capture, the capture itself, a part of it or a captured referent, that
    // the body writes, borrows exclusively or lends to an exclusive parameter as an argument or receiver makes the call Exclusive, so
    // a call of a `let` Closure is invalid; the Shared twins read or borrow shared. Each Exclusive row was classed Shared.
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
    public void AnExclusiveUseOfAPlaceReachedFromACaptureIsAnExclusiveCall(string body, bool exclusive)
    {
        var source = Places + body + "        return 0\n    let v = f()\n";
        var c = MinimalEmissionTest.Analyze(source);
        var closure = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.IsAnonymous);
        Assert.Equal(exclusive ? SemanticsKind.Uniq : SemanticsKind.Ref, closure.ClosureOf()!.Receiver);
        var errors = DiagnosticCorpus.Check(source).Diagnostics;
        if (!exclusive)
        {
            Assert.Empty(errors);
            return;
        }

        var error = Assert.Single(errors);
        Assert.Equal((nameof(DiagnosticCode.InvalidAssignment_Kd), "f"), (error.Code, error.Span is { } span ? source.Substring(span.Start, span.Length) : string.Empty));
        Assert.Equal("The call is Exclusive (SPEC 7.7.3): it borrows the callee exclusively, because the callee changes its environment or a captured referent", error.Note);
    }

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

    // SPEC 7.3.3 (check 1), 7.7.3, 13.5.5: an object callee supplies only its payload authority. rc, arc and objref grant shared access,
    // a let obj handle is not writable, a reference supplies no owning receiver and a payload offers no Take; a temporary callee, a
    // callee reached through a reference or an element and an Exclusive call of an unproven Sealed payload are located limits before
    // generation, and an environment-borrowing result keeps the handle lent.
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
    [InlineData("let n: i32 = 4\nlet h = Kimi.Intrinsics.makeRc(func [n] () => n@ref)\nlet r = h()\nlet moved = h@move\nrequire r@follow == 4 else => $abort(\"result\")", "Unsupported_Kd", "h()", null, false)]
    [InlineData("let n: i32 = 4\nlet h = Kimi.Intrinsics.makeRc(func [n] () => n@ref)\nlet r = h@follow()\nlet moved = h@move\nrequire r@follow == 4 else => $abort(\"result\")", "Unsupported_Kd", "h@follow()", null, false)]
    [InlineData("func call<F>(v: objuniq/F) -> i32\n    F is Callable<uniq, () -> i32> and ObjectPayload\n    return v()\n()", "Unsupported_Kd", "v", null, false)]
    [InlineData("struct Holder\n    public var callback: rc/(() -> i32)\n    public init(callback: rc/(() -> i32)) => self.callback = callback@move\nfunc call(holder: ref/Holder) -> i32 => holder.callback()\nlet f: () -> i32 = func [] () -> i32 => 7\nlet holder = Holder.init(Kimi.Intrinsics.makeRc(f@move))\nrequire call(holder@ref) == 7 else => $abort(\"field\")", "Unsupported_Kd", "holder.callback", null, false)]
    [InlineData("func call(r: ref/(rc/(() -> i32))) -> i32 => r@follow()\nlet f: () -> i32 = func [] () -> i32 => 7\nlet h = Kimi.Intrinsics.makeRc(f@move)\nrequire call(h@ref) == 7 else => $abort(\"follow\")", "Unsupported_Kd", "r@follow", null, false)]
    [InlineData("let f: () -> i32 = func [] () -> i32 => 7\nlet hs = [Kimi.Intrinsics.makeRc(f@move)]\nrequire hs[0]() == 7 else => $abort(\"element\")", "Unsupported_Kd", "hs[0]", null, false)]
    public void ObjectCalleesSupplyOnlyTheirPayloadAuthority(string source, string code, string at, string? note, bool authority)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Validate(out _));
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((code, at), (error.Code, error.Span is { } span ? source.Substring(span.Start, span.Length) : string.Empty));
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
}
