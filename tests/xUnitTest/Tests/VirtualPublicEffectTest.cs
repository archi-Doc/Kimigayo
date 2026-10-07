// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class VirtualPublicEffectTest
{
    [Theory]
    [InlineData("", false, false)]
    [InlineData("        effect confined\n", true, false)]
    [InlineData("        effect preserves results\n", false, true)]
    [InlineData("        effect confined\n        effect preserves results\n", true, true)]
    public void OnlyTheOriginalDeclarationsPublishVirtualBounds(string clauses, bool confined, bool preserves)
    {
        var c = MinimalEmissionTest.Analyze("open struct Base\n    public virtual func read(self: objref/Self) -> i32\n" + clauses + "        return 1\nstruct Derived : Base\n    override func read(self: objref/Self) -> i32 => 2\n()");
        var original = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.IsVirtual);
        Assert.Equal((confined, preserves), c.Binding.AvailableEffectBounds(original, null, original));
        var implementation = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.IsOverride);
        Assert.Equal((false, false), c.Binding.AvailableEffectBounds(implementation, null, implementation));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void AReboundContractRevokesPreviouslyPublishedBounds()
    {
        const string Source = "open struct Base\n    public virtual func read(self: objref/Self) -> i32\n        effect confined\n        return 1\n()";
        var c = MinimalEmissionTest.Analyze(Source);
        var original = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.IsVirtual);
        Assert.Equal((true, false), c.Binding.AvailableEffectBounds(original, null, original));
        var donor = MinimalEmissionTest.Analyze(Source.Replace("effect confined", "effect preserves results", StringComparison.Ordinal));
        var replacement = Assert.Single(KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.IsVirtual);
        Assert.True(KotoHelper.Replace(original.Parent!, original, replacement));
        Assert.Equal((false, false), c.Binding.AvailableEffectBounds(original, null, original));
        c.Bind();
        Assert.Equal((false, false), c.Binding.AvailableEffectBounds(original, null, original));
        Assert.Equal((false, true), c.Binding.AvailableEffectBounds(replacement, null, replacement));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void RepeatedPublicBoundQueriesAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze("open struct Base\n    public virtual func read(self: objref/Self) -> i32\n        effect confined\n        return 1\n()");
        var original = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.IsVirtual);
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Binding.AvailableEffectBounds(original, null, original) == (true, false), iterations: 64, warmupIterations: 32));
        Assert.True(valid);
    }
}
