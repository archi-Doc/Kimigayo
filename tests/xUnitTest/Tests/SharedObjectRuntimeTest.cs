// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Verification;
using Xunit;

namespace XunitTest;

public class SharedObjectRuntimeTest
{
    private const string Item = "struct Item\n    public let value: i32\n    public init(value: i32) => self.value = value\n    drop => Console.writeLine(\"drop\")\n";

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void RcCloneRetainsOneAllocationUntilTheFinalOwnerIsDestroyed()
    {
        const string Source = """
            struct Item
                public let value: i32
                public init(value: i32) => self.value = value
                drop => Console.writeLine("drop")
            do
                let first = Kimi.Intrinsics.makeRc(Item.init(7))
                do
                    let second = Kimi.Intrinsics.clone(first@ref)
                    require second.value == 7 and second is Item else => $abort("clone")
                Console.writeLine("alive")
                require first.value == 7 else => $abort("owner")
            Console.writeLine("done")
            """;
        NativeAllocationAudit.WriteFixture("SharedRcCloneLifetime", Source, 1, 1, 20, "alive\ndrop\ndone\n");
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void RcResultsOutliveTheirSourceSlotsAndReplacementReleasesTheOldOwner()
    {
        const string Source = Item + """
            func duplicate(value: ref/rc/Item) -> rc/Item => Kimi.Intrinsics.clone(value)
            let retained = label selected: do
                let local = Kimi.Intrinsics.makeRc(Item.init(7))
                exit to selected duplicate(local@ref)
            var current = retained@move
            require current.value == 7 else => $abort("escaped slot")
            current = Kimi.Intrinsics.makeRc(Item.init(9))
            require current.value == 9 else => $abort("replacement")
            Console.writeLine("replaced")
            """;
        NativeAllocationAudit.WriteFixture("SharedRcResultsReplacement", Source, 2, 2, 40, "drop\nreplaced\ndrop\n");
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ObjAndRcSharePayloadMetadataButKeepTheirOwnCleanup()
        => NativeAllocationAudit.WriteFixture("SharedRcMixedModes", Item + "let exclusive = Kimi.Intrinsics.makeObj(Item.init(1))\nlet shared = Kimi.Intrinsics.makeRc(Item.init(2))\nlet clone = Kimi.Intrinsics.clone(shared@ref)\nrequire exclusive.value == 1 and clone.value == 2 else => $abort(\"modes\")", 2, 2, 40, "drop\ndrop\n");

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmRcAnalysisAndEmissionAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(VerificationWorkloads.RcClone);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var issue), MinimalEmissionTest.Describe(c, issue));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified, iterations: 64, warmupIterations: 32));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _), iterations: 64, warmupIterations: 32));
        Assert.True(valid);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RcRetainChecksTheMaximumBeforeUpdating(bool maximum)
    {
        const string Source = "let first = Kimi.Intrinsics.makeRc(7)\nlet second = Kimi.Intrinsics.clone(first@ref)\nConsole.writeLine(\"retained\")";
        var c = MinimalEmissionTest.Analyze(Source);
        var ir = CompilationTestHelper.WriteIr(c);
        const string InitialControl = "store i64 %controlValue, ptr %control, align 8";
        Assert.Contains(InitialControl, ir, StringComparison.Ordinal);
        // Exercise the real generated transition at otherwise impractically large counts. Only the unpublished
        // initial control is injected; the checked clone, call location and failure runtime remain unchanged.
        ir = ir.Replace(InitialControl, "store i64 " + (maximum ? "-2" : "-4") + ", ptr %control, align 8", StringComparison.Ordinal);
        var stderr = maximum ? "Hello.kimi:2:14: abort KIMI_E_REF_COUNT: Reference count limit exceeded\n" : string.Empty;
        ScalarEmissionTest.WriteFixture("SharedRcCount" + (maximum ? "Maximum" : "BelowMaximum"), ir, maximum ? string.Empty : "retained\n", maximum ? 1 : 0, stderr);
    }

    [Fact]
    public void RcGeneratedTransitionsCheckBeforeRetentionAndPublishZeroBeforeFinalization()
    {
        var ir = CompilationTestHelper.WriteIr(MinimalEmissionTest.Analyze("let value = Kimi.Intrinsics.makeRc(7)\nlet copy = Kimi.Intrinsics.clone(value@ref)"));
        var clone = ir[ir.IndexOf("define internal void @__kimi_clone_rc(", StringComparison.Ordinal)..];
        clone = clone[..clone.IndexOf("\n}", StringComparison.Ordinal)];
        Assert.Contains("%maximum = icmp eq i64 %old, -2\n  br i1 %maximum, label %overflow, label %retain\nretain:\n  %next = add i64 %old, 2\n  store i64 %next, ptr %control", clone, StringComparison.Ordinal);
        Assert.DoesNotContain("store ", clone[(clone.IndexOf("overflow:\n", StringComparison.Ordinal) + 10)..], StringComparison.Ordinal);
        var release = ir[ir.IndexOf("define internal void @__kimi_drop_rc(", StringComparison.Ordinal)..];
        release = release[..release.IndexOf("\n}", StringComparison.Ordinal)];
        Assert.Contains("store i64 %next, ptr %control, align 8\n  %final = icmp eq i64 %old, 2\n  br i1 %final, label %destroy, label %done", release, StringComparison.Ordinal);
        Assert.EndsWith("call void @__kimi_drop_object(ptr %slot, ptr %location, i64 %length)\n  br label %done\ndone:\n  ret void", release, StringComparison.Ordinal);
        Assert.DoesNotContain("atomic", clone + release, StringComparison.Ordinal);
    }
}
