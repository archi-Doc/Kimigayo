// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.RegularExpressions;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// The statics of MIR code generation (R5 C7b; SPEC 22.4.3, 22.4.4, 22.5.1, 22.5.2; DECISIONS R5-M106): a StaticInit item's slot, state and
// ensure function, an access's state test, the fold of a constant StaticInit, and statics natively.
public sealed partial class FunctionCodegenTest
{
    // A Library's statics: first-access i32, bool and i128 ones; constant ones, legacy's folded set (a signed literal, a bool, a
    // parenthesized literal) and f32 and f64 literals; a non-literal 1 + 2; read by functions and by both instances of a generic one.
    private const string Statics =
        "group Values\n    public let count: i32 = start()\n    public let flag: bool = Values.count > 0\n    public let wide: i128 = make()\n" +
        "    public let minus: i64 = -5\n    public let yes: bool = true\n    public let paren: i32 = (7)\n    public let single: f32 = 0.1\n" +
        "    public let tenth: f64 = 0.1\n    public let sum: i32 = 1 + 2\n    func start() -> i32 => 4\n" +
        "    func make() -> i128 => 170141183460469231731687303715884105727\n" +
        "func read() -> i32 => Values.count + Values.paren + Values.sum\nfunc pick<T>(value: T) -> i32 => Values.count\n" +
        "func both() -> i32 => pick(1) + pick(true)\n" +
        "func others() -> bool => Values.flag and Values.yes and Values.minus < 0 and Values.single > 0.0 and Values.tenth > 0.0 and Values.wide > 0\n";

    // SPEC 22.4.3 (DECISIONS R5-M106): first-access statics initialized once, in dependency order, each printing when it runs: char, f32,
    // f64 and bool ones folded or not, one read through a generic function in a loop, and one whose initializer reads another.
    public static TheoryData<string, string, string> StaticPrograms => new()
    {
        {
            "StaticRows",
            "group Values\n    public let count: i32 = start()\n    public let letter: char = 'k'\n    public let small: f32 = 0.25\n" +
            "    public let flag: bool = check()\n    public let tenth: f64 = 0.1\n    public let first: i64 = 41\n" +
            "    public let next: i64 = Values.first + 1\n" +
            "    func start() -> i32\n        Console.writeLine(\"start\")\n        return 3\n" +
            "    func check() -> bool\n        Console.writeLine(\"check\")\n        return Values.count == 3\n" +
            "func read<T>(value: T) -> i32 => Values.count\nvar total: i64 = 0\nvar i: i32 = 0\nwhile i < 4\n" +
            "    total = total + Values.next + read(i)@i64\n    i += 1\n" +
            "require total == 180 and Values.flag and Values.letter == 'k' and Values.small == 0.25 and Values.tenth == 0.1 else => $abort(\"statics\")\n" +
            "Console.writeLine(\"done\")\n",
            "start\ncheck\ndone\n"
        },
    };

