// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

/// <summary>The frame of MIR code generation (R5 C0b1; package C 3.2, 3.3 and 3.9; DECISIONS R5-M65): <c>ModuleCodegen</c> writes every
/// item of the checked set and the output form, <c>FunctionCodegen</c> a body item's prologue, slots, runtime-reached blocks and the forms
/// the frame covers, and a form not covered yet is one located <c>Unsupported_Kd</c> with no module. The <c>Frame*</c> fixtures run under
/// the process's generators (the old one by default, MIR code generation under <c>-Pipeline mir -Codegen mir</c>); the other tests select
/// MIR code generation themselves.</summary>
public sealed class FunctionCodegenTest
{
    private const string Branches =
        "func choose(flag: bool, a: i64, b: i64) -> i64\n    if flag => return a\n    return b\nfunc nested(first: bool, second: bool) -> char\n" +
        "    if first\n        if second => return 'a'\n        return 'b'\n    return 'c'\nlet flag = false\nlet other = true\n" +
        "let x: i32 = if flag => 1 else => 2\nlet y: char = if flag => 'x' else if other => 'y' else => 'z'\n";

    private const string ForLoop =
        "func f(values: ref/Array<i32>) -> i32\n    var total: i32 = 0\n    for value in values\n        total = 1\n    return total\n()";

    private const string Strings =
        "func show(text: ref/string) => Console.writeLine(text)\nfunc greeting() -> string => \"Hi\"\nfunc nothing(flag: bool) -> string => \"\"\n()";

    private const string Arithmetic =
        "func f(a: i32, b: i32) -> i32 => a / b + a % b - (a << b) * 3\nfunc g(a: Wrapping<u8>, c: char) -> bool => -a > 3 and not (c < 'z')\n()";

    // Programs of the frame's forms only: scalar locals and constants, branches, loops and labels, an explicit startup and unused functions
    // with scalar, string-borrow, zero-sized and Never signatures.
    public static TheoryData<string, string> Frames => new()
    {
        {
            "FrameConstants",
            "let flag = true\nlet off = false\nlet small: u8 = 255\nlet big: u64 = 18446744073709551615\nlet low: i64 = -7\nlet wide: i128 = -5\n" +
            "let letter = 'k'\nlet nothing = ()\nvar count: i32 = 1\ncount = 2\n"
        },
        { "FrameBranches", Branches },
        {
            "FrameLoops",
            "var running = true\nlabel outer: loop\n    if running\n        running = false\n        continue to outer\n    exit to outer\n" +
            "let found: i32 = loop => exit 5\nvar again = true\nwhile again\n    again = false\n"
        },
        {
            "FrameMain",
            "func identity(flag: bool, value: i32, text: ref/string, unit: ()) -> i32\n    if flag => return value\n    return 0\n" +
            "func stop() -> Never\n    loop\n        let x = 1\npublic func main()\n    var running = true\n    while running\n        running = false\n"
        },
    };

    // The first programs (R5 C0b2): console output through writeLine, one line, several lines, an empty line and non-ASCII text. MIR code
    // generation writes their calls and literals; it generates them once the Ref (C2b) and Drop (C4a) rows their string temporaries need
    // exist (DECISIONS R5-M73).
    public static TheoryData<string, string, string> Hellos => new()
    {
        { "HelloWorld", "Console.writeLine(\"Hello, world!\")\n", "Hello, world!\n" },
        { "HelloLines", "Console.writeLine(\"one\")\nConsole.writeLine(\"\")\nConsole.writeLine(\"日本語\")\n", "one\n\n日本語\n" },
    };

