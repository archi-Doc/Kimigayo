// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class CallableRegionInvalidationTest
{
    [Fact]
    public void EditingAnItemSignatureRevokesItsEntryContext()
    {
        const string Source = "func make<T>(value: T) -> i32 => 1\nlet f = make<i32>\n_ = f(42)";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out _), MinimalEmissionTest.Describe(c, null));
        var function = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "make");
        var item = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<GenericsKoto>().Single().TypeOf()!;
        var before = c.Binding.FunctionItemContext(item)!;
        var donor = MinimalEmissionTest.Analyze(Source.Replace("-> i32", "-> i64", StringComparison.Ordinal));
        var replacement = KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "make").ReturnType!;
        Assert.True(KotoHelper.Replace(function, function.ReturnType!, replacement));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        var updated = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<GenericsKoto>().Single().TypeOf()!;
        var after = c.Binding.FunctionItemContext(updated)!;
        Assert.NotSame(before, after);
        Assert.Equal("i64", after.ReturnType!.Name);
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out _), MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void EditingALocalSourceRevokesTheOldLoan()
    {
        const string Source = "var a = 1\nvar b = 2\nvar view = a@ref\na = 3\n_ = view@follow";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.False(c.Ownership.Result.IsVerified);
        var local = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<VariableKoto>().Single(x => x.NameKoto.IdentifierName == "view");
        var donor = MinimalEmissionTest.Analyze(Source.Replace("view = a@ref", "view = b@ref", StringComparison.Ordinal));
        var replacement = KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<VariableKoto>().Single(x => x.NameKoto.IdentifierName == "view").InitializerKoto!;
        Assert.True(KotoHelper.Replace(local, local.InitializerKoto!, replacement));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out _), MinimalEmissionTest.Describe(c, null));
    }
}
