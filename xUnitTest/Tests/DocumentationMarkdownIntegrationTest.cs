// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Documentation;
using Kimi.Compiler.Parsing;
using Tinyhand;
using Xunit;

namespace XunitTest;

#pragma warning disable xUnit1051 // Tests include explicit cancellation/retry and default-token requests.

public class DocumentationMarkdownIntegrationTest
{
    [Fact]
    public void ClassifiesExternalGenericSemanticsLengthAndOriginNamesFromRealDeclarations()
    {
        const string body = "- by: external\n- factor: internal\n- note: collision\n- self: ordinary\n- s: semantics\n- T: type\n- N: length\n- a: origin\n- return: result\n\n# note\n\nStandard section.";
        var tree = DocumentationCommentTest.Parse(Comment(body) + "func sample<s/T, length N>(by => factor: i32, note?: ref{a}/i32, self: i32) => ()");
        var comment = Assert.Single(Assert.Single(tree.DocumentationSources).Comments);
        var doc = DocumentationMarkdownDocument.Parse(comment);
        Assert.All(doc.GetItemCandidates().ToArray().Take(9), item => Assert.Equal(DocumentationMarkdownItemKind.Unclassified, item.Kind));
        Assert.True(tree.Compilation.Bind().IsComplete);
        var function = Assert.IsType<FunctionKoto>(comment.Declaration);
        Assert.Equal(-1, function.BoundSymbol!.ReceiverIndex);
        var classified = doc.ClassifyItems(DeclarationParameters(function)).ToArray();
        Assert.Equal(new[] { "by", "factor", "note", "self", "s", "T", "N", "a", "return", "note" }, classified.Select(x => x.Name));
        Assert.Equal(new[] { DocumentationMarkdownItemKind.Parameter, DocumentationMarkdownItemKind.Unknown, DocumentationMarkdownItemKind.Parameter, DocumentationMarkdownItemKind.Parameter, DocumentationMarkdownItemKind.Parameter, DocumentationMarkdownItemKind.Parameter, DocumentationMarkdownItemKind.Parameter, DocumentationMarkdownItemKind.Parameter, DocumentationMarkdownItemKind.Standard, DocumentationMarkdownItemKind.Standard }, classified.Select(x => x.Kind));
        Assert.All(doc.GetItemCandidates().ToArray().Take(9), item => Assert.Equal(DocumentationMarkdownItemKind.Unclassified, item.Kind));
    }

    [Theory]
    [InlineData("struct S {}", "self", true)]
    [InlineData("group G", "self: i32", false)]
    public void ReceiverExclusionUsesBindingRole(string container, string parameter, bool receiver)
    {
        var source = container + "\n" + Comment("- self: description\n- note: description", "    ") + "    public func f(" + parameter + ", note?: i32) => ()";
        var tree = DocumentationCommentTest.Parse(source);
        Assert.True(tree.Compilation.Bind().IsComplete);
        var comment = Assert.Single(Assert.Single(tree.DocumentationSources).Comments);
        var declaration = Assert.IsType<FunctionKoto>(comment.Declaration);
        Assert.Equal(receiver ? 0 : -1, declaration.BoundSymbol!.ReceiverIndex);
        var items = DocumentationMarkdownDocument.Parse(comment).ClassifyItems(DeclarationParameters(declaration)).ToArray();
        Assert.Equal(receiver ? DocumentationMarkdownItemKind.Unknown : DocumentationMarkdownItemKind.Parameter, items[0].Kind);
        Assert.Equal(DocumentationMarkdownItemKind.Parameter, items[1].Kind);
    }

    [Fact]
    public void AmbiguityAcrossNamespacesDoesNotBecomeAStandardItem()
    {
        var tree = DocumentationCommentTest.Parse(Comment("- note: ambiguous\n\n# note\n\nStandard") + "func f<note>(note?: i32) => ()");
        Assert.True(tree.Compilation.Bind().IsComplete);
        var comment = Assert.Single(Assert.Single(tree.DocumentationSources).Comments);
        var doc = DocumentationMarkdownDocument.Parse(comment);
        var items = doc.ClassifyItems(DeclarationParameters(comment.Declaration!)).ToArray();
        Assert.Equal(DocumentationMarkdownItemKind.Ambiguous, items[0].Kind);
        Assert.Equal(2, items[0].ParameterMatchCount);
        Assert.Equal(DocumentationMarkdownItemKind.Standard, items[1].Kind);
    }

