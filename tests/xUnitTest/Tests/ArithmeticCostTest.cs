// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Verification;
using Xunit;

namespace XunitTest;

public class ArithmeticCostTest
{
    public static TheoryData<string> Workloads => new(ArithmeticWorkloads.Names);

    [Trait("Purpose", "Allocation")]
    [Theory]
    [MemberData(nameof(Workloads))]
    public void EquivalentArithmeticPathsReuseCompilerStorageAndAllocateNoRuntimeHeap(string name)
    {
        var source = ArithmeticWorkloads.Create(name);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete, iterations: 64, warmupIterations: 32));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified, iterations: 64, warmupIterations: 32));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _), iterations: 64, warmupIterations: 32));
        Assert.True(valid);
        NativeAllocationAudit.WriteFixture("ArithmeticCost" + name, source, 0, 0, 0, "ok\n");
    }
}
