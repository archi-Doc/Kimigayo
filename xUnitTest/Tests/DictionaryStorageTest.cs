// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class DictionaryStorageTest
{
    [Fact]
    public void EmptyConstructionAndZeroReserveAllocateNothing()
        => NativeAllocationAudit.WriteFixture("DictionaryStorageEmpty", "var entries: Dictionary<i32, i32> = [:]\nrequire entries.length == 0 and entries.capacity == 0 else => $abort(\"empty\")\nentries.reserve(0)", 0, 0, 0);

    [Fact]
    public void SufficientReserveReusesTheBufferAndPreservesLength()
        => NativeAllocationAudit.WriteFixture("DictionaryStorageReserve", "var entries: Dictionary<i32, i32> = [:]\nentries.reserve(3)\nlet capacity = entries.capacity\nentries.reserve(0)\nentries.reserve(3)\nrequire entries.length == 0 and entries.capacity == capacity and capacity >= 3 else => $abort(\"capacity\")", 1, 1, 96);

    [Fact]
    public void GrowthReleasesThePreviousBuffer()
        => NativeAllocationAudit.WriteFixture("DictionaryStorageGrowth", "var entries: Dictionary<i32, i32> = [:]\nentries.reserve(3)\nentries.reserve(5)\nrequire entries.length == 0 and entries.capacity >= 5 else => $abort(\"growth\")", 2, 2, 288);

    [Fact]
    public void OwnedArgumentsResultsAndReplacementReleaseExactlyOnce()
        => NativeAllocationAudit.WriteFixture("DictionaryStorageTransfer", "func forward(value: Dictionary<i32, i32>) -> Dictionary<i32, i32> => value@move\nvar entries: Dictionary<i32, i32> = [:]\nentries.reserve(3)\nvar result = forward(entries@move)\nrequire result.capacity >= 3 and result.length == 0 else => $abort(\"move\")\nresult = [:]", 1, 1, 96);

    [Fact]
    public void BorrowedMetadataReadsTheSameHandle()
        => NativeAllocationAudit.WriteFixture("DictionaryStorageBorrow", "func length(value: ref/Dictionary<i32, i32>) -> isize => value.length\nvar entries: Dictionary<i32, i32> = [:]\nentries.reserve(3)\nrequire length(entries) == 0 else => $abort(\"borrow\")", 1, 1, 96);

    [Theory]
    [InlineData("let values: Array<Dictionary<i32, i32>> = [[:]]")]
    [InlineData("let entries: Dictionary<i32, i32> = [1: 2]")]
    [InlineData("var entries: Dictionary<i32, i32> = [:]\n_ = entries.tryInsert(1, 2)")]
    public void UnimplementedStorageOperationsRefuseGeneration(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }
}
