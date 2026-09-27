// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class AssociatedOriginSyntaxTest
{
    [Theory]
    [InlineData("contract C\n    associate Item(a)")]
    [InlineData("contract C\n    associate Item(a, b) is ref/i32 during (a and b)")]
    [InlineData("contract C\n    associate Item(a) for uniq/Self during a")]
    [InlineData("contract C\n    associate Item(a) is Iterator for ref/Self during a")]
    [InlineData("contract C\n    associate Item(a) is Self.Other(a)")]
    [InlineData("contract C\n    associate Item(a) is Other(a)")]
    [InlineData("contract C\n    associate Item(a) for uniq/Self during a\n        origin a outlives a")]
    [InlineData("struct S\n    Self is C\n    associate C.Item(a) is ref/i32 during a")]
    [InlineData("func f<T>(x: ref/T during a) -> T.(C).Item(a)\n    T is C\n    $abort(\"unused\")")]
    [InlineData("func f<T>(x: ref/T during a, y: ref/T during b) -> T.Item((a and b))\n    T is C\n    $abort(\"unused\")")]
    public void ParsesAndRoundTrips(string source)
    {
        var tree = ParseTestHelper.ParseSuccess(source);
        ParseTestHelper.ParseSuccess(tree.RootKoto.ToString());
    }

    [Theory]
    [InlineData("contract C\n    associate Item()")]
    [InlineData("contract C\n    associate Item(a,)")]
    [InlineData("contract C\n    associate Item((a and b))")]
    [InlineData("func f(x: T.Item()) => ()")]
    [InlineData("func f(x: T.Item(a,)) => ()")]
    [InlineData("func f(x: T.Item(a and b)) => ()")]
    public void RejectsInvalidLists(string source)
        => Assert.NotEmpty(ParseTestHelper.Parse(source).DiagnosticCollection.GetArray());

    [Fact]
    public void ValueCallsKeepTheirOwnSyntax()
    {
        var tree = ParseTestHelper.ParseSuccess("func f() => value.Item(a)");
        Assert.Single(KotoTree.Walk(tree.RootKoto).OfType<InvocationKoto>());
    }

    [Theory]
    [InlineData("contract C\n    associate Item(a, b) is ref/(ref/i32 during a) during b")]
    [InlineData("contract C\n    associate Item(a) for uniq/Self during a")]
    [InlineData("contract C\n    associate Item is i32 for uniq/Self during static")]
    [InlineData("contract C\n    associate Item\nstruct S\n    Self is C\n    associate C.Item is i32 for uniq/Self during static")]
    public void UnimplementedFormationCannotCertifyBinding(string source)
    {
        ParseTestHelper.ParseSuccess(source);
        var compilation = MinimalEmissionTest.Analyze(source);
        Assert.False(compilation.Binding.Result.IsComplete);
        Assert.False(compilation.Bind().IsComplete);
    }
}
