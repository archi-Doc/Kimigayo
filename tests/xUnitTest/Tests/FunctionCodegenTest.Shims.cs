// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.RegularExpressions;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Xunit;

namespace XunitTest;

// The Builtin shims of MIR code generation (R5 C8f; SPEC 12.4.3, 13.3.3, 13.4, 13.4.1, 22.5.1; DECISIONS R5-M126): one caller-location
// function per Builtin item under its requirement's signature, a comparison over F6's tree with the operator table's leaves, an arithmetic
// shim its Contract's MIR operation through the operator table's row.
public sealed partial class FunctionCodegenTest
{
    // A Library whose generic comparisons and arithmetic run on built-in Types: equality of i32, f64, Unit, bool, char, Wrapping<u8>, a ref
    // layer, a Tuple and a Tuple holding a user witness; ordering of string, u8, char and a Tuple; addition of i32 and Wrapping<u8>; division
    // of i32; negation of f64.
    private const string Shims =
        "func equal<T>(left: ref/T, right: ref/T) -> bool\n    T is Equatable\n    return left == right\n" +
        "func order<T>(left: ref/T, right: ref/T) -> i32\n    T is Comparable\n    return left.compare(right)\n" +
        "func sum<T>(left: ref/T, right: ref/T) -> T\n    T is Addable<T>\n    T.(Addable<T>).Output is T\n    return left + right\n" +
        "func quotient<T>(left: ref/T, right: ref/T) -> T\n    T is Dividable<T>\n    T.(Dividable<T>).Output is T\n    return left / right\n" +
        "func negate<T>(value: ref/T) -> T\n    T is Negatable\n    T.(Negatable).Output is T\n    return -value\n" +
        "struct Key\n    Self is Equatable\n    public let number: i32\n    public init(number: i32) => self.number = number\n" +
        "    public func equals(self: ref/Self, other: ref/Self) -> bool => self.number == other.number\n" +
        "func ints(a: ref/i32, b: ref/i32) -> bool => equal(a, b)\nfunc floats(a: ref/f64, b: ref/f64) -> bool => equal(a, b)\n" +
        "func units(a: ref/(), b: ref/()) -> bool => equal(a, b)\nfunc flags(a: ref/bool, b: ref/bool) -> bool => equal(a, b)\n" +
        "func letters(a: ref/char, b: ref/char) -> bool => equal(a, b)\nfunc wraps(a: ref/Wrapping<u8>, b: ref/Wrapping<u8>) -> bool => equal(a, b)\n" +
        "func layers(a: i32, b: i32) -> bool\n    let x = a@ref\n    let y = b@ref\n    return equal(x@ref, y@ref)\n" +
        "func texts(a: ref/string, b: ref/string) -> i32 => order(a, b)\nfunc bytes(a: ref/u8, b: ref/u8) -> i32 => order(a, b)\n" +
        "func marks(a: ref/char, b: ref/char) -> i32 => order(a, b)\n" +
        "func pairs(a: ref/(i32, string), b: ref/(i32, string)) -> bool => equal(a, b)\n" +
        "func ranks(a: ref/(i32, string), b: ref/(i32, string)) -> i32 => order(a, b)\n" +
        "func keys(a: ref/(Key, i32), b: ref/(Key, i32)) -> bool => equal(a, b)\n" +
        "func add(a: ref/i32, b: ref/i32) -> i32 => sum(a, b)\nfunc wrap(a: ref/Wrapping<u8>, b: ref/Wrapping<u8>) -> Wrapping<u8> => sum(a, b)\n" +
        "func divide(a: ref/i32, b: ref/i32) -> i32 => quotient(a, b)\nfunc flip(a: ref/f64) -> f64 => negate(a)\n";

    // One i32 equality that a Requirement call, a requirement Item's Value call, its erased adapter and a Dictionary's key reach, and no
    // string leaf.
    private const string Served =
        "func equal<T>(left: ref/T, right: ref/T) -> bool\n    T is Equatable\n    return left == right\n" +
        "func viaItem<T>(left: ref/T, right: ref/T) -> bool\n    T is Equatable\n    let same = T.equals\n    return same(left, right)\n" +
        "func erased<T>(left: ref/T, right: ref/T) -> bool\n    T is Equatable and Owned\n    let same: (ref/T, ref/T) -> bool = T.equals\n" +
        "    return same(left, right)\nfunc calls(a: ref/i32, b: ref/i32) -> bool => equal(a, b) and viaItem(a, b) and erased(a, b)\n" +
        "func put(d: uniq/Dictionary<i32, i32>)\n    _ = d.tryInsert(1, 2)\n";

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

