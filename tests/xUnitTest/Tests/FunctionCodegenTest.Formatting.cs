// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Text.RegularExpressions;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Xunit;

namespace XunitTest;

// The formatting rows of MIR code generation (R5 C9a, C9b; SPEC 22.7, 22.9, 22.10.1, 22.10.2, 22.10.5; DECISIONS R5-M128, R5-M130): a Text,
// buffer, window or writer call, and writeLine of a view, calls its row's routine in the formatting runtime with its operands by parameter
// kind, its result slot and location, a capacity Context the length of the fixed array its operand refers to and a kind Context its witness
// target's Self's, the runtime written once on request, at a direct call, an Item's Value call and an adapter's entry; a user formatter's
// write, toString or tryFormat runs through its wrappers; a BuiltinFormat item is the built-in witness; Text.writer gives its destination's
// writer kind and dispatch word; natively. The kinds are the formatting runtime's own numbering, the one table both
// generators read.
public sealed partial class FunctionCodegenTest
{
    // A Library calling every formatting row without a witness target: the fixed and heap buffers' operations, the windows', utf8,
    // validateUtf8, writeLine of a view, a writer's status, Text.fixed's Item called and erased.
    private const string Buffers =
        "func fxRun(storage: uniq/[8 of u8]) -> Result<isize, BufferFull>\n    var buffer = Text.fixed(storage)\n" +
        "    var window = try (buffer@uniq).reserve(2)\n    try (window@uniq).push(65)\n    let pair: [2 of u8] = [66, 67]\n" +
        "    try (window@uniq).append(pair[..])\n    let limited = (window@move).limit(4)\n    let written = (limited@move).commit()\n" +
        "    let count = buffer.bytes().length\n    _ = buffer.text()\n    _ = (buffer@uniq).validate()\n    (buffer@uniq).clear()\n" +
        "    _ = (buffer@move).intoText()\n    return .Ok(written + count)\n" +
        "func hpRun(n: isize) -> Result<isize, BufferFull>\n    var buffer = Text.heap(n)\n    var window = try (buffer@uniq).reserve(1)\n" +
        "    try (window@uniq).push(65)\n    _ = (window@move).commit()\n    let count = buffer.bytes().length\n    _ = buffer.text()\n" +
        "    _ = (buffer@uniq).validate()\n    (buffer@uniq).clear()\n    _ = (buffer@move).intoString()\n    return .Ok(count)\n" +
        "func vwLength(text: ref/string) -> isize => Text.utf8(text).length\n" +
        "func vwCheck(bytes: Slice<u8>) -> bool\n    match Text.validateUtf8(bytes)@move\n        .Ok(_) => return true\n        .Err(_) => return false\n" +
        "func vwShow(text: Text.Utf8Slice) => Console.writeLine(text)\n" +
        "func wrStatus(writer: ref/Utf8Writer) -> bool\n    match writer.status()@move\n        .Ok(_) => return true\n        .Err(_) => return false\n" +
        "func itFixed(storage: uniq/[4 of u8]) -> isize\n    let f = Text.fixed<4>\n    return f(storage).capacity\n" +
        "func itErasedFixed() -> (uniq/[4 of u8]) -> Text.FixedBuffer => Text.fixed<4>\n";

    // A Library whose formatting rows carry witness targets: built-in values written, converted and formatted into an array (their
    // BuiltinFormat items admitted and defined, R5-M130).
    private const string Formats =
        "func ctWriteI64(writer: uniq/Utf8Writer, n: i64) -> Result<(), BufferFull> => writer.write(n)\n" +
        "func ctWriteU8(writer: uniq/Utf8Writer, n: u8) -> Result<(), BufferFull> => writer.write(n)\n" +
        "func ctWriteText(writer: uniq/Utf8Writer, s: ref/string) -> Result<(), BufferFull> => writer.write(s)\n" +
        "func ctWriteView(writer: uniq/Utf8Writer, v: Text.Utf8Slice) -> Result<(), BufferFull> => writer.write(v)\n" +
        "func ctWriteBool(writer: uniq/Utf8Writer, b: bool) -> Result<(), BufferFull> => writer.write(b)\n" +
        "func ctWriteChar(writer: uniq/Utf8Writer, c: char) -> Result<(), BufferFull> => writer.write(c)\n" +
        "func ctWriteF64(writer: uniq/Utf8Writer, f: f64) -> Result<(), BufferFull> => writer.write(f)\n" +
        "func ctText(n: u16) -> string => Text.toString(n)\n" +
        "func ctTry(n: i32, bytes: uniq/[4 of u8]) -> bool\n    match Text.tryFormat(n, bytes)@move\n        .Ok(_) => return true\n" +
        "        .Err(_) => return false\n" +
        "func itText(n: ref/i32) -> string\n    let show = Text.toString<i32>\n    return show(n)\n";

