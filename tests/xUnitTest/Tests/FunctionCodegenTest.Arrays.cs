// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.RegularExpressions;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Xunit;

namespace XunitTest;

// The Array family of MIR code generation (R5 C8d; SPEC 4.3, 4.5, 4.9.3, 4.9.5, 4.9.7, 7.7.4, 16.3.2, 22.2; DECISIONS R5-M121): each operation
// and storage kind calls its LegacyRow item's routine (P1b's helper of its kind over the element, or a capacity routine given the element's
// stride) with the call's operands by slot, its result slot and location, at a direct call, an Item's Value call and an adapter's entry; an
// Array from none, setArrayLength and capacity are written in place; an Array's glue calls its Drop helper, which destroys the elements by
// their glue; natively.
public sealed partial class FunctionCodegenTest
{
    // A Library over Arrays of i32, a 16-byte struct, a drop-needing struct, a 24-byte Copy struct, string and Unit: the eight operations, the
    // storage kinds through the library's iteration, setArrayLength through a repeating construction, capacity, an Array from none, an Array
    // glue, Items and an erased Item.
    private const string Arrays =
        "struct Pair\n    public let a: i64\n    public let b: i64\n    public init(a: i64, b: i64)\n        self.a = a\n        self.b = b\n" +
        "struct Res\n    public let id: i64\n    public init(id: i64) => self.id = id\n    drop => Console.writeLine(\"res\")\n" +
        "struct Point\n    Self is Copy\n    public let x: i64 = 1\n    public let y: i64 = 2\n    public let z: i64 = 3\n" +
        "func arPut(values: uniq/Array<i32>, value: i32) => values.append(value)\n" +
        "func arPutText(values: uniq/Array<string>, value: string) => values.append(value@move)\n" +
        "func arPutUnit(values: uniq/Array<()>) => values.append(())\n" +
        "func arPutFirst(seed: Point) -> isize\n    var values: Array<Point> = [seed]\n    values.append(values[0])\n    return values.length\n" +
        "func arTake(values: uniq/Array<i32>) -> Option<i32> => values.pop()\n" +
        "func arCut(values: uniq/Array<i32>) -> i32 => values.remove(0)\n" +
        "func arCutPair(values: uniq/Array<Pair>) -> Pair => values.remove(0)\n" +
        "func arCutUnit(values: uniq/Array<()>) => values.remove(0)\n" +
        "func arWedge(values: uniq/Array<i32>) => values.insert(0, 7)\n" +
        "func arTrade(values: uniq/Array<i32>) => values.swap(0, 1)\n" +
        "func arEmpty(values: uniq/Array<Res>) => values.clear()\n" +
        "func arEmptyInts(values: uniq/Array<i32>) => values.clear()\n" +
        "func arRoom(values: uniq/Array<Pair>) => values.reserve(8)\n" +
        "func arFit(values: uniq/Array<Pair>) => values.shrinkToFit()\n" +
        "func arSize(values: ref/Array<i32>) -> isize => values.capacity\n" +
        "func arNone() -> Array<i32> => []\n" +
        "func arTotal(values: ref/Array<i32>) -> i32\n    var sum: i32 = 0\n    for v in values => sum += v\n    return sum\n" +
        "func arDrain(values: Array<Res>) -> i64\n    var sum: i64 = 0\n    for v in values@move => sum += v.id\n    return sum\n" +
        "func arDrainFixed(values: [3 of Res]) -> i64\n    var sum: i64 = 0\n    for v in values@move => sum += v.id\n    return sum\n" +
        "func arView(values: uniq/Array<i32>) -> i32\n    var outer = values.sliceUniq()\n    var inner = outer.sliceUniq()\n    return inner[0]\n" +
        "func arCopies() -> Array<i32> => Array<i32>.init(repeating: 1, count: 3)\n" +
        "func arKeep(values: Array<Res>) => ()\n" +
        "func arItemTake(values: uniq/Array<i32>) -> Option<i32>\n    let f = Array<i32>.pop\n    return f(values)\n" +
        "func arItemRoom(values: uniq/Array<Pair>)\n    let f = Array<Pair>.reserve\n    f(values, 4)\n" +
        "func arErased() -> (uniq/Array<i32>, i32) -> () => Array<i32>.append\n" +
        "func arErasedText() -> (uniq/Array<string>, string) -> () => Array<string>.append\n";

