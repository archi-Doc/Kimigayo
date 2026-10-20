// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.RegularExpressions;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

/// <summary>The frame of MIR code generation (R5 C0b1, K11, C2a; package C 3.2, 3.3 and 3.9; DECISIONS R5-M65, R5-M77, R5-M83):
/// <c>ModuleCodegen</c> writes the Generated items of the checked set and the output form, <c>FunctionCodegen</c> a body item's prologue,
/// slots, runtime-reached blocks, the Places of its place writer and the forms the frame covers, and a form not covered yet is one located
/// <c>Unsupported_Kd</c> with no module. The fixtures run under the process's generators (the old one by default, MIR code generation
/// under <c>-Pipeline mir -Codegen mir</c>); the other tests select MIR code generation themselves.</summary>
public sealed class FunctionCodegenTest
{
    private const string Branches =
        "func choose(flag: bool, a: i64, b: i64) -> i64\n    if flag => return a\n    return b\nfunc nested(first: bool, second: bool) -> char\n" +
        "    if first\n        if second => return 'a'\n        return 'b'\n    return 'c'\nlet flag = false\nlet other = true\n" +
        "let x: i32 = if flag => 1 else => 2\nlet y: char = if flag => 'x' else if other => 'y' else => 'z'\n";

    // Library programs (K11: a Library generates every root, an Application only what its startup body reaches).
    private const string ForLoop =
        "func f(values: ref/Array<i32>) -> i32\n    var total: i32 = 0\n    for value in values\n        total = 1\n    return total\n";

    private const string Strings =
        "func show(text: ref/string) => Console.writeLine(text)\nfunc greeting() -> string => \"Hi\"\nfunc nothing(flag: bool) -> string => \"\"\n";

    // Two literals' temporaries, one on a conditional path, each borrowed and destroyed (Keep), and a string never initialized (Elide).
    private const string Drops =
        "Console.writeLine(\"a\")\nlet s: string\nlet flag = true\nif flag\n    Console.writeLine(\"b\")\n";

    private const string Arithmetic =
        "func f(a: i32, b: i32) -> i32 => a / b + a % b - (a << b) * 3\nfunc g(a: Wrapping<u8>, c: char) -> bool => -a > 3 and not (c < 'z')\n";

    private const string Conversions =
        "func f(a: f64, b: i32) -> i64 => (a * b@f64)@i64\nfunc g(a: u64, c: f32) -> bool => a@f32 < -c and a@wrap<u8>@f64@f32 != 0.5\n";

    private const string Unused = "func unused(n: i64) -> i64 => n\n";

    // A pair binder's function in its owner and its uniq case (SPEC 8.15, 13.5.5), instanced by the two calls.
    private const string PairLayers =
        "func keep<s/T>(value: s/T, x: T) -> T\n    s is owner or uniq\n    T is Copy\n    var v = value@move\n    v@follow = x\n    return v@follow\n" +
        "var y: i32 = 1\nlet a = keep(5, 2)\nlet b = keep(y@uniq, 2)\n";

    // A base and its derived struct, and an enum whose payload Tuples differ in alignment (IMPL 21.1.2-21.1.3); the base call is C3a's row.
    private const string Layouts =
        "open struct Base\n    public var count: i32\n    protected init(count: i32) => self.count = count\nstruct Derived : Base\n" +
        "    public var extra: i32\n    public init(count: i32, extra: i32) : base(count)\n        self.extra = extra\n" +
        "enum Shape\n    Circle(i32)\n    Rect(i32, i64)\n    Empty\nfunc counted(d: ref/Derived) -> i32 => d.count\n" +
        "func extra(d: ref/Derived) -> i32 => d.extra\nfunc payload(shape: ref/Shape) -> i64\n    return match shape\n" +
        "        .Rect(_, let h) => h\n        _ => 0\n";

    // Elements read, written and updated, a referent read and written, and a fixed array built from its elements (R5 C2a).
    private const string Places =
        "func f(a: ref/[3 of i32], i: isize) -> i32 => a[i]\nfunc g(a: [3 of i64]) -> i64 => a[2]\nfunc h(q: uniq/i32) => q@follow = 7\n" +
        "func k(t: ref/(i8, i64, i16), a: uniq/Array<u8>, i: isize) -> i16\n    a[i] += 1\n    return t.2\n" +
        "func m() -> i16\n    let b: [3 of i16] = [1, 2, 3]\n    return b[1]\n";

    // Functions with scalar, string-borrow, zero-sized and Never signatures, a Library's roots.
    private const string Signatures =
        "func identity(flag: bool, value: i32, text: ref/string, unit: ()) -> i32\n    if flag => return value\n    return 0\n" +
        "func stop() -> Never\n    loop\n        let x = 1\n";

