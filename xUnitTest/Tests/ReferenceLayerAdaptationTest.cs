// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 10.2: at a fixed expected ref/U, safe reference layers ending in U yield one shared reference; the
/// innermost ref layer is Copied with its own Origin and uniq layers below it are shared-Reborrowed.</summary>
public class ReferenceLayerAdaptationTest
{
    private const string Node = "struct Node\n    public var id: i32\n    public init(id: i32) => self.id = id\nfunc validate(node: ref/Node) -> i32 => node.id\n";

    [Theory]
    [InlineData("SharedThroughExclusive", Node + "var first = Node.init(1)\nvar second = Node.init(2)\nlet nodes: [2 of uniq/Node] = [first@uniq, second@uniq]\nvar total: i32 = 0\nfor node in nodes\n    total += validate(node)\nrequire total == 3 else => $abort(\"total\")\nConsole.writeLine(\"ok\")", "ok\n")]
    [InlineData("InnerSharedCopied", "let x = \"a\"\nlet y = \"b\"\nlet names: [2 of ref/string] = [x@ref, y@ref]\nfor name in names\n    let view: ref/string = name\n    Console.writeLine(view)", "a\nb\n")]
    [InlineData("SharedBelowExclusive", "var n = 7\nlet r = n@ref\nvar holder: [1 of ref/i32] = [r]\nfor h in holder@uniq\n    let inner: ref/i32 = h\n    require inner == 7 else => $abort(\"inner\")\nConsole.writeLine(\"ok\")", "ok\n")]
    [InlineData("OptionPayload", Node + "func report(found: ref/Node? during a) -> i32\n    return match found\n        .Some(let hit) => validate(hit)\n        .None => 0\nvar total: i32 = 0\nlet third = Node.init(3)\nrequire validate(third@ref) == 3 else => $abort(\"direct\")\nConsole.writeLine(\"ok\")", "ok\n")]
    [InlineData("UniqTemporary", Node + "func pass(n: uniq/Node) -> uniq/Node => n\nvar a = Node.init(4)\nrequire validate(pass(a@uniq)) == 4 else => $abort(\"temporary\")\nConsole.writeLine(\"ok\")", "ok\n")]
    public void OneSharedReferenceThroughLayers(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("ReferenceLayer" + name, source, stdout);

    [Fact]
    public void TheInnerSharedReferenceKeepsOnlyItsOwnOrigin()
    {
        // The Copied inner reference no longer depends on the array slot; replacing the whole array afterwards is valid.
        var c = MinimalEmissionTest.Analyze("let x = \"a\"\nvar names: [1 of ref/string] = [x@ref]\nlet view: ref/string = names[0]\nnames = [x@ref]\nConsole.writeLine(view)");
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void AnExclusiveLayerKeepsItsLoan()
    {
        // Reborrowing through a uniq layer keeps that layer's Loan: the owner cannot be replaced while the view lives.
        var c = MinimalEmissionTest.Analyze(Node + "var first = Node.init(1)\nlet nodes: [1 of uniq/Node] = [first@uniq]\nlet view: ref/Node = nodes[0]\nfirst = Node.init(5)\nlet id = validate(view)");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
    }

    [Theory]
    [InlineData("func f(x: ref/i32) => ()\nvar a: i32 = 1\nlet r = a@uniq\nf(r@move)")]
    [InlineData("var a: i32 = 1\nlet r = a@uniq\nlet v: ref/i32 = r@move")]
    [InlineData("func f(x: i32) => ()\nvar a: i32 = 1\nlet r = a@uniq\nf(r@move)")]
    public void ATransferredReferenceIsNotAdaptedAfterwards(string source)
    {
        // SPEC 10.2: an explicit @move runs first and is not corrected by a later Reborrow or Scalar read.
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
    }

    [Fact]
    public void ATransferredReferenceFitsItsOwnType()
    {
        var c = MinimalEmissionTest.Analyze("func f(x: uniq/i32) => ()\nvar a: i32 = 1\nlet r = a@uniq\nf(r@move)");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("func get(m: ref/Dictionary<i32, i32>) -> i32 => m[1]")]
    [InlineData("func at(a: ref/(ref/[2 of i32] during b)) -> i32 => a[1]")]
    public void IndexingThroughSeveralLayersReportsOnlyItsBoundary(string source)
    {
        // STATUS boundary: indexing through several reference layers stops at Binding with one diagnostic.
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Contains(c.Binding.Issues, x => x.Code == Kimi.DiagnosticCode.UnsupportedBinding_Kd);
        Assert.Empty(c.AnalyzeControlFlow().Issues);
    }
}
