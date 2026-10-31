// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public partial class ModuleBindingTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TransplantedCallsUseTheRecipientModuleAndKeepTheirSourceLocation(bool recipientHasTarget)
    {
        var c = Create(
            "public func main() => ()\nfunc moved() => ()\n" + (recipientHasTarget ? "func target() => ()" : string.Empty),
            "func target() => ()\npublic func moved() => target()");
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c));
        var old = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), node => node.Name == "moved");
        var donor = c.SourceModules[1];
        var moved = Assert.Single(KotoTree.Walk(donor.RootKoto).OfType<FunctionKoto>(), node => node.Name == "moved");
        var call = Assert.IsType<InvocationKoto>(moved.ExpressionBody);
        var document = call.CodeContext.SourceDocument;
        var span = call.Method.Span;
        ParseTestHelper.Detach(moved, new CodeBlockKoto(moved.CodeContext));
        Assert.True(KotoHelper.Replace(old.Parent!, old, moved));
        Assert.Same(c.Kotonoha, call.Kotonoha);
        Assert.Same(document, call.CodeContext.SourceDocument);
        Assert.Equal(recipientHasTarget, c.Bind().IsComplete);
        if (recipientHasTarget)
        {
            Assert.Same(c.Kotonoha, call.CallOf()!.Target.Declaration.Kotonoha);
        }
        else
        {
            var error = Assert.Single(CompilationTestHelper.BindingErrors(c));
            Assert.Equal("UnresolvedBinding_Kd", error.Code);
            Assert.Equal("library.kimi", error.Path);
            Assert.Equal(span, error.Span);
            Assert.Null(call.CallOf());
        }
    }
}
