// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class Utf8BufferTest
{
    [Theory]
    [InlineData("FixedEmpty", "var storage = [8 of 0@u8]\nlet buffer = Text.fixed(storage@uniq)\nrequire buffer.capacity == 8 and buffer.length == 0 else => $abort(\"state\")")]
    [InlineData("HeapEmpty", "let buffer = Text.heap(0)\nrequire buffer.capacity == 0 and buffer.length == 0 else => $abort(\"state\")")]
    [InlineData("HeapCapacity", "let buffer = Text.heap(32)\nrequire buffer.capacity >= 32 and buffer.length == 0 else => $abort(\"state\")")]
    public void Creation(string name, string source)
        => ScalarEmissionTest.EmitFixture("Utf8Buffer" + name, source + "\nConsole.writeLine(\"ok\")", "ok\n");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WindowCommit(bool heap)
    {
        var source = """
            func run() -> Result<(), BufferFull>
                var storage = [8 of 0@u8]
                var buffer = Text.fixed(storage@uniq)
                var window = try (buffer@uniq).reserve(2)
                try (window@uniq).push(65)
                try (window@uniq).push(66)
                require window.written == 2 and window.remaining == 6 else => $abort("window")
                require (window@move).commit() == 2 else => $abort("commit")
                require buffer.length == 2 else => $abort("length")
                let bytes = buffer.bytes()
                require bytes.length == 2 and bytes[0] == 65 and bytes[1] == 66 else => $abort("bytes")
                return .Ok(())
            match run()@move
                .Ok(_) => Console.writeLine("ok")
                .Err(_) => $abort("full")
            """;
        if (heap)
        {
            source = source.Replace("var buffer = Text.fixed(storage@uniq)", "var buffer = Text.heap(8)", StringComparison.Ordinal);
        }

        ScalarEmissionTest.EmitFixture("Utf8BufferCommit" + (heap ? "Heap" : "Fixed"), source, "ok\n");
    }

    [Theory]
    [InlineData("[65@u8, 0, 66]", true)]
    [InlineData("[0xC2@u8, 0x80]", true)]
    [InlineData("[0xE0@u8, 0xA0, 0x80]", true)]
    [InlineData("[0xF4@u8, 0x8F, 0xBF, 0xBF]", true)]
    [InlineData("[0xC0@u8, 0x80]", false)]
    [InlineData("[0xE0@u8, 0x80, 0x80]", false)]
    [InlineData("[0xED@u8, 0xA0, 0x80]", false)]
    [InlineData("[0xF4@u8, 0x90, 0x80, 0x80]", false)]
    [InlineData("[0xF0@u8, 0x90]", false)]
    [InlineData("[0x80@u8]", false)]
    public void StrictValidation(string bytes, bool valid)
    {
        var source = $"let data = {bytes}\nlet valid = match Text.validateUtf8(data[..])@move\n    .Ok(_) => true\n    .Err(_) => false\nrequire valid == {valid.ToString().ToLowerInvariant()} else => $abort(\"validation\")\nConsole.writeLine(\"ok\")";
        ScalarEmissionTest.EmitFixture("Utf8BufferValidation" + bytes.Replace("@u8", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal).Replace(",", "_", StringComparison.Ordinal).Trim('[', ']'), source, "ok\n");
    }

    [Fact]
    public void ViewsAndOwnershipTransfer()
    {
        const string Source = """
            func run() -> Result<(), BufferFull>
                var buffer = Text.heap(2)
                var window = try (buffer@uniq).reserve(2)
                try (window@uniq).push(65)
                try (window@uniq).push(66)
                _ = (window@move).commit()
                match buffer.text()@move
                    .Ok(let text) => Console.writeLine(text)
                    .Err(_) => $abort("utf8")
                match (buffer@move).intoString()@move
                    .Ok(let text) => Console.writeLine(text)
                    .Err(_) => $abort("utf8")
                return .Ok(())
            match run()@move
                .Ok(_) => ()
                .Err(_) => $abort("full")
            """;
        ScalarEmissionTest.EmitFixture("Utf8BufferViews", Source, "AB\nAB\n");
    }

    [Fact]
    public void AtomicAppendLimitDropAndClear()
    {
        const string Source = """
            func run() -> Result<(), BufferFull>
                var storage = [3 of 0@u8]
                var buffer = Text.fixed(storage@uniq)
                let bytes: [2 of u8] = [65, 66]
                var window = try (buffer@uniq).reserve(0)
                try (window@uniq).append(bytes[..])
                match (window@uniq).append(bytes[..])@move
                    .Ok(_) => $abort("overflow")
                    .Err(_) => ()
                require window.written == 2 and window.remaining == 1 else => $abort("atomic")
                var limited = (window@move).limit(2)
                match (limited@uniq).push(67)@move
                    .Ok(_) => $abort("limit")
                    .Err(_) => ()
                _ = limited@move
                require buffer.length == 0 else => $abort("drop committed")
                var next = try (buffer@uniq).reserve(2)
                try (next@uniq).append(bytes[..])
                _ = (next@move).commit()
                match buffer.text()@move
                    .Ok(let text) => Console.writeLine(text)
                    .Err(_) => $abort("utf8")
                (buffer@uniq).clear()
                require buffer.capacity == 3 and buffer.length == 0 else => $abort("clear")
                return .Ok(())
            match run()@move
                .Ok(_) => ()
                .Err(_) => $abort("full")
            """;
        ScalarEmissionTest.EmitFixture("Utf8BufferAtomic", Source, "AB\n");
    }

    [Fact]
    public void GrowthPreservesCommittedPrefix()
    {
        const string Source = """
            func run() -> Result<(), BufferFull>
                var buffer = Text.heap(0)
                var empty = try (buffer@uniq).reserve(0)
                require empty.remaining == 0 else => $abort("zero reserve")
                _ = empty@move
                require buffer.capacity == 0 else => $abort("zero growth")
                var first = try (buffer@uniq).reserve(2)
                try (first@uniq).push(65)
                try (first@uniq).push(66)
                _ = (first@move).commit()
                var second = try (buffer@uniq).reserve(1)
                try (second@uniq).push(67)
                _ = (second@move).commit()
                require buffer.length == 3 and buffer.capacity == 4 else => $abort("growth")
                match (buffer@uniq).validate()@move
                    .Ok(_) => ()
                    .Err(_) => $abort("utf8")
                match (buffer@move).intoString()@move
                    .Ok(let text) => Console.writeLine(text)
                    .Err(_) => $abort("utf8")
                return .Ok(())
            match run()@move
                .Ok(_) => ()
                .Err(_) => $abort("full")
            """;
        ScalarEmissionTest.EmitFixture("Utf8BufferGrowth", Source, "ABC\n");
    }

    [Theory]
    [InlineData("_ = storage[0]\n_ = buffer.length", false)]
    [InlineData("_ = buffer.length\n_ = storage[0]", true)]
    public void FixedBufferHoldsItsExclusiveLoanUntilLastUse(string use, bool valid)
    {
        var c = MinimalEmissionTest.Analyze("var storage = [3 of 0@u8]\nlet buffer = Text.fixed(storage@uniq)\n" + use);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(valid, c.Ownership.Result.IsVerified);
        if (!valid)
        {
            Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        }
    }

    [Theory]
    [InlineData("Fixed", "var storage = [8 of 0@u8]\nlet buffer = Text.fixed(storage@uniq)", 0, 0)]
    [InlineData("Empty", "let buffer = Text.heap(0)", 0, 0)]
    [InlineData("Heap", "let buffer = Text.heap(16)", 1, 16)]
    [InlineData("EmptyTransfer", "let buffer = Text.heap(0)\n_ = (buffer@move).intoString()", 0, 0)]
    [InlineData("AllocatedEmptyTransfer", "let buffer = Text.heap(16)\n_ = (buffer@move).intoString()", 1, 16)]
    public void AllocationAndReleaseCosts(string name, string source, int allocations, int bytes)
        => NativeAllocationAudit.WriteFixture("Utf8BufferCost" + name, source, allocations, allocations, bytes);

    [Theory]
    [InlineData("Heap", "Text.heap(-1)", "Hello.kimi:1:1")]
    [InlineData("Reserve", "var buffer = Text.heap(0)\n(buffer@uniq).reserve(-1)", "Hello.kimi:2:1")]
    public void NegativeSizesAbort(string name, string source, string location)
        => ScalarEmissionTest.EmitFixture("Utf8BufferNegative" + name, source, string.Empty, 1, location + ": abort KIMI_E_ARG_RANGE: Argument out of range\n");
}
