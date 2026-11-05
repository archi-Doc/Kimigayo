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

    // A Library whose generic bodies reach each row of the standard property witnesses (SPEC 11.9.2) on one struct: an i32's Copy and Set
    // (a compound update), a bool's Copy, a (i64, bool) Copy through the result slot, a string's Borrow, a Set of a Resource that needs a
    // drop and a zero-sized Set.
    private const string Witnesses =
        "public struct Resource\n    public let id: i32\n    public init(id: i32) => self.id = id\n    drop => Console.writeLine(\"drop\")\n" +
        "contract Counted\n    property count: i32 has get, set\ncontract Flagged\n    property flag: bool has get\n" +
        "contract Paired\n    property pair: (i64, bool) has get\ncontract Named\n    property name: ref/string\n        get() -> ref/string\n" +
        "contract Held\n    property item: ref/Resource\n        get() -> ref/Resource\n        set(value: Resource) -> ()\n" +
        "contract Empty\n    property unit: () has get, set\n" +
        "struct S\n    Self is Counted and Flagged and Paired and Named and Held and Empty\n    public var count: i32 = 1\n" +
        "    public var flag: bool = true\n    public var pair: (i64, bool) = (2, false)\n    public var name: string = \"s\"\n" +
        "    public var item: Resource\n    public var unit: () = ()\n    public init() => self.item = Resource.init(1)\n" +
        "func bump<T>(x: uniq/T) -> i32\n    T is Counted\n    x.count += 1\n    return x.count\n" +
        "func flag<T>(x: ref/T) -> bool\n    T is Flagged\n    return x.flag\nfunc pair<T>(x: ref/T) -> (i64, bool)\n    T is Paired\n    return x.pair\n" +
        "func name<T>(x: ref/T during s) -> ref/string during s\n    T is Named\n    return x.name\n" +
        "func hold<T>(x: uniq/T, value: Resource)\n    T is Held\n    x.item = value@move\nfunc clear<T>(x: uniq/T)\n    T is Empty\n    x.unit = ()\n" +
        "func use(s: uniq/S) -> i32\n    hold(s, Resource.init(2))\n    clear(s)\n    let p = pair(s)\n    let n = name(s)\n" +
        "    return if flag(s) => bump(s) else => 0\n";

    // A Leaf whose Counted witness reads the Field its Base stores after an i64, and a Tip<string> whose Holds witness borrows the Field its
    // base's base stores.
    private const string InheritedWitnesses =
        "contract Counted\n    property count: i32 has get\npublic open struct Base\n    public var a: i64 = 3\n    public var count: i32 = 1\n" +
        "public struct Leaf : Base\n    Self is Counted\n    public init() => ()\n" +
        "contract Holds\n    associate E\n    property item: ref/E\n        get(self: ref/Self) -> ref/E\n" +
        "public open struct Root<T>\n    public var item: T\n    public init(item: T) => self.item = item@move\n" +
        "public open struct Middle<V> : Root<V>\n    public init(item: V) : base(item@move) => ()\n" +
        "public struct Tip<U> : Middle<U>\n    Self is Holds\n    associate Holds.E is U\n    public init(item: U) : base(item@move) => ()\n" +
        "func read<T>(x: ref/T) -> i32\n    T is Counted\n    return x.count\n" +
        "func view<X>(x: ref/X during s) -> ref/X.E during s\n    X is Holds\n    return x.item\n" +
        "func use(l: ref/Leaf, t: ref/Tip<string>) -> i32\n    let v = view(t)\n    return read(l)\n";

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

    // PD3, K package 4.3, SPEC 11.9.2, 13.7.1, 22.5.1 (DECISIONS R5-M141): one caller-location function per PropertyWitness item under its
    // key's signature, the Field at the receiver plus its offset: a Copy loads and returns it (a bool narrowed, a slot Type copied into the
    // result slot), a Borrow returns its address, a Set destroys the old value through the item's glue at the location it received and then
    // stores its input; a Field that needs no drop calls no glue, a zero-sized one takes no input and stores nothing. Each generic body's
    // Requirement call passes its own location, and a compound update calls the getter's function, then the setter's.
    [Fact]
    public void EachPropertyWitnessIsOneCallerLocationFunction()
    {
        var text = Generate(Analyze(Witnesses, OutputKind.Library));
        var ir = GeneratedIr.Parse(text);
        var witnesses = WitnessesOf(ir, text);
        Assert.Equal(7, witnesses.Length); // count's Copy and Set, flag's, pair's Copy, name's Borrow, item's and unit's Set.
        Assert.All(witnesses, static x => Assert.Equal(["ptr", "i64"], x.Parameters.Skip(x.Parameters.Count - 2).Select(static p => p.Shape)));
        Assert.Equal(["load i32, ptr %, align 4", "ret i32 %"], Unplaced(Of(witnesses, "internal i32 (ptr, ptr, i64)")));
        Assert.Equal(["load i8, ptr %, align 1", "trunc i8 % to i1", "ret i1 %"], Unplaced(Of(witnesses, "internal i1 (ptr, ptr, i64)")));
        Assert.Equal(["store i32 %, ptr %, align 4", "ret void"], Unplaced(Of(witnesses, "internal void (ptr, i32, ptr, i64)")));
        Assert.Equal(["ret void"], Unplaced(Of(witnesses, "internal void (ptr, ptr, i64)")));
        Assert.Equal("ret ptr %", Of(witnesses, "internal ptr (ptr, ptr, i64)").Blocks[^1].Terminator.Shape);
        var slots = witnesses.Where(static x => x.Signature == "internal void (ptr, ptr, ptr, i64)").ToArray();
        Assert.Equal(2, slots.Length); // pair's Copy into its result slot and item's Set from its input's slot, both pointers.
        string[] copy = ["call void @(ptr align 8 %, ptr align 8 %, i64 16, i1 false)", "ret void"];
        var pair = Assert.Single(slots, x => Unplaced(x).SequenceEqual(copy));
        var copied = pair.Instructions.Single(static x => x.Opcode == "call");
        Assert.Same(pair.Parameters[0], copied.Operands[0]); // Into the result slot, %ret, from the Field.
        Assert.True(IsField(copied.Operands[1], pair.Parameters[1]));
        var set = Assert.Single(slots, static x => CallCount(x) == 2);
        var glue = set.Instructions.First(static x => x.Opcode == "call");
        var drops = Regex.Matches(text, @"define internal void @(__kimi_drop\d+)\(").Select(x => ir.Function(x.Groups[1].Value));
        Assert.Contains(glue.Callee, drops);
        Assert.True(IsField(glue.Operands[0], set.Parameters[0])); // The old value at the Field's address,
        Assert.Equal(["argument", "argument"], glue.Operands.Skip(1).Select(static x => x.Opcode)); // at the location the function received.
        var put = set.Instructions.Last(static x => x.Opcode == "call");
        Assert.Equal("call void @(ptr align 4 %, ptr align 4 %, i64 4, i1 false)", put.Shape);
        Assert.Equal([glue.Operands[0], set.Parameters[1]], put.Operands); // Then the input's slot copied to the Field.
        var calls = ir.Functions.SelectMany(static x => x.Instructions).Where(x => witnesses.Contains(x.Callee)).ToArray();
        Assert.All(calls, static x => Assert.Contains(", ptr @, i64 ", x.Shape, StringComparison.Ordinal));
        var bump = ir.Functions.Single(x => x.Instructions.Count(y => witnesses.Contains(y.Callee)) == 3);
        Assert.Equal(
            ["internal i32 (ptr, ptr, i64)", "internal void (ptr, i32, ptr, i64)", "internal i32 (ptr, ptr, i64)"],
            bump.Instructions.Where(x => witnesses.Contains(x.Callee)).Select(static x => x.Callee!.Signature));

        static IrFunction Of(IrFunction[] functions, string signature) => Assert.Single(functions, x => x.Signature == signature);

        // Whether a value is a Field's address: the receiver, or an offset from it.
        static bool IsField(IrValue value, IrValue receiver) => value == receiver || (value.Opcode == "getelementptr" && value.Operands[0] == receiver);
    }

    // SPEC 11.9.2, 11.2.2 (X1; DECISIONS R5-M141): a witness of a Field a base layer stores reads it at the receiver plus its offset in that
    // layer's own Layout, the complete base being a prefix: Leaf's count after Base's i64 at 8, Tip<string>'s item two bases up at 0.
    [Fact]
    public void AnInheritedWitnessReadsItsLayersOffset()
    {
        var text = Generate(Analyze(InheritedWitnesses, OutputKind.Library));
        var ir = GeneratedIr.Parse(text);
        var witnesses = WitnessesOf(ir, text);
        Assert.Equal(2, witnesses.Length);
        var read = Assert.Single(witnesses, static x => x.Signature == "internal i32 (ptr, ptr, i64)");
        Assert.Equal(["getelementptr i8, ptr %, i64 8", "load i32, ptr %, align 4", "ret i32 %"], Shapes(read));
        var view = Assert.Single(witnesses, static x => x.Signature == "internal ptr (ptr, ptr, i64)");
        Assert.Equal(["ret ptr %"], Shapes(view));
        Assert.Equal("argument", view.Blocks[^1].Terminator.Operands[0].Opcode);
    }

    // The Builtin items' functions of a module, by their prefix (ModuleCodegen.BuiltinPrefix).
    private static IrFunction[] ShimsOf(GeneratedIr ir, string text)
        => [.. Regex.Matches(text, @"define internal \S+ @(" + ModuleCodegen.BuiltinPrefix + @"\d+)\(").Select(x => ir.Function(x.Groups[1].Value))];

    // The PropertyWitness items' functions of a module, by their prefix (ModuleCodegen.WitnessPrefix).
    private static IrFunction[] WitnessesOf(GeneratedIr ir, string text)
        => [.. Regex.Matches(text, @"define internal \S+ @(" + ModuleCodegen.WitnessPrefix + @"\d+)\(").Select(x => ir.Function(x.Groups[1].Value))];

    // A function's instruction shapes but its Field addresses (a getelementptr at a nonzero offset).
    private static string[] Unplaced(IrFunction function) => [.. Shapes(function).Where(static x => !x.StartsWith("getelementptr", StringComparison.Ordinal))];

    private static string[] Shapes(IrFunction function) => [.. function.Instructions.Select(static x => x.Shape)];

    private static int CallCount(IrFunction function) => function.Instructions.Count(static x => x.Opcode == "call");
}
