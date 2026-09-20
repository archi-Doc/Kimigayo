// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class BorrowedTupleProjectionTest
{
    private const string Counter = "struct Counter\n    public var value: i32 = 1\n";

    [Theory]
    [InlineData("Struct", Counter + "func read(pair?: ref/(Counter, bool)) -> i32\n    let item = pair.0@ref\n    return item.value\nlet pair = (Counter.init(), true)\nrequire read(pair@ref) == 1 else => $abort(\"value\")")]
    [InlineData("Tuple", "func read(pair?: ref/((i32, bool), bool)) -> i32\n    let item = pair.0@ref\n    return item.0\nlet pair: ((i32, bool), bool) = ((42, true), false)\nrequire read(pair@ref) == 42 else => $abort(\"value\")")]
    [InlineData("Array", "func read(pair?: ref/([2 of i32], bool)) -> i32\n    let item = pair.0@ref\n    return item[0] + item[1]\nlet pair: ([2 of i32], bool) = ([20, 22], true)\nrequire read(pair@ref) == 42 else => $abort(\"value\")")]
    [InlineData("Exclusive", Counter + "func change(pair?: uniq/(Counter, bool))\n    let item = pair.0@uniq\n    item.value += 41\nvar pair = (Counter.init(), true)\nchange(pair@uniq)\nrequire pair.0.value == 42 else => $abort(\"value\")")]
    [InlineData("Returned", Counter + "func first(pair?: ref/(Counter, bool)) -> ref{pair}/Counter => pair.0@ref\nlet pair = (Counter.init(), true)\nlet item = first(pair@ref)\nrequire item.value == 1 else => $abort(\"value\")")]
    [InlineData("Implicit", Counter + "func value(item?: ref/Counter) -> i32 => item.value\nfunc read(pair?: ref/(Counter, bool)) -> i32 => value(pair.0)\nrequire read((Counter.init(), true)) == 1 else => $abort(\"value\")")]
    [InlineData("Padding", "func read(pair?: ref/(bool, (i64, i32), u8)) -> i64\n    let item = pair.1@ref\n    return item.0\nlet pair: (bool, (i64, i32), u8) = (true, (42, 7), 255)\nrequire read(pair@ref) == 42 else => $abort(\"offset\")")]
    [InlineData("LastUse", Counter + "func change(pair?: uniq/(Counter, bool))\n    let item = pair.0@uniq\n    item.value += 41\n    pair.1 = false\nvar pair = (Counter.init(), true)\nchange(pair@uniq)\nrequire pair.0.value == 42 and not pair.1 else => $abort(\"value\")")]
    [InlineData("StoredReference", Counter + "let counter = Counter.init()\nlet pair = (counter@ref, true)\nlet view = pair@ref\nlet item = view.0@ref\nrequire item.value == 1 else => $abort(\"value\")")]
    [InlineData("StoredReferenceOutlivesWrapper", Counter + "let counter = Counter.init()\nlet item = work: do\n    let pair = (counter@ref, true)\n    let view = pair@ref\n    exit to work: view.0@ref\nrequire item.value == 1 else => $abort(\"value\")")]
    public void ExecutesStoredElementBorrows(string name, string source)
        => ScalarEmissionTest.EmitFixture("BorrowedTupleProjection" + name, source, string.Empty);

    [Theory]
    [InlineData(Counter + "func change(pair?: ref/(Counter, bool))\n    let item = pair.0@uniq\n    item.value = 42")]
    [InlineData("func change(pair?: ref/((i32, bool), bool))\n    let item = pair.0@uniq\n    item.0 = 42")]
    [InlineData(Counter + "var counter = Counter.init()\nlet pair = (counter@ref, true)\nlet view = pair@ref\nlet item = view.0@uniq\nitem.value = 42")]
    [InlineData(Counter + "var counter = Counter.init()\nlet pair = (counter@uniq, true)\nlet view = pair@ref\nlet item = view.0@uniq\nitem.value = 42")]
    [InlineData(Counter + "func bump(c?: uniq/Counter)\n    c.value += 1\nvar counter = Counter.init()\nlet pair = (counter@uniq, true)\nlet view = pair@ref\nbump(view.0)")]
    public void RejectsExclusiveProjectionThroughSharedReceiver(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Fact]
    public void RetainsOwnerLifetime()
    {
        var c = MinimalEmissionTest.Analyze(Counter + "var pair = (Counter.init(), true)\nlet view = pair@ref\nlet item = view.0@ref\npair = (Counter.init(), false)\nlet value = item.value");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Theory]
    [InlineData(Counter + "func first(pair?: ref/(Counter, bool)) -> ref{pair}/Counter => pair.0@ref\nlet item = first((Counter.init(), true))\nlet value = item.value")]
    [InlineData(Counter + "func bad(pair?: uniq/(Counter, bool))\n    let item = pair.0@ref\n    let other = pair.0@uniq\n    other.value = 9\n    let value = item.value")]
    [InlineData(Counter + "func bad(pair?: ref/(Counter, bool))\n    let moved = pair.0")]
    [InlineData(Counter + "var counter = Counter.init()\nlet pair = (counter@ref, true)\nlet view = pair@ref\nlet item = view.0@ref\ncounter = Counter.init()\nlet value = item.value")]
    public void RejectsEscapesConflictsAndImplicitMoves(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Fact]
    public void ProjectionDoesNotDestroyTheParent()
    {
        const string Source = "struct Counter\n    public var value: i32 = 42\n    deinit => Console.writeLine(\"drop\")\nfunc read(pair?: ref/(Counter, bool)) -> i32\n    let item = pair.0@ref\n    return item.value\nlet pair = (Counter.init(), true)\nrequire read(pair@ref) == 42 else => $abort(\"value\")\nConsole.writeLine(\"read\")";
        ScalarEmissionTest.EmitFixture("BorrowedTupleProjectionCleanup", Source, "read\ndrop\n");
    }

    [Fact]
    public void ReloadAndWarmAnalysisPreserveLocalReborrowAncestry()
    {
        const string Source = "func change(pair?: uniq/((i32, bool), bool))\n    let item = pair.0@uniq\n    item.0 += 41\n    pair.1 = false\nvar pair: ((i32, bool), bool) = ((1, true), true)\nchange(pair@uniq)\nrequire pair.0.0 == 42 and not pair.1 else => $abort(\"value\")";
        var original = ScalarEmissionTest.EmitFixture("BorrowedTupleProjectionNestedUpdate", Source, string.Empty);
        var c = MinimalEmissionTest.Analyze(Source);
        var bytes = TinyhandSerializer.Serialize(c.Kotonoha);
        c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        var tree = c.Kotonoha;
        TinyhandSerializer.DeserializeObject(bytes, ref tree);
        Assert.NotNull(tree);
        tree.OnDeserialized(c);
        for (var i = 0; i < 2; i++)
        {
            Assert.True(c.Bind().IsComplete);
            Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
            Assert.True(c.Ownership.Analyze().IsVerified);
            using var output = new StringWriter();
            Assert.True(c.Emission.WriteIr(output, out var error), error);
            Assert.Equal(original, output.ToString());
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
}
