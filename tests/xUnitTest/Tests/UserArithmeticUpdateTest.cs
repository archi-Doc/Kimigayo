// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class UserArithmeticUpdateTest
{
    private const string Counter = """
        struct Counter
            Self is Addable<Counter>
            associate Output is Counter
            public let value: i32
            public init(value: i32) => self.value = value
            public func added(self: ref/Self, right: ref/Counter) -> Counter => Counter.init(self.value + right.value)
            drop => ()
        """;

    [Fact]
    public void ALocalUpdateEndsInspectionBeforeReplacement()
        => ScalarEmissionTest.EmitFixture("UserArithmeticUpdateLocal", Counter + "\nvar total = Counter.init(21)\ntotal += total\nrequire total.value == 42 else => $abort(\"self update\")", string.Empty);

    [Fact]
    public void GenericUpdateRetainsThePublishedContract()
    {
        const string Program = "\nfunc doubled<T>(input: T) -> T\n    T is Addable<T>\n    T.(Addable<T>).Output is T\n    var value = input@move\n    value += value\n    return value@move\nlet result = doubled(Counter.init(21))\nrequire result.value == 42 else => $abort(\"generic update\")";
        ScalarEmissionTest.EmitFixture("UserArithmeticUpdateGeneric", Counter + Program, string.Empty);
    }

    [Fact]
    public void RightHandSideCompletesBeforeTheOldValueIsBorrowed()
    {
        const string Program = "\nfunc right(target: uniq/Counter) -> Counter\n    target@follow = Counter.init(41)\n    return Counter.init(1)\nvar total = Counter.init(0)\ntotal += right(total@uniq)\nrequire total.value == 42 else => $abort(\"RHS first\")";
        ScalarEmissionTest.EmitFixture("UserArithmeticUpdateOrder", Counter + Program, string.Empty);
    }

    [Fact]
    public void NumericTargetUsesTheRightProvidersSelectedOutput()
    {
        const string Program = "struct Offset\n    Self is LeftAddable<i32>\n    associate Output is i32\n    public func addedFrom(left: ref/i32, self: ref/Self) -> i32 => left + 2\nvar total = 40\ntotal += Offset.init()\nrequire total == 42 else => $abort(\"numeric target\")";
        ScalarEmissionTest.EmitFixture("UserArithmeticUpdateNumeric", Program, string.Empty);
    }

    [Fact]
    public void AnIndependentLoanStillPreventsReplacement()
    {
        var c = MinimalEmissionTest.Analyze(Counter + "\nvar total = Counter.init(21)\nlet saved = total@ref\ntotal += total\nrequire saved.value == 21 else => $abort(\"saved\")");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.Contains(c.Ownership.Issues, static x => x.Code == DiagnosticCode.ComparisonLoanConflict_Kd);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmUpdatesReuseCallAndReplacementStorage()
    {
        var c = MinimalEmissionTest.Analyze(Counter + "\nvar total = Counter.init(21)\ntotal += total");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete, iterations: 64, warmupIterations: 32));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified, iterations: 64, warmupIterations: 32));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _), iterations: 64, warmupIterations: 32));
        Assert.True(valid);
    }
}