    // SPEC 4.9.3-4.9.8 (DECISIONS R5-M121): the operations, the storage boundary, zero-sized and drop-needing elements, strings, an Item's Value
    // call and an erased Item natively.
    public static TheoryData<string, string, string> ArrayPrograms => new()
    {
        {
            "ArrayRows",
            "struct Res\n    public let name: string\n    public init(name: string) => self.name = name@move\n    drop => Console.writeLine(self.name)\n" +
            "struct Token\n    public init() => ()\n    drop => Console.writeLine(\"token\")\n" +
            "struct Pair\n    Self is Copy\n    public let a: i64\n    public let b: i64\n    public init(a: i64, b: i64)\n        self.a = a\n        self.b = b\n" +
            "var values: Array<i32> = [3, 1, 2]\nvalues.append(4)\nvalues.insert(0, 9)\nlet removed = values.remove(1)\nvalues.swap(0, ^1)\n" +
            "require values.length == 4 and removed == 3 and values[0] == 4 and values[3] == 9 else => $abort(\"ints\")\n" +
            "match values.pop()\n    .Some(let last) => require last == 9 else => $abort(\"pop\")\n    .None => $abort(\"none\")\n" +
            "values.reserve(16)\nrequire values.capacity >= 19 else => $abort(\"reserve\")\nvalues.shrinkToFit()\n" +
            "require values.capacity >= values.length else => $abort(\"shrink\")\n" +
            "var total: i32 = 0\nfor v in values => total += v\nrequire total == 7 else => $abort(\"total\")\n" +
            "var it = values.iterateUniq()\nloop\n    match it.next()\n        .Some(let v) => v@follow += 10\n        .None => exit\n" +
            "require values[0] == 14 else => $abort(\"uniq\")\n" +
            "var view = values.sliceUniq()\nvar inner = view.sliceUniq()\nrequire inner[1] == 11 else => $abort(\"view\")\n" +
            "let copies = Array<i32>.init(repeating: 5, count: 3)\nrequire copies.length == 3 and copies[2] == 5 else => $abort(\"copies\")\n" +
            "var pairs: Array<Pair> = []\npairs.append(Pair.init(1, 2))\npairs.append(pairs[0])\nlet p = pairs.remove(0)\n" +
            "require p.b == 2 and pairs.length == 1 else => $abort(\"pairs\")\n" +
            "var texts: Array<string> = [\"a\", \"b\"]\ntexts.append(\"c\")\nlet t = texts.remove(0)\nConsole.writeLine(t)\n" +
            "for s in texts@move => Console.writeLine(s)\n" +
            "var res: Array<Res> = [Res.init(\"r1\"), Res.init(\"r2\")]\nres.append(Res.init(\"r3\"))\nres.clear()\nConsole.writeLine(\"cleared\")\n" +
            "res.append(Res.init(\"r4\"))\n" +
            "let fixed: [2 of Res] = [Res.init(\"f1\"), Res.init(\"f2\")]\nfor r in fixed@move\n    Console.writeLine(\"item\")\n    exit\n" +
            "var tokens: Array<Token> = [Token.init(), Token.init()]\ntokens.append(Token.init())\n" +
            "require tokens.length == 3 else => $abort(\"tokens\")\nlet gone = tokens.remove(0)\n" +
            "match tokens.pop()\n    .Some(let k) => Console.writeLine(\"popped\")\n    .None => $abort(\"empty\")\n" +
            "var units: Array<()> = []\nunits.append(())\nunits.append(())\nrequire units.length == 2 else => $abort(\"units\")\n" +
            "let f = Array<i32>.append\nf(values@uniq, 1)\nlet g: (uniq/Array<i32>, i32) -> () = Array<i32>.append\ng(values@uniq, 2)\n" +
            "require values.length == 5 else => $abort(\"items\")\nConsole.writeLine(\"done\")\n",
            "a\nb\nc\nr3\nr2\nr1\ncleared\nitem\nf1\nf2\npopped\ntoken\ndone\ntoken\ntoken\nr4\n"
        },
        {
            // SPEC 4.3.1, 4.9.5, 12.4.1, 16.2.1 (DECISIONS R5-M129): literals of 1, 8 and 64 elements, of strings, of a drop-needing struct
            // (complete: destroyed from the last; interrupted by `try`: the elements secured so far, from the last), generic, empty, zero-sized,
            // nested, of fixed arrays, Tuples and Closures.
            "ArrayLiterals",
            "struct Res\n    public let name: string\n    public init(name: string) => self.name = name@move\n    drop => Console.writeLine(self.name)\n" +
            "func third(ok: bool) -> Result<Res, i32>\n    if ok => return .Ok(Res.init(\"c\"))\n    return .Err(7)\n" +
            "func build(ok: bool) -> Result<isize, i32>\n    let values: Array<Res> = [Res.init(\"a\"), Res.init(\"b\"), try third(ok)]\n" +
            "    Console.writeLine(\"built\")\n    return .Ok(values.length)\n" +
            "func pair<T>(a: T, b: T) -> Array<T> => [a@move, b@move]\nfunc none<T>() -> Array<T> => []\n" +
            "match build(true)\n    .Ok(let n) => require n == 3 else => $abort(\"three\")\n    .Err(_) => $abort(\"ok\")\n" +
            "match build(false)\n    .Ok(_) => $abort(\"err\")\n    .Err(let e) => require e == 7 else => $abort(\"seven\")\n" +
            "let one: Array<isize> = [41]\nlet eight: Array<isize> = [1, 2, 3, 4, 5, 6, 7, 8]\n" +
            "let wide: Array<isize> = [" + string.Join(", ", Enumerable.Range(0, 64)) + "]\n" +
            "require one[0] + eight[7] + wide[63] == 112 and wide.length == 64 else => $abort(\"ints\")\n" +
            "let texts: Array<string> = [\"x\", \"yy\"]\nConsole.writeLine(texts[1])\nlet names = pair(\"p\", \"q\")\nConsole.writeLine(names[0])\n" +
            "let empty = none<isize>()\nrequire empty.length == 0 and empty.capacity == 0 else => $abort(\"empty\")\n" +
            "let units: Array<()> = [(), (), ()]\nrequire units.length == 3 else => $abort(\"units\")\n" +
            "let nested: Array<Array<isize>> = [[1], [], [2, 3]]\nvar lengths: isize = 0\nfor row in nested => lengths = lengths * 10 + row.length\n" +
            "require lengths == 102 else => $abort(\"nested\")\n" +
            "let fixed: Array<[2 of isize]> = [[1, 2], [3, 4]]\nrequire fixed[1][0] == 3 else => $abort(\"fixed\")\n" +
            "let tuples: Array<(string, isize)> = [(\"t\", 1), (\"u\", 2)]\nConsole.writeLine(tuples[1].0)\n" +
            "let k: isize = 5\nlet closures: Array<(isize) -> isize> = [func (v) => v + k, func (v) => v * k]\n" +
            "require closures[0](1) + closures[1](2) == 16 else => $abort(\"closures\")\nConsole.writeLine(\"done\")\n",
            "built\nc\nb\na\nb\na\nyy\np\nu\ndone\n"
        },
    };

