// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class CallableRegionInvalidationTest
{
    // An edit revokes the entry context, also after a pass that did not finish (a Bind that throws leaves no result).
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EditingAnItemSignatureRevokesItsEntryContext(bool unfinished)
    {
        const string Source = "func make<T>(value: T) -> i32 => 1\nlet f = make<i32>\n_ = f(42)";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
        var function = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "make");
        var item = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<GenericsKoto>().Single().TypeOf()!;
        var before = c.Binding.FunctionItemContext(item)!;
        if (unfinished)
        {
            typeof(Binding).GetProperty(nameof(Binding.Result))!.SetValue(c.Binding, default(BindingResult)); // As a throwing Bind leaves it.
        }

        var donor = MinimalEmissionTest.Analyze(Source.Replace("-> i32", "-> i64", StringComparison.Ordinal));
        var replacement = KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "make").ReturnType!;
        ParseTestHelper.Detach(replacement, new CodeBlockKoto(replacement.CodeContext));
        Assert.True(KotoHelper.Replace(function, function.ReturnType!, replacement));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c));
        var updated = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<GenericsKoto>().Single().TypeOf()!;
        var after = c.Binding.FunctionItemContext(updated)!;
        Assert.NotSame(before, after);
        Assert.Equal("i64", after.ReturnType!.Name);
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(c.Analyze().IsVerified);
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
    }

    [Fact]
    public void EditingALocalSourceRevokesTheOldLoan()
    {
        const string Source = "var a = 1\nvar b = 2\nvar view = a@ref\na = 3\n_ = view@follow";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.False(c.Analysis.IsVerified);
        var local = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<VariableKoto>().Single(x => x.NameKoto.IdentifierName == "view");
        var donor = MinimalEmissionTest.Analyze(Source.Replace("view = a@ref", "view = b@ref", StringComparison.Ordinal));
        var replacement = KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<VariableKoto>().Single(x => x.NameKoto.IdentifierName == "view").InitializerKoto!;
        ParseTestHelper.Detach(replacement, new CodeBlockKoto(replacement.CodeContext));
        Assert.True(KotoHelper.Replace(local, local.InitializerKoto!, replacement));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
    }
}
