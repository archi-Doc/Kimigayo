// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class ObjectFieldUpdateTest
{
    private const string Item = "struct Item\n    public var id: i32 = 7\n    drop => Console.writeLine(\"drop\")\n";

    [Theory]
    [InlineData("Assign", "owner.id = owner.id + 2", "owner.id == 9")]
    [InlineData("Add", "owner.id += 2", "owner.id == 9")]
    [InlineData("Post", "let previous = owner.id++", "previous == 7 and owner.id == 8")]
    [InlineData("Pre", "let next = ++owner.id", "next == 8 and owner.id == 8")]
    [InlineData("View", "let view = owner@objuniq\nview.id += 2", "owner.id == 9")]
    [InlineData("Stored", "var pair = (owner@move, 1)\npair.0.id = pair.0.id + 2\npair.0.id++", "pair.0.id == 10")]
    public void UpdatesUseTheCheckedExclusiveFieldAddress(string name, string update, string check)
        => NativeAllocationAudit.WriteFixture("ObjectFieldUpdate" + name, Item + "var owner = Kimi.Intrinsics.makeObj(Item.init())\n" + update + "\nrequire " + check + " else => $abort(\"update\")", 1, 1, 20, "drop\n");

    [Fact]
    public void ReplacementDestroysTheOldFieldBeforeTheFinalField()
    {
        const string Source = """
            struct Resource
                let id: i32
                public init(id: i32) => self.id = id
                drop
                    if self.id == 1 => Console.writeLine("old")
                    else => Console.writeLine("new")
            struct Holder
                public var item: Resource = Resource.init(1)
            var owner = Kimi.Intrinsics.makeObj(Holder.init())
            owner.item = Resource.init(2)
            Console.writeLine("replaced")
            """;
        NativeAllocationAudit.WriteFixture("ObjectFieldUpdateCleanup", Source, 1, 1, 20, "old\nreplaced\nnew\n");
    }

    [Theory]
    [InlineData("let owner = Kimi.Intrinsics.makeObj(Item.init())")]
    [InlineData("var owner = Kimi.Intrinsics.makeRc(Item.init())")]
    [InlineData("var original = Kimi.Intrinsics.makeObj(Item.init())\nlet owner = original@objref")]
    public void ReadOnlyPayloadsDoNotGainWriteAuthority(string declaration)
    {
        var c = MinimalEmissionTest.Analyze(Item + declaration + "\nowner.id = 9");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Generate(TextWriter.Null));
    }

    [Fact]
    public void AFieldLoanPreventsReplacement()
    {
        var c = MinimalEmissionTest.Analyze(Item + "var owner = Kimi.Intrinsics.makeObj(Item.init())\nlet borrowed = owner.id@ref\nowner.id = 9\nrequire borrowed == 7 else => $abort(\"live\")");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Contains(c.Problems, static x => x.Code.IsLoanConflict());
        Assert.DoesNotContain(c.Problems, static x => x.Code == DiagnosticCode.Unsupported_Kd);
        Assert.False(c.Generate(TextWriter.Null));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmFieldUpdatesAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Item + "var owner = Kimi.Intrinsics.makeObj(Item.init())\nowner.id = owner.id + 2\nowner.id++");
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Generate(TextWriter.Null)));
        Assert.True(valid);
    }
}
