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
}
