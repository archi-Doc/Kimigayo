// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class Utf8WriterTest
{
    [Theory]
    [InlineData("Fixed", "var bytes = [8 of 0@u8]\nvar destination = Text.fixed(bytes@uniq)")]
    [InlineData("Heap", "var destination = Text.heap(0)")]
    [InlineData("User", "struct Destination\n    Self is BufferWriter\n    public init() => ()\n    public func reserve(self: uniq/Self, minimum: isize) -> Result<WriteWindow, BufferFull> => .Err(BufferFull.init())\nvar destination = Destination.init()")]
    public void CreatingAnAdapterDoesNotAllocateOrReserve(string name, string source)
        => NativeAllocationAudit.WriteFixture("Utf8WriterCreate" + name, source + "\nlet writer = Text.writer(destination@uniq)\nmatch writer.status()\n    .Ok(_) => ()\n    .Err(_) => $abort(\"initial status\")", 0, 0, 0);

    [Fact]
    public void GenericFactoryUsesConcreteStandardDispatch()
    {
        const string Source = """
            func make<W>(destination: uniq/W) -> Utf8Writer
                W is BufferWriter
                return Text.writer(destination)
            var destination = Text.heap(0)
            let writer = make(destination@uniq)
            match writer.status()
                .Ok(_) => ()
                .Err(_) => $abort("initial status")
            """;
        NativeAllocationAudit.WriteFixture("Utf8WriterCreateGeneric", Source, 0, 0, 0);
    }

    [Theory]
    [InlineData("_ = destination.length\n_ = writer.status()", false)]
    [InlineData("_ = writer.status()\n_ = destination.length", true)]
    public void AdapterBorrowsUntilLastUse(string use, bool valid)
    {
        var c = MinimalEmissionTest.Analyze("var destination = Text.heap(0)\nlet writer = Text.writer(destination@uniq)\n" + use);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(valid, c.Ownership.Result.IsVerified);
    }

    [Fact]
    public void ErasureKeepsNestedExclusiveLoans()
    {
        var c = MinimalEmissionTest.Analyze("var bytes = [4 of 0@u8]\nvar destination = Text.fixed(bytes@uniq)\nlet writer = Text.writer(destination@uniq)\n_ = bytes[0]\n_ = writer.status()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
    }
}
