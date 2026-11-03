// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Verification;
using Xunit;

namespace XunitTest;

// SPEC 8.15; PLAN G75 U6: the Semantics-case runs of a definition reuse their pooled side bodies, case vectors, binder lists,
// retentions and issue maps, so warm rebinding and ownership analysis of the case workload allocate nothing.
public sealed class GenericCaseAllocationTest
{
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmCaseRunsAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(VerificationWorkloads.PairCaseFamilies);
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c));
        c.Binding.CheckStartup(Kimi.Compiler.OutputKind.Application);
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        var success = true;
        var bindingBytes = AllocationMeasurement.Measure(() => success &= c.Bind().IsComplete);
        c.Binding.CheckStartup(Kimi.Compiler.OutputKind.Application);
        var bytes = AllocationMeasurement.Measure(() => success &= c.Analyze().IsVerified);
        Assert.True(success);
        Assert.Equal(0, bindingBytes);
        Assert.Equal(0, bytes);
    }

    // SPEC 3.5, 8.15.2 (R6-check (b2)): the owner case of a pair layer plans its target under the enclosing cases, a nested pair layer
    // included, in Binding's nested scratch, so a warm rebind of an owner case over `uniq/i32` and over `t/U` allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmBindOfANestedOwnerCaseAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze(
            "func exclusive<s/T>(value: s/(uniq/i32 during a)) -> ()\n    s is owner or uniq\n    let local = value\n    _ = local\n" +
            "func nested<t/U, s/T>(value: s/t/U) -> ()\n    s is owner or uniq\n    t is owner or uniq\n    U is Copy\n    let local = value\n" +
            "    _ = local\npublic func main() => ()\n");
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c));
        var success = true;
        var bytes = AllocationMeasurement.Measure(() => success &= c.Bind().IsComplete);
        Assert.True(success);
        Assert.Equal(0, bytes);
    }
}
