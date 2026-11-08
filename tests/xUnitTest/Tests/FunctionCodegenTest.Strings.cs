// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Xunit;

namespace XunitTest;

// The string comparisons and string constants of MIR code generation (R5 C8c; SPEC 13.4, 13.4.1, 14.8.1, 3.4.3, IMPL 21.1.3; DECISIONS
// R5-M118): a comparison is its library witness's call on its operands' addresses, a compare's i32 compared with 0; a comparison's literal
// passes its constant record's address and any other constant is written whole where it is stored; natively.
public sealed partial class FunctionCodegenTest
{
    // A shared borrow of a string, as a call passes it (IMPL 21.3.1).
    private const string StringView = "ptr noundef nonnull align 8 dereferenceable(24)";

    // A Library comparing strings by every operator, against literals and the empty string, through a uniq layer, by literal Patterns and as
    // a tuple's leaf, and storing string constants into a Tuple, a fixed array, an enum Case and raw storage.
    private const string Comparisons =
        "func equal(a: string, b: string) -> bool => a == b\nfunc differ(a: string, b: string) -> bool => a != b\n" +
        "func less(a: string, b: string) -> bool => a < b\nfunc most(a: string, b: string) -> bool => a <= b\n" +
        "func more(a: string, b: string) -> bool => a > b\nfunc least(a: string, b: string) -> bool => a >= b\n" +
        "func literal(a: string) -> bool => a == \"lit\"\nfunc empty(a: string) -> bool => a == \"\"\n" +
        "func constants() -> bool => \"a\" < \"b\"\nfunc layered(a: uniq/string) -> bool => a == \"x\"\n" +
        "func pick(a: string) -> i32\n    match a\n        \"x\" => return 1\n        \"\" => return 2\n        _ => return 3\n" +
        "func ordered(a: string, b: string) -> bool\n    let x = (a@move, 1)\n    let y = (b@move, 2)\n    return x < y\n" +
        "func pair() -> (string, i32) => (\"p\", 1)\nfunc words() -> [2 of string] => [\"a\", \"\"]\n" +
        "func some() -> Option<string> => .Some(\"s\")\nfunc put(p: raw/string)\n    unsafe => Raw.initialize(p, \"r\")\n";

    // SPEC 13.4, 14.8.1: operands and subjects of a qualifying pair layer, in each case, whose comparison below the layer is the string's, and a
    // Requirement call at string through one.
    private const string PairLayered =
        "func same<s/T>(value: s/string) -> bool\n    s is ref or uniq\n    return value == \"x\"\n" +
        "func less<s/T>(value: s/string) -> bool\n    s is ref or uniq\n    return value < \"x\"\n" +
        "func pick<s/T>(value: s/string) -> i32\n    s is ref or uniq\n    match value\n" +
        "        \"x\" => return 1\n        \"\" => return 3\n        _ => return 2\n" +
        "func generic<s/T>(value: s/T, other: ref/T) -> bool\n    s is ref or uniq\n    T is Comparable\n    return value == other or value < other\n" +
        "var y = \"x\"\nvar z = \"w\"\nlet a = same<ref/i32>(y@ref)\nlet b = same<uniq/i32>(y@uniq)\nlet c = same<ref/i32>(z@ref)\n" +
        "let d = less<uniq/i32>(z@uniq)\nlet e = pick<ref/i32>(y@ref)\nlet f = pick<uniq/i32>(z@uniq)\nlet g = generic(y@ref, z@ref)\n" +
        "let h = generic(z@uniq, y@ref)\nConsole.writeLine(\"\\(a) \\(b) \\(c) \\(d) \\(e) \\(f) \\(g) \\(h)\")\nConsole.writeLine(y)\n";

