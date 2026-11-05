// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public partial class UserIterationTest
{
    [Fact]
    public void AWholeBoundLoopRebuildsItsSyntheticCallsInTheRecipient()
    {
        const string Program = Counter + Three + "for item in Three.init() => Console.writeLine(\"item\")";
        var recipient = MinimalEmissionTest.Analyze(Program, "recipient.kimi");
        var donor = MinimalEmissionTest.Analyze(Program, "donor.kimi");
        Assert.True(recipient.Analysis.IsVerified, MinimalEmissionTest.Describe(recipient));
        Assert.True(donor.Analysis.IsVerified, MinimalEmissionTest.Describe(donor));
        var old = Assert.Single(KotoTree.Walk(recipient.Kotonoha.RootKoto).OfType<ForKoto>());
        var moved = Assert.Single(KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<ForKoto>());
        var oldPlan = moved.IterationOf().Plan;
        Assert.NotNull(oldPlan);
        ParseTestHelper.Detach(moved, new CodeBlockKoto(moved.CodeContext));
        Assert.True(KotoHelper.Replace(old.Parent!, old, moved));
        Assert.Null(moved.IterationOf().Plan);
        Assert.True(recipient.Bind().IsComplete, MinimalEmissionTest.Describe(recipient));
        var plan = moved.IterationOf().Plan!;
        Assert.NotNull(plan);
        Assert.NotSame(oldPlan, plan);
        foreach (var node in new Koto[] { plan.Iterator, plan.Receiver, plan.Next, plan.Match, plan.Item })
        {
            Assert.Same(recipient.Kotonoha, node.Kotonoha);
            Assert.Same(recipient.Kotonoha.Syntax, node.SyntaxOwner);
        }

        Assert.Same(moved.Body, plan.Decomposition.Arms[0].Syntax.Body);
        Assert.True(recipient.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(recipient.Analyze().IsVerified, MinimalEmissionTest.Describe(recipient));
        Assert.NotEmpty(CompilationTestHelper.WriteIr(recipient));
    }
}