    // The operator rows' programs (R5 C1a; DECISIONS R5-M76): each tests its results and divides by zero when one is wrong, so a normal exit
    // means every row computed SPEC 13.3.1's and 13.4's value; an Abort program stops with its code at its operator (SPEC 22.5.1-22.5.2).
    public static TheoryData<string, string, string> Operators => new()
    {
        {
            "OperatorSigned",
            "let a: i32 = 7\nlet b: i32 = -3\nlet m: i32 = -2147483648\nlet n: i32 = -1\nvar bad: i32 = 0\n" +
            "if a + b != 4 or a - b != 10 or a * b != -21 => bad = 1\nif a / b != -2 or a % b != 1 or -a != -7 => bad = 1\n" +
            "if m % n != 0 or m % -1 != 0 => bad = 1\nif b >> 1 != -2 or a << 28 != 1879048192 => bad = 1\n" +
            "if (a & 3) != 3 or (a | 8) != 15 or (a ^ 5) != 2 => bad = 1\nif not (b < a and a > b and b <= b and a >= a and a != b) => bad = 1\n" +
            "let check = 1 / (1 - bad)\n",
            string.Empty
        },
        {
            "OperatorUnsigned",
            "let a: u8 = 200\nlet b: u8 = 7\nlet big: u64 = 18446744073709551615\nvar bad: i32 = 0\n" +
            "if a + b != 207 or a - b != 193 or b * b != 49 => bad = 1\nif a / b != 28 or a % b != 4 => bad = 1\n" +
            "if a >> 1 != 100 or b << 5 != 224 => bad = 1\nif not (b < a and a > b) or (a & 15) != 8 => bad = 1\n" +
            "if big / 3 != 6148914691236517205 or big % 10 != 5 or big / big != 1 => bad = 1\nlet check = 1 / (1 - bad)\n",
            string.Empty
        },
        {
            "OperatorWrapping",
            "let max: Wrapping<i32> = 2147483647\nlet min: Wrapping<i32> = -2147483648\nlet minusOne: Wrapping<i32> = -1\n" +
            "let small: Wrapping<u8> = 250\nvar bad: i32 = 0\nif max + 1 != min or min - 1 != max or max * 2 != -2 => bad = 1\n" +
            "if -min != min or min / minusOne != min or min / -1 != min => bad = 1\nif min % minusOne != 0 or min % -1 != 0 => bad = 1\n" +
            "if small + 10 != 4 or -small != 6 => bad = 1\nif not (min < max and small > 4) => bad = 1\nlet check = 1 / (1 - bad)\n",
            string.Empty
        },
        {
            "OperatorScalars",
            "let a = 'a'\nlet z = 'z'\nlet accent = 'é'\nlet sun = '日'\nlet yes = true\nlet no = false\nlet unit = ()\nvar bad: i32 = 0\n" +
            "if not (a < z and z >= a and a == 'a' and a != z) => bad = 1\nif not (accent > z and sun > accent) => bad = 1\n" +
            "if yes == no or not yes or no != false => bad = 1\nif not (unit == ()) or unit != () => bad = 1\nlet check = 1 / (1 - bad)\n",
            string.Empty
        },
        { "OperatorOverflowAbort", "let a: u8 = 255\nlet b = a + 1\n", "Hello.kimi:2:9: abort KIMI_E_INT_OVERFLOW: Integer overflow\n" },
        {
            "OperatorQuotientAbort",
            "let m: i32 = -2147483648\nlet n: i32 = -1\nlet q = m / n\n",
            "Hello.kimi:3:9: abort KIMI_E_INT_OVERFLOW: Integer overflow\n"
        },
        {
            "OperatorZeroAbort",
            "let z: i64 = 0\nlet r = 7 % z\n",
            "Hello.kimi:2:9: abort KIMI_E_INT_DIV_ZERO: Integer division or remainder by zero\n"
        },
        {
            "OperatorShiftAbort",
            "let s: u8 = 32\nlet x: i32 = 1 << s\n",
            "Hello.kimi:2:14: abort KIMI_E_INT_SHIFT_COUNT: Shift count out of range\n"
        },
    };

    [Theory]
    [MemberData(nameof(Frames))]
    public void FrameProgramsRun(string name, string source)
        => ScalarEmissionTest.EmitFixture(name, source, string.Empty);

    [Theory]
    [MemberData(nameof(Operators))]
    public void OperatorProgramsRun(string name, string source, string stderr)
        => ScalarEmissionTest.EmitFixture(name, source, string.Empty, stderr.Length == 0 ? 0 : 1, stderr);

