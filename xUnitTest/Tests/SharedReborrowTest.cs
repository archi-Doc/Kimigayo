// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class SharedReborrowTest
{
    [Fact]
    public void SharedMatchReborrowsStoredExclusiveReferences()
    {
        const string Source = """
            var number = 42
            let value = (number@uniq, "hello")
            match value
                (let saved, let text) if saved == 42 and text == "hello"
                    require saved == 42 else => $abort("body")
                    Console.writeLine(text)
                (_, _) => $abort("missing")
            number = 7
            require number == 7 else => $abort("last use")
            """;
        NativeAllocationAudit.WriteFixture("SharedReborrowMatch", Source, 0, 0, 0, "hello\n");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedTupleIterationReborrowsExclusiveReferences(bool dynamic)
    {
        const string Source = """
            var number = 42
            let values: [1 of (uniq/i32, i32)] = [(number@uniq, 1)]
            for (saved, tag) in values
                require saved == 42 and tag == 1 else => $abort("iteration")
            number = 7
            """;
        var source = dynamic ? Source.Replace("[1 of (uniq/i32, i32)]", "Array<(uniq/i32, i32)>", StringComparison.Ordinal) : Source;
        NativeAllocationAudit.WriteFixture("SharedReborrowIteration" + dynamic, source, dynamic ? 1 : 0, dynamic ? 1 : 0, dynamic ? 64 : 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASharedPatternReadDoesNotDuplicateObjectOwnership(bool borrow)
    {
        const string Source = """
            struct Item
                public let value: i32
                public init(value: i32) => self.value = value
                deinit => Console.writeLine("drop")
            var owner = Kimi.Intrinsics.makeObj<Item>(Item.init(42))
            let value = (owner@move, 1)
            match value
                (let view, let tag) if (view@follow@objref).value == 42 and tag == 1
                    require (view@follow@objref).value == 42 else => $abort("body")
                (_, _) => $abort("missing")
            Console.writeLine("done")
            """;
        var source = borrow ? Source.Replace("owner@move", "owner@objuniq", StringComparison.Ordinal) : Source;
        NativeAllocationAudit.WriteFixture("SharedReborrowObject" + borrow, source, 1, 1, 20, "done\ndrop\n");
    }

    [Fact]
    public void ReborrowKeepsParentAuthoritySuspendedUntilLastUse()
    {
        const string Source = "var number = 42\nlet value = (number@uniq, 1)\nmatch value\n    (let saved, _)\n        number = 7\n        require saved == 42 else => $abort(\"value\")";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }
}
