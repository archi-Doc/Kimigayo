// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class DictionaryCostTest
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(17, false)]
    [InlineData(1000, false)]
    [InlineData(1, true)]
    [InlineData(17, true)]
    [InlineData(1000, true)]
    public void GrowthMovesOnlyLinearStorageAndUsesGeometricAllocations(int count, bool reserve)
    {
        var source = "var entries: Dictionary<i32, i32> = [:]\nvar i = 0\nwhile i < " + count + "\n" +
            (reserve ? "    entries.reserve(1)\n" : string.Empty) +
            "    _ = entries.tryInsert(i, i)\n    i += 1\nrequire entries.length == " + count + " else => $abort(\"length\")\nvar sum = 0\nfor (key, value) in entries@move\n    require key == value else => $abort(\"pair\")\n    sum += value\nrequire sum == " + ((long)count * (count - 1) / 2) + " else => $abort(\"sum\")";
        var capacity = 4;
        var allocations = 1;
        var bytes = 96L;
        while (capacity < count)
        {
            capacity *= 2;
            allocations++;
            bytes += capacity * 24L;
        }

        NativeAllocationAudit.WriteFixture("DictionaryCostGrowth" + count + reserve, source, allocations, allocations, bytes, maxTransferredBytes: count * 48L);
    }

    [Fact]
    public void RepeatedSearchAndSharedIterationAllocateNoEntryOrViewStorage()
    {
        const string Source = """
            var entries: Dictionary<i32, i32> = [:]
            entries.reserve(4)
            _ = entries.tryInsert(1, 10)
            _ = entries.tryInsert(2, 20)
            var count = 0
            while count < 1024
                _ = entries.tryInsert(1, 30)
                _ = entries.remove(3)
                let found = entries.tryGet(2)
                match found
                    .Some(let value) => require value == 20 else => $abort("lookup")
                    .None => $abort("missing")
                for (key, value) in entries => require value == key * 10 else => $abort("pair")
                count += 1
            """;
        NativeAllocationAudit.WriteFixture("DictionaryCostQueries", Source, 1, 1, 96, maxTransferredBytes: 0);
    }

    [Theory]
    [InlineData("Binding")]
    [InlineData("Ownership")]
    [InlineData("Emission")]
    [InlineData("Validation")]
    [InlineData("Pipeline")]
    [InlineData("Ownership", 1)]
    public void WarmDictionaryCompilationReusesStorage(string stage, int prefix = 0)
    {
        const string Source = """
            var entries: Dictionary<(i32, i32), i32> = [:]
            _ = entries.tryInsert((1, 2), 3)
            entries[(1, 2)] = 4
            let found = entries.tryGet((1, 2))
            match found
                .Some(let value) => require value == 4 else => $abort("value")
                .None => $abort("missing")
            for (key, value) in entries => require value == 4 else => $abort("shared")
            for (key, value) in entries@move => require value == 4 else => $abort("owner")
            """;
        var c = MinimalEmissionTest.Analyze(prefix == 0 ? Source : string.Join('\n', Source.Split('\n')[..prefix]));
        for (var i = 0; i < 32; i++)
        {
            Assert.True(c.Bind().IsComplete);
            c.Binding.CheckStartup(OutputKind.Application);
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        var bytes = AllocationMeasurement.Measure(() =>
        {
            if (stage == "Pipeline")
            {
                if (!c.Bind().IsComplete)
                {
                    throw new InvalidOperationException("Dictionary Binding failed.");
                }

                c.Binding.CheckStartup(OutputKind.Application);
                if (!c.Ownership.Analyze().IsVerified || !c.Emission.WriteIr(TextWriter.Null, out _))
                {
                    throw new InvalidOperationException("Dictionary compilation failed.");
                }

                return;
            }

            var valid = stage switch
            {
                "Binding" => c.Bind().IsComplete,
                "Ownership" => c.Ownership.Analyze().IsVerified,
                "Validation" => c.Emission.Validate(out _),
                _ => c.Emission.WriteIr(TextWriter.Null, out _),
            };
            if (!valid)
            {
                throw new InvalidOperationException("Dictionary compilation failed.");
            }
        });
        Assert.Equal(0, bytes);
    }
}
