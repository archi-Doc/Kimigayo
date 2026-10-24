// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

public partial class SourceDocumentAndDiagnosticTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MovingARecoveryImportsItsCauseWithoutUnrelatedDonorErrors(bool recoveryIsRoot)
    {
        var donor = CompilationTestHelper.Parse("let value = 1 +\nlet unrelated = 2 +", "donor.kimi");
        var errors = TestDiagnostics.Of(donor);
        Assert.Equal(2, errors.Length);
        var field = Assert.Single(KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<FieldKoto>(), node => node.NameKoto.IdentifierName == "value");
        var binary = Assert.IsType<BinaryKoto>(field.InitializerKoto);
        Koto moved = recoveryIsRoot ? Assert.IsType<ErrorKoto>(binary.Right) : binary;
        var recipient = CompilationTestHelper.ParseSuccess("let value = 0", "recipient.kimi");
        var target = Assert.Single(KotoTree.Walk(recipient.Kotonoha.RootKoto).OfType<FieldKoto>());
        ParseTestHelper.Detach(moved, new CodeBlockKoto(moved.CodeContext));
        Assert.True(KotoHelper.Replace(target, target.InitializerKoto!, moved));
        Assert.Equal(errors[0], Assert.Single(TestDiagnostics.Of(recipient)));
        Assert.Equal(errors, TestDiagnostics.Of(donor));
        Assert.False(recipient.Bind().IsComplete);
        recipient.Binding.ReportDiagnostics();
        Assert.Equal(errors[0], Assert.Single(TestDiagnostics.Of(recipient)));
    }

    [Fact]
    public void MovingAContextRecordedRecoveryKeepsTheKnownPrerequisite()
    {
        const string Header = "func f(x: ref/i32 during a, y: ref/i32 during b)\n";
        var donor = CompilationTestHelper.Parse(Header + "    origin a = b\n    ()\nlet unrelated = 2 +", "donor.kimi");
        var recipient = CompilationTestHelper.ParseSuccess(Header + "    origin a outlives b\n    ()", "recipient.kimi");
        var from = Assert.Single(KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<FunctionKoto>(), node => node.Name == "f");
        var to = Assert.Single(KotoTree.Walk(recipient.Kotonoha.RootKoto).OfType<FunctionKoto>(), node => node.Name == "f");
        var moved = Assert.Single(OriginClauses.Get(from));
        var old = Assert.Single(OriginClauses.Get(to));
        Assert.NotNull(moved.CodeContext.RecoveryCause(moved));
        var expected = Assert.Single(TestDiagnostics.Of(donor), diagnostic => diagnostic.Code == "OriginRelationOperator_Kd");
        var reader = new TokenReader(moved.CodeContext, string.Empty, []);
        var placeholder = new OriginRelationKoto(
            ref reader,
            moved.Span,
            new IdentifierNameKoto(moved, "a"),
            new IdentifierNameKoto(moved, "b"),
            false);
        ParseTestHelper.Detach(moved, placeholder);
        Assert.True(KotoHelper.Replace(to, old, moved));
        Assert.NotNull(moved.CodeContext.RecoveryCause(moved));
        Assert.False(recipient.Bind().IsComplete);
        recipient.Binding.ReportDiagnostics();
        Assert.Equal(expected, Assert.Single(TestDiagnostics.Of(recipient)));
    }

    [Fact]
    public void SameModuleAttachmentRetainsACustomDiagnosticDestination()
    {
        var c = CompilationTestHelper.ParseSuccess("func original() => ()");
        var owner = new DiagnosticOwner();
        var custom = owner.GetOrAddCollection("custom.kimi");
        c.Kotonoha.CreateCodeContext(custom).Parse(c.Kotonoha.RootKoto, new SourceDocument("custom.kimi", "func changed() => missing"));
        var original = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), node => node.Name == "original");
        var changed = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), node => node.Name == "changed");
        var call = Assert.IsType<IdentifierNameKoto>(changed.ExpressionBody);
        var context = call.CodeContext;
        ParseTestHelper.Detach(changed, new CodeBlockKoto(changed.CodeContext));
        Assert.True(KotoHelper.Replace(original.Parent!, original, changed));
        Assert.Same(context, call.CodeContext);
        Assert.Same(owner, call.DiagnosticCollection!.Owner);
        Assert.False(c.Bind().IsComplete);
        c.Binding.ReportDiagnostics();
        Assert.Contains(owner.Finalize().Diagnostics, diagnostic => diagnostic.Code == "UnresolvedBinding_Kd");
        Assert.Empty(TestDiagnostics.Of(c));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARecoveredRootTransfersItsCauseToTheActualCustomDiagnosticOwner(bool sameCompilation)
    {
        var recipient = Compilation.CreateForTest();
        Assert.True(recipient.Prepare(WindowsProfile.Target));
        var owner = new DiagnosticOwner();
        recipient.Kotonoha.CreateCodeContext(owner.GetOrAddCollection("custom.kimi"))
            .Parse(recipient.Kotonoha.RootKoto, new SourceDocument("custom.kimi", "let target = 0"));
        var donor = sameCompilation ? recipient : CompilationTestHelper.ParseSuccess(string.Empty);
        donor.Kotonoha.AddSource(new SourceDocument("donor.kimi", "let value = 1 +"));
        var expected = Assert.Single(TestDiagnostics.Of(donor));
        var error = Assert.Single(KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<ErrorKoto>());
        var target = Assert.Single(
            KotoTree.Walk(recipient.Kotonoha.RootKoto).OfType<FieldKoto>(),
            node => node.NameKoto.IdentifierName == "target");
        ParseTestHelper.Detach(error, new CodeBlockKoto(error.CodeContext));
        Assert.True(KotoHelper.Replace(target, target.InitializerKoto!, error));
        Assert.Same(owner, error.DiagnosticCollection!.Owner);
        var diagnostics = owner.Finalize();
        var imported = Assert.Single(diagnostics.Diagnostics);
        Assert.Equal(expected.Code, imported.Code);
        Assert.Equal(expected.Path, diagnostics.Sources[imported.Source].Path);
        Assert.Equal(expected.Span, imported.Span);
        Assert.True(Binding.IsRecovery(error, out var cause));
        Assert.Equal(error.Cause, cause);
        error.ReportDerived(DiagnosticRequirement.Binding(BindingFailure.MissingType), [cause]);
        Assert.Equal(imported, Assert.Single(owner.Finalize().Diagnostics));
    }

    [Fact]
    public void AForeignAttributeHeadDoesNotMoveTheRetainedRecipientTailThroughTheDonor()
    {
        var recipient = CompilationTestHelper.Parse("#Top\n#Bottom(1 +)\nfunc original() => ()", "recipient.kimi");
        var donor = CompilationTestHelper.ParseSuccess("#New\nfunc changed() => ()", "donor.kimi");
        var original = Assert.Single(KotoTree.Walk(recipient.Kotonoha.RootKoto).OfType<FunctionKoto>(), node => node.Name == "original");
        var changed = Assert.Single(KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<FunctionKoto>(), node => node.Name == "changed");
        var oldHead = Assert.IsType<AttributeKoto>(original.AttributeChain);
        var tail = Assert.IsType<AttributeKoto>(oldHead.AttributeChain);
        var context = tail.CodeContext;
        var recipientDiagnostics = TestDiagnostics.Of(recipient);
        var donorDiagnostics = TestDiagnostics.Of(donor);
        Assert.NotEmpty(recipientDiagnostics);
        var moved = Assert.IsType<AttributeKoto>(changed.AttributeChain);
        var reader = new TokenReader(moved.CodeContext, string.Empty, []);
        ParseTestHelper.Detach(moved, new AttributeKoto(ref reader, moved.Span, new IdentifierNameKoto(moved, "Placeholder")));
        Assert.True(KotoHelper.Replace(original, oldHead, moved));
        Assert.Same(tail, moved.AttributeChain);
        Assert.Same(context, tail.CodeContext);
        Assert.All(KotoTree.Walk(tail), node => Assert.Same(recipient.Kotonoha, node.Kotonoha));
        Assert.Equal(recipientDiagnostics, TestDiagnostics.Of(recipient));
        Assert.Equal(donorDiagnostics, TestDiagnostics.Of(donor));
    }
}