    [Fact]
    public void PublishedFragmentsAndGeneratedSourceStayIndependent()
    {
        var compilation = Compilation.CreateForTest();
        compilation.CollectDocumentation = true;
        var tree = compilation.Kotonoha;
        var context = tree.CreateCodeContext();
        context.Parse(tree.RootKoto, new SourceDocument("z.kimi", Comment("# note\n\nLast") + "public struct S {}"));
        context.Parse(tree.RootKoto, new SourceDocument("a.kimi", Comment("```kimi\n- return: code") + "public struct S {}"));
        context.Parse(tree.RootKoto, new SourceDocument("generated.kimi", Comment("- return: generated") + "public struct S {}"), "mod", 2);
        Assert.True(compilation.Bind().IsComplete);
        var declaration = tree.DocumentationSources.First().Comments[0].Declaration!;
        var comments = compilation.Binding.GetDocumentation(declaration, true);
        var documents = comments.Select(x => DocumentationMarkdownDocument.Parse(x)).ToArray();
        Assert.Equal(3, documents.Length);
        Assert.Equal(DocumentationMarkdownKind.CodeBlock, documents[0].Root.FirstChild!.Value.Kind);
        Assert.Empty(documents[0].GetItemCandidates().ToArray());
        Assert.Equal("note", Assert.Single(documents[1].GetItemCandidates().ToArray()).Name);
        Assert.Equal("return", Assert.Single(documents[2].GetItemCandidates().ToArray()).Name);
        Assert.Null(comments[2].Owner.LogicalName);
        Assert.Equal("mod", comments[2].Owner.ModId);
        Assert.Equal(2, comments[2].Owner.AdditionOrder);
        foreach (var index in Enumerable.Range(0, documents.Length))
        {
            var doc = documents[index];
            var rootSpan = doc.Root.SourceSpan;
            Assert.Equal(doc.Text, comments[index].Owner.Source.SourceText.Substring(rootSpan.Start, rootSpan.Length).Replace("\n/// ", "\n", StringComparison.Ordinal));
            DocumentationMarkdownParserTest.AssertRanges(doc);
        }
    }

    [Fact]
    public void ConfigurationReloadAndEditsDoNotReuseOldSyntaxOrClassification()
    {
        var compilation = Compilation.CreateForTest();
        compilation.CollectDocumentation = true;
        Assert.True(compilation.Prepare(WindowsProfile.Target));
        compilation.Kotonoha.AddSource(new SourceDocument("platform.kimi", "#switch\n    #case windows\n" + Comment("- windows: selected", "        ") + "        public func f(windows?: i32) => ()\n    #case _\n" + Comment("- other: selected", "        ") + "        public func f(other?: i32) => ()"));
        Assert.True(compilation.Bind().IsComplete);
        var original = Selected(compilation.Kotonoha);
        var oldDocument = DocumentationMarkdownDocument.Parse(original);
        var oldItems = oldDocument.ClassifyItems(DeclarationParameters(original.Declaration!)).ToArray();
        var target = Compilation.CreateForTest();
        target.CollectDocumentation = true;
        Assert.True(target.Prepare("x86_64-unknown-linux-gnu"));
        var restored = TinyhandSerializer.Deserialize<Kotonoha>(TinyhandSerializer.Serialize(compilation.Kotonoha))!;
        restored.OnDeserialized(target);
        var current = Selected(restored);
        var newDocument = DocumentationMarkdownDocument.Parse(current);
        Assert.Equal("other", Assert.Single(newDocument.GetItemCandidates().ToArray()).Name);
        Assert.Equal("windows", Assert.Single(oldItems).Name);
        Assert.NotEqual(oldDocument.Root, newDocument.Root);
        var edited = DocumentationCommentTest.Parse(Comment("- windows: selected") + "func f(renamed?: i32) => ()");
        Assert.True(edited.Compilation.Bind().IsComplete);
        var editedComment = Selected(edited);
        var editedDocument = DocumentationMarkdownDocument.Parse(editedComment);
        Assert.Equal(oldDocument.Text, editedDocument.Text);
        Assert.Equal(DocumentationMarkdownItemKind.Unknown, Assert.Single(editedDocument.ClassifyItems(DeclarationParameters(editedComment.Declaration!)).ToArray()).Kind);
        Assert.Equal(DocumentationMarkdownItemKind.Parameter, Assert.Single(oldItems).Kind);
        Assert.NotEqual(oldDocument.Root, editedDocument.Root);
    }

