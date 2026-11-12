// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class Utf8FormatTest
{
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("I8Min", "-128@i8", "-128")]
    [InlineData("U8Max", "255@u8", "255")]
    [InlineData("I16Min", "-32768@i16", "-32768")]
    [InlineData("U16Max", "65535@u16", "65535")]
    [InlineData("I32Min", "-2147483648@i32", "-2147483648")]
    [InlineData("U32Max", "4294967295@u32", "4294967295")]
    [InlineData("I64Min", "-9223372036854775808@i64", "-9223372036854775808")]
    [InlineData("U64Max", "18446744073709551615@u64", "18446744073709551615")]
    [InlineData("I128Min", "-170141183460469231731687303715884105728@i128", "-170141183460469231731687303715884105728")]
    [InlineData("U128Max", "340282366920938463463374607431768211455@u128", "340282366920938463463374607431768211455")]
    [InlineData("Zero", "0@isize", "0")]
    [InlineData("IsizeMin", "-9223372036854775808@isize", "-9223372036854775808")]
    [InlineData("UsizeMax", "18446744073709551615@usize", "18446744073709551615")]
    [InlineData("True", "true", "true")]
    [InlineData("False", "false", "false")]
    [InlineData("Unit", "()", "()")]
    [InlineData("Ascii", "'a'", "a")]
    [InlineData("TwoBytes", "'é'", "é")]
    [InlineData("ThreeBytes", "'君'", "君")]
    [InlineData("FourBytes", "'😀'", "😀")]
    [InlineData("Text", "\"a君😀\"", "a君😀")]
    [InlineData("View", "Text.utf8(\"a君😀\")", "a君😀")]
    [InlineData("Empty", "\"\"", "")]
    public void BuiltinFixedWritesHaveNoHeapAllocation(string name, string value, string expected)
        => NativeAllocationAudit.WriteFixture("Utf8Format" + name, Program("try (writer@uniq).write(" + value + ")"), 0, 0, 0, expected + "\n");

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("Number", "(123).format(writer@uniq)", "123")]
    [InlineData("String", "\"abc\".format(writer@uniq)", "abc")]
    [InlineData("Unit", "().format(writer@uniq)", "()")]
    [InlineData("Wrapping", "((-5@i32)@Wrapping<i32>).format(writer@uniq)", "-5")]
    public void DirectFormatCallsUseTheSameEncoder(string name, string expression, string expected)
        => NativeAllocationAudit.WriteFixture("Utf8FormatDirect" + name, Program("try " + expression), 0, 0, 0, expected + "\n");

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("Integer", "123", "123")]
    [InlineData("String", "\"abc\"", "abc")]
    [InlineData("View", "Text.utf8(\"君\")", "君")]
    [InlineData("Unit", "()", "()")]
    public void GenericRequirementUsesTheBuiltinWitness(string name, string value, string expected)
    {
        const string Prefix = """
            group Helpers
                public func append<T>(value: ref/T, writer: uniq/Utf8Writer) -> Result<(), BufferFull>
                    T is Utf8Format
                    return value.format(writer)
            """;
        NativeAllocationAudit.WriteFixture("Utf8FormatRequirement" + name, Prefix + "\n" + Program("try Helpers.append(" + value + ", writer@uniq)"), 0, 0, 0, expected + "\n");
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void FailureIsStickyIncludingEmptyWrites()
    {
        const string Source = """
            var bytes = [3 of 0@u8]
            var buffer = Text.fixed(bytes@uniq)
            var writer = Text.writer(buffer@uniq)
            match (writer@uniq).write("ab")@move
                .Ok(_) => ()
                .Err(_) => $abort("first")
            match (writer@uniq).write(12)@move
                .Ok(_) => $abort("overflow")
                .Err(_) => ()
            match (writer@uniq).write("")@move
                .Ok(_) => $abort("empty must fail")
                .Err(_) => ()
            match writer.status()@move
                .Ok(_) => $abort("sticky status")
                .Err(_) => ()
            require buffer.length == 2 else => $abort("partial prefix")
            match (buffer@move).intoText()@move
                .Ok(let view) => Console.writeLine(view)
                .Err(_) => $abort("utf8")
            """;
        NativeAllocationAudit.WriteFixture("Utf8FormatSticky", Source, 0, 0, 0, "ab\n");
    }

    // SPEC 22.10.1 (design review 2, R2-B2; R8-S2d1): a direct `format` has no failure test of its own, only the adapter `write` has, so
    // on a failed writer an empty string's and an empty view's `format` reserve nothing and return Ok, while a non-empty one's
    // reservation returns the recorded failure (a pin: the BuiltinFormat routine gave the same before R8-S2d1). The view's `format` is
    // no member (SPEC 22.10.2), so a `T is Utf8Format` body calls it.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ADirectFormatOfEmptyOutputOnAFailedWriterReturnsOk()
    {
        const string Source = """
            group Helpers
                public func append<T>(value: ref/T, writer: uniq/Utf8Writer) -> Result<(), BufferFull>
                    T is Utf8Format
                    return value.format(writer)
            var bytes = [1 of 0@u8]
            var buffer = Text.fixed(bytes@uniq)
            var writer = Text.writer(buffer@uniq)
            match (writer@uniq).write("ab")@move
                .Ok(_) => $abort("fits")
                .Err(_) => ()
            match "".format(writer@uniq)@move
                .Ok(_) => ()
                .Err(_) => $abort("empty string")
            match Helpers.append(Text.utf8(""), writer@uniq)@move
                .Ok(_) => ()
                .Err(_) => $abort("empty view")
            match "x".format(writer@uniq)@move
                .Ok(_) => $abort("non-empty")
                .Err(_) => ()
            match writer.status()@move
                .Ok(_) => $abort("sticky status")
                .Err(_) => ()
            Console.writeLine("ok")
            """;
        NativeAllocationAudit.WriteFixture("Utf8FormatDirectEmptyOnFailed", Source, 0, 0, 0, "ok\n");
    }

    private static string Program(string operation)
        => """
            func run() -> Result<(), BufferFull>
                var bytes = [64 of 0@u8]
                var buffer = Text.fixed(bytes@uniq)
                var writer = Text.writer(buffer@uniq)
            """ + "\n    " + operation + "\n" + """
                match (buffer@move).intoText()@move
                    .Ok(let view) => Console.writeLine(view)
                    .Err(_) => $abort("utf8")
                return .Ok(())
            match run()@move
                .Ok(_) => ()
                .Err(_) => $abort("full")
            """;
}
