// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.RegularExpressions;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// The Builtin shims of MIR code generation (R5 C8f; SPEC 12.4.3, 13.3.3, 13.4, 13.4.1, 22.5.1; DECISIONS R5-M126): one caller-location
// function per Builtin item under its requirement's signature, a comparison over F6's tree with the operator table's leaves, an arithmetic
// shim the row of its Contract.
public sealed partial class FunctionCodegenTest
{
    // A Library whose generic comparisons and arithmetic run on built-in Types: equality of i32, f64, Unit, a Tuple and a Tuple holding a
    // user witness; ordering of string and of a Tuple; addition of i32 and Wrapping<u8>; negation of f64.
    private const string Shims =
        "func equal<T>(left: ref/T, right: ref/T) -> bool\n    T is Equatable\n    return left == right\n" +
        "func order<T>(left: ref/T, right: ref/T) -> i32\n    T is Comparable\n    return left.compare(right)\n" +
        "func sum<T>(left: ref/T, right: ref/T) -> T\n    T is Addable<T>\n    T.(Addable<T>).Output is T\n    return left + right\n" +
        "func negate<T>(value: ref/T) -> T\n    T is Negatable\n    T.(Negatable).Output is T\n    return -value\n" +
        "struct Key\n    Self is Equatable\n    public let number: i32\n    public init(number: i32) => self.number = number\n" +
        "    public func equals(self: ref/Self, other: ref/Self) -> bool => self.number == other.number\n" +
        "func ints(a: ref/i32, b: ref/i32) -> bool => equal(a, b)\nfunc floats(a: ref/f64, b: ref/f64) -> bool => equal(a, b)\n" +
        "func units(a: ref/(), b: ref/()) -> bool => equal(a, b)\nfunc texts(a: ref/string, b: ref/string) -> i32 => order(a, b)\n" +
        "func pairs(a: ref/(i32, string), b: ref/(i32, string)) -> bool => equal(a, b)\n" +
        "func ranks(a: ref/(i32, string), b: ref/(i32, string)) -> i32 => order(a, b)\n" +
        "func keys(a: ref/(Key, i32), b: ref/(Key, i32)) -> bool => equal(a, b)\n" +
        "func add(a: ref/i32, b: ref/i32) -> i32 => sum(a, b)\nfunc wrap(a: ref/Wrapping<u8>, b: ref/Wrapping<u8>) -> Wrapping<u8> => sum(a, b)\n" +
        "func flip(a: ref/f64) -> f64 => negate(a)\n";

    // SPEC 13.3.3 (DECISIONS R5-M126, ruling Q5): the operator table's Contract column and Binding's ArithmeticContracts.Operator, composed
    // with the MIR builder's operator for that token, are two statements of SPEC 13.3.3's table, the second one in a syntax token the
    // Backend does not read: for each ordinary arithmetic Contract and Negatable, the column's row is the MIR operator the builder writes for
    // the Contract's operator on i32 operands, one row each; a comparison or Left Contract has none.
    [Fact]
    public void TheArithmeticContractColumnIsTheBuildersOperator()
    {
        KimiDeclarationId[] contracts =
        [
            KimiDeclarationId.Addable, KimiDeclarationId.Subtractable, KimiDeclarationId.Multipliable, KimiDeclarationId.Dividable,
            KimiDeclarationId.RemainderProvider, KimiDeclarationId.Negatable,
        ];
        var unary = Enum.GetValues<MirBinaryOp>().Length;
        var rows = new List<int>();
        foreach (var contract in contracts)
        {
            var token = ArithmeticContracts.Operator(contract);
            var spelling = token switch
            {
                KotoKind.Plus => "+",
                KotoKind.Minus or KotoKind.PrefixMinus => "-",
                KotoKind.Asterisk => "*",
                KotoKind.Slash => "/",
                KotoKind.Percent => "%",
                _ => throw new InvalidOperationException($"{contract} has no operator."),
            };
            var source = token == KotoKind.PrefixMinus ? $"func f(a: i32) -> i32 => {spelling}a\n" : $"func f(a: i32, b: i32) -> i32 => a {spelling} b\n";
            var program = MirBuildTest.Compile(source).BuildMir();
            var body = Assert.Single(program.Bodies.ToArray(), static x => x.Key.Decl?.Name == "f");
            var rvalue = Assert.Single(body.Statements.ToArray(), static x => x.Rvalue.Kind is MirRvalueKind.BinaryOp or MirRvalueKind.UnaryOp).Rvalue;
            var row = (rvalue.Kind == MirRvalueKind.UnaryOp ? unary : 0) + rvalue.Op;
            Assert.Equal(row, FunctionCodegen.ArithmeticRow(contract));
            rows.Add(row);
        }

        Assert.Equal(contracts.Length, rows.Distinct().Count());
        Assert.Equal(-1, FunctionCodegen.ArithmeticRow(KimiDeclarationId.Equatable));
        Assert.Equal(-1, FunctionCodegen.ArithmeticRow(KimiDeclarationId.LeftAddable));
    }

