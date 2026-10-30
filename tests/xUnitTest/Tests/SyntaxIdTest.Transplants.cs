// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public partial class SyntaxIdTest
{
    // The old generator's rows (Legacy fixtures) went with its selection at G4 (DECISIONS R5-M160); the fixtures keep their stems.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompoundTransplantsBindInTheRecipientRegardlessOfDonorRebindOrder(bool donorFirst)
    {
        const string Program = "func target() -> i32 => 1\nfunc use() -> i32 => target()\nrequire use() == 1 else => $abort(\"use\")";
        var recipient = CompilationTestHelper.ParseSuccess(Program, "recipient.kimi");
        var donor = CompilationTestHelper.BindSuccess("func target() -> i32 => 2\nfunc use() -> i32 => target()");
        Assert.True(recipient.Bind().IsComplete);
        var original = Function(recipient, "use");
        var moved = Function(donor, "use");
        var call = Assert.IsType<InvocationKoto>(moved.ExpressionBody);
        var oldPlan = Assert.IsType<CallPlan>(call.CallOf());
        var originalDocument = call.CodeContext.SourceDocument;
        var originalSpan = call.Span;
        ParseTestHelper.Detach(moved, new CodeBlockKoto(moved.CodeContext));
        Assert.True(KotoHelper.Replace(original.Parent!, original, moved));

        Assert.All(PreOrder(moved), node => Assert.Same(recipient.Kotonoha, node.Kotonoha));
        Assert.All(PreOrder(moved), node => Assert.True(node.SyntaxOwner is null && node.SyntaxId == 0));
        Assert.Null(call.CallOf());
        Assert.Same(originalDocument, call.CodeContext.SourceDocument);
        Assert.Equal(originalSpan, call.Span);
        Assert.Same(original.CodeContext.SourceDocument, moved.CodeContext.SourceDocument);
        if (donorFirst)
        {
            Assert.True(donor.Bind().IsComplete);
        }

        Assert.True(recipient.Bind().IsComplete, MinimalEmissionTest.Describe(recipient, null));
        Assert.NotSame(oldPlan, call.CallOf());
        Assert.Same(Function(recipient, "target"), call.CallOf()!.Target.Declaration);
        Assert.True(recipient.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(recipient.Analyze().IsVerified, MinimalEmissionTest.Describe(recipient, null));
        var ir = CompilationTestHelper.WriteIr(recipient);
        ScalarEmissionTest.WriteFixture($"G235TransplantedFunctionMir{(donorFirst ? "DonorFirst" : "RecipientFirst")}", ir, string.Empty);
        if (!donorFirst)
        {
            Assert.True(donor.Bind().IsComplete);
        }

        Assert.Same(Function(recipient, "target"), call.CallOf()!.Target.Declaration);
        Assert.All(PreOrder(moved), node => Assert.Same(recipient.Kotonoha.Syntax, node.SyntaxOwner));
        AssertConsistent(recipient.Kotonoha.Syntax);
        AssertConsistent(donor.Kotonoha.Syntax);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedTransplantEditsInvalidateOnlyTheRecipientBeforeOrAfterBinding(bool bindBeforeEdit)
    {
        var recipient = CompilationTestHelper.BindSuccess("func target(x: i32) -> i32 => x\nfunc use() -> i32 => target(0)");
        var donor = CompilationTestHelper.BindSuccess("func target(x: i32) -> i32 => x\nfunc use() -> i32 => target(1)");
        var moved = Function(donor, "use");
        var old = Function(recipient, "use");
        ParseTestHelper.Detach(moved, new CodeBlockKoto(moved.CodeContext));
        Assert.True(KotoHelper.Replace(old.Parent!, old, moved));
        Assert.True(donor.Bind().IsComplete);
        if (bindBeforeEdit)
        {
            Assert.True(recipient.Bind().IsComplete, MinimalEmissionTest.Describe(recipient, null));
        }

        var recipientVersion = recipient.Kotonoha.Syntax.Version;
        var donorVersion = donor.Kotonoha.Syntax.Version;
        var call = Assert.IsType<InvocationKoto>(moved.ExpressionBody);
        var literalSource = CompilationTestHelper.ParseSuccess("let value = 7");
        var literal = Assert.IsType<NumberLiteralKoto>(Statement(literalSource, "value").InitializerKoto);
        ParseTestHelper.Detach(literal, new CodeBlockKoto(literal.CodeContext));
        Assert.True(KotoHelper.Replace(call, call.ArgumentNodes[0], literal));
        Assert.Equal(recipientVersion + 1, recipient.Kotonoha.Syntax.Version);
        Assert.Equal(donorVersion, donor.Kotonoha.Syntax.Version);
        Assert.False(recipient.Binding.Result.IsComplete);
        Assert.True(donor.Binding.Result.IsComplete);
        Assert.Null(call.CallOf());
        Assert.True(recipient.Bind().IsComplete, MinimalEmissionTest.Describe(recipient, null));
        Assert.Same(Function(recipient, "target"), call.CallOf()!.Target.Declaration);
    }

    private static FunctionKoto Function(Compilation compilation, string name)
        => Assert.Single(KotoTree.Walk(compilation.Kotonoha.RootKoto).OfType<FunctionKoto>(), node => node.Name == name);
}
