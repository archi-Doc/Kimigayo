// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class VirtualPublicEffectTest
{
    [Theory]
    [InlineData("", false)]
    [InlineData("        effect confined\n", true)]
    public void OnlyTheOriginalDeclarationsPublishVirtualBounds(string clauses, bool confined)
    {
        var c = MinimalEmissionTest.Analyze("open struct Base\n    public virtual func read(self: objref/Self) -> i32\n" + clauses + "        return 1\nstruct Derived : Base\n    override func read(self: objref/Self) -> i32 => 2\n()");
        var original = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.IsVirtual);
        Assert.Equal(confined, c.Binding.AvailableEffectBounds(original, null, original));
        var implementation = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.IsOverride);
        Assert.False(c.Binding.AvailableEffectBounds(implementation, null, implementation));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
    }

    [Fact]
    public void AReboundContractRevokesPreviouslyPublishedBounds()
    {
        const string Source = "open struct Base\n    public virtual func read(self: objref/Self) -> i32\n        effect confined\n        return 1\n()";
        var c = MinimalEmissionTest.Analyze(Source);
        var original = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.IsVirtual);
        Assert.True(c.Binding.AvailableEffectBounds(original, null, original));
        var donor = MinimalEmissionTest.Analyze(Source);
        var replacement = Assert.Single(KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.IsVirtual);
        ParseTestHelper.Detach(replacement, new CodeBlockKoto(replacement.CodeContext));
        Assert.True(KotoHelper.Replace(original.Parent!, original, replacement));
        Assert.False(c.Binding.AvailableEffectBounds(original, null, original));
        c.Bind();
        Assert.False(c.Binding.AvailableEffectBounds(original, null, original));
        Assert.True(c.Binding.AvailableEffectBounds(replacement, null, replacement));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void RepeatedPublicBoundQueriesAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze("open struct Base\n    public virtual func read(self: objref/Self) -> i32\n        effect confined\n        return 1\n()");
        var original = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.IsVirtual);
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Binding.AvailableEffectBounds(original, null, original)));
        Assert.True(valid);
    }
}
