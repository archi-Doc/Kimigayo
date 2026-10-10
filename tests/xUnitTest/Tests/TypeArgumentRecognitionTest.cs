// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Xunit;
using static XunitTest.ParseTestHelper;

namespace XunitTest;

/// <summary>
/// A '&lt;' opens Type arguments only adjacent to a Name and with a matching '&gt;' that the layout rules let continue it
/// (SPEC 2.2.1, 2.4, 12.4.2); otherwise it compares values. One rule, decided by the tokenizer, serves both phases.
/// </summary>
public class TypeArgumentRecognitionTest
{
    [Theory]
    [InlineData("func f(a: i32, b: i32, c: i32, d: i32) -> bool\n    if a<b\n        return true\n    return c > d")]
    [InlineData("let r = f(x: a < b, y: c >> 1)\nlet s = r > 0")]
    [InlineData("let r = a<b and c >= d")]
    [InlineData("let r = a<b\nlet s = c >= d")]
    [InlineData("if a<b => f(c > 0)")]
    [InlineData("let r = g(a<b) > c")]
    [InlineData("let r = x[a<b] > c")]
    [InlineData("let r = g(\n    a<b,\n    c > d)")]
    [InlineData("func f(a: i32, b: i32) -> bool\n    return a<b\nfunc g(x: i32) -> i32\n    return x >> 2")]
    public void ComparisonsWithoutAMatchingCloserStayComparisons(string source)
    {
        var tree = ParseSuccess(source);
        var nodes = KotoTree.Walk(tree.RootKoto).ToArray();
        Assert.DoesNotContain(nodes, static x => x is GenericsKoto);
        Assert.Contains(nodes, static x => x.Akind == KotoKind.LessThan);
    }

    [Theory]
    [InlineData("let a = f<i32>(1)")]
    [InlineData("let a = f<Option<i32>>(x >> 2)")]
    [InlineData("let a: List<\n    List<i32>> = values")]
    [InlineData("let a = f<\n    Option<i32>\n>(values)")]
    [InlineData("let a = x@wrap<u8>")]
    [InlineData("let a = b is Box<i32>")]
    [InlineData("let a = g(f<i32>(1), c > d)")]
    public void AdjacentListsWithAMatchingCloserAreTypeArguments(string source)
    {
        var tree = ParseSuccess(source);
        Assert.DoesNotContain(KotoTree.Walk(tree.RootKoto), static x => x.Akind == KotoKind.LessThan);
        var roundTrip = ParseSuccess(Unparse(tree));
        Assert.DoesNotContain(KotoTree.Walk(roundTrip.RootKoto), static x => x.Akind == KotoKind.LessThan);
    }
}
