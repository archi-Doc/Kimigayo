// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Verification;
using Xunit;

namespace XunitTest;

public class InheritedPlanReuseTest
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(8, 1)]
    [InlineData(32, 1)]
    [InlineData(4, 8)]
    [InlineData(4, 32)]
    public void DeepAndWideStorageRetainsFieldIdentity(int depth, int width)
        => ScalarEmissionTest.EmitFixture($"InheritedPlans{depth}x{width}", VerificationWorkloads.InheritedPlans(depth, width), string.Empty);

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(32, 1)]
    [InlineData(4, 32)]
    public void WarmFieldAndBasePlansAllocateNothing(int depth, int width)
    {
        var c = MinimalEmissionTest.Analyze(VerificationWorkloads.InheritedPlans(depth, width));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), MinimalEmissionTest.Describe(c, error));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid);
    }

    [Theory]
    [InlineData("func read(self: ref/Self) -> i32", "public func read(self: ref/Self) -> i32 => 1", "x.read()")]
    [InlineData("property item: i32 has get", "public computed item: i32\n        get(self: ref/Self) -> i32 => 1", "x.item")]
    public void InheritedRequirementDispatchUsesItsVerifiedProjection(string requirement, string implementation, string use)
    {
        var source = "contract C\n    " + requirement + "\nopen struct Base\n    " + implementation + "\nstruct Leaf: Base\n    Self is C\n    public init() => ()\nfunc read<T>(x: ref/T) -> i32\n    T is C\n    return " + use + "\nlet x = Leaf.init()\nrequire read(x@ref) == 1 else => $abort(\"dispatch\")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        ScalarEmissionTest.EmitFixture("InheritedRequirement" + (use == "x.item" ? "Getter" : "Method"), source, string.Empty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GenericWitnessesRetainTheirResultLoan(bool getter)
    {
        var source = BorrowingWitness(getter);
        ScalarEmissionTest.EmitFixture("InheritedRequirementBorrow" + getter, source + "require r == 42 and x.extra == 7 else => $abort(\"borrow\")", string.Empty);
        var error = Assert.Single(DiagnosticCorpus.Check(source + "x.value = 0\n_ = r").Diagnostics);
        Assert.Equal("ComparisonLoanConflict_Kd", error.Code);
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WitnessProjectionPlansAreReused(bool getter)
    {
        var c = MinimalEmissionTest.Analyze(BorrowingWitness(getter) + "_ = r");
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), MinimalEmissionTest.Describe(c, error));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid);
        c = CompilationTestHelper.Reload(c);
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        c.Ownership.Analyze();
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out error), MinimalEmissionTest.Describe(c, error));
    }

    private static string BorrowingWitness(bool getter)
    {
        var requirement = getter ? "property view: ref/i32 has get" : "func view(self: ref/Self) -> ref/i32";
        var implementation = getter ? "public computed view: ref/i32\n        get(self: ref/Self) -> ref/i32 => self.value@ref" : "public func view(self: ref/Self) -> ref/i32 => self.value@ref";
        var use = getter ? "x.view" : "x.view()";
        return "contract C\n    " + requirement + "\nopen struct Base<T>\n    public var value: i32 = 42\n    " + implementation + "\nopen struct Middle<U>: Base<U>\n    protected init() => ()\nstruct Leaf<V>: Middle<V>\n    Self is C\n    public let extra: i64 = 7\n    public init() => ()\nfunc read<X>(x: ref/X) -> ref/i32\n    X is C\n    return " + use + "\nvar x = Leaf<bool>.init()\nlet r = read(x@ref)\n";
    }
}