    // SPEC 13.4 (DECISIONS R5-M118): string comparisons natively: every operator, literals and the empty string,
    // NUL bytes and UTF-8 order, a uniq layer, literal Patterns, tuple orderings and constants stored in aggregates.
    public static TheoryData<string, string, string> StringPrograms => new()
    {
        {
            "StringRows",
            "func less(a: ref/string, b: ref/string) -> bool => a < b\n" +
            "func classify(text: ref/string) -> i32\n    match text\n        \"alpha\" => return 1\n        \"\" => return 2\n        _ => return 3\n" +
            "func layered(u: uniq/string) -> bool => u == \"beta\"\n" +
            "let words: [4 of string] = [\"alpha\", \"beta\", \"\", \"a\\0b\"]\nvar count = 0\n" +
            "let first = words[0] == \"alpha\" and words[1] != \"alpha\"\n" +
            "if first and less(words[0]@ref, words[1]@ref) and not less(words[1]@ref, words[0]@ref) => count += 1\n" +
            "let bytes = \"\" < \"a\" and \"a\" < \"ab\" and \"ab\" < \"b\" and \"a\\0b\" > \"a\"\n" +
            "if bytes and \"z\" < \"日本\" and \"b\" >= \"b\" and \"a\" <= \"b\" => count += 1\n" +
            "if classify(words[0]@ref) == 1 and classify(words[2]@ref) == 2 and classify(words[3]@ref) == 3 => count += 1\n" +
            "var b = \"beta\"\nvar g = \"gamma\"\nif layered(b@uniq) and not layered(g@uniq) => count += 1\n" +
            "let x = (\"same\", 1)\nlet y = (\"same\", 2)\nlet z = (\"other\", 3)\nif x < y and x != y and z < x and not (y <= z) => count += 1\n" +
            "let pair = (\"first\", \"second\")\nif pair.0 < pair.1 and pair.1 >= pair.0 and pair.0 != \"\" => count += 1\n" +
            "require count == 6 else => $abort(\"count\")\nConsole.writeLine(words[1])\nConsole.writeLine(pair.1)\n",
            "beta\nsecond\n"
        },
        { "StringPairLayers", PairLayered, "true true false true 1 2 false true\nx\n" },
    };

