// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Text.RegularExpressions;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Xunit;

namespace XunitTest;

// The formatting of MIR code generation (R5 C9a, C9b; SPEC 22.7, 22.9, 22.10.1, 22.10.2, 22.10.5; DECISIONS R5-M128, R5-M130; R8-W1): the
// Text operations, the buffers, the windows, the adapter's write, the conversions and writeLine of a view are bodies, called with their
// operands, result slot and, for a CallerLocation row, the caller's location, at a direct call, an Item's Value call and an adapter's
// entry; the formatting runtime is written once on request; a user formatter runs through the bodies; a BuiltinFormat item is the built-in
// witness; Text.writer gives its destination's writer kind and dispatch word; natively. The kinds are the formatting runtime's own numbering.
public sealed partial class FunctionCodegenTest
{
    // A Library without a formatting row: the fixed and heap buffers' and the windows' operations, the Text bodies (fixed, heap, utf8,
    // validateUtf8), writeLine of a view (a body), a writer's status, Text.fixed's Item called and erased.
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

    // A Library formatting built-in values: written, converted and formatted into an array (their BuiltinFormat items admitted and
    // defined, R5-M130), toString's Item called.
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

    // A user formatter: written, converted and formatted into an array, toString's Item called and erased.
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

    // A user BufferWriter witness (Text.writer's row reserves through it).
    private const string Sink = "struct Sink\n    Self is BufferWriter\n    public var count: i32 = 0\n" +
        "    public func reserve(self: uniq/Self, minimum: isize) -> Result<WriteWindow, BufferFull>\n        self.count += 1\n" +
        "        return .Err(BufferFull.init())\n";

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

    // SPEC 22.7, 22.9 (DECISIONS R5-M89): the program calls no formatting row: writeLine of a view and the buffer, window and Text
    // operations are bodies (R8-W1a, W1b, W1c), which take an in-memory Copy operand through its own copy slot.
    [Fact]
    public void FormattingRowsCallTheirRoutine()
    {
        var c = Analyze(Buffers, OutputKind.Library);
        var text = Generate(c);
        var ir = GeneratedIr.Parse(text);

        // R8-W1c-3: every buffer, window and view operation is a body, so the module requests no formatting runtime and calls none of
        // its routines.
        using var runtime = new StringWriter();
        LegacyRuntimeHelpers.WriteFormattingRuntime(runtime);
        Assert.DoesNotContain(runtime.ToString(), text, StringComparison.Ordinal);

        // SPEC 22.7, 22.5.1 (R8-W1c-3): writeLine(Utf8Slice) is a body with a CallerLocation row: the user's call passes the view's own
        // copy slot and its location, and the body writes the bytes and the LF through WriteStdout's routine, each at the location it
        // received, so KIMI_E_STDOUT reports the user's call.
        var lines = FunctionsOf(ir, c, "writeLine").ToArray();
        var show = Assert.Single(CallsOf(Assert.Single(FunctionsOf(ir, c, "vwShow"))), x => lines.Contains(x.Callee!));
        Assert.Equal($"call void @(ptr %, ptr @, i64 {Located(Buffers, "Console.writeLine(text)")})", show.Shape);
        Assert.Equal("alloca", show.Operands[0].Opcode); // The view's own copy slot, which the body owns from the call on (R5-M89).
        var writes = CallsOf(show.Callee!).Where(x => x.Callee == ir.Function(RuntimeAbi.Stdout.Name)).ToArray();
        Assert.Equal(2, writes.Length);
        Assert.All(writes, x => Assert.Equal(show.Callee!.Parameters.TakeLast(2), x.Operands.TakeLast(2)));

        // SPEC 22.5.1, 22.9.2 (R8-W1c): the Text operations are bodies; heap has a CallerLocation row, so the user's call passes its own
        // location, and the other three take none. A fixed buffer's capacity N is its instance's: [8 of u8] and [4 of u8] call two.
        var called = new Dictionary<string, IrFunction>();
        foreach (var (name, callee, shape) in new[]
        {
            ("fxRun", "fixed", "call void @(ptr %, ptr %)"),
            ("itFixed", "fixed", "call void @(ptr %, ptr %)"),
            ("hpRun", "heap", $"call void @(ptr %, i64 %, ptr @, i64 {Located(Buffers, "Text.heap(n)")})"),
            ("vwLength", "utf8", "call void @(ptr %, ptr noundef nonnull align 8 dereferenceable(24) %)"),
            ("vwCheck", "validateUtf8", "call void @(ptr %, ptr %)"),
        })
        {
            var bodies = FunctionsOf(ir, c, callee).ToArray();
            var call = Assert.Single(CallsOf(Assert.Single(FunctionsOf(ir, c, name))), x => bodies.Contains(x.Callee!));
            Assert.Equal(shape, call.Shape);
            called[name] = call.Callee!;
        }

        Assert.NotSame(called["fxRun"], called["itFixed"]);
        var check = Assert.Single(CallsOf(Assert.Single(FunctionsOf(ir, c, "vwCheck"))), x => x.Callee == called["vwCheck"]);
        Assert.Equal("alloca", check.Operands[1].Opcode); // The Copy Slice's own slot, which the body owns from the call on (R5-M89).

        // SPEC 22.5.1, 22.9.2 (R8-W1c): heap's body Aborts at the location it received, its last two parameters: its precondition's
        // KIMI_E_ARG_RANGE and its reserve, whose allocation Aborts there.
        var heap = called["hpRun"];
        var range = Assert.Single(CallsOf(heap), x => x.Callee == ir.Function("__kimi_abort"));
        Assert.Equal($"call void @(i32 {AbortReasons.ArgumentRange}, ptr %, i64 %, i64 -2)", range.Shape);
        Assert.Equal(heap.Parameters.TakeLast(2), range.Operands);
        var grow = Assert.Single(CallsOf(heap), x => FunctionsOf(ir, c, "reserve").Contains(x.Callee!));
        Assert.Equal(heap.Parameters.TakeLast(2), grow.Operands.TakeLast(2));

        // SPEC 22.5.1 (R8-W1a): each buffer's reserve is a body with a CallerLocation row, so the user's call passes its own location.
        var reserves = FunctionsOf(ir, c, "reserve").ToArray();
        foreach (var (name, minimum, site) in new[] { ("fxRun", 2, "(buffer@uniq).reserve(2)"), ("hpRun", 1, "(buffer@uniq).reserve(1)") })
        {
            var call = Assert.Single(CallsOf(Assert.Single(FunctionsOf(ir, c, name))), x => reserves.Contains(x.Callee!));
            Assert.Equal($"call void @(ptr %, ptr %, i64 {minimum}, ptr @, i64 {Located(Buffers, site)})", call.Shape);
        }

        // SPEC 22.5.1, 22.9.4 (R8-W1b): the window operations are bodies; limit has a CallerLocation row, so the user's call passes its own
        // location, and the status test is a body without a routine.
        var limit = Assert.Single(FunctionsOf(ir, c, "limit"));
        var cut = Assert.Single(CallsOf(Assert.Single(FunctionsOf(ir, c, "fxRun"))), x => x.Callee == limit);
        Assert.Equal($"call void @(ptr %, ptr %, i64 4, ptr @, i64 {Located(Buffers, "(window@move).limit(4)")})", cut.Shape);
        Assert.Single(FunctionsOf(ir, c, "status"));
    }

