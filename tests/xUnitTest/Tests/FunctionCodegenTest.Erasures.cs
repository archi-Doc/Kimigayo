// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.RegularExpressions;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// The common Function values of MIR code generation (R5 C5b, C5c; IMPL 21.1.3, SPEC 7.7.4, 10.7, 16.4, 22.5.1; DECISIONS R5-M104, R5-M108):
// erasure, the adapter items' tables and entries (one per target signature), a heap environment's release, a Function value's glue, and
// a compiler function's Item called as a value or erased.
public sealed partial class FunctionCodegenTest
{
    // A Library's erasures (SPEC 7.7.4): Closures of a 4-byte environment, a 4-byte and a zero-sized drop-needing one and a 24-byte string
    // one, a function Item; an erased value called through its parameter and destroyed by its glue, and transferred to an owner call; a
    // Never-result Item erased into three results (SPEC 10.7) and two compiler functions' Items (DECISIONS R5-M108).
    private const string Erasures =
        "struct Guard\n    public let id: i32 = 1\n    drop => ()\nstruct Zero\n    drop => ()\nfunc add(a: i32, b: i32) -> i32 => a + b\n" +
        "func inline(n: i32) -> () -> i32 => func [n] () -> i32 => n\nfunc guarded(g: Guard) -> () -> i32 => func [g@move] () -> i32 => g.id\n" +
        "func zero(z: Zero) -> () -> i32 => func [z@move] () -> i32 => 7\nfunc heap(s: string) -> () -> i64 => func [s@move] () -> i64 => 1\n" +
        "func item() -> (i32, i32) -> i32 => add\nfunc call(f: () -> i32) -> i32 => f()\n" +
        "func invoke<F>(action: F) -> i32\n    F is Callable<owner, () -> i32>\n    return action@move()\n" +
        "func consume(f: () -> i32) -> i32 => invoke(f@move)\n" +
        "func fail() -> Never => $abort(\"x\")\nfunc ni() -> () -> i32 => fail\nfunc ns() -> () -> string => fail\n" +
        "func nn() -> () -> Never => fail\nfunc ra() -> (isize) -> raw/u8 => Raw.allocate\nfunc wl() -> (ref/string) -> () => Console.writeLine\n";

    // SPEC 7.7.4 (DECISIONS R5-M104): erased values called twice and destroyed once, in reverse declaration order: an inline environment, an
    // inline drop-needing one, a heap one holding a drop-declaring struct and a function Item.
    public static TheoryData<string, string, string> ErasurePrograms => new()
    {
        {
            "ErasureRows",
            "struct Noisy\n    public let name: string\n    public init(name: string) => self.name = name@move\n    drop => Console.writeLine(self.name)\n" +
            "struct Tag\n    public let id: i32\n    public init(id: i32) => self.id = id\n    drop => Console.writeLine(\"tag\")\n" +
            "func run(f: ref/(() -> i32)) -> i32 => f()\nfunc twice(x: i32) -> i32 => x + x\nlet n: i32 = 20\n" +
            "let small: () -> i32 = func [n] () -> i32 => n + 1\nlet tag = Tag.init(7)\nlet tagged: () -> i32 = func [tag@move] () -> i32 => tag.id\n" +
            "let noisy = Noisy.init(\"noisy\")\nlet big: () -> i32 = func [noisy@move] () -> i32 => 3\nlet doubled: (i32) -> i32 = twice\n" +
            "require run(small@ref) + run(small@ref) == 42 and run(tagged@ref) + run(tagged@ref) == 14 and big() + big() == 6 and " +
            "doubled(4) == 8 else => $abort(\"calls\")\nConsole.writeLine(\"done\")\n",
            "done\nnoisy\ntag\n"
        },
    };