    // Programs of the frame's forms only: scalar locals and constants, branches, loops and labels and an explicit startup (an Application
    // generates only what its startup body reaches, K11; Signatures are a Library's).
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
        { "FrameMain", "public func main()\n    var running = true\n    while running\n        running = false\n" },
    };

    // The first programs (R5 C0b2, C0c): console output through writeLine, one line, several lines, an empty line and non-ASCII text: the
    // literal, its borrow, the call and the temporary's destruction (DECISIONS R5-M73, R5-M78).
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
        {
            "OperatorFloating",
            "let zero = 0.0\nlet one = 1.0\nlet nan = zero / zero\nlet infinity = one / zero\nlet negative = -zero\nlet tenth = 0.1\nlet fifth = 0.2\n" +
            "let big: f32 = 16777216.0\nlet unit: f32 = 1.0\nlet three: f32 = 3.0\nvar bad: i32 = 0\n" +
            "if not (nan != nan) or nan == nan or nan < one or nan <= one or nan > one or nan >= one => bad = 1\n" +
            "if negative != zero or not (negative == zero and negative <= zero and negative >= zero) or negative < zero => bad = 1\n" +
            "if negative@bits<u64> != 9223372036854775808 or one / negative != -infinity or not (infinity > one) => bad = 1\n" +
            "if (tenth + fifth)@bits<u64> != 4599075939470750516 or tenth + fifth == 0.3 or fifth - tenth != tenth => bad = 1\n" +
            "if (infinity - infinity) == (infinity - infinity) or tenth * 10.0 != one or one / three@f64 == 0.0 => bad = 1\n" +
            "if big + unit != big or (unit / three)@bits<u32> != 1051372203 or -unit != unit - 2.0 or (-(zero@f32))@bits<u32> != 2147483648 => bad = 1\n" +
            "let check = 1 / (1 - bad)\n",
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

    // The conversion rows' programs (R5 C1b; DECISIONS R5-M80), self-checking as the operator rows' are: SPEC 13.5.4's checked integer,
    // wrapping, floating, numeric and bit conversions (truncation toward zero, ties to even, the sign of a zero, NaN and infinities kept)
    // and SPEC 5.4-5.5's address conversions; each Abort program stops with its code at its conversion (SPEC 22.5.2).
    public static TheoryData<string, string, string> Casts => new()
    {
        {
            "CastInteger",
            "let a: i32 = 300\nlet n: i32 = -1\nlet b: u8 = 200\nlet m: i8 = -56\nlet max: u64 = 18446744073709551615\n" +
            "let w: Wrapping<u32> = 4294967295\nvar bad: i32 = 0\n" +
            "if a@wrap<u8> != 44 or n@wrap<u64> != max or b@wrap<i8> != m or m@wrap<u32> != 4294967240 or m@wrap<i64> != -56 => bad = 1\n" +
            "if b@i32 != 200 or m@i64 != -56 or a@i16 != 300 or (a - 45)@u8 != 255 or n@i8 != -1 or b@u64 != 200 => bad = 1\n" +
            "if w@u32 != 4294967295 or (max - 1)@wrap<u32> != 4294967294 or (b@Wrapping<u8> + b@Wrapping<u8>)@u8 != 144 => bad = 1\n" +
            "let check = 1 / (1 - bad)\n",
            string.Empty
        },
        {
            "CastFloating",
            "let zero = 0.0\nlet down = -2.9\nlet up = 2.9\nlet edge = -128.9\nlet tiny = -1e-300\nlet nan = zero / zero\nlet infinity = 1.0 / zero\n" +
            "let odd: i32 = 16777219\nlet even: i32 = 16777217\nlet big: u64 = 18446744073709551615\nlet half: f32 = 1.5\nlet raw: u32 = 1069547520\n" +
            "let integerZero: i64 = 0\nvar bad: i32 = 0\n" +
            "if down@i32 != -2 or up@i32 != 2 or edge@i8 != -128 or (-0.9)@u8 != 0 or 255.9@u8 != 255 => bad = 1\n" +
            "if odd@f32 != 16777220.0 or even@f32 != 16777216.0 or big@f64 != 18446744073709551616.0 or integerZero@f64@bits<u64> != 0 => bad = 1\n" +
            "if tiny@f32@bits<u32> != 2147483648 or not (nan@f32 != nan@f32) or infinity@f32 != infinity@f32 or (-infinity)@f32@f64 != -infinity => bad = 1\n" +
            "if half@bits<u32> != raw or raw@bits<f32> != half or half@f64 != 1.5 or (3.4028234663852886e38)@f32@f64 != 3.4028234663852886e38 => bad = 1\n" +
            "if up@f32@f64 == up or down@f32@bits<u32> != 3225000346 => bad = 1\nlet check = 1 / (1 - bad)\n",
            string.Empty
        },
        {
            "CastPointer",
            "let address: usize = 4096\nlet p = address@raw/i32\nlet q = p@raw/u8\nvar bad: i32 = 0\nif p@usize != 4096 or q@usize != address => bad = 1\n" +
            "let check = 1 / (1 - bad)\n",
            string.Empty
        },
        { "CastRangeAbort", "let a: i32 = 300\nlet b = a@u8\n", "Hello.kimi:2:9: abort KIMI_E_INT_CONVERSION: Integer conversion out of range\n" },
        {
            "CastTruncationAbort",
            "let big = 2147483648.0\nlet i = big@i32\n",
            "Hello.kimi:2:9: abort KIMI_E_INT_CONVERSION: Integer conversion out of range\n"
        },
        {
            "CastNaNAbort",
            "let zero = 0.0\nlet n = (zero / zero)@i64\n",
            "Hello.kimi:2:9: abort KIMI_E_INT_CONVERSION: Integer conversion out of range\n"
        },
        {
            "CastNarrowingAbort",
            "let big = 3.5e38\nlet f = big@f32\n",
            "Hello.kimi:2:9: abort KIMI_E_FLOAT_CONVERSION: Floating conversion out of range\n"
        },
    };

    // The place writer's programs (R5 C2a; DECISIONS R5-M83), self-checking as the operator rows' are: fixed-array elements read, written and
    // updated at constant and computed positions, also of a narrower Type, and arrays built, filled and left uninitialized (SPEC 4.3, 4.7);
    // a referent read and written through a reference and a compound update through its reservation (SPEC 13.5.5, 13.7.2). Each Abort
    // program stops with KIMI_E_INDEX_BOUNDS at its selection (SPEC 4.7.4, 22.5.1-22.5.2): past the end, at a negative position, at a
    // same-width unsigned position isize cannot hold, on a write and on an update; an assignment's right-hand side Aborts before its target
    // is located (SPEC 13.7.1).
    public static TheoryData<string, string, string> PlacePrograms => new()
    {
        {
            "PlaceArray",
            "var a: [4 of i32] = [10, 20, 30, 40]\nlet i: isize = 2\nlet n: u8 = 3\nvar bad: i32 = 0\n" +
            "if a[0] != 10 or a[3] != 40 or a[i] != 30 or a[n] != 40 => bad = 1\na[i] = 7\na[1] += 5\na[n] -= 1\n" +
            "if a[2] != 7 or a[1] != 25 or a[3] != 39 => bad = 1\nlet flags: [3 of bool] = [true, false, true]\nlet j: isize = 1\n" +
            "if not flags[0] or flags[j] or not flags[i] => bad = 1\nlet zeros: [3 of i64] = [3 of 0]\nif zeros[i] != 0 => bad = 1\n" +
            "unsafe\n    var b: [2 of i32] = noinit\n    b[j] = 5\n    b[0] = b[j] + 1\n    if b[0] != 6 => bad = 1\nlet check = 1 / (1 - bad)\n",
            string.Empty
        },
        {
            "PlaceDeref",
            "var x: i32 = 1\nlet r = x@uniq\nr@follow = 41\nvar bad: i32 = 0\nif x != 41 => bad = 1\nx += 1\nlet s = x@ref\n" +
            "if s@follow != 42 => bad = 1\nvar flag = false\nlet f = flag@uniq\nf@follow = true\nif not flag => bad = 1\nlet check = 1 / (1 - bad)\n",
            string.Empty
        },
        {
            "PlaceIndexAbort",
            "let a: [3 of i32] = [1, 2, 3]\nlet i: isize = 3\nlet x = a[i]\n",
            "Hello.kimi:3:9: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n"
        },
        {
            "PlaceNegativeAbort",
            "let a: [3 of i32] = [1, 2, 3]\nlet i: isize = -1\nlet x = a[i]\n",
            "Hello.kimi:3:9: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n"
        },
        {
            "PlaceUnsignedAbort",
            "let a: [3 of i32] = [1, 2, 3]\nlet n: u64 = 18446744073709551615\nlet x = a[n]\n",
            "Hello.kimi:3:9: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n"
        },
        { "PlaceWriteAbort", "var a: [2 of u8] = [1, 2]\nlet i: isize = 2\na[i] = 0\n", "Hello.kimi:3:1: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n" },
        { "PlaceUpdateAbort", "var a: [2 of u8] = [1, 2]\nlet i: isize = 5\na[i] += 1\n", "Hello.kimi:3:1: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n" },
        {
            "PlaceOrderAbort",
            "var a: [2 of u8] = [1, 2]\nlet x: u8 = 255\nlet i: isize = 9\na[i] = x + 1\n",
            "Hello.kimi:4:8: abort KIMI_E_INT_OVERFLOW: Integer overflow\n"
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
    [MemberData(nameof(Casts))]
    public void CastProgramsRun(string name, string source, string stderr)
        => ScalarEmissionTest.EmitFixture(name, source, string.Empty, stderr.Length == 0 ? 0 : 1, stderr);

    [Theory]
    [MemberData(nameof(Hellos))]
    public void HelloProgramsRun(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture(name, source, stdout);

    [Theory]
    [MemberData(nameof(PlacePrograms))]
    public void PlaceProgramsRun(string name, string source, string stderr)
        => ScalarEmissionTest.EmitFixture(name, source, string.Empty, stderr.Length == 0 ? 0 : 1, stderr);

    // The console writeLine is a compiler function's legacy runtime row (RuntimeAbi.WriteLine): the routine is called with the string borrow
    // loaded from its local and the call site's location text (SPEC 22.5.1), a header constant. A string constant is stored whole into its
    // local, its bytes a header constant and its release kind Static (IMPL 21.1.3); the empty string has no bytes.
    [Fact]
    public void WriteLineCallsTheConsoleRuntimeRow()
    {
        var text = Generate(Analyze(Strings, OutputKind.Library));
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

    // DECISIONS R5-M78, R4-M6a: a Ref stores its local's address; a Keep Drop calls the dropped Type's glue once, on the Place's address, and
    // an Elide Drop nothing, so each string a path initializes is destroyed exactly once and the one never initialized is not; the string's
    // glue item calls the runtime release with its own parameters (PD18).
    [Fact]
    public void EachInitializedStringIsDestroyedOnce()
    {
        var ir = GeneratedIr.Parse(Generate(Drops));
        var body = ir.Startup;
        var strings = body.Instructions.Where(static x => x.Shape == "alloca %kimi.string, align 8").ToArray();
        var literals = body.Instructions.Where(static x => x.Opcode == "store" && x.Shape.StartsWith("store %kimi.string", StringComparison.Ordinal))
            .Select(static x => Assert.Single(x.Operands)).ToArray();
        var borrows = body.Instructions.Where(static x => x.Shape == "store ptr %, ptr %, align 8").Select(static x => x.Operands[0]).ToArray();
        var writes = body.Instructions.Where(x => x.Opcode == "call" && x.Callee == ir.Function("__kimi_write_line")).ToArray();
        var drops = body.Instructions.Where(x => x.Opcode == "call" && x.Callee != ir.Function("__kimi_write_line")).ToArray();
        Assert.Equal(3, strings.Length);
        Assert.Equal(2, literals.Distinct().Count());
        Assert.Equal(literals, borrows);
        Assert.Equal(2, writes.Length);
        Assert.Equal(literals, drops.Select(static x => x.Operands[0]));
        Assert.All(drops, static x => Assert.Equal("call void @(ptr %, ptr @, i64 15)", x.Shape));
        var glue = Assert.Single(drops.Select(static x => x.Callee).Distinct())!;
        Assert.Equal(["call void @(ptr %, ptr %, i64 %)", "ret void"], glue.Instructions.Select(static x => x.Shape));
        Assert.Same(ir.Function("__kimi_destroy_string"), glue.Instructions[0].Callee);
        Assert.Equal(glue.Parameters, glue.Instructions[0].Operands);
    }

    // Gate step 2's stand-in (DECISIONS R5-M78): A2's verdicts are exact only for programs A1 accepts (R4-M6a), and under sel the old analysis
    // decides acceptance, so a program whose MIR analyses recorded an Ownership problem writes no Drop: each is one located Unsupported_Kd; a
    // control-flow problem (A3) does not gate. No program the old analysis accepts and the frame covers holds such a problem yet (ledger row
    // a1-whole-incomplete needs a fixed array, and the arena-skipped bodies are rejected first), so the problem is added to the version's
    // report.
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void AnOwnershipProblemOfTheProgramLeavesItsDropsUnwritten(bool ownership, bool written)
    {
        var c = Analyze("Console.writeLine(\"a\")\n");
        var program = c.BuildMir();
        var requirement = ownership ? DiagnosticRequirement.Ownership(OwnershipRequirement.PossiblyMovedUse) : DiagnosticRequirement.ControlFlow;
        program.Problems.Add(new(DiagnosticCode.MovedPlace_Kd, requirement, program.Bodies[0].Source, 0, 1));
        using var writer = new StringWriter();
        Assert.Equal(written, c.Generate(writer));
        Assert.Equal(written ? [] : [("Unsupported_Kd", (string?)"\"a\"")], Records(c));
    }

    // PD12: in a program the stand-in passes, A1 and A2 judged every runtime Drop, so one without a verdict group is a broken contract
    // (InternalInvariant_Kd at the Drop); the program's one drop row is moved to another block to make it so.
    [Fact]
    public void ARuntimeDropWithoutAVerdictIsABrokenContract()
    {
        var c = Analyze("Console.writeLine(\"a\")\n");
        var program = c.BuildMir();
        var body = program.Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn, Decl: null });
        var first = program.Bodies.ToArray().Take(body.Ordinal).Sum(x => program.Drops.RowsOf(x.Ordinal).Length);
        var row = Assert.Single(program.Drops.RowsOf(body.Ordinal).ToArray());
        program.Drops.Rows[first] = row with { Block = body.Blocks.Length };
        Assert.False(c.Generate(TextWriter.Null));
        Assert.Equal([("InternalInvariant_Kd", (string?)"\"a\"")], Records(c));
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
        var (ir, text) = Parse(source);
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
        var (ir, text) = Parse(source);
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
    // widened; not is xor with -1; Unit's one value is equal to itself. Floating point (R5 C1b, SPEC 13.3.1) checks nothing, a division by
    // zero included, negation is fneg, which keeps the sign of zero, and its comparisons are ordered fcmp predicates but for !=, which is
    // unordered (SPEC 13.4: with a NaN operand, != is true and the others false).
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
    [InlineData("func f(a: f64, b: f64) -> f64 => a + b", "internal double (double, double)", "fadd double %, %")]
    [InlineData("func f(a: f32, b: f32) -> f32 => a - b", "internal float (float, float)", "fsub float %, %")]
    [InlineData("func f(a: f64) -> f64 => a * 0.5", "internal double (double)", "fmul double %, 0x3FE0000000000000")]
    [InlineData("func f(a: f32, b: f32) -> f32 => a / b", "internal float (float, float)", "fdiv float %, %")]
    [InlineData("func f(a: f64) -> f64 => -a", "internal double (double)", "fneg double %")]
    [InlineData("func f(a: f64, b: f64) -> bool => a == b", "internal i1 (double, double)", "fcmp oeq double %, %")]
    [InlineData("func f(a: f32, b: f32) -> bool => a != b", "internal i1 (float, float)", "fcmp une float %, %")]
    [InlineData("func f(a: f64, b: f64) -> bool => a < b", "internal i1 (double, double)", "fcmp olt double %, %")]
    [InlineData("func f(a: f32) -> bool => a <= 1.5", "internal i1 (float)", "fcmp ole float %, 0x3FF8000000000000")]
    [InlineData("func f(a: f64, b: f64) -> bool => a > b", "internal i1 (double, double)", "fcmp ogt double %, %")]
    [InlineData("func f(a: f32, b: f32) -> bool => a >= b", "internal i1 (float, float)", "fcmp oge float %, %")]
    public void UncheckedOperatorsAreOneInstruction(string source, string signature, string shape)
    {
        var (ir, text) = Parse(source);
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

    // The conversion rows (R5 C1b; DECISIONS R5-M80; SPEC 13.5.4, 5.4-5.5, IMPL 21.3.3): a conversion that cannot fail (a wrapping, an
    // integer widening within the target's range, a floating widening, an integer to floating-point, a bit or an address conversion) is its
    // row's one cast of the loaded argument, by the width and the operand's signedness, and nothing Aborts; one that keeps the
    // representation (U and Wrapping<U>, raw pointer Types) stores the loaded argument itself.
    [Theory]
    [InlineData("func f(a: i32) -> i64 => a@i64", "internal i64 (i32)", "sext i32 % to i64")]
    [InlineData("func f(a: u8) -> i32 => a@i32", "internal i32 (i8)", "zext i8 % to i32")]
    [InlineData("func f(a: i32) -> u8 => a@wrap<u8>", "internal i8 (i32)", "trunc i32 % to i8")]
    [InlineData("func f(a: i8) -> u32 => a@wrap<u32>", "internal i32 (i8)", "sext i8 % to i32")]
    [InlineData("func f(a: Wrapping<u16>) -> i64 => a@wrap<i64>", "internal i64 (i16)", "zext i16 % to i64")]
    [InlineData("func f(a: f32) -> f64 => a@f64", "internal double (float)", "fpext float % to double")]
    [InlineData("func f(a: i64) -> f64 => a@f64", "internal double (i64)", "sitofp i64 % to double")]
    [InlineData("func f(a: u64) -> f32 => a@f32", "internal float (i64)", "uitofp i64 % to float")]
    [InlineData("func f(a: f32) -> u32 => a@bits<u32>", "internal i32 (float)", "bitcast float % to i32")]
    [InlineData("func f(a: Wrapping<i64>) -> f64 => a@bits<f64>", "internal double (i64)", "bitcast i64 % to double")]
    [InlineData("func f(p: raw/i32) -> usize => p@usize", "internal i64 (ptr)", "ptrtoint ptr % to i64")]
    [InlineData("func f(a: usize) -> raw/i32 => a@raw/i32", "internal ptr (i64)", "inttoptr i64 % to ptr")]
    [InlineData("func f(w: Wrapping<u32>) -> u32 => w@u32", "internal i32 (i32)", null)]
    [InlineData("func f(a: i64) -> Wrapping<i64> => a@Wrapping<i64>", "internal i64 (i64)", null)]
    [InlineData("func f(p: raw/i32) -> raw/u8 => p@raw/u8", "internal ptr (ptr)", null)]
    public void UncheckedConversionsAreTheirCast(string source, string signature, string? cast)
    {
        var (ir, _) = Parse(source);
        var f = ir.Single(signature);
        Assert.DoesNotContain(f.Instructions, x => x.Callee == ir.Function("__kimi_abort"));
        var stored = Assert.Single(f.Blocks[1].Instructions, static x => x.Opcode == "store").Operands[0];
        var input = cast is null ? stored : Assert.Single(stored.Operands);
        Assert.Equal((cast ?? input.Shape, "load"), (stored.Shape, input.Opcode));
        Assert.Contains(f.Blocks[0].Instructions, x => x.Opcode == "store" && x.Operands.SequenceEqual([f.Parameters[0], input.Operands[0]]));
    }

    // SPEC 13.5.4, 22.5.2 (R5 C1b; DECISIONS R5-M80): a conversion that can fail tests its loaded operand, in the source Type, against each
    // side of the target's range the source exceeds: an integer against the target's extremes (KIMI_E_INT_CONVERSION, 9); a floating-point
    // value against the bounds its truncation toward zero must lie within, the minimum's predecessor where the significand holds it, with
    // unordered predicates, so NaN and the infinities fail (9); a narrowing to f32 against the overflow threshold, ordered, and a finite input
    // only, so NaN and the infinities convert (KIMI_E_FLOAT_CONVERSION, 11). The failure Aborts at the conversion's location text; the success
    // block converts, or stores the operand where the representation is kept.
    [Theory]
    [InlineData("func f(a: i32) -> u8 => a@u8", "internal i8 (i32)", "trunc i32 % to i8", "icmp slt i32 %, 0", "icmp sgt i32 %, 255", 9)]
    [InlineData("func f(a: u64) -> i64 => a@i64", "internal i64 (i64)", null, null, "icmp ugt i64 %, 9223372036854775807", 9)]
    [InlineData("func f(a: i8) -> u16 => a@u16", "internal i16 (i8)", "sext i8 % to i16", "icmp slt i8 %, 0", null, 9)]
    [InlineData("func f(a: i128) -> u64 => a@u64", "internal i64 (i128)", "trunc i128 % to i64", "icmp slt i128 %, 0", "icmp sgt i128 %, 18446744073709551615", 9)]
    [InlineData("func f(a: u128) -> i128 => a@i128", "internal i128 (i128)", null, null, "icmp ugt i128 %, 170141183460469231731687303715884105727", 9)]
    [InlineData("func f(a: f64) -> i32 => a@i32", "internal i32 (double)", "fptosi double % to i32", "fcmp ule double %, 0xC1E0000000200000", "fcmp uge double %, 0x41E0000000000000", 9)]
    [InlineData("func f(a: f32) -> i32 => a@i32", "internal i32 (float)", "fptosi float % to i32", "fcmp ult float %, 0xC1E0000000000000", "fcmp uge float %, 0x41E0000000000000", 9)]
    [InlineData("func f(a: f64) -> i64 => a@i64", "internal i64 (double)", "fptosi double % to i64", "fcmp ult double %, 0xC3E0000000000000", "fcmp uge double %, 0x43E0000000000000", 9)]
    [InlineData("func f(a: f32) -> u8 => a@u8", "internal i8 (float)", "fptoui float % to i8", "fcmp ule float %, 0xBFF0000000000000", "fcmp uge float %, 0x4070000000000000", 9)]
    [InlineData("func f(a: f64) -> u64 => a@u64", "internal i64 (double)", "fptoui double % to i64", "fcmp ule double %, 0xBFF0000000000000", "fcmp uge double %, 0x43F0000000000000", 9)]
    [InlineData("func f(a: f64) -> f32 => a@f32", "internal float (double)", "fptrunc double % to float", "fcmp ole double %, 0xC7EFFFFFF0000000", "fcmp oge double %, 0x47EFFFFFF0000000", 11)]
    public void CheckedConversionsAbortOutsideTheirTarget(string source, string signature, string? cast, string? lower, string? upper, int reason)
    {
        var (ir, text) = Parse(source);
        var f = ir.Single(signature);
        var check = Assert.Single(f.Blocks, static x => x.Successors.Count == 2);
        var input = Assert.Single(check.Instructions, static x => x.Opcode == "load");
        string?[] tests = reason == AbortReasons.FloatingConversion
            ? [lower, upper, "fcmp one double %, 0x7FF0000000000000", "fcmp one double %, 0xFFF0000000000000"] : [lower, upper];
        Assert.Equal(
            tests.OfType<string>().Order(StringComparer.Ordinal),
            check.Instructions.Where(static x => x.Opcode is "icmp" or "fcmp").Select(static x => x.Shape).Order(StringComparer.Ordinal));
        Assert.All(check.Instructions.Where(static x => x.Opcode is "icmp" or "fcmp"), x => Assert.Equal([input], x.Operands));
        AssertAbort(ir, text, check.Successors[0], reason, source, source[(source.IndexOf("=> ", StringComparison.Ordinal) + 3)..]);
        var stored = Assert.Single(check.Successors[1].Instructions, static x => x.Opcode == "store").Operands[0];
        Assert.Same(input, cast is null ? stored : Assert.Single(stored.Operands));
        Assert.Equal(cast ?? input.Shape, stored.Shape);
    }

    // DECISIONS R5-M80: an instance of a generic conversion (SPEC 8.7.4), whose check the builder cannot prove, converts its closed Types:
    // where the target holds every source value, its Range has no side and only the cast is written; where not, the instance checks. The
    // instances are written directly, since the calls naming them are C3a's row.
    [Fact]
    public void AGenericConversionChecksItsInstancesRange()
    {
        var source = "func widen<T>(value: T) -> i64\n    T is PrimitiveInteger\n    return value@i64\nlet a = widen(5@i32)\nlet b = widen(5@u64)\n";
        var texts = ItemTexts(Analyze(source), "widen");
        Assert.Equal(2, texts.Length);
        Assert.Single(texts, static x => x.Contains("sext i32 %t0 to i64\n", StringComparison.Ordinal) && !x.Contains("__kimi_abort", StringComparison.Ordinal));
        Assert.Single(texts, static x => x.Contains("icmp ugt i64 %t0, 9223372036854775807\n", StringComparison.Ordinal) && x.Contains("__kimi_abort", StringComparison.Ordinal));
    }

    // The place writer (R5 C2a; DECISIONS R5-M83, package C 3.2 I7 and I9): a Place's address is its root's home, then each step at the offset
    // the prefix's Layout gives (IMPL 21.1): a Tuple element in Kimigayo order (the i64, then the i16 at 8), a Base at its derived struct's
    // own address (X1) and a Field after the base, an enum payload after its i32 tag (8, the i64's alignment) and its second element at 0, a
    // reference's referent at the address it stores, a constant element at its stride's multiple without a check; an Assign's value is
    // computed before its Place is located (SPEC 13.7.1), and a fixed array is stored element by element. The items are written directly:
    // the base call and the match's Discriminant are other rows (C3a, C2b). No inbounds (I9).
    [Theory]
    [InlineData(Places, "k", "%t14 = load ptr, ptr %l1, align 8\n  %t15 = getelementptr i8, ptr %t14, i64 8\n  %t16 = load i16, ptr %t15, align 2\n")]
    [InlineData(Layouts, "counted", "%t0 = load ptr, ptr %l1, align 8\n  %t1 = load i32, ptr %t0, align 4\n")]
    [InlineData(Layouts, "extra", "%t0 = load ptr, ptr %l1, align 8\n  %t1 = getelementptr i8, ptr %t0, i64 4\n  %t2 = load i32, ptr %t1, align 4\n")]
    [InlineData(Layouts, "payload", "%t2 = load ptr, ptr %l3, align 8\n  %t3 = getelementptr i8, ptr %t2, i64 8\n  store ptr %t3, ptr %l5, align 8\n")]
    [InlineData(Places, "g", "%t0 = getelementptr i8, ptr %a0, i64 16\n  %t1 = load i64, ptr %t0, align 8\n")]
    [InlineData(Places, "h", "%t0 = load i32, ptr %l3, align 4\n  %t1 = load ptr, ptr %l1, align 8\n  store i32 %t0, ptr %t1, align 4\n")]
    [InlineData(Places, "m", "store i16 1, ptr %l1, align 2\n  %t0 = getelementptr i8, ptr %l1, i64 2\n  store i16 2, ptr %t0, align 2\n")]
    public void APlaceIsAddressedStepByStep(string source, string name, string text)
    {
        var item = Assert.Single(ItemTexts(Analyze(source, OutputKind.Library), name));
        Assert.Contains(text, item);
        Assert.DoesNotContain("inbounds", item);
    }

    // SPEC 4.7.1-4.7.4, 22.5.1-22.5.2 (R5 C2a; DECISIONS R5-M83): an Index checks its isize position, unsigned, so a negative one fails too,
    // against the length: a fixed array's, or the length word of an Array's or Slice's handle, beside its buffer word. Its failure Aborts
    // with KIMI_E_INDEX_BOUNDS (10) at the selection's location text; its success block addresses the element at the position times the
    // stride from the array's or the buffer's address.
    [Theory]
    [InlineData("func f(a: ref/[3 of i32], i: isize) -> i32 => a[i]", "icmp uge i64 %, 3", "mul i64 %, 4", false)]
    [InlineData("func f(a: ref/Array<i64>, i: isize) -> i64 => a[i]", "icmp uge i64 %, %", "mul i64 %, 8", true)]
    [InlineData("func f(s: Slice<u16>, i: isize) -> u16 => s[i]", "icmp uge i64 %, %", "mul i64 %, 2", true)]
    public void AnIndexChecksItsPositionAgainstTheLength(string source, string condition, string scale, bool handle)
    {
        var (ir, text) = Parse(source);
        var f = Assert.Single(ir.Functions, x => x.Instructions.Any(y => y.Shape == condition));
        var compare = Assert.Single(f.Instructions, x => x.Shape == condition);
        var check = Assert.Single(f.Blocks, x => x.Instructions.Contains(compare));
        Assert.Equal(("br i1 %, label %, label %", compare), (check.Terminator.Shape, Assert.Single(check.Terminator.Operands)));
        AssertAbort(ir, text, check.Successors[0], AbortReasons.IndexBounds, source, source[(source.IndexOf("=> ", StringComparison.Ordinal) + 3)..]);
        var element = Assert.Single(check.Successors[1].Instructions, static x => x.Shape == "getelementptr i8, ptr %, i64 %");
        Assert.Equal((scale, compare.Operands[0]), (element.Operands[1].Shape, element.Operands[1].Operands[0]));
        if (handle)
        {
            var length = compare.Operands[1];
            Assert.Equal(("load i64, ptr %, align 8", "getelementptr i8, ptr %, i64 8"), (length.Shape, length.Operands[0].Shape));
            Assert.Equal(("load ptr, ptr %, align 8", length.Operands[0].Operands[0]), (element.Operands[0].Shape, element.Operands[0].Operands[0]));
        }

        Assert.DoesNotContain("inbounds", text);
    }

    // X13, SPEC 13.5.5: a PairLayer step is decided by the item's view: in the owner case the Place itself, so the write stores into the
    // binding's own slot; in the uniq case the referent of the reference that slot holds.
    [Fact]
    public void APairLayerFollowsTheItemsView()
    {
        var texts = ItemTexts(Analyze(PairLayers), "keep");
        Assert.Equal(2, texts.Length);
        Assert.Single(texts, static x => x.Contains("%t2 = load i32, ptr %l5, align 4\n  store i32 %t2, ptr %l3, align 4\n", StringComparison.Ordinal));
        var referent = "%t2 = load i32, ptr %l5, align 4\n  %t3 = load ptr, ptr %l3, align 8\n  store i32 %t2, ptr %t3, align 4\n";
        Assert.Single(texts, x => x.Contains(referent, StringComparison.Ordinal));
    }

    // A floating constant is LLVM's hexadecimal double (R5 C1b; DECISIONS R5-M80): an f32's value widened exactly, so the sign of a zero, a
    // subnormal and a NaN's sign, payload and signaling bit are kept (SPEC 13.5.4's bit conversion).
    [Theory]
    [InlineData("func f() -> f64 => -0.0", "store double 0x8000000000000000, ptr %, align 8")]
    [InlineData("func f() -> f32 => 1.5", "store float 0x3FF8000000000000, ptr %, align 4")]
    [InlineData("func f() -> f32 => 1@bits<f32>", "store float 0x36A0000000000000, ptr %, align 4")]
    [InlineData("func f() -> f32 => 2139095040@bits<f32>", "store float 0x7FF0000000000000, ptr %, align 4")]
    [InlineData("func f() -> f32 => 2139095041@bits<f32>", "store float 0x7FF0000020000000, ptr %, align 4")]
    [InlineData("func f() -> f32 => 4290772993@bits<f32>", "store float 0xFFF8000020000000, ptr %, align 4")]
    [InlineData("func f() -> f32 => 4286578689@bits<f32>", "store float 0xFFF0000020000000, ptr %, align 4")]
    [InlineData("func f() -> f64 => 9218868437227405313@bits<f64>", "store double 0x7FF0000000000001, ptr %, align 8")]
    public void FloatingConstantsAreHexadecimalDoubles(string source, string store)
        => Assert.Single(Parse(source).Ir.Functions.SelectMany(static x => x.Instructions), x => x.Shape == store);

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
        var source = "func f(flag: bool, n: i64, text: ref/string, unit: ()) -> bool => flag\nfunc g(pair: (i32, i64)) => ()\n";
        var text = Generate(Analyze(source, OutputKind.Library));
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
        var c = Analyze("func f(flag: bool) -> i32\n    if flag\n        return 1\n    return 2\n", OutputKind.Library);
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
    // its valid counterpart is. A string's Move into another local copies a slot Type (C2b's row), at the source; a callee's call is C3a's,
    // at that call, while a generic callee's instance whose Keep of a T instanced at i32 needs no drop records nothing (R5-M78); the
    // Utf8Slice overload of writeLine is another legacy row (C9a), at its call; the records of a for loop's statement and of the siblings
    // its expansion synthesizes with its span ($for.iterator, $for.some, $for.none) are one at that location (DECISIONS R5-M65), which the
    // diagnostics could not order when each was keyed by its node. A static's initializer is C7b's row, at the static. The operator rows
    // (DECISIONS R5-M76) leave raw pointers to C8a and checked 128-bit arithmetic to ModuleCodegen's wide overflow declarations; the conversion
    // rows (DECISIONS R5-M80) leave an erasure to C5b, at its Cast, and a raw pointer's null to C8a, at the Cast that reads it; the place
    // writer (DECISIONS R5-M83) leaves a Tuple aggregate to C2b, at the literal, as it does a zero-sized local's address, at its borrow, and an
    // object borrow's payload to C6a, at the Place read through it. K11: an Application's function nothing calls is not generated, so its form
    // records nothing; a Library generates it.
    [Theory]
    [InlineData("func twice(a: i128) -> i128 => a + a\n", OutputKind.Library, new[] { "a + a" })]
    [InlineData("func twice(a: i128) -> i128 => a + a\n()", OutputKind.Application, new string[0])]
    [InlineData("func twice(a: f64) -> f64 => a + a\n", OutputKind.Library, new string[0])]
    [InlineData("func id(x: i32) -> i32 => x\nfunc get() -> (i32) -> i32 => id\n", OutputKind.Library, new[] { "id" })]
    [InlineData("func none() -> usize => null@raw/i32@usize\n", OutputKind.Library, new[] { "null@raw/i32" })]
    [InlineData("func same(p: raw/i32, q: raw/i32) -> bool => p == q\n", OutputKind.Library, new[] { "p == q" })]
    [InlineData("let t = (1, 2)\n", OutputKind.Application, new[] { "(1, 2)" })]
    [InlineData("let u = ()\nlet r = u@ref\n", OutputKind.Application, new[] { "u@ref" })]
    [InlineData("struct Item\n    public var id: i32 = 7\nfunc take(v: objref/Item) -> i32 => v.id\n", OutputKind.Library, new[] { "v.id" })]
    [InlineData("func twice(a: Wrapping<i128>) -> Wrapping<i128> => a + a\n", OutputKind.Library, new string[0])]
    [InlineData("func twice(a: i32) -> i32 => a\nlet x = twice(1)\n", OutputKind.Application, new[] { "twice(1)" })]
    [InlineData("let t = \"x\"\nlet u = t@move\n", OutputKind.Application, new[] { "t" })]
    [InlineData("func keep<T>(value: T) => ()\nkeep(1)\n", OutputKind.Application, new[] { "keep(1)" })]
    [InlineData("func name() -> string => \"x\"\nfunc show() => Console.writeLine(name())\n", OutputKind.Library, new[] { "name()" })]
    [InlineData("func show(text: Text.Utf8Slice) => Console.writeLine(text)\n", OutputKind.Library, new[] { "Console.writeLine(text)" })]
    [InlineData(ForLoop, OutputKind.Library, new[] { "for value in values\n        total = 1" })]
    [InlineData("group Registry\n    public let limit: i32 = 4\n", OutputKind.Library, new[] { "let limit: i32 = 4" })]
    [InlineData("func twice(a: i32) -> i32 => a\n", OutputKind.Library, new string[0])]
    [InlineData(Signatures, OutputKind.Library, new string[0])]
    public void AnUncoveredFormIsOneLocatedUnsupportedRecord(string source, OutputKind output, string[] sites)
    {
        var c = Analyze(source, output);
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
    [InlineData(Branches, OutputKind.Application)]
    [InlineData(Strings, OutputKind.Library)]
    [InlineData(Arithmetic, OutputKind.Library)]
    [InlineData(Conversions, OutputKind.Library)]
    [InlineData(Drops, OutputKind.Application)]
    [InlineData(Places, OutputKind.Library)]
    public void GenerationIsDeterministic(string source, OutputKind output)
    {
        var first = Generate(Analyze(source, output));
        var c = Analyze(source, output);
        Assert.Equal(first, Generate(c));
        Assert.Equal(first, Generate(c));
    }

    // K11 (K package 4.10, DECISIONS R5-M77): only the Generated items are written, the closure of the output roots over runtime targets: an
    // Application's startup body without the function nothing calls, a Library's every root. A request for an item outside Generated,
    // here writing the unused body, is a broken contract at the item's site.
    [Fact]
    public void OnlyTheGeneratedItemsAreWritten()
    {
        var c = Analyze(Unused + "let x = 1\n");
        var text = Generate(c);
        Assert.Equal([(null, true), ("unused", false)], Generated(c.Instances!));
        Assert.Single(Regex.Matches(text, @"define internal \S+ @__kimi_f\d+\("));
        Assert.DoesNotContain(GeneratedIr.Parse(text).Functions, static x => x.Signature == "internal i64 (i64)");
        Assert.Single(GeneratedIr.Parse(Generate(Analyze(Unused, OutputKind.Library))).Functions, static x => x.Signature == "internal i64 (i64)");

        var program = c.BuildMir();
        var collector = new InstanceCollector(c.Types, c.Layout);
        var set = collector.Run(program, 1);
        var (start, unused) = (Of(set, null), Of(set, "unused"));
        set.Mark([start]);
        var module = new ModuleCodegen(c.Layout, c.Locations, c.Types);
        Assert.True(module.Write(program, collector, c.Binding.Startup, [start], TextWriter.Null));
        new FunctionCodegen(module, c.Types).Write(program, collector, unused, TextWriter.Null);
        Assert.Equal([new(DiagnosticCode.InternalInvariant_Kd, set.Items[unused].Site)], module.Records);

        static int Of(CheckedSet set, string? name) => Enumerable.Range(0, set.Items.Length).Single(x => set.Items[x].Key.Body.Def.Decl?.Name == name);
    }

    // K11 with R4-M28 (K package 4.4, 4.10): a Library generates every root but the body of a closed override whose slot TyCtxt.Applies
    // refutes at its owning layer, which is checked only (the old generator skips it too); where the slot applies, the override is generated.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ALibraryLeavesOutTheOverrideOfARefutedSlot(bool applies)
    {
        var c = Analyze(
            "contract Marker\nstruct Thing\n" + (applies ? "    Self is Marker\n" : string.Empty) + "    public init() => ()\nopen struct Base<T>\n" +
            "    public init() => ()\n    public virtual func mark(self: objref/Self) -> i32\n        T is Marker\n        return 5\n" +
            "struct Plain : Base<Thing>\n    public init() => ()\n    override func mark(self: objref/Self) -> i32 => 7\n",
            OutputKind.Library);
        Assert.True(c.Accepts, MinimalEmissionTest.Describe(c, null));
        _ = c.Generate(TextWriter.Null);
        Assert.Equal(applies, Assert.Single(Generated(c.Instances!), static x => x.Name == "mark").Generated);
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
    [InlineData(Branches, OutputKind.Application)]
    [InlineData(Strings, OutputKind.Library)]
    [InlineData(Arithmetic, OutputKind.Library)]
    [InlineData(Conversions, OutputKind.Library)]
    [InlineData(Drops, OutputKind.Application)]
    [InlineData(Places, OutputKind.Library)]
    public void WarmGenerationAllocatesNothing(string source, OutputKind output)
    {
        var c = Analyze(source, output);
        var written = true;
        var allocated = AllocationMeasurement.Measure(() => written &= c.Generate(TextWriter.Null));
        Assert.True(written);
        Assert.Equal(0, allocated);
    }

    // The library bodies an Application reaches (K12, DECISIONS R4-M13a, R5-M77), where generation decides them: the program analyzed under
    // MIR code generation, whatever the process's generators, and the definitions of the body items its generation marks Generated. The
    // mark precedes writing, so a coverage row not written yet (Unsupported_Kd) leaves it whole; any other record fails.
    internal static (Compilation C, Koto[] Definitions) GeneratedDefinitions(string source)
    {
        var c = Analyze(source);
        Assert.True(c.Accepts, MinimalEmissionTest.Describe(c, null));
        if (!c.Generate(TextWriter.Null))
        {
            Assert.All(Records(c), static x => Assert.Equal("Unsupported_Kd", x.Code));
        }

        var set = c.Instances!;
        var generated = Enumerable.Range(0, set.Items.Length).Where(set.IsGenerated).ToArray();
        Assert.NotEmpty(generated); // The startup body at least: the mark was made.
        return (c, [.. generated.Where(x => set.Items[x].Key.Kind == MonoKind.Body).Select(x => set.Items[x].Key.Body.Def.Decl?.Declaration).OfType<Koto>()]);
    }

    // A program analyzed under MIR code generation (sel) for an output form.
    private static Compilation Analyze(string source, OutputKind output = OutputKind.Application)
    {
        var c = Compilation.CreateForTest();
        c.Pipeline = CompilerPipeline.Mir;
        c.Codegen = CompilerCodegen.Mir;
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", source));
        c.Bind();
        c.Binding.CheckStartup(output);
        c.Analyze();
        return c;
    }

    private static string Generate(string source) => Generate(Analyze(source));

    // The definitions of a definition's body items (null: the top-level statements), each written directly whatever records the module's
    // other items make: an instance a call names (the call is C3a's row) or a body beside a form not written yet. Every body item is an
    // output root.
    private static string[] ItemTexts(Compilation c, string? name)
    {
        var program = c.BuildMir();
        var collector = new InstanceCollector(c.Types, c.Layout);
        var set = collector.Run(program, 1);
        var bodies = Enumerable.Range(0, set.Items.Length).Where(x => set.Items[x].Key.Kind == MonoKind.Body).ToArray();
        set.Mark(bodies);
        var module = new ModuleCodegen(c.Layout, c.Locations, c.Types);
        _ = module.Write(program, collector, c.Binding.Startup, bodies, TextWriter.Null);
        return [.. bodies.Where(x => set.Items[x].Key.Body.Def.Decl?.Name == name).Select(x =>
        {
            using var writer = new StringWriter();
            new FunctionCodegen(module, c.Types).Write(program, collector, x, writer);
            return writer.ToString();
        })];
    }

    // A Library program's IR and text: a Library generates every root (K11).
    private static (GeneratedIr Ir, string Text) Parse(string source)
    {
        var text = Generate(Analyze(source, OutputKind.Library));
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

    // Each body item's definition name (null: top-level statements) and Generated mark, by name.
    private static (string? Name, bool Generated)[] Generated(CheckedSet set)
        => [.. Enumerable.Range(0, set.Items.Length).Select(x => (set.Items[x].Key.Body.Def.Decl?.Name, set.IsGenerated(x)))
            .OrderBy(static x => x.Name, StringComparer.Ordinal)];

    private static (string Code, string? Text)[] Records(Compilation c)
        => [.. TestDiagnostics.Of(c).Where(static x => x.Severity == DiagnosticSeverity.Error).Select(static x => (x.Code, x.Text))];
}