    // PD3, K package 4.3 (DECISIONS R5-M126): one function per Builtin item, keyed by its function and erased Self, under its requirement's
    // signature in its instance, caller-location (SPEC 22.5.1): two shared borrows (one for negation) and the location pair; the generic
    // bodies' Requirement calls call it with their own location.
    [Fact]
    public void EachBuiltinItemIsOneCallerLocationFunction()
    {
        var c = Analyze(Shims, OutputKind.Library);
        var text = Generate(c);
        var ir = GeneratedIr.Parse(text);
        var shims = ShimsOf(ir, text);
        Assert.Equal(10, shims.Length); // i32, f64, (), (i32, string), (Key, i32) equals; string, (i32, string) compare; i32, Wrapping<u8> added; f64 negated.
        Assert.Equal([3, 4, 4, 4, 4, 4, 4, 4, 4, 4], shims.Select(static x => x.Parameters.Count).Order());
        Assert.All(shims, static x => Assert.Equal(["ptr", "i64"], x.Parameters.Skip(x.Parameters.Count - 2).Select(static p => p.Shape.Split(' ')[0])));
        foreach (var shim in shims)
        {
            var calls = ir.Functions.SelectMany(static x => x.Instructions).Where(x => x.Callee == shim).ToArray();
            Assert.NotEmpty(calls);
            Assert.All(calls, static x => Assert.Contains(", ptr @, i64 ", x.Shape, StringComparison.Ordinal));
        }
    }

    // SPEC 13.4.1, 12.4.3 (DECISIONS R5-M126, ruling Q4): a Builtin leaf through the operator table's column of its Type, read in place:
    // an integer's equality is Eq or both values unequal to themselves (constant false for an integer, folded at O2); a floating one's is
    // fcmp oeq or both une with themselves, so all NaNs are one value and +0.0 == -0.0; Unit's is the rows' Reflexive constants without a load, true;
    // a string's order is StringCompare's i32 itself.
    [Fact]
    public void BuiltinLeavesAreTheOperatorTablesEntries()
    {
        var c = Analyze(Shims, OutputKind.Library);
        var text = Generate(c);
        var ir = GeneratedIr.Parse(text);
        var shims = ShimsOf(ir, text);
        var ints = Assert.Single(shims, static x => Shapes(x).Contains("icmp eq i32 %, %") && x.Signature.StartsWith("internal i1 ", StringComparison.Ordinal) && CallCount(x) == 0);
        Assert.Equal(["load", "load", "icmp", "icmp", "icmp", "and", "or", "ret"], ints.Instructions.Select(static x => x.Opcode));
        var floats = Assert.Single(shims, static x => Shapes(x).Contains("fcmp oeq double %, %"));
        Assert.Equal(2, Shapes(floats).Count(static x => x == "fcmp une double %, %"));
        var units = Assert.Single(shims, static x => x.Instructions.All(static y => y.Opcode is not ("load" or "call")) && x.Parameters.Count == 4);
        Assert.Equal(["and i1 0, 0", "or i1 1, %", "ret i1 %"], Shapes(units));
        var order = ir.Function(LegacyRuntimeHelpers.StringCompare.Name);
        var texts = Assert.Single(shims, x => x.Instructions.Any(y => y.Callee == order) && !Shapes(x).Any(static y => y.StartsWith("br ", StringComparison.Ordinal)));
        Assert.Same(texts.Instructions.Single(x => x.Callee == order), texts.Blocks[^1].Terminator.Operands[0]);
    }

