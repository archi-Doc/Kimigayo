// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// SPEC 7.6.3, 13.5.3, 15.1.6: a closure's receiver requirement follows its resolved operations. Only an explicit transfer of
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
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        ScalarEmissionTest.EmitFixture("ClosureReceiver" + name, source, string.Empty);
    }

    [Fact]
    public void AnExplicitTransferIsConsuming()
    {
        const string Source = Bag + "let b = Bag.init([1, 2], .None)\nlet take = func [b@move] () -> Array<i32> => b.items@move\nlet items = take@move()\nrequire items.length == 2 else => $abort(\"take\")";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        var closure = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.IsAnonymous);
        Assert.Equal(SemanticsKind.Owner, closure.BoundClosure!.Receiver);
    }

    [Fact]
    public void AnExclusiveBodyNeedsAWritableClosure()
    {
        var c = MinimalEmissionTest.Analyze(Bag + "let c = Bag.init([1, 2], .None)\nlet bump = func [var c@move] () => c.items[0] += 5\nbump()");
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        Assert.Equal(nameof(DiagnosticCode.InvalidAssignment_Kd), Assert.Single(TestDiagnostics.Of(c)).Code);
    }

    private const string CapturedView = "public func main() -> ()\n    var n = 7\n    let view = n@uniq\n";

    // SPEC 3.5, 7.6.3, 14.9.1: an expression body without a written or expected result Type is a result source acquired by bare
    // acquisition, so a bare captured exclusive reference is Reborrowed exclusively and the call is Exclusive. The Note names that
    // Reborrow; a ref/F value never supplies an Exclusive call, so its Advice is not to declare the binding with var.
    [Theory]
    [InlineData(CapturedView + "    let f = func [view] () => view\n    f()\n", "f", "Declare the binding with var to call it")]
    [InlineData(CapturedView + "    let f = func [view] () => view\n    let g = f@ref\n    g()\n", "g", "A ref/F value cannot supply an Exclusive call; call the closure through its own var binding or a uniq/F borrow")]
    public void ABareInferredResultReborrowIsAnExclusiveCall(string source, string callee, string advice)
    {
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.InvalidAssignment_Kd), callee), (error.Code, error.Span is { } span ? source.Substring(span.Start, span.Length) : string.Empty));
        Assert.Equal("The call is Exclusive (SPEC 7.6.3): it borrows the callee exclusively, because the callee Reborrows the captured exclusive reference view exclusively", error.Note);
        Assert.Equal(advice, error.Advice);
    }

    // A shared borrow through the captured exclusive reference keeps the call Shared; its receiver-dependent result is the G65 limit.
    [Fact]
    public void ASharedReborrowResultStaysShared()
    {
        var source = CapturedView + "    let f = func [view] () => view@follow@ref\n    f()\n";
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.UnsupportedBinding_Kd), error.Code);
    }

    [Fact]
    public void AScalarReadThroughACapturedExclusiveReferenceIsShared()
        => ScalarEmissionTest.EmitFixture("ClosureReceiverScalarRead", "var n = 7\nlet view = n@uniq\nlet f = func [view] () => view@follow + 1\nlet r = f()\nrequire r == 8 and f() == 8 else => $abort(\"r\")", string.Empty);
}
