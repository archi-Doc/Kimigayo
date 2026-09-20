// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class BorrowedArrayEmissionTest
{
    [Theory]
    [InlineData("Sum", "func sum(a?: ref/[3 of i32]) -> i32\n    var total = 0\n    for i in a.indices => total += a[i]\n    return total\nlet a: [3 of i32] = [10, 20, 12]\nrequire sum(a@ref) == 42 else => $abort(\"sum\")")]
    [InlineData("Empty", "func inspect(a?: ref/[0 of i32])\n    require a.length == 0 else => $abort(\"length\")\n    for i in a.indices => $abort(\"iteration\")\nlet a: [0 of i32] = []\ninspect(a@ref)")]
    [InlineData("Typed", "func first(a?: ref/[2 of bool]) -> bool => a[0]\nlet a: [2 of bool] = [true, false]\nrequire first(a@ref/[2 of bool]) else => $abort(\"value\")")]
    [InlineData("Snapshot", "var a: [2 of i32] = [1, 2]\nlet b = a@ref\nlet indices = b.indices\na = [3, 4]\nvar total = 0\nfor i in indices => total += a[i]\nrequire total == 7 else => $abort(\"snapshot\")")]
    [InlineData("Exclusive", "func sum(a?: uniq/[2 of i32]) -> i32\n    var total = 0\n    for i in a.indices => total += a[i]\n    return total\nvar a: [2 of i32] = [20, 22]\nrequire sum(a@uniq) == 42 else => $abort(\"sum\")")]
    [InlineData("Forward", "func first(a?: ref/[1 of i32]) -> i32 => a[0]\nfunc forward(a?: ref/[1 of i32]) -> i32 => first(a)\nlet a: [1 of i32] = [42]\nrequire forward(a@ref) == 42 else => $abort(\"forward\")")]
    [InlineData("Temporary", "func first(a?: ref/[1 of i32]) -> i32 => a[0]\nfunc make() -> [1 of i32] => [42]\nrequire first(make()@ref) == 42 else => $abort(\"temporary\")")]
    [InlineData("Returned", "func view(a?: ref/[1 of i32]) -> ref{a}/[1 of i32] => a\nlet a: [1 of i32] = [42]\nrequire view(a@ref)[0] == 42 else => $abort(\"returned\")")]
    public void Executes(string name, string source)
        => ScalarEmissionTest.EmitFixture("BorrowedArray" + name, source, string.Empty);

    [Theory]
    [InlineData("var a: [1 of i32] = [1]\nlet b = a@ref\na[0] = 2\nlet n = b[0]")]
    [InlineData("var a: [1 of i32] = [1]\nlet b = a@ref\na = [2]\nlet n = b[0]")]
    [InlineData("let a: [1 of i32] = [1]\nlet b = a@ref\nb[0] = 2")]
    [InlineData("func escape() -> ref/[1 of i32]\n    let a: [1 of i32] = [1]\n    return a@ref")]
    [InlineData("let a: [1 of i32] = [1]\nlet b = a@ref/[2 of i32]")]
    [InlineData("let a: [1 of string] = [\"owned\"]\nlet b = a@ref\nConsole.writeLine(b[0])")]
    [InlineData("var a: [1 of i32] = [1]\nlet b = a@ref\nlet n = b[(work: do\n    a[0] = 2\n    exit to work: 0)]")]
    [InlineData("var a: [1 of i32] = [1]\nlet b = a@uniq\nlet r = b@ref\nlet c = b@uniq\nlet m = r[0]")]
    [InlineData("func make() -> [1 of i32] => [42]\nlet b = make()@ref\nlet n = b[0]")]
    [InlineData("let a: [1 of i32]\nlet b = a@ref\nlet n = b[0]")]
    public void Rejects(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
    }

    [Theory]
    [InlineData("Negative", "-1")]
    [InlineData("Length", "1")]
    [InlineData("Maximum", "9223372036854775807")]
    public void BoundsAbort(string name, string index)
        => ScalarEmissionTest.EmitFixture(
            "BorrowedArrayBounds" + name,
            "func at(a?: ref/[1 of i32]) -> i32 => a[" + index + "]\nlet a: [1 of i32] = [42]\nlet n = at(a@ref)",
            string.Empty,
            1,
            "Hello.kimi:1:38: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n");

    [Fact]
    public void RebindReloadAndWarmPlans()
    {
        var c = MinimalEmissionTest.Analyze("func first(a?: ref/[1 of i32]) -> i32 => a[0]\nlet a: [1 of i32] = [42]\nrequire first(a@ref/[1 of i32]) == 42 else => $abort(\"value\")");
        var bytes = TinyhandSerializer.Serialize(c.Kotonoha);
        c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        var tree = c.Kotonoha;
        TinyhandSerializer.DeserializeObject(bytes, ref tree);
        Assert.NotNull(tree);
        tree.OnDeserialized(c);
        for (var i = 0; i < 3; i++)
        {
            Assert.True(c.Bind().IsComplete);
            c.Binding.CheckStartup(OutputKind.Application);
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        for (var i = 0; i < 32; i++)
        {
            c.Ownership.Analyze();
            c.Emission.WriteIr(TextWriter.Null, out _);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 16; i++)
        {
            c.Ownership.Analyze();
            c.Emission.WriteIr(TextWriter.Null, out _);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void RejectsDifferentReferenceProducerAndReanalysisRecovers()
    {
        var c = MinimalEmissionTest.Analyze("let a: [1 of i32] = [42]\nlet r = a@ref\nlet n = r.length");
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies.Single();
        var sequence = body.Sequences.Single();
        var borrow = body.OperationStorage.FindIndex(x => x.Kind == OwnershipOperationKind.Borrow);
        body.ValueOperands[body.Values[sequence.Operation].Start] = borrow;
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out error), error);
    }

    [Fact]
    public void BorrowedGenericAbiRemainsGuarded()
    {
        var c = MinimalEmissionTest.Analyze("func identity<T>(x?: T) -> T => x\nlet a: [1 of i32] = [42]\nlet r = identity(a@ref)\nlet n = r[0]");
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
    }
}
