// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Verification;
using Xunit;

namespace XunitTest;

// SPEC 7.6.2, 15.8.2: an environment binding is a Place of its closure, so a closure body checks the Loans on its environment as an
// ordinary body checks its locals, and a borrow of an environment binding's own slot depends on that binding.
public class ClosureEnvironmentOriginTest
{
    private const string Main = "public func main() -> ()\n";

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
    // result; it was accepted and outlived the Move of the closure. Receiver-dependent results remain a located limit (G65).
    [Fact]
    public void ASlotBorrowOfAnEnvironmentBindingIsReceiverDependent()
    {
        var source = Main + "    let n = 7\n    let f = func [n@ref] () => n@ref\n    let r = f()\n    let g = f@move\n    require r@follow@follow == 7 else => $abort(\"r\")\n";
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnsupportedBinding_Kd), "f()"), (error.Code, error.Span is { } span ? source.Substring(span.Start, span.Length) : string.Empty));
    }

    // SPEC 15.8.2, 15.6.1, 23.3.6.5: a result inferred from the body that borrows the call's own storage, a parameter, a body local or
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

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmEnvironmentBorrowsAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(VerificationWorkloads.EnvironmentBorrows);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }
}
