// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

#pragma warning disable SA1117, SA1118 // Multiline language fixtures.

/// <summary>SPEC 4.3.1: only the fixed-array structure written in a local annotation constrains its initializer.</summary>
public class ArrayAnnotationHoleTest
{
    [Theory]
    [InlineData("Length", "let values: [_ of i64] = [1, 2, 3]\nrequire values[2] == 3 else => $abort(\"value\")")]
    [InlineData("Both", "let value: u8 = 2\nlet values: [_ of _] = [1, value]\nrequire values[1] == 2 else => $abort(\"value\")")]
    [InlineData("Empty", "let values: [_ of i32] = []\nrequire values.length == 0 else => $abort(\"length\")")]
    [InlineData("Nested", "let values: [_ of [_ of f32]] = [[1.0, 2.0], [3.0, 4.0]]\nrequire values[1][0] == 3.0 else => $abort(\"value\")")]
    [InlineData("NestedBoth", "let value: u8 = 2\nlet values: [_ of [_ of _]] = [[1], [value]]\nrequire values[1][0] == 2 else => $abort(\"value\")")]
    [InlineData("DynamicRows", "let values: [_ of _] = [[1], [2, 3]]\nrequire values[1].length == 2 else => $abort(\"shape\")")]
    [InlineData("Fill", "let values: [_ of i64] = [3 of 2]\nrequire values.length == 3 and values[2] == 2 else => $abort(\"fill\")")]
    [InlineData("Typed", "let original: [2 of string] = [\"a\", \"b\"]\nlet values: [_ of _] = original@move\nrequire values.length == 2 else => $abort(\"move\")")]
    [InlineData("ExpectedResult", "func zeros<T>() -> [3 of T]\n    T is PrimitiveInteger\n    return [3 of 0]\nlet values: [_ of i64] = zeros()\nrequire values[2] == 0 else => $abort(\"call\")")]
    [InlineData("ExpectedNestedResult", "func zeros<T>() -> [2 of [3 of T]]\n    T is PrimitiveInteger\n    return [2 of [3 of 0]]\nlet values: [_ of [_ of i64]] = zeros()\nrequire values[1][2] == 0 else => $abort(\"call\")")]
    [InlineData("Selection", "func zeros<T>() -> [3 of T]\n    T is PrimitiveInteger\n    return [3 of 0]\nlet values: [_ of i64] = if true => zeros() else => zeros()\nrequire values[2] == 0 else => $abort(\"selection\")")]
    [InlineData("Symbolic", "func copy<length N>(input: [N of i32]) -> [N of i32]\n    let values: [_ of i32] = input\n    return values\nlet result = copy([1, 2])\nrequire result[1] == 2 else => $abort(\"symbolic\")")]
    [InlineData("ReferenceRead", "let original: [2 of i32] = [1, 2]\nlet reference = original@ref\nlet values: [_ of i32] = reference@follow\nrequire values[1] == 2 else => $abort(\"read\")")]
    [InlineData("InnerOrigins", "let a = 1\nlet b = 2\nlet values: [_ of ref/i32] = [a@ref, b@ref]\nrequire values[1] == 2 else => $abort(\"origins\")")]
    [InlineData("ArgumentEvidence", "func identity<T>(value: T) -> T => value@move\nlet original: [2 of i32] = [1, 2]\nlet values: [_ of i32] = identity(original)\nrequire values[1] == 2 else => $abort(\"argument\")")]
    [InlineData("NestedBorrow", "let values: [_ of _] = [[1], [2, 3]]\nlet tail = values[1][..]\nrequire tail[1] == 3 else => $abort(\"view\")")]
    [InlineData("ConditionalShape", "let values: [_ of [_ of _]] = if true => [[1, 2]] else => [[3, 4]]\nrequire values[0][1] == 2 else => $abort(\"shape\")")]
    [InlineData("MatchShape", "let value: u8 = 7\nlet values: [_ of [_ of _]] = match value\n    let found => [[1, found@follow]]\nrequire values[0][1] == 7 else => $abort(\"pattern\")")]
    [InlineData("BlockShape", "let values: [_ of [_ of _]] = label result: do\n    let value: u8 = 7\n    exit to result [[1, value]]\nrequire values[0][1] == 7 else => $abort(\"block\")")]
    [InlineData("NestedSelectionShape", "let value: u8 = 7\nlet values: [_ of [_ of _]] = if true => match value\n    let found => [[1, found@follow]]\nelse => [[2, value]]\nrequire values[0][1] == 7 else => $abort(\"nested\")")]
    [InlineData("ConditionalDynamicRows", "let values: [_ of _] = if true => [[1], [2, 3]] else => [[4], [5, 6]]\nrequire values[1].length == 2 else => $abort(\"dynamic\")")]
    [InlineData("ConditionalNumericEvidence", "let value: u8 = 7\nlet values: [_ of _] = if true => [1, 2] else => [value, 3]\nlet typed: [2 of u8] = values\nrequire typed[1] == 2 else => $abort(\"type\")")]
    [InlineData("ConditionalEmpty", "let known: [0 of i64] = []\nlet values: [_ of _] = if true => [] else => known\nrequire values.length == 0 else => $abort(\"empty\")")]
    [InlineData("LoopShape", "let values: [_ of _] = label result: loop\n    let value: u8 = 7\n    exit to result [1, value]\nrequire values[1] == 7 else => $abort(\"loop\")")]
    [InlineData("NeverArm", "let values: [_ of _] = if true => [1, 2] else => $abort(\"never\")\nrequire values[1] == 2 else => $abort(\"value\")")]
    public void WrittenShapePropagatesAndHolesUseInitializerEvidence(string name, string source)
        => ScalarEmissionTest.EmitFixture("ArrayAnnotationHole" + name, source + "\nConsole.writeLine(\"ok\")", "ok\n");

