// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.RegularExpressions;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// The common Function values of MIR code generation (R5 C5b; IMPL 21.1.3, SPEC 7.7.4, 16.4, 22.5.1; DECISIONS R5-M104): erasure, the adapter
// items' tables and entries, a heap environment's release, a Function value's glue, and a compiler function's Item called as a value.
public sealed partial class FunctionCodegenTest
{
    // A Library's erasures (SPEC 7.7.4): Closures of a 4-byte environment, a 4-byte and a zero-sized drop-needing one and a 24-byte string
    // one, a function Item; an erased value called through its parameter and destroyed by its glue, and transferred to an owner call.
    private const string Erasures =
        "struct Guard\n    public let id: i32 = 1\n    drop => ()\nstruct Zero\n    drop => ()\nfunc add(a: i32, b: i32) -> i32 => a + b\n" +
        "func inline(n: i32) -> () -> i32 => func [n] () -> i32 => n\nfunc guarded(g: Guard) -> () -> i32 => func [g@move] () -> i32 => g.id\n" +
        "func zero(z: Zero) -> () -> i32 => func [z@move] () -> i32 => 7\nfunc heap(s: string) -> () -> i64 => func [s@move] () -> i64 => 1\n" +
        "func item() -> (i32, i32) -> i32 => add\nfunc call(f: () -> i32) -> i32 => f()\n" +
        "func invoke<F>(action: F) -> i32\n    F is Callable<owner, () -> i32>\n    return action@move()\n" +
        "func consume(f: () -> i32) -> i32 => invoke(f@move)\n";

    // SPEC 7.7.4 (DECISIONS R5-M104): erased values called twice and destroyed once, in reverse declaration order: an inline environment, an
    // inline drop-needing one, a heap one holding a drop-declaring struct and a function Item, also under the old generator.
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
    // which receives the word's address; a larger one is allocated once, at the conversion's location, and its table's drop entry, the
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

    // SPEC 16.4, 8.10 (DECISIONS R5-M102, R5-M104): a Function value's glue calls its table's drop entry, when present, with its own
    // parameters; a parameter's Drop destroys it so, and an owner call of the erased value destroys the value after the call.
    [Fact]
    public void AFunctionValueIsDestroyedByItsTablesDropEntry()
    {
        var c = Analyze(Erasures, OutputKind.Library);
        var ir = GeneratedIr.Parse(Generate(c));
        var call = Assert.Single(FunctionsOf(ir, c, "call"));
        var glue = Assert.Single(CallsOf(call), static x => x.Callee?.Signature == "internal void (ptr, ptr, i64)").Callee!;
        Assert.Equal(
            ["getelementptr i8, ptr %, i64 8", "load ptr, ptr %, align 8", "getelementptr i8, ptr %, i64 16", "load ptr, ptr %, align 8",
                "icmp ne ptr %, null", "br i1 %, label %, label %"],
            glue.Blocks[0].Instructions.Select(static x => x.Shape));
        var drop = Assert.Single(glue.Instructions, static x => x.Shape == "call void %(ptr %, ptr %, i64 %)");
        Assert.Equal([glue.Instructions[3], .. glue.Parameters], drop.Operands);
        var invoke = Assert.Single(FunctionsOf(ir, c, "invoke"));
        var calls = CallsOf(invoke).Where(static x => x.Shape.StartsWith("call i32", StringComparison.Ordinal) ||
            x.Callee?.Signature == "internal void (ptr, ptr, i64)").ToArray();
        Assert.Equal(2, calls.Length);
        Assert.Same(calls[0].Operands[1].Operands[0], calls[1].Operands[0]);
        Assert.Matches(@"^call i32 %\(i64 %, ptr %, ptr @, i64 \d+\)$", calls[0].Shape);
        Assert.Equal("internal void (ptr, ptr, i64)", calls[1].Callee!.Signature);
    }

    // SPEC 10.7, 7.7.4 (DECISIONS R5-M104): an Item whose result is Never fits any result, but its adapter's entry is under its own signature
    // until C5c keys an adapter by its target Function Type: erased into another result it is one located Unsupported_Kd at each
    // conversion; erased into its own, it is written, the entry ending unreachable after the call.
    [Fact]
    public void ANeverItemIsErasedOnlyIntoItsOwnSignatureUntilC5c()
    {
        const string Fail = "func fail() -> Never => $abort(\"x\")\n";
        var c = Analyze(Fail + "func i() -> () -> i32 => fail\nfunc s() -> () -> string => fail\n", OutputKind.Library);
        Assert.False(c.Generate(TextWriter.Null));
        Assert.Equal([("Unsupported_Kd", (string?)"fail"), ("Unsupported_Kd", "fail")], Records(c));

        c = Analyze(Fail + "func n() -> () -> Never => fail\n", OutputKind.Library);
        var text = Generate(c);
        var entry = GeneratedIr.Parse(text).Function(TableOf(text, Assert.Single(ItemTexts(c, "n"))).Entry);
        Assert.Equal("internal void (i64, ptr, ptr, i64)", entry.Signature);
        Assert.Contains("noreturn", entry.Attributes);
        Assert.Equal(["call", "unreachable"], entry.Instructions.Select(static x => x.Opcode));
    }

    // SPEC 7.7.4 (DECISIONS R5-M104): a compiler function's Item called as a value calls what its direct call does, its intrinsic's row over
    // the call's operands (Raw.allocate: the count, the element's stride, the call's location); erased, it waits for C5c's adapter key.
    [Fact]
    public void ACompilerFunctionItemCallsItsDirectCallsRow()
    {
        var c = Analyze("func alloc() -> raw/u8\n    let f = Raw.allocate<u8>\n    return f(2)\n", OutputKind.Library);
        var ir = GeneratedIr.Parse(Generate(c));
        var call = Assert.Single(CallsOf(Assert.Single(FunctionsOf(ir, c, "alloc"))));
        Assert.Same(ir.Function("__kimi_raw_allocate"), call.Callee);
        Assert.Matches(@"^call ptr @\(i64 2, i64 1, ptr @, i64 \d+\)$", call.Shape);

        c = Analyze("func e() -> (isize) -> raw/u8 => Raw.allocate\n", OutputKind.Library);
        Assert.False(c.Generate(TextWriter.Null));
        Assert.Equal([("Unsupported_Kd", (string?)"Raw.allocate")], Records(c));
    }

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