    // A user formatter, whose formatting rows run through its wrappers: written, converted and formatted into an array, toString's Item
    // called and erased.
    private const string UserFormats =
        "struct Shown\n    Self is Utf8Format\n    public init() => ()\n" +
        "    public func format(self: ref/Self, writer: uniq/Utf8Writer) -> Result<(), BufferFull> => .Ok(())\n" +
        "func usWrite(writer: uniq/Utf8Writer, s: ref/Shown) -> Result<(), BufferFull> => writer.write(s)\n" +
        "func usText(s: ref/Shown) -> string => Text.toString(s)\n" +
        "func usTry(s: ref/Shown, bytes: uniq/[8 of u8]) -> bool\n    match Text.tryFormat(s, bytes)@move\n        .Ok(_) => return true\n" +
        "        .Err(_) => return false\n" +
        "func usItem(s: ref/Shown) -> string\n    let show = Text.toString<Shown>\n    return show(s)\n" +
        "func usErased() -> (ref/Shown) -> string => Text.toString<Shown>\n";

    // A Library calling the format requirement on built-in Types: a generic body's Requirement call at six Selves and a requirement Item's
    // Value call at i32.
    private const string FormatShims =
        "func apAppend<T>(value: ref/T, writer: uniq/Utf8Writer) -> Result<(), BufferFull>\n    T is Utf8Format\n    return value.format(writer)\n" +
        "func apItem<T>(value: ref/T, writer: uniq/Utf8Writer) -> Result<(), BufferFull>\n    T is Utf8Format\n    let show = T.format\n" +
        "    return show(value, writer)\n" +
        "func apI32(n: ref/i32, w: uniq/Utf8Writer) -> Result<(), BufferFull> => apAppend(n, w)\n" +
        "func apText(s: ref/string, w: uniq/Utf8Writer) -> Result<(), BufferFull> => apAppend(s, w)\n" +
        "func apUnit(u: ref/(), w: uniq/Utf8Writer) -> Result<(), BufferFull> => apAppend(u, w)\n" +
        "func apView(v: ref/Text.Utf8Slice, w: uniq/Utf8Writer) -> Result<(), BufferFull> => apAppend(v, w)\n" +
        "func apF64(f: ref/f64, w: uniq/Utf8Writer) -> Result<(), BufferFull> => apAppend(f, w)\n" +
        "func apWide(x: ref/u128, w: uniq/Utf8Writer) -> Result<(), BufferFull> => apAppend(x, w)\n" +
        "func apItemOf(n: ref/i32, w: uniq/Utf8Writer) -> Result<(), BufferFull> => apItem(n, w)\n";

    // A Library creating writers over a FixedBuffer, a HeapBuffer and a user Writer (Sink), Text.writer's Item called and erased.
    private const string Writers = Sink +
        "func wrFixed(buffer: uniq/Text.FixedBuffer)\n    _ = Text.writer(buffer)\n" +
        "func wrHeap(buffer: uniq/Text.HeapBuffer)\n    _ = Text.writer(buffer)\n" +
        "func wrUser(sink: uniq/Sink)\n    _ = Text.writer(sink)\n" +
        "func wrItem(sink: uniq/Sink)\n    let make = Text.writer<Sink>\n    _ = make(sink)\n" +
        "func wrErased() -> (uniq/Sink) -> Utf8Writer => Text.writer<Sink>\n" +
        "func wrErasedHeap() -> (uniq/Text.HeapBuffer) -> Utf8Writer => Text.writer<Text.HeapBuffer>\n";

