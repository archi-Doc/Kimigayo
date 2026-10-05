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
    public void EnvironmentLoansAreCheckedInTheBody(string body, string text)
    {
        var source = "func id(x: ref/i32) -> ref/i32 during x => x\n" + Main + body;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.ComparisonLoanConflict_Kd), text), (error.Code, error.Span is { } span ? source.Substring(span.Start, span.Length) : string.Empty));
    }

    // Loans on environment bindings end at their last use and stay per Field, as those of locals do.
    [Theory]
    [InlineData("LastUse", "let n = 7\nvar f = func [var n] () -> i32\n    let r = n@ref\n    let v = r@follow\n    n = 1\n    return v + n\nrequire f() == 8 else => $abort(\"last use\")")]
    [InlineData("Fields", "struct P\n    public var a: i32\n    public var b: i32\n    public init(a: i32, b: i32)\n        self.a = a\n        self.b = b\nlet p = P.init(a: 1, b: 2)\nvar f = func [var p@move] () -> i32\n    let r = p.a@ref\n    p.b = 5\n    return r@follow + p.b\nrequire f() == 6 else => $abort(\"fields\")")]
    [InlineData("ExclusiveAfterChild", "var n = 7\nlet view = n@uniq\nvar f = func [view] () -> i32\n    let c = view@follow@uniq\n    c@follow = 2\n    view@follow += 1\n    return view@follow\nrequire f() == 3 else => $abort(\"exclusive\")")]
    [InlineData("String", "let message = \"env\"\nlet f = func [message@move] () => Console.writeLine(message)\nf()")]
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
