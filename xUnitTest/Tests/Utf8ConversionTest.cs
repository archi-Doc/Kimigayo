// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class Utf8ConversionTest
{
    private const string User = "struct Value\n    Self is Utf8Format\n    public init() => ()\n    public func format(self: ref/Self, writer: uniq/Utf8Writer) -> Result<(), BufferFull> => writer.write(\"word\")\n";

    [Theory]
    [InlineData("Integer", "123", "123", 1, 11)]
    [InlineData("Wide", "340282366920938463463374607431768211455@u128", "340282366920938463463374607431768211455", 1, 39)]
    [InlineData("Bool", "true", "true", 1, 5)]
    [InlineData("Char", "'😀'", "😀", 1, 4)]
    [InlineData("Unit", "()", "()", 1, 2)]
    [InlineData("String", "\"a君😀\"", "a君😀", 1, 8)]
    [InlineData("EmptyString", "\"\"", "", 0, 0)]
    [InlineData("View", "Text.utf8(\"a君😀\")", "a君😀", 1, 8)]
    [InlineData("EmptyView", "Text.utf8(\"\")", "", 0, 0)]
    public void OwningBuiltinConversionAllocatesAtMostOnce(string name, string value, string expected, int allocations, int bytes)
        => NativeAllocationAudit.WriteFixture("Utf8Conversion" + name, "Console.writeLine(Text.toString(" + value + "))", allocations, allocations, bytes, expected + "\n");

    [Theory]
    [InlineData("Builtin", "", "123", "123", 11)]
    [InlineData("User", User, "Value.init()", "word", 4)]
    public void GenericOwningConversionSelectsTheFormatter(string name, string prefix, string value, string expected, int bytes)
    {
        var source = prefix + "func copy<T>(value: ref/T) -> string\n    T is Utf8Format\n    return Text.toString(value)\nConsole.writeLine(copy(" + value + "))";
        NativeAllocationAudit.WriteFixture("Utf8ConversionGeneric" + name, source, 1, 1, bytes, expected + "\n");
    }

    [Theory]
    [InlineData("Integer", "", "123", 3, "123")]
    [InlineData("Empty", "", "\"\"", 0, "")]
    [InlineData("String", "", "\"君\"", 3, "君")]
    [InlineData("User", User, "Value.init()", 4, "word")]
    public void FixedConversionReturnsAnAllocationFreeView(string name, string prefix, string value, int capacity, string expected)
    {
        var source = prefix + "var bytes = [" + capacity + " of 0@u8]\nmatch Text.tryFormat(" + value + ", bytes@uniq)\n    .Ok(let view) => Console.writeLine(view)\n    .Err(_) => $abort(\"full\")";
        NativeAllocationAudit.WriteFixture("Utf8ConversionFixed" + name, source, 0, 0, 0, expected + "\n");
    }

    [Fact]
    public void FixedFailureLeavesOnlyTheCompletedPrefix()
    {
        const string Source = """
            struct Value
                Self is Utf8Format
                public init() => ()
                public func format(self: ref/Self, writer: uniq/Utf8Writer) -> Result<(), BufferFull>
                    try writer.write("ab")
                    return writer.write("cd")
            var bytes = [3 of 0@u8]
            match Text.tryFormat(Value.init(), bytes@uniq)
                .Ok(_) => $abort("must fail")
                .Err(_) => ()
            require bytes[0] == 97@u8 and bytes[1] == 98@u8 and bytes[2] == 0@u8 else => $abort("partial prefix")
            """;
        NativeAllocationAudit.WriteFixture("Utf8ConversionFixedFailure", Source, 0, 0, 0);
    }

    [Theory]
    [InlineData("_ = bytes[0]\n_ = result@move", false)]
    [InlineData("_ = result@move\n_ = bytes[0]", true)]
    public void FixedResultRetainsExclusiveSourceAuthority(string use, bool valid)
    {
        var c = MinimalEmissionTest.Analyze("var bytes = [3 of 0@u8]\nlet result = Text.tryFormat(123, bytes@uniq)\n" + use);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(valid == c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        if (!valid)
        {
            Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        }
    }

    [Fact]
    public void StringCopyHasIndependentHeapStorage()
    {
        const string Source = """
            var copy = ""
            do
                let original = Text.toString("abc")
                copy = Text.toString(original)
            Console.writeLine(copy)
            """;
        NativeAllocationAudit.WriteFixture("Utf8ConversionStringCopy", Source, 2, 2, 6, "abc\n");
    }

    [Fact]
    public void OwningUserFailureAbortsWithFormatReason()
    {
        var source = User.Replace("writer.write(\"word\")", ".Err(BufferFull.init())", StringComparison.Ordinal) + "_ = Text.toString(Value.init())";
        ScalarEmissionTest.EmitFixture("Utf8ConversionAbort", source, string.Empty, 1, "Hello.kimi:5:5: abort KIMI_E_FORMAT: Formatting failed\n");
    }
}