    [Theory]
    [MemberData(nameof(Hellos))]
    public void HelloProgramsRun(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture(name, source, stdout);

    // The console writeLine is a compiler function's legacy runtime row (RuntimeAbi.WriteLine): the routine is called with the string borrow
    // loaded from its local and the call site's location text (SPEC 22.5.1), a header constant. A string constant is stored whole into its
    // local, its bytes a header constant and its release kind Static (SPEC 22.7); the empty string has no bytes.
    [Fact]
    public void WriteLineCallsTheConsoleRuntimeRow()
    {
        var text = Generate(Strings);
        var ir = GeneratedIr.Parse(text);
        var call = Assert.Single(ir.Single("internal void (ptr noundef nonnull align 8 dereferenceable(24))").Instructions, static x => x.Opcode == "call");
        Assert.Same(ir.Function("__kimi_write_line"), call.Callee);
        Assert.Equal("call void @(ptr noundef nonnull align 8 dereferenceable(24) %, ptr @, i64 15)", call.Shape);
        Assert.Equal("load ptr, ptr %, align 8", Assert.Single(call.Operands).Shape);
        Assert.Contains(Constant("Hello.kimi:1:32"), text);
        var greeting = ir.Single("internal void (ptr)");
        var store = Assert.Single(greeting.Instructions, static x => x.Opcode == "store");
        Assert.Equal(("store %kimi.string { ptr @, i64 2, i8 0 }, ptr %, align 8", greeting.Parameters[0]), (store.Shape, Assert.Single(store.Operands)));
        Assert.Contains(Constant("Hi"), text);
        Assert.Equal(
            "store %kimi.string { ptr null, i64 0, i8 0 }, ptr %, align 8",
            Assert.Single(ir.Single("internal void (ptr, i1)").Instructions, static x => x.Opcode == "store" && x.Shape.Contains("kimi.string")).Shape);
    }

    // The operator rows (R5 C1a; DECISIONS R5-M76; SPEC 13.3.1, IMPL 21.3.3): a checked integer operation is its overflow intrinsic, whose
    // flag branches to an Abort with KIMI_E_INT_OVERFLOW (6) at the operator's location text (SPEC 22.5.1-22.5.2), and negation subtracts
    // from 0; the result is the intrinsic's value. Nothing claims nsw or nuw (I9).
    [Theory]
    [InlineData("func f(a: i32, b: i32) -> i32 => a + b", "a + b", "llvm.sadd.with.overflow.i32", "i32 %, i32 %")]
    [InlineData("func f(a: u64, b: u64) -> u64 => a - b", "a - b", "llvm.usub.with.overflow.i64", "i64 %, i64 %")]
    [InlineData("func f(a: i16, b: i16) -> i16 => a * b", "a * b", "llvm.smul.with.overflow.i16", "i16 %, i16 %")]
    [InlineData("func f(a: u8, b: u8) -> u8 => a * b", "a * b", "llvm.umul.with.overflow.i8", "i8 %, i8 %")]
    [InlineData("func f(a: i8) -> i8 => -a", "-a", "llvm.ssub.with.overflow.i8", "i8 0, i8 %")]
    [InlineData("func f(a: isize) -> isize\n    let b = a\n    return b - a", "b - a", "llvm.ssub.with.overflow.i64", "i64 %, i64 %")]
    public void CheckedArithmeticAbortsAtItsOperator(string source, string expression, string intrinsic, string arguments)
    {
        var (ir, text) = Parse(source + "\n()");
        var call = Assert.Single(ir.Functions.SelectMany(static x => x.Instructions), x => x.Callee == ir.Function(intrinsic));
        var f = Assert.Single(ir.Functions, x => x.Instructions.Contains(call));
        var type = arguments.Split(' ')[0];
        Assert.Equal($"call {{ {type}, i1 }} @({arguments})", call.Shape);
        var flag = Assert.Single(f.Instructions, x => x.Shape == $"extractvalue {{ {type}, i1 }} %, 1");
        Assert.Equal([call], flag.Operands);
        var block = Assert.Single(f.Blocks, x => x.Instructions.Contains(call));
        Assert.Equal(("br i1 %, label %, label %", flag), (block.Terminator.Shape, Assert.Single(block.Terminator.Operands)));
        AssertAbort(ir, text, block.Successors[0], AbortReasons.IntegerOverflow, source, expression);
        var value = Assert.Single(f.Instructions, x => x.Shape == $"extractvalue {{ {type}, i1 }} %, 0");
        Assert.Contains(block.Successors[1].Instructions, x => x.Opcode == "store" && x.Operands[0] == value);
        Assert.DoesNotMatch(@"\b(nsw|nuw)\b", text);
    }

    // SPEC 13.3.1: a quotient or remainder by zero Aborts with KIMI_E_INT_DIV_ZERO (7), a signed quotient also the minimum by -1 with
    // KIMI_E_INT_OVERFLOW (6), its failure block selecting the reason; a dynamic signed divisor of -1 divides as 1, so a remainder is 0 and a
    // wrapping quotient the negation; a shift count outside 0 <= count < N Aborts with KIMI_E_INT_SHIFT_COUNT (8) and only then takes the
    // left operand's width. Each check precedes its instruction, which its success block computes.
    [Theory]
    [InlineData("func f(a: i32, b: i32) -> i32 => a / b", "sdiv i32 %, %", "load i32, ptr %, align 4", "icmp eq i32 %, 0", -1)]
    [InlineData("func f(a: u32, b: u32) -> u32 => a / b", "udiv i32 %, %", "load i32, ptr %, align 4", "icmp eq i32 %, 0", 7)]
    [InlineData("func f(a: i64, b: i64) -> i64 => a % b", "srem i64 %, %", "select i1 %, i64 1, i64 %", "icmp eq i64 %, 0", 7)]
    [InlineData("func f(a: Wrapping<u16>, b: Wrapping<u16>) -> Wrapping<u16> => a % b", "urem i16 %, %", "load i16, ptr %, align 2", "icmp eq i16 %, 0", 7)]
    [InlineData("func f(a: Wrapping<i32>, b: Wrapping<i32>) -> Wrapping<i32> => a / b", "sdiv i32 %, %", "select i1 %, i32 1, i32 %", "icmp eq i32 %, 0", 7)]
    [InlineData("func f(a: i32, b: u8) -> i32 => a << b", "shl i32 %, %", "zext i8 % to i32", "icmp uge i8 %, 32", 8)]
    [InlineData("func f(a: u64, b: i8) -> u64 => a >> b", "lshr i64 %, %", "zext i8 % to i64", "icmp uge i8 %, 64", 8)]
    [InlineData("func f(a: Wrapping<i8>, b: i64) -> Wrapping<i8> => a >> b", "ashr i8 %, %", "trunc i64 % to i8", "icmp uge i64 %, 8", 8)]
    [InlineData("func f(a: i32, b: i32) -> i32 => a << b", "shl i32 %, %", "load i32, ptr %, align 4", "icmp uge i32 %, 32", 8)]
    public void DivisionAndShiftChecksAbortBeforeTheirInstruction(string source, string instruction, string divisor, string condition, int reason)
    {
        var (ir, text) = Parse(source + "\n()");
        var operation = Assert.Single(ir.Functions.SelectMany(static x => x.Instructions), x => x.Shape == instruction);
        var f = Assert.Single(ir.Functions, x => x.Instructions.Contains(operation));
        var check = Assert.Single(f.Blocks, x => x.Successors.Count == 2 && x.Successors[1].Instructions.Contains(operation));
        var abort = AssertAbort(ir, text, check.Successors[0], reason, source, source[(source.IndexOf("=> ", StringComparison.Ordinal) + 3)..]);
        if (reason < 0)
        {
            Assert.Equal("select i1 %, i32 7, i32 6", Assert.Single(abort.Operands).Shape); // Zero, else the minimum by -1.
        }

        Assert.Equal(("load", divisor), (operation.Operands[0].Opcode, operation.Operands[1].Shape));
        var tested = operation.Operands[1].Opcode is "zext" or "trunc" ? operation.Operands[1].Operands[0] : operation.Operands[1];
        Assert.Contains(check.Instructions, x => x.Shape == condition && x.Operands[0] == (tested.Opcode == "select" ? tested.Operands[1] : tested));
        if (f.Instructions.FirstOrDefault(x => x.Opcode == "select" && x.Operands.Contains(operation)) is { } wrapped)
        {
            Assert.Equal(("select i1 %, i32 %, i32 %", "sub i32 0, %"), (wrapped.Shape, wrapped.Operands[1].Shape)); // MIN / -1 is MIN.
        }

        Assert.DoesNotMatch(@"\b(nsw|nuw)\b", text);
    }

    // IMPL 21.3.3: a Proven operation writes no check, and a wrapping integer operation (SPEC 3.1.1), a bitwise operation and a
    // comparison check nothing: each is one instruction, a literal divisor of -1 dividing as the negation (wrapping) or a remainder as 1.
    // Comparisons are icmp predicates by the operands' signedness (char a Unicode scalar value's, SPEC 13.4) whose i1 result is stored
    // widened; not is xor with -1; Unit's one value is equal to itself.
    [Theory]
    [InlineData("func f(a: Wrapping<i32>, b: Wrapping<i32>) -> Wrapping<i32> => a + b", "internal i32 (i32, i32)", "add i32 %, %")]
    [InlineData("func f(a: Wrapping<u8>) -> Wrapping<u8> => -a", "internal i8 (i8)", "sub i8 0, %")]
    [InlineData("func f(a: Wrapping<i128>, b: Wrapping<i128>) -> Wrapping<i128> => a * b", "internal i128 (i128, i128)", "mul i128 %, %")]
    [InlineData("func f(a: i32) -> i32 => a / 3", "internal i32 (i32)", "sdiv i32 %, 3")]
    [InlineData("func f(a: i32) -> i32 => a % -1", "internal i32 (i32)", "srem i32 %, 1")]
    [InlineData("func f(a: Wrapping<i32>) -> Wrapping<i32> => a / -1", "internal i32 (i32)", "sub i32 0, %")]
    [InlineData("func f(a: i64) -> i64 => a << 3", "internal i64 (i64)", "shl i64 %, 3")]
    [InlineData("func f(a: u16) -> u16 => a >> 2", "internal i16 (i16)", "lshr i16 %, 2")]
    [InlineData("func f(a: i128) -> i128 => a >> 1", "internal i128 (i128)", "ashr i128 %, 1")]
    [InlineData("func f(a: i64, b: i64) -> i64 => a & b", "internal i64 (i64, i64)", "and i64 %, %")]
    [InlineData("func f(a: u8, b: u8) -> u8 => a | b", "internal i8 (i8, i8)", "or i8 %, %")]
    [InlineData("func f(a: Wrapping<u32>, b: Wrapping<u32>) -> Wrapping<u32> => a ^ b", "internal i32 (i32, i32)", "xor i32 %, %")]
    [InlineData("func f(a: i32, b: i32) -> bool => a < b", "internal i1 (i32, i32)", "icmp slt i32 %, %")]
    [InlineData("func f(a: u32, b: u32) -> bool => a <= b", "internal i1 (i32, i32)", "icmp ule i32 %, %")]
    [InlineData("func f(a: Wrapping<i8>, b: Wrapping<i8>) -> bool => a >= b", "internal i1 (i8, i8)", "icmp sge i8 %, %")]
    [InlineData("func f(a: Wrapping<u64>, b: Wrapping<u64>) -> bool => a > b", "internal i1 (i64, i64)", "icmp ugt i64 %, %")]
    [InlineData("func f(a: char, b: char) -> bool => a < b", "internal i1 (i32, i32)", "icmp ult i32 %, %")]
    [InlineData("func f(a: u128, b: u128) -> bool => a != b", "internal i1 (i128, i128)", "icmp ne i128 %, %")]
    [InlineData("func f(a: bool, b: bool) -> bool => a == b", "internal i1 (i1, i1)", "icmp eq i1 %, %")]
    [InlineData("func f(a: bool) -> bool => not a", "internal i1 (i1)", "xor i1 %, -1")]
    [InlineData("func f(a: (), b: ()) -> bool => a == b", "internal i1 ()", "zext i1 1 to i8")]
    [InlineData("func f(a: (), b: ()) -> bool => a != b", "internal i1 ()", "zext i1 0 to i8")]
    public void UncheckedOperatorsAreOneInstruction(string source, string signature, string shape)
    {
        var (ir, text) = Parse(source + "\n()");
        var f = ir.Single(signature);
        var operation = Assert.Single(f.Instructions, x => x.Shape == shape);
        Assert.DoesNotContain(f.Instructions, x => x.Callee == ir.Function("__kimi_abort"));
        Assert.All(operation.Operands, static x => Assert.Contains(x.Opcode, new[] { "load", "trunc" }));
        if (signature.StartsWith("internal i1 ", StringComparison.Ordinal) && operation.Opcode != "zext")
        {
            Assert.Contains(f.Instructions, x => x.Shape == "zext i1 % to i8" && x.Operands[0] == operation); // I10: bool is stored as i8.
        }

        Assert.DoesNotMatch(@"\b(nsw|nuw)\b", text);
    }

    // I2 (IMPL 21.1.3): one aligned slot per local of nonzero size, in local order, bool stored as i8 and never as i1, none for a zero-sized
    // local; an integer constant is its value's two's complement at its width, stored into its local's slot (GeneratedIrTest covers the
    // entry and the profile attributes for both generators).
    [Fact]
    public void EachLocalOfNonzeroSizeHasOneSlot()
    {
        var text = Generate(
            "let flag = true\nlet n: i32 = 7\nlet nothing = ()\nlet small: u8 = 255\nlet big: u64 = 18446744073709551615\nlet wide: i128 = -5\n");
        var ir = GeneratedIr.Parse(text);
        var body = ir.Startup;
        var slots = body.Instructions.Where(static x => x.Opcode == "alloca").ToArray();
        Assert.Equal(
            ["alloca i8, align 1", "alloca i32, align 4", "alloca i8, align 1", "alloca i64, align 8", "alloca i128, align 16"],
            slots.Select(static x => x.Shape));
        Assert.Equal(
            [
                ("store i8 1, ptr %, align 1", slots[0]), ("store i32 7, ptr %, align 4", slots[1]), ("store i8 -1, ptr %, align 1", slots[2]),
                ("store i64 -1, ptr %, align 8", slots[3]), ("store i128 -5, ptr %, align 16", slots[4]),
            ],
            body.Instructions.Where(static x => x.Opcode == "store").Select(static x => (x.Shape, x.Operands[^1])));
        Assert.DoesNotMatch(@"store i1\b", text);
    }

    // I2: a Value argument is stored into its local's slot (bool widened), a pointer-passed argument is its local's home, a zero-sized one has
    // no parameter; a bool result is narrowed to be returned. I1: values %t<n>, slots %l<local> and arguments %a<slot> are numbered per
    // function.
    [Fact]
    public void ArgumentsLiveInTheirLocalsHomes()
    {
        var text = Generate("func f(flag: bool, n: i64, text: ref/string, unit: ()) -> bool => flag\nfunc g(pair: (i32, i64)) => ()\n()");
        Assert.Contains("  %t0 = zext i1 %a0 to i8\n  store i8 %t0, ptr %l1, align 1\n  store i64 %a1, ptr %l2, align 8\n", text);
        var ir = GeneratedIr.Parse(text);
        var f = ir.Single("internal i1 (i1, i64, ptr noundef nonnull align 8 dereferenceable(24))");
        var entry = f.Blocks[0].Instructions;
        Assert.Equal(
            [
                "alloca i8, align 1", "alloca i8, align 1", "alloca i64, align 8", "alloca ptr, align 8", "zext i1 % to i8", "store i8 %, ptr %, align 1",
                "store i64 %, ptr %, align 8", "store ptr %, ptr %, align 8", "br label %",
            ],
            entry.Select(static x => x.Shape));
        Assert.Equal([f.Parameters[0]], entry[4].Operands);
        Assert.Equal([entry[4], entry[1]], entry[5].Operands);
        Assert.Equal([f.Parameters[1], entry[2]], entry[6].Operands);
        Assert.Equal([f.Parameters[2], entry[3]], entry[7].Operands);
        Assert.Same(f.Blocks[1], Assert.Single(f.Blocks[0].Successors));
        var result = Assert.Single(f.Instructions, static x => x.Opcode == "ret");
        Assert.Equal("ret i1 %", result.Shape);
        Assert.Equal("trunc i8 % to i1", Assert.Single(result.Operands).Shape);
        Assert.DoesNotContain(ir.Single("internal void (ptr)").Instructions, static x => x.Opcode == "alloca");
    }

    // I3 (SPEC 14.10.3): exactly the blocks a runtime path from the entry reaches are generated; a block only the checks reach is not.
    [Fact]
    public void OnlyRuntimeReachedBlocksAreGenerated()
    {
        var c = Analyze("func f(flag: bool) -> i32\n    if flag\n        return 1\n    return 2\n()");
        var ir = Generate(c);
        var function = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "f");
        var body = c.BuildMir()[new(function.SymbolOf(), MirBodyKind.Fn)]!;
        var reach = new MirReach();
        reach.Clear(body.Blocks.Length);
        reach.Walk(body, 0, checks: false);
        var expected = Enumerable.Range(0, body.Blocks.Length).Where(x => reach[x]).Select(static x => "b" + x).ToArray();
        Assert.NotEqual(body.Blocks.Length, expected.Length);
        Assert.Equal(expected, GeneratedIr.Parse(ir).Single("internal i32 (i1)").Blocks.Skip(1).Select(static x => x.Label));
    }