    [Theory]
    [MemberData(nameof(ArrayPrograms))]
    public void ArrayProgramsRun(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture(name, source, stdout);

    // SPEC 4.9.3 (DECISIONS R5-M121): each operation calls the helper of its LegacyRow item with the handle, its operands at their slots (an
    // element by value, a string by its storage, a zero-sized one not at all), its result slot and the call's location; append's helper grows
    // by the element's stride.
    [Fact]
    public void ArrayOperationsCallTheHelperOfTheirInstance()
    {
        var c = Analyze(Arrays, OutputKind.Library);
        var text = Generate(c);
        var ir = GeneratedIr.Parse(text);
        var grow = ir.Function("__kimi_array_grow");
        foreach (var (name, site, signature, shape, stride) in new[]
        {
            ("arPut", "values.append(value)", "internal void (ptr, i32, ptr, i64)", "call void @(ptr %, i32 %, ptr @, i64 {0})", 4),
            ("arPutText", "values.append(value@move)", "internal void (ptr, ptr, ptr, i64)", "call void @(ptr %, ptr %, ptr @, i64 {0})", 24),
            ("arPutUnit", "values.append(())", "internal void (ptr, ptr, i64)", "call void @(ptr %, ptr @, i64 {0})", 0),
        })
        {
            var call = Assert.Single(CallsOf(Assert.Single(FunctionsOf(ir, c, name))));
            Assert.Equal(signature, call.Callee!.Signature);
            Assert.Equal(string.Format(System.Globalization.CultureInfo.InvariantCulture, shape, LocationText(Arrays, site).Length), call.Shape);
            Assert.Contains(call.Callee.Instructions, x => x.Callee == grow && x.Shape == $"call void @(ptr %, i64 {stride}, i64 %, ptr %, i64 %)");
        }

        var text0 = Assert.Single(CallsOf(Assert.Single(FunctionsOf(ir, c, "arPutText"))));
        Assert.Equal("alloca", text0.Operands[1].Opcode); // The string's own storage, which the call consumes.
        var take = Assert.Single(FunctionsOf(ir, c, "arTake"));
        var pop = Assert.Single(CallsOf(take));
        Assert.Equal("internal void (ptr, ptr)", pop.Callee!.Signature);
        Assert.Same(take.Parameters[0], pop.Operands[1]); // Its result slot is the caller's.
        Assert.Contains(pop.Callee.Instructions, static x => x.Shape == "store i32 1, ptr %, align 4"); // None's tag.

        var removes = FunctionsOf(ir, c, "remove").SelectMany(CallsOf).Select(static x => x.Callee!.Signature).Where(static x => x.Contains("(ptr, i64", StringComparison.Ordinal));
        Assert.Equal(["internal i32 (ptr, i64, ptr, i64)", "internal void (ptr, i64, ptr, i64)", "internal void (ptr, i64, ptr, ptr, i64)"], removes.Distinct().Order(StringComparer.Ordinal));
        Assert.Contains(FunctionsOf(ir, c, "insert").SelectMany(CallsOf), static x => x.Callee!.Signature == "internal void (ptr, i64, i32, ptr, i64)");
        Assert.Contains(FunctionsOf(ir, c, "swap").SelectMany(CallsOf), static x => x.Callee!.Signature == "internal void (ptr, i64, i64, ptr, i64)");

        var reserve = Assert.Single(CallsOf(Assert.Single(FunctionsOf(ir, c, "arRoom"))));
        Assert.Same(ir.Function("__kimi_array_reserve"), reserve.Callee);
        Assert.Equal($"call void @(ptr %, i64 16, i64 8, ptr @, i64 {LocationText(Arrays, "values.reserve(8)").Length})", reserve.Shape);
        var shrink = Assert.Single(CallsOf(Assert.Single(FunctionsOf(ir, c, "arFit"))));
        Assert.Same(ir.Function("__kimi_array_shrink"), shrink.Callee);
        Assert.Equal($"call void @(ptr %, i64 16, ptr @, i64 {LocationText(Arrays, "values.shrinkToFit()").Length})", shrink.Shape);
    }

    // SPEC 4.9.7, 16.3.2 (DECISIONS R5-M121): clear's helper destroys each element by the element's glue, from the last, and keeps the buffer;
    // over i32 it destroys nothing. An Array's glue calls its own Drop helper with its parameters, which also releases the buffer.
    [Fact]
    public void ArrayDestructionRunsTheElementsGlueThroughItsHelper()
    {
        var c = Analyze(Arrays, OutputKind.Library);
        var ir = GeneratedIr.Parse(Generate(c));
        var free = ir.Function("__kimi_array_free");
        var clear = Assert.Single(CallsOf(Assert.Single(FunctionsOf(ir, c, "arEmpty")))).Callee!;
        var element = Assert.Single(CalleesOf(clear));
        Assert.Equal("internal void (ptr, ptr, i64)", element.Signature);
        Assert.Contains(CalleesOf(element), static x => x.Signature == "internal void (ptr)"); // Res's destructor instance.
        Assert.Empty(CalleesOf(Assert.Single(CallsOf(Assert.Single(FunctionsOf(ir, c, "arEmptyInts")))).Callee!));

        var keep = Assert.Single(FunctionsOf(ir, c, "arKeep"));
        var glue = Assert.Single(CalleesOf(keep), static x => x.Signature == "internal void (ptr, ptr, i64)");
        var call = Assert.Single(CallsOf(glue));
        Assert.Equal([glue.Parameters[0], glue.Parameters[1], glue.Parameters[2]], call.Operands);
        Assert.Equal([element, free], CalleesOf(call.Callee!));
    }

    // SPEC 4.3, 4.9.5, 22.2 (DECISIONS R5-M121): an Array from none is the runtime's empty handle at its Place; capacity loads the handle's
    // capacity word; setArrayLength stores the length in its length word; the storage boundary's helpers write the remainder's Fields.
    [Fact]
    public void TheStorageBoundaryAndTheInPlaceRowsWriteTheHandlesWords()
    {
        var c = Analyze(Arrays, OutputKind.Library);
        var ir = GeneratedIr.Parse(Generate(c));
        var none = Assert.Single(FunctionsOf(ir, c, "arNone"));
        var init = Assert.Single(CallsOf(none));
        Assert.Same(ir.Function("__kimi_array_init"), init.Callee);
        Assert.Same(none.Parameters[0], init.Operands[0]);
        var size = Assert.Single(FunctionsOf(ir, c, "arSize"));
        Assert.Contains(size.Instructions, static x => x.Shape == "getelementptr i8, ptr %, i64 16");
        Assert.Contains(size.Instructions, static x => x.Shape == "load i64, ptr %, align 8" && x.Operands[0].Shape == "getelementptr i8, ptr %, i64 16");

        var copies = FunctionsOf(ir, c, "initRepeating").Single();
        Assert.Contains(copies.Instructions, static x => x.Shape == "store i64 %, ptr %, align 8" && x.Operands[^1].Shape == "getelementptr i8, ptr %, i64 8");

        var borrows = FunctionsOf(ir, c, "iterate").SelectMany(CallsOf).Where(static x => x.Callee!.Signature == "internal void (ptr, ptr)").ToArray();
        Assert.Contains(borrows, static x => x.Callee!.Instructions.Where(static y => y.Opcode == "store").Select(static y => y.Shape)
            .SequenceEqual(["store ptr %, ptr %, align 8", "store i64 0, ptr %, align 8", "store i64 %, ptr %, align 8"]));
        var owns = FunctionsOf(ir, c, "intoIterator").SelectMany(CallsOf).Where(static x => x.Callee!.Signature == "internal void (ptr, ptr)");
        Assert.Contains(owns, static x => x.Callee!.Instructions.Count(static y => y.Opcode == "store") == 4); // Storage, position, count, capacity.
        Assert.Contains(FunctionsOf(ir, c, "intoIterator").SelectMany(CallsOf), static x => x.Shape == "call void @(ptr %, i64 3, ptr %)");
    }

    // SPEC 4.3.1, 4.9.5, 12.4.1 (DECISIONS R5-M129): an Array literal is the runtime's empty handle at its Place, one growth to its count
    // given the element's stride, its length, and each element put in the buffer at its position, with no helper call; `[]` is the empty
    // handle alone, and a zero-sized element is put nowhere (fail-before: a reserve call and one append helper call per element).
    [Fact]
    public void AnArrayLiteralGrowsOnceAndPutsItsElements()
    {
        const string Literals = "func arLit(x: i32) -> Array<i32> => [x, 2, 3]\nfunc arLitUnits() -> Array<()> => [(), ()]\n" +
            "func arLitNone() -> Array<i32> => []\nfunc arLitTexts(s: string) -> Array<string> => [s@move, \"t\"]\n";
        var c = Analyze(Literals, OutputKind.Library);
        var text = Generate(c);
        var ir = GeneratedIr.Parse(text);
        var (init, grow) = (ir.Function("__kimi_array_init"), ir.Function("__kimi_array_grow"));
        foreach (var (name, site, stride, count, stores) in new[]
        {
            ("arLit", "[x, 2, 3]", 4, 3,
                new[] { "store i64 3, ptr %, align 8", "store i32 %, ptr %, align 4", "store i32 2, ptr %, align 4", "store i32 3, ptr %, align 4" }),
            ("arLitUnits", "[(), ()]", 0, 2, ["store i64 2, ptr %, align 8"]),
        })
        {
            var function = Assert.Single(FunctionsOf(ir, c, name));
            var calls = CallsOf(function);
            Assert.Equal([init, grow], calls.Select(static x => x.Callee));
            Assert.Equal($"call void @(ptr %, i64 {stride}, i64 {count}, ptr @, i64 {LocationText(Literals, site).Length})", calls[1].Shape);
            Assert.Equal(stores, function.Instructions.SkipWhile(x => x != calls[1]).Where(static x => x.Opcode == "store").Select(static x => x.Shape));
        }

        Assert.Equal([init], CallsOf(Assert.Single(FunctionsOf(ir, c, "arLitNone"))).Select(static x => x.Callee));
        var texts = CallsOf(Assert.Single(FunctionsOf(ir, c, "arLitTexts")));
        Assert.Equal([init, grow], texts.Where(x => x.Callee == init || x.Callee == grow).Select(static x => x.Callee));
        Assert.Equal(
            $"call void @(ptr %, i64 24, i64 2, ptr @, i64 {LocationText(Literals, "[s@move, \"t\"]").Length})", texts.Single(x => x.Callee == grow).Shape);
        Assert.DoesNotContain("@__kimi_array_append_", text, StringComparison.Ordinal);
    }