    // DECISIONS R8-M2 (RP8; T-81, T-88): the buffer and window bodies write arithmetic that an invariant or a dominating guard bounds in
    // Wrapping<isize>, so no instance of them checks for overflow; checked steps there cost the O2 Buffers kernel twice. commitUtf8 and
    // next compute only through end, which is here.
    [Fact]
    public void BufferAndWindowBodiesCheckNoBoundedArithmetic()
    {
        var c = Analyze(Buffers, OutputKind.Library);
        foreach (var name in new[] { "push", "append", "limit", "commit", "advance", "end", "next", "reserve", "validSuffix", "validUtf8" })
        {
            var texts = ItemTexts(c, name);
            Assert.NotEmpty(texts);
            Assert.All(texts, x => Assert.DoesNotContain("with.overflow", x));
        }
    }

    // SPEC 22.9.2, 7.7.4 (DECISIONS R5-M128, R8-W1c): an erased Text.fixed Item's adapter calls the body instance of its entry's own input
    // Type, the target Function Type's, [4 of u8]'s as itFixed's direct call, with its own parameters: N is the instance's.
    [Fact]
    public void AnErasedFixedItemCallsItsInputTypesInstance()
    {
        var c = Analyze(Buffers, OutputKind.Library);
        var text = Generate(c);
        var ir = GeneratedIr.Parse(text);
        var entry = ir.Function(TableOf(text, Assert.Single(ItemTexts(c, "itErasedFixed"))).Entry);
        var call = Assert.Single(CallsOf(entry));
        var bodies = FunctionsOf(ir, c, "fixed");
        Assert.Same(Assert.Single(CallsOf(Assert.Single(FunctionsOf(ir, c, "itFixed"))), x => bodies.Contains(x.Callee!)).Callee, call.Callee);
        Assert.Equal("call void @(ptr %, ptr %)", call.Shape);
        Assert.Equal([entry.Parameters[1], entry.Parameters[2]], call.Operands); // Its result slot and its input, after the environment word.
    }

