// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 4.3, 4.5 and 4.6.1: the compiler-managed Array Type binds; its operations are not generated yet (PLAN P29).</summary>
public class ArrayBindingTest
{
    private const string Task = "struct Task\n    public let id: i32\n    public init(id: i32) => self.id = id\n    deinit => ()\n";

    [Theory]
    [InlineData("func describe(tasks: ref/Array<Task>) -> isize => tasks.length\nlet tasks: Array<Task> = []\nlet n = describe(tasks@ref)")]
    [InlineData("let tasks: Array<Task> = [Task.init(1), Task.init(2)]\nlet room: isize = tasks.capacity\nlet all = tasks.indices")]
    [InlineData("let numbers: Array<i32> = [1, 2, 3]\nlet n: isize = numbers.length")]
    public void ArrayTypesLiteralsAndMetadataBind(string source)
    {
        var c = MinimalEmissionTest.Analyze(Task + source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified); // Array storage is not analyzed or generated yet.
        Assert.True(c.Ownership.Result.UnsupportedCount > 0, MinimalEmissionTest.Describe(c, null));
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
    }

    [Theory]
    [InlineData("func take(values: Array<i32>) => ()\nlet values: Array<i32> = []\ntake(values)", DiagnosticCode.TransferRequired_Kd)]
    [InlineData("let values: Array<i32> = []\nlet empty = values.isEmpty", DiagnosticCode.UnresolvedBinding_Kd)]
    [InlineData("let values: Array<i32> = [1, true]", DiagnosticCode.TypeMismatch_Kd)]
    [InlineData("let values: Array<i32, bool> = []", DiagnosticCode.TypeMismatch_Kd)]
    public void ArrayIsNonCopyAndCheckedLikeOtherSequences(string source, DiagnosticCode code)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == code);
    }
}