    // SPEC 7.7.4 (DECISIONS R5-M108, R5-M121): an Item's Value call and an erased Item's adapter entry call what its direct call calls, the
    // helper of its instance or the capacity routine given its stride; the entry forwards its own parameters.
    [Fact]
    public void AnArrayItemCallsItsDirectCallsRoutine()
    {
        var c = Analyze(Arrays, OutputKind.Library);
        var text = Generate(c);
        var ir = GeneratedIr.Parse(text);
        var item = Assert.Single(FunctionsOf(ir, c, "arItemTake"));
        Assert.Contains(CallsOf(item), static x => x.Callee!.Signature == "internal void (ptr, ptr)" && x.Callee.Instructions.Any(y => y.Shape == "store i32 1, ptr %, align 4"));
        var room = Assert.Single(CallsOf(Assert.Single(FunctionsOf(ir, c, "arItemRoom"))), x => x.Callee == ir.Function("__kimi_array_reserve"));
        Assert.Equal($"call void @(ptr %, i64 16, i64 4, ptr @, i64 {LocationText(Arrays, "f(values, 4)").Length})", room.Shape);

        var entries = ir.Functions.Where(x => x.Blocks.Count > 0 && CallsOf(x) is [{ Callee.Signature: "internal void (ptr, i32, ptr, i64)" }]).ToArray();
        var entry = Assert.Single(entries, x => x.Parameters.Count > 4);
        var forward = Assert.Single(CallsOf(entry));
        Assert.All(forward.Operands, x => Assert.Contains(x, entry.Parameters));
        Assert.Single(ir.Functions, x => x.Blocks.Count > 0 && x.Parameters.Count > 4 && CallsOf(x) is [{ Callee.Signature: "internal void (ptr, ptr, ptr, i64)" }]);
    }

