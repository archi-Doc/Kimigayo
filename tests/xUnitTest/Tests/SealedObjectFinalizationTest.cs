// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class SealedObjectFinalizationTest
{
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("Obj")]
    [InlineData("Rc")]
    [InlineData("Arc")]
    public void SealedPayloadsHaveDirectCleanupAndRelease(string factory)
    {
        var source = "struct Item\n    public let value: i32 = 7\n    drop => Console.writeLine(\"drop\")\n" +
            $"let owner = Kimi.Intrinsics.make{factory}(Item.init())\nrequire owner.value == 7 else => $abort(\"payload\")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Emission.TryPrepare(out var module, out var failure), MinimalEmissionTest.Describe(c, failure));
        var handle = Assert.Single(module.Aggregates, x => x.ObjectHandle is not null);
        Assert.True(handle.ObjectPayloadDrop >= 0);
        var ir = CompilationTestHelper.WriteIr(c);
        var cleanup = Function(ir, "__kimi_drop_aggregate" + handle.Id);
        Assert.Contains("call void @__kimi_drop_aggregate" + handle.ObjectPayloadDrop, cleanup, StringComparison.Ordinal);
        Assert.Contains("call void @__kimi_free(ptr %header", cleanup, StringComparison.Ordinal);
        Assert.DoesNotContain("%descriptor", cleanup, StringComparison.Ordinal);
        Assert.DoesNotContain("call void %", cleanup, StringComparison.Ordinal);
        if (factory == "Rc")
        {
            Assert.Contains("store i64 %next, ptr %control, align 8\n  %final = icmp eq i64 %old, 2", cleanup, StringComparison.Ordinal);
        }

        NativeAllocationAudit.WriteFixture("SealedObjectDirect" + factory, source, 1, 1, 20, "drop\n");
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("Obj", "7", -1, 20)]
    [InlineData("Rc", "7", -1, 20)]
    [InlineData("Obj", "\"value\"", -2, 40)]
    [InlineData("Rc", "\"value\"", -2, 40)]
    [InlineData("Arc", "7", -1, 20)]
    [InlineData("Arc", "\"value\"", -2, 40)]
    public void TrivialAndStringPayloadsKeepTheirDistinctCleanup(string factory, string value, int drop, int bytes)
    {
        var source = $"let owner = Kimi.Intrinsics.make{factory}({value})";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Emission.TryPrepare(out var module, out var failure), MinimalEmissionTest.Describe(c, failure));
        Assert.Equal(drop, Assert.Single(module.Aggregates, x => x.ObjectHandle is not null).ObjectPayloadDrop);
        NativeAllocationAudit.WriteFixture("SealedObjectPayload" + factory + (drop == -1 ? "Trivial" : "String"), source, 1, 1, bytes);
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("Obj")]
    [InlineData("Rc")]
    [InlineData("Arc")]
    public void ZeroSizeStillDestroysTheLogicalPayload(string factory)
        => NativeAllocationAudit.WriteFixture("SealedObjectZero" + factory, "struct Item\n    drop => Console.writeLine(\"zero\")\n" + $"let owner = Kimi.Intrinsics.make{factory}(Item.init())", 1, 1, 16, "zero\n");

    [Theory]
    [InlineData("obj")]
    [InlineData("rc")]
    [InlineData("arc")]
    public void OpenTargetsRetainDynamicDestruction(string mode)
    {
        var c = MinimalEmissionTest.Analyze($"open struct Base\n    drop => Console.writeLine(\"base\")\nfunc consume(value: {mode}/Base) => ()\n()");
        Assert.True(c.Emission.TryPrepare(out var module, out var failure), MinimalEmissionTest.Describe(c, failure));
        var handle = Assert.Single(module.Aggregates, x => x.ObjectHandle is not null);
        Assert.Null(handle.ObjectPayloadDrop);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void DifferentSealedDestructorsAndModesKeepTheirPlansAcrossWarmEmission()
    {
        const string Source = """
            struct First
                drop => Console.writeLine("first")
            struct Second
                drop => Console.writeLine("second")
            let first = Kimi.Intrinsics.makeObj(First.init())
            let second = Kimi.Intrinsics.makeRc(Second.init())
            let clone = Kimi.Intrinsics.clone(second@ref)
            """;
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var failure), MinimalEmissionTest.Describe(c, failure));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _), iterations: 64, warmupIterations: 32));
        Assert.True(valid);
        NativeAllocationAudit.WriteFixture("SealedObjectMixed", Source, 2, 2, 32, "second\nfirst\n");
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecursivePayloadsKeepFinitePlansAndDestroyEveryNode(bool atomic)
    {
        const string Source = """
            struct Node
                public let next: Option<rc/Node>
                public init(next: Option<rc/Node>) => self.next = next@move
                drop => Console.writeLine("node")
            let tail = Kimi.Intrinsics.makeRc(Node.init(Option<rc/Node>.None))
            let head = Kimi.Intrinsics.makeRc(Node.init(Option<rc/Node>.Some(tail@move)))
            """;
        var source = atomic ? SharedObjectRuntimeTest.ArcSource(Source) : Source;
        var c = MinimalEmissionTest.Analyze(source);
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _), iterations: 64, warmupIterations: 32));
        Assert.True(valid);
        NativeAllocationAudit.WriteFixture("SealedObjectRecursive" + (atomic ? "Arc" : "Rc"), source, 2, 2, 64, "node\nnode\n");
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void NestedSealedPayloadsReleaseTheirOwnAllocations()
    {
        const string Source = """
            struct Item
                drop => Console.writeLine("item")
            let child = Kimi.Intrinsics.makeRc(Item.init())
            let parent = Kimi.Intrinsics.makeObj((child@move, 7))
            """;
        NativeAllocationAudit.WriteFixture("SealedObjectNested", Source, 2, 2, 48, "item\n");
    }

    private static string Function(string ir, string name)
    {
        var body = ir[ir.IndexOf("define internal void @" + name + "(", StringComparison.Ordinal)..];
        return body[..body.IndexOf("\n}", StringComparison.Ordinal)];
    }
}
