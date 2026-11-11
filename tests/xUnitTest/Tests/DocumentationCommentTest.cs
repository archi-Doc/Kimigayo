// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class DocumentationCommentTest
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void ExtractsTextAndMapsUnicodeAndWhitespace(string newline)
    {
        var source = "/// First 😀  \n///\n///     code\nfunc f() => ()".Replace("\n", newline);
        var tree = Parse(source);
        var comment = Assert.Single(Assert.Single(tree.DocumentationSources).Comments);
        Assert.Equal("f", Assert.IsType<FunctionKoto>(comment.Declaration).Name);
        var text = comment.GetText();
        Assert.Equal("First 😀  \n\n    code", text.Text);
        Assert.Equal(source.IndexOf("😀", StringComparison.Ordinal), text.GetSourceOffset(text.Text.IndexOf("😀", StringComparison.Ordinal)));
        Assert.Equal(source.IndexOf("code", StringComparison.Ordinal) + 4, text.GetSourceOffset(text.Text.Length));
        Assert.Same(text, comment.GetText());
    }

    [Fact]
    public void CollectionIsOptInAndDoesNotChangeSyntax()
    {
        const string source = "/// Description.\nfunc f() -> i32 => 42";
        var normal = Parse(source, false);
        var documented = Parse(source);
        Assert.Empty(normal.DocumentationSources);
        Assert.Equal(normal.RootKoto.ToString(), documented.RootKoto.ToString());
        Assert.Empty(TestDiagnostics.Of(documented));
    }

    [Theory]
    [InlineData("/// old\n\n/// new\nfunc f() => ()", "new")]
    [InlineData("/// old\n// func old() => ()\n/// new\nfunc f() => ()", "new")]
    [InlineData("/// old\n\n///\nfunc f() => ()", "")]
    [InlineData("/// test\n#Test\nfunc f() => ()", "test")]
    [InlineData("#Test\n/// test\nfunc f() => ()", "test")]
    [InlineData("/// test\n    // ordinary\nfunc f() => ()", "test")]
    public void UsesOnlyNearestBlock(string source, string expected)
    {
        var tree = Parse(source);
        var comments = Assert.Single(tree.DocumentationSources).Comments;
        Assert.Equal(expected, Assert.Single(comments, x => x.Declaration is not null).GetText().Text);
    }

    [Theory]
    [InlineData("/// outer\n    /// wrong\nfunc f() => ()")]
    [InlineData("/// orphan\n()\nfunc f() => ()")]
    [InlineData("/// orphan\n#if true\nfunc f() => ()")]
    [InlineData("func f(\n    /// parameter\n    x: i32\n) => ()\nfunc g() => ()")]
    public void DoesNotCrossBoundaries(string source)
    {
        var tree = Parse(source);
        Assert.DoesNotContain(Assert.Single(tree.DocumentationSources).Comments, x => x.Declaration is not null);
    }

    [Fact]
    public void AttributesAreAtomic()
    {
        var tree = Parse("/// outer\n#Marker(\n    /// argument\n    1\n)\nfunc f() => ()");
        Assert.Equal("outer", Assert.Single(Assert.Single(tree.DocumentationSources).Comments, x => x.Declaration is not null).GetText().Text);
    }

    [Fact]
    public void ExcludedDeclarationsDoNotCaptureFollowingDocumentation()
    {
        var tree = Parse("#if false\n    /// excluded\n    let excluded = 1\n/// kept\nfunc f() => ()");
        var docs = Assert.Single(tree.DocumentationSources);
        Assert.Equal("kept", Assert.Single(docs.Comments, x => x.IsSelected && x.Declaration is not null).GetText().Text);
        Assert.Empty(docs.GetDiagnostics());
    }

    [Fact]
    public void KeepsFragmentsOnTheWrittenLeaf()
    {
        var tree = Parse("/// a\nrootgroup A.B\n/// b\nrootgroup A.B");
        var comments = Assert.Single(tree.DocumentationSources).Comments;
        Assert.Equal(2, comments.Count);
        Assert.Same(comments[0].Declaration, comments[1].Declaration);
        Assert.Equal("B", Assert.IsType<GroupKoto>(comments[0].Declaration).Name);
        Assert.NotEqual(comments[0].DeclarationSpan, comments[1].DeclarationSpan);
    }

    // The header starts after the prelude (SPEC 2.3) at its first modifier or its introducer, also after a nested body in the header.
    [Theory]
    [InlineData("/// d\npublic open struct S\n    public virtual func area(self) -> f64 => 0.0", "public open struct S")]
    [InlineData("group G\n    /// d\n    public unsafe func f() => ()", "public unsafe func f()")]
    [InlineData("group G\n    /// d\n    #Test public func f() => ()", "public func f()")]
    [InlineData("open struct B\n    public virtual func s(self: objref/Self) -> i32 => 1\nstruct D : B\n    /// d\n    override func s(self: objref/Self) -> i32 => 2", "override func s(self: objref/Self) -> i32")]
    [InlineData("struct S\n    var x: i32\n    /// d\n    public init(x: i32)\n        self.x = x", "public init(x: i32)")]
    [InlineData("func g<T>(x: T) => ()\n/// d\nspecialize func g<i32>(x: i32) => ()", "specialize func g<i32>(x: i32)")]
    [InlineData("public struct S\n    /// d\n    public var f: (i32) -> i32 = func(x: i32) -> i32\n        /// inner\n        let y = x\n        y", "public var f")]
    public void DeclarationSpanStartsAtTheWrittenHeader(string source, string header)
    {
        var comment = Assert.Single(Assert.Single(Parse(source).DocumentationSources).Comments, x => x.GetText().Text == "d");
        Assert.NotNull(comment.Declaration);
        Assert.Equal(header, source[comment.DeclarationSpan.Start..comment.DeclarationSpan.End]);
    }

    [Fact]
    public void CommentsInLiteralsAndBlockCommentsAreNotDocumentation()
    {
        var tree = Parse("//// ordinary\n/*\n/// ignored\n*/\nlet s = \"/// literal\"\n() /// trailing");
        var docs = Assert.Single(tree.DocumentationSources);
        Assert.Equal("IgnoredDocumentation", Assert.Single(docs.GetDiagnostics()).Code);
        Assert.Single(docs.Comments);
    }

    [Fact]
    public void PreservesBodyWhenFormatting()
    {
        var tree = Parse("///a\n///  b  \n///\nfunc f() => ()");
        var comment = Assert.Single(Assert.Single(tree.DocumentationSources).Comments);
        var formatted = Parse(comment.Format() + "\nfunc f() => ()");
        Assert.Equal(comment.GetText().Text, Assert.Single(Assert.Single(formatted.DocumentationSources).Comments).GetText().Text);
    }

    internal static Kotonoha Parse(string source, bool collect = true)
    {
        var compilation = Compilation.CreateForTest();
        compilation.CollectDocumentation = collect;
        var tree = compilation.Kotonoha;
        tree.CreateCodeContext().Parse(tree.RootKoto, new SourceDocument("docs.kimi", source));
        ParseTestHelper.AssertValid(tree);
        return tree;
    }
}