        // i32, f64, (), bool, char, Wrapping<u8>, ref/i32, (i32, string), (Key, i32) equals; string, u8, char, (i32, string) compare; i32,
        // Wrapping<u8> added; i32 divided; f64 negated.
        Assert.Equal(17, shims.Length);
        Assert.Equal([3, .. Enumerable.Repeat(4, 16)], shims.Select(static x => x.Parameters.Count).Order());
        Assert.All(shims, static x => Assert.Equal(["ptr", "i64"], x.Parameters.Skip(x.Parameters.Count - 2).Select(static p => p.Shape.Split(' ')[0])));
        foreach (var shim in shims)
        {
            var calls = ir.Functions.SelectMany(static x => x.Instructions).Where(x => x.Callee == shim).ToArray();
            Assert.NotEmpty(calls);
            Assert.All(calls, static x => Assert.Contains(", ptr @, i64 ", x.Shape, StringComparison.Ordinal));
        }
    }

    // SPEC 13.4.1, 12.4.3 (DECISIONS R5-M126, ruling Q4): a Builtin leaf through the operator table's column of its Type, read in place:
    // an integer's equality is Eq or both values unequal to themselves (constant false for an integer, folded at O2), char's, bool's
    // (narrowed) and a wrapping integer's on the unsigned integer column; a floating one's is fcmp oeq or both une with themselves, so all
    // NaNs are one value and +0.0 == -0.0; Unit's is the rows' Reflexive constants without a load, true; a ref layer's loads its referents
    // first. An order is zext Gt - zext Lt, unsigned for u8 and char; a string's is StringCompare's i32 itself.
    [Fact]
    public void BuiltinLeavesAreTheOperatorTablesEntries()
    {
        var c = Analyze(Shims, OutputKind.Library);
        var text = Generate(c);
        var ir = GeneratedIr.Parse(text);
        var shims = ShimsOf(ir, text);
        string[] equality = ["icmp", "icmp", "icmp", "and", "or", "ret"];
        var ints = shims.Where(static x => Shapes(x).Contains("icmp eq i32 %, %") && x.Blocks.Count == 1 && CallCount(x) == 0).ToArray();
        Assert.Equal(3, ints.Length); // i32, char and ref/i32.
        Assert.Equal(2, ints.Count(x => x.Instructions.Select(static y => y.Opcode).SequenceEqual(["load", "load", .. equality])));
        Assert.Single(ints, x => x.Instructions.Select(static y => y.Opcode).SequenceEqual(["load", "load", "load", "load", .. equality]));
        var flags = Assert.Single(shims, static x => Shapes(x).Contains("icmp eq i1 %, %"));
        Assert.Equal(["load", "trunc", "load", "trunc", .. equality], flags.Instructions.Select(static x => x.Opcode));
        var wraps = Assert.Single(shims, static x => Shapes(x).Contains("icmp eq i8 %, %"));
        Assert.Equal(["load", "load", .. equality], wraps.Instructions.Select(static x => x.Opcode));
        var floats = Assert.Single(shims, static x => Shapes(x).Contains("fcmp oeq double %, %"));
        Assert.Equal(2, Shapes(floats).Count(static x => x == "fcmp une double %, %"));
        var units = Assert.Single(shims, static x => x.Instructions.All(static y => y.Opcode is not ("load" or "call")) && x.Parameters.Count == 4);
        Assert.Equal(["and i1 0, 0", "or i1 1, %", "ret i1 %"], Shapes(units));
        foreach (var width in new[] { "i8", "i32" })
        {
            var unsigned = Assert.Single(shims, x => Shapes(x).Contains($"icmp ugt {width} %, %"));
            Assert.Contains($"icmp ult {width} %, %", Shapes(unsigned));
            Assert.Equal(["zext", "zext", "sub", "ret"], unsigned.Instructions.Skip(4).Select(static x => x.Opcode));
        }

        var order = ir.Function(LegacyRuntimeHelpers.StringCompare.Name);
        var texts = Assert.Single(shims, x => x.Instructions.Any(y => y.Callee == order) && x.Blocks.Count == 1);
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
        var order = ir.Function(LegacyRuntimeHelpers.StringCompare.Name);
        var ranks = Assert.Single(shims, x => x.Blocks.Count == 3 && x.Instructions.Any(y => y.Callee == order));
        Assert.Contains("icmp eq i32 %, 0", Shapes(ranks));
        Assert.Equal("ret i32 %", ranks.Blocks[1].Terminator.Shape);
        var keys = Assert.Single(
            shims,
            static x => x.Instructions.Any(static y => y.Callee?.Signature.StartsWith("internal i1 (", StringComparison.Ordinal) is true));
        var witness = keys.Instructions.Single(static x => x.Opcode == "call");
        Assert.Equal(["argument", "argument"], witness.Operands.Select(static x => x.Opcode));
    }

    // SPEC 13.3.3, 22.5.1 (DECISIONS R5-M126): an arithmetic shim is its Contract's MIR operation (BuiltinArithmetic) through the operator
    // table's row on its loaded operands with the row's check, which Aborts at the location the shim received: i32 addition through its
    // overflow intrinsic, i32 division after its zero divisor and minimum / -1 checks, Wrapping<u8> addition unchecked, f64 negation fneg
    // (keeping -0.0).
    [Fact]
    public void AnArithmeticShimIsItsContractsOperation()
    {
        var c = Analyze(Shims, OutputKind.Library);
        var text = Generate(c);
        var ir = GeneratedIr.Parse(text);
        var shims = ShimsOf(ir, text);
        var abort = ir.Function("__kimi_abort");
        var add = Assert.Single(shims, x => x.Instructions.Any(y => y.Callee == ir.Function("llvm.sadd.with.overflow.i32")));
        var failure = add.Instructions.Single(x => x.Callee == abort);
        Assert.Equal(["argument", "argument"], failure.Operands.Select(static x => x.Opcode)); // The location the shim received.
        var divide = Assert.Single(shims, static x => Shapes(x).Contains("sdiv i32 %, %"));
        Assert.Contains("icmp eq i32 %, 0", Shapes(divide));
        failure = divide.Instructions.Single(x => x.Callee == abort);
        Assert.Equal(["select", "argument", "argument"], failure.Operands.Select(static x => x.Opcode)); // Its reason and the location.
        Assert.Single(shims, static x => Shapes(x).Contains("add i8 %, %"));
        Assert.Single(shims, static x => Shapes(x).Contains("fneg double %"));
    }

    // K 4.5 rows 3 and 5, SPEC 12.4.3 (DECISIONS R5-M126): one item serves every call of its key: a Requirement call, a requirement Item's
    // Value call, its erased adapter's entry and a Dictionary's key equality (findDictionaryLink's Requirement call at the built-in key) call
    // the one i32 equality function; a module without a string leaf has no string routine.
    [Fact]
    public void OneShimServesEveryCallOfItsKey()
    {
        var text = Generate(Analyze(Served, OutputKind.Library));
        var ir = GeneratedIr.Parse(text);
        var shim = Assert.Single(ShimsOf(ir, text));
        var callers = ir.Functions.Where(x => x.Instructions.Any(y => y.Callee == shim)).ToArray();
        Assert.Equal(4, callers.Length); // equal<i32>, viaItem<i32>, the adapter and findDictionaryLink<i32, i32>.
        Assert.Contains(ir.Function(Regex.Match(text, @"define internal \S+ @(" + ModuleCodegen.AdapterPrefix + @"\d+)\(").Groups[1].Value), callers);
        Assert.DoesNotContain(LegacyRuntimeHelpers.StringEqual.Name, text, StringComparison.Ordinal);
        Assert.DoesNotContain(LegacyRuntimeHelpers.StringCompare.Name, text, StringComparison.Ordinal);
    }

    // The Builtin items' functions of a module, by their prefix (ModuleCodegen.BuiltinPrefix).
    private static IrFunction[] ShimsOf(GeneratedIr ir, string text)
        => [.. Regex.Matches(text, @"define internal \S+ @(" + ModuleCodegen.BuiltinPrefix + @"\d+)\(").Select(x => ir.Function(x.Groups[1].Value))];

    private static string[] Shapes(IrFunction function) => [.. function.Instructions.Select(static x => x.Shape)];

    private static int CallCount(IrFunction function) => function.Instructions.Count(static x => x.Opcode == "call");
}
