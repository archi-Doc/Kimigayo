// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class EnumLocalOriginTest
{
    [Theory]
    [InlineData("let n = 7\nlet value: Option<ref/i32> = .Some(n@ref)")]
    [InlineData("struct Item\n    public let id: i32 = 7\nlet owner = Kimi.Intrinsics.makeObj(Item.init())\nlet value: Option<objref/Item> = .Some(owner)")]
    [InlineData("let n = 7\nlet value: Option<(ref/i32, i32)> = .Some((n@ref, 9))")]
    [InlineData("enum Pair<T>\n    Both(T, T)\nlet a = 7\nlet b = 9\nlet value: Pair<ref/i32> = .Both(a@ref, b@ref)")]
    public void LocalAnnotationsInferTheAcquiredPayloadOrigins(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Emission.Validate(out var issue), MinimalEmissionTest.Describe(c, issue));
    }

    [Theory]
    [InlineData("let n = 7\nlet value: Option<ref/i32 during static> = .Some(n@ref)")]
    [InlineData("func bad(n: ref/i32) -> Option<ref/i32 during static> => .Some(n)")]
    public void PublishedAndExplicitOriginsRemainFixed(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Emission.Validate(out _));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void InferredObjectPayloadKeepsItsOwnerWithoutAllocation()
    {
        const string Source = "struct Item\n    public let id: i32 = 7\n    drop => Console.writeLine(\"drop\")\nlet owner = Kimi.Intrinsics.makeObj(Item.init())\nlet value: Option<objref/Item> = .Some(owner)\nmatch value@copy\n    .Some(let view)\n        require view.id == 7 else => $abort(\"view\")\n    .None => $abort(\"missing\")";
        NativeAllocationAudit.WriteFixture("EnumLocalOriginObject", Source, 1, 1, 20, "drop\n");
    }

    [Fact]
    public void InferredPayloadRetainsTheLoan()
    {
        var c = MinimalEmissionTest.Analyze("struct Item\n    public let id: i32 = 7\nlet owner = Kimi.Intrinsics.makeObj(Item.init())\nlet value: Option<objref/Item> = .Some(owner)\nlet moved = owner@move\nmatch value@copy\n    .Some(let view)\n        require view.id == 7 else => $abort(\"view\")\n    .None => $abort(\"missing\")");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void RebindingReusesTheInferenceAndAcquisitionPlan()
    {
        const string Source = "let n = 7\nlet value: Option<ref/i32> = .Some(n@ref)\nmatch value\n    .Some(let view)\n        require view == 7 else => $abort(\"view\")\n    .None => $abort(\"missing\")";
        var c = MinimalEmissionTest.Analyze(Source);
        var expected = CompilationTestHelper.WriteIr(c);
        ScalarEmissionTest.WriteFixture("EnumLocalOriginScalar", expected, string.Empty);
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete, iterations: 64, warmupIterations: 32));
        Assert.True(valid);
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(expected, CompilationTestHelper.WriteIr(c));
    }

    [Fact]
    public void BorrowedObjectViewReceiverUsesItsStoredReference()
    {
        var c = MinimalEmissionTest.Analyze("struct Item\n    public let id: i32 = 7\nlet owner = Kimi.Intrinsics.makeObj(Item.init())\nlet value = Option.Some(owner@objref)\nmatch value\n    .Some(let view)\n        require view.id == 7 else => $abort(\"view\")\n    .None => $abort(\"missing\")");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Emission.WriteIr(new StringWriter(), out var issue), MinimalEmissionTest.Describe(c, issue));
    }
}
