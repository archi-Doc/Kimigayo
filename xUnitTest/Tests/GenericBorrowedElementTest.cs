// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class GenericBorrowedElementTest
{
    private const string At = "func at<length N, T>(a: ref/[N of T], i: isize) -> T\n    T is Copy\n    return a[i]\n";

    [Theory]
    [InlineData("Integer", "i32", "6, 7", "require n == 7 else => $abort(\"value\")")]
    [InlineData("Wide", "u128", "0, 340282366920938463463374607431768211455", "require n == 340282366920938463463374607431768211455 else => $abort(\"wide\")")]
    [InlineData("Tuple", "(i32, bool)", "(6, false), (7, true)", "match n\n    (7, true) => ()\n    (_, _) => $abort(\"tuple\")")]
    [InlineData("Unit", "()", "(), ()", "let unit: () = n")]
    public void Executes(string name, string type, string values, string check)
        => ScalarEmissionTest.EmitFixture("GenericBorrowedElement" + name, At + $"let a: [2 of {type}] = [{values}]\nlet n = at<2, {type}>(a@ref, 1)\n{check}", string.Empty);

    [Theory]
    [InlineData("func at<length N, T>(a: ref/[N of T], i: isize) -> T => a[i]")]
    [InlineData("let a: [1 of string] = [\"owned\"]\nlet n = at<1, string>(a@ref, 0)")]
    public void RejectsUnprovedCopy(string source)
    {
        var c = MinimalEmissionTest.Analyze(source.StartsWith("func", StringComparison.Ordinal) ? source : At + source);
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
    }

    [Theory]
    [InlineData("Negative", 2, -1)]
    [InlineData("Length", 2, 2)]
    [InlineData("Empty", 0, 0)]
    public void BoundsChecksIncludeZeroSizedElements(string name, int length, int index)
        => ScalarEmissionTest.EmitFixture(
            "GenericBorrowedElementBounds" + name,
            At + $"let a: [{length} of ()] = [{(length == 0 ? string.Empty : "(), ()")}]\nlet n = at<{length}, ()>(a@ref, {index})",
            string.Empty,
            1,
            "Hello.kimi:3:12: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n");

    [Fact]
    public void MonomorphizesAggregateElementReads()
    {
        // SPEC 21.3.1: the (i32, bool) instance copies the proved-Copy element from the borrowed array's
        // element storage instead of keeping the shared entry.
        var c = MinimalEmissionTest.Analyze(At + "let a: [2 of (i32, bool)] = [(6, false), (7, true)]\nlet n = at<2, (i32, bool)>(a@ref, 1)");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), MinimalEmissionTest.Describe(c, error));
        Assert.Empty(module.SharedEntries);
        var instance = Assert.Single(GenericStorageEmissionTest.Instances(module));
        Assert.Contains(instance.Instructions, x => x.Opcode == EmissionOpcode.Sequence && x.ScalarOperator == "ArrayStorageRead");
        Assert.Contains(instance.Instructions, x => x.Opcode == EmissionOpcode.TransferAggregate);
    }

    [Fact]
    public void RejectsUncheckedElementProducerAndRecoversAfterReload()
    {
        var c = MinimalEmissionTest.Analyze(At + "let a: [1 of i32] = [7]\nlet n = at<1, i32>(a@ref, 0)");
        using var first = new StringWriter();
        Assert.True(c.Emission.WriteIr(first, out var error), error);
        var bytes = Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha);
        var body = c.Ownership.Bodies.Single(x => x.Sequences.Count != 0);
        body.Sequences[0] = body.Sequences[0] with { Index = 0 };
        using var invalid = new StringWriter();
        Assert.False(c.Emission.WriteIr(invalid, out _));
        Assert.Empty(invalid.ToString());
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var tree = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(bytes, ref tree);
        Assert.NotNull(tree);
        tree.OnDeserialized(restored);
        Assert.True(restored.Bind().IsComplete);
        restored.Binding.CheckStartup(OutputKind.Application);
        Assert.True(restored.Ownership.Analyze().IsVerified);
        using var second = new StringWriter();
        Assert.True(restored.Emission.WriteIr(second, out error), error);
        Assert.Equal(first.ToString(), second.ToString());
    }
}
