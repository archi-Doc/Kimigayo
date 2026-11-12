// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.RegularExpressions;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Xunit;

namespace XunitTest;

// The owning interpolation's rows of MIR code generation (R5 C9c, C9d; SPEC 22.7, 22.10.3, 22.10.5; DECISIONS R3-10, R5-M133):
// FormatBuffer, FormatSegment and FormatFinish over each instance's estimate, a segment step's literal text after its hint, deferred when
// a value follows, and a direct bounded line within the windows-x64 limit on its MIR region (R8-W2 a2: a fixed array, Text.fixed's).
public sealed partial class FunctionCodegenTest
{
    // A Library of owning interpolations: direct and bounded (the stack), not direct, unbounded without literal text, with an early exit
    // from an embedded expression, a generic one at two bounded Types, and unbounded ones with literal text before values and at the end.
    private const string Interpolations =
        "func ipNumber(n: i64) => Console.writeLine(\"My number is \\(n)\")\n" +
        "func ipOwned(n: i64) -> string => \"My number is \\(n)\"\n" +
        "func ipPlain(s: ref/string) -> string => \"\\(s)\"\n" +
        "func ipRead(ok: bool) -> Result<i32, ()>\n    if ok => return .Ok(5)\n    return .Err(())\n" +
        "func ipTry(ok: bool) -> Result<(), ()>\n    Console.writeLine(\"v=\\(try ipRead(ok))!\")\n    return .Ok(())\n" +
        "func ipShow<T>(value: ref/T)\n    T is Utf8Format\n    Console.writeLine(\"value=\\(value)\")\n" +
        "func ipUse() -> ()\n    ipShow(42)\n    ipShow(true)\n" +
        "func ipPrefix(s: ref/string) -> string => \"prefix\\(s):\\(42)\"\n" +
        "func ipTail(s: ref/string) -> string => \"\\(s)!\"\n";

    // SPEC 22.10.5, 22.7, rule L (R8-W2 a2): a direct bounded line formats on its region of exactly the literal bytes and each value's
    // maximum (13 + 20), a fixed array local Text.fixed's instance takes (Region), its adapter a FixedBuffer's (kind 0), each write after
    // the hint of the later ones, and its end text() viewing the bytes for writeLine(Utf8Slice): no heap buffer, no string (fail-before:
    // the region was FormatBuffer's frame home and the end the stack form, which marked a string static).
    [Fact]
    public void ADirectBoundedLineFormatsOnItsRegion()
    {
        var c = Analyze(Interpolations, OutputKind.Library);
        var ir = GeneratedIr.Parse(Generate(c));
        var function = Assert.Single(FunctionsOf(ir, c, "ipNumber"));
        Assert.Equal(RegionShape + "33 x i8] }, align 1", Region(ir, c, function).Region.Shape);
        Assert.Equal("call void @(ptr %, ptr %, i64 0, ptr null)", Assert.Single(CallsOf(function), x => x.Callee == ir.Function("__kimi_text_writer")).Shape);
        Assert.Equal(
            ["call void @(ptr %, i64 20)", "call void @(ptr %, i64 0)"],
            CallsOf(function).Where(x => x.Callee == ir.Function("__kimi_format_hint")).Select(static x => x.Shape));
        Assert.Single(CallsOf(function), x => FunctionsOf(ir, c, "text").Contains(x.Callee));
        Assert.Single(CallsOf(function), x => FunctionsOf(ir, c, "writeLine").Contains(x.Callee));
        Assert.DoesNotContain(
            CalleesOf(function),
            x => x == ir.Function("__kimi_text_heap") || x == ir.Function("__kimi_format_finish") || x == ir.Function("__kimi_text_fixed") ||
                x == ir.Function("__kimi_write_line"));
    }

    // SPEC 22.10.5 (DECISIONS R5-M131): a bounded interpolation that is not passed directly to writeLine, and an unbounded one, format on the
    // heap, the first allocating its estimate once, the second starting unallocated; both end with the heap form.
    [Theory]
    [InlineData("ipOwned", 33, new[] { 20L, 0L })]
    [InlineData("ipPlain", 0, new[] { 0L })]
    public void AnOwningInterpolationOffTheStackFormatsOnTheHeap(string name, long capacity, long[] hints)
    {
        var c = Analyze(Interpolations, OutputKind.Library);
        var ir = GeneratedIr.Parse(Generate(c));
        var function = Assert.Single(FunctionsOf(ir, c, name));
        var heap = Assert.Single(CallsOf(function), x => x.Callee == ir.Function("__kimi_text_heap"));
        Assert.Matches($@"^call void @\(ptr %, i64 {capacity}, ptr @, i64 \d+\)$", heap.Shape);
        Assert.Equal(
            hints.Select(static x => $"call void @(ptr %, i64 {x})"),
            CallsOf(function).Where(x => x.Callee == ir.Function("__kimi_format_hint")).Select(static x => x.Shape));
        Assert.Single(CallsOf(function), x => x.Callee == ir.Function("__kimi_format_finish"));
        Assert.DoesNotContain(CalleesOf(function), x => x == ir.Function("__kimi_text_fixed") || FunctionsOf(ir, c, "fixed").Contains(x));
        Assert.DoesNotContain(function.Instructions, static x => x.Opcode == "alloca" && x.Shape.StartsWith(RegionShape, StringComparison.Ordinal));
    }

