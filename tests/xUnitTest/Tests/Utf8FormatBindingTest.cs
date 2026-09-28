// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class Utf8FormatBindingTest
{
    private const string Prefix = "var buffer = Text.heap(0)\nvar writer = Text.writer(buffer@uniq)\n";

    [Theory]
    [InlineData("0@i8")]
    [InlineData("0@u8")]
    [InlineData("0@i16")]
    [InlineData("0@u16")]
    [InlineData("0@i32")]
    [InlineData("0@u32")]
    [InlineData("0@i64")]
    [InlineData("0@u64")]
    [InlineData("0@i128")]
    [InlineData("0@u128")]
    [InlineData("0@isize")]
    [InlineData("0@usize")]
    [InlineData("0@f32")]
    [InlineData("0@f64")]
    [InlineData("true")]
    [InlineData("'a'")]
    [InlineData("()")]
    [InlineData("\"text\"")]
    [InlineData("Text.utf8(\"text\")")]
    public void BuiltinsUseTheFormattingContract(string value)
    {
        var c = MinimalEmissionTest.Analyze(Prefix + "_ = (writer@uniq).write(" + value + ")");
        Assert.True(c.Binding.Result.IsComplete, Describe(c));
    }

    [Theory]
    [InlineData("(42).format(writer@uniq)")]
    [InlineData("\"text\".format(writer@uniq)")]
    [InlineData("true.format(writer@uniq)")]
    public void DirectBuiltinRequirementSelection(string expression)
    {
        var c = MinimalEmissionTest.Analyze(Prefix + "_ = " + expression);
        Assert.True(c.Binding.Result.IsComplete, Describe(c));
    }

    [Theory]
    [InlineData("(1, 2)")]
    [InlineData("[2 of 1]")]
    [InlineData("null@unsafe/i32")]
    public void UnspecifiedTypesDoNotAcquireImplicitFormatting(string value)
    {
        var c = MinimalEmissionTest.Analyze(Prefix + "_ = (writer@uniq).write(" + value + ")");
        Assert.False(c.Binding.Result.IsComplete);
    }

    private static string Describe(Compilation c)
        => MinimalEmissionTest.Describe(c, null) + string.Join("\n", c.Binding.Issues.Select(x => x.Code + ": " + x.Node));
}
