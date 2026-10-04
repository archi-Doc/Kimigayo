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
}
