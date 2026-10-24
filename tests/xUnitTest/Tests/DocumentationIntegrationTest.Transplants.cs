// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public partial class DocumentationIntegrationTest
{
    [Fact]
    public void RepeatedRootTransplantsCopyOnlyTheMovedDocumentationAssociation()
    {
        var donor = DocumentationCommentTest.Parse(
            "/// moved documentation\nfunc moved() -> i32 => 1\n/// unrelated documentation\nfunc unrelated() -> i32 => 2");
        Assert.True(donor.Compilation.Bind().IsComplete);
        var source = Assert.Single(donor.DocumentationSources);
        var originalComment = source.Comments[0];
        var unrelatedComment = source.Comments[1];
        var moved = Assert.IsType<FunctionKoto>(originalComment.Declaration);
        var previousComment = originalComment;
        CodeBlockKoto? originalSlot = null;
        for (var i = 0; i < 2; i++)
        {
            var recipient = i == 0 ? DocumentationCommentTest.Parse("func moved() -> i32 => 0") : donor;
            Koto target = i == 0
                ? Assert.Single(KotoTree.Walk(recipient.RootKoto).OfType<FunctionKoto>(), node => node.Name == "moved")
                : originalSlot!;
            var placeholder = new CodeBlockKoto(moved.CodeContext);
            ParseTestHelper.Detach(moved, placeholder);
            originalSlot ??= placeholder;
            Assert.True(KotoHelper.Replace(target.Parent!, target, moved));
            Assert.True(recipient.Compilation.Bind().IsComplete);
            var imported = Assert.Single(recipient.Compilation.Binding.GetDocumentation(moved));
            Assert.NotSame(previousComment, imported);
            Assert.Equal("moved documentation", imported.GetText().Text);
            Assert.Same(moved, imported.Declaration);
            var importedSource = Assert.Single(recipient.DocumentationSources, item => item.Comments.Contains(imported));
            Assert.Same(source.Source, importedSource.Source);
            Assert.Equal(source.LogicalName, importedSource.LogicalName);
            if (i == 0)
            {
                Assert.NotSame(source, importedSource);
                Assert.DoesNotContain(
                    recipient.DocumentationSources.SelectMany(item => item.Comments),
                    comment => comment.GetText().Text == "unrelated documentation");
            }

            Assert.Contains(unrelatedComment, source.Comments);
            Assert.Equal("unrelated documentation", unrelatedComment.GetText().Text);
            previousComment = imported;
        }
    }
}
