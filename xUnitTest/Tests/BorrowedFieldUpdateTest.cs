// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class BorrowedFieldUpdateTest
{
    private const string Counter = "struct Counter\n    public var value: i32 = 1\n";

    [Theory]
    [InlineData("TupleAssign", "func set(pair?: uniq/(i32, bool))\n    pair.0 = 42\n    pair.1 = false\nvar pair: (i32, bool) = (1, true)\nset(pair@uniq)\nrequire pair.0 == 42 and not pair.1 else => $abort(\"value\")")]
    [InlineData("TupleCompound", "func add(pair?: uniq/(i32, bool))\n    pair.0 += 41\nvar pair: (i32, bool) = (1, true)\nadd(pair@uniq)\nrequire pair.0 == 42 else => $abort(\"value\")")]
    [InlineData("StructCompound", Counter + "func add(counter?: uniq/Counter)\n    counter.value += 41\nvar counter = Counter.init()\nadd(counter@uniq)\nrequire counter.value == 42 else => $abort(\"value\")")]
    [InlineData("StructIncrement", Counter + "func add(counter?: uniq/Counter)\n    let before = counter.value++\n    let after = ++counter.value\n    require before == 1 and after == 3 else => $abort(\"result\")\nvar counter = Counter.init()\nadd(counter@uniq)\nrequire counter.value == 3 else => $abort(\"value\")")]
    [InlineData("TupleDecrement", "func change(pair?: uniq/(i32, bool))\n    let before = pair.0--\n    let after = --pair.0\n    require before == 42 and after == 40 else => $abort(\"result\")\nvar pair: (i32, bool) = (42, true)\nchange(pair@uniq)\nrequire pair.0 == 40 else => $abort(\"value\")")]
    [InlineData("TupleOperators", "func change(pair?: uniq/(i32, f32))\n    pair.0 *= 3\n    pair.0 -= 2\n    pair.0 /= 2\n    pair.0 %= 5\n    pair.0 <<= 2@u8\n    pair.0 >>= 1@u16\n    pair.1 += 0.5\n    pair.1 *= 2.0\nvar pair: (i32, f32) = (8, 1.5)\nchange(pair@uniq)\nrequire pair.0 == 2 and pair.1 == 4.0 else => $abort(\"value\")")]
    [InlineData("RhsReturn", Counter + "func change(counter?: uniq/Counter) -> i32\n    counter.value += (return 7)\n    return 0\nvar counter = Counter.init()\nrequire change(counter@uniq) == 7 and counter.value == 1 else => $abort(\"transfer\")")]
    [InlineData("AssignRhsReturn", Counter + "func change(counter?: uniq/Counter) -> i32\n    counter.value = (return 7)\n    return 0\nvar counter = Counter.init()\nrequire change(counter@uniq) == 7 and counter.value == 1 else => $abort(\"transfer\")")]
    [InlineData("Snapshot", "func change(pair?: uniq/(i32, bool))\n    let previous = pair.0\n    pair.0 += previous\nvar pair: (i32, bool) = (21, true)\nchange(pair@uniq)\nrequire pair.0 == 42 else => $abort(\"value\")")]
    [InlineData("TupleOwnedCleanup", "func change(pair?: uniq/(i32, string))\n    pair.0 += 41\nvar pair: (i32, string) = (1, \"owned\")\nchange(pair@uniq)\nrequire pair.0 == 42 else => $abort(\"value\")\nlet text = pair.1")]
    public void ExecutesExclusiveScalarUpdates(string name, string source)
        => ScalarEmissionTest.EmitFixture("BorrowedFieldUpdate" + name, source, string.Empty);

    [Fact]
    public void EvaluatesReceiverOnceInRequiredOrder()
    {
        const string Source = Counter + "func locate(p?: uniq/Counter) -> uniq{p}/Counter\n    Console.writeLine(\"receiver\")\n    return p\nfunc amount() -> i32\n    Console.writeLine(\"rhs\")\n    return 2\nvar counter = Counter.init()\nlocate(counter@uniq).value = amount()\nlocate(counter@uniq).value += amount()\nlet before = locate(counter@uniq).value++\nrequire before == 4 else => $abort(\"result\")\nrequire counter.value == 5 else => $abort(\"value\")";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Ownership.Result.IsVerified, string.Join('\n', c.Ownership.Issues));
        ScalarEmissionTest.EmitFixture("BorrowedFieldUpdateOrder", Source, "rhs\nreceiver\nreceiver\nrhs\nreceiver\n");
    }

    [Theory]
    [InlineData("func change(pair?: ref/(i32, bool))\n    pair.0 += 1")]
    [InlineData("func change(pair?: ref/(i32, bool))\n    pair.0++")]
    [InlineData("struct Counter\n    public let value: i32 = 1\nfunc change(p?: uniq/Counter)\n    p.value += 1")]
    [InlineData("func change(pair?: uniq/(f32, bool))\n    pair.0++")]
    public void RejectsNonWritableOrNonIntegerIncrement(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Theory]
    [InlineData("var pair: (i32, bool) = (1, true)\nlet r = pair@uniq\nr.0 += (work: do\n    pair.0 = 9\n    exit to work: 1)")]
    [InlineData("func change(pair?: uniq/(i32, bool))\n    pair.0 += pair.0")]
    [InlineData(Counter + "func change(counter?: uniq/Counter)\n    counter.value += counter.value")]
    public void KeepsTargetBorrowedAcrossRhs(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.NotEmpty(c.Ownership.Issues);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Theory]
    [InlineData("Increment", "pair.0++")]
    [InlineData("Compound", "pair.0 += 1")]
    public void OverflowAbortsWithoutCleanup(string name, string expression)
    {
        var source = "func change(pair?: uniq/(u8, string))\n    " + expression + "\nvar pair: (u8, string) = (255, \"held\")\ndefer => Console.writeLine(\"bad\")\nchange(pair@uniq)";
        const string Error = "Hello.kimi:2:5: abort KIMI_E_INT_OVERFLOW: Integer overflow\n";
        var ir = ScalarEmissionTest.EmitFixture("BorrowedFieldUpdateOverflow" + name, source, string.Empty, 1, Error);
        StringEmissionTest.WriteAuditedFixture("BorrowedFieldUpdateOverflow" + name, source, ir, string.Empty, "held=0;bad=0", 1, Error);
    }

    [Fact]
    public void RebindingAndReloadRetainUpdates()
    {
        var c = MinimalEmissionTest.Analyze("func change(pair?: uniq/(i32, bool))\n    pair.0 += 1\n    pair.0++\nvar pair: (i32, bool) = (40, true)\nchange(pair@uniq)\nrequire pair.0 == 42 else => $abort(\"value\")");
        using var original = new StringWriter();
        Assert.True(c.Emission.WriteIr(original, out var error), error);
        var bytes = TinyhandSerializer.Serialize(c.Kotonoha);
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var tree = restored.Kotonoha;
        TinyhandSerializer.DeserializeObject(bytes, ref tree);
        Assert.NotNull(tree);
        tree.OnDeserialized(restored);
        for (var i = 0; i < 2; i++)
        {
            Assert.True(restored.Bind().IsComplete);
            Assert.True(restored.Binding.CheckStartup(OutputKind.Application).IsComplete);
            Assert.True(restored.Ownership.Analyze().IsVerified);
            using var output = new StringWriter();
            Assert.True(restored.Emission.WriteIr(output, out error), error);
            Assert.Equal(original.ToString(), output.ToString());
        }
    }
}