    // SPEC 22.9.2, 7.7.4 (R8-W1c): an erased toString<i32> and tryFormat<i32, 4> Item's entry calls the conversion's body instance.
    [Fact]
    public void ErasedConversionItemsCallTheirBodies()
    {
        var c = Analyze(
            "func erText() -> (ref/i32) -> string => Text.toString<i32>\n" +
            "func erTry() -> (ref/i32, uniq/[4 of u8]) -> Result<Text.Utf8Slice, BufferFull> => Text.tryFormat<i32, 4>\n",
            OutputKind.Library);
        var text = Generate(c);
        var ir = GeneratedIr.Parse(text);
        foreach (var (item, body) in new[] { ("erText", "toString"), ("erTry", "tryFormat") })
        {
            var entry = ir.Function(TableOf(text, Assert.Single(ItemTexts(c, item))).Entry);
            Assert.Contains(Assert.Single(CallsOf(entry)).Callee!, FunctionsOf(ir, c, body));
        }
    }

    // SPEC 22.10.1-22.10.2 (DECISIONS R5-M128; R8-W1b-4): a write calls Utf8Writer.write's instance at the value's Type, whose BuiltinFormat
    // shim gives the runtime's built-in routine the Self's kind. The module is written (each BuiltinFormat item defined, R5-M130), and the
    // definitions are read directly.
    [Fact]
    public void ABuiltinWriteGivesItsValuesKind()
    {
        var c = Analyze(Formats, OutputKind.Library);
        var text = Generate(c);
        var ir = GeneratedIr.Parse(text);
        var (writes, shims) = (FunctionsOf(ir, c, "write"), ShimsOf(ir, text));
        foreach (var (name, kind) in new[]
        {
            ("ctWriteI64", 7), ("ctWriteU8", 2), ("ctWriteText", 16), ("ctWriteView", 17), ("ctWriteBool", 14), ("ctWriteChar", 13), ("ctWriteF64", 12),
        })
        {
            var write = Assert.Single(CalleesOf(Assert.Single(FunctionsOf(ir, c, name))), writes.Contains);
            var shim = Assert.Single(CalleesOf(write), shims.Contains);
            Assert.Contains($", i32 {kind}, ptr %", Assert.Single(CallsOf(shim)).Shape, StringComparison.Ordinal);
        }
    }