    // PD11: a form the frame does not cover yet is one Unsupported_Kd in the Emission partition at its location, and no module is written;
    // its valid counterpart is. Console.writeLine's call is written, and its literal's borrow (C2b) and destruction (C4a, the Drop and the
    // string's DropGlue item) are one record at the literal (DECISIONS R5-M73), as a callee's result's are at that call (C3a); the Utf8Slice
    // overload is another legacy row (C9a), at its call; the records of a for loop's statement and of the siblings its
    // expansion synthesizes with its span ($for.iterator, $for.some, $for.none) are one at that location (DECISIONS R5-M65), which the
    // diagnostics could not order when each was keyed by its node. A static's initializer is C7b's row, at the static. The operator rows
    // (DECISIONS R5-M76) leave floating-point operands to C1b, raw pointers to C8a and checked 128-bit arithmetic to ModuleCodegen's wide
    // overflow declarations; a compound update writes through a borrow (C2b) of its target.
    [Theory]
    [InlineData("func twice(a: i128) -> i128 => a + a\n()", new[] { "a + a" })]
    [InlineData("func twice(a: f64) -> f64 => a + a\n()", new[] { "a + a" })]
    [InlineData("func same(p: raw/i32, q: raw/i32) -> bool => p == q\n()", new[] { "p == q" })]
    [InlineData("var x: i32 = 1\nx += 1\n", new[] { "x += 1" })]
    [InlineData("func twice(a: Wrapping<i128>) -> Wrapping<i128> => a + a\n()", new string[0])]
    [InlineData("func twice(a: i32) -> i32 => a\nlet x = twice(1)\n", new[] { "twice(1)" })]
    [InlineData("Console.writeLine(\"\")\n", new[] { "\"\"" })]
    [InlineData("func name() -> string => \"x\"\nfunc show() => Console.writeLine(name())\n()", new[] { "name()" })]
    [InlineData("func show(text: Text.Utf8Slice) => Console.writeLine(text)\n()", new[] { "Console.writeLine(text)" })]
    [InlineData(ForLoop, new[] { "for value in values\n        total = 1", "value" })]
    [InlineData("group Registry\n    public let limit: i32 = 4\n()", new[] { "let limit: i32 = 4" })]
    [InlineData("func twice(a: i32) -> i32 => a\n()", new string[0])]
    public void AnUncoveredFormIsOneLocatedUnsupportedRecord(string source, string[] sites)
    {
        var c = Analyze(source);
        using var writer = new StringWriter();
        var written = sites.Length == 0;
        Assert.Equal(written, c.Generate(writer));
        Assert.Equal(written, writer.ToString().Length != 0);
        Assert.Equal(sites.Select(static x => ("Unsupported_Kd", (string?)x)), Records(c));
        Assert.All(TestDiagnostics.Of(c), static x => Assert.Equal("Hello.kimi", x.Path));
        Assert.Equal(!written, c.Diagnostics.HasErrorsIn(DiagnosticPartition.Emission));
        Assert.Equal(written, c.WriteIr(TextWriter.Null, out var failure));
        Assert.Equal(written, failure is null);
    }

