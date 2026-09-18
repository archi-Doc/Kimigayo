// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Documentation;
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
        var source = "/// First 😀  \n///\n///     code\npublic func f() => ()".Replace("\n", newline);
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
        Assert.Empty(documented.DiagnosticCollection.GetArray());
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
        var tree = Parse("#if false\n    /// excluded\n    let invalid =\n/// kept\nfunc f() => ()");
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
