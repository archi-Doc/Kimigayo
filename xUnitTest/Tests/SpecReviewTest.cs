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
    [InlineData("func make(a: ref/A, b: ref/B)\n    -> Pair<A, B> from (\n        left => a,\n        right => b)\n    return a")]
    [InlineData("func make(a: ref/A, b: ref/B)\n    -> Pair<A, B> from (\n        left => a,\n        right => b\n    )\n    return a")]
    [InlineData("func make(a: ref/A)\n    -> View<A> from (\n        source => a) => .Some(a)\nlet next = 1")]
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
    [InlineData("func store<T> origin a, b : a(value: ref/T from b) => ()")]
    [InlineData("struct Holder<T> origin stored, other : static\n    let value: ref/T from stored")]
    public void OriginBoundsParseAndRoundTrip(string source)
    {
        var parsed = ParseSuccess(source);
        var text = Write(parsed);
        Assert.Contains(source.Contains("func", StringComparison.Ordinal) ? "origin a, b : a(" : "origin stored, other : static", text);
        Assert.Equal(text, Write(ParseSuccess(text)));
    }

    [Theory]
    [InlineData("func bad<T> origin a :(value: ref/T from a) => ()")]
    [InlineData("func bad<T> origin a : 1(value: ref/T from a) => ()")]
    public void MissingOriginBoundTargetsAreSyntaxErrors(string source)
        => Assert.NotEmpty(Parse(source).DiagnosticCollection.GetArray());

    [Fact]
    public void ContainerFragmentsMustRepeatTheSameBounds()
    {
        var parsed = ParseSuccess("struct S<T> origin a, b : a\n    func f() => ()\nstruct S<T> origin a, b\n    func g() => ()");
        Assert.True(Assert.Single(parsed.RootKoto.NestedContainers).HasIncompatibleBindingHeader);
        parsed = ParseSuccess("struct S<T> origin a, b : a\n    func f() => ()\nstruct S<T> origin a, b : a\n    func g() => ()");
        Assert.False(Assert.Single(parsed.RootKoto.NestedContainers).HasIncompatibleBindingHeader);
    }

    [Theory]
    [InlineData("func f<T> origin a, b : a(x: ref/T from a, y: ref/T from b) => ()", DiagnosticCode.UnsupportedBinding_Kd)]
    [InlineData("func f<T> origin a : static(x: ref/T from a) => ()", DiagnosticCode.UnsupportedBinding_Kd)]
    [InlineData("func f<T> origin a, b : missing(x: ref/T from a, y: ref/T from b) => ()", DiagnosticCode.InvalidOriginBinding_Kd)]
    public void OriginBoundsResolveButAreNeverCertifiedWithoutProofs(string source, DiagnosticCode code)
    {
        var c = Compilation.CreateForTest();
        c.Kotonoha.AddSource(new SourceDocument("bounds.kimi", source));
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == code);
        var schema = Assert.IsType<FunctionKoto>(Assert.Single(GetChildren(c.Kotonoha.RootKoto))).BoundSymbol!.Schema!;
        var bounded = schema.Origins[^1];
        Assert.Equal(code == DiagnosticCode.InvalidOriginBinding_Kd ? null : bounded.Slot == 0 ? BoundOrigin.Static : schema.Origins[0].Origin, bounded.Bound);
    }

    [Fact]
    public void UnboundedOriginsStillBindCompletely()
    {
        var c = Compilation.CreateForTest();
        c.Kotonoha.AddSource(new SourceDocument("origins.kimi", "func f<T> origin a, b(x: ref/T from a, y: ref/T from b) => ()"));
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
