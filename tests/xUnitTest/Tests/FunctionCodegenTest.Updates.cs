// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// The whole-value updates as calls of their bodies' instances (SPEC 7.7.4, 15.7, 15.7.1, 16.3.2, 22.5.1; R8-U1) and borrowStorage's
// remainder over a fixed array (SPEC 22.2); natively.
public sealed partial class FunctionCodegenTest
{
    // A Library updating an i32, a 32-byte struct in memory, a drop-needing struct, a zero-sized drop-needing one, a bool, a string (also
    // from a constant) and Unit; an Item's Value calls of replace and swap; erased update Items; fixed arrays borrowed shared, exclusively
    // and empty. Every function is read: GenerationIsDeterministic and WarmGenerationAllocatesNothing take the whole source, so each
    // form's body instance and adapter stays generated and allocation-checked.
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

    // SPEC 15.7, 15.7.1, 22.5.1 (R8-U1): an update is a call of its body's instance, directly and as an Item's Value call; replace's call
    // passes the call's location (CallerLocation), and replace's body destroys the old value by T's glue at the location it received
    // before it places the new value.
    [Fact]
    public void AnUpdateIsACallOfItsBodysInstance()
    {
        var c = Analyze(Updates, OutputKind.Library);
        var ir = GeneratedIr.Parse(Generate(c));
        var memcpy = ir.Function("llvm.memcpy.p0.p0.i64");
        var replaces = FunctionsOf(ir, c, "replace");
        foreach (var (name, site) in new[] { ("replaceRes", "Kimi.Intrinsics.replace(t, with: v@move)"), ("replaceItem", "f(t, v@move)") })
        {
            var call = Assert.Single(CallsOf(Assert.Single(FunctionsOf(ir, c, name))), x => replaces.Contains(x.Callee));
            Assert.Equal($"call void @(ptr %, ptr %, ptr @, i64 {LocationText(Updates, site).Length})", call.Shape);
        }

        foreach (var (name, update) in new[] { ("exchangeBig", "exchange"), ("swapBig", "swap"), ("swapItem", "swap") })
        {
            Assert.Single(CallsOf(Assert.Single(FunctionsOf(ir, c, name))), x => FunctionsOf(ir, c, update).Contains(x.Callee));
        }

        var res = Assert.Single(CallsOf(Assert.Single(FunctionsOf(ir, c, "replaceRes"))), x => replaces.Contains(x.Callee)).Callee!;
        var calls = CallsOf(res);
        var glue = Assert.Single(calls, static x => x.Callee!.Signature == "internal void (ptr, ptr, i64)");
        Assert.Equal([res.Parameters[^2], res.Parameters[^1]], glue.Operands[1..]);
        Assert.True(Array.IndexOf(calls, glue) < Array.LastIndexOf(calls, calls.Last(x => x.Callee == memcpy)));
    }

    // SPEC 22.2 (R8-A1d): borrowStorage's body over a fixed array gives its remainder's init the borrowed array's address and its closed
    // length, a constant it stores, reading no element: the shared body of [3] and of an empty array (0), the exclusive body of [2] (sliceUniq).
    [Fact]
    public void AFixedArraysStorageIsItsAddressAndLength()
    {
        var c = Analyze(Updates, OutputKind.Library);
        var ir = GeneratedIr.Parse(Generate(c));
        var stores = new[] { "borrowStorage", "borrowStorageUniq" }.SelectMany(x => FunctionsOf(ir, c, x)).SelectMany(static x => x.Instructions).Select(static x => x.Shape).ToArray();
        foreach (var length in new[] { 3, 0, 2 })
        {
            Assert.Contains($"store i64 {length}, ptr %, align 8", stores);
        }
    }
}