    // SPEC 3.5, 4.9.3 (DECISIONS R5-M89, R5-M121): an element in memory a helper takes by pointer and copies after the buffer grows is
    // copied into a slot of the entry block first, so `values.append(values[0])` never reads the released buffer.
    [Fact]
    public void ACopiedElementInMemoryIsSlottedBeforeTheHelperGrows()
    {
        var c = Analyze(Arrays, OutputKind.Library);
        var ir = GeneratedIr.Parse(Generate(c));
        var function = Assert.Single(FunctionsOf(ir, c, "arPutFirst"));
        var append = CallsOf(function).Last(static x => x.Callee!.Signature == "internal void (ptr, ptr, ptr, i64)");
        var slot = append.Operands[1];
        Assert.Contains(slot, function.Blocks[0].Instructions);
        Assert.Equal("alloca { [0 x i64], [24 x i8] }, align 8", slot.Shape);
        var copy = Assert.Single(function.Instructions, x => x.Opcode == "call" && x.Callee == ir.Function("llvm.memcpy.p0.p0.i64") && x.Operands[0] == slot);
        Assert.True(function.Instructions.IndexOf(copy) < function.Instructions.IndexOf(append));
    }

    // PD8, R5-M46 (DECISIONS R5-M121): the helpers and the Array runtime precede every definition, each helper written once; the runtime is
    // written only for a row that declares it, so a module whose only Array call clears has none.
    [Fact]
    public void ArrayHelpersPrecedeTheDefinitionsWithTheRuntimeTheirRowsDeclare()
    {
        var text = Generate(Analyze(Arrays, OutputKind.Library));
        var first = Regex.Match(text, @"define internal \S+ @__kimi_f\d+\(").Index;
        var helpers = Regex.Matches(text, @"define internal \S+ @(__kimi_array_[a-z]+_\d+|__kimi_fixed_own_\d+)\(").ToArray();
        Assert.NotEmpty(helpers);
        Assert.All(helpers, x => Assert.True(x.Index < first, x.Value));
        Assert.Equal(helpers.Length, helpers.Select(static x => x.Groups[1].Value).Distinct().Count());
        var grow = Regex.Match(text, @"define internal void @__kimi_array_grow\(");
        Assert.True(grow.Success && grow.Index < first);

        var cleared = Generate(Analyze("func arEmptyInts(values: uniq/Array<i32>) => values.clear()\n", OutputKind.Library));
        Assert.Matches(@"define internal void @__kimi_array_clear_\d+\(", cleared);
        Assert.DoesNotContain("@__kimi_array_grow", cleared, StringComparison.Ordinal);
    }