    [Theory]
    [MemberData(nameof(StaticPrograms))]
    public void StaticProgramsRun(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture(name, source, stdout);

    // SPEC 22.4.3 (DECISIONS R5-M106): a first-access static's slot, zero, and its state, Not started, are globals of its StaticInit item; its
    // ensure function Aborts with KIMI_E_STATIC_CYCLE at the location it receives when the slot has started, else marks it Initializing,
    // calls the StaticInit into the slot (a bool widened) and marks it Initialized.
    [Theory]
    [InlineData("count", "i32", 4, "call i32 @()", "store i32 %, ptr @, align 4")]
    [InlineData("flag", "i8", 1, "call i1 @()", "zext i1 % to i8", "store i8 %, ptr @, align 1")]
    [InlineData("wide", "i128", 16, "call i128 @()", "store i128 %, ptr @, align 16")]
    public void AStaticIsInitializedOnceByItsEnsureFunction(string name, string storage, int alignment, params string[] initialize)
    {
        var c = Analyze(Statics, OutputKind.Library);
        var ir = GeneratedIr.Parse(Generate(c));
        var definition = Assert.Single(ItemTexts(c, name));
        var slot = Regex.Match(definition, $@"(?m)^@(__kimi_static\d+) = internal global {storage} zeroinitializer, align {alignment}$").Groups[1].Value;
        Assert.Matches($@"(?m)^@{slot}_state = internal global i8 0, align 1$", definition);
        var ensure = ir.Function(slot + "_ensure");
        Assert.Equal("internal void (ptr, i64)", ensure.Signature);
        Assert.Equal(
            ["load i8, ptr @, align 1", "icmp ne i8 %, 0", "br i1 %, label %, label %", "call void @(i32 17, ptr %, i64 %, i64 -2)", "unreachable",
                "store i8 1, ptr @, align 1", .. initialize, "store i8 2, ptr @, align 1", "ret void"],
            ensure.Instructions.Select(static x => x.Shape));
        Assert.Equal(ensure.Parameters, ensure.Instructions[3].Operands);
        Assert.Same(Assert.Single(FunctionsOf(ir, c, name)), ensure.Instructions[6].Callee);
        Assert.Same(ir.Function("__kimi_abort"), ensure.Instructions[3].Callee);
    }

    // SPEC 22.4.3, 22.5.1 (DECISIONS R5-M106): an access of a slot that is not a constant reads its state and, unless Initialized, calls its
    // ensure function with the start of the Place expression, its qualifier included; both paths reach the access. Both instances of a
    // generic reader access the one slot (SPEC 22.4.4: one key per Field here).
    [Fact]
    public void AnAccessEnsuresItsSlotUnlessInitialized()
    {
        var c = Analyze(Statics, OutputKind.Library);
        var text = Generate(c);
        var ir = GeneratedIr.Parse(text);
        var ensure = ir.Function(Regex.Match(Assert.Single(ItemTexts(c, "count")), @"define internal void @(__kimi_static\d+_ensure)\(").Groups[1].Value);
        var read = Assert.Single(FunctionsOf(ir, c, "read"));
        Assert.Equal(2, read.Instructions.Count(static x => x.Shape == "icmp eq i8 %, 2")); // count's and sum's; paren folds.
        var test = read.Blocks.First(static x => x.Instructions.Any(static y => y.Shape == "icmp eq i8 %, 2"));
        Assert.Equal(["load i8, ptr @, align 1", "icmp eq i8 %, 2", "br i1 %, label %, label %"], test.Instructions.TakeLast(3).Select(static x => x.Shape));
        var (ready, cold) = (test.Successors[0], test.Successors[1]);
        var location = LocationText(Statics, "Values.count + Values.paren");
        Assert.Equal([$"call void @(ptr @, i64 {location.Length})", "br label %"], cold.Instructions.Select(static x => x.Shape));
        Assert.Same(ensure, cold.Instructions[0].Callee);
        Assert.Contains(Constant(location), text);
        Assert.Equal([ready], cold.Successors);
        Assert.Equal(3, read.Instructions.Count(static x => x.Shape == "load i32, ptr @, align 4")); // Each slot's value.
        Assert.All(FunctionsOf(ir, c, "pick"), x => Assert.Same(ensure, Assert.Single(CallsOf(x)).Callee));
        Assert.Equal(2, FunctionsOf(ir, c, "pick").Length);
    }

    // Package C 3.7's fold (DECISIONS R5-M106): a StaticInit whose block 0 holds only storage markers and one _0 = Use(Const), then Return,
    // is its slot's constant, spelled as an operand's (a float's exact double bits), with no state, ensure function or function, and its
    // reads test nothing; the old generator's folded set (signed and bool literals, parenthesized) is among them, 1 + 2 is not.
    [Theory]
    [InlineData("minus", "i64 -5, align 8")]
    [InlineData("yes", "i8 1, align 1")]
    [InlineData("paren", "i32 7, align 4")]
    [InlineData("single", "float 0x3FB99999A0000000, align 4")]
    [InlineData("tenth", "double 0x3FB999999999999A, align 8")]
    public void AConstantStaticIsItsSlotsValue(string name, string constant)
    {
        var c = Analyze(Statics, OutputKind.Library);
        var ir = GeneratedIr.Parse(Generate(c));
        Assert.Matches($@"^@__kimi_static\d+ = internal unnamed_addr constant {constant}\n$", Assert.Single(ItemTexts(c, name)));
        Assert.Matches(@"define internal i32 @__kimi_f\d+\(", Assert.Single(ItemTexts(c, "sum")));
        Assert.Equal(2, Assert.Single(FunctionsOf(ir, c, "others")).Instructions.Count(static x => x.Shape == "icmp eq i8 %, 2")); // flag, wide.
    }
}