    [Theory]
    [MemberData(nameof(StringPrograms))]
    public void StringProgramsRun(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture(name, source, stdout);

    // SPEC 13.4, 13.4.1 (DECISIONS R5-M118): each operator calls its witness, equals for == and != and compare for the others, on its
    // operands' addresses, then negates the bool or compares the i32 with 0 by its predicate; the bool is stored widened. Each witness reads
    // its records' Fields 0 and 1 through those addresses and calls its byte comparison on them, once; no routine reaches the module.
    [Fact]
    public void StringComparisonsCallTheirWitnessOnTheOperands()
    {
        var c = Analyze(Comparisons, OutputKind.Library);
        var text = Generate(c);
        var ir = GeneratedIr.Parse(text);
        var (equal, order) = StringWitnesses(ir, c);
        foreach (var (name, witness, relation) in new[]
        {
            ("equal", equal, null), ("differ", equal, "xor i1 %, -1"), ("less", order, "icmp slt i32 %, 0"), ("most", order, "icmp sle i32 %, 0"),
            ("more", order, "icmp sgt i32 %, 0"), ("least", order, "icmp sge i32 %, 0"),
        })
        {
            var function = Assert.Single(FunctionsOf(ir, c, name), static x => !x.Signature.Contains("i64", StringComparison.Ordinal));
            var call = Assert.Single(StringCallsOf(ir, c, function));
            Assert.Same(witness, call.Callee);
            Assert.Equal($"call {(witness == equal ? "i1" : "i32")} @({StringView} %, {StringView} %)", call.Shape);
            var result = relation is null ? call : Assert.Single(function.Instructions, x => x.Shape == relation);
            Assert.Contains(function.Instructions, x => x.Shape == "zext i1 % to i8" && x.Operands[0] == result);
        }

        foreach (var (witness, shape) in new[] { (equal, "call i1 @(ptr %, i64 %, ptr %, i64 %)"), (order, "call i32 @(ptr %, i64 %, ptr %, i64 %)") })
        {
            var bytes = Assert.Single(CallsOf(witness));
            Assert.Equal(shape, bytes.Shape);
            for (var side = 0; side < 2; side++)
            {
                var (data, length) = (bytes.Operands[2 * side], bytes.Operands[(2 * side) + 1]);
                Assert.Equal("load ptr, ptr %, align 8", data.Shape);
                Assert.Equal("load i64, ptr %, align 8", length.Shape);
                Assert.Equal("getelementptr i8, ptr %, i64 8", length.Operands[0].Shape);
                Assert.Same(data.Operands[0].Operands[0], length.Operands[0].Operands[0].Operands[0]);
            }
        }

        Assert.DoesNotContain(LegacyRuntimeHelpers.StringComparisons, text, StringComparison.Ordinal);
    }

    // SPEC 13.4: an operand of a qualifying pair layer denotes its terminal Place, so its comparison is the witness's call too, the operator's
    // table reached by no case. Fail-first for R8-S1e (PLAN), whose one walk of those layers gives it the witness: the row is Explicit until
    // that unit lands and removes the marker.
    [Fact(Explicit = true)]
    public void APairLayeredOperandCallsTheWitness()
    {
        var c = Analyze(PairLayered);
        Assert.DoesNotContain(LegacyRuntimeHelpers.StringComparisons, Generate(c), StringComparison.Ordinal);
    }

    // SPEC 13.4, 3.1.2, IMPL 21.1.3: a comparison's literal passes its constant record's address, Static over its pooled bytes and their
    // count, the empty string's over null and 0, and no record is stored for it: no temporary, as the operator has none.
    [Fact]
    public void StringConstantOperandsPassTheirRecords()
    {
        var c = Analyze(Comparisons, OutputKind.Library);
        var text = Generate(c);
        var ir = GeneratedIr.Parse(text);
        foreach (var (name, shape) in new[]
        {
            ("literal", $"call i1 @({StringView} %, {StringView} @)"), ("empty", $"call i1 @({StringView} %, {StringView} @)"),
            ("constants", $"call i32 @({StringView} @, {StringView} @)"),
        })
        {
            var function = Assert.Single(FunctionsOf(ir, c, name));
            Assert.Equal(shape, Assert.Single(StringCallsOf(ir, c, function)).Shape);
            Assert.DoesNotContain(function.Instructions, static x => x.Shape.StartsWith("store %kimi.string", StringComparison.Ordinal));
        }

        Assert.Matches(@"(?m)^@__kimi_record[.\d]* = private unnamed_addr constant %kimi\.string \{ ptr @__kimi_text[.\d]*, i64 3, i8 0 \}, align 8$", text);
        Assert.Matches(@"(?m)^@__kimi_record[.\d]* = private unnamed_addr constant %kimi\.string \{ ptr null, i64 0, i8 0 \}, align 8$", text);
    }

    // IMPL 21.1.3: equal literals share one record, and an entry a later module reuses states that module's bytes: a record's name and its
    // bytes' constant's name repeat across modules, its length need not.
    [Fact]
    public void AConstantRecordStatesItsModulesBytes()
    {
        var pool = new LlvmConstantPool();
        Assert.Equal(pool.InternRecord("abc"), pool.InternRecord("abc"));
        Assert.Equal(2, pool.Count);
        Assert.Equal("@__kimi_record = private unnamed_addr constant %kimi.string { ptr @__kimi_text, i64 3, i8 0 }, align 8\n", pool[1].Definition);
        pool.Clear();
        Assert.Equal(
            "@__kimi_record = private unnamed_addr constant %kimi.string { ptr @__kimi_text, i64 4, i8 0 }, align 8\n",
            pool[pool.InternRecord("abcd")].Definition);
        pool.Clear();
        Assert.Equal(
            "@__kimi_record = private unnamed_addr constant %kimi.string { ptr null, i64 0, i8 0 }, align 8\n",
            pool[pool.InternRecord(string.Empty)].Definition);
    }

    // SPEC 13.4, 3.4.3 (DECISIONS R5-M118): an operand below a uniq layer reaches its Place through the pointer the layer stores: the
    // operand is the pointer read from the local holding the layer, its record's address, which the witness reads as any other.
    [Fact]
    public void ALayeredOperandLoadsItsReferences()
    {
        var c = Analyze(Comparisons, OutputKind.Library);
        var ir = GeneratedIr.Parse(Generate(c));
        var function = Assert.Single(FunctionsOf(ir, c, "layered"));
        var call = Assert.Single(StringCallsOf(ir, c, function));
        Assert.Equal($"call i1 @({StringView} %, {StringView} @)", call.Shape);
        var borrow = call.Operands[0];
        Assert.Equal("load ptr, ptr %, align 8", borrow.Shape); // The shared borrow's value ...
        var uniq = Assert.Single(function.Instructions, x => x.Opcode == "store" && x.Operands[^1] == borrow.Operands[0]).Operands[0];
        Assert.Equal("load ptr, ptr %, align 8", uniq.Shape); // ... is the pointer the layer stores.
    }

    // SPEC 14.8.1 (DECISIONS R5-M118): a literal Pattern's test is the comparison's call: equals on the selected Place and the literal's
    // record, in arm order, its bool tested.
    [Fact]
    public void LiteralPatternsCompareTheSubject()
    {
        var c = Analyze(Comparisons, OutputKind.Library);
        var ir = GeneratedIr.Parse(Generate(c));
        var function = Assert.Single(FunctionsOf(ir, c, "pick"));
        var calls = StringCallsOf(ir, c, function);
        Assert.Equal(Enumerable.Repeat($"call i1 @({StringView} %, {StringView} @)", 2), calls.Select(static x => x.Shape));
        Assert.All(calls, x => Assert.Same(StringWitnesses(ir, c).Equal, x.Callee));
        Assert.All(calls, x => Assert.Single(function.Instructions, y => y.Shape == "zext i1 % to i8" && y.Operands[0] == x));
    }

    // SPEC 13.4 (DECISIONS R5-M118, R5-M166): a tuple ordering is a call of its Self's comparison item, whose string leaf, not its last,
    // runs compare once and decides where its i32 is not zero (one pass; R5-M75's equality test first is gone).
    [Fact]
    public void ATuplesStringLeafRunsItsOrderOnce()
    {
        var c = Analyze(Comparisons, OutputKind.Library);
        var text = Generate(c);
        var ir = GeneratedIr.Parse(text);
        Assert.Empty(StringCallsOf(ir, c, Assert.Single(FunctionsOf(ir, c, "ordered"))));
        var order = StringWitnesses(ir, c).Order;
        var call = Assert.Single(StringCallsOf(ir, c, Assert.Single(ShimsOf(ir, text), x => x.Instructions.Any(y => y.Callee == order))));
        Assert.Same(order, call.Callee);
        Assert.Contains(ir.Functions, x => x.Instructions.Any(y => y.Shape == "icmp eq i32 %, 0" && y.Operands[0] == call));
    }

    // IMPL 21.1.3 (DECISIONS R5-M118): a string constant is written whole as its record where it is stored, a Tuple's, a fixed array's and an
    // enum Case's component and raw storage alike: its bytes, its count and the Static release kind.
    [Fact]
    public void StringConstantsAreWrittenWholeWhereTheyAreStored()
    {
        var c = Analyze(Comparisons, OutputKind.Library);
        var ir = GeneratedIr.Parse(Generate(c));
        foreach (var (name, records) in new[]
        {
            ("pair", new[] { "store %kimi.string { ptr @, i64 1, i8 0 }, ptr %, align 8" }),
            ("words", ["store %kimi.string { ptr @, i64 1, i8 0 }, ptr %, align 8", "store %kimi.string { ptr null, i64 0, i8 0 }, ptr %, align 8"]),
            ("some", ["store %kimi.string { ptr @, i64 1, i8 0 }, ptr %, align 8"]),
            ("put", ["store %kimi.string { ptr @, i64 1, i8 0 }, ptr %, align 8"]),
        })
        {
            var function = Assert.Single(FunctionsOf(ir, c, name));
            var stores = function.Instructions.Where(static x => x.Shape.StartsWith("store %kimi.string", StringComparison.Ordinal));
            Assert.Equal(records, stores.Select(static x => x.Shape));
        }
    }

    // DECISIONS R5-M118: the routines' text is the module's only where a comparison is written.
    [Fact]
    public void AModuleWithoutStringComparisonsHasNoRoutines()
        => Assert.DoesNotContain(LegacyRuntimeHelpers.StringEqual.Name, Generate(Analyze(Strings, OutputKind.Library)), StringComparison.Ordinal);

    // The string witnesses of a module comparing strings: StringWitness' equals and compare, each one body.
    private static (IrFunction Equal, IrFunction Order) StringWitnesses(GeneratedIr ir, Compilation c)
        => (Assert.Single(FunctionsOf(ir, c, "equals"), static x => x.Signature == $"internal i1 ({StringView}, {StringView})"),
            Assert.Single(FunctionsOf(ir, c, "compare"), static x => x.Signature == $"internal i32 ({StringView}, {StringView})"));

    // A function's calls of the string witnesses, in order.
    private static IrValue[] StringCallsOf(GeneratedIr ir, Compilation c, IrFunction function)
    {
        var (equal, order) = StringWitnesses(ir, c);
        return [.. CallsOf(function).Where(x => x.Callee == equal || x.Callee == order)];
    }
}