    // SPEC 22.7, 22.9 (DECISIONS R5-M128): the buffers, windows, views, validation and a view's writeLine natively,
    // with Text.fixed's and Text.heap's Items called and a window's limit Aborting at the invoking expression.
    public static TheoryData<string, string, string, int, string> FormattingPrograms => new()
    {
        {
            "FormattingRows",
            "func fill() -> Result<isize, BufferFull>\n    var storage = [4 of 0@u8]\n    var buffer = Text.fixed(storage@uniq)\n" +
            "    var window = try (buffer@uniq).reserve(3)\n    try (window@uniq).push(72)\n    let pair: [2 of u8] = [105, 33]\n" +
            "    try (window@uniq).append(pair[..])\n    match (window@uniq).append(pair[..])@move\n        .Ok(_) => $abort(\"atomic\")\n" +
            "        .Err(_) => ()\n    let limited = (window@move).limit(4)\n    let written = (limited@move).commit()\n" +
            "    match buffer.text()@move\n        .Ok(let text) => Console.writeLine(text)\n        .Err(_) => $abort(\"text\")\n" +
            "    let count = buffer.bytes().length\n    match (buffer@uniq).validate()@move\n        .Ok(_) => ()\n" +
            "        .Err(_) => $abort(\"validate\")\n    match (buffer@move).intoText()@move\n        .Ok(let text) => Console.writeLine(text)\n" +
            "        .Err(_) => $abort(\"intoText\")\n    return .Ok(written + count)\n" +
            "func grow() -> Result<isize, BufferFull>\n    var buffer = Text.heap(1)\n    var window = try (buffer@uniq).reserve(5)\n" +
            "    let word: [5 of u8] = [104, 101, 97, 112, 33]\n    try (window@uniq).append(word[..])\n    _ = (window@move).commit()\n" +
            "    let length = buffer.length\n    match buffer.text()@move\n        .Ok(let text) => Console.writeLine(text)\n" +
            "        .Err(_) => $abort(\"text\")\n    (buffer@uniq).clear()\n    var again = try (buffer@uniq).reserve(2)\n" +
            "    try (again@uniq).push(111)\n    try (again@uniq).push(107)\n    _ = (again@move).commit()\n" +
            "    match (buffer@uniq).validate()@move\n        .Ok(_) => ()\n        .Err(_) => $abort(\"validate\")\n" +
            "    require buffer.bytes().length == 2 else => $abort(\"bytes\")\n    match (buffer@move).intoString()@move\n" +
            "        .Ok(let text) => Console.writeLine(text)\n        .Err(_) => $abort(\"intoString\")\n    return .Ok(length)\n" +
            "match fill()@move\n    .Ok(let n) => require n == 6 else => $abort(\"fill\")\n    .Err(_) => $abort(\"full\")\n" +
            "match grow()@move\n    .Ok(let n) => require n == 5 else => $abort(\"grow\")\n    .Err(_) => $abort(\"full\")\n" +
            "let bad: [2 of u8] = [0xC0@u8, 0x80]\nmatch Text.validateUtf8(bad[..])@move\n    .Ok(_) => $abort(\"invalid accepted\")\n" +
            "    .Err(_) => Console.writeLine(\"invalid\")\nlet good: [2 of u8] = [0xC2@u8, 0x80]\n" +
            "match Text.validateUtf8(good[..])@move\n    .Ok(let view) => require view.length == 2 else => $abort(\"view\")\n" +
            "    .Err(_) => $abort(\"valid rejected\")\nlet name = \"kimi\"\nConsole.writeLine(Text.utf8(name))\n" +
            "let make = Text.fixed<4>\nvar small = [4 of 0@u8]\nlet tiny = make(small@uniq)\n" +
            "require tiny.capacity == 4 and tiny.length == 0 else => $abort(\"item\")\nlet heap = Text.heap\nlet spare = heap(3)\n" +
            "require spare.capacity >= 3 else => $abort(\"heap item\")\nConsole.writeLine(\"done\")\n",
            "Hi!\nHi!\nheap!\nok\ninvalid\nkimi\ndone\n",
            0,
            string.Empty
        },
        {
            "FormattingLimit",
            "func cut() -> Result<(), BufferFull>\n    var storage = [4 of 0@u8]\n    var buffer = Text.fixed(storage@uniq)\n" +
            "    var window = try (buffer@uniq).reserve(2)\n    try (window@uniq).push(1)\n    try (window@uniq).push(2)\n" +
            "    _ = (window@move).limit(1)\n    return .Ok(())\n_ = cut()\n",
            string.Empty,
            1,
            "Hello.kimi:7:9: abort KIMI_E_ARG_RANGE: Argument out of range\n"
        },
    };

    [Theory]
    [MemberData(nameof(FormattingPrograms))]
    public void FormattingProgramsRun(string name, string source, string stdout, int exit, string stderr)
        => ScalarEmissionTest.EmitFixture(name, source, stdout, exit, stderr);

