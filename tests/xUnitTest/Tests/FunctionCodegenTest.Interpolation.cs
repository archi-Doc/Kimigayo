// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Xunit;

namespace XunitTest;

// The owning interpolation's rows of MIR code generation (R5 C9c; SPEC 22.7, 22.10.3, 22.10.5; DECISIONS R3-10, R5-M131, R5-M132):
// FormatBuffer, FormatHint and FormatFinish over each instance's estimate, a direct bounded buffer within the windows-x64 limit on the
// frame's region, and an unbounded one with literal text the literal path's (C9d) one located record.
public sealed partial class FunctionCodegenTest
{
    // A Library of owning interpolations: direct and bounded (the stack), not direct, unbounded without literal text, with an early exit
    // from an embedded expression, and a generic one at two bounded Types.
    private const string Interpolations =
        "func ipNumber(n: i64) => Console.writeLine(\"My number is \\(n)\")\n" +
        "func ipOwned(n: i64) -> string => \"My number is \\(n)\"\n" +
        "func ipPlain(s: ref/string) -> string => \"\\(s)\"\n" +
        "func ipRead(ok: bool) -> Result<i32, ()>\n    if ok => return .Ok(5)\n    return .Err(())\n" +
        "func ipTry(ok: bool) -> Result<(), ()>\n    Console.writeLine(\"v=\\(try ipRead(ok))!\")\n    return .Ok(())\n" +
        "func ipShow<T>(value: ref/T)\n    T is Utf8Format\n    Console.writeLine(\"value=\\(value)\")\n" +
        "func ipUse() -> ()\n    ipShow(42)\n    ipShow(true)\n";

    // SPEC 22.10.5, 22.7 (DECISIONS R5-M131): a direct bounded interpolation formats on the frame's region of exactly its estimate, the
    // literal bytes and each value's maximum (13 + 20), its adapter a HeapBuffer's (kind 1: the region never grows), each write after the
    // hint of the later ones, and its end the stack form, which marks the string static.
    [Fact]
    public void ADirectBoundedInterpolationFormatsOnItsFramesRegion()
    {
        var c = Analyze(Interpolations, OutputKind.Library);
        var ir = GeneratedIr.Parse(Generate(c));
        var function = Assert.Single(FunctionsOf(ir, c, "ipNumber"));
        var region = Assert.Single(function.Blocks[0].Instructions, static x => x.Shape == "alloca [33 x i8], align 1");
        var fixedBuffer = Assert.Single(CallsOf(function), x => x.Callee == ir.Function("__kimi_text_fixed"));
        Assert.Equal("call void @(ptr %, ptr %, i64 33)", fixedBuffer.Shape);
        Assert.Same(region, fixedBuffer.Operands[1]);
        Assert.Equal("call void @(ptr %, ptr %, i64 1, ptr null)", Assert.Single(CallsOf(function), x => x.Callee == ir.Function("__kimi_text_writer")).Shape);
        Assert.Equal(
            ["call void @(ptr %, i64 20)", "call void @(ptr %, i64 0)"],
            CallsOf(function).Where(x => x.Callee == ir.Function("__kimi_format_hint")).Select(static x => x.Shape));
        Assert.Single(CallsOf(function), x => x.Callee == ir.Function("__kimi_format_stack_finish"));
        Assert.DoesNotContain(CalleesOf(function), x => x == ir.Function("__kimi_text_heap") || x == ir.Function("__kimi_format_finish"));
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
        Assert.DoesNotContain(CalleesOf(function), x => x == ir.Function("__kimi_text_fixed") || x == ir.Function("__kimi_format_stack_finish"));
        Assert.DoesNotContain(function.Instructions, static x => x.Opcode == "alloca" && x.Shape.StartsWith("alloca [", StringComparison.Ordinal));
    }

    // The windows-x64 stack rule (DECISIONS R5-M131, plan row C9c, STATUS): the limit holds the literal bytes and the values' maxima, 1,024
    // bytes on the region and 1,025 on the heap; a generic body decides per instance (i32's 6 + 11, bool's 6 + 5). FormatBuffer and
    // FormatFinish apply the one rule to the same operands, so each buffer's end is its own form: a region's the stack form, a heap
    // buffer's the heap form.
    [Fact]
    public void TheStackLimitAndEachInstanceChooseTheBuffer()
    {
        var source = "func atLimit(n: i8) => Console.writeLine(\"" + new string('x', 1020) + "\\(n)\")\n" +
            "func pastLimit(n: i8) => Console.writeLine(\"" + new string('x', 1021) + "\\(n)\")\n";
        var c = Analyze(source, OutputKind.Library);
        var ir = GeneratedIr.Parse(Generate(c));
        var at = Assert.Single(FunctionsOf(ir, c, "atLimit"));
        Assert.Single(at.Blocks[0].Instructions, static x => x.Shape == "alloca [1024 x i8], align 1");
        Assert.Equal([ir.Function("__kimi_text_fixed"), ir.Function("__kimi_format_stack_finish")], Ends(ir, at));
        var past = Assert.Single(FunctionsOf(ir, c, "pastLimit"));
        Assert.Matches(@"^call void @\(ptr %, i64 1025, ptr @, i64 \d+\)$", Assert.Single(CallsOf(past), x => x.Callee == ir.Function("__kimi_text_heap")).Shape);
        Assert.Equal([ir.Function("__kimi_text_heap"), ir.Function("__kimi_format_finish")], Ends(ir, past));
        var generic = Analyze(Interpolations, OutputKind.Library);
        var instances = GeneratedIr.Parse(Generate(generic));
        Assert.Equal(
            ["alloca [11 x i8], align 1", "alloca [17 x i8], align 1"],
            FunctionsOf(instances, generic, "ipShow").SelectMany(static x => x.Blocks[0].Instructions).Select(static x => x.Shape)
                .Where(static x => x.StartsWith("alloca [", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
    }

    // SPEC 22.10.4-22.10.5 and the windows-x64 stack rule (DECISIONS R5-M131): a region holds no storage of its value's own, so no path
    // destroys the buffer on it, including the early exit of an embedded `try` (fail-before: the HeapBuffer's glue freed the frame's bytes).
    [Fact]
    public void ARegionsBufferIsDestroyedOnNoPath()
    {
        var c = Analyze(Interpolations, OutputKind.Library);
        var ir = GeneratedIr.Parse(Generate(c));
        var function = Assert.Single(FunctionsOf(ir, c, "ipTry"));
        Assert.Single(function.Blocks[0].Instructions, static x => x.Shape == "alloca [14 x i8], align 1");
        var fixedBuffer = Assert.Single(CallsOf(function), x => x.Callee == ir.Function("__kimi_text_fixed"));
        Assert.Equal([fixedBuffer], CallsOf(function).Where(x => x.Operands.Contains(fixedBuffer.Operands[0])));
    }

    // A function's interpolation buffer and end routines, in order: the region's or the heap's buffer, then the stack or heap end.
    private static IrFunction[] Ends(GeneratedIr ir, IrFunction function)
    {
        IrFunction[] routines = [ir.Function("__kimi_text_fixed"), ir.Function("__kimi_text_heap"), ir.Function("__kimi_format_stack_finish"),
            ir.Function("__kimi_format_finish")];
        return [.. CalleesOf(function).Where(routines.Contains)];
    }
}
