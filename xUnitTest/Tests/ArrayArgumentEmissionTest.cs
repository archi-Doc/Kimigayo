// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ArrayArgumentEmissionTest
{
    [Theory]
    [InlineData("Integer", "func use(x: [2 of i32]) -> i32 => x[1]\nif use([1, 2]) == 2 => Console.writeLine(\"ok\")")]
    [InlineData("Byte", "func use(x: [1 of u8]) -> u8 => x[0]\nif use([255]) == 255 => Console.writeLine(\"ok\")")]
    [InlineData("Empty", "func use(x: [0 of string]) => Console.writeLine(\"ok\")\nuse([])")]
    [InlineData("Nested", "func use(x: [1 of [1 of string]]) => Console.writeLine(x[0][0])\nuse([[\"ok\"]])")]
    [InlineData("Tuple", "func use(x: [1 of (u8, string)]) => Console.writeLine(x[0].1)\nuse([(255, \"ok\")])")]
    [InlineData("Named", "func use(first: [1 of i32], second: [1 of string]) => Console.writeLine(second[0])\nuse(second: [\"ok\"], first: [1])")]
    [InlineData("ShapeOverload", "func use(x: [0 of i32]) => Console.writeLine(\"wrong\")\nfunc use(x: [1 of i32]) => Console.writeLine(\"ok\")\nuse([1])")]
    [InlineData("RangeOverload", "func use(x: [1 of i8]) => Console.writeLine(\"wrong\")\nfunc use(x: [1 of u8]) => Console.writeLine(\"ok\")\nuse([255])")]
    [InlineData("RangeReversed", "func use(x: [1 of u8]) => Console.writeLine(\"ok\")\nfunc use(x: [1 of i8]) => Console.writeLine(\"wrong\")\nuse([255])")]
    [InlineData("Float", "func use(x: [1 of f32]) -> f32 => x[0]\nif use([1.5]) == 1.5 => Console.writeLine(\"ok\")")]
    [InlineData("Wide", "func use(x: [1 of u128]) -> u128 => x[0]\nif use([340282366920938463463374607431768211455]) > 0 => Console.writeLine(\"ok\")")]
    [InlineData("Unit", "func use(x: [2 of ()]) => Console.writeLine(\"ok\")\nuse([(), ()])")]
    [InlineData("TypedNested", "func use(x: [1 of [1 of u8]]) => Console.writeLine(\"ok\")\nlet row: [1 of u8] = [1]\nuse([row])")]
    public void FixedParameterTypesFitArrayLiterals(string name, string source)
        => ScalarEmissionTest.EmitFixture("ArrayArgument" + name, source, "ok\n");

    [Theory]
    [InlineData("func use(x: [1 of i32]) => ()\nuse([])")]
    [InlineData("func use(x: [1 of u8]) => ()\nlet n: i32 = 1\nuse([n])")]
    [InlineData("func use(x: [1 of u8]) => ()\nuse([1 + 2])")]
    [InlineData("func use(x: [1 of i32]) => ()\nfunc use(x: [1 of i64]) => ()\nuse([1])")]
    [InlineData("func use(x: [0 of i32]) => ()\nfunc use(x: [0 of string]) => ()\nuse([])")]
    [InlineData("func use(x: [1 of i32]) => ()\nuse([null])")]
    [InlineData("func use(x: [1 of [1 of i32]]) => ()\nuse([[1, 2]])")]
    [InlineData("func inner(x: i32) -> i32 => x\nfunc inner(x: i64) -> i64 => x\nfunc use(x: [1 of i32]) => ()\nfunc use(x: [1 of i64]) => ()\nuse([inner(1)])")]
    public void CandidateFittingPreservesShapeEstablishedTypesAndAmbiguity(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("Drop", "func use(x: [2 of string]) => ()\nuse([\"first\", \"last\"])", "first=1;last=1", new[] { 1, 0 })]
    [InlineData("Partial", "func take(x: [2 of string]) -> string => x[0]\nlet result = take([\"first\", \"last\"])", "first=1;last=1", new[] { 1, 0 })]
    [InlineData("Move", "func use(x: [1 of string]) => ()\nlet value = \"moved\"\nuse([value])", "moved=1", new[] { 0 })]
    public void ArrayArgumentsRetainOneCleanupResponsibility(string name, string source, string counts, int[] order)
    {
        var ir = ScalarEmissionTest.EmitFixture("ArrayArgument" + name, source, string.Empty);
        StringEmissionTest.WriteAuditedFixture("ArrayArgument" + name, source, ir, string.Empty, counts, order: order);
    }

    [Fact]
    public void ExplicitArgumentsAndElementsExecuteInSourceOrder()
        => ScalarEmissionTest.EmitFixture("ArrayArgumentOrder", "func mark(x: string) -> i32\n    Console.writeLine(x)\n    return 1\nfunc use(first: [1 of i32], second: [2 of i32]) => Console.writeLine(\"body\")\nuse(second: [mark(\"one\"), mark(\"two\")], first: [mark(\"three\")])", "one\ntwo\nthree\nbody\n");

    [Fact]
    public void CandidateProbesDoNotConsumeOwnedElements()
    {
        var c = MinimalEmissionTest.Analyze("func use(x: [1 of string]) => ()\nfunc use(x: [1 of i32]) => ()\nlet value = \"moved\"\nuse([value])\nConsole.writeLine(value)");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.PossiblyMovedUse);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("func use<T>(x: T, values: [1 of T]) => ()\nuse(1, [2])")]
    [InlineData("func use<T>(values: [1 of T], x: T) => ()\nuse([2], 1)")]
    [InlineData("func use<T>(values: [1 of T]) => ()\nuse<u8>([255])")]
    public void OtherEvidenceCanFixGenericElementTypesBeforeProbing(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void ReloadAndRebindPreserveTheSelectedLiteralType()
    {
        const string Source = "func use(x: [1 of i8]) => Console.writeLine(\"wrong\")\nfunc use(x: [1 of u8]) => Console.writeLine(\"ok\")\nuse([255])";
        var c = MinimalEmissionTest.Analyze(Source);
        using var original = new StringWriter();
        Assert.True(c.Emission.WriteIr(original, out var error), error);
        var bytes = Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha);
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var kotonoha = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(bytes, ref kotonoha);
        Assert.NotNull(kotonoha);
        kotonoha.OnDeserialized(restored);
        for (var i = 0; i < 2; i++)
        {
            Assert.True(restored.Bind().IsComplete);
            restored.Binding.CheckStartup(OutputKind.Application);
            restored.Ownership.Analyze();
            using var output = new StringWriter();
            Assert.True(restored.Emission.WriteIr(output, out error), error);
            Assert.Equal(original.ToString(), output.ToString());
        }

        ScalarEmissionTest.EmitFixture("ArrayArgumentReload", Source, "ok\n");
    }

    [Fact]
    public void WarmCandidateProbingAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze("func use(x: [1 of (u8, string)]) => ()\nfunc use(x: [1 of (i8, string)]) => ()\nuse([(255, \"value\")])");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Array argument Binding failed.");
            }
        }));
    }
}
