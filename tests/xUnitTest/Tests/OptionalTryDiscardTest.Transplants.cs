// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public partial class OptionalTryDiscardTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ATransplantedTryReactivatesItsOtherAlternativeInTheRecipient(bool donorOption)
    {
        const string Option = "func source() -> i32? => .Some(1)\nfunc run() -> i32? => .Some(try source())\n_ = run()";
        const string Result = "func source() -> Result<i32, i32> => .Ok(1)\nfunc run() -> Result<i32, i32> => .Ok(try source())\n_ = run()";
        var donor = MinimalEmissionTest.Analyze(donorOption ? Option : Result, "donor.kimi");
        var recipient = MinimalEmissionTest.Analyze(donorOption ? Result : Option, "recipient.kimi");
        Assert.True(donor.Analysis.IsVerified, MinimalEmissionTest.Describe(donor));
        Assert.True(recipient.Analysis.IsVerified, MinimalEmissionTest.Describe(recipient));
        var moved = Assert.Single(KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<TryKoto>());
        var old = Assert.Single(KotoTree.Walk(recipient.Kotonoha.RootKoto).OfType<TryKoto>());
        ParseTestHelper.Detach(moved, new CodeBlockKoto(moved.CodeContext));
        Assert.True(KotoHelper.Replace(old.Parent!, old, moved));
        Assert.True(recipient.Bind().IsComplete, MinimalEmissionTest.Describe(recipient));
        Assert.All(KotoTree.Walk(moved), node => Assert.Same(recipient.Kotonoha, node.Kotonoha));
        Assert.All(KotoTree.Walk(moved), node => Assert.Same(recipient.Kotonoha.Syntax, node.SyntaxOwner));
        Assert.Same(BoundType.I32, moved.TypeOf());
        Assert.True(recipient.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(recipient.Analyze().IsVerified, MinimalEmissionTest.Describe(recipient));
        Assert.NotEmpty(CompilationTestHelper.WriteIr(recipient));
    }
}
