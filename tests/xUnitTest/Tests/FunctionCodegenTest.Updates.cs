// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.RegularExpressions;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// The whole-value updates and the fixed-array storage kernel of MIR code generation (R5 C8b; SPEC 7.7.4, 15.7, 15.7.1-15.7.3, 16.3.2,
// 22.2, 22.5.1; DECISIONS R5-M116): replace's glue before the new value, exchange's old value kept before the new one, swap through a held
// value, an Item's Value call writing its direct call's row, and borrowStorage's remainder over a fixed array; natively.
public sealed partial class FunctionCodegenTest
{
    // A Library updating an i32, a 32-byte struct in memory, a drop-needing struct, a zero-sized drop-needing one, a bool, a string (also
    // from a constant) and Unit; an Item's Value calls of replace and swap; fixed arrays borrowed shared, exclusively and empty.
    private const string Updates =
        "struct Big\n    public var a: i64\n    public var b: i64\n    public var c: i64\n    public var d: i64\n" +
        "    public init(a: i64)\n        self.a = a\n        self.b = a\n        self.c = a\n        self.d = a\n" +
        "struct Res\n    public let name: string\n    public let id: i64\n    public init(name: string, id: i64)\n" +
        "        self.name = name@move\n        self.id = id\n    drop => Console.writeLine(\"res\")\n" +
        "struct Mark\n    public init() => ()\n    drop => Console.writeLine(\"mark\")\n" +
        "struct Quad\n    Self is Copy\n    public let a: i64 = 1\n    public let b: i64 = 2\n    public let c: i64 = 3\n    public let d: i64 = 4\n" +
        "func exchangeQuad(t: uniq/Quad, v: Quad) -> Quad => Kimi.Intrinsics.exchange(t, with: v)\n" +
        "func replaceInt(t: uniq/i32, v: i32) => Kimi.Intrinsics.replace(t, with: v)\n" +
        "func replaceRes(t: uniq/Res, v: Res) => Kimi.Intrinsics.replace(t, with: v@move)\n" +
        "func replaceMark(t: uniq/Mark) => Kimi.Intrinsics.replace(t, with: Mark.init())\n" +
        "func replaceText(t: uniq/string) => Kimi.Intrinsics.replace(t, with: \"new\")\n" +
        "func exchangeBig(t: uniq/Big, v: Big) -> Big => Kimi.Intrinsics.exchange(t, with: v@move)\n" +
        "func exchangeInt(t: uniq/i32, v: i32) -> i32 => Kimi.Intrinsics.exchange(t, with: v)\n" +
        "func exchangeFlag(t: uniq/bool) -> bool => Kimi.Intrinsics.exchange(t, with: false)\n" +
        "func exchangeText(t: uniq/string) -> string => Kimi.Intrinsics.exchange(t, with: \"new\")\n" +
        "func swapInt(a: uniq/i32, b: uniq/i32) => Kimi.Intrinsics.swap(a, b)\n" +
        "func swapBig(a: uniq/Big, b: uniq/Big) => Kimi.Intrinsics.swap(a, b)\n" +
        "func swapUnit(a: uniq/(), b: uniq/()) => Kimi.Intrinsics.swap(a, b)\n" +
        "func replaceItem(t: uniq/Res, v: Res)\n    let f = Kimi.Intrinsics.replace<Res>\n    f(t, v@move)\n" +
        "func swapItem(a: uniq/Big, b: uniq/Big)\n    let f = Kimi.Intrinsics.swap<Big>\n    f(a, b)\n" +
        "func total(values: ref/[3 of i64]) -> i64\n    var sum: i64 = 0\n    for v in values => sum += v\n    return sum\n" +
        "func none(values: ref/[0 of i32]) -> i32\n    var n = 0\n    for v in values => n += 1\n    return n\n" +
        "func first(values: uniq/[2 of i64]) -> i64\n    var view = values.sliceUniq()\n    return view[0]\n" +
        "func erasedExchange() -> (uniq/i32, i32) -> i32 => Kimi.Intrinsics.exchange\n" +
        "func erasedText() -> (uniq/string, string) -> string => Kimi.Intrinsics.exchange\n" +
        "func erasedReplace() -> (uniq/Res, Res) -> () => Kimi.Intrinsics.replace\n" +
        "func erasedSwap() -> (uniq/Big, uniq/Big) -> () => Kimi.Intrinsics.swap\n";