    // SPEC 13.4 (DECISIONS R5-M126): a Tuple compares its parts left to right; a part that is not equal decides and its value is returned
    // (equals' false itself, compare's i32 tested against 0), so a later part's leaf, a user witness called on the leaf addresses (a
    // first part's, the shim's own), runs only after the earlier parts are equal.
    [Fact]
    public void ATupleShimDecidesAtTheFirstUnequalPart()
    {
        var c = Analyze(Shims, OutputKind.Library);
        var text = Generate(c);
        var ir = GeneratedIr.Parse(text);
        var shims = ShimsOf(ir, text);
        var equal = ir.Function(LegacyRuntimeHelpers.StringEqual.Name);
        var pairs = Assert.Single(shims, x => x.Instructions.Any(y => y.Callee == equal));
        Assert.Equal(3, pairs.Blocks.Count);
        Assert.Equal("ret i1 %", pairs.Blocks[1].Terminator.Shape);
        Assert.Same(pairs.Blocks[0].Terminator.Operands[0], pairs.Blocks[1].Terminator.Operands[0]);
        var ranks = Assert.Single(shims, x => x.Blocks.Count == 3 && Shapes(x).Contains("icmp eq i32 %, 0") && x.Signature.StartsWith("internal i32 ", StringComparison.Ordinal));
        Assert.Equal("ret i32 %", ranks.Blocks[1].Terminator.Shape);
        var keys = Assert.Single(shims, static x => x.Instructions.Any(static y => y.Callee?.Signature.StartsWith("internal i1 (", StringComparison.Ordinal) == true));
        var witness = keys.Instructions.Single(static x => x.Opcode == "call");
        Assert.Equal(["argument", "argument"], witness.Operands.Select(static x => x.Opcode));
    }

    // SPEC 13.3.3, 22.5.1 (DECISIONS R5-M126, ruling Q5): an arithmetic shim is its Contract's row of the operator table on its loaded
    // operands with the row's check, which Aborts at the location the shim received: i32 addition through its overflow intrinsic,
    // Wrapping<u8> addition unchecked, f64 negation fneg (keeping -0.0).
    [Fact]
    public void AnArithmeticShimIsItsContractsRow()
    {
        var c = Analyze(Shims, OutputKind.Library);
        var text = Generate(c);
        var ir = GeneratedIr.Parse(text);
        var shims = ShimsOf(ir, text);
        var add = Assert.Single(shims, x => x.Instructions.Any(y => y.Callee == ir.Function("llvm.sadd.with.overflow.i32")));
        var abort = add.Instructions.Single(x => x.Callee == ir.Function("__kimi_abort"));
        Assert.Equal(["argument", "argument"], abort.Operands.Select(static x => x.Opcode)); // The location the shim received.
        Assert.Single(shims, static x => Shapes(x).Contains("add i8 %, %"));
        Assert.Single(shims, static x => Shapes(x).Contains("fneg double %"));
    }

    // The Builtin items' functions of a module, by their prefix (ModuleCodegen.BuiltinPrefix).
    private static IrFunction[] ShimsOf(GeneratedIr ir, string text)
        => [.. Regex.Matches(text, @"define internal \S+ @(" + ModuleCodegen.BuiltinPrefix + @"\d+)\(").Select(x => ir.Function(x.Groups[1].Value))];

    private static string[] Shapes(IrFunction function) => [.. function.Instructions.Select(static x => x.Shape)];

    private static int CallCount(IrFunction function) => function.Instructions.Count(static x => x.Opcode == "call");
}