    // SPEC 22.9 (DECISIONS R5-M128): each row calls its formatting routine, the symbol P1d's row names, with its operands by parameter kind (a
    // reference's pointer, an owned operand's Place or its copy slot, its result slot) and the call's location where it takes one; rows that
    // share a symbol call it under their own row ABI (FixedBuffer.text and intoText).
    [Fact]
    public void FormattingRowsCallTheirRoutine()
    {
        var c = Analyze(Buffers, OutputKind.Library);
        var ir = GeneratedIr.Parse(Generate(c));
        Assert.Equal(
            Routines(
            [
                CompilerFunctionKind.TextFixed, CompilerFunctionKind.FixedBufferReserve, CompilerFunctionKind.WindowPush,
                CompilerFunctionKind.WindowAppend, CompilerFunctionKind.WindowLimit, CompilerFunctionKind.WindowCommit,
                CompilerFunctionKind.FixedBufferBytes, CompilerFunctionKind.FixedBufferText, CompilerFunctionKind.FixedBufferValidate,
                CompilerFunctionKind.FixedBufferClear, CompilerFunctionKind.FixedBufferIntoText,
            ]),
            FormattingCalls(ir, c, "fxRun"));
        Assert.Equal(
            Routines(
            [
                CompilerFunctionKind.TextHeap, CompilerFunctionKind.HeapBufferReserve, CompilerFunctionKind.WindowPush,
                CompilerFunctionKind.WindowCommit, CompilerFunctionKind.HeapBufferBytes, CompilerFunctionKind.HeapBufferText,
                CompilerFunctionKind.HeapBufferValidate, CompilerFunctionKind.HeapBufferClear, CompilerFunctionKind.HeapBufferIntoString,
            ]),
            FormattingCalls(ir, c, "hpRun"));
        foreach (var (name, kind, shape) in new[]
        {
            ("fxRun", CompilerFunctionKind.TextFixed, "call void @(ptr %, ptr %, i64 8)"),
            ("fxRun", CompilerFunctionKind.FixedBufferReserve, $"call void @(ptr %, ptr %, i64 2, ptr @, i64 {Located(Buffers, "(buffer@uniq).reserve(2)")})"),
            ("fxRun", CompilerFunctionKind.WindowPush, "call void @(ptr %, ptr %, i8 65)"),
            ("hpRun", CompilerFunctionKind.TextHeap, $"call void @(ptr %, i64 %, ptr @, i64 {Located(Buffers, "Text.heap(n)")})"),
            ("vwLength", CompilerFunctionKind.TextUtf8, "call void @(ptr %, ptr noundef nonnull align 8 dereferenceable(24) %)"),
            ("vwCheck", CompilerFunctionKind.TextValidateUtf8, "call void @(ptr %, ptr %)"),
            ("vwShow", CompilerFunctionKind.WriteLineUtf8, $"call void @(ptr %, ptr @, i64 {Located(Buffers, "Console.writeLine(text)")})"),
            ("wrStatus", CompilerFunctionKind.WriterStatus, "call void @(ptr %, ptr %)"),
            ("itFixed", CompilerFunctionKind.TextFixed, "call void @(ptr %, ptr %, i64 4)"),
        })
        {
            var call = Assert.Single(CallsOf(Assert.Single(FunctionsOf(ir, c, name))), x => x.Callee == ir.Function(Routines(kind)[0]));
            Assert.Equal(shape, call.Shape);
        }

        var validate = ir.Function(Routines(CompilerFunctionKind.TextValidateUtf8)[0]);
        var check = Assert.Single(CallsOf(Assert.Single(FunctionsOf(ir, c, "vwCheck"))), x => x.Callee == validate);
        Assert.Equal("alloca", check.Operands[1].Opcode); // The Copy Slice's own slot, which the routine owns from the call on (R5-M89).
    }

    // SPEC 22.9.2, 7.7.4 (DECISIONS R5-M128): an erased Text.fixed Item's adapter measures its capacity from its entry's own input Type, the
    // target Function Type's, and calls the routine with its own parameters.
    [Fact]
    public void AnErasedFixedItemMeasuresItsInput()
    {
        var c = Analyze(Buffers, OutputKind.Library);
        var text = Generate(c);
        var ir = GeneratedIr.Parse(text);
        var entry = ir.Function(TableOf(text, Assert.Single(ItemTexts(c, "itErasedFixed"))).Entry);
        var call = Assert.Single(CallsOf(entry));
        Assert.Same(ir.Function(Routines(CompilerFunctionKind.TextFixed)[0]), call.Callee);
        Assert.Equal("call void @(ptr %, ptr %, i64 4)", call.Shape);
        Assert.Equal([entry.Parameters[1], entry.Parameters[2]], call.Operands); // Its result slot and its input, after the environment word.
    }