    // SPEC 15.7 (DECISIONS R5-M116): updates natively: drop order around replace, exchange's old value, swap of
    // scalars, strings and structs in memory, an Item's Value call and a fixed-array loop.
    public static TheoryData<string, string, string> UpdatePrograms => new()
    {
        {
            "UpdateRows",
            "struct Res\n    public let name: string\n    public let id: i64\n    public init(name: string, id: i64)\n" +
            "        self.name = name@move\n        self.id = id\n    drop => Console.writeLine(self.name)\n" +
            "var a = Res.init(\"first\", 1)\nKimi.Intrinsics.replace(a@uniq, with: Res.init(\"second\", 2))\nConsole.writeLine(\"replaced\")\n" +
            "do\n    let old = Kimi.Intrinsics.exchange(a@uniq, with: Res.init(\"third\", 3))\n    require old.id == 2 else => $abort(\"old\")\n" +
            "var b = Res.init(\"fourth\", 4)\nKimi.Intrinsics.swap(a@uniq, b@uniq)\nrequire a.id == 4 and b.id == 3 else => $abort(\"swap\")\n" +
            "var x: i32 = 1\nvar y: i32 = 2\nKimi.Intrinsics.swap(x@uniq, y@uniq)\nlet z = Kimi.Intrinsics.exchange(x@uniq, with: x + 5)\n" +
            "var s = \"s\"\nlet t = Kimi.Intrinsics.exchange(s@uniq, with: \"u\")\nlet f = Kimi.Intrinsics.swap<i32>\nf(x@uniq, y@uniq)\n" +
            "let items: [3 of i64] = [1, 2, 3]\nvar sum: i64 = 0\nfor v in items => sum += v\n" +
            "require x == 1 and y == 7 and z == 2 and sum == 6 else => $abort(\"values\")\n" +
            "Console.writeLine(t)\nConsole.writeLine(s)\n",
            "first\nreplaced\nsecond\ns\nu\nthird\nfourth\n"
        },
        {
            "UpdateItems",
            "struct Res\n    public let name: string\n    public let id: i64\n    public init(name: string, id: i64)\n" +
            "        self.name = name@move\n        self.id = id\n    drop => Console.writeLine(self.name)\n" +
            "var a = Res.init(\"first\", 1)\nlet r: (uniq/Res, Res) -> () = Kimi.Intrinsics.replace\nr(a@uniq, Res.init(\"second\", 2))\n" +
            "let e: (uniq/i32, i32) -> i32 = Kimi.Intrinsics.exchange\nvar n: i32 = 1\nlet old = e(n@uniq, 5)\n" +
            "var b = Res.init(\"third\", 3)\nlet s: (uniq/Res, uniq/Res) -> () = Kimi.Intrinsics.swap\ns(a@uniq, b@uniq)\n" +
            "require old == 1 and n == 5 and a.id == 3 and b.id == 2 else => $abort(\"items\")\nConsole.writeLine(\"done\")\n",
            "first\ndone\nsecond\nthird\n"
        },
    };