    // I1: the same program gives the same text, in one compilation and in two.
    [Theory]
    [InlineData(Branches)]
    [InlineData(Strings)]
    [InlineData(Arithmetic)]
    public void GenerationIsDeterministic(string source)
    {
        var first = Generate(Analyze(source));
        var c = Analyze(source);
        Assert.Equal(first, Generate(c));
        Assert.Equal(first, Generate(c));
    }

    // Every milestone generates under MIR code generation, or records only coverage rows: never a broken contract.
    [Theory]
    [MemberData(nameof(HirColumnsTest.Corpus), MemberType = typeof(HirColumnsTest))]
    public void TheMilestonesRecordOnlyCoverageRows(string name)
    {
        if (!name.StartsWith("milestone/", StringComparison.Ordinal))
        {
            return;
        }

        var c = Analyze(File.ReadAllText(DiagnosticCorpus.RepositoryPath("tests", "milestones", name["milestone/".Length..] + ".kimi")));
        if (c.Generate(TextWriter.Null))
        {
            Assert.Empty(Records(c));
            return;
        }

        Assert.All(Records(c), static x => Assert.Equal("Unsupported_Kd", x.Code));
    }

    // I11: generating a program again allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(Branches)]
    [InlineData(Strings)]
    [InlineData(Arithmetic)]
    public void WarmGenerationAllocatesNothing(string source)
    {
        var c = Analyze(source);
        var written = true;
        var allocated = AllocationMeasurement.Measure(() => written &= c.Generate(TextWriter.Null));
        Assert.True(written);
        Assert.Equal(0, allocated);
    }