    // R5-M46 (DECISIONS R5-M121): borrow and own declare no Array runtime, since they only load and store the handle's words, so a module whose
    // only Array calls are an iteration's borrowStorage or ownStorage defines their helper and none of the capacity routines.
    [Theory]
    [InlineData("func arTotal(values: ref/Array<i32>) -> i32\n    var sum: i32 = 0\n    for v in values => sum += v\n    return sum\n", "borrow")]
    [InlineData("func arDrain(values: Array<i32>) -> i32\n    var sum: i32 = 0\n    for v in values@move => sum += v\n    return sum\n", "own")]
    public void BorrowAndOwnWriteNoArrayRuntime(string source, string helper)
    {
        var text = Generate(Analyze(source, OutputKind.Library));
        Assert.Matches($@"define internal \S+ @__kimi_array_{helper}_\d+\(", text);
        Assert.DoesNotMatch(@"@__kimi_array_(grow|reserve|shrink)\b", text);
    }

    // SPEC 22.2, IMPL 21.1.1 (DECISIONS R5-M121): a consumed fixed array whose storage has no bytes, of a zero-sized element or of no element,
    // moves in by its length alone: its helper takes no value, so the call passes the length and the result slot.
    [Theory]
    [InlineData("[2 of Token]", 2)]
    [InlineData("[0 of Res]", 0)]
    public void AZeroSizedConsumedFixedArrayIsOwnedByItsLengthAlone(string array, int length)
    {
        var c = Analyze(
            "struct Token\n    public init() => ()\n    drop => Console.writeLine(\"token\")\n" +
            "struct Res\n    public let id: i64\n    public init(id: i64) => self.id = id\n    drop => Console.writeLine(\"res\")\n" +
            $"func drain(values: {array}) -> i64\n    var n: i64 = 0\n    for v in values@move => n += 1\n    return n\n",
            OutputKind.Library);
        var ir = GeneratedIr.Parse(Generate(c));
        var calls = FunctionsOf(ir, c, "intoIterator").SelectMany(CallsOf).Where(static x => x.Callee!.Signature == "internal void (i64, ptr)").ToArray();
        Assert.Equal($"call void @(i64 {length}, ptr %)", Assert.Single(calls.Select(static x => x.Shape).Distinct()));
    }