    // SPEC 22.9.2, 22.10.5, 7.7.4 (DECISIONS R5-M128): an erased Builtin toString<i32> and tryFormat<i32, 4> call their built-in routines from
    // their adapters' entries, given the target Self's kind and, for tryFormat, the capacity measured from the entry's input at index 1; the
    // module is written (its BuiltinFormat item defined, R5-M130), and the entries are read directly.
    [Fact]
    public void ErasedBuiltinFormatItemsGiveTheirKindAndCapacity()
    {
        var c = Analyze(
            "func erText() -> (ref/i32) -> string => Text.toString<i32>\n" +
            "func erTry() -> (ref/i32, uniq/[4 of u8]) -> Result<Text.Utf8Slice, BufferFull> => Text.tryFormat<i32, 4>\n",
            OutputKind.Library);
        _ = Generate(c);
        var entries = AdapterTexts(c);
        Assert.Equal(2, entries.Length);
        Assert.Contains(entries, static x => x.Contains(
            $"call void @{Routines(CompilerFunctionKind.TextToString)[0]}(ptr %ret, ptr %a0, i32 5, ptr %location, i64 %location_length)\n",
            StringComparison.Ordinal));
        Assert.Contains(entries, static x => x.Contains(
            $"call void @{Routines(CompilerFunctionKind.TextTryFormat)[0]}(ptr %ret, ptr %a0, ptr %a1, i64 4, i32 5, ptr %location, i64 %location_length)\n",
            StringComparison.Ordinal));
    }

    // SPEC 22.10.1-22.10.2, 22.10.5 (DECISIONS R5-M128): a write, a conversion and a formatting into an array of a built-in value call their
    // row's built-in routine given the Self's kind (and the array's capacity), at a direct call and an Item's Value call; the module is
    // written (each BuiltinFormat item defined, R5-M130), and the definitions are read directly.
    [Fact]
    public void FormattingRowsGiveTheirTargetsKind()
    {
        var c = Analyze(Formats, OutputKind.Library);
        _ = Generate(c);
        foreach (var (name, kind, words) in new[]
        {
            ("ctWriteI64", CompilerFunctionKind.WriterWrite, "i32 7"),
            ("ctWriteU8", CompilerFunctionKind.WriterWrite, "i32 2"),
            ("ctWriteText", CompilerFunctionKind.WriterWrite, "i32 16"),
            ("ctWriteView", CompilerFunctionKind.WriterWrite, "i32 17"),
            ("ctWriteBool", CompilerFunctionKind.WriterWrite, "i32 14"),
            ("ctWriteChar", CompilerFunctionKind.WriterWrite, "i32 13"),
            ("ctWriteF64", CompilerFunctionKind.WriterWrite, "i32 12"),
            ("ctText", CompilerFunctionKind.TextToString, "i32 4"),
            ("ctTry", CompilerFunctionKind.TextTryFormat, "i64 4, i32 5"),
            ("itText", CompilerFunctionKind.TextToString, "i32 5"),
        })
        {
            var call = Assert.Single(Regex.Matches(Assert.Single(ItemTexts(c, name)), $@"call void @{Routines(kind)[0]}\((.*)\)"));
            Assert.Contains(words + ", ptr @", call.Groups[1].Value);
        }
    }