    // A program analyzed under MIR code generation (sel).
    private static Compilation Analyze(string source)
    {
        var c = Compilation.CreateForTest();
        c.Pipeline = CompilerPipeline.Mir;
        c.Codegen = CompilerCodegen.Mir;
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", source));
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        c.Analyze();
        return c;
    }

    private static string Generate(string source) => Generate(Analyze(source));

    private static (GeneratedIr Ir, string Text) Parse(string source)
    {
        var text = Generate(source);
        return (GeneratedIr.Parse(text), text);
    }

    // A check's failure block (SPEC 22.5.1-22.5.2): the runtime Abort with its reason (a selected one when negative), the location text of
    // the expression's first character and no OS error, then unreachable.
    private static IrValue AssertAbort(GeneratedIr ir, string text, IrBlock failure, int reason, string source, string expression)
    {
        var at = source.IndexOf(expression, StringComparison.Ordinal);
        var location = $"Hello.kimi:{source[..at].Count(static x => x == '\n') + 1}:{at - source.LastIndexOf('\n', Math.Max(at - 1, 0))}";
        var abort = Assert.Single(failure.Instructions, x => x.Callee == ir.Function("__kimi_abort"));
        var code = reason < 0 ? "%" : reason.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal($"call void @(i32 {code}, ptr @, i64 {System.Text.Encoding.UTF8.GetByteCount(location)}, i64 -2)", abort.Shape);
        Assert.Equal(["call", "unreachable"], failure.Instructions.Select(static x => x.Opcode).Skip(failure.Instructions.Count - 2));
        Assert.Contains(Constant(location), text);
        return abort;
    }

    private static string Generate(Compilation c)
    {
        using var writer = new StringWriter();
        Assert.True(c.Generate(writer), string.Join(", ", Records(c)));
        return writer.ToString();
    }

    // The end of the header's definition of a pooled constant: its UTF-8 bytes, each escaped (LlvmConstantPool).
    private static string Constant(string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        return $" = private unnamed_addr constant [{bytes.Length} x i8] c\"{string.Concat(bytes.Select(static x => $"\\{x:X2}"))}\", align 1\n";
    }

    private static (string Code, string? Text)[] Records(Compilation c)
        => [.. TestDiagnostics.Of(c).Where(static x => x.Severity == DiagnosticSeverity.Error).Select(static x => (x.Code, x.Text))];
}