    // SPEC 7.7.4, 4.9.3 (DECISIONS R5-M121, ruling Q6): an erased append over an element in memory (a struct) or over a reference (ref/string)
    // is an entry that forwards its handle, its element and its location, as received, to the helper of its instance: the entry's input of a
    // reference is a SharedReference where the helper's is a Value of the same pointer Type.
    [Theory]
    [InlineData("Pair")]
    [InlineData("ref/string during static")]
    public void AnErasedAppendForwardsItsElementToTheHelper(string element)
    {
        var text = Generate(Analyze(
            "struct Pair\n    public let a: i64\n    public let b: i64\n    public init(a: i64, b: i64)\n        self.a = a\n        self.b = b\n" +
            $"func erased() -> (uniq/Array<{element}>, {element}) -> () => Array<{element}>.append\n",
            OutputKind.Library));
        var ir = GeneratedIr.Parse(text);
        var helper = ir.Function(Regex.Match(text, @"define internal \S+ @(__kimi_array_append_\d+)\(").Groups[1].Value);
        var entry = Assert.Single(ir.Functions, x => x.Blocks.Count > 0 && CallsOf(x) is [{ } only] && only.Callee == helper);
        var forward = Assert.Single(CallsOf(entry));
        Assert.Equal(4, forward.Operands.Count); // The handle, the element, and the caller's location and its length.
        Assert.Equal(entry.Parameters.Where(forward.Operands.Contains), forward.Operands);
    }