    // SPEC 22.10.1-22.10.2, 22.5.1 (DECISIONS R5-M126, R5-M130): a BuiltinFormat item's function is the built-in formatting witness: its own
    // parameters forwarded to the BuiltinFormat routine with its Self's kind, nothing else; a generic body's Requirement call passes its own
    // location, and a requirement Item's Value call calls the same function.
    [Fact]
    public void BuiltinFormatShimsForwardToTheBuiltinRoutine()
    {
        var c = Analyze(FormatShims, OutputKind.Library);
        var text = Generate(c);
        var ir = GeneratedIr.Parse(text);
        var routine = ir.Function(Routines(CompilerFunctionKind.BuiltinFormat)[0]);
        var shims = ShimsOf(ir, text);
        var kinds = shims.Select(x =>
        {
            var call = Assert.Single(CallsOf(x));
            Assert.Same(routine, call.Callee);
            Assert.Equal(x.Parameters, call.Operands); // The result slot, the value, the writer and the received location.
            Assert.Equal(["call", "ret"], x.Instructions.Select(static y => y.Opcode));
            var kind = Regex.Match(call.Shape, @"^call void @\(ptr %, ptr %, ptr %, i32 (\d+), ptr %, i64 %\)$").Groups[1].Value;
            return int.Parse(kind, CultureInfo.InvariantCulture);
        });
        Assert.Equal([5, 10, 12, 15, 16, 17], kinds.Order()); // i32, u128, f64, (), string, Utf8Slice.
        var location = $", ptr @, i64 {Located(FormatShims, "value.format(writer)")})"; // After the value's and the writer's pointers.
        Assert.All(
            FunctionsOf(ir, c, "apAppend"),
            x => Assert.EndsWith(location, Assert.Single(CallsOf(x), y => y.Callee is { } f && shims.Contains(f)).Shape, StringComparison.Ordinal));
        var i32 = Assert.Single(shims, x => CallsOf(x)[0].Shape.Contains("i32 5,", StringComparison.Ordinal));
        var callers = ir.Functions.Where(x => CalleesOf(x).Contains(i32)).ToArray();
        Assert.Equal(2, callers.Length); // apAppend<i32> and apItem<i32>, the requirement Item's Value call.
        Assert.Contains(Assert.Single(FunctionsOf(ir, c, "apItem")), callers);
    }

    // SPEC 22.10.1 (DECISIONS R5-M130): a module whose only formatting is one direct write of an i32 through a writer adapter reaches the i32
    // BuiltinFormat item once (K row 2's Builtin target) and defines its function once, with no record.
    [Fact]
    public void ADirectBuiltinWriteDefinesItsItemOnce()
    {
        var text = Generate(Analyze("func one(writer: uniq/Utf8Writer) -> Result<(), BufferFull> => writer.write(1)\n", OutputKind.Library));
        var shim = Assert.Single(ShimsOf(GeneratedIr.Parse(text), text));
        Assert.Equal("call void @(ptr %, ptr %, ptr %, i32 5, ptr %, i64 %)", Assert.Single(CallsOf(shim)).Shape);
    }

    // SPEC 22.10.1, 22.9.2, 7.7.4 (DECISIONS R5-M130): a user formatter's write, toString and tryFormat run through its wrappers, one per row,
    // whatever calls them: the write wrapper is SPEC 22.10.1's frame (the runtime's failure test before and after, its failure record)
    // around one call of the witness with the value and the adapter; a conversion is the old generator's text over that write wrapper; the
    // calls pass their operands, tryFormat's measured capacity and their location, and toString's Item called and erased runs its wrapper.
    [Fact]
    public void AUserFormatterRunsThroughItsWrappers()
    {
        var c = Analyze(UserFormats, OutputKind.Library);
        var text = Generate(c);
        var ir = GeneratedIr.Parse(text);
        var (write, show, attempt) = (Wrapper(ir, text, "__kimi_write"), Wrapper(ir, text, "__kimi_tostring"), Wrapper(ir, text, "__kimi_tryformat"));
        var witness = Assert.Single(FunctionsOf(ir, c, "format"));
        var (healthy, fail) = (ir.Function(LegacyRuntimeHelpers.FormatHealthy.Name), ir.Function(LegacyRuntimeHelpers.WriterFail.Name));
        Assert.Equal([healthy, witness, healthy, fail], CalleesOf(write));
        Assert.Equal([write.Parameters[0], write.Parameters[2], write.Parameters[1]], CallsOf(write)[1].Operands);
        Assert.Equal(
            [ir.Function(Routines(CompilerFunctionKind.TextHeap)[0]), ir.Function(Routines(CompilerFunctionKind.TextWriter)[0]), write,
                ir.Function("__kimi_format_take_string"), ir.Function("__kimi_abort")],
            CalleesOf(show));
        Assert.Equal(
            [ir.Function(Routines(CompilerFunctionKind.TextFixed)[0]), ir.Function(Routines(CompilerFunctionKind.TextWriter)[0]), write,
                ir.Function(Routines(CompilerFunctionKind.FixedBufferBytes)[0])],
            CalleesOf(attempt));
        foreach (var (name, wrapper, shape) in new[]
        {
            ("usWrite", write, "call void @(ptr %, ptr %, ptr %)"),
            ("usText", show, $"call void @(ptr %, ptr %, ptr @, i64 {Located(UserFormats, "Text.toString(s)")})"),
            ("usTry", attempt, $"call void @(ptr %, ptr %, ptr %, i64 8, ptr @, i64 {Located(UserFormats, "Text.tryFormat(s, bytes)")})"),
            ("usItem", show, $"call void @(ptr %, ptr %, ptr @, i64 {Located(UserFormats, "show(s)")})"),
        })
        {
            Assert.Equal(shape, Assert.Single(CallsOf(Assert.Single(FunctionsOf(ir, c, name))), x => x.Callee == wrapper).Shape);
        }

        Assert.Contains(show, CalleesOf(ir.Function(TableOf(text, Assert.Single(ItemTexts(c, "usErased"))).Entry)));
        Assert.Same(write, Assert.Single(ir.Functions, x => CalleesOf(x).Contains(witness)));
    }

