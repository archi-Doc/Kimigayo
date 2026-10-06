// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class SliceToArrayTest
{
    [Theory]
    [InlineData("Fixed", "var values: [3 of i32] = [1, 2, 3]")]
    [InlineData("Dynamic", "var values = [1, 2, 3]")]
    public void CopiesReleaseTheOuterLoanAndKeepIndependentStorage(string name, string declaration)
        => ScalarEmissionTest.EmitFixture("SliceToArray" + name, declaration + "\nvar copied = values.slice(1..).toArray()\nvalues[1] = 9\ncopied[1] = 8\nrequire copied.length == 2 and copied[0] == 2 and values[2] == 3 else => $abort(\"copy\")", string.Empty);

    [Theory]
    [InlineData("Zero", "let values: [3 of ()] = [(), (), ()]\nlet copied = values.slice().toArray()\nrequire copied.length == 3 else => $abort(\"zero\")")]
    [InlineData("Empty", "let values: [0 of i32] = []\nlet copied = values.slice().toArray()\nrequire copied.isEmpty else => $abort(\"empty\")")]
    [InlineData("Nested", "let values: [2 of [2 of i32]] = [[1, 2], [3, 4]]\nlet copied = values.slice().toArray()\nrequire copied[1][0] == 3 else => $abort(\"nested\")")]
    [InlineData("References", "var first = 4\nvar second = 7\nvar values: [2 of ref/i32] = [first@ref, second@ref]\nlet copied = values.slice().toArray()\nvalues[0] = second@ref\nrequire copied[0]@follow == 4 and copied[1]@follow == 7 else => $abort(\"references\")")]
    [InlineData("NoInit", "unsafe\n    var values: [2 of bool] = noinit\n    var view = values.sliceUniq()\n    view[0] = true\n    view[1] = false\n    let copied = view.slice().toArray()\n    view[0] = false\n    require copied[0] and not copied[1] else => $abort(\"noinit\")")]
    [InlineData("Append", "let values: [3 of i32] = [1, 2, 3]\nvar copied = [9]\ncopied.appendCopies(values.slice())\ncopied.appendCopies(values.slice(1..))\nrequire copied.length == 6 and copied[0] == 9 and copied[4] == 2 and copied[5] == 3 else => $abort(\"append\")")]
    public void CopiesUseTheCommonOrderedAppend(string name, string source)
        => ScalarEmissionTest.EmitFixture("SliceToArray" + name, source, string.Empty);

    [Theory]
    [InlineData("toArray", "let copied = values.slice().toArray()", "Hello.kimi:2:14")]
    [InlineData("appendCopies", "var copied: Array<i32> = []\ncopied.appendCopies(values.slice())", "Hello.kimi:3:1")]
    public void AllocationFailureReportsThePublicCall(string name, string use, string location)
    {
        var ir = CompilationTestHelper.WriteIr(MinimalEmissionTest.Analyze("let values: [2 of i32] = [1, 2]\n" + use));
        Assert.Contains("call ptr @HeapAlloc(", ir, StringComparison.Ordinal);
        ir = ir.Replace("call ptr @HeapAlloc(", "call ptr @fail_allocate(", StringComparison.Ordinal);
        ir += "\ndefine internal ptr @fail_allocate(ptr %heap, i32 %flags, i64 %size) { ret ptr null }\n";
        ScalarEmissionTest.WriteFixture("SliceToArrayFailure" + name, ir, string.Empty, 1, location + ": abort KIMI_E_ALLOC: Failed to allocate memory\n");
    }

    [Fact]
    public void CopyLoopsDoNotCallPerElementAppendOrGrowth()
    {
        var ir = CompilationTestHelper.WriteIr(MinimalEmissionTest.Analyze("let values: [3 of i32] = [1, 2, 3]\nlet copied = values.slice().toArray()"));
        Assert.DoesNotContain("@__kimi_array_append_", ir, StringComparison.Ordinal);
        Assert.DoesNotContain("@__kimi_array_place_", ir, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("let values = [\"text\"]\nlet copy = values.slice().toArray()")]
    [InlineData("var value = 1\nlet values: [1 of ref/i32] = [value@ref]\nlet copy = values.slice().toArray()\nvalue = 2\nrequire copy[0]@follow == 1 else => $abort(\"loan\")")]
    [InlineData("var values = [1, 2]\nvar view = values.sliceUniq()\nlet copy = view.toArray()")]
    [InlineData("var values = [1, 2]\nvalues.appendCopies(values.slice())")]
    public void NonCopyElementsInnerLoansAndAliasingKeepOrdinaryRestrictions(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmCopyPhasesReuseStorage()
    {
        var c = MinimalEmissionTest.Analyze("let values: [3 of i32] = [1, 2, 3]\nlet copy = values.slice().toArray()\nrequire copy[1] == 2 else => $abort(\"copy\")");
        var valid = true;
        var bytes = AllocationMeasurement.Measure(
            () =>
            {
                valid &= c.Bind().IsComplete;
                c.Binding.CheckStartup(OutputKind.Application);
                valid &= c.Ownership.Analyze().IsVerified && c.Emission.WriteIr(TextWriter.Null, out _);
            },
            iterations: 8,
            warmupIterations: 8);
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(0, bytes);
    }
}