    // DECISIONS R5-M121 (ruling Q6): an adapter passes a callee's parameter by its own of the same logical index and passing: the same kind, or a
    // SharedReference and a Value of the same pointer Type; never a Value of another Type or an OwnedSlot (Own also keeps the index).
    [Fact]
    public void AnAdaptersParameterPassesWhatTheCalleeTakes()
    {
        var shared = new AbiParameter("ptr", "a", AbiParameterKind.SharedReference, 1);
        Assert.True(FunctionCodegen.Passes(shared, new("ptr", "b", AbiParameterKind.Value, 1)));
        Assert.True(FunctionCodegen.Passes(new("ptr", "a", AbiParameterKind.Value, 1), shared));
        Assert.True(FunctionCodegen.Passes(new("i64", "a", AbiParameterKind.Value, 1), new("i64", "b", AbiParameterKind.Value, 1)));
        Assert.False(FunctionCodegen.Passes(shared, new("i64", "b", AbiParameterKind.Value, 1)));
        Assert.False(FunctionCodegen.Passes(shared, new("ptr", "b", AbiParameterKind.OwnedSlot, 1)));
        Assert.False(FunctionCodegen.Passes(new("ptr", "a", AbiParameterKind.OwnedSlot, 1), new("ptr", "b", AbiParameterKind.Value, 1)));
    }
}