    // SPEC 22.9.2, 22.9.4, 7.7.4 (K package 4.5 row 6; DECISIONS R5-M130): Text.writer calls its routine with its destination's writer kind and
    // dispatch word: a FixedBuffer's 0 and a HeapBuffer's 1 with none, a user Writer's 2 with its reserve witness's function, at its call, its
    // Item's Value call and its erased adapters; the witness is never called here (the runtime calls it through the word).
    [Fact]
    public void TextWriterGivesItsDestinationsKindAndDispatch()
    {
        var c = Analyze(Writers, OutputKind.Library);
        var text = Generate(c);
        var ir = GeneratedIr.Parse(text);
        var routine = Routines(CompilerFunctionKind.TextWriter)[0];
        foreach (var (name, words) in new[]
        {
            ("wrFixed", "i64 0, ptr null"), ("wrHeap", "i64 1, ptr null"), ("wrUser", "i64 2, ptr @"), ("wrItem", "i64 2, ptr @"),
        })
        {
            var call = Assert.Single(CallsOf(Assert.Single(FunctionsOf(ir, c, name))), x => x.Callee == ir.Function(routine));
            Assert.Equal($"call void @(ptr %, ptr %, {words})", call.Shape);
        }

        foreach (var (name, words) in new[] { ("wrErased", "i64 2, ptr @"), ("wrErasedHeap", "i64 1, ptr null") })
        {
            var entry = ir.Function(TableOf(text, Assert.Single(ItemTexts(c, name))).Entry);
            Assert.Equal($"call void @(ptr %, ptr %, {words})", Assert.Single(CallsOf(entry), x => x.Callee == ir.Function(routine)).Shape);
        }

        var reserve = Regex.Match(Assert.Single(ItemTexts(c, "reserve")), @"define internal \S+ @(__kimi_f\d+)\(").Groups[1].Value;
        Assert.Equal(3, Regex.Matches(text, $@"call void @{routine}\(ptr %\S+, ptr %\S+, i64 2, ptr @{reserve}\)").Count);
        Assert.DoesNotContain(ir.Functions, x => CalleesOf(x).Contains(ir.Function(reserve)));
    }

    // DECISIONS R5-M30, R5-M128: the formatting runtime is written once, only in a module that calls a formatting row, and is the text the
    // old generator writes.
    [Fact]
    public void TheFormattingRuntimeIsWrittenOnRequest()
    {
        using var runtime = new StringWriter();
        LegacyRuntimeHelpers.WriteFormattingRuntime(runtime);
        var text = Generate(Analyze(Buffers, OutputKind.Library));
        var at = text.IndexOf(runtime.ToString(), StringComparison.Ordinal);
        Assert.True(at >= 0);
        Assert.Equal(at, text.LastIndexOf(runtime.ToString(), StringComparison.Ordinal));
        Assert.DoesNotContain("@" + Routines(CompilerFunctionKind.TextFixed)[0] + "(", Generate(Analyze("func one() -> i32 => 1\n", OutputKind.Library)));
    }

