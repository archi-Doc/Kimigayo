// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Verification;
using Xunit;

namespace XunitTest;

public class ConstructorInferenceReuseTest
{
    [Theory]
    [InlineData(4, false)]
    [InlineData(8, false)]
    [InlineData(16, false)]
    [InlineData(16, true)]
    public void FixedValidationReusesOnlyUnchangedObligations(int count, bool reverse)
    {
        var source = ConstructorInferenceWorkloads.Create("candidates", count, reverse: reverse);
        var fast = CompilationTestHelper.Parse(source);
        var reference = CompilationTestHelper.Parse(source);
        fast.Binding.MeasureCallInference = reference.Binding.MeasureCallInference = true;
        reference.Binding.UseConstructorReferenceCheck = true;
        Assert.True(fast.Bind().IsComplete, MinimalEmissionTest.Describe(fast, null));
        Assert.True(reference.Bind().IsComplete, MinimalEmissionTest.Describe(reference, null));
        Assert.Equal(0, fast.Binding.InferenceMetrics.FixedChecks);
        Assert.Equal(count, fast.Binding.InferenceMetrics.FixedReuses);
        Assert.Equal(count, reference.Binding.InferenceMetrics.FixedChecks);
        Assert.Equal(fast.Binding.InferenceMetrics.Mappings, reference.Binding.InferenceMetrics.Mappings);
        Assert.Equal(fast.Binding.InferenceMetrics.Correlations, reference.Binding.InferenceMetrics.Correlations);
        Assert.Equal(count * (count - 1) / 2, fast.Binding.InferenceMetrics.Correlations);
        Assert.Equal(Selected(fast), Selected(reference));

        static string Selected(Compilation c)
        {
            var call = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Single(x => x.BoundCall?.DeclaringType?.Symbol?.Name == "Box");
            return call.BoundCall!.Target.Declaration.ToString() + Binding.DiagnosticTypeName(call.BoundType!);
        }
    }

    [Theory]
    [InlineData("ambiguous", nameof(BindingFailure.Ambiguous))]
    [InlineData("unbound", nameof(BindingFailure.UnboundTypeArgument))]
    [InlineData("conflict", nameof(BindingFailure.NoApplicableCandidate))]
    [InlineData("correlation", nameof(BindingFailure.UnprovenAcquisitionCorrelation))]
    [InlineData("selection", nameof(BindingFailure.ConstructorSelectionChanged))]
    public void FailureWorkloadsKeepTheirIndependentExpectedOutcome(string axis, string expected)
    {
        var c = CompilationTestHelper.Parse(ConstructorInferenceWorkloads.Create(axis, 1));
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Failure.ToString() == expected);
        c.Binding.UseConstructorReferenceCheck = true;
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Failure.ToString() == expected);
    }

    [Theory]
    [InlineData("candidates", 16)]
    [InlineData("slots", 65)]
    [InlineData("depth", 8)]
    [InlineData("calls", 32)]
    [Trait("Purpose", "Allocation")]
    public void GrowingWorkloadsRetainBoundedStorageAndAllocateNothing(string axis, int count)
    {
        var c = CompilationTestHelper.Parse(ConstructorInferenceWorkloads.Create(axis, count));
        c.Binding.MeasureCallInference = true;
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        var before = c.Binding.InferenceMetrics;
        var complete = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => complete &= c.Bind().IsComplete));
        Assert.True(complete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(before, c.Binding.InferenceMetrics);
        Assert.InRange(before.SlotWordCapacity, 0, 256);
        Assert.InRange(before.ContractCapacity, 0, 256);
        Assert.InRange(before.DependencyCapacity, 0, 256);
    }
}