    // SPEC 22.9.2, 22.5.1 (R8-W1c): the conversions are bodies with CallerLocation rows: a direct call and an Item's Value call pass their own
    // location and no kind, and each body forwards the location it received, its last two parameters, to its write (R8-W1b-4's CallerLocation
    // row), so an Abort inside the write reports the user's conversion.
    [Fact]
    public void ConversionsAreBodiesAtTheirCallersLocation()
    {
        var c = Analyze(Formats, OutputKind.Library);
        var ir = GeneratedIr.Parse(Generate(c));
        var writes = FunctionsOf(ir, c, "write").ToArray();
        foreach (var (name, body, site) in new[]
        {
            ("ctText", "toString", "Text.toString(n)"), ("ctTry", "tryFormat", "Text.tryFormat(n, bytes)"), ("itText", "toString", "show(n)"),
        })
        {
            var bodies = FunctionsOf(ir, c, body).ToArray();
            var call = Assert.Single(CallsOf(Assert.Single(FunctionsOf(ir, c, name))), x => bodies.Contains(x.Callee!));
            Assert.EndsWith($"ptr @, i64 {Located(Formats, site)})", call.Shape, StringComparison.Ordinal);
            var write = Assert.Single(CallsOf(call.Callee!), x => writes.Contains(x.Callee!));
            Assert.Equal(call.Callee!.Parameters.TakeLast(2), write.Operands.TakeLast(2));
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

    // SPEC 22.5.1, 22.10.1 (R8-W1b-4; DECISIONS R8-M18): Utf8Writer.write has a CallerLocation row, so a direct write and an interpolated
    // value's write pass the user's location, and the instance forwards the location it received, its last two parameters, to its value's
    // format witness, whose built-in formatting reserves and Aborts there (fail-before: a Formatting.kimi location).
    [Fact]
    public void ABuiltinWriteReportsItsCallersLocation()
    {
        const string Source = "func wlDirect(writer: uniq/Utf8Writer, n: i64) -> Result<(), BufferFull> => writer.write(n)\n" +
            "func wlValue(n: i64) -> string => \"n=\\(n)\"\n";
        var c = Analyze(Source, OutputKind.Library);
        var text = Generate(c);
        var ir = GeneratedIr.Parse(text);
        var write = Assert.Single(FunctionsOf(ir, c, "write"));
        var shim = Assert.Single(ShimsOf(ir, text));
        Assert.Equal(write.Parameters.TakeLast(2), Assert.Single(CallsOf(write), x => x.Callee == shim).Operands.TakeLast(2));
        foreach (var (name, site) in new[] { ("wlDirect", "writer.write(n)"), ("wlValue", "\"n=") })
        {
            var call = Assert.Single(CallsOf(Assert.Single(FunctionsOf(ir, c, name))), x => x.Callee == write);
            Assert.EndsWith($", ptr @, i64 {Located(Source, site)})", call.Shape, StringComparison.Ordinal);
        }
    }

    // SPEC 22.10.1, 22.9.2, 7.7.4 (R8-W1b-4, W1c): a user formatter runs through the write body and the conversion bodies, a direct write, a
    // conversion, an Item's Value call and an erased Item alike, each given the user's location (CallerLocation rows); the write instance
    // alone calls the witness, once, with its result slot, the value and the adapter (no format witness is caller-location); the module
    // writes no wrapper over it.
    [Fact]
    public void AUserFormatterRunsThroughTheBodies()
    {
        var c = Analyze(UserFormats, OutputKind.Library);
        var text = Generate(c);
        var ir = GeneratedIr.Parse(text);
        Assert.DoesNotMatch(@"@__kimi_(write|tostring|tryformat)\d+\(", text);
        var witness = Assert.Single(FunctionsOf(ir, c, "format"));
        var write = Assert.Single(FunctionsOf(ir, c, "write"));
        Assert.Same(write, Assert.Single(ir.Functions, x => CalleesOf(x).Contains(witness)));
        Assert.Equal("call void @(ptr %, ptr %, ptr %)", Assert.Single(CallsOf(write), x => x.Callee == witness).Shape);
        foreach (var (name, body, site) in new[]
        {
            ("usWrite", "write", "writer.write(s)"), ("usText", "toString", "Text.toString(s)"), ("usTry", "tryFormat", "Text.tryFormat(s, bytes)"),
            ("usItem", "toString", "show(s)"),
        })
        {
            var bodies = FunctionsOf(ir, c, body).ToArray();
            var call = Assert.Single(CallsOf(Assert.Single(FunctionsOf(ir, c, name))), x => bodies.Contains(x.Callee!));
            Assert.EndsWith($", ptr @, i64 {Located(UserFormats, site)})", call.Shape, StringComparison.Ordinal);
        }

        Assert.Contains(CalleesOf(ir.Function(TableOf(text, Assert.Single(ItemTexts(c, "usErased"))).Entry)), FunctionsOf(ir, c, "toString").Contains);
    }

    // SPEC 22.9.2, 22.9.4, 7.7.4 (K package 4.5 row 6; DECISIONS R5-M130): Text.writer calls its routine with its destination's writer kind and
    // dispatch word: a FixedBuffer's 0 and a HeapBuffer's 1 with none, a user Writer's 2 with its reserve witness's function, at its call, its
    // Item's Value call and its erased adapters (SPEC 7.7.4: Text.writer is not its row's witness, so its Item runs its row); the witness is
    // never called here (the runtime calls it through the word).
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
    // old generator wrote.
    [Fact]
    public void TheFormattingRuntimeIsWrittenOnRequest()
    {
        using var runtime = new StringWriter();
        LegacyRuntimeHelpers.WriteFormattingRuntime(runtime);
        var text = Generate(Analyze(Formats, OutputKind.Library));
        var at = text.IndexOf(runtime.ToString(), StringComparison.Ordinal);
        Assert.True(at >= 0);
        Assert.Equal(at, text.LastIndexOf(runtime.ToString(), StringComparison.Ordinal));
        Assert.DoesNotContain("@" + LegacyRuntimeHelpers.FormatFixed.Name + "(", Generate(Analyze("func one() -> i32 => 1\n", OutputKind.Library)));
    }

    // R8-W1b-2: the erased writer's kept runtime (Text.writer's adapter and the reserve through its dispatch word) is a module part of its
    // own, written once in a module that calls Text.writer and in none without formatting; the formatting runtime defines neither routine.
    [Fact]
    public void TheErasedWriterRuntimeIsWrittenOnRequest()
    {
        using var formatting = new StringWriter();
        LegacyRuntimeHelpers.WriteFormattingRuntime(formatting);
        string[] definitions = ["define internal void @" + Routines(CompilerFunctionKind.TextWriter)[0] + "(", "define internal void @__kimi_erased_reserve("];
        var text = Generate(Analyze(Writers, OutputKind.Library));
        var at = text.IndexOf(LlvmModuleText.ErasedWriterRuntime, StringComparison.Ordinal);
        Assert.True(at >= 0);
        Assert.Equal(at, text.LastIndexOf(LlvmModuleText.ErasedWriterRuntime, StringComparison.Ordinal));
        var none = Generate(Analyze("func one() -> i32 => 1\n", OutputKind.Library));
        foreach (var definition in definitions)
        {
            Assert.Equal(at, text.IndexOf(definition, StringComparison.Ordinal) - LlvmModuleText.ErasedWriterRuntime.IndexOf(definition, StringComparison.Ordinal));
            Assert.Equal(text.IndexOf(definition, StringComparison.Ordinal), text.LastIndexOf(definition, StringComparison.Ordinal));
            Assert.DoesNotContain(definition, formatting.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(definition, none, StringComparison.Ordinal);
        }
    }

    // R8-W1b-3: a Requested row's call requests its own routine's module part (IntrinsicRow.Requests): ErasedReserve's routine is the kept
    // runtime's, defined there once, so its call needs no other requester; the storage byte primitives keep their pair; every other row
    // requests nothing. A module without formatting writes neither the kept runtime nor its reserve routine.
    [Fact]
    public void ARequestedRowRequestsItsRoutinesModulePart()
    {
        var reserve = IntrinsicRows.Of(IntrinsicId.ErasedReserve);
        Assert.Equal((IntrinsicWriter.Requested, ModuleCodegen.Needs.ErasedWriter), (reserve.Writer, reserve.Requests));
        Assert.Contains(reserve.Routine!, LlvmModuleText.ErasedWriterDefinitions);
        Assert.Single(Regex.Matches(LlvmModuleText.ErasedWriterRuntime, @"(?m)^define internal void @__kimi_erased_reserve\(ptr %ret, ptr %writer, i64 %amount\)"));
        Assert.Single(Regex.Matches(LlvmModuleText.ErasedWriterRuntime, @"(?m)^declare void @llvm\.memset\.p0\.i64\("));
        foreach (var id in Enum.GetValues<IntrinsicId>().Where(static x => x != IntrinsicId.None))
        {
            var row = IntrinsicRows.Of(id);
            var expected = id switch
            {
                IntrinsicId.ErasedReserve => ModuleCodegen.Needs.ErasedWriter,
                IntrinsicId.TryAllocateBytes or IntrinsicId.MoveBytes => ModuleCodegen.Needs.StorageBytes | ModuleCodegen.Needs.Memory,
                _ => ModuleCodegen.Needs.None,
            };
            Assert.True(row.Requests == expected && (row.Writer == IntrinsicWriter.Requested) == (expected != ModuleCodegen.Needs.None), id.ToString());
        }

        Assert.DoesNotContain("@__kimi_erased_reserve(", Generate(Analyze("func one() -> i32 => 1\n", OutputKind.Library)), StringComparison.Ordinal);
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

    // SPEC 22.10.5 (the old utf8-formatting §5.1; DECISIONS R5-M131): a bounded value's maximum encoded bytes (FormattingTypes.Bound), a
    // wrapping integer's its argument's and an isize's an i64's, and -1 exactly where the Type is unbounded (a string, a view, a Type
    // without built-in formatting): the one table, which an owning interpolation's estimate, and so Text.toString, reads (R8-W1c).
    [Fact]
    public void FormatBoundsAreFixed()
    {
        string[] names = ["i8", "u8", "i16", "u16", "i32", "u32", "i64", "u64", "i128", "u128", "isize", "usize", "f32", "f64", "char"];
        Assert.Equal(
            [4, 3, 6, 5, 11, 10, 20, 20, 40, 39, 20, 20, 17, 24, 4],
            names.Select(static x => FormattingTypes.Bound(BoundType.Primitives[x])));
        BoundType[] others = [BoundType.Boolean, BoundType.Unit, BoundType.String, BoundType.Range, BoundType.WrappingOf(BoundType.Primitives["u8"])];
        Assert.Equal([5, 2, -1, -1, 3], others.Select(FormattingTypes.Bound));
    }

    // The routine symbols of formatting rows (P1d's FormattingRow), in order.
    private static string[] Routines(params CompilerFunctionKind[] kinds) => [.. kinds.Select(static x => LegacyRuntimeHelpers.FormattingRow(x)!.Abi.Name)];

    // The byte length of a site's location text (SPEC 22.5.1).
    private static int Located(string source, string site) => LocationText(source, site).Length;
}
