// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.RegularExpressions;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Xunit;

namespace XunitTest;

// The Array family of MIR code generation (R5 C8d; SPEC 4.3, 4.5, 4.9.3, 4.9.5, 4.9.7, 7.7.4, 16.3.2, 22.2): each operation and storage
// function is a Kimigayo body (R8-A1a to A1d) called with the call's operands by slot, its result slot and location, at a direct call, an
// Item's Value call and an adapter's entry; an Array from none and an Array literal's handle are written in place; an Array's glue calls
// destroyArray's instance, which destroys the elements by their glue; natively.
public sealed partial class FunctionCodegenTest
{
    // A Library over Arrays of i32, a 16-byte struct, a drop-needing struct, a 24-byte Copy struct, string and Unit: the eight operations, the
    // storage boundary through the library's iteration, a length's write through a repeating construction, capacity, an Array from none, an Array
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

    // DECISIONS R8-M2, R8-M18 (PLAN G693): the steps the Array invariant or a dominating guard bounds check no arithmetic. `insertAt` keeps one
    // check, its documented length sum, as `reserve` and `append` keep theirs; `appendCopies` keeps one, the step of the range it iterates.
    [Fact]
    public void ArrayBodiesCheckNoBoundedArithmetic()
    {
        const string Source =
            "func abSwap(values: uniq/Array<i32>) -> i32 => values.swapRemove(0)\n" +
            "func abCut(values: uniq/Array<i32>) => values.truncate(1)\n" +
            "func abFill() -> Array<i32> => Array<i32>.init(repeating: 1, count: 3)\n" +
            "func abCopy(values: uniq/Array<i32>, more: Slice<i32>) => values.appendCopies(more)\n" +
            "func abTake(values: uniq/Array<i32>) -> Option<i32> => values.pop()\n" +
            "func abWedge(values: uniq/Array<i32>) => values.insert(0, 7)\n" +
            "func abRemove(values: uniq/Array<i32>) -> i32 => values.remove(0)\n" +
            "func abLarge(value: ref/i32) -> bool => value > 25\n" +
            "func abFilter(values: uniq/Array<i32>) => values.removeAll(matching: abLarge)\n" +
            "func abTurn(values: uniq/Array<i32>) => values.reverse()\n" +
            "func abSort(values: uniq/Array<i32>) => values.sort()\n" +
            "func abDrain(values: Array<i32>) -> i32\n    var sum: i32 = 0\n    for v in values@move => sum += v\n    return sum\n";
        var c = Analyze(Source, OutputKind.Library);
        AssertNoCheckedArithmetic(c, "swapRemove", "truncate", "initRepeating", "pop", "removeAt", "removeAll", "reverse", "sort", "next");
        foreach (var name in new[] { "insertAt", "appendCopies" })
        {
            Assert.Equal(1, Regex.Count(Assert.Single(ItemTexts(c, name)), @"call .*with\.overflow"));
        }
    }

    // SPEC 4.9.3: each operation calls its body (R8-A1a, A1b) with the handle, its operands at their slots (an element by value, a string by its storage, a zero-sized one not at all), its result slot and the call's
    // location; `append`'s body grows through `growArray` by the element's stride.
    [Fact]
    public void ArrayOperationsCallTheBodyOfTheirInstance()
    {
        var c = Analyze(Arrays, OutputKind.Library);
        var text = Generate(c);
        var ir = GeneratedIr.Parse(text);
        var grow = Assert.Single(FunctionsOf(ir, c, "growArray"));
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
            Assert.Contains(FunctionsOf(ir, c, "append"), x => x == call.Callee);
            Assert.Contains(call.Callee.Instructions, x => x.Callee == grow && x.Shape == "call void @(ptr %, i64 %, i64 %, ptr %, i64 %)");
            Assert.Contains(call.Callee.Instructions, x => x.Shape == $"store i64 {stride}, ptr %, align 8");
        }

        var text0 = Assert.Single(CallsOf(Assert.Single(FunctionsOf(ir, c, "arPutText"))));
        Assert.Equal("alloca", text0.Operands[1].Opcode); // The string's own storage, which the call consumes.
        var take = Assert.Single(FunctionsOf(ir, c, "arTake"));
        var pop = Assert.Single(CallsOf(take));
        Assert.Equal("internal void (ptr, ptr)", pop.Callee!.Signature);
        Assert.Same(take.Parameters[0], pop.Operands[0]); // Its result slot is the caller's, first in a body's signature.
        Assert.Contains(pop.Callee.Instructions, static x => x.Shape == "store i32 1, ptr %, align 4"); // None's tag.

