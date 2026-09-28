// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

// G10 (SPEC §21.2–21.3): shared generic bodies call ordinary concrete functions.
public class SharedConcreteCallTest
{
    [Theory]
    [InlineData("Result", "func twice(n: i32) -> i32 => n * 2\nfunc measure<T>(value: ref/T) -> i32\n    return twice(3) + 1\nlet v: i64 = 3\nrequire measure(v) == 7 else => $abort(\"value\")", "")]
    [InlineData("Unit", "func note(n: i32)\n    require n == 3 else => $abort(\"arg\")\n    Console.writeLine(\"note\")\nfunc inspect<T>(value: ref/T)\n    note(3)\nlet v: i64 = 3\ninspect(v)\nlet b = true\ninspect(b)", "note\nnote\n")]
    [InlineData("Wide", "func twice(n: i64) -> i64 => n * 2\nfunc check(n: i64, m: i8) -> bool => n == 6 and m == -2\nfunc measure<T>(value: ref/T) -> bool\n    let m: i8 = -2\n    return check(twice(3), m)\nlet v: i32 = 3\nrequire measure(v) else => $abort(\"value\")", "")]
    [InlineData("Operators", "func scale<T>(value: ref/T) -> i16\n    let a: i16 = 300\n    return a * 2 - 100\nfunc le<T>(value: ref/T, x: i16, y: i16) -> bool => x <= y\nfunc ge<T>(value: ref/T, x: i64, y: i64) -> bool => x >= y\nfunc ne<T>(value: ref/T, x: i8, y: i8) -> bool => x != y\nlet v: bool = true\nrequire scale(v) == 500 else => $abort(\"scale\")\nrequire le(v, 3, 3) and not le(v, 4, 3) else => $abort(\"le\")\nrequire ge(v, -1, -1) and not ge(v, -2, -1) else => $abort(\"ge\")\nrequire ne(v, 1, 2) and not ne(v, -5, -5) else => $abort(\"ne\")", "")]
    [InlineData("Unary", "func flip<T>(value: ref/T, b: bool) -> bool => not b\nfunc negate<T>(value: ref/T, n: i16) -> i16 => -n\nfunc keep<T>(value: ref/T, n: i64) -> i64 => +n\nlet v: bool = true\nrequire not flip(v, true) and flip(v, false) else => $abort(\"not\")\nrequire negate(v, 7) == -7 and negate(v, -32767) == 32767 else => $abort(\"neg\")\nrequire keep(v, -5) == -5 else => $abort(\"pos\")", "")]
    [InlineData("Division", "func quotient<T>(value: ref/T, a: i32, b: i32) -> i32 => a / b\nfunc remainder<T>(value: ref/T, a: i64, b: i64) -> i64 => a % b\nlet v: bool = true\nrequire quotient(v, -7, 2) == -3 and remainder(v, -7, 3) == -1 and remainder(v, 7, -3) == 1 else => $abort(\"div\")", "")]
    [InlineData("Unsigned", "func mix<T>(value: ref/T, a: u8, b: u8) -> u8\n    let c: u8 = 200\n    return a + b + c\nfunc ratio<T>(value: ref/T, a: u32, b: u32) -> u32 => a / b + a % b\nfunc above<T>(value: ref/T, a: u64, b: u64) -> bool => a > b\nlet v: bool = true\nrequire mix(v, 20, 30) == 250 else => $abort(\"mix\")\nrequire ratio(v, 4000000000, 7) == 571428574 else => $abort(\"ratio\")\nrequire above(v, 18446744073709551615, 1) and not above(v, 1, 2) else => $abort(\"above\")", "")]
    [InlineData("Mutable", "func f<T>(value: ref/T) -> i16\n    var x: i16 = 1\n    x += 2\n    x = x * 3\n    return x\nlet v: bool = true\nrequire f(v) == 9 else => $abort(\"v\")", "")]
    [InlineData("Callback", "func apply<T>(value: ref/T, f: (i64) -> i64) -> i64 => f(21)\nlet g: (i64) -> i64 = func (n: i64) => n * 2\nlet v: bool = true\nrequire apply(v, g@move) == 42 else => $abort(\"callback\")", "")]
    [InlineData("BoolEquality", "func same<T>(value: ref/T, a: bool, b: bool) -> bool => a == b\nfunc differ<T>(value: ref/T, a: bool, b: bool) -> bool => a != b\nlet v: i32 = 1\nrequire same(v, true, true) and not same(v, true, false) and differ(v, false, true) and not differ(v, false, false) else => $abort(\"bool\")", "")]
    public void ExecutesConcreteCallsFromSharedBodies(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("SharedConcreteCall" + name, source, stdout);

    [Theory]
    [InlineData("DivisionZero", "func quotient<T>(value: ref/T, a: i32, b: i32) -> i32 => a / b\nlet v: bool = true\nlet r = quotient(v, 1, 0)", "Hello.kimi:1:58: abort KIMI_E_INT_DIV_ZERO: Integer division or remainder by zero\n")]
    [InlineData("RemainderOverflow", "func remainder<T>(value: ref/T, a: i8, b: i8) -> i8 => a % b\nlet v: bool = true\nlet r = remainder(v, -128, -1)", "Hello.kimi:1:56: abort KIMI_E_INT_OVERFLOW: Integer overflow\n")]
    [InlineData("UnsignedOverflow", "func mix<T>(value: ref/T, a: u8, b: u8) -> u8 => a + b\nlet v: bool = true\nlet r = mix(v, 200, 56)", "Hello.kimi:1:50: abort KIMI_E_INT_OVERFLOW: Integer overflow\n")]
    public void CheckedSharedDivisionAborts(string name, string source, string stderr)
        => ScalarEmissionTest.EmitFixture("SharedConcreteCall" + name, source, string.Empty, 1, stderr);

    [Fact]
    public void CheckedSharedNegationAborts()
        => ScalarEmissionTest.EmitFixture("SharedConcreteCallNegationOverflow", "func negate<T>(value: ref/T, n: i8) -> i8 => -n\nlet v: bool = true\nlet r = negate(v, -128)", string.Empty, 1, "Hello.kimi:1:46: abort KIMI_E_INT_OVERFLOW: Integer overflow\n");
}