    [Theory]
    [MemberData(nameof(ErasurePrograms))]
    public void ErasureProgramsRun(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture(name, source, stdout);

    // PD19, IMPL 21.1.3 (DECISIONS R5-M103, R5-M104): an environment of 8 bytes or less is stored in the Function value's word, zero-filled
    // first, also one that needs destruction, so its erasure allocates nothing: its table's drop entry is null, or the environment's own glue,
    // which receives its copy's address; a larger one is allocated once, at the conversion's location, and its table's drop entry, the
    // release, destroys it through its glue and frees it once.
    [Fact]
    public void AnInlineEnvironmentAllocatesNothingAndALargerOneOnce()
    {
        var c = Analyze(Erasures, OutputKind.Library);
        var text = Generate(c);
        var ir = GeneratedIr.Parse(text);
        var allocate = ir.Function("__kimi_alloc");
        foreach (var (name, drop) in new[] { ("inline", false), ("guarded", true), ("zero", true) })
        {
            var function = Assert.Single(FunctionsOf(ir, c, name));
            Assert.DoesNotContain(allocate, CalleesOf(function));
            Assert.Contains(function.Instructions, static x => x.Shape == "store i64 0, ptr %, align 8");
            var entry = TableOf(text, Assert.Single(ItemTexts(c, name)));
            Assert.Equal(drop, entry.Drop is not null);
            if (entry.Drop is { } glue)
            {
                Assert.StartsWith("__kimi_drop", glue, StringComparison.Ordinal);
                Assert.Equal("internal void (ptr, ptr, i64)", ir.Function(glue).Signature);
            }
        }

        var heap = Assert.Single(ItemTexts(c, "heap"));
        var location = LocationText(Erasures, "func [s@move]");
        Assert.Single(Regex.Matches(heap, $@"call ptr @__kimi_alloc\(i64 24, ptr @__kimi_location\.\d+, i64 {location.Length}\)"));
        Assert.Contains(Constant(location), text);
        var release = ir.Function(Assert.IsType<string>(TableOf(text, heap).Drop));
        Assert.EndsWith("_drop", TableOf(text, heap).Drop, StringComparison.Ordinal);
        var word = Assert.Single(release.Instructions, static x => x.Shape == "load ptr, ptr %, align 8");
        Assert.Same(release.Parameters[0], word.Operands[0]);
        var calls = CallsOf(release);
        Assert.Equal(2, calls.Length);
        Assert.Equal("internal void (ptr, ptr, i64)", calls[0].Callee!.Signature);
        Assert.Same(ir.Function("__kimi_free"), calls[1].Callee);
        Assert.All(calls, x => Assert.Same(word, x.Operands[0]));
    }

    // IMPL 21.3.1 (DECISIONS R5-M104): an adapter's entry is its source's erased signature, the environment word first and the context and
    // the location last; a Closure's spills an inline word to a slot of its own and passes it, a heap one the address it holds, with a null
    // context; an Item's passes its own parameters to the function.
    [Fact]
    public void AnAdapterEntryCallsItsSourceWithItsOwnParameters()
    {
        var c = Analyze(Erasures, OutputKind.Library);
        var text = Generate(c);
        var ir = GeneratedIr.Parse(text);
        var inline = ir.Function(TableOf(text, Assert.Single(ItemTexts(c, "inline"))).Entry);
        Assert.Equal("internal i32 (i64, ptr, ptr, i64)", inline.Signature);
        var slot = Assert.Single(inline.Instructions, static x => x.Shape == "alloca i64, align 8");
        Assert.Contains(inline.Instructions, x => x.Shape == "store i64 %, ptr %, align 8" && x.Operands[0] == inline.Parameters[0] && x.Operands[1] == slot);
        var body = Assert.Single(CallsOf(inline));
        Assert.Equal("call i32 @(ptr %, ptr null)", body.Shape);
        Assert.Same(slot, body.Operands[0]);
        Assert.Equal("internal i32 (ptr, ptr)", body.Callee!.Signature);

        var heap = ir.Function(TableOf(text, Assert.Single(ItemTexts(c, "heap"))).Entry);
        var pointer = Assert.Single(heap.Instructions, static x => x.Shape == "inttoptr i64 % to ptr");
        Assert.Same(heap.Parameters[0], pointer.Operands[0]);
        Assert.Same(pointer, Assert.Single(CallsOf(heap)).Operands[0]);

        var item = ir.Function(TableOf(text, Assert.Single(ItemTexts(c, "item"))).Entry);
        Assert.Equal("internal i32 (i64, i32, i32, ptr, ptr, i64)", item.Signature);
        var add = Assert.Single(CallsOf(item));
        Assert.Same(Assert.Single(FunctionsOf(ir, c, "add")), add.Callee);
        Assert.Equal([item.Parameters[1], item.Parameters[2]], add.Operands);
    }

    // SPEC 22.5.1 (DECISIONS R5-M104): an erased Item whose function is a caller-location body (FromEnd.resolve) gives it the location its
    // own entry received, the erased call's, after its other parameters: the entry's, but for its environment word and its context.
    [Fact]
    public void AnAdapterEntryForwardsItsLocationToACallerLocationBody()
    {
        var c = Analyze("func resolver() -> (FromEnd<i32>, isize) -> isize => FromEnd<i32>.resolve\n", OutputKind.Library);
        var text = Generate(c);
        var entry = GeneratedIr.Parse(text).Function(TableOf(text, Assert.Single(ItemTexts(c, "resolver"))).Entry);
        var own = entry.Parameters;
        var body = Assert.Single(CallsOf(entry));
        Assert.EndsWith(", ptr, i64)", body.Callee!.Signature, StringComparison.Ordinal);
        Assert.Equal([.. own.Skip(1).Take(own.Count - 4), own[^2], own[^1]], body.Operands);
    }

    // SPEC 16.4, 8.10 (DECISIONS R5-M102, R5-M104 as G182 amends it): a Function value's glue calls its table's drop entry, when present,
    // with the address of its word's copy in an entry slot, so the value's own address does not escape, and its own location and length; a
    // parameter's Drop destroys it so, and an owner call of the erased value destroys the value after the call.
    [Fact]
    public void AFunctionValueIsDestroyedByItsTablesDropEntry()
    {
        var c = Analyze(Erasures, OutputKind.Library);
        var ir = GeneratedIr.Parse(Generate(c));
        var call = Assert.Single(FunctionsOf(ir, c, "call"));
        var glue = Assert.Single(CallsOf(call), static x => x.Callee?.Signature == "internal void (ptr, ptr, i64)").Callee!;
        Assert.Equal(
            ["alloca i64, align 8", "getelementptr i8, ptr %, i64 8", "load ptr, ptr %, align 8", "getelementptr i8, ptr %, i64 16",
                "load ptr, ptr %, align 8", "icmp ne ptr %, null", "br i1 %, label %, label %"],
            glue.Blocks[0].Instructions.Select(static x => x.Shape));
        var (slot, entry) = (glue.Blocks[0].Instructions[0], glue.Blocks[0].Instructions[4]);
        var guarded = glue.Blocks[1].Instructions;
        Assert.Equal(
            ["load i64, ptr %, align 8", "store i64 %, ptr %, align 8", "call void %(ptr %, ptr %, i64 %)", "br label %"],
            guarded.Select(static x => x.Shape));
        Assert.Same(glue.Parameters[0], guarded[0].Operands[0]);
        Assert.Equal([guarded[0], slot], guarded[1].Operands);
        Assert.Equal([entry, slot, glue.Parameters[1], glue.Parameters[2]], guarded[2].Operands);
        var invoke = Assert.Single(FunctionsOf(ir, c, "invoke"));
        var calls = CallsOf(invoke).Where(static x => x.Shape.StartsWith("call i32", StringComparison.Ordinal) ||
            x.Callee?.Signature == "internal void (ptr, ptr, i64)").ToArray();
        Assert.Equal(2, calls.Length);
        Assert.Same(calls[0].Operands[1].Operands[0], calls[1].Operands[0]);
        Assert.Matches(@"^call i32 %\(i64 %, ptr %, ptr @, i64 \d+\)$", calls[0].Shape);
        Assert.Equal("internal void (ptr, ptr, i64)", calls[1].Callee!.Signature);
    }

    // SPEC 10.7, 7.7.4 (DECISIONS R5-M108): a source whose result is Never fits any result, so its adapter items are keyed also by their
    // target Function Type and each entry has the erased signature the target's calls write, the source's call then unreachable: a function
    // Item into i32, string and Never; a requirement Item whose witness's result is Never, at its requirement's own result (a slot Type); a
    // Closure annotated Never, stored and then erased.
    [Theory]
    [InlineData(Erasures, "ni", "internal i32 (i64, ptr, ptr, i64)")]
    [InlineData(Erasures, "ns", "internal void (i64, ptr, ptr, ptr, i64)")]
    [InlineData(Erasures, "nn", "internal void (i64, ptr, ptr, i64)")]
    [InlineData(NeverWitness, "g", "internal void (i64, ptr, ptr, ptr, i64)")]
    [InlineData("func c() -> () -> i32\n    let k = func () -> Never => $abort(\"x\")\n    return k@move\n", "c", "internal i32 (i64, ptr, ptr, i64)")]
    public void ANeverSourceHasAnEntryPerTargetSignature(string source, string function, string signature)
    {
        var c = Analyze(source, OutputKind.Library);
        var text = Generate(c);
        var entry = GeneratedIr.Parse(text).Function(TableOf(text, Assert.Single(ItemTexts(c, function))).Entry);
        Assert.Equal(signature, entry.Signature);
        Assert.Equal(signature.StartsWith("internal void (i64, ptr, ptr, i64)", StringComparison.Ordinal), entry.Attributes.Contains("noreturn"));
        Assert.Single(CallsOf(entry));
        Assert.Equal(["call", "unreachable"], entry.Instructions.Select(static x => x.Opcode).TakeLast(2));
    }

    // SPEC 7.7.4 (DECISIONS R5-M104): a compiler function's Item called as a value calls what its direct call does, its intrinsic's row over
    // the call's operands (Raw.allocate: the count, the element's stride, the call's location).
    [Fact]
    public void ACompilerFunctionItemCallsItsDirectCallsRow()
    {
        var c = Analyze("func alloc() -> raw/u8\n    let f = Raw.allocate<u8>\n    return f(2)\n", OutputKind.Library);
        var ir = GeneratedIr.Parse(Generate(c));
        var call = Assert.Single(CallsOf(Assert.Single(FunctionsOf(ir, c, "alloc"))));
        Assert.Same(ir.Function("__kimi_raw_allocate"), call.Callee);
        Assert.Matches(@"^call ptr @\(i64 2, i64 1, ptr @, i64 \d+\)$", call.Shape);
    }

    // SPEC 7.7.4, 22.5.1 (DECISIONS R5-M108): an erased compiler function's Item runs its direct call's routine row from its entry, with the
    // entry's own parameters, the row's slot-less values and the location the entry received (the erased call's): Raw.allocate's count, its
    // element's stride and the location; the console writeLine's borrowed string and the location.
    [Fact]
    public void AnErasedCompilerFunctionItemRunsItsRoutine()
    {
        var c = Analyze(Erasures, OutputKind.Library);
        var text = Generate(c);
        var ir = GeneratedIr.Parse(text);
        var allocate = ir.Function(TableOf(text, Assert.Single(ItemTexts(c, "ra"))).Entry);
        Assert.Equal("internal ptr (i64, i64, ptr, ptr, i64)", allocate.Signature);
        var call = Assert.Single(CallsOf(allocate));
        Assert.Same(ir.Function("__kimi_raw_allocate"), call.Callee);
        Assert.Equal("call ptr @(i64 %, i64 1, ptr %, i64 %)", call.Shape);
        Assert.Equal([allocate.Parameters[1], allocate.Parameters[3], allocate.Parameters[4]], call.Operands);
        var write = ir.Function(TableOf(text, Assert.Single(ItemTexts(c, "wl"))).Entry);
        var line = Assert.Single(CallsOf(write));
        Assert.Same(ir.Function("__kimi_write_line"), line.Callee);
        Assert.Equal([write.Parameters[1], write.Parameters[3], write.Parameters[4]], line.Operands);
    }

    // SPEC 7.7.4 (DECISIONS R5-M108, R5-M128, R5-M130): a compiler function's call is its direct call's row, whatever targets the row needs:
    // Text.toString's row runs the format witness and Text.writer's the reserve witness, but neither function is its witness, so their Items,
    // called as values or erased, run their row: a user formatter's toString its conversion over the witness's write wrapper, which alone
    // calls the witness, and Text.writer its routine, whose dispatch word names the witness, which nothing calls (they called the witness
    // with the function's operands before).
    [Theory]
    [InlineData(UserFormat + "public func run(n: ref/Value<i32>) -> string\n    let show = Text.toString<Value<i32>>\n    return show(n)\n", "format", 1)]
    [InlineData(
        UserFormat + "public func run(n: ref/Value<i32>) -> string\n    let show: (ref/Value<i32>) -> string = Text.toString\n    return show(n)\n",
        "format",
        1)]
    [InlineData(Sink + "public func run(s: uniq/Sink) -> Utf8Writer during s\n    let make = Text.writer<Sink>\n    return make(s)\n", "reserve", 0)]
    public void ACompilerFunctionsItemRunsItsRowNotItsRowsWitness(string source, string witness, int callers)
    {
        var c = Analyze(source, OutputKind.Library);
        var text = Generate(c);
        var ir = GeneratedIr.Parse(text);
        var body = Assert.Single(FunctionsOf(ir, c, witness));
        var calling = ir.Functions.Where(x => CalleesOf(x).Contains(body)).ToArray();
        Assert.Equal(callers, calling.Length);
        Assert.All(calling, x => Assert.Same(ir.Function(Regex.Match(text, @"define internal void @(__kimi_write\d+)\(").Groups[1].Value), x));
    }

    // SPEC 7.7.4, 10.7, 22.5.1 (DECISIONS R5-M108): erased compiler functions' Items and a Never Item erased into two results run natively, the
    // Never one called through a borrowed string-result value.
    [Fact]
    public void ErasedTargetsRun()
        => ScalarEmissionTest.EmitFixture("ErasureTargets", ErasureTargets, "write\n", 1, "Hello.kimi:1:25: abort KIMI_E_ABORT: never\n");

    // SPEC 7.7.4 (DECISIONS R5-M104): a compiler function's Item whose direct call is a legacy runtime row (the console writeLine) calls that
    // row, over the call's operands and its location.
    [Fact]
    public void AConsoleItemCallsItsLegacyRow()
    {
        var c = Analyze(
            "func call<F>(f: ref/F)\n    F is Callable<(ref/string) -> ()>\n    f(\"x\")\nfunc show() => call(Console.writeLine)\n", OutputKind.Library);
        var text = Generate(c);
        var ir = GeneratedIr.Parse(text);
        var call = Assert.Single(CallsOf(Assert.Single(FunctionsOf(ir, c, "call"))), x => x.Callee == ir.Function("__kimi_write_line"));
        Assert.Matches(@"^call void @\(ptr noundef nonnull align 8 dereferenceable\(24\) %, ptr @, i64 \d+\)$", call.Shape);
        Assert.Contains(Constant("Hello.kimi:3:5"), text);
    }

    // ErasedTargetsRun's program.
    private const string ErasureTargets = "func fail() -> Never => $abort(\"never\")\nfunc pick(f: ref/(() -> string)) -> string => f()\n" +
        "let w: (ref/string) -> () = Console.writeLine\nw(\"write\")\nlet a: (isize) -> raw/u8 = Raw.allocate\nlet p = a(0)\n" +
        "let n: () -> i32 = fail\nlet s: () -> string = fail\nConsole.writeLine(pick(s@ref))\n";

    // A requirement whose witness's result is Never (SPEC 10.7), its Item taken in a generic function at the requirement's result.
    private const string NeverWitness = "contract Empty\n    func empty() -> Self\nstruct Impossible\n    Self is Empty\n    public let x: i64 = 0\n" +
        "    public func empty() -> Never => $abort(\"no value\")\nfunc g<T>() -> () -> T\n    T is Empty\n    T is Owned\n    return T.empty\n" +
        "public func h() -> () -> Impossible => g<Impossible>()\n";

    // A user Utf8Format witness (Text.toString's row runs it).
    private const string UserFormat = "struct Value<T>\n    Self is Utf8Format\n    let value: T\n    public init(value: T) => self.value = value@move\n" +
        "    public func format(self: ref/Self, writer: uniq/Utf8Writer) -> Result<(), BufferFull> => Result<(), BufferFull>.Ok(())\n";

    // A user BufferWriter witness (Text.writer's row reserves through it).
    private const string Sink = "struct Sink\n    Self is BufferWriter\n    public var count: i32 = 0\n" +
        "    public func reserve(self: uniq/Self, minimum: isize) -> Result<WriteWindow, BufferFull>\n        self.count += 1\n" +
        "        return .Err(BufferFull.init())\n";

    // The table an item's text stores into its Function value, read from the module's text: its entry's and its drop entry's names (null:
    // none).
    private static (string Entry, string? Drop) TableOf(string module, string item)
    {
        var table = Regex.Match(item, @"store ptr @(__kimi_erase\d+)_table,").Groups[1].Value;
        var row = Regex.Match(module, $@"(?m)^@{table}_table = private constant {{ ptr, ptr, ptr }} {{ ptr @{table}, ptr null, ptr (null|@(\S+)) }}, align 8$");
        Assert.True(row.Success, table);
        return (table, row.Groups[2].Success ? row.Groups[2].Value : null);
    }

    // The location text of a site, its first character's (SPEC 22.5.1).
    private static string LocationText(string source, string site)
    {
        var at = source.IndexOf(site, StringComparison.Ordinal);
        return $"Hello.kimi:{source[..at].Count(static x => x == '\n') + 1}:{at - source.LastIndexOf('\n', Math.Max(at - 1, 0))}";
    }
}
