// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class AcquisitionCorrelationTest
{
    private const string Crossed = "struct C<T>\n    public init(value: (T, i32), code: i32) => ()\n    public init(value: ref/(i64, T), name: string) => ()\nlet x: (i64, i32) = (1, 2)\n";

    [Theory]
    [InlineData("let c = C.init(x, 0)")]
    [InlineData("let c = C.init(x, code: 0)")]
    [InlineData("let c = C.init(x)")]
    [InlineData("let c: C<i64> = C.init(x, 0)")]
    public void WholeGroupCorrelationPrecedesApplicability(string call)
    {
        var c = MinimalEmissionTest.Analyze(Crossed + call);
        Assert.Contains(c.Binding.Issues, x => x.Failure == BindingFailure.UnprovenAcquisitionCorrelation);
        Assert.DoesNotContain(c.Binding.Issues, x => x.Failure == BindingFailure.ParameterShapeMismatch);
    }

    [Fact]
    public void ExplicitConstructionFixesTheCorrelation()
    {
        var c = MinimalEmissionTest.Analyze(Crossed + "let c = C<i64>.init(x, 0)");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("let item = Item.init(42, 0)")]
    [InlineData("let n: i64 = 42\nlet item = Item.init(n, 0)")]
    [InlineData("let item: Item<i64> = Item.init(42, 0)")]
    public void SharedIndependentOrUnresolvedInputsEstablishTheContract(string use)
    {
        var c = MinimalEmissionTest.Analyze("struct Item<T>\n    public init(value: T, code: i32) => ()\n    public init(value: T, name: string) => ()\n" + use);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnAdditionalWitnessCannotMasqueradeAsACommonUnresolvedSymbol(bool constructor)
    {
        var group = constructor
            ? "struct Item<T>\n    public init(value: T, witness: Tag<T>) => ()\n    public init(value: T, witness: Tag<ref/T>) => ()\n"
            : "func pick<T>(value: T, witness: Tag<T>) => ()\nfunc pick<T>(value: T, witness: Tag<ref/T>) => ()\n";
        var source = "struct Tag<T>\n    public init() => ()\n" + group + "let witness = Tag<ref/i32>.init()\n" + (constructor ? "let item = Item.init" : "pick") + "(42, witness)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Contains(c.Binding.Issues, x => x.Failure == BindingFailure.UnprovenAcquisitionCorrelation);
    }

    [Fact]
    [Trait("Purpose", "Allocation")]
    public void RejectedCorrelationReusesContractsAndFacts()
    {
        var c = MinimalEmissionTest.Analyze(Crossed + "let c = C.init(x, 0)");
        var rejected = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => rejected &= !c.Bind().IsComplete));
        Assert.True(rejected);
        Assert.Contains(c.Binding.Issues, x => x.Failure == BindingFailure.UnprovenAcquisitionCorrelation);
    }
}
