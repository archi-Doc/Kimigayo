// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

// SPEC 5.2.3: raw Places, and their Fields and elements, offer Take without tracked state. (*p)@move takes the value and leaves
// the storage Uninitialized, _ = (*p)@move destroys it in place, and a bare acquisition follows the rule for every Place: a
// Copy value is Copied and a Non-Copy or Copy-unproven value needs @move (SPEC 3.5).
public class RawPlaceAccessTest
{
    private const string Resource = "struct Resource\n    public var value: i32\n    public init(value: i32) => self.value = value\n    drop => Console.writeLine(\"drop\")\n";

    [Theory]
    [InlineData("let taken = *pointer")]
    [InlineData("let taken = pointer[0]")]
    [InlineData("_ = *pointer")]
    [InlineData("consume(*pointer)")]
    public void ABareNonCopyTakeIsRejected(string take)
    {
        var source = Resource + "func consume(value: Resource) => ()\nfunc run(pointer: raw/Resource)\n    unsafe\n        " + take + "\npublic func main() => ()\n";
        var diagnostic = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.TransferRequired_Kd), diagnostic.Code);
        if (take.Contains('*'))
        {
            Assert.Contains("Write (*pointer)@move", diagnostic.Advice);
        }
    }

    [Theory]
    [InlineData("func take<E>(pointer: raw/E) -> E\n    unsafe => return *pointer\n", false)]
    [InlineData("func take<E>(pointer: raw/E) -> E\n    unsafe => return (*pointer)@move\n", true)]
    [InlineData("func take(pointer: raw/i32) -> i32\n    unsafe => return *pointer\n", true)]
    public void ACopyUnprovenTakeNeedsMove(string function, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(function + "public func main() => ()\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(valid, c.Ownership.Result.IsVerified);
    }

    [Fact]
    public void AnExplicitTakeMovesAndDestroys()
    {
        var source = Resource + """
            group Native
                #LibraryImport("kernel32", "VirtualAlloc")
                public unsafe func allocate(address: raw/u8, size: u64, kind: u32, protect: u32) -> raw/u8
                #LibraryImport("kernel32", "VirtualFree")
                public unsafe func free(address: raw/u8, size: u64, kind: u32) -> i32
            public func main()
                unsafe
                    let bytes = Native.allocate(null, 4096, 12288, 4)
                    require bytes != null else => $abort("allocate")
                    let storage = bytes@raw/Resource
                    (*storage).value = 1
                    storage[1].value = 2
                    let taken = (*storage)@move
                    require taken.value == 1 else => $abort("take")
                    _ = storage[1]@move
                    Console.writeLine("taken")
                    require Native.free(bytes, 0, 32768) != 0 else => $abort("free")
                Console.writeLine("done")
            """;
        ScalarEmissionTest.EmitFixture("RawPlaceTake", source, "drop\ntaken\ndrop\ndone\n");
    }
}
