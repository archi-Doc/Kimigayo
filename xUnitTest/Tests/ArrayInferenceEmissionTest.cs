// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ArrayInferenceEmissionTest
{
    [Theory]
    [InlineData("Integers", "let row: [2 of _] = [1, 2]\nif row[1] == 2 => writeLine(\"ok\")")]
    [InlineData("Strings", "let row: [2 of _] = [\"first\", \"ok\"]\nwriteLine(row[1])")]
    [InlineData("Typed", "let x: u8 = 2\nlet row: [2 of _] = [1, x]\nif row[1] == 2 => writeLine(\"ok\")")]
    [InlineData("TypedFirst", "let x: u8 = 2\nlet row: [2 of _] = [x, 1]\nif row[0] == 2 => writeLine(\"ok\")")]
    [InlineData("Nested", "let matrix: [2 of [1 of _]] = [[1], [2]]\nif matrix[1][0] == 2 => writeLine(\"ok\")")]
    [InlineData("NestedEvidence", "let x: u8 = 2\nlet matrix: [2 of [1 of _]] = [[1], [x]]\nif matrix[1][0] == 2 => writeLine(\"ok\")")]
    [InlineData("Existing", "let source: [1 of string] = [\"ok\"]\nlet row: [1 of _] = source\nwriteLine(row[0])")]
    [InlineData("ExistingEmpty", "let source: [0 of string] = []\nlet row: [0 of _] = source\nwriteLine(\"ok\")")]
    [InlineData("Tuple", "let row: [1 of _] = [(\"ok\", 1)]\nwriteLine(row[0].0)")]
    [InlineData("Float", "let x: f32 = 2.0\nlet row: [2 of _] = [1.0, x]\nif row[1] == 2.0 => writeLine(\"ok\")")]
    [InlineData("Wide", "let x: u128 = 1\nlet row: [2 of _] = [340282366920938463463374607431768211455, x]\nif row[0] > row[1] => writeLine(\"ok\")")]
    [InlineData("Once", "func get() -> i32\n    writeLine(\"ok\")\n    return 1\nlet row: [1 of _] = [get()]")]
    [InlineData("Selection", "let first: [1 of string] = [\"ok\"]\nlet second: [1 of string] = [\"bad\"]\nlet row: [1 of _] = if true => first else => second\nwriteLine(row[0])")]
    [InlineData("Constants", "let N = 2\nlet row: [N of _] = [\"first\", \"ok\"]\nwriteLine(row[1])")]
    public void LocalArrayAnnotationsInferOneCompleteElementType(string name, string source)
        => ScalarEmissionTest.EmitFixture("ArrayInference" + name, source, "ok\n");

    [Theory]
    [InlineData("let row: [0 of _] = []")]
    [InlineData("let matrix: [0 of [1 of _]] = []")]
    [InlineData("let row: [2 of _] = [1]")]
    [InlineData("let matrix: [2 of [1 of _]] = [[1], [2, 3]]")]
    [InlineData("let row: [2 of _] = [1, true]")]
    [InlineData("let x: u8 = 1\nlet y: i32 = 2\nlet row: [2 of _] = [x, y]")]
    [InlineData("let x: u8 = 1\nlet row: [2 of _] = [256, x]")]
    [InlineData("let x: u8 = 1\nlet row: [2 of _] = [(1 + 2), x]")]
    [InlineData("let source: [2 of i32] = [1, 2]\nlet row: [1 of _] = source")]
    [InlineData("let row: [1 of _] = [null]")]
    public void InferenceDoesNotChangeEstablishedTypesOrArrayShapes(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("LiteralCleanup", "let row: [2 of _] = [\"first\", \"last\"]", "first=1;last=1", new[] { 1, 0 })]
    [InlineData("MovedCleanup", "let source: [2 of string] = [\"first\", \"last\"]\nlet row: [2 of _] = source", "first=1;last=1", new[] { 1, 0 })]
    [InlineData("PartialCleanup", "let row: [2 of _] = [\"first\", \"last\"]\nlet taken = row[0]", "first=1;last=1", new[] { 0, 1 })]
    public void InferenceRetainsOneDestructionResponsibility(string name, string source, string counts, int[] order)
    {
        var ir = ScalarEmissionTest.EmitFixture("ArrayInference" + name, source, string.Empty);
        StringEmissionTest.WriteAuditedFixture("ArrayInference" + name, source, ir, string.Empty, counts, order: order);
    }

    [Fact]
    public void InferredNonCopyArraysStillMoveTheirSource()
    {
        var c = MinimalEmissionTest.Analyze("let source: [1 of string] = [\"value\"]\nlet row: [1 of _] = source\nlet twice = source");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.PossiblyMovedUse);
        Assert.False(c.Emission.Validate(out _));
    }

    [Fact]
    public void RebindingAndReloadRecomputeArrayInference()
    {
        var c = MinimalEmissionTest.Analyze("let value: u8 = 2\nlet row: [2 of _] = [1, value]\nif row[0] == 1 => writeLine(\"ok\")");
        using var original = new StringWriter();
        Assert.True(c.Emission.WriteIr(original, out var error), error);
        var variable = c.Kotonoha.GeneratedFunction!.Body!.ChildNodes.OfType<VariableKoto>().Single(x => x.NameKoto.IdentifierName == "row");
        var array = Assert.IsType<FixedArrayTypeKoto>(variable.TypeKoto);
        array.ElementType.BoundType = BoundType.String;
        Assert.True(c.Bind().IsComplete);
        Assert.Same(BoundType.Primitives["u8"], array.ElementType.BoundType);
        c.Binding.CheckStartup(OutputKind.Application);
        c.Ownership.Analyze();
        using var rebound = new StringWriter();
        Assert.True(c.Emission.WriteIr(rebound, out error), error);
        Assert.Equal(original.ToString(), rebound.ToString());
        var bytes = Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha);
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var kotonoha = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(bytes, ref kotonoha);
        Assert.NotNull(kotonoha);
        kotonoha.OnDeserialized(restored);
        Assert.True(restored.Bind().IsComplete);
        restored.Binding.CheckStartup(OutputKind.Application);
        restored.Ownership.Analyze();
        using var writer = new StringWriter();
        Assert.True(restored.Emission.WriteIr(writer, out error), error);
        Assert.Equal(original.ToString(), writer.ToString());
        ScalarEmissionTest.WriteFixture("ArrayInferenceReload", writer.ToString(), "ok\n");
    }

    [Fact]
    public void WarmArrayInferenceAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze("let value: u8 = 2\nlet matrix: [2 of [1 of _]] = [[1], [value]]");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Array inference failed.");
            }
        }));
    }
}