    [Fact]
    public void PublicationAccessAndSpecializationAreAppliedBeforeMarkdownParsing()
    {
        var tree = DocumentationCommentTest.Parse(Comment("**original**") + "public func weight<T>(value?: ref/T) -> i32 => 1\n" + Comment("**implementation**") + "specialize func weight<i32>(value: ref/i32) -> i32 => 2\nstruct Hidden {}\n" + Comment("**private**", "    ") + "    public func f() => ()");
        Assert.True(tree.Compilation.Bind().IsComplete);
        var comments = Assert.Single(tree.DocumentationSources).Comments;
        var binding = tree.Compilation.Binding;
        var original = DocumentationMarkdownDocument.Parse(Assert.Single(binding.GetDocumentation(comments[1].Declaration!, true)));
        var implementation = DocumentationMarkdownDocument.Parse(Assert.Single(binding.GetDocumentation(comments[1].Declaration!, true, true)));
        Assert.Equal("<p><strong>original</strong></p>\n", DocumentationMarkdownParserTest.RenderSyntax(original.Root));
        Assert.Equal("<p><strong>implementation</strong></p>\n", DocumentationMarkdownParserTest.RenderSyntax(implementation.Root));
        Assert.Empty(binding.GetDocumentation(comments[2].Declaration!, true));
        Assert.Equal("**private**", DocumentationMarkdownDocument.Parse(Assert.Single(binding.GetDocumentation(comments[2].Declaration!))).Text);
    }

    [Fact]
    public void MarkdownInterruptionAndIncompleteInputDoNotChangeLanguageValidity()
    {
        var tree = DocumentationCommentTest.Parse(Comment(new string('>', 300) + " [unfinished") + "func f() => ()");
        var comment = Selected(tree);
        Assert.Throws<DocumentationMarkdownLimitException>(() => DocumentationMarkdownDocument.Parse(comment));
        Assert.True(tree.Compilation.Bind().IsComplete);
        Assert.Empty(tree.DiagnosticCollection.GetArray());
        var retried = DocumentationMarkdownDocument.Parse(comment, maximumDepth: 400);
        DocumentationMarkdownParserTest.AssertRanges(retried);
        Assert.Empty(retried.GetItemCandidates().ToArray());
    }

    private static string Comment(string text, string indent = "") => string.Concat(text.Split('\n').Select(line => indent + "/// " + line + "\n"));

    private static DocumentationComment Selected(Kotonoha tree) => Assert.Single(Assert.Single(tree.DocumentationSources).Comments, x => x.IsSelected && x.Declaration is not null);

    // Test adapter: consume real Binding receiver roles, not the spelling "self".
    // Product publication/classification wiring remains a later migration step.
    private static DocumentationMarkdownParameter[] DeclarationParameters(Koto declaration)
    {
        var result = new List<DocumentationMarkdownParameter>();
        var generics = declaration is FunctionKoto function ? function.GenericArguments : declaration is DeclarationContainerKoto container ? container.GenericParameterNodes : [];
        foreach (var generic in generics)
        {
            result.Add(new(generic.Identifier));
            if (generic.SemanticsParameter is { } semantics)
            {
                result.Add(new(semantics));
            }
        }

        var origins = declaration is FunctionKoto callable ? callable.Origins : declaration is DeclarationContainerKoto type ? type.OriginNames : [];
        result.AddRange(origins.Select(name => new DocumentationMarkdownParameter(name)));
        if (declaration is FunctionKoto f)
        {
            Assert.NotNull(f.BoundSymbol);
            for (var index = 0; index < f.Parameters.Count; index++)
            {
                result.Add(new(f.Parameters[index].ExternalName, index == f.BoundSymbol.ReceiverIndex));
            }
        }

        return result.ToArray();
    }
}
