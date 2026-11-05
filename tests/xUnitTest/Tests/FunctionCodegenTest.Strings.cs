// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Xunit;

namespace XunitTest;

// The string comparisons and string constants of MIR code generation (R5 C8c; SPEC 13.4, 13.4.1, 14.8.1, 3.4.3, IMPL 21.1.3; DECISIONS
// R5-M118): a comparison is the string column's routine on its operands' bytes, the routine's i32 compared with 0; a constant passes its
// pooled bytes and is written whole where it is stored; natively.
public sealed partial class FunctionCodegenTest
{
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
    };

    [Theory]
    [MemberData(nameof(StringPrograms))]
    public void StringProgramsRun(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture(name, source, stdout);

    // SPEC 13.4, 13.4.1 (DECISIONS R5-M118): each operator calls its row's routine, the equality routine for == and != and the ordering
    // routine for the others, on its operands' bytes, the records' Fields 0 and 1 read through the borrows, then compares the routine's i32
    // with 0 by its predicate; the bool is stored widened.
    [Fact]
    public void StringComparisonsCallTheirRoutineOnTheRecordsBytes()
    {
        var c = Analyze(Comparisons, OutputKind.Library);
        var text = Generate(c);
        var ir = GeneratedIr.Parse(text);
        var (equal, order) = (ir.Function(LegacyRuntimeHelpers.StringEqual.Name), ir.Function(LegacyRuntimeHelpers.StringCompare.Name));
        foreach (var (name, routine, predicate) in new[]
        {
            ("equal", equal, "eq"), ("differ", equal, "ne"), ("less", order, "slt"), ("most", order, "sle"), ("more", order, "sgt"),
            ("least", order, "sge"),
        })
        {
            var function = Assert.Single(FunctionsOf(ir, c, name));
            var call = Assert.Single(StringCallsOf(ir, function));
            Assert.Same(routine, call.Callee);
            Assert.Equal("call i32 @(ptr %, i64 %, ptr %, i64 %)", call.Shape);
            for (var side = 0; side < 2; side++)
            {
                var (data, length) = (call.Operands[2 * side], call.Operands[(2 * side) + 1]);
                Assert.Equal("load ptr, ptr %, align 8", data.Shape);
                Assert.Equal("load i64, ptr %, align 8", length.Shape);
                Assert.Equal("getelementptr i8, ptr %, i64 8", length.Operands[0].Shape);
                Assert.Same(data.Operands[0], length.Operands[0].Operands[0]);
            }

            var test = Assert.Single(function.Instructions, x => x.Opcode == "icmp");
            Assert.Equal($"icmp {predicate} i32 %, 0", test.Shape);
            Assert.Same(call, test.Operands[0]);
            Assert.Contains(function.Instructions, x => x.Shape == "zext i1 % to i8" && x.Operands[0] == test);
        }

        Assert.Equal(1, text.Split(LegacyRuntimeHelpers.StringComparisons).Length - 1); // The routines' one text, the old generator's (PD2).
    }

    // SPEC 13.4, IMPL 21.1.3 (DECISIONS R5-M118): a string constant operand passes its pooled bytes and their count, the empty string a
    // null pointer and 0, and no record is stored for it.
    [Fact]
    public void StringConstantOperandsPassTheirBytes()
    {
        var c = Analyze(Comparisons, OutputKind.Library);
        var ir = GeneratedIr.Parse(Generate(c));
        foreach (var (name, shape) in new[]
        {
            ("literal", "call i32 @(ptr %, i64 %, ptr @, i64 3)"), ("empty", "call i32 @(ptr %, i64 %, ptr null, i64 0)"),
            ("constants", "call i32 @(ptr @, i64 1, ptr @, i64 1)"),
        })
        {
            var function = Assert.Single(FunctionsOf(ir, c, name));
            Assert.Equal(shape, Assert.Single(StringCallsOf(ir, function)).Shape);
            Assert.DoesNotContain(function.Instructions, static x => x.Shape.StartsWith("store %kimi.string", StringComparison.Ordinal));
        }
    }

    // SPEC 13.4, 3.4.3 (DECISIONS R5-M118): an operand below a uniq layer reaches its record through the pointer the layer stores: the
    // shared borrow's value, then one more pointer load, then the record's bytes.
    [Fact]
    public void ALayeredOperandLoadsItsReferences()
    {
        var c = Analyze(Comparisons, OutputKind.Library);
        var ir = GeneratedIr.Parse(Generate(c));
        var call = Assert.Single(StringCallsOf(ir, Assert.Single(FunctionsOf(ir, c, "layered"))));
        Assert.Equal("call i32 @(ptr %, i64 %, ptr @, i64 1)", call.Shape);
        var record = call.Operands[0].Operands[0];
        Assert.Equal("load ptr, ptr %, align 8", record.Shape); // The uniq pointer the borrowed slot stores ...
        Assert.Equal("load ptr, ptr %, align 8", record.Operands[0].Shape); // ... read through the shared borrow's value.
    }

    // SPEC 14.8.1 (DECISIONS R5-M118): a literal Pattern's test is the comparison row: the equality routine on the selected Place's bytes
    // and the literal's, in arm order, its i32 tested equal to 0.
    [Fact]
    public void LiteralPatternsCompareTheSubjectsBytes()
    {
        var c = Analyze(Comparisons, OutputKind.Library);
        var ir = GeneratedIr.Parse(Generate(c));
        var function = Assert.Single(FunctionsOf(ir, c, "pick"));
        var calls = StringCallsOf(ir, function);
        Assert.Equal(["call i32 @(ptr %, i64 %, ptr @, i64 1)", "call i32 @(ptr %, i64 %, ptr null, i64 0)"], calls.Select(static x => x.Shape));
        Assert.All(calls, x => Assert.Same(ir.Function(LegacyRuntimeHelpers.StringEqual.Name), x.Callee));
        Assert.All(calls, x => Assert.Single(function.Instructions, y => y.Shape == "icmp eq i32 %, 0" && y.Operands[0] == x));
    }

    // SPEC 13.4 (DECISIONS R5-M118, R5-M166): a tuple ordering is a call of its Self's comparison item, whose string leaf, not its last,
    // runs the ordering routine once and decides where its i32 is not zero (one pass; R5-M75's equality test first is gone).
    [Fact]
    public void ATuplesStringLeafRunsItsOrderOnce()
    {
        var c = Analyze(Comparisons, OutputKind.Library);
        var text = Generate(c);
        var ir = GeneratedIr.Parse(text);
        Assert.Empty(StringCallsOf(ir, Assert.Single(FunctionsOf(ir, c, "ordered"))));
        var order = ir.Function(LegacyRuntimeHelpers.StringCompare.Name);
        var call = Assert.Single(StringCallsOf(ir, Assert.Single(ShimsOf(ir, text), x => x.Instructions.Any(y => y.Callee == order))));
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

    // A function's calls of the string routines (LegacyRuntimeHelpers' StringEqual and StringCompare), in order.
    private static IrValue[] StringCallsOf(GeneratedIr ir, IrFunction function)
    {
        var (equal, order) = (ir.Function(LegacyRuntimeHelpers.StringEqual.Name), ir.Function(LegacyRuntimeHelpers.StringCompare.Name));
        return [.. CallsOf(function).Where(x => x.Callee == equal || x.Callee == order)];
    }
}
