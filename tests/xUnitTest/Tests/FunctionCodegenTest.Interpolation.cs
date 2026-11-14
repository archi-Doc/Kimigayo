// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.RegularExpressions;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Xunit;

namespace XunitTest;

// The owning interpolation's rows of MIR code generation (R5 C9c, C9d; SPEC 22.7, 22.10.3, 22.10.5; DECISIONS R3-10, R5-M133):
// the private buffer Text.heap(0)'s (R8-W2 c3b), each position's hint a store of the writer's Field in the instance (R8-W2 c2b), a literal
// position's text a library call after its hint, deferred when a value follows (R8-W2 c1), the end Utf8Writer.finish and
// HeapBuffer.intoString (R8-W2 c3a), and a direct bounded line within the windows-x64 limit on its MIR region (R8-W2 a2: a fixed array,
// Text.fixed's).
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
        Assert.Equal(["20", "0"], Hints(ir, function));
        Assert.Single(CallsOf(function), x => FunctionsOf(ir, c, "text").Contains(x.Callee));
        var lines = FunctionsOf(ir, c, "writeLine");
        Assert.Same(LineOf(ir, c, view: true), Assert.Single(CallsOf(function), x => lines.Contains(x.Callee)).Callee);
        Assert.DoesNotContain(CalleesOf(function), x => FunctionsOf(ir, c, "heap").Contains(x) || FunctionsOf(ir, c, "intoString").Contains(x));
    }

    // SPEC 22.10.5 (DECISIONS R8-M24; R8-W2 c3b): a bounded interpolation that is not passed directly to writeLine, and an unbounded one,
    // format on the heap, each buffer the library's Text.heap of capacity 0 with its caller location (SPEC 22.5.1), which allocates
    // nothing: the adapter's first reservation grows it once by its bytes, a deferred literal and its hint. Both end with the heap form,
    // the library's Utf8Writer.finish, then HeapBuffer.intoString (R8-W2 c3a), each hint is a store of the writer's Field (R8-W2 c2b), and
    // no module holds the runtime's second statement of the buffers, of their end or of the hint (fail-before: FormatBuffer's
    // __kimi_text_heap of the instance's estimate, 33 for ipOwned, over __kimi_text_fixed; FormatFinish's __kimi_format_finish, which called
    // __kimi_format_check and __kimi_format_take_string, which called __kimi_buffer_bytes; FormatSegment's __kimi_format_hint call per
    // position, which stored it).
    [Theory]
    [InlineData("ipOwned", new[] { 20L, 0L })]
    [InlineData("ipPlain", new[] { 0L })]
    public void AnOwningInterpolationOffTheStackFormatsOnTheHeap(string name, long[] hints)
    {
        var c = Analyze(Interpolations, OutputKind.Library);
        var text = Generate(c);
        var ir = GeneratedIr.Parse(text);
        var function = Assert.Single(FunctionsOf(ir, c, name));
        var heap = Assert.Single(CallsOf(function), x => FunctionsOf(ir, c, "heap").Contains(x.Callee));
        Assert.Matches(@"^call void @\(ptr %, i64 0, ptr @, i64 \d+\)$", heap.Shape);
        Assert.Equal(hints.Select(static x => $"{x}"), Hints(ir, function));
        IrFunction[] end = [Assert.Single(FunctionsOf(ir, c, "finish")), Assert.Single(FunctionsOf(ir, c, "intoString"))];
        Assert.Equal(end, CalleesOf(function).Where(end.Contains));
        Assert.DoesNotContain(CalleesOf(function), x => FunctionsOf(ir, c, "fixed").Contains(x));
        Assert.DoesNotContain(function.Instructions, static x => x.Opcode == "alloca" && x.Shape.StartsWith(RegionShape, StringComparison.Ordinal));
        string[] routines =
        [
            "__kimi_text_heap", "__kimi_text_fixed", "__kimi_format_finish", "__kimi_format_take_string", "__kimi_format_check", "__kimi_buffer_bytes",
            "__kimi_format_hint",
        ];
        foreach (var routine in routines)
        {
            Assert.DoesNotContain(routine, text, StringComparison.Ordinal);
        }
    }

    // Rule L (STATUS; plan row C9c; R8-W2 a2): the limit holds the literal bytes and the values' maxima, 1,024 bytes on the region and
    // 1,025 on the heap, whose buffer starts empty (Text.heap(0), R8-W2 c3b); a generic body's term decides per instance (i32's 6 + 11,
    // bool's 6 + 5), and so does its first hint, a Length constant (11 and 5; R8-W2 c2b). Each buffer's end is its own form: a region's
    // text() and writeLine(Utf8Slice), a heap buffer's finish, intoString and writeLine(ref/string).
    [Fact]
    public void TheStackLimitAndEachInstanceChooseTheBuffer()
    {
        var source = "func atLimit(n: i8) => Console.writeLine(\"" + new string('x', 1020) + "\\(n)\")\n" +
            "func pastLimit(n: i8) => Console.writeLine(\"" + new string('x', 1021) + "\\(n)\")\n";
        var c = Analyze(source, OutputKind.Library);
        var ir = GeneratedIr.Parse(Generate(c));
        var at = Assert.Single(FunctionsOf(ir, c, "atLimit"));
        Assert.Equal(RegionShape + "1024 x i8] }, align 1", Region(ir, c, at).Region.Shape);
        Assert.Equal([FunctionsOf(ir, c, "fixed")[0], LineOf(ir, c, view: true)], Ends(ir, c, at));
        var past = Assert.Single(FunctionsOf(ir, c, "pastLimit"));
        var heap = Assert.Single(FunctionsOf(ir, c, "heap"));
        Assert.Matches(@"^call void @\(ptr %, i64 0, ptr @, i64 \d+\)$", Assert.Single(CallsOf(past), x => x.Callee == heap).Shape);
        Assert.Equal(
            [heap, Assert.Single(FunctionsOf(ir, c, "finish")), Assert.Single(FunctionsOf(ir, c, "intoString")), LineOf(ir, c, view: false)],
            Ends(ir, c, past));
        var generic = Analyze(Interpolations, OutputKind.Library);
        var instances = GeneratedIr.Parse(Generate(generic));
        Assert.Equal(
            [RegionShape + "11 x i8] }, align 1", RegionShape + "17 x i8] }, align 1"],
            FunctionsOf(instances, generic, "ipShow").Select(x => Region(instances, generic, x).Region.Shape).Order(StringComparer.Ordinal));
        Assert.Equal(
            ["11 0", "5 0"],
            FunctionsOf(instances, generic, "ipShow").Select(x => string.Join(' ', Hints(instances, x))).Order(StringComparer.Ordinal));
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

    // Rule L (R8-W2 a2; consents 3 and 4 of C-180; R8-W2 c3b): a generic direct line's owning instance (T = string) has a region of no
    // bytes, Text.fixed<0>'s, with no alloca for anything to destroy. Both buffers are made once, before the first branch (Text.fixed<0>,
    // then Text.heap(0); MirBuildTest's AGenericLinesBuffersAreMadeBeforeItsFirstBranch), so the owning buffer is initialized on every
    // path; its end, intoString, moves it on the owning path only, so its drop has one flag again (R8-W2 c3a; at c3b none).
    [Fact]
    public void AnOwningInstancesRegionHasNoBytes()
    {
        var c = Analyze("func gs<T>(v: ref/T)\n    T is Utf8Format\n    Console.writeLine(\"v=\\(v)\")\nfunc use() -> ()\n    gs(\"abc\")\n", OutputKind.Library);
        var ir = GeneratedIr.Parse(Generate(c));
        var function = Assert.Single(FunctionsOf(ir, c, "gs"));
        Assert.DoesNotContain(function.Instructions, static x => x.Shape.StartsWith(RegionShape, StringComparison.Ordinal));
        IrFunction[] buffers = [.. FunctionsOf(ir, c, "fixed"), .. FunctionsOf(ir, c, "heap")];
        Assert.Equal(buffers, CalleesOf(function).Where(buffers.Contains));
        Assert.Single(CallsOf(function), x => FunctionsOf(ir, c, "intoString").Contains(x.Callee));
        Assert.Single(FlagsOf(function));
    }

    // SPEC 22.10.3, 22.10.5 (DECISIONS R5-M133; R8-W2 c1, c2b): each position stores its hint into the writer's Field, then the position's
    // write follows: a literal's text is a library body's call, Utf8Writer.deferLiteral where a value follows (kept for the next
    // reservation), string's built-in format at the end, where no value follows; a value's is Utf8Writer.write's instance at the value's
    // Type (R8-W1b-4), the witness table's. No module holds the formatting runtime's literal routines (fail-before: the text was the
    // step's, __kimi_format_literal or the built-in write with the string's kind, then __kimi_format_check; the hint was the step's
    // __kimi_format_hint call).
    [Theory]
    [InlineData("ipPrefix", new[] { "hint 12", "deferLiteral", "hint 12", "write", "hint 11", "deferLiteral", "hint 0", "write" })]
    [InlineData("ipTail", new[] { "hint 1", "write", "hint 0", "format" })]
    public void ALiteralsTextIsALibraryCallAfterItsHint(string name, string[] steps)
    {
        var c = Analyze(Interpolations, OutputKind.Library);
        var text = Generate(c);
        var ir = GeneratedIr.Parse(text);
        var function = Assert.Single(FunctionsOf(ir, c, name));
        var routines = new Dictionary<IrFunction, string>();
        foreach (var body in new[] { "write", "deferLiteral", "format" })
        {
            foreach (var instance in FunctionsOf(ir, c, body))
            {
                routines[instance] = body;
            }
        }

        var writer = Writer(ir, function);
        Assert.Equal(
            steps,
            function.Instructions
                .Select(x => Hint(x, writer) is { } hint ? "hint " + hint : x.Callee is { } callee && routines.TryGetValue(callee, out var call) ? call : null)
                .OfType<string>());
        foreach (var routine in new[] { "__kimi_format_literal", "__kimi_writer_write_builtin", "__kimi_format_builtin", "__kimi_format_bytes" })
        {
            Assert.DoesNotContain(routine, text, StringComparison.Ordinal);
        }
    }

    // The shape of a direct line's region, a fixed array of bytes, up to its length.
    private const string RegionShape = "alloca { [0 x i8], [";

    // A function's interpolation buffer and end routines, in order: Text.fixed's or the heap's buffer, then the region's or the heap's end
    // (finish and intoString) and its writeLine.
    private static IrFunction[] Ends(GeneratedIr ir, Compilation c, IrFunction function)
    {
        IrFunction[] routines = [.. FunctionsOf(ir, c, "fixed"), .. FunctionsOf(ir, c, "heap"), .. FunctionsOf(ir, c, "finish"),
            .. FunctionsOf(ir, c, "intoString"), .. FunctionsOf(ir, c, "writeLine")];
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

    // A function's owning interpolation's hints, in order (R8-W2 c2b): each Hint of its writer's slot.
    private static string[] Hints(GeneratedIr ir, IrFunction function)
    {
        var writer = Writer(ir, function);
        return [.. function.Instructions.Select(x => Hint(x, writer)).OfType<string>()];
    }

    // A function's writer slot: Text.writer's result, the first operand of every __kimi_text_writer call in it (both arms' of a generic
    // direct line).
    private static IrValue Writer(GeneratedIr ir, IrFunction function)
        => Assert.Single(CallsOf(function).Where(x => x.Callee == ir.Function("__kimi_text_writer")).Select(static x => x.Operands[0]).Distinct());

    // The hint an instruction stores, an i64 constant at the writer's Field hint, the layout's offset 24 (RuntimeLayoutsAreFixed); null
    // for another instruction.
    private static string? Hint(IrValue instruction, IrValue writer)
        => instruction is { Opcode: "store", Operands: [{ Opcode: "getelementptr", Shape: "getelementptr i8, ptr %, i64 24" } field] } &&
            field.Operands[0] == writer && Regex.Match(instruction.Shape, @"^store i64 (\d+), ptr %, align 8$") is { Success: true } stored
            ? stored.Groups[1].Value : null;
}