        var removes = FunctionsOf(ir, c, "remove").SelectMany(CallsOf).Select(static x => x.Callee!.Signature).Where(static x => x.EndsWith("ptr, i64, ptr, i64)", StringComparison.Ordinal));
        Assert.Equal(["internal i32 (ptr, i64, ptr, i64)", "internal void (ptr, i64, ptr, i64)", "internal void (ptr, ptr, i64, ptr, i64)"], removes.Distinct().Order(StringComparer.Ordinal));
        Assert.Contains(FunctionsOf(ir, c, "insert").SelectMany(CallsOf), static x => x.Callee!.Signature == "internal void (ptr, i64, i32, ptr, i64)");
        Assert.Contains(FunctionsOf(ir, c, "swap").SelectMany(CallsOf), static x => x.Callee!.Signature == "internal void (ptr, i64, i64, ptr, i64)");

        var reserve = Assert.Single(CallsOf(Assert.Single(FunctionsOf(ir, c, "arRoom"))));
        Assert.Contains(FunctionsOf(ir, c, "reserve"), x => x == reserve.Callee);
        Assert.Equal($"call void @(ptr %, i64 8, ptr @, i64 {LocationText(Arrays, "values.reserve(8)").Length})", reserve.Shape);
        Assert.Contains(reserve.Callee!.Instructions, static x => x.Shape == "store i64 16, ptr %, align 8"); // The stride it gives growArray.
        var shrink = Assert.Single(CallsOf(Assert.Single(FunctionsOf(ir, c, "arFit"))));
        Assert.Contains(FunctionsOf(ir, c, "shrinkToFit"), x => x == shrink.Callee);
        Assert.Equal($"call void @(ptr %, ptr @, i64 {LocationText(Arrays, "values.shrinkToFit()").Length})", shrink.Shape);
        Assert.Contains(FunctionsOf(ir, c, "shrinkArray"), x => shrink.Callee!.Instructions.Any(y => y.Callee == x && y.Shape == "call void @(ptr %, i64 %, ptr %, i64 %)"));
    }

    // SPEC 4.9.7, 16.3.2 (PLAN G126): clear's body destroys each element by the element's glue in one counted loop, and keeps the buffer; over
    // i32 it destroys nothing. An Array's glue calls its one destroyArray instance with its parameters, which also releases the buffer.
    [Fact]
    public void ArrayDestructionRunsTheElementsGlueThroughItsBodies()
    {
        var c = Analyze(Arrays, OutputKind.Library);
        var ir = GeneratedIr.Parse(Generate(c));
        var free = ir.Function("__kimi_free");
        var clear = Assert.Single(CallsOf(Assert.Single(FunctionsOf(ir, c, "arEmpty")))).Callee!;
        var element = Assert.Single(CalleesOf(clear), static x => x.Signature == "internal void (ptr, ptr, i64)");
        Assert.Contains(CalleesOf(element), static x => x.Signature == "internal void (ptr)"); // Res's destructor instance.
        Assert.DoesNotContain(CalleesOf(Assert.Single(CallsOf(Assert.Single(FunctionsOf(ir, c, "arEmptyInts")))).Callee!), static x => x.Signature == "internal void (ptr, ptr, i64)");

        var keep = Assert.Single(FunctionsOf(ir, c, "arKeep"));
        var glue = Assert.Single(CalleesOf(keep), static x => x.Signature == "internal void (ptr, ptr, i64)");
        var call = Assert.Single(CallsOf(glue));
        Assert.Equal([glue.Parameters[0], glue.Parameters[1], glue.Parameters[2]], call.Operands);
        Assert.Contains(FunctionsOf(ir, c, "destroyArray"), x => x == call.Callee);
        Assert.Equal([element, free], CalleesOf(call.Callee!));
    }

    // SPEC 4.9.7, 4.9.8 (R8-A1c): truncate destroys the tail by destroyValues in its own body, as clear does; it does not walk it through pop.
    [Fact]
    public void TruncateDestroysThroughItsOwnBody()
    {
        var c = Analyze("func arTrim(values: uniq/Array<i32>) => values.truncate(1)\n", OutputKind.Library);
        var ir = GeneratedIr.Parse(Generate(c));
        Assert.Single(FunctionsOf(ir, c, "truncate"));
        Assert.Empty(FunctionsOf(ir, c, "pop"));
    }

    // SPEC 4.3, 4.9.5, 22.2: an Array from none zeroes the handle's three words at its Place, with no call; capacity's body loads the handle's
    // capacity word; a repeating construction's body stores the length in its length word; the storage boundary's bodies read the handle's
    // length word (R8-A1d).
    [Fact]
    public void TheStorageBoundaryAndTheInPlaceRowsWriteTheHandlesWords()
    {
        var c = Analyze(Arrays, OutputKind.Library);
        var ir = GeneratedIr.Parse(Generate(c));
        var none = Assert.Single(FunctionsOf(ir, c, "arNone"));
        Assert.Empty(CallsOf(none));
        Assert.Single(none.Instructions, static x => x.Shape == "store ptr null, ptr %, align 8"); // The data word, a pointer (ArrayBuffer's reason).
        Assert.Equal(2, none.Instructions.Count(static x => x.Shape == "store i64 0, ptr %, align 8"));
        var size = Assert.Single(CallsOf(Assert.Single(FunctionsOf(ir, c, "arSize")))).Callee!;
        Assert.Contains(FunctionsOf(ir, c, "arrayCapacity"), x => x == size);
        Assert.Contains(size.Instructions, static x => x.Shape == "load i64, ptr %, align 8" && x.Operands[0].Shape == "getelementptr i8, ptr %, i64 16");

        var published = FunctionsOf(ir, c, "initRepeating").SelectMany(static x => x.Instructions);
        Assert.Contains(published, static x => x.Shape == "store i64 %, ptr %, align 8" && x.Operands[^1].Shape == "getelementptr i8, ptr %, i64 8");

        var borrows = FunctionsOf(ir, c, "borrowStorage").SelectMany(static x => x.Instructions);
        Assert.Contains(borrows, static x => x.Shape == "load i64, ptr %, align 8" && x.Operands[0].Shape == "getelementptr i8, ptr %, i64 8");
        Assert.Contains(FunctionsOf(ir, c, "intoIterator").SelectMany(static x => x.Instructions), static x => x.Shape == "store i64 3, ptr %, align 8");
    }

    // SPEC 4.3.1, 4.9.5, 12.4.1 (DECISIONS R5-M129): an Array literal is the empty handle at its Place, one call of growArray, the aggregate's
    // target, to its count given the element's stride, its length, and each element put in the buffer at its position, with no helper call;
    // `[]` is the empty handle alone, and a zero-sized element is put nowhere (fail-before: a reserve call and one append helper call per element).
    [Fact]
    public void AnArrayLiteralGrowsOnceAndPutsItsElements()
    {
        const string Literals = "func arLit(x: i32) -> Array<i32> => [x, 2, 3]\nfunc arLitUnits() -> Array<()> => [(), ()]\n" +
            "func arLitNone() -> Array<i32> => []\nfunc arLitTexts(s: string) -> Array<string> => [s@move, \"t\"]\n";
        var c = Analyze(Literals, OutputKind.Library);
        var text = Generate(c);
        var ir = GeneratedIr.Parse(text);
        var grow = Assert.Single(FunctionsOf(ir, c, "growArray"));
        foreach (var (name, site, stride, count, stores) in new[]
        {
            ("arLit", "[x, 2, 3]", 4, 3,
                new[] { "store i64 3, ptr %, align 8", "store i32 %, ptr %, align 4", "store i32 2, ptr %, align 4", "store i32 3, ptr %, align 4" }),
            ("arLitUnits", "[(), ()]", 0, 2, ["store i64 2, ptr %, align 8"]),
        })
        {
            var function = Assert.Single(FunctionsOf(ir, c, name));
            var calls = CallsOf(function);
            Assert.Equal([grow], calls.Select(static x => x.Callee));
            Assert.Equal($"call void @(ptr %, i64 {stride}, i64 {count}, ptr @, i64 {LocationText(Literals, site).Length})", calls[0].Shape);
            Assert.Equal(stores, function.Instructions.SkipWhile(x => x != calls[0]).Where(static x => x.Opcode == "store").Select(static x => x.Shape));
        }

        Assert.Empty(CallsOf(Assert.Single(FunctionsOf(ir, c, "arLitNone"))));
        var texts = CallsOf(Assert.Single(FunctionsOf(ir, c, "arLitTexts")));
        Assert.Equal([grow], texts.Where(x => x.Callee == grow).Select(static x => x.Callee));
        Assert.Equal(
            $"call void @(ptr %, i64 24, i64 2, ptr @, i64 {LocationText(Literals, "[s@move, \"t\"]").Length})", texts.Single(x => x.Callee == grow).Shape);
        Assert.Empty(FunctionsOf(ir, c, "append")); // A literal places its elements through one growth and never appends.
    }

    // SPEC 7.7.4 (DECISIONS R5-M108, R5-M121): an Item's Value call and an erased Item's adapter entry call what its direct call calls, the
    // helper or body of its instance or the capacity routine given its stride; the entry forwards its own parameters.
    [Fact]
    public void AnArrayItemCallsItsDirectCallsRoutine()
    {
        var c = Analyze(Arrays, OutputKind.Library);
        var text = Generate(c);
        var ir = GeneratedIr.Parse(text);
        var item = Assert.Single(FunctionsOf(ir, c, "arItemTake"));
        Assert.Contains(CallsOf(item), static x => x.Callee!.Signature == "internal void (ptr, ptr)" && x.Callee.Instructions.Any(y => y.Shape == "store i32 1, ptr %, align 4"));
        var reserves = FunctionsOf(ir, c, "reserve");
        var room = Assert.Single(CallsOf(Assert.Single(FunctionsOf(ir, c, "arItemRoom"))), x => reserves.Contains(x.Callee));
        Assert.Equal($"call void @(ptr %, i64 4, ptr @, i64 {LocationText(Arrays, "f(values, 4)").Length})", room.Shape);

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

    // R8-A1d (DECISIONS R5-M121's rows are gone): no Array operation has a helper, and growth is a body, so a module whose only Array call clears
    // holds no growth routine.
    [Fact]
    public void ArraysHoldNoHelperAndOnlyGrowthHoldsItsRoutine()
    {
        Assert.DoesNotMatch(@"define internal \S+ @(__kimi_array_[a-z]+_\d+|__kimi_fixed_own_\d+)\(", Generate(Analyze(Arrays, OutputKind.Library)));
        var c = Analyze("func arEmptyInts(values: uniq/Array<i32>) => values.clear()\n", OutputKind.Library);
        Assert.Empty(FunctionsOf(GeneratedIr.Parse(Generate(c)), c, "growArray"));
    }

    // R5-M46 (R8-A1d): a borrow and an owning iteration only load and store the handle's words, so a module whose only Array calls are an
    // iteration's holds their bodies and no capacity routine.
    [Theory]
    [InlineData("func arTotal(values: ref/Array<i32>) -> i32\n    var sum: i32 = 0\n    for v in values => sum += v\n    return sum\n", "borrowStorage")]
    [InlineData("func arDrain(values: Array<i32>) -> i32\n    var sum: i32 = 0\n    for v in values@move => sum += v\n    return sum\n", "intoIterator")]
    public void BorrowAndOwnWriteNoArrayRuntime(string source, string body)
    {
        var c = Analyze(source, OutputKind.Library);
        var ir = GeneratedIr.Parse(Generate(c));
        Assert.NotEmpty(FunctionsOf(ir, c, body));
        Assert.Empty(FunctionsOf(ir, c, "growArray"));
        Assert.Empty(FunctionsOf(ir, c, "shrinkArray"));
    }

    // SPEC 22.2, IMPL 21.1.1 (R8-A1d): a consumed fixed array whose storage has no bytes, of a zero-sized element or of no element, moves in
    // by its length alone: its remainder's init takes no value, so the call passes the result slot and the length.
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
        var bodies = FunctionsOf(ir, c, "intoIterator").ToArray();
        Assert.Contains(bodies.SelectMany(CallsOf), static x => x.Callee!.Signature == "internal void (ptr, i64)");
        Assert.Contains(bodies.SelectMany(static x => x.Instructions), x => x.Shape == $"store i64 {length}, ptr %, align 8");
    }

    // SPEC 7.7.4, 4.9.3 (DECISIONS R5-M121, ruling Q6): an erased append over an element in memory (a struct) or over a reference (ref/string)
    // is an entry that forwards its handle, its element and its location, as received, to the body of its instance: the entry's input of a
    // reference is a SharedReference where the body's is a Value of the same pointer Type.
    [Theory]
    [InlineData("Pair")]
    [InlineData("ref/string during static")]
    public void AnErasedAppendForwardsItsElementToItsBody(string element)
    {
        var c = Analyze(
            "struct Pair\n    public let a: i64\n    public let b: i64\n    public init(a: i64, b: i64)\n        self.a = a\n        self.b = b\n" +
            $"func erased() -> (uniq/Array<{element}>, {element}) -> () => Array<{element}>.append\n",
            OutputKind.Library);
        var ir = GeneratedIr.Parse(Generate(c));
        var body = Assert.Single(FunctionsOf(ir, c, "append"));
        var entry = Assert.Single(ir.Functions, x => x.Blocks.Count > 0 && CallsOf(x) is [{ } only] && only.Callee == body);
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
