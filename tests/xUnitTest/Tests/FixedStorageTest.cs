// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 22.1.2.5: borrowStorage over a fixed array yields the contiguous remainder of all N elements; shared steps
/// lend each element once in index order and exclusive steps split each element off as a child Loan.</summary>
public class FixedStorageTest
{
    // A probe inside the Kimi Kotonoha, since user source cannot reach Kimi.Storage.
    private const string Probe = """
        public group StorageProbe
            public func weighted(values: ref/[3 of i32]) -> i32
                var state = Storage.borrowStorage(values)
                var total: i32 = 0
                var position: i32 = 1
                loop
                    match Storage.splitFirst(state@uniq)
                        .Some(let item)
                            let value: i32 = item
                            total += value * position
                            position += 1
                        .None => exit
                return total
            public func bump(values: uniq/[3 of i32])
                var state = Storage.borrowStorage(values)
                loop
                    match Storage.splitFirst(state@uniq)
                        .Some(let item) => item@follow += 10
                        .None => exit
        """;

    [Fact]
    public void FixedArrayRemaindersTraverseEveryElementOnce()
    {
        var c = CompilationTestHelper.ParseSuccess("""
            var values: [3 of i32] = [1, 2, 3]
            require Kimi.StorageProbe.weighted(values@ref) == 14 else => $abort("shared")
            Kimi.StorageProbe.bump(values@uniq)
            require values[0] == 11 and values[1] == 12 and values[2] == 13 else => $abort("exclusive")
            require Kimi.StorageProbe.weighted(values@ref) == 74 else => $abort("order")
            Console.writeLine("fixed storage")
            """);
        c.Library.Kotonoha.CreateCodeContext().Parse(c.Library.Kotonoha.RootKoto, Probe);
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        ScalarEmissionTest.WriteFixture("FixedStorageTraversal", CompilationTestHelper.WriteIr(c), "fixed storage\n");
    }
}