    // Rule L (STATUS; plan row C9c; R8-W2 a2): the limit holds the literal bytes and the values' maxima, 1,024 bytes on the region and
    // 1,025 on the heap; a generic body's term decides per instance (i32's 6 + 11, bool's 6 + 5). Each buffer's end is its own form: a
    // region's text() and writeLine(Utf8Slice), a heap buffer's FormatFinish and writeLine(ref/string).
    [Fact]
    public void TheStackLimitAndEachInstanceChooseTheBuffer()
    {
        var source = "func atLimit(n: i8) => Console.writeLine(\"" + new string('x', 1020) + "\\(n)\")\n" +
            "func pastLimit(n: i8) => Console.writeLine(\"" + new string('x', 1021) + "\\(n)\")\n";
        var c = Analyze(source, OutputKind.Library);
        var ir = GeneratedIr.Parse(Generate(c));
        var at = Assert.Single(FunctionsOf(ir, c, "atLimit"));
        Assert.Equal(RegionShape + "1024 x i8] }, align 1", Region(ir, c, at).Region.Shape);
        Assert.Equal([FunctionsOf(ir, c, "fixed")[0], Assert.Single(FunctionsOf(ir, c, "writeLine"))], Ends(ir, c, at));
        var past = Assert.Single(FunctionsOf(ir, c, "pastLimit"));
        Assert.Matches(@"^call void @\(ptr %, i64 1025, ptr @, i64 \d+\)$", Assert.Single(CallsOf(past), x => x.Callee == ir.Function("__kimi_text_heap")).Shape);
        Assert.Equal([ir.Function("__kimi_text_heap"), ir.Function("__kimi_format_finish"), ir.Function("__kimi_write_line")], Ends(ir, c, past));
        var generic = Analyze(Interpolations, OutputKind.Library);
        var instances = GeneratedIr.Parse(Generate(generic));
        Assert.Equal(
            [RegionShape + "11 x i8] }, align 1", RegionShape + "17 x i8] }, align 1"],
            FunctionsOf(instances, generic, "ipShow").Select(x => Region(instances, generic, x).Region.Shape).Order(StringComparer.Ordinal));
    }

    // SPEC 22.10.4-22.10.5 and rule L (R8-W2 a2): a region and its FixedBuffer hold no storage of their own (FixedBuffer has no drop), so
    // no path releases either, including the early exit of an embedded `try`: the region's address reaches only Text.fixed's borrow, and
    // the FixedBuffer only Text.fixed's result slot (fail-before: the HeapBuffer's glue freed the frame's bytes).
    [Fact]
    public void ARegionsBufferIsDestroyedOnNoPath()
    {
        var c = Analyze(Interpolations, OutputKind.Library);
        var ir = GeneratedIr.Parse(Generate(c));
        var function = Assert.Single(FunctionsOf(ir, c, "ipTry"));
        var (region, fixedBuffer) = Region(ir, c, function);
        Assert.Equal(RegionShape + "14 x i8] }, align 1", region.Shape);
        Assert.DoesNotContain(CallsOf(function), x => x.Operands.Contains(region));
        Assert.Equal([fixedBuffer], CallsOf(function).Where(x => x.Operands.Contains(fixedBuffer.Operands[0])));
    }

    // Rule L (R8-W2 a2; consents 3 and 4 of C-180): a generic direct line's owning instance (T = string) has a region of no bytes,
    // Text.fixed<0>'s, with no alloca for anything to destroy, its dead fixed arm's calls only (O0 builds both arms), and the owning
    // buffer made on one path has one drop flag.
    [Fact]
    public void AnOwningInstancesRegionHasNoBytes()
    {
        var c = Analyze("func gs<T>(v: ref/T)\n    T is Utf8Format\n    Console.writeLine(\"v=\\(v)\")\nfunc use() -> ()\n    gs(\"abc\")\n", OutputKind.Library);
        var ir = GeneratedIr.Parse(Generate(c));
        var function = Assert.Single(FunctionsOf(ir, c, "gs"));
        Assert.DoesNotContain(function.Instructions, static x => x.Shape.StartsWith(RegionShape, StringComparison.Ordinal));
        Assert.Single(CallsOf(function), x => FunctionsOf(ir, c, "fixed").Contains(x.Callee));
        Assert.Single(CallsOf(function), x => x.Callee == ir.Function("__kimi_format_finish"));
        Assert.Single(FlagsOf(function));
    }

