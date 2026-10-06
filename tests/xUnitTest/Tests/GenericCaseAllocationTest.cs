// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Verification;
using Xunit;

namespace XunitTest;

// SPEC 8.10; PLAN G75 U6: the Semantics-case runs of a definition reuse their pooled side bodies, case vectors, binder lists,
// retentions and issue maps, so warm rebinding and ownership analysis of the case workload allocate nothing.
public sealed class GenericCaseAllocationTest
{
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmCaseRunsAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(VerificationWorkloads.PairCaseFamilies);
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
            c.Binding.CheckStartup(Kimi.Compiler.OutputKind.Application);
            Assert.True(c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        }

        var success = true;
        var bindingBytes = AllocationMeasurement.Measure(() => success &= c.Bind().IsComplete, iterations: 128, warmupIterations: 100);
        c.Binding.CheckStartup(Kimi.Compiler.OutputKind.Application);
        var bytes = AllocationMeasurement.Measure(() => success &= c.Ownership.Analyze().IsVerified, iterations: 128, warmupIterations: 100);
        Assert.True(success);
        Assert.Equal(0, bindingBytes);
        Assert.Equal(0, bytes);
    }
}
