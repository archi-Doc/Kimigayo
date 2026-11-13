// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class Utf8WriterTest
{
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("Fixed", "var bytes = [8 of 0@u8]\nvar destination = Text.fixed(bytes@uniq)")]
    [InlineData("Heap", "var destination = Text.heap(0)")]
    [InlineData("User", "struct Destination\n    Self is BufferWriter\n    public init() => ()\n    public func reserve(self: uniq/Self, minimum: isize) -> Result<WriteWindow, BufferFull> => .Err(BufferFull.init())\nvar destination = Destination.init()")]
    public void CreatingAnAdapterDoesNotAllocateOrReserve(string name, string source)
        => NativeAllocationAudit.WriteFixture("Utf8WriterCreate" + name, source + "\nlet writer = Text.writer(destination@uniq)\nmatch writer.status()@move\n    .Ok(_) => ()\n    .Err(_) => $abort(\"initial status\")", 0, 0, 0);

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void GenericFactoryUsesConcreteStandardDispatch()
    {
        const string Source = """
            func make<W>(destination: uniq/W) -> Utf8Writer
                W is BufferWriter
                return Text.writer(destination)
            var destination = Text.heap(0)
            let writer = make(destination@uniq)
            match writer.status()@move
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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Equal(valid, c.Analysis.IsVerified);
    }

    [Fact]
    public void ErasureKeepsNestedExclusiveLoans()
    {
        var c = MinimalEmissionTest.Analyze("var bytes = [4 of 0@u8]\nvar destination = Text.fixed(bytes@uniq)\nlet writer = Text.writer(destination@uniq)\n_ = bytes[0]\n_ = writer.status()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Contains(c.Problems, x => x.Code.IsLoanConflict());
    }

    // SPEC 22.5.1, 22.10.1, 8.13.2 (R8-W1b-4, R8-S2a, R8-S2c, R8-S2d1, R8-S2b1, R8-S2b2): Utf8Writer.write, the witness table's bool, char,
    // Unit, f32, f64, string, Wrapping<T> and integer bodies and the view's format have CallerLocation rows, which IntegerFormat's i128 and
    // u128 specializations keep (TyCtxt.Original), so an allocation that fails inside a built-in write's leaf write Aborts at the user's
    // write, not at a library line.
    [Theory]
    [InlineData("", "12345")]
    [InlineData("Bool", "true")]
    [InlineData("Char", "'é'")]
    [InlineData("Unit", "()")]
    [InlineData("F32", "1.5@f32")]
    [InlineData("F64", "1.5")]
    [InlineData("String", "\"hello\"")]
    [InlineData("View", "Text.utf8(\"hello\")")]
    [InlineData("Wrapping", "(200@u8)@Wrapping<u8>")]
    [InlineData("U64", "18446744073709551615@u64")]
    [InlineData("I8", "-128@i8")]
    [InlineData("I128", "-170141183460469231731687303715884105728@i128")]
    [InlineData("U128", "340282366920938463463374607431768211455@u128")]
    public void AFailedAllocationInsideABuiltinWriteReportsTheWrite(string name, string value)
    {
        var ir = CompilationTestHelper.WriteIr(MinimalEmissionTest.Analyze("var buffer = Text.heap(0)\nvar writer = Text.writer(buffer@uniq)\n_ = writer.write(" + value + ")"));
        Assert.Contains("call ptr @HeapAlloc(", ir, StringComparison.Ordinal);
        ir = ir.Replace("call ptr @HeapAlloc(", "call ptr @fail_allocate(", StringComparison.Ordinal);
        ir += "\ndefine internal ptr @fail_allocate(ptr %heap, i32 %flags, i64 %size) { ret ptr null }\n";
        ScalarEmissionTest.WriteFixture("Utf8WriterBuiltinWriteFailure" + name, ir, string.Empty, 1, "Hello.kimi:3:5: abort KIMI_E_ALLOC: Failed to allocate memory\n");
    }
}
