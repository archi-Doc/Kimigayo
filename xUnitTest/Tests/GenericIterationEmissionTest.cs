// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class GenericIterationEmissionTest
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    public void WalksAllIndices(int length)
        => ScalarEmissionTest.EmitFixture(
            "GenericIteration" + length,
            "func count<length N, T>(values?: ref/[N of T]) -> isize\n    var count: isize = 0\n    for index in values.indices\n        count = count + 1\n    return count\nlet values: [" + length + " of i32] = [" + string.Join(", ", Enumerable.Repeat("7", length)) + "]\nif count<" + length + ", i32>(values@ref) != " + length + "\n    $abort(\"count\")",
            string.Empty);

    [Fact]
    public void SharedScalarReadsRetainSnapshots()
        => ScalarEmissionTest.EmitFixture("GenericIterationSnapshot", "func snapshot<T>(x?: T) -> isize\n    var n: isize = 1\n    let value = n + n++\n    return value\nrequire snapshot<i32>(7) == 2 else => $abort(\"snapshot\")", string.Empty);

    [Fact]
    public void SharedAdditionChecksOverflow()
        => ScalarEmissionTest.EmitFixture("GenericIterationOverflow", "func increment<T>(x?: T, n?: isize) -> isize => n + 1\nlet value = increment<i32>(7, 9223372036854775807)", string.Empty, 1, "Hello.kimi:1:47: abort KIMI_E_INT_OVERFLOW: Integer overflow\n");

    [Theory]
    [InlineData("endpoint")]
    [InlineData("operator")]
    [InlineData("operand")]
    public void RejectsCorruptIterationAndRecovers(string defect)
    {
        var c = MinimalEmissionTest.Analyze("func first<length N, T>(values?: ref/[N of T]) -> isize\n    for index in values.indices\n        return index\n    return values.length\nlet values: [1 of i32] = [7]\nlet result = first<1, i32>(values@ref)");
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies.Single(x => x.Function.Name == "first");
        if (defect == "endpoint")
        {
            var index = body.Sequences.FindIndex(x => x.Kind == SequenceOperation.Start);
            body.Sequences[index] = body.Sequences[index] with { Receiver = 0 };
        }
        else
        {
            var id = body.Values.FindIndex(x => x.Kind == OwnershipValueKind.Binary);
            if (defect == "operator")
            {
                body.Values[id] = body.Values[id] with { Operator = KotoKind.Asterisk };
            }
            else
            {
                body.ValueOperands[body.Values[id].Start] = id;
            }
        }

        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.Validate(out error), error);
    }
}