    // SPEC 22.10.3, 22.10.5 (DECISIONS R5-M133): each FormatSegment step gives its hint, then writes its literal text with the string's kind
    // and checks the adapter: deferred to the next reservation (__kimi_format_literal) when a value follows, written (the built-in write) at
    // the end, where no value follows; a value's write follows its step: Utf8Writer.write's instance at the value's Type (R8-W1b-4), named
    // by the kind its BuiltinFormat shim gives, or "body" where the witness table's body formats it (a string's, R8-S2d1) (fail-before:
    // C9c's interim record at FormatBuffer and no text step).
    [Theory]
    [InlineData("ipPrefix", new[] { "hint 12", "literal 16", "check", "hint 12", "write body", "hint 11", "literal 16", "check", "hint 0", "write 5" })]
    [InlineData("ipTail", new[] { "hint 1", "write body", "hint 0", "write 16", "check" })]
    public void ASegmentWritesItsLiteralTextAfterItsHint(string name, string[] steps)
    {
        var c = Analyze(Interpolations, OutputKind.Library);
        var text = Generate(c);
        var ir = GeneratedIr.Parse(text);
        var function = Assert.Single(FunctionsOf(ir, c, name));
        var routines = new Dictionary<IrFunction, string>
        {
            [ir.Function("__kimi_format_hint")] = "hint",
            [ir.Function("__kimi_format_literal")] = "literal",
            [ir.Function("__kimi_writer_write_builtin")] = "write",
            [ir.Function("__kimi_format_check")] = "check",
        };
        var shims = ShimsOf(ir, text);
        foreach (var write in FunctionsOf(ir, c, "write"))
        {
            // A string's (R8-S2d1) and ipShow(true)'s (R8-S2a) instances call the witness table's body, no shim.
            routines[write] = CallsOf(write).SingleOrDefault(x => shims.Contains(x.Callee!))?.Callee is { } shim
                ? "write " + Regex.Match(Assert.Single(CallsOf(shim)).Shape, @", i32 (\d+), ").Groups[1].Value
                : "write body";
        }

        Assert.Equal(
            steps,
            CallsOf(function).Where(x => x.Callee is { } callee && routines.ContainsKey(callee))
                .Select(x => routines[x.Callee!] + Regex.Match(x.Shape, @"^call void @\(ptr %, (?:i64 (\d+)\)|ptr %, ptr %, i32 (\d+),)") switch
                {
                    { Groups: [_, { Success: true } hint, _] } => " " + hint.Value,
                    { Groups: [_, _, { Success: true } kind] } => " " + kind.Value,
                    _ => string.Empty,
                }));
    }

    // The shape of a direct line's region, a fixed array of bytes, up to its length.
    private const string RegionShape = "alloca { [0 x i8], [";

    // A function's interpolation buffer and end routines, in order: Text.fixed's or the heap's buffer, then the region's or the heap's end
    // and its writeLine.
    private static IrFunction[] Ends(GeneratedIr ir, Compilation c, IrFunction function)
    {
        IrFunction[] routines = [.. FunctionsOf(ir, c, "fixed"), ir.Function("__kimi_text_heap"), ir.Function("__kimi_format_finish"),
            .. FunctionsOf(ir, c, "writeLine"), ir.Function("__kimi_write_line")];
        return [.. CalleesOf(function).Where(routines.Contains)];
    }

    // A direct line's region in a function, its one byte-array alloca, and the Text.fixed call whose argument borrows it (its address stored
    // into the borrow the call loads).
    private static (IrValue Region, IrValue Call) Region(GeneratedIr ir, Compilation c, IrFunction function)
    {
        var region = Assert.Single(function.Blocks[0].Instructions, static x => x.Shape.StartsWith(RegionShape, StringComparison.Ordinal));
        var borrow = Assert.Single(function.Instructions, x => x.Opcode == "store" && x.Operands[0] == region).Operands[1];
        var call = Assert.Single(CallsOf(function), x => x.Operands.Any(y => y.Opcode == "load" && y.Operands[0] == borrow));
        Assert.Contains(call.Callee, FunctionsOf(ir, c, "fixed"));
        return (region, call);
    }
}
