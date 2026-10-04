// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class SharedArcRuntimeTest
{
    private const string Scalar = "let owner = Kimi.Intrinsics.makeArc(7)\nlet copy = Kimi.Intrinsics.clone(owner@ref)\nrequire copy@follow == 7 else => $abort(\"payload\")";

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void CloneKeepsThePayloadAliveUntilFinalRelease()
    {
        const string Source = """
            struct Item
                public let value: i32 = 7
                drop => Console.writeLine("drop")
            do
                let owner = Kimi.Intrinsics.makeArc(Item.init())
                do
                    let copy = Kimi.Intrinsics.clone(owner@ref)
                    require copy.value == 7 and copy is Item else => $abort("clone")
                Console.writeLine("alive")
                require owner.value == 7 else => $abort("owner")
            Console.writeLine("done")
            """;
        NativeAllocationAudit.WriteFixture("SharedArcCloneLifetime", Source, 1, 1, 20, "alive\ndrop\ndone\n");
    }

    [Fact]
    public void GeneratedTransitionsKeepTheRequiredOrderingAndSuccessConditions()
    {
        var c = MinimalEmissionTest.Analyze(Scalar);
        Assert.True(c.Emission.TryPrepare(out var module, out var failure), MinimalEmissionTest.Describe(c, failure));
        var ir = CompilationTestHelper.WriteIr(c);
        Assert.Contains("store i64 %controlValue, ptr %control, align 8", ir, StringComparison.Ordinal);
        var clone = Function(ir, "__kimi_clone_arc");
        Assert.Contains("%initial = load atomic i64, ptr %control monotonic, align 8", clone, StringComparison.Ordinal);
        Assert.Contains("%old = phi i64 [ %initial, %entry ], [ %observed, %retain ]", clone, StringComparison.Ordinal);
        Assert.Contains("%maximum = icmp eq i64 %old, -2\n  br i1 %maximum, label %overflow, label %retain", clone, StringComparison.Ordinal);
        Assert.Contains("cmpxchg ptr %control, i64 %old, i64 %next monotonic monotonic, align 8", clone, StringComparison.Ordinal);
        Assert.Contains("br i1 %updated, label %publish, label %check\npublish:\n  store ptr %header, ptr %ret", clone, StringComparison.Ordinal);
        Assert.DoesNotContain("store ", clone[clone.IndexOf("overflow:", StringComparison.Ordinal)..], StringComparison.Ordinal);
        var dynamicRelease = Function(ir, "__kimi_drop_arc");
        var handle = Assert.Single(module.Aggregates, x => x.ObjectHandle?.Counting == ObjectCountingStep.Atomic);
        var sealedRelease = Function(ir, "__kimi_drop_aggregate" + handle.Id);
        foreach (var release in new[] { dynamicRelease, sealedRelease })
        {
            Assert.Contains("load atomic i64, ptr %control monotonic, align 8", release, StringComparison.Ordinal);
            Assert.Contains("%old = phi i64 [ %initial, %entry ], [ %observed, %release ]", release, StringComparison.Ordinal);
            Assert.Contains("cmpxchg ptr %control, i64 %old, i64 %next release monotonic, align 8", release, StringComparison.Ordinal);
            Assert.Contains("br i1 %updated, label %released, label %release\nreleased:\n  %final = icmp eq i64 %old, 2", release, StringComparison.Ordinal);
            Assert.Contains("br i1 %final, label %destroy, label %done\ndestroy:\n  fence acquire", release, StringComparison.Ordinal);
            Assert.DoesNotContain("atomicrmw", release, StringComparison.Ordinal);
            Assert.DoesNotContain("store i64", release, StringComparison.Ordinal);
        }

        Assert.EndsWith("call void @__kimi_drop_object(ptr %slot, ptr %location, i64 %length)\n  br label %done\ndone:\n  ret void", dynamicRelease, StringComparison.Ordinal);
        Assert.EndsWith("call void @__kimi_free(ptr %header, ptr %location, i64 %length)\n  br label %done\ndone:\n  ret void", sealedRelease, StringComparison.Ordinal);
        Assert.DoesNotContain("atomicrmw", clone, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CloneChecksTheMaximumBeforeUpdating(bool maximum)
    {
        const string Source = "let owner = Kimi.Intrinsics.makeArc(7)\nlet copy = Kimi.Intrinsics.clone(owner@ref)\nConsole.writeLine(\"retained\")";
        var ir = CompilationTestHelper.WriteIr(MinimalEmissionTest.Analyze(Source));
        const string Initial = "store i64 %controlValue, ptr %control, align 8";
        Assert.Contains(Initial, ir, StringComparison.Ordinal);
        ir = ir.Replace(Initial, "store i64 " + (maximum ? "-2" : "-4") + ", ptr %control, align 8", StringComparison.Ordinal);
        ScalarEmissionTest.WriteFixture(
            "SharedArcCount" + (maximum ? "Maximum" : "BelowMaximum"),
            ir,
            maximum ? string.Empty : "retained\n",
            maximum ? 1 : 0,
            maximum ? "Hello.kimi:2:12: abort KIMI_E_REF_COUNT: Reference count limit exceeded\n" : string.Empty);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void GenericFactoryAndCloneShareTheConcretePlans()
    {
        const string Source = "func box<T>(value: T) -> arc/T\n    T is ObjectPayload\n    return Kimi.Intrinsics.makeArc(value@move)\n" +
            "func duplicate<T>(value: ref/arc/T) -> arc/T\n    T is ObjectPayload\n    return Kimi.Intrinsics.clone(value)\n" +
            "let owner = box(7)\nlet copy = duplicate(owner@ref)\nrequire copy@follow == 7 else => $abort(\"payload\")";
        NativeAllocationAudit.WriteFixture("SharedArcGenericFactory", Source, 1, 1, 20);
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void FailedComparisonsRetryWithoutPublishingOrFinalizing(bool dynamic, bool smaller)
    {
        var source = (dynamic ? "open " : string.Empty) + "struct Item\n    public let value: i32 = 7\n    drop => Console.writeLine(\"drop\")\n" +
            "do\n    let owner = Kimi.Intrinsics.makeArc(Item.init())\n    do\n        let copy = Kimi.Intrinsics.clone(owner@ref)\n        require copy.value == 7 else => $abort(\"copy\")\n    Console.writeLine(\"alive\")\n    require owner.value == 7 else => $abort(\"owner\")\nConsole.writeLine(\"done\")";
        NativeAllocationAudit.WriteFixture("SharedArcRetry" + dynamic + smaller, source, 1, 1, 20, "alive\ndrop\ndone\n", transformIr: ir =>
        {
            // Model an intervening count update only by supplying a stale initial observation. The actual control,
            // CAS, returned observations and success branches remain production IR. Every first CAS must fail.
            const string Initial = "%initial = load atomic i64, ptr %control monotonic, align 8";
            Assert.Contains(Initial, Function(ir, "__kimi_clone_arc"), StringComparison.Ordinal);
            Assert.Contains(Initial, Function(ir, "__kimi_drop_arc"), StringComparison.Ordinal);
            var stale = "%sampled = load atomic i64, ptr %control monotonic, align 8\n  %larger = add i64 %sampled, 2\n" +
                (smaller ? "  %smaller = sub i64 %sampled, 2\n  %multiple = icmp ugt i64 %sampled, 2\n  %initial = select i1 %multiple, i64 %smaller, i64 %larger" : "  %initial = add i64 %larger, 0");
            return ir.Replace(Initial, stale, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void RetainRechecksTheMaximumAfterAFailedComparison()
    {
        const string Source = "let owner = Kimi.Intrinsics.makeArc(7)\nlet copy = Kimi.Intrinsics.clone(owner@ref)\nConsole.writeLine(\"unreachable\")";
        var ir = CompilationTestHelper.WriteIr(MinimalEmissionTest.Analyze(Source));
        const string Initial = "store i64 %controlValue, ptr %control, align 8";
        Assert.Contains(Initial, ir, StringComparison.Ordinal);
        ir = ir.Replace(Initial, "store i64 -2, ptr %control, align 8", StringComparison.Ordinal);
        var clone = Function(ir, "__kimi_clone_arc");
        const string Load = "%initial = load atomic i64, ptr %control monotonic, align 8";
        Assert.Contains(Load, clone, StringComparison.Ordinal);
        // A retain from the preceding count lost the race to another retain reaching the maximum.
        ir = ir.Replace(clone, clone.Replace(Load, "%initial = add i64 -4, 0", StringComparison.Ordinal), StringComparison.Ordinal);
        ScalarEmissionTest.WriteFixture("SharedArcRetryMaximum", ir, string.Empty, 1, "Hello.kimi:2:12: abort KIMI_E_REF_COUNT: Reference count limit exceeded\n");
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmAnalysisAndEmissionAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Scalar);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var issue), MinimalEmissionTest.Describe(c, issue));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified, iterations: 64, warmupIterations: 32));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _), iterations: 64, warmupIterations: 32));
        Assert.True(valid);
    }

    private static string Function(string ir, string name)
    {
        var body = ir[ir.IndexOf("define internal void @" + name + "(", StringComparison.Ordinal)..];
        return body[..body.IndexOf("\n}", StringComparison.Ordinal)];
    }
}