    // SPEC 22.10.2: an integer's kind by its width and signedness (a wrapping integer's by its argument, an isize's as i64's), then f32, f64,
    // char, bool, Unit and string; -1 exactly for a Type without built-in formatting.
    [Fact]
    public void FormatKindsAreTheRuntimesNumbering()
    {
        string[] names = ["i8", "u8", "i16", "u16", "i32", "u32", "i64", "u64", "i128", "u128", "isize", "usize", "f32", "f64", "char"];
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 7, 8, 11, 12, 13], names.Select(static x => LegacyRuntimeHelpers.FormatKind(BoundType.Primitives[x])));
        Assert.Equal([14, 15, 16], new[] { BoundType.Boolean, BoundType.Unit, BoundType.String }.Select(LegacyRuntimeHelpers.FormatKind));
        Assert.Equal(2, LegacyRuntimeHelpers.FormatKind(BoundType.WrappingOf(BoundType.Primitives["u8"])));
        Assert.Equal(-1, LegacyRuntimeHelpers.FormatKind(BoundType.Range));
        Assert.All(
            BoundType.Primitives.Values.Concat(BoundType.WrappingScalars.Values),
            static x => Assert.Equal(FormattingTypes.IsBuiltin(x), LegacyRuntimeHelpers.FormatKind(x) >= 0));
    }

    // SPEC 22.10.5 (the old utf8-formatting §5.1; DECISIONS R5-M131): a bounded value's maximum encoded bytes, a wrapping integer's its
    // argument's and an isize's an i64's; each is the runtime's own bound at the Type's kind (__kimi_format_bounds), and -1 exactly where the
    // Type is unbounded (a string, a view, a Type without built-in formatting).
    [Fact]
    public void FormatBoundsAreTheRuntimesTable()
    {
        string[] names = ["i8", "u8", "i16", "u16", "i32", "u32", "i64", "u64", "i128", "u128", "isize", "usize", "f32", "f64", "char"];
        Assert.Equal(
            [4, 3, 6, 5, 11, 10, 20, 20, 40, 39, 20, 20, 17, 24, 4],
            names.Select(static x => LegacyRuntimeHelpers.FormatBound(BoundType.Primitives[x])));
        BoundType[] others = [BoundType.Boolean, BoundType.Unit, BoundType.String, BoundType.Range, BoundType.WrappingOf(BoundType.Primitives["u8"])];
        Assert.Equal([5, 2, -1, -1, 3], others.Select(LegacyRuntimeHelpers.FormatBound));
        using var runtime = new StringWriter();
        LegacyRuntimeHelpers.WriteFormattingRuntime(runtime);
        var table = Regex.Match(runtime.ToString(), @"@__kimi_format_bounds = private unnamed_addr constant \[\d+ x i8\] \[([^\]]*)\]").Groups[1].Value
            .Split(',').Select(static x => int.Parse(x.Trim()[3..], CultureInfo.InvariantCulture)).ToArray();
        Assert.Equal(18, table.Length);
        Assert.All(
            BoundType.Primitives.Values.Concat(BoundType.WrappingScalars.Values).Append(BoundType.Unit).Append(BoundType.String),
            x => Assert.Equal(LegacyRuntimeHelpers.FormatKind(x) is var kind and > 0 and < 16 ? table[kind] : -1, LegacyRuntimeHelpers.FormatBound(x)));
    }

    // The routine symbols of formatting rows (P1d's FormattingRow), in order.
    private static string[] Routines(params CompilerFunctionKind[] kinds) => [.. kinds.Select(static x => LegacyRuntimeHelpers.FormattingRow(x)!.Abi.Name)];

    // A function's calls of the formatting runtime's routines, their symbols in order.
    private static string[] FormattingCalls(GeneratedIr ir, Compilation c, string name)
    {
        var routines = Enumerable.Range((int)CompilerFunctionKind.TextFixed, CompilerFunctionKind.BuiltinFormat - CompilerFunctionKind.TextFixed + 1)
            .Select(static x => LegacyRuntimeHelpers.FormattingRow((CompilerFunctionKind)x)!.Abi.Name).Distinct().ToDictionary(ir.Function);
        return [.. CalleesOf(Assert.Single(FunctionsOf(ir, c, name))).Where(routines.ContainsKey).Select(x => routines[x])];
    }

    // The definitions of a program's ItemAdapter items, each written directly whatever records the module's other items make (ItemTexts).
    private static string[] AdapterTexts(Compilation c)
    {
        var (_, collector, module, _) = WrittenModule(c);
        var set = collector.Set;
        return [.. Enumerable.Range(0, set.Items.Length).Where(x => set.Items[x].Key.Kind == MonoKind.ItemAdapter).Select(x =>
        {
            using var writer = new StringWriter();
            new FunctionCodegen(module, c.Types).WriteAdapter(collector, x, writer);
            return writer.ToString();
        })];
    }

    // A module's one wrapper of a prefix (ModuleCodegen.Formatting: __kimi_write, __kimi_tostring, __kimi_tryformat), defined once.
    private static IrFunction Wrapper(GeneratedIr ir, string text, string prefix)
        => ir.Function(Assert.Single(Regex.Matches(text, "define internal void @(" + prefix + @"\d+)\(")).Groups[1].Value);

    // The byte length of a site's location text (SPEC 22.5.1).
    private static int Located(string source, string site) => LocationText(source, site).Length;
}