    [Theory]
    [InlineData("let values: [_ of _] = []")]
    [InlineData("let values: [_ of [_ of i32]] = []")]
    [InlineData("let values: [_ of [_ of _]] = [[1], [2, 3]]")]
    [InlineData("let values: [_ of [2 of i32]] = [[1], [2]]")]
    [InlineData("let dynamic = [1, 2]\nlet values: [_ of i32] = dynamic@move")]
    [InlineData("let values: [_ of i32]")]
    [InlineData("func take(values: [_ of i32]) => ()\ntake([1])")]
    [InlineData("struct S\n    public let values: [_ of i32]\nlet value = 1")]
    [InlineData("group G\n    public let values: [_ of i32] = [1]\nlet value = 1")]
    [InlineData("func identity<T>(value: T) -> T => value@move\nlet values = identity<[_ of i32]>([1])")]
    [InlineData("func zeros<length N>() -> [(N + 1) of i32] => [(N + 1) of 0]\nlet values: [_ of i32] = zeros()")]
    [InlineData("func unknown<T>() -> T => $abort(\"unknown\")\nlet values: [_ of i32] = unknown()")]
    [InlineData("func zeros<T>() -> [3 of T]\n    T is PrimitiveInteger\n    return [3 of 0]\nlet values: [_ of bool] = zeros()")]
    [InlineData("let values: [_ of i32] = if true => [2 of 0] else => [3 of 0]")]
    [InlineData("let values: [_ of [_ of i32]] = if true => [] else => []")]
    [InlineData("let original: [2 of i32] = [1, 2]\nlet reference = original@ref\nlet values: [_ of i32] = reference")]
    public void MissingOrConflictingEvidenceAndForbiddenPositionsAreRejected(string source)
    {
        var compilation = MinimalEmissionTest.Analyze(source);
        using var output = new StringWriter();
        Assert.False(compilation.Emission.WriteIr(output, out _));
    }

    [Theory]
    [InlineData("let values: [_ of [_ of i64]] = [[1], [2]]")]
    [InlineData("let values: [_ of [_ of _]] = if true => [[1], [2]] else => [[3], [4]]")]
    public void RebindingAndReloadPreserveWrittenHolesAndRecomputeTheirTypes(string source)
    {
        var compilation = MinimalEmissionTest.Analyze(source);
        var variable = compilation.Kotonoha.GeneratedFunction!.Body!.ChildNodes.OfType<VariableKoto>().Single();
        var shape = Assert.IsType<FixedArrayTypeKoto>(variable.TypeKoto);
        Assert.Equal(2, shape.BoundType!.Length);
        Assert.Equal(1, shape.BoundType.Components[0].Length);
        Assert.True(compilation.Bind().IsComplete);
        var reloaded = CompilationTestHelper.Reload(compilation);
        Assert.True(reloaded.Bind().IsComplete);
        Assert.Equal(source.Replace(" else", "\nelse", StringComparison.Ordinal), variable.ToString());
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("let value: u8 = 2\nlet values: [_ of [_ of _]] = [[1], [value]]")]
    [InlineData("let values: [_ of [_ of _]] = label result: do\n    let value: u8 = 2\n    exit to result if true => [[1], [value]] else => [[value], [1]]")]
    public void WarmNestedHoleInferenceAllocatesNothing(string source)
    {
        var compilation = MinimalEmissionTest.Analyze(source);
        for (var i = 0; i < 100; i++)
        {
            Assert.True(compilation.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!compilation.Bind().IsComplete)
            {
                throw new InvalidOperationException("Array annotation inference failed.");
            }
        }));
    }
}