    [Theory]
    [MemberData(nameof(UpdatePrograms))]
    public void UpdateProgramsRun(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture(name, source, stdout);

    // SPEC 15.7.1, 16.3.2, 22.5.1 (DECISIONS R5-M116): replace destroys the target's old value by its Type's glue at the call's location,
    // then puts the new value there: a struct in memory copied, an i32 stored, a string constant's record written; a zero-sized value
    // still destroyed, nothing put; an Item's Value call writes the same row.
    [Fact]
    public void ReplaceDestroysTheOldValueBeforeItPutsTheNewOne()
    {
        var c = Analyze(Updates, OutputKind.Library);
        var text = Generate(c);
        var ir = GeneratedIr.Parse(text);
        var memcpy = ir.Function("llvm.memcpy.p0.p0.i64");
        foreach (var (name, site) in new[] { ("replaceRes", "Kimi.Intrinsics.replace(t, with: v@move)"), ("replaceItem", "f(t, v@move)") })
        {
            var location = LocationText(Updates, site);
            var function = Assert.Single(FunctionsOf(ir, c, name));
            var calls = CallsOf(function);
            var glue = Assert.Single(calls, x => x.Callee != memcpy);
            Assert.Equal("internal void (ptr, ptr, i64)", glue.Callee!.Signature);
            var put = calls[^1];
            Assert.Same(memcpy, put.Callee);
            Assert.Same(glue.Operands[0], put.Operands[0]);
            Assert.True(Array.IndexOf(calls, glue) < Array.IndexOf(calls, put));
            Assert.Equal($"call void @(ptr %, ptr @, i64 {location.Length})", glue.Shape);
            Assert.Contains(Constant(location), text);
        }

        var mark = Assert.Single(FunctionsOf(ir, c, "replaceMark"));
        Assert.Matches(@"^call void @\(ptr %, ptr @, i64 \d+\)$", CallsOf(mark)[^1].Shape);
        Assert.DoesNotContain(mark.Instructions, static x => x.Opcode == "store" && x.Shape != "store ptr %, ptr %, align 8");

        var integer = Assert.Single(FunctionsOf(ir, c, "replaceInt"));
        Assert.Empty(CallsOf(integer));
        Assert.Equal("store i32 %, ptr %, align 4", integer.Instructions.Last(static x => x.Opcode == "store").Shape);
        var textual = Assert.Single(FunctionsOf(ir, c, "replaceText"));
        var release = Assert.Single(CallsOf(textual));
        var record = textual.Instructions.Last(static x => x.Opcode == "store");
        Assert.Equal("store %kimi.string { ptr @, i64 3, i8 0 }, ptr %, align 8", record.Shape);
        Assert.Same(release.Operands[0], record.Operands[^1]);
    }

    // SPEC 15.7.1 (DECISIONS R5-M116): exchange puts the target's old value at its result before it puts the new value at the target: a
    // value in memory copied twice, a scalar loaded before the store, a bool constant's storage form, a string constant's record.
    [Fact]
    public void ExchangeKeepsTheOldValueBeforeItPutsTheNewOne()
    {
        var c = Analyze(Updates, OutputKind.Library);
        var ir = GeneratedIr.Parse(Generate(c));
        var memcpy = ir.Function("llvm.memcpy.p0.p0.i64");
        var big = Assert.Single(FunctionsOf(ir, c, "exchangeBig"));
        var copies = CallsOf(big).Where(x => x.Callee == memcpy).ToArray();
        var (kept, put) = (copies[^2], copies[^1]);
        Assert.Same(big.Parameters[0], kept.Operands[0]);
        Assert.Same(kept.Operands[1], put.Operands[0]);

        var integer = Assert.Single(FunctionsOf(ir, c, "exchangeInt"));
        var target = integer.Instructions.Last(static x => x.Opcode == "store").Operands[^1];
        var old = Assert.Single(integer.Instructions, x => x.Opcode == "load" && x.Operands[0] == target);
        Assert.True(integer.Instructions.IndexOf(old) < integer.Instructions.LastIndexOf(integer.Instructions.Last(static x => x.Opcode == "store")));

        var flag = Assert.Single(FunctionsOf(ir, c, "exchangeFlag"));
        Assert.Equal("store i8 0, ptr %, align 1", flag.Instructions.Last(static x => x.Opcode == "store").Shape);
        var textual = Assert.Single(FunctionsOf(ir, c, "exchangeText"));
        var stores = textual.Instructions.Where(static x => x.Opcode == "store").ToArray();
        Assert.Equal("store %kimi.string %, ptr %, align 8", stores[^2].Shape);
        Assert.Same(textual.Parameters[0], stores[^2].Operands[^1]);
        Assert.Equal("store %kimi.string { ptr @, i64 3, i8 0 }, ptr %, align 8", stores[^1].Shape);
    }

    // SPEC 15.7, 15.7.2 (DECISIONS R5-M116): swap holds the first value, puts the second at the first and the held one at the second:
    // scalars as two loads, then two stores; a value in memory through one held slot of the entry block and three copies, also through
    // an Item's Value call; a zero-sized value writes nothing.
    [Fact]
    public void SwapHoldsTheFirstValue()
    {
        var c = Analyze(Updates, OutputKind.Library);
        var ir = GeneratedIr.Parse(Generate(c));
        var memcpy = ir.Function("llvm.memcpy.p0.p0.i64");
        var integer = Assert.Single(FunctionsOf(ir, c, "swapInt"));
        Assert.Equal(
            ["load i32, ptr %, align 4", "load i32, ptr %, align 4", "store i32 %, ptr %, align 4", "store i32 %, ptr %, align 4"],
            integer.Instructions.Where(static x => x.Shape.Contains("i32", StringComparison.Ordinal)).Select(static x => x.Shape));
        foreach (var name in new[] { "swapBig", "swapItem" })
        {
            var function = Assert.Single(FunctionsOf(ir, c, name));
            var held = Assert.Single(function.Blocks[0].Instructions, static x => x.Shape == "alloca { [0 x i64], [32 x i8] }, align 8");
            var copies = CallsOf(function).Where(x => x.Callee == memcpy).ToArray();
            Assert.Equal(3, copies.Length);
            Assert.Same(held, copies[0].Operands[0]);
            Assert.Same(copies[0].Operands[1], copies[1].Operands[0]);
            Assert.Same(copies[1].Operands[1], copies[2].Operands[0]);
            Assert.Same(held, copies[2].Operands[1]);
        }

        var unit = Assert.Single(FunctionsOf(ir, c, "swapUnit"));
        Assert.DoesNotContain(unit.Instructions, static x => x.Opcode == "call" || (x.Opcode == "store" && x.Shape != "store ptr %, ptr %, align 8"));
    }

    // SPEC 22.2 (DECISIONS R5-M45, R5-M116): borrowStorage over a fixed array writes the remainder {storage, position, count} at its Fields:
    // the borrowed array's address, 0 and its closed length, through P2a's kernel text: the shared kernel of [3] and of an empty array (count
    // 0), the exclusive kernel of [2] (sliceUniq).
    [Fact]
    public void AFixedArraysStorageIsItsAddressAndLength()
    {
        var c = Analyze(Updates, OutputKind.Library);
        var text = Generate(c);
        foreach (var length in new[] { 3, 0, 2 })
        {
            Assert.Matches(
                $@"(?m)^  store ptr %t\d+, ptr %l\d+, align 8\n  %fpos(\d+) = getelementptr i8, ptr %l\d+, i64 8\n  store i64 0, ptr %fpos\1, align 8\n" +
                $@"  %fcount\1 = getelementptr i8, ptr %l\d+, i64 16\n  store i64 {length}, ptr %fcount\1, align 8\n",
                text);
        }
    }

    // SPEC 7.7.4 (DECISIONS R5-M116, ruling Q3): an erased update Item's entry writes the update over its own parameters: exchange<i32>
    // returns the old value it loads before the store, a string's exchange keeps it in the result slot, replace runs the glue with the
    // location the entry received, swap holds the first value in a slot of its entry block; no table has a drop entry.
    [Fact]
    public void AnErasedUpdateItemsEntryWritesTheUpdate()
    {
        var c = Analyze(Updates, OutputKind.Library);
        var text = Generate(c);
        var ir = GeneratedIr.Parse(text);
        var memcpy = ir.Function("llvm.memcpy.p0.p0.i64");
        var entries = new[] { "erasedExchange", "erasedText", "erasedReplace", "erasedSwap" }.Select(name =>
        {
            var (entry, drop) = TableOf(text, Assert.Single(ItemTexts(c, name)));
            Assert.Null(drop);
            return ir.Function(entry);
        }).ToArray();

        var integer = entries[0];
        var stored = integer.Instructions.Last(static x => x.Opcode == "store");
        var old = Assert.Single(integer.Instructions, x => x.Opcode == "load" && x.Operands[0] == stored.Operands[^1]);
        Assert.True(integer.Instructions.IndexOf(old) < integer.Instructions.IndexOf(stored));
        Assert.Same(old, integer.Instructions[^1].Operands[0]);
        Assert.Equal("ret i32 %", integer.Instructions[^1].Shape);

        var textual = entries[1];
        var stores = textual.Instructions.Where(static x => x.Opcode == "store").ToArray();
        Assert.Equal(["store %kimi.string %, ptr %, align 8", "store %kimi.string %, ptr %, align 8"], stores.Select(static x => x.Shape));
        Assert.Same(textual.Parameters[1], stores[0].Operands[^1]); // The old value at the result slot (after the environment word) ...
        Assert.Same(textual.Parameters[2], stores[1].Operands[^1]); // ... then the new one at the target.

        var replace = entries[2];
        var glue = CallsOf(replace).First();
        Assert.Equal([replace.Parameters[1], replace.Parameters[^2], replace.Parameters[^1]], glue.Operands);
        Assert.Same(memcpy, CallsOf(replace)[^1].Callee);

        var swap = entries[3];
        Assert.Single(swap.Blocks[0].Instructions, static x => x.Opcode == "alloca");
        Assert.Equal(3, CallsOf(swap).Count(x => x.Callee == memcpy));
    }

    // DECISIONS R5-M116: a whole-value update reads its operands in place, so its Copy of a value in memory gets no copy slot; a swap of a
    // value in memory has its one held slot.
    [Fact]
    public void UpdatesTakeCopySlotsByTheirRows()
    {
        var c = Analyze(Updates, OutputKind.Library);
        Assert.DoesNotContain("%s0 = alloca", Assert.Single(ItemTexts(c, "exchangeQuad")), StringComparison.Ordinal);
        Assert.Single(Regex.Matches(Assert.Single(ItemTexts(c, "swapBig")), @"%s\d+ = alloca "));
    }
}
