// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;
using static XunitTest.ParseTestHelper;

namespace XunitTest;

/// <summary>Regressions found by checking SPEC examples against the Tokenizer, Parser and Binder.</summary>
public class SpecReviewTest
{
    [Theory]
    [InlineData("let protected_or_internal = 1")]
    [InlineData("let protected_and_internal = 2")]
    public void UnderscoreAccessSpellingsAreOrdinaryNames(string source)
        => ParseSuccess(source); // SPEC 2.5: compound access specifications are two keywords.

    [Fact]
    public void CompoundAccessSpecificationsUseTwoKeywordTokens()
    {
        var parsed = ParseSuccess("struct S\n    protected internal func f() => ()\n    private protected func g() => ()");
        var members = Assert.Single(parsed.RootKoto.NestedContainers).Members;
        Assert.Equal(ModifierKind.ProtectedOrInternal, Assert.IsType<FunctionKoto>(members[0]).Modifier);
        Assert.Equal(ModifierKind.ProtectedAndInternal, Assert.IsType<FunctionKoto>(members[1]).Modifier);
    }

    [Theory]
    [InlineData("func make(a?: ref/A, b?: ref/B)\n    -> Pair<A, B>{\n        result,}\n    origin result.left == a\n    origin result.right == b\n    return a")]
    [InlineData("func make(a?: ref/A, b?: ref/B)\n    -> Pair<A, B>{\n        result\n    }\n    origin result.left == a\n    return a")]
    [InlineData("func make(a?: ref/A)\n    -> View<A>{\n        result}\n    origin result.source == a\n    return .Some(a)\nlet next = 1")]
    [InlineData("func f()\n    -> i32\n    return 1\nlet next = 2")]
    public void ArrowHeaderContinuationNestsDelimitersFromItsOwnLine(string source)
    {
        // SPEC 2.2.1 and the §15.5 result example: content inside a delimiter opened on the "->" line is one level deeper.
        var parsed = ParseSuccess(source);
        var function = Assert.IsType<FunctionKoto>(GetChildren(parsed.RootKoto)[0]);
        Assert.NotNull(function.ReturnType);
        Assert.True(function.Body is { Items.Count: 1 } || function.ExpressionBody is not null);
    }

    [Fact]
    public void ArrowContinuationDoesNotAuthorizeASeparateBodyArrowLine()
        => Assert.NotEmpty(Parse("func f()\n    -> i32\n    => 1").DiagnosticCollection.GetArray());

    [Theory]
    [InlineData("func store<T>(value?: ref{b}/T, other?: ref{a}/T)\n    origin b outlives a\n    ()")]
    [InlineData("struct Holder<T> {stored, other}\n    origin other outlives static\n    let value: ref{stored}/T")]
    public void OriginBoundsParseAndRoundTrip(string source)
    {
        var parsed = ParseSuccess(source);
        var text = Write(parsed);
        Assert.Contains(source.Contains("func", StringComparison.Ordinal) ? "origin b outlives a" : "origin other outlives static", text);
        Assert.Equal(text, Write(ParseSuccess(text)));
    }

    [Theory]
    [InlineData("func bad<T> {a}:(value: ref{a}/T) => ()")]
    [InlineData("func bad<T> {a}: 1(value: ref{a}/T) => ()")]
    public void MissingOriginBoundTargetsAreSyntaxErrors(string source)
        => Assert.NotEmpty(Parse(source).DiagnosticCollection.GetArray());

    [Fact]
    public void ContainerFragmentsShareRelationsAndRepeatClosedHeaders()
    {
        var parsed = ParseSuccess("struct S<T> {a, b}\n    origin b outlives a\n    func f() => ()\nstruct S<T> {a}\n    func g() => ()");
        Assert.True(Assert.Single(parsed.RootKoto.NestedContainers).HasIncompatibleBindingHeader);
        parsed = ParseSuccess("struct S<T> {a, b}\n    origin b outlives a\n    func f() => ()\nstruct S<T> {a, b}\n    func g() => ()");
        Assert.False(Assert.Single(parsed.RootKoto.NestedContainers).HasIncompatibleBindingHeader);
    }

    [Theory]
    [InlineData("func f<T>(x?: ref{a}/T, y?: ref{b}/T)\n    origin b outlives a\n    ()", true)]
    [InlineData("func f<T>(x?: ref{a}/T)\n    origin a outlives static\n    ()", true)]
    [InlineData("func f<T>(x?: ref{a}/T, y?: ref{b}/T)\n    origin b outlives missing\n    ()", false)]
    public void OriginRelationsResolveOnlyExistingBinders(string source, bool valid)
    {
        var c = Compilation.CreateForTest();
        c.Kotonoha.AddSource(new SourceDocument("bounds.kimi", source));
        Assert.Equal(valid, c.Bind().IsComplete);
    }

    [Fact]
    public void UnboundedOriginsStillBindCompletely()
    {
        var c = Compilation.CreateForTest();
        c.Kotonoha.AddSource(new SourceDocument("origins.kimi", "func f<T>(x?: ref{a}/T, y?: ref{b}/T) => ()"));
        Assert.True(c.Bind().IsComplete, string.Join(", ", c.Binding.Issues.Select(x => x.Code)));
    }

    private static string Write(Kotonoha parsed)
    {
        var builder = default(IndentedStringBuilder);
        try
        {
            parsed.RootKoto.UnparseAll(ref builder);
            return builder.ToString();
        }
        finally
        {
            builder.Dispose();
        }
    }
}
