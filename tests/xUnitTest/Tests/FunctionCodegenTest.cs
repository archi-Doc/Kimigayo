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

/// <summary>The frame of MIR code generation (R5 C0b1, K11, C2a, X2; package C 3.2 and 3.3; DECISIONS R5-M65, R5-M77, R5-M83, R5-M146):
/// <c>ModuleCodegen</c> writes the Generated items of the checked set and the output form, <c>FunctionCodegen</c> a body item's prologue,
/// slots, runtime-reached blocks, the Places of its place writer and every form of an accepted program, and a broken input is one
/// <c>InternalInvariant_Kd</c> with no module.</summary>
public sealed partial class FunctionCodegenTest
{
    private const string Branches =
        "func choose(flag: bool, a: i64, b: i64) -> i64\n    if flag => return a\n    return b\nfunc nested(first: bool, second: bool) -> char\n" +
        "    if first\n        if second => return 'a'\n        return 'b'\n    return 'c'\nlet flag = false\nlet other = true\n" +
        "let x: i32 = if flag => 1 else => 2\nlet y: char = if flag => 'x' else if other => 'y' else => 'z'\n";

    // Library programs (K11: a Library generates every root, an Application only what its startup body reaches).
    private const string ForLoop =
        "func f(values: ref/Array<i32>) -> i32\n    var total: i32 = 0\n    for value in values\n        total = 1\n    return total\n";

    private const string DictionaryLoop =
        "func f(values: ref/Dictionary<i32, i32>) -> i32\n    var total: i32 = 0\n    for entry in values\n        total = 1\n    return total\n";

    private const string Strings =
        "func show(text: ref/string) => Console.writeLine(text)\nfunc greeting() -> string => \"Hi\"\nfunc nothing(flag: bool) -> string => \"\"\n";

    private const string Arithmetic =
        "func f(a: i32, b: i32) -> i32 => a / b + a % b - (a << b) * 3\nfunc g(a: Wrapping<u8>, c: char) -> bool => -a > 3 and not (c < 'z')\n";

    private const string Conversions =
        "func f(a: f64, b: i32) -> i64 => (a * b@f64)@i64\nfunc g(a: u64, c: f32) -> bool => a@f32 < -c and a@wrap<u8>@f64@f32 != 0.5\n";

    private const string Unused = "func unused(n: i64) -> i64 => n\n";

    // SPEC 13.4, 5.1 (DECISIONS R5-M143): raw pointers compared through a ref layer in both orders and an i32 compared through one, each
    // answer checked by the division, run under MIR code generation in every run (the old generator refuses the raw forms, GenerationFailed_Kd).
    private const string LayeredComparisons =
        "func same(p: ref/raw/i32, q: raw/i32) -> bool => p == q\nfunc differ(p: raw/i32, q: ref/raw/i32) -> bool => p != q\n" +
        "func near(a: ref/i32, b: i32) -> bool => a == b\nfunc far(a: i32, b: ref/i32) -> bool => a != b\n" +
        "var x: i32 = 1\nlet n = x@raw\nlet m: raw/i32 = null\nvar bad: i32 = 0\n" +
        "if not same(n@ref, n) or same(m@ref, n) or differ(n, n@ref) or not differ(m, n@ref) => bad = 1\n" +
        "if not near(x@ref, 1) or near(x@ref, 2) or far(1, x@ref) or not far(2, x@ref) => bad = 1\nlet check = 1 / (1 - bad)\n";

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

    // The rvalue rows (R5 C2b; DECISIONS R5-M87): values in memory copied, a string moved, an enum Case built and matched, a length, a
    // Fill of a value in memory and a zero-sized borrow.
    private const string Rvalues =
        "enum Shape\n    Circle(i32)\n    Rect(i32, i64)\n    Empty\nfunc copy(t: (i32, i64)) -> (i32, i64) => t\nfunc text(x: string) -> string => x@move\n" +
        "func rect(a: i32, b: i64) -> Shape => .Rect(a, b)\nfunc area(s: ref/Shape) -> i64\n    return match s\n        .Rect(let w, let h) => w@i64 * h\n" +
        "        _ => 0\nfunc count(a: ref/Array<i64>) -> isize => a.length\nfunc fill(p: (i32, i64)) -> [3 of (i32, i64)] => [3 of p]\n" +
        "func unit() -> i32\n    let u = ()\n    let r = u@ref\n    return 1\n";

    // A pair binder's acquisition (tests/mir/cases.kimi's keep) and a case adaptation, each in its owner and its other case (SPEC 8.14,
    // 8.15), instanced by the calls.
    private const string Acquisitions =
        "func keep<s/T>(value: s/T, x: T) -> T\n    s is owner or uniq\n    T is Copy\n    var v = value@move\n    let local = v\n    _ = local\n" +
        "    v@follow = x\n    return v@follow\nfunc owned() -> i32 => keep(5, 2)\nfunc lent() -> i32\n    var y: i32 = 1\n    return keep(y@uniq, 2)\n";

    private const string Adaptations =
        "func put<s/T>(value: s/T, x: i32) -> ()\n    s is owner or ref\n    T is Copy\n    _ = x@s\nfunc owned() => put(5, 1)\nfunc lent()\n" +
        "    var y: i32 = 1\n    put(y@ref, 1)\n";

    // A case adaptation whose one case creates an object (SPEC 8.14, 13.5.3), instanced by the call.
    private const string Creations =
        "func put<s/T>(value: s/T, x: i32) -> ()\n    s is obj\n    _ = x@s\nfunc lent()\n    let o = 5@obj\n    put(o@move, 1)\n";

    // The call rows (R5 C3a; DECISIONS R5-M89): a scalar and a bool argument and result, a string borrow, a zero-sized parameter, a slot
    // result, a string moved and a Tuple copied by pointer, a diverging callee, a generic instance and a recursive default's template.
    private const string Calls =
        "func id(x: i32) -> i32 => x\nfunc f(a: i32) -> i32 => id(a)\nfunc flip(b: bool) -> bool => not b\nfunc g(c: bool) -> bool => flip(c)\n" +
        "func show(t: ref/string) => Console.writeLine(t)\nfunc call(t: ref/string) => show(t)\nfunc pair(a: i32) -> (i32, i64) => (a, 2)\n" +
        "func second() -> i64 => pair(1).1\nfunc take(s: string) => ()\nfunc give()\n    let t = \"x\"\n    take(t@move)\n" +
        "func use(p: (i32, i64)) -> i64 => p.1\nfunc copy(q: (i32, i64)) -> i64 => use(q)\nfunc unit(x: (), y: i32) -> i32 => y\n" +
        "func callUnit() -> i32 => unit((), 3)\nfunc stop() -> Never\n    loop => ()\nfunc diverge(flag: bool) -> i32\n    if flag => stop()\n" +
        "    return 1\nfunc keep<T>(value: T) -> T => value@move\nfunc kept() -> i32 => keep(1)\n" +
        "func count(n: i32, m: i32 = if n <= 0 => 0 else => count(n - 1) + 1) -> i32 => m\nfunc counted() -> i32 => count(3)\n";

    // The raw memory and failure rows (R5 C8a; DECISIONS R5-M92): Raw's storage operations, raw pointer displacement and equality, null and
    // $abort.
    private const string Raws =
        "func make(n: isize) -> raw/i64 => Raw.allocate<i64>(n)\nfunc put(p: raw/i64, v: i64)\n    unsafe => Raw.initialize(p, v)\n" +
        "func view() -> Slice<i64>\n    unsafe => return Raw.slice(null@raw/i64, 0)\nfunc free(p: raw/i64)\n    unsafe => Raw.release(p)\n" +
        "func next(p: raw/i64, n: isize) -> raw/i64\n    var q: raw/i64 = null\n    unsafe => q = p - n\n    return q\n" +
        "func empty(p: raw/i64) -> bool => p == null\nfunc stop() => $abort(\"stop\")\n";

    // A #LibraryImport call (SPEC 22.6; DECISIONS R5-M89), its C signature memoized per Write.
    private const string Imports = "group Native\n    #LibraryImport(\"kernel32\", \"GetLastError\")\n    public unsafe func lastError() -> u32\n" +
        "func last() -> u32\n    var e: u32 = 0\n    unsafe => e = Native.lastError()\n    return e\n";

