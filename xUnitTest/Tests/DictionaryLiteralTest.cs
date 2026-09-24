// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class DictionaryLiteralTest
{
    [Theory]
    [InlineData("i32", "1", "(+1)")]
    [InlineData("i32", "1_000", "1000")]
    [InlineData("i32", "0", "-0")]
    [InlineData("i32", "-2147483648", "(-2147483648)")]
    [InlineData("u128", "340282366920938463463374607431768211455", "340282366920938463463374607431768211455")]
    [InlineData("bool", "true", "(true)")]
    [InlineData("char", "'a'", "'\\u(61)'")]
    [InlineData("string", "\"a\"", "\"\\u(61)\"")]
    [InlineData("()", "()", "(())")]
    public void MandatoryDuplicateKeysFailBinding(string type, string first, string later)
    {
        var source = "let entries: Dictionary<" + type + ", i32> = [" + first + ": 1, " + later + ": 2]";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains("DuplicateDictionaryKey_Kd", MinimalEmissionTest.Describe(c, null));
        var duplicate = Assert.Single(c.Binding.Issues, issue => issue.Code == DiagnosticCode.DuplicateDictionaryKey_Kd);
        Assert.Equal(source.LastIndexOf(later, StringComparison.Ordinal) + (type == "string" ? 1 : 0), duplicate.Node.Span.Start);
    }

    [Fact]
    public void EligibleKeysAreComparedAcrossRuntimeKeysAndInUnreachableCode()
    {
        var c = MinimalEmissionTest.Analyze("func unused(key: i32)\n    if false\n        let entries: Dictionary<i32, i32> = [1: 1, key: 2, (1): 3, +1: 4]");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains("DuplicateDictionaryKey_Kd", MinimalEmissionTest.Describe(c, null));
        Assert.Equal(2, c.Binding.Issues.Count(issue => issue.Code == DiagnosticCode.DuplicateDictionaryKey_Kd));
    }

    [Theory]
    [InlineData("i32", "1", "2")]
    [InlineData("i32", "-1", "1")]
    [InlineData("i32", "1", "1 + 0")]
    [InlineData("i32", "1", "1 as i32")]
    [InlineData("f64", "1.0", "1.0")]
    [InlineData("(i32, i32)", "(1, 2)", "(1, 2)")]
    public void StaticCheckingDoesNotExpandIntoOtherExpressions(string type, string first, string later)
    {
        var c = MinimalEmissionTest.Analyze("let entries: Dictionary<" + type + ", i32> = [" + first + ": 1, " + later + ": 2]");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void NamedKeysAndNestedLiteralsDoNotPolluteStaticChecking()
    {
        var c = MinimalEmissionTest.Analyze("let key = 1\nlet entries: Dictionary<i32, Dictionary<i32, i32>> = [key: [1: 2], key: [1: 3], 1: [1: 4]]");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
    }
}
