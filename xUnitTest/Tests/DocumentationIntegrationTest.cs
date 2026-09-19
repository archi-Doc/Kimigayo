// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Kimi.Compiler;
using Kimi.Compiler.Documentation;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class DocumentationIntegrationTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData("/// d\ngroup G")]
    [InlineData("/// d\nstruct S")]
    [InlineData("/// d\nenum E\n    A")]
    [InlineData("/// d\ncontract C")]
    [InlineData("/// d\npublic func main() => ()")]
    [InlineData("/// d\n#Test func f() => ()")]
    [InlineData("struct S\n    /// d\n    init() => ()")]
    [InlineData("struct S\n    /// d\n    deinit => ()")]
    [InlineData("contract C\n    /// d\n    func f(self)")]
    [InlineData("contract C\n    /// d\n    property value: i32 has get")]
    [InlineData("struct S\n    /// d\n    var value: i32 = 0")]
    [InlineData("group G\n    /// d\n    computed value: i32\n        get() -> i32 => 0")]
    [InlineData("func f()\n    /// d\n    let value = 0")]
    [InlineData("enum E\n    /// d\n    A(i32)")]
    [InlineData("contract C\n    /// d\n    associate Item")]
    [InlineData("struct S\n    /// d\n    associate C.Item is i32")]
    [InlineData("/// d\nspecialize func f<i32>(value: i32) => ()")]
    public void CoversEveryDeclarationTarget(string source)
    {
        var tree = DocumentationCommentTest.Parse(source);
        var comment = Assert.Single(Assert.Single(tree.DocumentationSources).Comments);
        Assert.NotNull(comment.Declaration);
        Assert.Empty(Assert.Single(tree.DocumentationSources).GetDiagnostics());
        var independent = DocumentationMarkdownDocument.Parse(comment, TestContext.Current.CancellationToken);
        Assert.Equal("d", independent.Text);
        Assert.Equal("d", Assert.Single(independent.Summary!.Value.Children).Text.ToString());
        DocumentationMarkdownParserTest.AssertRanges(independent);
    }

    [Fact]
    public void SelectsSwitchArmsAndNestedExclusions()
    {
        var tree = DocumentationCommentTest.Parse("#switch\n    #case false\n        /// other\n        func a() => ()\n    #case true\n        #if false\n            /// excluded\n            let broken =\n        /// chosen\n        func b() => ()\n    #case _\n        /// fallback\n        func c() => ()\n/// after\nfunc d() => ()");
        var docs = Assert.Single(tree.DocumentationSources);
        Assert.Equal(new[] { "chosen", "after" }, docs.Comments.Where(x => x.IsSelected && x.Declaration is not null).Select(x => x.GetText().Text));
        Assert.Empty(docs.GetDiagnostics());
    }

    [Theory]
    [InlineData("#if false\n    ()\n    /// trailing\n/// kept\nfunc f() => ()")]
    [InlineData("#if false\n    ()\n    /// trailing")]
    public void ExcludedTrailingCommentsHaveNoDiagnostics(string source)
    {
        var docs = Assert.Single(DocumentationCommentTest.Parse(source).DocumentationSources);
        Assert.Empty(docs.GetDiagnostics());
    }

    [Fact]
    public void OrdersFragmentsAndKeepsGeneratedSourcesWithoutLinkBases()
    {
        var c = Compilation.CreateForTest();
        c.CollectDocumentation = true;
        var tree = c.Kotonoha;
        var context = tree.CreateCodeContext();
        context.Parse(tree.RootKoto, new SourceDocument("z.kimi", "/// z\npublic struct S"));
        context.Parse(tree.RootKoto, new SourceDocument("generated.kimi", "/// generated2\npublic struct S"), "b", 2);
        context.Parse(tree.RootKoto, new SourceDocument("a.kimi", "/// a\npublic struct S"));
        context.Parse(tree.RootKoto, new SourceDocument("generated.kimi", "/// generated1\npublic struct S"), "b", 1);
        ParseTestHelper.AssertValid(tree);
        Assert.True(c.Bind().IsComplete);
        var target = tree.DocumentationSources.First().Comments[0].Declaration!;
        Assert.Equal(new[] { "a", "z", "generated1", "generated2" }, c.Binding.GetDocumentation(target, true).Select(x => x.GetText().Text));
        Assert.All(tree.DocumentationSources.Where(x => x.ModId is not null), x => Assert.Null(x.LogicalName));
    }

    [Fact]
    public void UsesOriginalSpecializationContractAndEffectiveAccess()
    {
        var tree = DocumentationCommentTest.Parse("/// original\npublic func weight<T>(value: ref/T) -> i32 => 1\n/// implementation\nspecialize func weight<i32>(value: ref/i32) -> i32 => 2\nstruct Hidden\n    /// private container\n    public func f() => ()");
        var c = tree.RootKoto.CodeContext.Compilation;
        Assert.True(c.Bind().IsComplete);
        var comments = Assert.Single(tree.DocumentationSources).Comments;
        var target = Assert.IsType<FunctionKoto>(comments[1].Declaration);
        Assert.Equal("original", Assert.Single(c.Binding.GetDocumentation(target, true)).GetText().Text);
        Assert.Equal("implementation", Assert.Single(c.Binding.GetDocumentation(target, true, true)).GetText().Text);
        Assert.Empty(c.Binding.GetDocumentation(comments[2].Declaration!, true));
        tree.AddSource(new SourceDocument("next.kimi", "()"));
        Assert.Empty(c.Binding.GetDocumentation(target, true));
    }

    [Fact]
    public void PublishesPublicCasesAndContractRequirements()
    {
        var tree = DocumentationCommentTest.Parse("public enum E\n    /// case\n    A\npublic contract C\n    /// associate\n    associate Item\n    /// function\n    func f(self)\n    /// property\n    property value: i32 has get");
        Assert.True(tree.Compilation.Bind().IsComplete);
        foreach (var comment in Assert.Single(tree.DocumentationSources).Comments)
        {
            var published = tree.Compilation.Binding.GetDocumentation(comment.Declaration!, true);
            Assert.True(published.Count == 1, $"{comment.GetText().Text}: {comment.Declaration?.Akind}, {comment.Declaration?.BoundSymbol}");
            Assert.Same(comment, published[0]);
        }
    }

    [Fact]
    public void AssociatedSpecificationsDoNotCopyRequirementDocumentation()
    {
        var tree = DocumentationCommentTest.Parse("public contract C\n    /// requirement\n    associate Item\npublic struct S\n    Self is C\n    /// implementation\n    associate C.Item is i32");
        Assert.True(tree.Compilation.Bind().IsComplete);
        foreach (var comment in Assert.Single(tree.DocumentationSources).Comments)
        {
            Assert.Same(comment, Assert.Single(tree.Compilation.Binding.GetDocumentation(comment.Declaration!, true)));
        }
    }

    [Fact]
    public void PrivateImplementationsDoNotExposeAssociatedDocumentation()
    {
        var tree = DocumentationCommentTest.Parse("public contract C\n    associate Item\nstruct Hidden\n    Self is C\n    /// private implementation\n    associate C.Item is i32");
        Assert.True(tree.Compilation.Bind().IsComplete);
        var comment = Assert.Single(Assert.Single(tree.DocumentationSources).Comments);
        Assert.Empty(tree.Compilation.Binding.GetDocumentation(comment.Declaration!, true));
        Assert.Same(comment, Assert.Single(tree.Compilation.Binding.GetDocumentation(comment.Declaration!)));
    }

    [Fact]
    public void DocumentationDoesNotChangeBindingAnalysisLoweringOrEmission()
    {
        const string source = "/// Documentation <T>\n/// - missing: text\nfunc twice(x: i32) -> i32 => x * 2\n/// Local\nlet value = twice(21)\nConsole.writeLine(\"ok\")";
        var ordinary = Compile(false);
        var documented = Compile(true);
        Assert.Equal(ordinary, documented);
        ScalarEmissionTest.WriteFixture("DocumentationComments", documented, "ok\n");

        string Compile(bool collect)
        {
            var c = Compilation.CreateForTest();
            c.CollectDocumentation = collect;
            Assert.True(c.Prepare(WindowsProfile.Target));
            c.Kotonoha.AddSource(new SourceDocument("same.kimi", source));
            Assert.True(c.Bind().IsComplete);
            if (collect)
            {
                foreach (var comment in c.Kotonoha.DocumentationSources.SelectMany(x => x.Comments))
                {
                    var markdown = DocumentationMarkdownDocument.Parse(comment);
                    DocumentationMarkdownParserTest.AssertRanges(markdown);
                    Assert.NotNull(markdown.Summary);
                    _ = markdown.GetItemCandidates();
                }
            }

            c.Binding.CheckStartup(OutputKind.Application);
            Assert.True(c.Ownership.Analyze().IsVerified);
            using var writer = new StringWriter();
            Assert.True(c.Emission.WriteIr(writer, out var error), error);
            return writer.ToString();
        }
    }

    [Fact]
    public void CollectionWithoutCandidatesAllocatesNoExtraStorage()
    {
        const string source = "func f() -> i32 => 42";
        for (var i = 0; i < 20; i++)
        {
            DocumentationCommentTest.Parse(source, i % 2 == 0);
        }

        var off = Measure(false);
        var on = Measure(true);
        output.WriteLine($"32 parses without documentation: disabled={off.Bytes} bytes/{off.Milliseconds:F2} ms; enabled={on.Bytes} bytes/{on.Milliseconds:F2} ms");
        Assert.Equal(off.Bytes, on.Bytes);

        (long Bytes, double Milliseconds) Measure(bool collect)
        {
            var start = Stopwatch.GetTimestamp();
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 32; i++)
            {
                DocumentationCommentTest.Parse(source, collect);
            }

            return (GC.GetAllocatedBytesForCurrentThread() - before, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    [Fact]
    public void ReparseReplacesAssociationsAndPreservesGeneratedProvenance()
    {
        var c = Compilation.CreateForTest();
        c.CollectDocumentation = true;
        var tree = c.Kotonoha;
        tree.CreateCodeContext().Parse(tree.RootKoto, new SourceDocument("generated.kimi", "/// generated\npublic struct S"), "mod", 3);
        var restored = TinyhandSerializer.Deserialize<Kotonoha>(TinyhandSerializer.Serialize(tree))!;
        restored.OnDeserialized(c);
        var old = Assert.Single(restored.DocumentationSources);
        Assert.Null(old.LogicalName);
        Assert.Equal("mod", old.ModId);
        Assert.Equal(3, old.AdditionOrder);
        restored.OnDeserialized(c);
        var next = Assert.Single(restored.DocumentationSources);
        Assert.NotSame(old.Comments[0].Declaration, next.Comments[0].Declaration);
        c.CollectDocumentation = false;
        restored.OnDeserialized(c);
        Assert.Empty(restored.DocumentationSources);
    }

    [Fact]
    public void ConfigurationAndSourceSnapshotsDoNotShareDocumentation()
    {
        var c = Compilation.CreateForTest();
        c.CollectDocumentation = true;
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("platform.kimi", "#switch\n    #case windows\n        /// windows\n        public struct S\n    #case _\n        /// other\n        public struct S"));
        var bytes = TinyhandSerializer.Serialize(c.Kotonoha);
        var target = Compilation.CreateForTest();
        target.CollectDocumentation = true;
        Assert.True(target.Prepare("x86_64-unknown-linux-gnu"));
        var restored = TinyhandSerializer.Deserialize<Kotonoha>(bytes)!;
        restored.OnDeserialized(target);
        Assert.Equal("other", Assert.Single(Assert.Single(restored.DocumentationSources).Comments, x => x.IsSelected).GetText().Text);
        Assert.Equal("windows", Assert.Single(Assert.Single(c.Kotonoha.DocumentationSources).Comments, x => x.IsSelected).GetText().Text);
        var edited = DocumentationCommentTest.Parse("/// edited\nstruct S");
        Assert.Equal("edited", Assert.Single(Assert.Single(edited.DocumentationSources).Comments).GetText().Text);
    }

    [Fact]
    public void RecoveryDefersUncertainAssociationsWithoutSuppressingLanguageErrors()
    {
        var c = Compilation.CreateForTest();
        c.CollectDocumentation = true;
        c.Kotonoha.AddSource(new SourceDocument("broken.kimi", "/// broken\nfunc f(\n/// following\nfunc g() => ()"));
        Assert.True(c.Kotonoha.HasSourceErrors);
        var source = Assert.Single(c.Kotonoha.DocumentationSources);
        Assert.All(source.Comments, x => Assert.Null(x.Declaration));
        Assert.Empty(source.GetDiagnostics());
    }

    [Fact]
    public void CollectsActualInterpolationCommentsWithoutReadingLiteralText()
    {
        var tree = DocumentationCommentTest.Parse("let text = \"literal /// \\(\n    /// expression comment\n    1\n)\"\n/// after\nfunc f() => ()");
        var docs = Assert.Single(tree.DocumentationSources);
        Assert.Equal(new[] { "expression comment", "after" }, docs.Comments.Select(x => x.GetText().Text));
        Assert.Null(docs.Comments[0].Declaration);
        Assert.NotNull(docs.Comments[1].Declaration);
        Assert.Equal("docs.kimi", docs.LogicalName);
    }

    [Fact]
    public void TokensAndLexicalErrorsAreIdenticalWithCollectionEnabled()
    {
        const string text = "/// text\nfunc f() => ()\n\t/// invalid indentation\n;\n/// eof";
        var c = Compilation.CreateForTest();
        var source = new SourceDocument("lexical.kimi", text);
        var off = Lex(false);
        var on = Lex(true);
        Assert.Equal(off.Tokens, on.Tokens);
        Assert.Equal(off.Errors, on.Errors);
        Assert.NotEmpty(on.Errors);

        (Token[] Tokens, string[] Errors) Lex(bool collect)
        {
            var diagnostics = c.Kimigayo.GetOrAddDiagnosticCollection(collect.ToString());
            var tokenizer = new Tokenizer(diagnostics, source) { CollectDocumentation = collect };
            try
            {
                tokenizer.ReadAll();
                return (tokenizer.Tokens.ToArray(), diagnostics.GetArray().Select(x => x.ToString()).ToArray());
            }
            finally
            {
                tokenizer.Dispose();
            }
        }
    }

    [Fact]
    public void DisabledCollectionAddsNoAllocationsWithDocumentation()
    {
        var c = Compilation.CreateForTest();
        var diagnostics = c.Kotonoha.DiagnosticCollection;
        var source = new SourceDocument("many.kimi", string.Concat(Enumerable.Range(0, 256).Select(i => $"/// item {i}\nfunc f{i}() => ()\n")));
        var ordinary = new SourceDocument(source.Path, source.SourceText.Replace("///", "// ", StringComparison.Ordinal));
        for (var i = 0; i < 20; i++)
        {
            Lex(source, false);
            Lex(source, true);
            Lex(ordinary, false);
        }

        Assert.Empty(diagnostics.GetArray());

        var before = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        for (var i = 0; i < 64; i++)
        {
            Lex(ordinary, false);
        }

        var baseline = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        start = Stopwatch.GetTimestamp();
        for (var i = 0; i < 64; i++)
        {
            Lex(source, false);
        }

        var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(baseline, bytes);
        output.WriteLine($"64 lexical passes, 256 declarations: ordinary={baseline} bytes; documentation disabled={bytes} bytes/{elapsed:F2} ms");
        before = GC.GetAllocatedBytesForCurrentThread();
        start = Stopwatch.GetTimestamp();
        for (var i = 0; i < 64; i++)
        {
            Lex(source, true);
        }

        elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"64 lexical passes, 256 documented declarations: enabled={bytes} bytes/{elapsed:F2} ms");

        void Lex(SourceDocument input, bool collect)
        {
            var tokenizer = new Tokenizer(diagnostics, input) { CollectDocumentation = collect };
            try
            {
                tokenizer.ReadAll();
            }
            finally
            {
                tokenizer.Dispose();
            }
        }
    }
}