    // Programs of the frame's forms only: scalar locals and constants, branches, loops and labels and an explicit startup, which calls
    // functions with scalar, string-borrow, zero-sized and Never signatures (an Application generates only what its startup body reaches, K11).
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
            "func stop() -> Never\n    loop\n        let x = 1\npublic func main()\n    var running = true\n    while running\n        running = false\n" +
            "    let text = \"frame\"\n    if running => stop()\n    let check = 1 / (identity(true, 1, text@ref, ()) - identity(false, 1, text@ref, ()))\n"
        },
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
            "PlaceFill",
            "let x: i32 = 7\nlet five: [5 of i32] = [5 of x]\nlet none: [0 of i32] = [0 of x]\nlet i: isize = 4\nvar bad: i32 = 0\n" +
            "if five[0] != 7 or five[i] != 7 => bad = 1\nlet bytes: [64 of u8] = [64 of 255]\nif bytes[63] != 255 => bad = 1\n" +
            "let check = 1 / (1 - bad)\n",
            string.Empty
        },
        {
            "PlaceWidePosition",
            "let a: [4 of i32] = [10, 20, 30, 40]\nlet b: [2 of u64] = [1, 3]\nlet c: [2 of i128] = [2, 0]\nlet i: isize = 1\nvar bad: i32 = 0\n" +
            "if a[b[i]] != 40 or a[c[i]] != 10 or a[b[0]] != 20 or a[c[0]] != 30 => bad = 1\nlet check = 1 / (1 - bad)\n",
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

    // The rvalue rows' programs (R5 C2b; DECISIONS R5-M87), self-checking as the operator rows' are: Tuples built, copied, nested and read,
    // zero-sized values borrowed (SPEC 4.3, IMPL 21.1.3); enum Cases built and matched by their discriminant and payload (SPEC 6.5, 14.8);
    // lengths and indices (SPEC 4.7.3); arrays of values in memory built and filled (SPEC 4.3.3); a string moved, then written.
    public static TheoryData<string, string, string> RvaluePrograms => new()
    {
        {
            "RvalueTuple",
            "let t: (i8, i64) = (1, 2)\nlet u = t\nvar bad: i32 = 0\nif u.0 != 1 or u.1 != 2 => bad = 1\nlet n: ((i8, i64), i32) = (u, 3)\nlet m = n\n" +
            "if m.0.1 != 2 or m.1 != 3 or m.0.0 != 1 => bad = 1\nlet z = ((), ())\nlet r = z@ref\nlet q = z.1@ref\nlet check = 1 / (1 - bad)\n",
            string.Empty
        },
        {
            "RvalueEnum",
            "enum Shape\n    Circle(i32)\n    Rect(i32, i64)\n    Empty\nlet s: Shape = .Rect(3, 4)\nlet c: Shape = .Circle(5)\nlet e: Shape = .Empty\n" +
            "var bad: i32 = 0\nlet a: i64 = match s\n    .Rect(let w, let h) => w@i64 + h\n    _ => 0\nif a != 7 => bad = 1\n" +
            "let b: i32 = match c\n    .Circle(let r) => r\n    _ => 0\nif b != 5 => bad = 1\nlet k: i32 = match e\n    .Empty => 1\n    _ => 0\n" +
            "if k != 1 => bad = 1\nlet o: Option<i64> = .Some(9)\nlet v: i64 = match o\n    .Some(let x) => x\n    .None => 0\nif v != 9 => bad = 1\n" +
            "let check = 1 / (1 - bad)\n",
            string.Empty
        },
        {
            "RvalueLength",
            "let a: [4 of i32] = [1, 2, 3, 4]\nvar bad: i32 = 0\nif a.length != 4 => bad = 1\nlet r = a.indices\nif r.start != 0 or r.end != 4 => bad = 1\n" +
            "let none: [0 of i32] = [0 of 1]\nif none.length != 0 => bad = 1\nlet check = 1 / (1 - bad)\n",
            string.Empty
        },
        {
            "RvalueElements",
            "let p: (i32, i64) = (1, 2)\nlet q: [2 of (i32, i64)] = [p, (3, 4)]\nlet f: [3 of (i32, i64)] = [3 of p]\nlet i: isize = 1\nvar bad: i32 = 0\n" +
            "if q[i].0 != 3 or q[0].1 != 2 or q[i].1 != 4 => bad = 1\nif f[2].1 != 2 or f[i].0 != 1 => bad = 1\nlet check = 1 / (1 - bad)\n",
            string.Empty
        },
        { "RvalueString", "let t = \"moved\"\nlet u = t@move\nConsole.writeLine(u)\n", "moved\n" },
    };

    // A requirement's witnesses (SPEC 8.6.3-8.6.4; DECISIONS R5-M93): a conformance's body, an inherited one and a generic Type's instance.
    private const string RequirementWitnesses =
        "contract Shape\n    func area(self: ref/Self) -> i32\nstruct Square\n    Self is Shape\n    public var side: i32\n" +
        "    public init(side: i32) => self.side = side\n    public func area(self: ref/Self) -> i32 => self.side * self.side\n" +
        "public open struct Base\n    public var side: i32 = 2\n    public func area(self: ref/Self) -> i32 => self.side\npublic struct Leaf : Base\n" +
        "    Self is Shape\n    public init() => ()\nstruct Box<T>\n    Self is Shape when T is Copy\n    public var value: T\n" +
        "    public init(value: T) => self.value = value@move\n    public func area(self: ref/Self) -> i32 => 4\nfunc measure<T>(x: ref/T) -> i32\n" +
        "    T is Shape\n    return x.area()\nlet square = Square.init(3)\nlet leaf = Leaf.init()\nlet box = Box<i64>.init(5)\nvar bad: i32 = 0\n" +
        "if measure(square) != 9 or measure(leaf) != 2 or measure(box) != 4 => bad = 1\nlet check = 1 / (1 - bad)\n";

    // A Contract property a computed property's accessors witness (SPEC 8.6.4; DECISIONS R5-M93).
    private const string RequirementAccessors =
        "contract Counted\n    property count: i32 has get, set\nstruct Gauge\n    Self is Counted\n    var stored: i32 = 2\n    public computed count: i32\n" +
        "        get() -> i32 => self.stored\n        set(value: i32) -> () => self.stored = value\n    public init() => ()\n" +
        "func bump<T>(x: uniq/T) -> i32\n    T is Counted\n    x.count = x.count + 1\n    return x.count\nvar gauge = Gauge.init()\nvar bad: i32 = 0\n" +
        "if bump(gauge@uniq) != 3 or gauge.count != 3 => bad = 1\nlet check = 1 / (1 - bad)\n";

    // A caller-location library function's call (SPEC 22.5.1; DECISIONS R5-M93): Range.iterate, which RangeIterator.starting's Fail follows.
    private const string CallerLocations = "let r = 3..1\nlet it = r.iterate()\n";

    // The call rows' programs (R5 C3a, C3b; DECISIONS R5-M89, R5-M93), self-checking: recursion; Tuple arguments and results, a Copy argument kept
    // independent of its source while the call writes the source through an exclusive borrow (the ABI result-slot and copy rules); a string
    // moved into and out of a call; generic instances; defaults, a recursive one through its template; a pair binder's function in its
    // owner and its uniq case (tests/mir/cases.kimi's keep, moved here from C2b); a #LibraryImport call; a diverging callee's Abort at its
    // site (SPEC 22.5.1); requirement calls of each witness kind.
    public static TheoryData<string, string, string, string> CallPrograms => new()
    {
        {
            "CallScalar",
            "func factorial(n: i64) -> i64\n    if n <= 1 => return 1\n    return n * factorial(n - 1)\nfunc even(n: u32) -> bool\n" +
            "    if n == 0 => return true\n    return not even(n - 1)\nvar bad: i32 = 0\nif factorial(10) != 3628800 or not even(10) or even(7) => bad = 1\n" +
            "let check = 1 / (1 - bad)\n",
            string.Empty,
            string.Empty
        },
        {
            "CallSlots",
            "func swap(p: (i32, i64)) -> (i64, i32) => (p.1, p.0)\nfunc probe(p: (i32, i64), r: uniq/(i32, i64)) -> i64\n    r@follow = (7, 8)\n" +
            "    return p.1\nvar s: (i32, i64) = (1, 2)\nvar bad: i32 = 0\nlet w = swap(s)\nif w.0 != 2 or w.1 != 1 => bad = 1\n" +
            "if probe(s, s@uniq) != 2 or s.1 != 8 or s.0 != 7 => bad = 1\nlet check = 1 / (1 - bad)\n",
            string.Empty,
            string.Empty
        },
        { "CallString", "func echo(s: string) -> string => s@move\nlet t = echo(\"called\")\nConsole.writeLine(t)\n", "called\n", string.Empty },
        {
            "CallGeneric",
            "func pick<T>(flag: bool, a: T, b: T) -> T\n    T is Copy\n    if flag => return a\n    return b\nvar bad: i32 = 0\n" +
            "if pick(true, 1, 2) != 1 or pick(false, 1.5, 2.5) != 2.5 or pick(true, (1, 2), (3, 4)).1 != 2 => bad = 1\nlet check = 1 / (1 - bad)\n",
            string.Empty,
            string.Empty
        },
        {
            "CallDefault",
            "func next(x: i32, y: i32 = x + 1) -> i32 => y\nfunc count(n: i32, m: i32 = if n <= 0 => 0 else => count(n - 1) + 1) -> i32 => m\n" +
            "var bad: i32 = 0\nif next(1) != 2 or next(1, 5) != 5 or count(3) != 3 or count(3, 9) != 9 => bad = 1\nlet check = 1 / (1 - bad)\n",
            string.Empty,
            string.Empty
        },
        {
            "CallViews",
            "func keep<s/T>(value: s/T, x: T) -> T\n    s is owner or uniq\n    T is Copy\n    var v = value@move\n    let local = v\n    _ = local\n" +
            "    v@follow = x\n    return v@follow\nvar y: i32 = 1\nvar bad: i32 = 0\nlet a = keep(5, 2)\nlet b = keep(y@uniq, 3)\n" +
            "if a != 2 or b != 3 or y != 3 => bad = 1\nlet check = 1 / (1 - bad)\n",
            string.Empty,
            string.Empty
        },
        {
            "CallForeign",
            "group Native\n    #LibraryImport(\"kernel32\", \"GetLastError\")\n    public unsafe func lastError() -> u32\nvar error: u32 = 1\n" +
            "unsafe => error = Native.lastError()\nConsole.writeLine(\"imported\")\n",
            "imported\n",
            string.Empty
        },
        {
            "CallNeverAbort",
            "func fail(x: u8) -> Never\n    let y = x + 1\n    loop => ()\nlet z: u8 = 255\nfail(z)\n",
            string.Empty,
            "Hello.kimi:2:13: abort KIMI_E_INT_OVERFLOW: Integer overflow\n"
        },
        { "CallRequirementWitnesses", RequirementWitnesses, string.Empty, string.Empty },
        { "CallRequirementAccessors", RequirementAccessors, string.Empty, string.Empty },
    };

    // The raw memory and failure rows' programs (R5 C8a; DECISIONS R5-M92), self-checking: storage allocated, initialized with a scalar and
    // a Tuple, read through p[n] and a Slice, displaced and compared, then released; $abort of a constant and of a moved string, at the
    // $abort expression; a Fail, a range selection out of bounds, at the selection (SPEC 22.5.1-22.5.2).
    public static TheoryData<string, string, string> RawPrograms => new()
    {
        {
            "RawRows",
            "let p = Raw.allocate<i64>(3)\nvar bad: i32 = 0\nunsafe\n    Raw.initialize(p, 7)\n    Raw.initialize(p + 1, 8)\n    Raw.initialize(p + 2, 9)\n" +
            "    let s = Raw.slice(p, 3)\n    if p[0] + p[1] + s[2] != 24 or s.length != 3 => bad = 1\n    let q = p + 2\n    if q - 1 != p + 1 => bad = 1\n" +
            "    Raw.release(p)\nif p == null => bad = 1\nlet check = 1 / (1 - bad)\n",
            string.Empty
        },
        {
            "RawRowsTuple",
            "let p = Raw.allocate<(i32, i64)>(1)\nvar bad: i32 = 0\nunsafe\n    Raw.initialize(p, (1, 2))\n    if (*p).1 != 2 => bad = 1\n" +
            "    Raw.release(p)\nlet check = 1 / (1 - bad)\n",
            string.Empty
        },
        { "AbortRowsConstant", "$abort(\"stop\")\n", "Hello.kimi:1:1: abort KIMI_E_ABORT: stop\n" },
        { "AbortRowsMoved", "func fail(text: string) => $abort(text@move)\nfail(\"moved\")\n", "Hello.kimi:1:28: abort KIMI_E_ABORT: moved\n" },
        {
            "AbortRowsBounds",
            "let a: [4 of i32] = [1, 2, 3, 4]\nvar n: isize = 5\nlet s = a[1..n]\n",
            "Hello.kimi:3:9: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n"
        },
    };

    [Theory]
    [MemberData(nameof(Frames))]
    public void FrameProgramsRun(string name, string source)
        => ScalarEmissionTest.EmitFixture(name, source, string.Empty);

    // DECISIONS BU-48 and R5-M149 (the owner's Q1 (a); SPEC 6.3.2-6.3.4, 8.15.3, IMPL 21.3.2): a constructor's construction receiver is no
    // use, so an uncalled constructor whose receiver has no layout is accepted at check; no program that builds calls it (each construction
    // expression is a use of that layout), so a Library checks it only and does not generate it (InstanceCollector.Generates), and an
    // Application, which generates only its startup's closure, neither: both are written without a record, its Generated column clear, and
    // so are the bodies only it makes (DECISIONS R5-M153): a Closure in its body, and a Closure in a Field's initializer with the function
    // declared in it (fail-before: a Library generated each, at the base and under the lexical-nesting rule alike); its default templates
    // take its verdict, a constant's and a Closure's (fail-before: a Library generated each template, the Closure in it too).
    [Theory]
    [InlineData(OutputKind.Library, "clayout")]
    [InlineData(OutputKind.Library, "limit")]
    [InlineData(OutputKind.Library, "default")]
    [InlineData(OutputKind.Library, "closure default")]
    [InlineData(OutputKind.Application, "clayout")]
    [InlineData(OutputKind.Application, "limit")]
    public void AnUncalledConstructorWithoutAReceiverLayoutIsCheckedOnly(OutputKind output, string form)
    {
        var source = form switch
        {
            "clayout" => "#Layout(\"C\")\npublic struct S\n    public init()\n        let add = func [] (x: i32) -> i32 => x + x\n",
            "default" => "#Layout(\"C\")\npublic struct S\n    public init(n: i32 = 2) => ()\n",
            "closure default" => "#Layout(\"C\")\npublic struct S\n    public init(f: (i32) -> i32 = func [] (x: i32) -> i32 => x + 1) => ()\n",
            _ => "public struct Big\n    public var a: [1073741824 of u8]\n    public var b: [1073741824 of u8]\n    public var c: [1073741824 of u8]\n" +
                "    public var d: i32 = (func [] (x: i32) -> i32\n        func twice(y: i32) -> i32 => y * 2\n        return twice(x)\n    )(2)\n" +
                "    public init(a: [1073741824 of u8], b: [1073741824 of u8], c: [1073741824 of u8])\n" +
                "        self.a = a@move\n        self.b = b@move\n        self.c = c@move\n",
        };
        var c = Analyze(source + (output == OutputKind.Application ? "let zz = 1\n" : string.Empty), output);
        Assert.True(c.Accepts, string.Join(", ", Records(c)));
        using var writer = new StringWriter();
        Assert.True(c.Generate(writer), string.Join(", ", Records(c)));
        Assert.Empty(Records(c));
        var set = c.Instances!;
        var init = Assert.Single(Enumerable.Range(0, set.RootCount), x => set.Items[x].Key.Body.Def.Kind == MirBodyKind.Fn &&
            set.Items[x].Key.Body.Def.Decl?.Declaration?.ToString().Contains("init(", StringComparison.Ordinal) == true);
        Assert.False(set.IsGenerated(init));
        Assert.DoesNotContain(Enumerable.Range(0, set.Items.Length), x => set.Items[x].Key.Body.Def.Decl is not null && set.IsGenerated(x));
    }

    // SPEC 4.7.4, 8.15.3 (DECISIONS R5-M149): an element of a fixed array behind a reference needs only the element's stride and the array's
    // closed length, so an array over the size limit is indexed without its layout (fail-before: the place writer read the array's record,
    // InternalInvariant_Kd); its element's verdict is the collector's.
    [Fact]
    public void AFixedArrayBehindAReferenceIsIndexedWithoutItsLayout()
    {
        var c = Analyze("public func f(p: ref/[300000000 of i64], i: isize) -> i64 => p[i]\n", OutputKind.Library);
        Assert.True(c.Accepts, string.Join(", ", Records(c)));
        using var writer = new StringWriter();
        Assert.True(c.Generate(writer), string.Join(", ", Records(c)));
        Assert.Contains("icmp uge i64", writer.ToString(), StringComparison.Ordinal);
    }

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

    [Theory]
    [MemberData(nameof(RvaluePrograms))]
    public void RvalueProgramsRun(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture(name, source, stdout);

    [Theory]
    [MemberData(nameof(CallPrograms))]
    public void CallProgramsRun(string name, string source, string stdout, string stderr)
        => ScalarEmissionTest.EmitFixture(name, source, stdout, stderr.Length == 0 ? 0 : 1, stderr);

    [Theory]
    [MemberData(nameof(RawPrograms))]
    public void RawProgramsRun(string name, string source, string stderr)
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

    // The operator rows (R5 C1a; DECISIONS R5-M76; SPEC 13.3.1, IMPL 21.3.3): a checked integer operation is its overflow intrinsic, whose
    // flag branches to an Abort with KIMI_E_INT_OVERFLOW (6) at the operator's location text (SPEC 22.5.1-22.5.2), and negation subtracts
    // from 0; the result is the intrinsic's value. Nothing claims nsw or nuw (I9). A 128-bit one is the same entry; its intrinsics'
    // declarations are the module's exactly when such a check is written (DECISIONS R5-M143).
    [Theory]
    [InlineData("func f(a: i32, b: i32) -> i32 => a + b", "a + b", "llvm.sadd.with.overflow.i32", "i32 %, i32 %")]
    [InlineData("func f(a: i128, b: i128) -> i128 => a + b", "a + b", "llvm.sadd.with.overflow.i128", "i128 %, i128 %")]
    [InlineData("func f(a: u128, b: u128) -> u128 => a + b", "a + b", "llvm.uadd.with.overflow.i128", "i128 %, i128 %")]
    [InlineData("func f(a: u128, b: u128) -> u128 => a - b", "a - b", "llvm.usub.with.overflow.i128", "i128 %, i128 %")]
    [InlineData("func f(a: i128, b: i128) -> i128 => a * b", "a * b", "llvm.smul.with.overflow.i128", "i128 %, i128 %")]
    [InlineData("func f(a: u128, b: u128) -> u128 => a * b", "a * b", "llvm.umul.with.overflow.i128", "i128 %, i128 %")]
    [InlineData("func f(a: i128) -> i128 => -a", "-a", "llvm.ssub.with.overflow.i128", "i128 0, i128 %")]
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
        Assert.Equal(type == "i128", text.Contains(LlvmModuleText.WideOverflowDeclarations, StringComparison.Ordinal));
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
    [InlineData(Layouts, "payload", "%t5 = load ptr, ptr %l3, align 8\n  %t6 = getelementptr i8, ptr %t5, i64 8\n  store ptr %t6, ptr %l5, align 8\n")]
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

    // SPEC 4.3.3 (DECISIONS R4-M81): a Fill stores its one value at each element by one counted i64 loop over its instance's closed
    // length, so its text does not grow with the length (fail-before: one store per element), and a zero-length one stores nothing, also
    // into a Field followed by another (a Field initializer's Fill is built into its Field).
    [Fact]
    public void AFillIsOneLoopOverItsLength()
    {
        IrFunction Filler(int length) => Assert.Single(
            Parse($"func f() -> i32\n    let a: [{length} of i32] = [{length} of 7]\n    return a[1]\n").Ir.Functions,
            static x => x.Instructions.Any(static y => y.Shape == "store i32 7, ptr %, align 4"));
        var (small, large) = (Filler(4), Filler(4096));
        Assert.Equal(small.Instructions.Select(static x => x.Opcode), large.Instructions.Select(static x => x.Opcode));
        Assert.Single(large.Instructions, static x => x.Shape == "store i32 7, ptr %, align 4");
        Assert.Single(large.Instructions, static x => x.Shape == "icmp eq i64 %, 4096");
        Assert.Single(large.Instructions, static x => x.Opcode == "phi");

        var init = Assert.Single(ItemTexts(
            Analyze("struct S\n    public var n: i32 = 5\n    public var z: [0 of i32] = [0 of 9]\n    public var m: i32 = 6\n    public init() => ()\n", OutputKind.Library),
            "init"));
        Assert.Contains("store i32 6", init, StringComparison.Ordinal);
        Assert.DoesNotContain("store i32 9", init, StringComparison.Ordinal);
    }

    // DECISIONS R5-M87 (IMPL 21.1.3, 21.3.1): a value in memory, a slot Type of nonzero size other than the string's first-class record, is
    // copied from its source's address into its destination by one memcpy of its size with its alignment on both pointers, which the
    // module then declares.
    [Theory]
    [InlineData("func f(t: (i32, i64)) -> (i32, i64) => t", 8, 16)]
    [InlineData("func f(a: [3 of i16]) -> [3 of i16] => a", 2, 6)]
    [InlineData("func f(o: Option<i32>) -> Option<i32> => o@move", 4, 8)]
    [InlineData("struct P\n    public var x: i64\n    public var y: i8\n    public init(x: i64, y: i8)\n        self.x = x\n        self.y = y\nfunc f(p: P) -> P => p@move", 8, 16)]
    public void AValueInMemoryIsCopiedByMemcpy(string source, int alignment, int size)
    {
        var (ir, text) = Parse(source);
        var f = ir.Single("internal void (ptr, ptr)");
        var copy = Assert.Single(f.Instructions, static x => x.Opcode == "call");
        Assert.Same(ir.Function("llvm.memcpy.p0.p0.i64"), copy.Callee);
        Assert.Equal($"call void @(ptr align {alignment} %, ptr align {alignment} %, i64 {size}, i1 false)", copy.Shape);
        Assert.Equal(f.Parameters, copy.Operands);
        Assert.Contains(LlvmModuleText.MemoryDeclarations, text);
    }

    // DECISIONS R5-M87: the string's record, a first-class value of its three Fields (RuntimeAbi.String), is loaded and stored whole, its
    // Fields only, as the old generator moves it; nothing is copied by memcpy, so the module declares none.
    [Fact]
    public void AStringIsMovedAsItsRecord()
    {
        var (ir, text) = Parse("func f(x: string) -> string => x@move");
        var f = ir.Single("internal void (ptr, ptr)");
        var moves = f.Instructions.Where(static x => x.Opcode is "load" or "store").ToArray();
        Assert.Equal(["load %kimi.string, ptr %, align 8", "store %kimi.string %, ptr %, align 8"], moves.Select(static x => x.Shape));
        Assert.Equal([f.Parameters[1], moves[0], f.Parameters[0]], [moves[0].Operands[0], .. moves[1].Operands]);
        Assert.DoesNotContain("llvm.memcpy", text);
    }

    // An aggregate puts each operand at its part (IMPL 21.1.2-21.1.3; DECISIONS R5-M87), after its operands: a Tuple's elements at the
    // record's Field offsets (Kimigayo order: the i64 first); an enum Case's ordinal as its i32 tag at 0 and its payload at the payload's
    // offset plus the payload Tuple's; a ResolvedRange's Fields; a value in memory copied into its part; a Fill's copied at each element.
    [Theory]
    [InlineData("func f(a: i8, b: i64) -> (i8, i64) => (a, b)", "  %t2 = getelementptr i8, ptr %ret, i64 8\n  store i8 %t0, ptr %t2, align 1\n  store i64 %t1, ptr %ret, align 8\n")]
    [InlineData(
        "enum Shape\n    Circle(i32)\n    Rect(i32, i64)\n    Empty\nfunc f(a: i32, b: i64) -> Shape => .Rect(a, b)",
        "  store i32 1, ptr %ret, align 4\n  %t2 = getelementptr i8, ptr %ret, i64 16\n  store i32 %t0, ptr %t2, align 4\n  %t3 = getelementptr i8, ptr %ret, i64 8\n  store i64 %t1, ptr %t3, align 8\n")]
    [InlineData("enum Shape\n    Circle(i32)\n    Rect(i32, i64)\n    Empty\nfunc f() -> Shape => .Empty", "b0:\n  store i32 2, ptr %ret, align 4\n  ret void\n")]
    [InlineData(
        "func f(p: (i32, i64), c: i8) -> ((i32, i64), i8) => (p, c)",
        "  call void @llvm.memcpy.p0.p0.i64(ptr align 8 %ret, ptr align 8 %a0, i64 16, i1 false)\n  %t1 = getelementptr i8, ptr %ret, i64 16\n  store i8 %t0, ptr %t1, align 1\n")]
    [InlineData(
        "func f(a: ref/[3 of i32]) -> ResolvedRange => a.indices",
        "  store i64 3, ptr %l2, align 8\n  %t1 = load i64, ptr %l2, align 8\n  store i64 0, ptr %ret, align 8\n  %t2 = getelementptr i8, ptr %ret, i64 8\n  store i64 %t1, ptr %t2, align 8\n")]
    [InlineData(
        "func f(p: (i32, i64)) -> [2 of (i32, i64)] => [p, p]",
        "  call void @llvm.memcpy.p0.p0.i64(ptr align 8 %ret, ptr align 8 %a0, i64 16, i1 false)\n  %t0 = getelementptr i8, ptr %ret, i64 16\n  call void @llvm.memcpy.p0.p0.i64(ptr align 8 %t0, ptr align 8 %a0, i64 16, i1 false)\n")]
    [InlineData(
        "func f(p: (i32, i64)) -> [3 of (i32, i64)] => [3 of p]",
        "  %t5 = getelementptr i8, ptr %ret, i64 %t3\n  call void @llvm.memcpy.p0.p0.i64(ptr align 8 %t5, ptr align 8 %a0, i64 16, i1 false)\n  %t2 = add i64 %t1, 1\n")]
    public void AnAggregatePutsEachOperandAtItsPart(string source, string text)
        => Assert.Contains(text, Parse(source).Text, StringComparison.Ordinal);

    // SPEC 14.8, IMPL 21.1.3 (DECISIONS R5-M87): a Discriminant reads its enum Place's i32 tag at offset 0 and widens it to isize, which the
    // match's SwitchInt compares with the Case ordinals; the Downcast then addresses the payload.
    [Fact]
    public void ADiscriminantReadsTheTag()
        => Assert.Contains(
            "  %t1 = load ptr, ptr %l3, align 8\n  %t2 = load i32, ptr %t1, align 4\n  %t3 = zext i32 %t2 to i64\n  store i64 %t3, ptr %l4, align 8\n  %t4 = load i64, ptr %l4, align 8\n  switch i64 %t4, label %b2 [ i64 1, label %b3 ]\n",
            Assert.Single(ItemTexts(Analyze(Layouts, OutputKind.Library), "payload")),
            StringComparison.Ordinal);

    // SPEC 4.7.3 (DECISIONS R5-M87): a Len is a fixed array's closed length once its Place is located, or the length word (Field 1) of an
    // Array's or a Slice's handle, the word an Index checks against.
    [Theory]
    [InlineData("func f(a: ref/[3 of i32]) -> isize => a.length", "  %t0 = load ptr, ptr %l1, align 8\n  store i64 3, ptr %l0, align 8\n")]
    [InlineData(
        "func f(a: ref/Array<i64>) -> isize => a.length",
        "  %t0 = load ptr, ptr %l1, align 8\n  %t1 = getelementptr i8, ptr %t0, i64 8\n  %t2 = load i64, ptr %t1, align 8\n  store i64 %t2, ptr %l0, align 8\n")]
    [InlineData("func f(s: Slice<u16>) -> isize => s.length", "  %t0 = getelementptr i8, ptr %a0, i64 8\n  %t1 = load i64, ptr %t0, align 8\n  store i64 %t1, ptr %l0, align 8\n")]
    public void ALenIsTheSequencesLength(string source, string text)
        => Assert.Contains(text, Parse(source).Text, StringComparison.Ordinal);

    // IMPL 21.1.3 (package C row 10; DECISIONS R5-M87): a zero-sized local's home, so its borrow and its parts', is the module's one
    // 16-aligned zero-sized symbol, which the module defines only when a definition uses it.
    [Fact]
    public void AZeroSizedHomeIsTheModulesSymbol()
    {
        var text = Parse("func f() -> i32\n    let u = ()\n    let r = u@ref\n    let z = ((), ())\n    let q = z.1@ref\n    return 1\n").Text;
        Assert.Equal(["%l2", "%l4"], Regex.Matches(text, @"store ptr @__kimi_zero_sized, ptr (%l\d+), align 8\n").Select(static x => x.Groups[1].Value));
        Assert.Contains("\n@__kimi_zero_sized = private unnamed_addr constant [1 x i8] zeroinitializer, align 16\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("__kimi_zero_sized", Parse(Unused).Text, StringComparison.Ordinal);
    }

    // SPEC 8.14, 8.15 (DECISIONS R5-M87): a PairAcquire writes its Place's bits in the item's case Type, a Copy in the owner case and, in
    // the uniq case, the pointer the Place holds, its Reborrow; an Adapt is the rvalue the view selects, an Identity's Copy or a borrow.
    [Fact]
    public void AnAcquisitionAndAnAdaptationFollowTheItemsView()
    {
        var keep = ItemTexts(Analyze(Acquisitions, OutputKind.Library), "keep");
        Assert.Equal(2, keep.Length);
        Assert.Single(keep, static x => Regex.IsMatch(x, @"(%t\d+) = load i32, ptr %l3, align 4\n  store i32 \1, ptr %l4, align 4\n"));
        Assert.Single(keep, static x => Regex.IsMatch(x, @"(%t\d+) = load ptr, ptr %l3, align 8\n  store ptr \1, ptr %l4, align 8\n"));
        var put = ItemTexts(Analyze(Adaptations, OutputKind.Library), "put");
        Assert.Equal(2, put.Length);
        Assert.Single(put, static x => Regex.IsMatch(x, @"(%t\d+) = load i32, ptr %l2, align 4\n  store i32 \1, ptr %l\d+, align 4\n"));
        Assert.Single(put, static x => Regex.IsMatch(x, @"store ptr %l2, ptr %l\d+, align 8\n"));
    }

    // DECISIONS R5-M87, R5-M111 (SPEC 8.14, 13.5.3, 13.6.2): an Adapt whose view selects an object creation calls its payload's factory,
    // the statement's target, with the operand at its kind, obj's control value and the adaptation's location, and copies nothing in
    // place of the object.
    [Fact]
    public void AnObjectCreationAdaptationCallsItsPayloadsFactory()
    {
        var c = Analyze(Creations, OutputKind.Library);
        var ir = GeneratedIr.Parse(Generate(c));
        var put = Assert.Single(FunctionsOf(ir, c, "put"));
        var create = Assert.Single(CallsOf(put), static x => x.Callee!.Signature == "internal void (ptr, i32, i64, ptr, i64)");
        Assert.Matches(@"^call void @\(ptr %, i32 %, i64 0, ptr @, i64 \d+\)$", create.Shape);
        Assert.Contains(Constant(LocationText(Creations, "x@s")), Generate(c));
        Assert.DoesNotContain(put.Instructions, static x => x.Shape == "store i32 %, ptr %, align 4" && x.Operands[^1].Shape == "alloca [8 x i8], align 8");
    }

    // IMPL 21.3.1 (DECISIONS R5-M89): a call of a function's item passes each argument by its parameter's kind and puts its value into the
    // destination: a scalar in its computation representation (a bool narrowed, its result widened), a string borrow with its attributes
    // (the O2 parity list), nothing for a zero-sized parameter, the destination's address as a slot result, a moved value's Place itself
    // and a Copy of a value in memory through a copy slot of the caller's entry block; the callee is the item's own definition.
    [Theory]
    [InlineData("func id(x: i32) -> i32 => x\nfunc f(a: i32) -> i32 => id(a)", "internal i32 (i32)", "call i32 @(i32 %)", "store i32 %, ptr %, align 4")]
    [InlineData("func flip(b: bool) -> bool => not b\nfunc g(c: bool) -> bool => flip(c)", "internal i1 (i1)", "call i1 @(i1 %)", "zext i1 % to i8")]
    [InlineData(
        "func show(t: ref/string) => Console.writeLine(t)\nfunc call(t: ref/string) => show(t)",
        "internal void (ptr noundef nonnull align 8 dereferenceable(24))",
        "call void @(ptr noundef nonnull align 8 dereferenceable(24) %)",
        null)]
    [InlineData(
        "func unit(x: (), y: i32) -> i32 => y\nfunc f() -> i32 => unit((), 3)",
        "internal i32 (i32)",
        "call i32 @(i32 3)",
        "store i32 %, ptr %, align 4")]
    [InlineData("func pair(a: i32) -> (i32, i64) => (a, 2)\nfunc f() -> i64 => pair(1).1", "internal void (ptr, i32)", "call void @(ptr %, i32 1)", null)]
    [InlineData("func take(s: string) => ()\nfunc give()\n    let t = \"x\"\n    take(t@move)", "internal void (ptr)", "call void @(ptr %)", null)]
    [InlineData(
        "func use(p: (i32, i64)) -> i64 => p.1\nfunc copy(q: (i32, i64)) -> i64 => use(q)",
        "internal i64 (ptr)",
        "call i64 @(ptr %)",
        "store i64 %, ptr %, align 8")]
    [InlineData(
        "func keep<T>(value: T) -> T => value@move\nfunc f() -> i32 => keep(1)",
        "internal i32 (i32)",
        "call i32 @(i32 1)",
        "store i32 %, ptr %, align 4")]
    public void ACallPassesEachArgumentByItsParameter(string source, string callee, string call, string? result)
    {
        var (ir, text) = Parse(source);
        var definition = Assert.Single(
            ir.Functions,
            x => x.Signature == callee && !x.Instructions.Any(y => y.Opcode == "call" && y.Callee is { } c && c.Signature == callee));
        var site = Assert.Single(ir.Functions.SelectMany(static x => x.Instructions), x => x.Opcode == "call" && x.Callee == definition);
        Assert.Equal(call, site.Shape);
        if (result is not null)
        {
            Assert.Contains(ir.Functions.SelectMany(static x => x.Instructions), x => x.Shape == result && x.Operands.Contains(site));
        }

        Assert.DoesNotContain("inbounds", text, StringComparison.Ordinal);
    }

    // SPEC 8.6.3-8.6.4 (DECISIONS R5-M93): a requirement call calls the witness the collector targets at it, under the witness's own
    // signature: measure's three instances call the three area bodies (a conformance's, an inherited one, a generic Type's instance), and
    // bump calls the computed property's Getter, Setter and Getter.
    [Fact]
    public void ARequirementCallCallsItsWitness()
    {
        var c = Analyze(RequirementWitnesses);
        var areas = ItemTexts(c, "area");
        var callees = ItemTexts(c, "measure").Select(static x => Regex.Match(x, @"= call i32 @(__kimi_f\d+)\(ptr %t\d+\)\n").Groups[1].Value).ToArray();
        Assert.Equal(3, callees.Distinct(StringComparer.Ordinal).Count());
        Assert.All(callees, x => Assert.Single(areas, y => y.StartsWith($"define internal i32 @{x}(ptr %a0) ", StringComparison.Ordinal)));
        var bump = Assert.Single(ItemTexts(Analyze(RequirementAccessors), "bump"));
        Assert.Equal(3, Regex.Matches(bump, @"\n  (?:%t\d+ = )?call (?:i32|void) @__kimi_f\d+\(ptr %t\d+").Count);
    }

    // SPEC 22.5.1 (DECISIONS R5-M93): a caller-location library function receives its caller's location as its last two parameters, which
    // the user's call passes as its site's text (Hello.kimi:2:10, 15 bytes), and every location it reports is the one it received:
    // Range.iterate forwards it to RangeIterator.starting, whose Fail Aborts at it, and no body with those parameters names a pooled one.
    [Fact]
    public void ACallerLocationBodyReportsTheLocationItReceived()
    {
        var text = Generate(CallerLocations);
        Assert.Matches(@"\n  call void @__kimi_f\d+\(ptr %l\d+, ptr %t\d+, ptr @__kimi_location, i64 15\)\n", text);
        const string Received = @"\ndefine internal [^\n]* @__kimi_f\d+\([^\n]*, ptr %location, i64 %location_length\) #0 \{\n(?:[^}\n][^\n]*\n)*\}\n";
        var received = Regex.Matches(text, Received).Select(static x => x.Value).ToArray();
        Assert.Equal(2, received.Length);
        Assert.All(received, static x => Assert.DoesNotContain("@__kimi_location", x, StringComparison.Ordinal));
        Assert.Single(received, static x => Regex.IsMatch(x, @"\n  call void @__kimi_f\d+\([^\n]*, ptr %location, i64 %location_length\)\n"));
        var fail = $"\n  call void @__kimi_abort(i32 {AbortReasons.Of("KIMI_E_ARG_RANGE")}, ptr %location, i64 %location_length, i64 -2)\n";
        Assert.Single(received, x => x.Contains(fail, StringComparison.Ordinal));
    }

    // SPEC 3.5 with IMPL 21.3.1 (DECISIONS R5-M89): a Copy of a value in memory passed by pointer is copied into a copy slot, an alloca of
    // the caller's entry block, which the callee receives, and a string constant is written into one as its record (Static, IMPL 21.1.3);
    // a moved string's Place is passed itself, its record never loaded.
    [Fact]
    public void ACopyArgumentInMemoryHasACopySlot()
    {
        var text = Parse("func use(p: (i32, i64)) -> i64 => p.1\nfunc copy(q: (i32, i64)) -> i64 => use(q)").Text;
        Assert.Contains("  %s0 = alloca { [0 x i64], [16 x i8] }, align 8\n", text, StringComparison.Ordinal);
        const string Copied = "  call void @llvm.memcpy.p0.p0.i64(ptr align 8 %s0, ptr align 8 %a0, i64 16, i1 false)\n  %t0 = call i64 @";
        Assert.Contains(Copied, text, StringComparison.Ordinal);
        var moved = Parse("func take(s: string) => ()\nfunc give()\n    let t = \"x\"\n    take(t@move)").Text;
        Assert.Matches(@"\n  call void @__kimi_f\d+\(ptr %l\d+\)\n", moved);
        Assert.DoesNotContain("%s0", moved, StringComparison.Ordinal);
        var constant = Parse("func take(s: string) => ()\nfunc give() => take(\"x\")").Text;
        Assert.Contains("  %s0 = alloca %kimi.string, align 8\n", constant, StringComparison.Ordinal);
        Assert.Matches(@"\n  store %kimi.string \{ ptr @[^,]+, i64 1, i8 \d+ \}, ptr %s0, align 8\n  call void @__kimi_f\d+\(ptr %s0\)\n", constant);
    }

    // I1 (DECISIONS R5-M92): a copy slot's name is never a block's, so a function with copy slots and checks whose success labels share
    // their numbers (c<id>) stays valid IR; R5-M89's %c<k> collided with them.
    [Fact]
    public void CopySlotsAndCheckLabelsNeverShareAName()
    {
        var text = Parse("func use(a: (i32, i64), b: (i32, i64), c: (i32, i64)) -> i64 => a.1\nfunc f(x: i32, q: (i32, i64)) -> i64\n" +
            "    let y = x + 1\n    return use(q, q, q) + y@i64").Text;
        var slots = Regex.Matches(text, @"\n  %(\w+) = alloca ").Select(static x => x.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var labels = Regex.Matches(text, @"\n(\w+):\n").Select(static x => x.Groups[1].Value);
        Assert.Contains("s2", slots);
        Assert.Contains("c1", labels);
        Assert.DoesNotContain(labels, slots.Contains);
    }

    // An ABI requirement (IMPL 21.3.1; DECISIONS R5-M89): a slot result is written through the caller's destination, which is never one of
    // the arguments' pointers: a moved argument's Place is moved out and a copied one's is a copy slot.
    [Theory]
    [InlineData("func swap(p: (i32, i64)) -> (i64, i32) => (p.1, p.0)\nfunc f(q: (i32, i64)) -> (i64, i32) => swap(q)")]
    [InlineData("func swap(p: (i32, i64)) -> (i64, i32) => (p.1, p.0)\nfunc f(q: (i32, i64)) -> i64\n    let r = swap(q)\n    return r.0")]
    [InlineData("func echo(s: string) -> string => s@move\nfunc f(t: string) -> string => echo(t@move)")]
    [InlineData("func join(p: (i32, i64), q: (i32, i64)) -> (i64, i64) => (p.1, q.1)\nfunc f(a: (i32, i64), b: (i32, i64)) -> (i64, i64) => join(a, b@move)")]
    public void AResultSlotIsNeverAnArgumentsPointer(string source)
    {
        var ir = Parse(source).Ir;
        var calls = ir.Functions.SelectMany(static x => x.Instructions)
            .Where(static x => x.Opcode == "call" && x.Shape.StartsWith("call void @(ptr %, ptr %", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(calls);
        Assert.All(calls, static x => Assert.DoesNotContain(x.Operands[0], x.Operands.Skip(1)));
    }

    // SPEC 14.10.3, the O2 parity list: a callee of result Never is defined noreturn, and a call without a target is followed by unreachable.
    [Fact]
    public void ADivergingCallIsFollowedByUnreachable()
    {
        var text = Parse("func stop() -> Never\n    loop => ()\nfunc diverge(flag: bool) -> i32\n    if flag => stop()\n    return 1").Text;
        Assert.Matches(@"define internal void @__kimi_f\d+\(\) noreturn #0 \{", text);
        Assert.Matches(@"\n  call void @__kimi_f\d+\(\)\n  unreachable\n", text);
    }

    // Plan 5.6.2 (DECISIONS R5-M89): a recursive default calls its template, whose Arg locals are its prepared inputs in slot order, each a
    // shared borrow of the slot the call prepared; the template is written as a function of them.
    [Fact]
    public void ARecursiveDefaultCallsItsTemplate()
    {
        var ir = Parse("func count(n: i32, m: i32 = if n <= 0 => 0 else => count(n - 1) + 1) -> i32 => m\nfunc counted() -> i32 => count(3)").Ir;
        var template = ir.Single("internal i32 (ptr)");
        Assert.Contains(template.Instructions, x => x.Opcode == "call" && x.Callee == template && x.Shape == "call i32 @(ptr %)");
        Assert.Contains(ir.Functions.SelectMany(static x => x.Instructions), x => x.Opcode == "call" && x.Callee == ir.Single("internal i32 (i32, i32)"));
    }

    // SPEC 5.6, 17.4, 22.5.2 (DECISIONS R5-M92): an intrinsic is written by its row's writer: Raw.allocate calls its routine with the
    // element's stride and the call's location, Raw.release its routine, Raw.initialize stores the value at the storage, Raw.slice puts the
    // record's two Fields, $abort calls the message routine with its constant's copy slot and Fail the Abort routine with its code's reason,
    // each without a successor.
    [Theory]
    [InlineData("func make(n: isize) -> raw/i64 => Raw.allocate<i64>(n)", @"= call ptr @__kimi_raw_allocate\(i64 %t\d+, i64 8, ptr @\S+, i64 \d+\)\n")]
    [InlineData("func make(n: isize) -> raw/(i32, i64) => Raw.allocate<(i32, i64)>(n)", @"= call ptr @__kimi_raw_allocate\(i64 %t\d+, i64 16, ptr @\S+")]
    [InlineData("func free(p: raw/i64)\n    unsafe => Raw.release(p)", @"\n  call void @__kimi_free\(ptr %t\d+, ptr @\S+, i64 \d+\)\n  br ")]
    [InlineData("func put(p: raw/i64, v: i64)\n    unsafe => Raw.initialize(p, v)", @"\n  store i64 %t\d+, ptr %t\d+, align 8\n  br ")]
    [InlineData(
        "func put(p: raw/(i32, i64), v: (i32, i64))\n    unsafe => Raw.initialize(p, v@move)",
        @"@llvm\.memcpy\.p0\.p0\.i64\(ptr align 8 %t\d+, ptr align 8 %l\d+, i64 16, i1 false\)\n  br ")]
    [InlineData(
        "func view() -> Slice<i64>\n    unsafe => return Raw.slice(null@raw/i64, 0)",
        @"\n  store ptr %t\d+, ptr (%\w+), align 8\n  (%t\d+) = getelementptr i8, ptr \1, i64 8\n  store i64 0, ptr \2, align 8\n")]
    [InlineData("func stop() => $abort(\"stop\")", @"\n  call void @__kimi_abort_message\(ptr %s0, ptr @\S+, i64 \d+\)\n  unreachable\n")]
    [InlineData(
        "func cut(a: ref/[4 of i32], n: isize) -> isize\n    let s = a[1..n]\n    return s.length",
        @"\n  call void @__kimi_abort\(i32 10, ptr @\S+, i64 \d+, i64 -2\)\n  unreachable\n")]
    public void AnIntrinsicIsWrittenByItsRow(string source, string pattern)
        => Assert.Matches(pattern, Parse(source).Text);

    // SPEC 5.1, 5.3 (DECISIONS R5-M92): p + n and p - n displace a raw pointer by n * stride(T) through the place writer's byte offset, a zero
    // stride keeping the pointer; == and != compare addresses, null the one pointer constant.
    [Theory]
    [InlineData(
        "func next(p: raw/i64, n: isize) -> raw/i64\n    var q: raw/i64 = null\n    unsafe => q = p + n\n    return q",
        @"= mul i64 %t\d+, 8\n  %t\d+ = getelementptr i8, ptr %t\d+, i64 %t\d+\n")]
    [InlineData("func back(p: raw/i64, n: isize) -> raw/i64\n    var q: raw/i64 = null\n    unsafe => q = p - n\n    return q", @"= mul i64 %t\d+, -8\n")]
    [InlineData("func same(p: raw/i32, q: raw/i32) -> bool => p == q", @"= icmp eq ptr %t\d+, %t\d+\n")]
    [InlineData("func empty(p: raw/i32) -> bool => p != null", @"= icmp ne ptr %t\d+, null\n")]
    [InlineData("func none() -> raw/i32 => null", @"\n  store ptr null, ptr %l0, align 8\n")]
    public void ARawPointerOperatorIsItsColumn(string source, string pattern)
        => Assert.Matches(pattern, Parse(source).Text);

    // SPEC 13.4, 3.4.3, 5.1 (DECISIONS R5-M143): a comparison operand of safe value-reference layers denotes its terminal Place, so each
    // operand is read through its layers, on either side: the raw pointers are compared, never a reference's own address. Each count is the
    // loads between the compared value and its operand's slot.
    [Theory]
    [InlineData("func same(p: ref/raw/i32, q: raw/i32) -> bool => p == q", "eq", 1, 0)]
    [InlineData("func differ(p: raw/i32, q: ref/raw/i32) -> bool => p != q", "ne", 0, 1)]
    [InlineData("func same(p: ref/raw/i32, q: ref/raw/i32) -> bool => p == q", "eq", 1, 1)]
    [InlineData("func same(p: uniq/raw/i32, q: raw/i32) -> bool => p == q", "eq", 2, 0)]
    public void AComparisonReadsEachOperandAtItsTerminal(string source, string predicate, int left, int right)
    {
        var compare = Assert.Single(Parse(source).Ir.Functions.SelectMany(static x => x.Instructions), x => x.Shape == $"icmp {predicate} ptr %, %");
        Assert.Equal([left, right], compare.Operands.Select(Loads));
    }

    // SPEC 13.4, 5.1 (DECISIONS R5-M143): LayeredComparisons runs natively in every run.
    [Fact]
    public void LayeredComparisonsRun()
        => ScalarEmissionTest.WriteFixture("RawComparisonLayers", Generate(LayeredComparisons), string.Empty);

    // SPEC 5.3: a raw pointer to a zero-sized Type is displaced by nothing, so p + n is the pointer itself.
    [Fact]
    public void AZeroStrideDisplacementKeepsThePointer()
    {
        const string Source = "func next(p: raw/(), n: isize) -> raw/()\n    var q: raw/() = null\n    unsafe => q = p + n\n    return q";
        var text = Assert.Single(ItemTexts(Analyze(Source, OutputKind.Library), "next"));
        Assert.DoesNotContain("getelementptr", text, StringComparison.Ordinal);
        Assert.DoesNotContain(" = mul ", text, StringComparison.Ordinal);
    }

    // SPEC 22.2 (DECISIONS R5-M92, R5-M145): DictionaryStorage's byte copy and nullable Alloc call the runtime routines both generators define
    // where a body calls them, the copy with its destination, source and count.
    [Fact]
    public void TheStorageBytePrimitivesAreTheirRows()
    {
        const string Source = "func more(d: uniq/Dictionary<i32, i32>) => d.reserve(4)\nfunc fit(d: uniq/Dictionary<i32, i32>) => d.shrinkToFit()\n";
        var c = Analyze(Source, OutputKind.Library);
        const string Copy = @"\n  call void @__kimi_transfer_bytes\(ptr %t\d+, ptr %t\d+, i64 %t\d+\)\n";
        Assert.Contains(ItemTexts(c, "grow"), static x => Regex.IsMatch(x, Copy));
        Assert.Contains(ItemTexts(c, "shrinkToFit"), static x => Regex.IsMatch(x, @"= call ptr @__kimi_try_allocate\(i64 %t\d+\)\n"));
    }

    // SPEC 21.4.3, 22.2 (DECISIONS R5-M92, R5-M145): each storage byte primitive's definition, the nullable Alloc's and the byte copy's, is one
    // runtime text spliced from its FunctionAbi, which a module whose bodies call them defines exactly once (the old generator's row went
    // with its selection at G4, DECISIONS R5-M160).
    [Fact]
    public void TheStorageBytePrimitivesAreDefinedOnceFromTheirTexts()
    {
        const string Source = "var d: Dictionary<i32, i32> = [1: 2]\nd.shrinkToFit()\n";
        var ir = Generate(Source);
        var primitives = new[] { (RuntimeAbi.TryAllocateBytes, LlvmModuleText.TryAllocate), (RuntimeAbi.TransferBytes, LlvmModuleText.TransferBytes) };
        foreach (var (abi, text) in primitives)
        {
            var definition = abi.GetDefinition(exported: false);
            Assert.StartsWith(definition, text, StringComparison.Ordinal);
            Assert.Single(Regex.Matches(text, Regex.Escape(definition)));
            Assert.Single(Regex.Matches(ir, Regex.Escape(definition)));
            Assert.Contains(text, ir, StringComparison.Ordinal);
        }
    }

    // SPEC 22.5.2 (DECISIONS R5-M92): each of the 7 fixed codes MIR's Fail carries, the five the served compiler functions name and the
    // builder's duplicate key and format codes, names its Abort reason (AbortReasons.Of), whose text begins with the code.
    [Fact]
    public void EveryMirFailCodeHasAReason()
    {
        CompilerFunctionKind[] served =
        [
            CompilerFunctionKind.StorageIndexBounds, CompilerFunctionKind.StorageMissingDictionaryKey, CompilerFunctionKind.StorageArgumentOutOfRange,
            CompilerFunctionKind.StorageCountOverflow, CompilerFunctionKind.StorageAllocationSizeExceeded,
        ];
        string[] codes = [.. served.Select(static x => IntrinsicDisposition.Of(x).Code!), "KIMI_E_DUPLICATE_KEY", "KIMI_E_FORMAT"];
        Assert.Equal(7, codes.Distinct().Count());
        Assert.All(codes, static x => Assert.StartsWith(x + ":", AbortReasons.All[AbortReasons.Of(x)].Text, StringComparison.Ordinal));
        Assert.Equal(-1, AbortReasons.Of("KIMI_E_UNKNOWN"));
    }

    // SPEC 22.6, IMPL 21.4.7 (DECISIONS R5-M89): a #LibraryImport function is called by its C signature, whose declaration its call
    // requests, once per symbol; an import no call reaches is not declared. A caller-location library callee is called since C3b (R5-M93);
    // the library bodies a Dictionary's tryInsert over an i32 key reaches, its key's equality shim included, are written since C8f
    // (R5-M126), and Dictionary.reserve's body since C8e (R5-M124).
    [Fact]
    public void AnImportIsCalledByItsCSignature()
    {
        var imported = Compilation.CreateForTest();
        imported.Project.ProjectFile.NativeRequirements[WindowsProfile.Target] = new(StringComparer.Ordinal) { ["codec"] = new() { Kind = "static" } };
        Assert.True(imported.Prepare(WindowsProfile.Target));
        const string Source =
            "group Native\n    #LibraryImport(\"codec\", \"mix\")\n    public unsafe func mix(a: i8, b: f64) -> f64\n    #LibraryImport(\"codec\", \"mix\")\n" +
            "    public unsafe func again(a: i8, b: f64) -> f64\n    #LibraryImport(\"codec\", \"idle\")\n    public unsafe func idle() -> ()\n" +
            "public func main()\n    var t: f64 = 0.0\n    unsafe => t = Native.mix(-1, 2.25)\n    unsafe => t = Native.again(1, t)\n";
        imported.Kotonoha.AddSource(new SourceDocument("Hello.kimi", Source));
        imported.Bind();
        imported.Binding.CheckStartup(OutputKind.Application);
        imported.Analyze();
        var text = Generate(imported);
        Assert.Single(Regex.Matches(text, @"\ndeclare double @mix\(i8, double\)\n"));
        Assert.Matches(@"= call double @mix\(i8 -1, double 0x4002000000000000\)\n", text);
        Assert.Matches(@"= call double @mix\(i8 1, double %t\d+\)\n", text);
        Assert.DoesNotContain("@idle", text, StringComparison.Ordinal);
        Assert.True(Analyze("func put(d: uniq/Dictionary<i32, i32>)\n    _ = d.tryInsert(1, 2)\n", OutputKind.Library).Generate(TextWriter.Null));
        Assert.True(Analyze("func grow(d: uniq/Dictionary<i32, i32>) => d.reserve(4)\n", OutputKind.Library).Generate(TextWriter.Null));
    }

    // DECISIONS R5-M58, R5-M89: an import edited without changing its C signature (a parameter renamed) keeps its signature: the
    // module's Write after the edit returns the same FunctionAbi, as the compilation's pooled module does across edits.
    [Fact]
    public void AnEditedImportKeepsItsSignature()
    {
        var c = Compilation.CreateForTest();
        c.Project.ProjectFile.NativeRequirements[WindowsProfile.Target] = new(StringComparer.Ordinal) { ["codec"] = new() { Kind = "static" } };
        Assert.True(c.Prepare(WindowsProfile.Target));
        const string Source = "group Native\n    #LibraryImport(\"codec\", \"mix\")\n    public unsafe func mix(a: i8, b: f64) -> f64\n" +
            "public func main()\n    var t: f64 = 0.0\n    unsafe => t = Native.mix(-1, t)\n";
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", Source));
        var module = new ModuleCodegen(c.Layout, c.Locations, c.Types, c.Binding.LibraryImports, c.Tests);
        var before = Signature(c, module);
        var original = Mix(c.Kotonoha.RootKoto);
        var donor = ParseTestHelper.ParseSuccess(Source.Replace("mix(a: i8", "mix(x: i8", StringComparison.Ordinal));
        var edited = Mix(donor.RootKoto);
        var replacement = ParseTestHelper.Detach(edited, new FunctionKoto(edited.CodeContext, "placeholder"));
        Assert.True(KotoHelper.Replace(original.Parent!, original, replacement));
        Assert.Same(before, Signature(c, module));

        static FunctionKoto Mix(Koto root) => KotoTree.Walk(root).OfType<FunctionKoto>().Single(static x => x.Name == "mix");

        // The program bound and analyzed, every body item written by the module, and the signature its Write gave the import.
        static FunctionAbi Signature(Compilation c, ModuleCodegen module)
        {
            Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
            c.Binding.CheckStartup(OutputKind.Application);
            c.Analyze();
            var program = c.BuildMir();
            var collector = new InstanceCollector(c.Types, c.Layout);
            var set = collector.Run(program, 1);
            var bodies = Enumerable.Range(0, set.Items.Length).Where(x => set.Items[x].Key.Kind == MonoKind.Body).ToArray();
            set.Mark(bodies);
            Assert.True(module.Write(program, collector, c.Binding.Startup, bodies, TextWriter.Null), string.Join(", ", module.Records));
            return Assert.IsType<FunctionAbi>(module.Import(Mix(c.Kotonoha.RootKoto).SymbolOf()!));
        }
    }

    // SPEC 7.7.4, 22.6 (DECISIONS R5-M146): a safe #LibraryImport function's Item, which has no body target, is called by the import's C
    // signature, as its direct call is (fail-before: one located Unsupported_Kd at the Value call).
    [Fact]
    public void AnImportsItemCallsTheImport()
    {
        var c = Analyze(
            "group Native\n    #LibraryImport(\"kernel32\", \"GetLastError\")\n    public func lastError() -> u32\nfunc last() -> u32\n" +
            "    let f = Native.lastError\n    return f()\n",
            OutputKind.Library);
        _ = Generate(c);
        Assert.Single(Regex.Matches(Assert.Single(ItemTexts(c, "last")), @"= call i32 @GetLastError\(\)\n"));
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
    // entry and the profile attributes).
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

    // DECISIONS R5-M146 (X2): every form of an accepted program is written, with no record (a requirement whose witness has no MIR body is
    // the collector's BodylessWitness gap at check, InstanceCollectorTest). A string's Move into another local is written since C2b
    // (DECISIONS R5-M87), a function's call since C3a, a generic callee's instance whose Keep of a T instanced at i32 needs no drop
    // included (R5-M78, R5-M89), a requirement's since C3b (R5-M93), a Builtin shim's since C8f (R5-M126) and a property witness's since
    // C3c (R5-M141); a Fn call of a formatting row is written since C9a (DECISIONS R5-M128), the Utf8Slice overload of writeLine included,
    // and Text.toString of an i32 since C9b wrote its target, the BuiltinFormat item (R5-M130); the records of a for loop's statement and
    // of the siblings its expansion synthesizes with its span ($for.iterator, $for.some, $for.none) are one at that location (DECISIONS
    // R5-M65), which the diagnostics could not order when each was keyed by its node. A static's initializer is written since C7b
    // (DECISIONS R5-M106), a constant one folded into its slot. The operator rows (DECISIONS R5-M76) write checked 128-bit arithmetic and a
    // comparison through an operand's ref layers since wide (R5-M143); they have a raw pointer column since C8a (R5-M92), which writes null
    // too; an erasure is written since C5b (DECISIONS R5-M104), into any target since C5c (R5-M108); an Array's capacity and an Array from
    // none since C8d (R5-M121); the place writer (DECISIONS R5-M83) reads an object borrow's payload since C6a (R5-M110); the rvalue rows
    // (DECISIONS R5-M87) write a Tuple aggregate and a zero-sized local's address, and since C8e (R5-M124) a Dictionary's length, a
    // Dictionary from none and its iteration's storage.
    [Theory]
    [InlineData("func same(p: ref/raw/i32, q: raw/i32) -> bool => p == q\n", OutputKind.Library)]
    [InlineData("func twice(a: f64) -> f64 => a + a\n", OutputKind.Library)]
    [InlineData("func id(x: i32) -> i32 => x\nfunc get() -> (i32) -> i32 => id\n", OutputKind.Library)]
    [InlineData("func none() -> usize => null@raw/i32@usize\n", OutputKind.Library)]
    [InlineData("func same(p: raw/i32, q: raw/i32) -> bool => p == q\n", OutputKind.Library)]
    [InlineData("func room(a: ref/Array<i32>) -> isize => a.capacity\n", OutputKind.Library)]
    [InlineData("let t = (1, 2)\n", OutputKind.Application)]
    [InlineData("let u = ()\nlet r = u@ref\n", OutputKind.Application)]
    [InlineData("func count(d: ref/Dictionary<i32, i32>) -> isize => d.length\n", OutputKind.Library)]
    [InlineData("func make() -> Array<i32> => []\n", OutputKind.Library)]
    [InlineData("func make() -> Dictionary<i32, i32> => [:]\n", OutputKind.Library)]
    [InlineData("struct Item\n    public var id: i32 = 7\nfunc take(v: objref/Item) -> i32 => v.id\n", OutputKind.Library)]
    [InlineData("func twice(a: Wrapping<i128>) -> Wrapping<i128> => a + a\n", OutputKind.Library)]
    [InlineData("func twice(a: i32) -> i32 => a\nlet x = twice(1)\n", OutputKind.Application)]
    [InlineData("let t = \"x\"\nlet u = t@move\n", OutputKind.Application)]
    [InlineData("func keep<T>(value: T) => ()\nkeep(1)\n", OutputKind.Application)]
    [InlineData("func name() -> string => \"x\"\nfunc show() => Console.writeLine(name())\n", OutputKind.Library)]
    [InlineData(
        "func same<T>(a: T, b: T) -> bool\n    T is Equatable\n    return a == b\nfunc use() -> bool => same(1, 2)\n",
        OutputKind.Library)]
    [InlineData("func show(text: Text.Utf8Slice) => Console.writeLine(text)\n", OutputKind.Library)]
    [InlineData("func show(x: i32) -> string => Text.toString(x)\n", OutputKind.Library)]
    [InlineData("func show(x: i32) -> string => \"x=\\(x)\"\n", OutputKind.Library)]
    [InlineData("func show(s: ref/string) -> string => \"\\(s)\"\n", OutputKind.Library)]
    [InlineData("func show(s: ref/string) -> string => \"x=\\(s)\"\n", OutputKind.Library)]
    [InlineData(
        "func show<T>(value: ref/T)\n    T is Utf8Format\n    Console.writeLine(\"x=\\(value)\")\nfunc use() -> ()\n    show(1)\n    show(\"s\")\n",
        OutputKind.Library)]
    [InlineData(ForLoop, OutputKind.Library)]
    [InlineData(DictionaryLoop, OutputKind.Library)]
    [InlineData("group Registry\n    public let limit: i32 = 4\n", OutputKind.Library)]
    [InlineData("func twice(a: i32) -> i32 => a\n", OutputKind.Library)]
    public void EveryAcceptedFormIsWritten(string source, OutputKind output)
    {
        var c = Analyze(source, output);
        using var writer = new StringWriter();
        Assert.True(c.Generate(writer), string.Join(", ", Records(c)));
        Assert.NotEqual(0, writer.ToString().Length);
        Assert.Empty(Records(c));
        Assert.False(c.Diagnostics.HasErrorsIn(DiagnosticPartition.Emission));
        Assert.True(c.WriteIr(TextWriter.Null, out var failure));
        Assert.Null(failure);
    }

    // I1: the same program gives the same text, in one compilation and in two.
    [Theory]
    [InlineData(Branches, OutputKind.Application)]
    [InlineData(Strings, OutputKind.Library)]
    [InlineData(Arithmetic, OutputKind.Library)]
    [InlineData(Conversions, OutputKind.Library)]
    [InlineData(Drops, OutputKind.Application)]
    [InlineData(Places, OutputKind.Library)]
    [InlineData(Rvalues, OutputKind.Library)]
    [InlineData(Acquisitions, OutputKind.Library)]
    [InlineData(Adaptations, OutputKind.Library)]
    [InlineData(Calls, OutputKind.Library)]
    [InlineData(Imports, OutputKind.Library)]
    [InlineData(Raws, OutputKind.Library)]
    [InlineData(RequirementWitnesses, OutputKind.Application)]
    [InlineData(CallerLocations, OutputKind.Application)]
    [InlineData(Glues, OutputKind.Library)]
    [InlineData(Flags, OutputKind.Library)]
    [InlineData(Opens, OutputKind.Library)]
    [InlineData(Closures, OutputKind.Library)]
    [InlineData(Owners, OutputKind.Library)]
    [InlineData(Erasures, OutputKind.Library)]
    [InlineData(Statics, OutputKind.Library)]
    [InlineData(Objects, OutputKind.Library)]
    [InlineData(Releases, OutputKind.Library)]
    [InlineData(Virtuals, OutputKind.Library)]
    [InlineData(Updates, OutputKind.Library)]
    [InlineData(Comparisons, OutputKind.Library)]
    [InlineData(Arrays, OutputKind.Library)]
    [InlineData(Dictionaries, OutputKind.Library)]
    [InlineData(Shims, OutputKind.Library)]
    [InlineData(Buffers, OutputKind.Library)]
    [InlineData(FormatShims, OutputKind.Library)]
    [InlineData(UserFormats, OutputKind.Library)]
    [InlineData(Writers, OutputKind.Library)]
    [InlineData(Interpolations, OutputKind.Library)]
    [InlineData(Verifications, OutputKind.Application)]
    [InlineData(Phases, OutputKind.Application)]
    [InlineData(Witnesses, OutputKind.Library)]
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
        var module = new ModuleCodegen(c.Layout, c.Locations, c.Types, c.Binding.LibraryImports, c.Tests);
        Assert.True(module.Write(program, collector, c.Binding.Startup, [start], TextWriter.Null));
        new FunctionCodegen(module, c.Types).Write(program, collector, unused, TextWriter.Null);
        Assert.Equal([new(DiagnosticCode.InternalInvariant_Kd, set.Items[unused].Site)], module.Records);

        static int Of(CheckedSet set, string? name) => Enumerable.Range(0, set.Items.Length).Single(x => set.Items[x].Key.Body.Def.Decl?.Name == name);
    }

    // K11 with R4-M28 (K package 4.4, 4.10): a Library generates every root but the body of a closed override whose slot TyCtxt.Applies
    // refutes at its owning layer, which is checked only (the old generator skips it too); where the slot applies, the override is generated.
    // A Closure and a function nested in its body are no output roots: the override's runtime targets reach them, so they follow its mark
    // (DECISIONS R5-M153; fail-before: both generated where the slot is refuted).
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ALibraryLeavesOutTheOverrideOfARefutedSlot(bool applies)
    {
        var c = Analyze(
            "contract Marker\nstruct Thing\n" + (applies ? "    Self is Marker\n" : string.Empty) + "    public init() => ()\nopen struct Base<T>\n" +
            "    public init() => ()\n    public virtual func mark(self: objref/Self) -> i32\n        T is Marker\n        return 5\n" +
            "struct Plain : Base<Thing>\n    public init() => ()\n    override func mark(self: objref/Self) -> i32\n" +
            "        let add = func [] (x: i32) -> i32 => x + x\n        func twice(x: i32) -> i32 => x * 2\n        return add(3) + twice(2)\n",
            OutputKind.Library);
        Assert.True(c.Accepts, MinimalEmissionTest.Describe(c, null));
        _ = c.Generate(TextWriter.Null);
        var set = c.Instances!;
        Assert.Equal(applies, Assert.Single(Generated(set), static x => x.Name == "mark").Generated);
        Assert.Equal(applies, Assert.Single(Generated(set), static x => x.Name == "twice").Generated);
        var closure = Assert.Single(Enumerable.Range(0, set.Items.Length), x => set.Items[x].Key.Body.Def.Kind == MirBodyKind.Closure);
        Assert.Equal(applies, set.IsGenerated(closure));
    }

    // DECISIONS R5-M153, against over-exclusion: no Closure's body is a Library output root (Generates), yet each one a generated body makes
    // at its Aggregate(Closure) site is Generated through that target: in an accessor's get and set, a destructor, a default template and a
    // static's initializer.
    [Fact]
    public void ALibraryReachesEachClosureItsBodiesMake()
    {
        var c = Analyze(
            "struct Gauge\n    var stored: i32 = 2\n    public computed count: i32\n        get() -> i32\n" +
            "            let f = func [] (x: i32) -> i32 => x + 1\n            return f(self.stored)\n        set(value: i32) -> ()\n" +
            "            let g = func [] (x: i32) -> i32 => x * 2\n            self.stored = g(value)\n    public init() => ()\n    drop\n" +
            "        let h = func [] (y: i32) -> i32 => y + 1\n        _ = h(2)\n" +
            "func apply(x: i32, f: (i32) -> i32 = func [] (y: i32) -> i32 => y + 1) -> i32 => f(x)\n" +
            "group Registry\n    public let limit: i32 = (func [] (y: i32) -> i32 => y + 1)(3)\n",
            OutputKind.Library);
        _ = Generate(c);
        var program = c.BuildMir();
        var collector = new InstanceCollector(c.Types, c.Layout);
        var set = collector.Run(program, 1);
        var closures = Enumerable.Range(0, set.RootCount).Where(x => set.Items[x].Key.Body.Def.Kind == MirBodyKind.Closure).ToArray();
        Assert.Equal(5, closures.Length);
        Assert.All(closures, x => Assert.False(collector.Generates(program, x)));
        Assert.All(closures, x => Assert.True(c.Instances!.IsGenerated(c.Instances.Find(set.Items[x].Key))));
    }

    // DECISIONS R5-M146: every milestone check accepts is generated with no record; one check rejects is not generated and adds no record.
    [Theory]
    [MemberData(nameof(HirColumnsTest.Corpus), MemberType = typeof(HirColumnsTest))]
    public void TheMilestonesGenerate(string name)
    {
        if (!name.StartsWith("milestone/", StringComparison.Ordinal))
        {
            return;
        }

        var c = Analyze(File.ReadAllText(DiagnosticCorpus.RepositoryPath("tests", "milestones", name["milestone/".Length..] + ".kimi")));
        Assert.Equal(c.Accepts, c.Generate(TextWriter.Null));
        Assert.False(c.Diagnostics.HasErrorsIn(DiagnosticPartition.Emission));
        if (c.Accepts)
        {
            Assert.Empty(Records(c));
        }
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
    [InlineData(Rvalues, OutputKind.Library)]
    [InlineData(Acquisitions, OutputKind.Library)]
    [InlineData(Adaptations, OutputKind.Library)]
    [InlineData(Calls, OutputKind.Library)]
    [InlineData(Imports, OutputKind.Library)]
    [InlineData(Raws, OutputKind.Library)]
    [InlineData(RequirementWitnesses, OutputKind.Application)]
    [InlineData(CallerLocations, OutputKind.Application)]
    [InlineData(Glues, OutputKind.Library)]
    [InlineData(Flags, OutputKind.Library)]
    [InlineData(Opens, OutputKind.Library)]
    [InlineData(Closures, OutputKind.Library)]
    [InlineData(Owners, OutputKind.Library)]
    [InlineData(Erasures, OutputKind.Library)]
    [InlineData(Statics, OutputKind.Library)]
    [InlineData(Objects, OutputKind.Library)]
    [InlineData(Releases, OutputKind.Library)]
    [InlineData(Virtuals, OutputKind.Library)]
    [InlineData(Updates, OutputKind.Library)]
    [InlineData(Comparisons, OutputKind.Library)]
    [InlineData(Arrays, OutputKind.Library)]
    [InlineData(Dictionaries, OutputKind.Library)]
    [InlineData(Shims, OutputKind.Library)]
    [InlineData(Buffers, OutputKind.Library)]
    [InlineData(FormatShims, OutputKind.Library)]
    [InlineData(UserFormats, OutputKind.Library)]
    [InlineData(Writers, OutputKind.Library)]
    [InlineData(Interpolations, OutputKind.Library)]
    [InlineData(Verifications, OutputKind.Application)]
    [InlineData(Phases, OutputKind.Application)]
    [InlineData(Witnesses, OutputKind.Library)]
    public void WarmGenerationAllocatesNothing(string source, OutputKind output)
    {
        var c = Analyze(source, output);
        var written = true;
        var allocated = AllocationMeasurement.Measure(() => written &= c.Generate(TextWriter.Null));
        Assert.True(written);
        Assert.Equal(0, allocated);
    }

    // G308 S7: the program of P_n's equality (MirBuildTest.Dag) at n = 12 rebuilds (Binding and the MIR) and writes its module warm without
    // allocating.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmDagComparisonRebuildsAndWritesWithoutAllocating()
    {
        var c = Analyze(MirBuildTest.Dag(12, MirBuildTest.DagOperator), OutputKind.Library);
        Assert.True(c.Generate(TextWriter.Null), string.Join(", ", Records(c)));
        var written = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => written &= c.Generate(TextWriter.Null)));
        Assert.True(written);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Binding.Bind(BindingMode.Final);
            c.BuildMir();
        }));
    }

    // G308 S5 (V16): the module of S1's operator and of S4's Contract mapping over P_n's Tuple (MirBuildTest.Dag) at n = 6, 9, 12, a Library
    // generating every root, has an IR text length and a function count linear in n, the distinct Types (the ratio rule of
    // MirBuildTest.DropTreesScaleLinearly). Fails before G308: the comparison's code is written per leaf, 2^n of them, so the text's second
    // increment is about 8 times the first.
    [Theory]
    [InlineData(MirBuildTest.DagOperator)]
    [InlineData(MirBuildTest.DagContract)]
    public void ADagComparisonsModuleGrowsWithItsDistinctTypes(string form)
    {
        var modules = new[] { 6, 9, 12 }.Select(n => Generate(Analyze(MirBuildTest.Dag(n, form), OutputKind.Library))).ToArray();
        var lengths = modules.Select(static x => (long)x.Length).ToArray();
        var functions = modules.Select(static x => (long)Regex.Count(x, "(?m)^define ")).ToArray();
        var shown = $"IR text {string.Join(", ", lengths)}; functions {string.Join(", ", functions)}";
        Assert.True(lengths[2] - lengths[1] <= (2 * (lengths[1] - lengths[0])) + 8, shown);
        Assert.True(functions[2] - functions[1] <= (2 * (functions[1] - functions[0])) + 8, shown);
    }

    // The library bodies an Application reaches (K12, DECISIONS R4-M13a, R5-M77), where generation decides them: the program analyzed and
    // the definitions of the body items its generation marks Generated; any record fails (DECISIONS R5-M146).
    internal static (Compilation C, Koto[] Definitions) GeneratedDefinitions(string source)
    {
        var c = Analyze(source);
        Assert.True(c.Accepts, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Generate(TextWriter.Null), string.Join(", ", Records(c)));

        var set = c.Instances!;
        var generated = Enumerable.Range(0, set.Items.Length).Where(set.IsGenerated).ToArray();
        Assert.NotEmpty(generated); // The startup body at least: the mark was made.
        return (c, [.. generated.Where(x => set.Items[x].Key.Kind == MonoKind.Body).Select(x => set.Items[x].Key.Body.Def.Decl?.Declaration).OfType<Koto>()]);
    }

    // A program analyzed for an output form; a source that declares a test is a test build's test-only source, its catalog discovered
    // after acceptance as Project discovers it.
    private static Compilation Analyze(string source, OutputKind output = OutputKind.Application)
    {
        var c = Compilation.CreateForTest();
        c.IsTestBuild = source.Contains("#Test", StringComparison.Ordinal);
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", source, isTestOnly: c.IsTestBuild));
        c.Bind();
        if (!c.IsTestBuild)
        {
            c.Binding.CheckStartup(output);
            c.Analyze();
            return c;
        }

        c.Binding.CheckTestStartup();
        if (c.Analyze().IsVerified)
        {
            c.Tests.Discover(c);
        }

        return c;
    }

    private static string Generate(string source) => Generate(Analyze(source));

    // The definitions of a definition's body items (null: the top-level statements), each written directly whatever records the module's
    // other items make: an instance a call names (the call is C3a's row) or a body beside a broken one. Every body item is an output
    // root.
    private static string[] ItemTexts(Compilation c, string? name)
    {
        var (program, collector, module, bodies) = WrittenModule(c);
        return [.. bodies.Where(x => collector.Set.Items[x].Key.Body.Def.Decl?.Name == name).Select(x =>
        {
            using var writer = new StringWriter();
            new FunctionCodegen(module, c.Types).Write(program, collector, x, writer);
            return writer.ToString();
        })];
    }

    // A program's module written with every body item an output root (ItemTexts), whatever records it makes: its MIR, collector, module and
    // body items, whose items a test then writes directly.
    private static (MirProgram Program, InstanceCollector Collector, ModuleCodegen Module, int[] Bodies) WrittenModule(Compilation c)
    {
        var program = c.BuildMir();
        var collector = new InstanceCollector(c.Types, c.Layout);
        var set = collector.Run(program, 1);
        var bodies = Enumerable.Range(0, set.Items.Length).Where(x => set.Items[x].Key.Kind == MonoKind.Body).ToArray();
        set.Mark(bodies);
        var module = new ModuleCodegen(c.Layout, c.Locations, c.Types, c.Binding.LibraryImports, c.Tests);
        _ = module.Write(program, collector, c.Binding.Startup, bodies, TextWriter.Null);
        return (program, collector, module, bodies);
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

    // The pointer loads between a value and the slot it was first loaded from: 0 for a slot's own value.
    private static int Loads(IrValue value) => value.Opcode == "load" && value.Operands[0].Opcode == "load" ? 1 + Loads(value.Operands[0]) : 0;

    private static (string Code, string? Text)[] Records(Compilation c)
        => [.. TestDiagnostics.Of(c).Where(static x => x.Severity == DiagnosticSeverity.Error).Select(static x => (x.Code, x.Text))];
}
