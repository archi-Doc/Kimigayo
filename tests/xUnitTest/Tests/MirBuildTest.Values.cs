// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using System.Text.RegularExpressions;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

/// <summary>The values of the MIR build: a form without a building rule is one record and an Opaque value, a value or operation Binding did not
/// form is derived without a record, a node published without its facts is a broken contract; and the fills, Never-element literals, the
/// verifications' comparison records, an Abort's location and an InlineStorage's destructor.</summary>
public sealed partial class MirBuildTest
{
    // Plan 6.2: a form without a building rule is one record at its location, however many times a body meets it (a defer body, built at
    // each way out of its scope), and an Opaque value naming the locals its syntax touches. A node the lexer or Binding rejected is a derived
    // Opaque without a record (SPEC 23.1.2): Binding's Error, a use of a local whose rejected declaration has no Type (one with a Type is
    // its local's, ARejectedDeclarationsLocalIsItsNamesOneStorage), and the Field initializer of a declaration Binding did not bind (a
    // struct with a misplaced `length` slot), in each constructor. Building goes on after each. The unbuilt form is the tuple operator
    // without its comparison (Unbuilt); its Opaque names the roots of its operands.
    [Fact]
    public void UnsupportedFormsAreOpaqueAtOneRecord()
    {
        var c = CompilationTestHelper.Parse(
            "struct Grid<length N>\n    public var cells: [N of i32] = [N of 7]\n    public init() => ()\n    public init(y: i32) => ()\n" +
            Unbuilt.Declarations + $"func f(a: i32, x: ref/{Unbuilt.Tuple}, y: ref/{Unbuilt.Tuple}) -> i32\n    let c = x == y\n    return a\n" +
            $"func g() -> i32 => missing\nfunc h(a: i32, c: bool, u: ref/{Unbuilt.Tuple}, v: ref/{Unbuilt.Tuple}) -> i32\n    defer\n" +
            "        let d = u == v\n    if c => return 1\n    return a\nfunc k() -> i32\n    let x = missing\n    return x\n");
        Assert.Equal(nameof(DiagnosticCode.MisplacedSyntax_Kd), Assert.Single(TestDiagnostics.Of(c)).Code);
        c.Bind();
        _ = c.Binding.CheckStartup(OutputKind.Application);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var bodies = program.Bodies.ToArray().Where(static x => x.Key.Decl?.Name is "f" or "g" or "h" or "k").ToDictionary(static x => x.Key.Decl!.Name);
        var unbuilt = bodies["f"];
        Assert.True(unbuilt.HasOpaque);
        Assert.Equal([2, 3], unbuilt.OpaqueRoots.ToArray());
        Assert.Contains(unbuilt.Blocks.ToArray(), static x => x.Terminator.Kind == MirTerminatorKind.Return);
        var records = program.Records.Where(static x => x.Subject.Module == 0).Select(x => c.SyntaxOf(x.Subject).ToString()).Order(StringComparer.Ordinal);
        Assert.Equal(["u == v", "x == y"], records);
        Assert.Equal(2, bodies["h"].OpaqueSites.ToArray().Count(x => c.SyntaxOf(x.Source).ToString() == "u == v"));

        Assert.Equal(2, program.Bodies.ToArray().Count(x => x.OpaqueSites.ToArray().Any(y => c.SyntaxOf(y.Source).ToString() == "[N of 7]")));
        Assert.Equal(HirOp.Error, HirTables.Op(c.SyntaxOf(bodies["g"].OpaqueSites[0].Source)));
        Assert.Equal(["let x = missing", "x"], bodies["k"].OpaqueSites.ToArray().Select(x => c.SyntaxOf(x.Source).ToString()));
    }

    // SPEC 23.1.2 (DECISIONS R4-M70): where Binding or control flow published that it formed no value, the MIR forms none from facts
    // that do not exist and records nothing, so each body validates: a Field Binding formed no stored Type for (one naming a missing Type,
    // or inferred from a call it rejected) has no Place, so a constructor places no initializer there; and an empty body falling through
    // a non-Unit result, which control flow rejects, leaves no value, as a body ending in a statement does. (A raw read of a C-layout
    // struct Binding rejected was the derived Opaque's row until DECISIONS R4-M134 Q8 made such a declaration no Binding error.)
    [Theory]
    [InlineData("struct S\n    var item: Missing = 7\n    var other: i32 = 1\n", "UnresolvedBinding_Kd")]
    [InlineData(
        "group Tags\n    public func tagOf<T>() -> i32\n        T is Owned\n        return 0\nstruct Entry\n    var tag = Tags.tagOf<Entry>()\n",
        "UnprovenConstraint_Kd")]
    [InlineData("func f<T>() -> T\n    T is Copy\n", "FunctionFallthrough_Kd")]
    public void ValuesBindingDidNotFormAreDerived(string source, string code)
    {
        var c = Compile(source);
        Assert.Contains(CompilationTestHelper.BindingErrors(c), x => x.Code == code);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.DoesNotContain(program.Records, static x => x.Subject.Module == 0);
        Assert.Empty(program.Bodies.ToArray().Where(static x => x.Source.Module == 0).SelectMany(static x => x.OpaqueSites.ToArray()));
    }

    // SPEC 23.1.2 (DECISIONS R4-M88): an operation Binding left unresolved (a built-in operator, `and`/`or`, a runtime Type test, a
    // selection whose index Type it rejected, PLAN G95) is an Error node whose value is the derived Opaque, never a form built from a Type
    // that does not exist, and a construct it left unresolved is held at the Type its position fixes (SPEC 14.9.2), so each body validates
    // without a record (fail-before: Type and Projection findings); a selection keeps no `if` index without a Type, also in parentheses
    // (built at `()`, it would be a finding).
    [Theory]
    [InlineData("'A' == 65\n", "'A' == 65")]
    [InlineData("func f<T>(x: Wrapping<T>) -> Wrapping<T>\n    T is PrimitiveInteger\n    return x + 128\n", "x + 128")]
    [InlineData("func f(x: i32) -> bool\n    return x is Missing\n", "x is Missing")]
    [InlineData("s is ref or uniq\n", "s is ref or uniq")]
    [InlineData("func f(values: [3 of i32], r: Range<isize>) -> i32 => values[r]\n", "values[r]")]
    [InlineData("func f(x: i32) -> i32\n    return x + missing\n", "x + missing")]
    [InlineData("func f(c: bool) -> bool\n    return c and missing\n", "c and missing")]
    [InlineData("func f(c: bool) -> i32\n    return if c => 1 else => missing\n", "missing")]
    [InlineData("func f(c: bool, v: [3 of i32], i: isize) -> i32\n    return v[if c => i else => missing]\n", "v[if c => i\nelse => missing]")]
    [InlineData("func f(c: bool, v: [3 of i32], i: isize) -> i32\n    return v[(if c => i else => missing)]\n", "v[(\n    if c => i\n    else => missing\n    )]")]
    public void OperationsBindingDidNotResolveAreDerived(string source, string opaque)
    {
        var c = Compile(source);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.DoesNotContain(program.Records, static x => x.Subject.Module == 0);
        var sites = program.Bodies.ToArray().Where(static x => x.Source.Module == 0).SelectMany(static x => x.OpaqueSites.ToArray());
        Assert.Contains(opaque, sites.Select(x => c.SyntaxOf(x.Source).ToString()));
    }

    // SPEC 4.3.3 (DECISIONS R4-M81): a fill, of a generic or a constant length, also 0, is one Fill of its literal's Type from its value's
    // one acquisition (an argument read, a call's result, an inner aggregate's temp), with no record, no Opaque value and no copy of the
    // value into a temp of its own (fail-before: a generic fill was an Unsupported record, a constant one L copies of such a temp).
    [Theory]
    [InlineData("[N of x]", "_3 = Fill(copy _1): [N of i32]")]
    [InlineData("[(N + 1) of 0]", "_3 = Fill(const 0_i32): [(1 + N) of i32]")]
    [InlineData("[4 of x]", "_3 = Fill(copy _1): [4 of i32]")]
    [InlineData("[0 of x]", "_3 = Fill(copy _1): [0 of i32]")]
    [InlineData("[0 of g()]", "_3 = Fill(move _4): [0 of i32]")]
    [InlineData("[3 of (4, true)]", "_3 = Fill(move _4): [3 of (i32, bool)]")]
    [InlineData("[2 of [3 of 9]]", "_4 = Fill(const 9_i32): [3 of i32]|_3 = Fill(move _4): [2 of [3 of i32]]")]
    public void AFillIsOneAggregateOfItsValuesAcquisition(string literal, string fills)
    {
        // The parameter's Type is a signature length, whose ValidLength obligation SPEC 4.4.2 lets the body's (N + 1) rely on.
        var c = Compile($"func g() -> i32 => 1\nfunc f<length N>(x: i32, s: [(N + 1) of i32]) -> i32\n    let a = {literal}\n    return 0\n");
        Assert.Empty(c.Binding.Issues);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.Empty(program.Records);
        var body = program.Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn } && x.Key.Decl?.Name == "f");
        Assert.False(body.HasOpaque);
        var text = new StringBuilder();
        MirDump.Write(body, text, program.Drops, body.Ordinal);
        var aggregates = Regex.Matches(text.ToString(), @"(_\d+ = (?:Fill|FixedArray)\(.*?\): .*?) @\d+").Select(static x => x.Groups[1].Value);
        Assert.Equal(fills.Split('|'), aggregates);
        var statements = body.Statements.ToArray();
        foreach (var fill in statements.Where(static x => x.Rvalue is { Kind: MirRvalueKind.Aggregate, Op: (byte)MirAggregateKind.Fill }))
        {
            var value = body.Operands[fill.Rvalue.Start];
            Assert.DoesNotContain(statements, x => value.Kind != MirOperandKind.Const && x is { Kind: MirStatementKind.Assign, Rvalue.Kind: MirRvalueKind.Use } &&
                x.Place == value.Place);
        }
    }

    // SPEC 4.3.1, 4.9.5, 12.2, 12.4.1 (DECISIONS R5-M129): an Array literal is one Array aggregate of its elements in source order, `[]` one
    // of none, an inner literal its own, and an element before the last that builds code secured where it is evaluated (A1 moves each
    // operand at the one Assign); no call, record or Opaque value (fail-before: the empty Array from a FixedArray aggregate, then reserve and
    // one append per element).
    [Theory]
    [InlineData("Array<i32>", "[x, 2]", "_3 = Array(copy _1, const 2_i32): Array<i32>")]
    [InlineData("Array<i32>", "[]", "_3 = Array(): Array<i32>")]
    [InlineData("Array<i32>", "[x, g(), 3]", "_3 = Array(move _4, move _5, const 3_i32): Array<i32>")]
    [InlineData("Array<Array<i32>>", "[[x], []]", "_4 = Array(copy _1): Array<i32>|_5 = Array(): Array<i32>|_3 = Array(move _4, move _5): Array<Array<i32>>")]
    [InlineData("Array<(string, i32)>", "[(s, x)]", "_4 = Tuple(move _2, copy _1): (string, i32)|_3 = Array(move _4): Array<(string, i32)>")]
    public void AnArrayLiteralIsOneAggregateOfItsElements(string type, string literal, string aggregates)
    {
        var c = Compile($"func g() -> i32 => 1\nfunc f(x: i32, s: string) -> i32\n    let a: {type} = {literal}\n    return 0\n");
        Assert.Empty(c.Binding.Issues);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.Empty(program.Records);
        var body = program.Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn } && x.Key.Decl?.Name == "f");
        Assert.False(body.HasOpaque);
        var text = new StringBuilder();
        MirDump.Write(body, text, program.Drops, body.Ordinal);
        var built = Regex.Matches(text.ToString(), @"(_\d+ = (?:Array|Tuple|FixedArray)\(.*?\): .*?) @\d+").Select(static x => x.Groups[1].Value);
        Assert.Equal(aggregates.Split('|'), built);
        Assert.DoesNotContain(body.Blocks.ToArray(), x => x.Terminator.Kind == MirTerminatorKind.Call && x.Terminator.Callee.Symbol?.Name != "g");
    }

    // SPEC 3.5.1, 8.14 (PLAN G262, DECISIONS BU-45): a bare acquisition that Binding plans without a Reborrow through a shared layer is
    // built as the Move its HIR row says (A1's, rejected by the old path's TransferRequired_Kd), never a Ref or PairAcquire past that
    // layer: at a `let`, a `return`, a default, an Array literal element, a Slice element, a pair layer through `ref` or another binder's
    // layer; an exclusive path and the acquiring binder's own layer keep theirs (fail-before: `&uniq` and `acquire` in the first seven).
    // Each row: the built statement, and the form no body may contain.
    [Theory]
    [InlineData("func f(r: ref/(uniq/i32 during a) during b)\n    let y = r@follow\n", "_2 = move (*_1)", "&uniq")]
    [InlineData("func f(r: ref/(uniq/i32 during a) during b) -> uniq/i32 during a\n    return r@follow\n", "_2 = move (*_1)", "&uniq")]
    [InlineData("func f(r: ref/(uniq/i32 during a) during b, x: uniq/i32 during a = r@follow)\n    return\n", "_0 = move (*(*_1))", "&uniq")]
    [InlineData("func f(r: ref/(uniq/i32 during a) during b)\n    let values = [r@follow]\n", "_2 = Array(move (*_1))", "&uniq")]
    [InlineData("func f(s: Slice<uniq/i32 during a> during b)\n    let y = s[0]\n", "_2 = move _1[_3]", "&uniq")]
    [InlineData("func f<s/T>(x: ref/(s/T during a) during b)\n    s is ref or uniq\n    let y = x@follow\n", "_2 = move (*_1)", "acquire")]
    [InlineData("func f<s/T, t/U>(x: t/(s/T during a) during b)\n    s is ref or uniq\n    t is ref or uniq\n    let y = x@follow\n", "_2 = move (_1@b1)", "acquire")]
    [InlineData("func f<s/T>(x: s/(s/T during a) during b)\n    s is ref or uniq\n    let y = x@follow\n", "_2 = acquire b0 (_1@b0)", "move (_1@b0)")]
    [InlineData("func f(r: uniq/(uniq/i32 during a) during b)\n    let y = r@follow\n", "_2 = &uniq (*(*_1))", "move (*(*_1))")]
    public void ABareAcquisitionPastASharedLayerIsBuiltAsItsMove(string source, string built, string absent)
    {
        var c = Compile(source);
        Assert.Empty(c.Binding.Issues);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.Empty(program.Records);
        var text = new StringBuilder();
        MirDump.Write(program, 0, text);
        Assert.Contains(built, text.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(absent, text.ToString(), StringComparison.Ordinal);
    }

    // SPEC 4.3.3, IMPL 21.3.2 (DECISIONS R4-M81): a fill's MIR does not grow with its length (fail-before: [1048576 of 0@u8] had 1,048,580
    // operands, [4 of 0@u8] 8).
    [Fact]
    public void AFillsMirDoesNotGrowWithItsLength()
    {
        int Operands(int length) => Compile($"func f() -> u8\n    let bytes: [{length} of u8] = [{length} of 0]\n    return bytes[3]\n").BuildMir()
            .Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "f").Operands.Length;
        Assert.Equal(Operands(4), Operands(1048576));
    }

    // SPEC 3.1.3, 3.8: a literal whose Type holds a Never element is built at its published Type, with no record and no validator finding:
    // its own Type where no position decides one (a fresh local, an array element), the payload Type of a Case (DECISIONS BU-2), its
    // expected Type, its join's Type for a Never Place element, and a Type with a Never elsewhere (a Function's result, a Type argument).
    // Other joins: NeverContinuationTest.
    [Theory]
    [InlineData("let t = (a, (return 1))", "(a, (return 1))", "(i64, Never)")]
    [InlineData("let t = (a, (return 1))@move", "(a, (return 1))", "(i64, Never)")]
    [InlineData("let t = [(a, (return 1))]", "[(a, (return 1))]", "Array<(i64, Never)>")]
    [InlineData("let x = P.Pair(1, (a, (return 1)))", "(a, (return 1))", "(i64, i64)")]
    [InlineData("let x = P.Pair(1, ((return 1), a))", "((return 1), a)", "(i64, i64)")]
    [InlineData("let x = P2.Pair((a, (return 1)), a)", "(a, (return 1))", "(i64, i64)")]
    [InlineData("let x: P<i64> = P.Pair(1, (a, (return 1)))", "(a, (return 1))", "(i64, i64)")]
    [InlineData("let t: (i64, i64) = (a, (return 1))", "(a, (return 1))", "(i64, i64)")]
    [InlineData("let t = if c => (a, a) else => (a, x)", "(a, x)", "(i64, i64)", ", x: Never")]
    [InlineData("let t = (a, h@move)", "(a, h@move)", "(i64, () -> Never)")]
    [InlineData("let n: Option<Never> = .None\n    let t = (a, n@move)", "(a, n@move)", "(i64, Option<Never>)")]
    public void NeverElementLiteralsAreBuilt(string statement, string literal, string type, string parameters = "")
    {
        var c = Compile("enum P<T>\n    Pair(T, (T, T))\nenum P2<T>\n    Pair((T, T), T)\nfunc f(a: i64, c: bool, h: () -> Never" + parameters +
            ") -> i32\n    " + statement + "\n    return 0\n");
        Assert.Empty(c.Binding.Issues);
        var node = KotoTree.Walk(c.Kotonoha.RootKoto).Single(x => x is TupleLiteralKoto or ArrayLiteralKoto && x.ToString() == literal);
        Assert.Equal(type, TyCtxt.Name((BoundType)HirTables.Meaning(node)!));
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.DoesNotContain(program.Records, static x => x.Subject.Module == 0);
    }

    // SPEC 13.7.1, 3.1.3 (DECISIONS R5-M159): the right side is secured at the destination Type, the element's, before a diverging key
    // is evaluated: a call's result, a stored exclusive reference Reborrowed and a reference read through its layers, each in a temp of
    // that Type with no record and no finding (fail-before: a Never temp, written by the call with a Type and a NeverWrite finding, the
    // Reborrow's InternalInvariant_Kd, the read not built).
    [Theory]
    [InlineData("var a: [1 of string] = [\"old\"]\n    a[(return 42)] = echo(\"new\")", "a[(return 42)]", "string")]
    [InlineData("var x = \"x\"\n    var y = \"y\"\n    var us: [1 of uniq/string] = [x@uniq]\n    let s = y@uniq\n    us[(return 42)] = s", "us[(return 42)]", "uniq/string")]
    [InlineData("let x = \"x\"\n    let r = x@ref\n    let rr = r@ref\n    var us: [1 of ref/string] = [r]\n    us[(return 42)] = rr", "us[(return 42)]", "ref/string")]
    public void ARightSideIsSecuredAtTheElementTypeBeforeADivergingKey(string statements, string target, string type)
    {
        var c = Compile("func echo(text: string) -> string => text@move\nfunc f() -> i32\n    " + statements + "\n    return 0\n");
        Assert.Empty(c.Binding.Issues);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.DoesNotContain(program.Records, static x => x.Subject.Module == 0);
        var f = program.Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn, Decl.Name: "f" });
        var temps = f.Locals.ToArray().Where(x => x.Kind == MirLocalKind.Temp && c.SyntaxOf(x.Source.Syntax).ToString() == target).Select(static x => TyCtxt.Name(x.Type));
        Assert.Contains(type, temps);
        Assert.DoesNotContain("Never", temps);
    }

    // SPEC 17.6.3 (PD22): a verification records each Scalar operand of its built-in comparison, side 0 the left and 1 the right: a
    // constant as itself, another value by Copy of the temp it was read into once, before any record, which the comparison then moves;
    // under parentheses too. A string or Contract comparison and another condition record nothing.
    [Theory]
    [InlineData("$expect(helper(1) == a)", "0 Copy i32, 1 Copy i32")]
    [InlineData("$expect((a < 4))", "0 Copy i32, 1 Const i32")]
    [InlineData("$require(1.5 >= f, message: \"m\")", "0 Const f64, 1 Copy f64")]
    [InlineData("$expect(true != flag)", "0 Const bool, 1 Copy bool")]
    [InlineData("$expect(s == \"y\")", "")]
    [InlineData("$expect(k != k)", "")]
    [InlineData("$expect(flag)", "")]
    public void VerificationsRecordTheirComparisonsScalars(string verification, string expected)
    {
        var c = Compile(
            "struct Key\n    Self is Equatable\n    public var n: i32 = 0\n    public func equals(self: ref/Self, other: ref/Self) -> bool => true\n" +
            "func helper(x: i32) -> i32 => x + 1\n#Test\nfunc sample()\n    let a = 3\n    let f = 1.5\n    let flag = true\n    let s = \"x\"\n" +
            "    let k = Key.init()\n    " + verification + "\n",
            test: true);
        Assert.False(c.Diagnostics.HasErrorsIn(DiagnosticPartition.Binding));
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var body = program.Bodies.ToArray().Single(static x => x.Key.Kind == MirBodyKind.Test);
        var records = Records(body);
        string Record(MirTerminator call)
        {
            var (side, value) = (body.Operands[call.Start], body.Operands[call.Start + 1]);
            var type = value.Kind == MirOperandKind.Const ? body.Consts[value.Arg].Type : body.TypeOf(value.Place);
            return $"{body.Consts[side.Arg].Bits} {value.Kind} {type!.Name}";
        }

        Assert.Equal(expected, string.Join(", ", records.Select(Record)));
        var copied = records.Select(x => body.Operands[x.Start + 1]).Where(static x => x.Kind == MirOperandKind.Copy).Select(static x => x.Place).ToArray();
        Assert.All(copied, x => Assert.Equal(MirLocalKind.Temp, body.Locals[x.Root].Kind));
        foreach (var last in records.TakeLast(1))
        {
            var next = body.Blocks[last.Target];
            var comparison = body.Statements[next.Start].Rvalue;
            Assert.Equal(MirRvalueKind.BinaryOp, comparison.Kind);
            Assert.All(copied, x => Assert.Contains(MirOperand.Move(x), body.Operands.Slice(comparison.Start, comparison.Count).ToArray()));
            Assert.Equal((int)IntrinsicId.TestObserve, next.Terminator.Callee.Arg);
        }
    }

    // SPEC 22.5.1: an Abort reports where its `$abort` expression is, which HIR publishes as a Group of the invocation it builds: the Abort
    // call's syntax is the macro, in a body, a require's failure body and a default's template.
    [Theory]
    [InlineData("func stop() -> Never => $abort(\"stopped\")\n")]
    [InlineData("func check(n: i32) -> i32\n    require n >= 0 else => $abort(\"range\")\n    return n\n")]
    [InlineData("func halt(n: i32, value: i32 = $abort(\"halt\")) -> i32 => value\nlet x = halt(1)\n")]
    public void AnAbortIsLocatedAtItsMacro(string source)
    {
        var c = Compile(source);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var aborts = program.Bodies.ToArray().SelectMany(static x => x.Blocks.ToArray()).Select(static x => x.Terminator)
            .Where(static x => x is { Kind: MirTerminatorKind.Call, Callee: { Kind: MirCalleeKind.Intrinsic, Arg: (int)IntrinsicId.Abort } })
            .Where(static x => x.Source.Syntax.Module == 0).ToArray();
        Assert.NotEmpty(aborts);
        Assert.All(aborts, x => Assert.IsType<MacroKoto>(c.SyntaxOf(x.Source.Syntax)));
    }

    // SPEC 22.2, 16.3.2 (InlineStorage's contract in Storage.kimi): the compiler never destroys an InlineStorage's contents, so the
    // destructor body of a fixed array's owning remainder, which destroys the remaining elements itself, ends without a Drop of its
    // receiver or of any of its parts.
    [Fact]
    public void AnInlineStorageIsNeverDestroyed()
    {
        var program = Compile("public func main()\n    let values: [3 of string] = [\"a\", \"b\", \"c\"]\n    for value in values@move => ()\n").BuildMir();
        Assert.Empty(program.Findings);
        var drop = program.Bodies.ToArray()
            .Single(static x => x.Key is { Kind: MirBodyKind.Fn, Decl.Name: "drop" } && x.Locals[1].Type.Symbol?.Name == "FixedOwnedRemainder");
        Assert.DoesNotContain(drop.Blocks.ToArray(), static x => x.Terminator.Kind == MirTerminatorKind.Drop && x.Terminator.Place.Root == 1);
    }

    // DECISIONS R4-M61: the publisher gives a node an Op only with the facts its building rule reads, so a node Binding resolved without
    // them (a call's, an indexer's or a view selection's plan, a local's symbol, a closure's definition, a case adaptation's plan, a loop's
    // iterator symbol, a write of an interpolation, a tuple operator's conforming Type, R5-M75) is published Error and recorded as
    // InternalInvariant_Kd at it, with an Opaque value in its place, which the validator accepts, never a fault. A node Binding rejected
    // that lacks the same fact is an Error derived from its error, without a record.
    [Fact]
    public void AResolvedNodeWithoutItsFactsIsABrokenContract()
    {
        var c = Compile(
            "func one() -> i32 => 1\nfunc called() -> i32 => one()\nfunc rejected() -> i32 => one()\nstruct Pair\n    Self is UniqIndexable<isize>\n" +
            "    associate Element is i32\n    var first: i32 = 1\n    public init() => ()\n" +
            "    public func index(self, key: ref/isize) -> ref/i32 during self => self.first@ref\n" +
            "    public func indexUniq(self: uniq/Self, key: ref/isize) -> uniq/i32 during self => self.first@uniq\n" +
            "func read(pair: ref/Pair) -> i32 => pair[0]\nfunc view(items: ref/UniqSlice<i32>) -> Slice<i32> during items => items[1..^1]\n" +
            "func local() -> i32\n    let x = 1\n    return x\n" +
            "func makeAdder(offset: i32) -> (i32) -> i32\n    return func [offset] (value) => value + offset\n" +
            "func discard<s/T>(value: i32) -> ()\n    s is owner or ref or object\n    _ = value@s\n" +
            "func sum(values: ref/Array<i32>) -> i32\n    var total = 0\n    for value in values\n        total += value\n    return total\n" +
            "func text(x: i32) -> string => \"a\\(x)\"\nfunc order(a: (i32, f64), b: (i32, f64)) -> bool => a < b\n");
        var nodes = KotoTree.Walk(c.Kotonoha.RootKoto).ToArray();
        var calls = nodes.OfType<InvocationKoto>().Where(static x => x.ToString() == "one()").ToArray();
        var call = calls[0];
        var indexes = nodes.OfType<IndexKoto>().ToArray();
        var local = nodes.OfType<VariableKoto>().Single(static x => x.ToString().StartsWith("let x", StringComparison.Ordinal));
        var closure = nodes.OfType<FunctionKoto>().Single(static x => x.IsAnonymous);
        var adaptation = nodes.OfType<ConversionKoto>().Single(static x => x.ToString() == "value@s"); // A case adaptation (SPEC 8.14).
        var loop = nodes.OfType<ForKoto>().Single();
        var text = nodes.OfType<InterpolatedStringKoto>().Single();
        Assert.Equal((2, 2), (calls.Length, indexes.Length));
        HirTables.PlanFor(call, written: false).Plan = null; // After Binding and before the first publish, the facts go missing.
        HirTables.PlanFor(calls[1], written: false).Plan = null;
        HirTables.Of(calls[1]).Failures[calls[1].SyntaxId] = BindingFailure.TypeMismatch; // Rejected by Binding as well.
        HirTables.PlanFor(c.Binding.IndexerCall(indexes[0], exclusive: false)!, written: false).Plan = null;
        HirTables.PlanFor(c.Binding.ViewRangeCall(indexes[1])!, written: false).Plan = null;
        HirTables.Of(local).Symbols[local.SyntaxId] = null;
        HirTables.PlanFor(closure, written: false).Plan = null;
        HirTables.PlanFor(adaptation, written: false).Plan = null;
        HirTables.Of(loop.Iteration!.Receiver).Symbols[loop.Iteration.Receiver.SyntaxId] = null;
        HirTables.PlanFor(text.Formatting!.Writes[1], written: false).Plan = null;
        var order = nodes.OfType<BinaryKoto>().Single(static x => x.ToString() == "a < b");
        var tuple = CallPlan.Of(order)!;
        Assert.True(tuple.TupleOperator);
        var (mapping, types, operations) = (tuple.ArgumentToParameter.ToArray(), tuple.TypeArguments.ToArray(), tuple.ArgumentOperations.ToArray());
        tuple.Set(tuple.Target, tuple.ReturnType, tuple.Receiver, mapping, types, operations: operations);
        tuple.TupleOperator = true; // Its conforming Type is gone.
        var program = c.BuildMir();
        Koto[] broken = [call, indexes[0], indexes[1], local, closure, adaptation, loop, text, order];
        Assert.Empty(program.Findings);
        Assert.All(broken.Append(calls[1]), static x => Assert.Equal(HirOp.Error, HirTables.Op(x)));
        Assert.Equal(
            broken.Select(SourceRef.Of),
            program.Records.Where(static x => x.Code == DiagnosticCode.InternalInvariant_Kd).Select(static x => x.Subject));
        Assert.DoesNotContain(SourceRef.Of(calls[1]), program.Records.Select(static x => x.Subject));
    }

    // SPEC 23.1.2, 13.7.2 (DECISIONS R4-M109): an input Binding rejected after selection is its consumer's derived value, an Opaque of the
    // Type the consumer takes, moved into it, with no Ref of a temp of the node's own Type; an update locates an Error receiver once (the
    // call it keeps evaluated once), and its getter and setter each take their own derived value, whose site names the located root
    // (fail-before: an Opaque of the node's own Type borrowed by each consumer).
    [Fact]
    public void ARejectedInputIsItsConsumersDerivedValue()
    {
        const string Source = """
            struct Tally
                public var n: i32 = 0
                public var item: i32 = 0
                    get(self: ref/Self during static) -> i32 => storage
                    set(self: uniq/Self, value: i32) -> () => storage = value
                public init() => ()
            struct Outer
                public var inner: Tally = Tally.init()
                public init() => ()
            func keepStatic(x: ref/i32 during static) -> ref/i32 during static => x
            func chosen(c: uniq/Outer, a: string, b: string) -> uniq/Outer during c => c
            func field(c: ref/Tally) -> ref/i32 during static => keepStatic(c.n)
            func update(c: uniq/Outer, s: string) => chosen(c, s@move, s@move).inner.item += 1
            """;
        var c = Compile(Source);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var field = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "field");
        Assert.DoesNotContain(field.Statements.ToArray(), static x => x.Rvalue.Kind == MirRvalueKind.Ref);
        Assert.Equal(["ref/i32"], Opaques(field).Select(static x => x.Type));
        var update = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "update");
        var opaques = Opaques(update);
        Assert.Equal(["Tally", "ref/Tally", "uniq/Tally"], opaques.Select(static x => x.Type));
        var located = opaques[0].Root;
        Assert.All(opaques.Skip(1), x => Assert.Equal([located], x.Roots));
        Assert.DoesNotContain(update.Statements.ToArray(), x => x.Rvalue.Kind == MirRvalueKind.Ref && x.Rvalue.Place.Root == located);
        Assert.Single(update.Blocks.ToArray(), static x => x.Terminator is { Kind: MirTerminatorKind.Call, Callee.Symbol.Name: "chosen" });
    }

    // SPEC 6.6, 3.6, 23.1.2 (DECISIONS R4-M125): a local's declaration Binding rejected, whose symbol it typed, declares its local as a
    // valid one does, which holds the declaration's derived value, and every use of the name is that one storage: its Borrow, its write
    // and a capture's Borrow, with no record (fail-before: each use an Opaque in a temp of its own statement, the write derived whole and
    // the capture a located Unsupported_Kd record).
    [Fact]
    public void ARejectedDeclarationsLocalIsItsNamesOneStorage()
    {
        var c = Compile("func f() -> i32\n    var h: i32 = \"s\"\n    let p = h@ref\n    h = 3\n    let g = func [h@ref] () => h@follow\n    return p@follow\n");
        Assert.Equal([DiagnosticCode.TypeMismatch_Kd], c.Binding.Issues.Select(static x => x.Code));
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.DoesNotContain(program.Records, static x => x.Subject.Module == 0);
        var body = program.Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn } && x.Key.Decl?.Name == "f");
        var h = new MirPlace(Array.FindIndex(body.Locals.ToArray(), static x => x.Name == "h"));
        Assert.Equal((MirLocalKind.Var, true), (body.Locals[h.Root].Kind, body.Locals[h.Root].Mutable));
        var opaque = Assert.Single(Opaques(body));
        Assert.Equal(("i32", h.Root), (opaque.Type, opaque.Root));
        var statements = body.Statements.ToArray();
        Assert.Equal(2, statements.Count(x => x.Rvalue.Kind == MirRvalueKind.Ref && x.Rvalue.Place == h));
        Assert.Equal(2, statements.Count(x => x.Kind == MirStatementKind.Assign && x.Place == h));
    }

    // SPEC 14.2.2, 3.5 (DECISIONS R4-M145): a discarded Place is acquired by its plan row's bare acquisition, as at a value position, into
    // its statement's temporary: a pair layer by case, a Copy, a stored exclusive reference Reborrowed in its own mode, and the Move a
    // bare Non-Copy Place requires (its TransferRequired_Kd at it), which a later `title@move` reads moved (A1's MovedPlace_Kd).
    [Fact]
    public void ADiscardedPlaceIsAcquiredByItsBareAcquisition()
    {
        var c = Compile("func f<s/T>(value: s/T, count: i32, r: uniq/i32, title: string)\n    s is owner or uniq\n    T is Copy\n    value\n    count\n    r\n" +
            "    title\npublic func main() => ()\n");
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var body = program.Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn } && x.Key.Decl?.Name == "f");
        var acquired = body.Statements.ToArray()
            .Where(x => x.Kind == MirStatementKind.Assign && body.Locals[x.Place.Root].Kind == MirLocalKind.Temp && x.Rvalue.Kind is MirRvalueKind.Use or MirRvalueKind.Ref)
            .Select(x => x.Rvalue.Kind == MirRvalueKind.Ref ? $"Ref {(MirRefKind)x.Rvalue.Op} {Steps(body, x.Rvalue.Place)}"
                : $"{body.Operands[x.Rvalue.Start].Kind} {Steps(body, body.Operands[x.Rvalue.Start].Place)}");
        Assert.Equal(["PairAcquire _1", "Copy _2", "Ref Uniq _3*", "Move _4"], acquired);
    }

    // SPEC 23.1.2 (DECISIONS R4-M145): a Place whose bare acquisition Binding left unset, a Place without a Type (its parameter's Type
    // unresolved), is a derived Opaque with no record, discarded (its plan row unset) or as an item no consumer took (its Adjust unset,
    // which the container reads as a discard), as any consumed child's unset Adjust is; a complete compilation has no such row
    // (HirFormsTest.EveryDiscardedPlaceHasItsAcquisition).
    [Fact]
    public void AnUnsetDiscardRowIsDerivedFromABindingError()
    {
        var c = Compile("func f(b: Missing, n: i32)\n    b\n    n\nfunc g(b: Missing) -> i32\n    b\npublic func main() => ()\n");
        Assert.Equal([DiagnosticCode.UnresolvedBinding_Kd, DiagnosticCode.UnresolvedBinding_Kd], c.Binding.Issues.Select(static x => x.Code));
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.DoesNotContain(program.Records, static x => x.Subject.Module == 0);
        foreach (var name in new[] { "f", "g" })
        {
            var body = program.Bodies.ToArray().Single(x => x.Key is { Kind: MirBodyKind.Fn } && x.Key.Decl?.Name == name);
            var b = Array.FindIndex(body.Locals.ToArray(), static x => x.Name == "b");
            Assert.Equal([b], Assert.Single(Opaques(body)).Roots);
        }
    }

    // SPEC 23.1.2 (PLAN G122): a char literal the lexer could not read is published Error, so its value is the derived Opaque of a node
    // the lexer rejected, with no record; a published Literal is built as its constant.
    [Fact]
    public void ACharLiteralWithoutItsValueIsDerived()
    {
        var c = CompilationTestHelper.Parse("func f() -> char => '\\q'\nfunc g() -> char => 'a'\npublic func main() => ()\n");
        Assert.Equal(nameof(DiagnosticCode.InvalidEscape_Kd), Assert.Single(TestDiagnostics.Of(c)).Code);
        c.Bind();
        _ = c.Binding.CheckStartup(OutputKind.Application);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.DoesNotContain(program.Records, static x => x.Subject.Module == 0);
        var bodies = program.Bodies.ToArray().Where(static x => x.Key is { Kind: MirBodyKind.Fn, Decl.Name: "f" or "g" })
            .ToDictionary(static x => x.Key.Decl!.Name);
        Assert.Equal(("char", 1, 0), (Assert.Single(Opaques(bodies["f"])).Type, Opaques(bodies["f"]).Length, Opaques(bodies["g"]).Length));
    }

    // SPEC 23.1.2 (PLAN G122; R5's condition on Literal's broken-contract arm): a user's literal error builds a derived value or the
    // located Unsupported, never InternalInvariant_Kd: the lexer's unreadable char is published Error, a number Binding cannot fit fails
    // in Binding (an Error), at a local, a parameter, a default or as a float, and a literal at an unresolved expected Type keeps its fit.
    [Theory]
    [InlineData("func f() -> char => '\\q'\n")]
    [InlineData("func f()\n    let x: u8 = 300\n")]
    [InlineData("func g(x: u8) => ()\nfunc f() => g(300)\n")]
    [InlineData("func g(x: i8 = 300) => ()\nfunc f() => g()\n")]
    [InlineData("func f() -> f32 => 1e39\n")]
    [InlineData("func f()\n    let x: Missing = 5\n")]
    public void ALiteralErrorBuildsNoInternalInvariant(string source)
    {
        var c = CompilationTestHelper.Parse(source + "public func main() => ()\n");
        c.Bind();
        _ = c.Binding.CheckStartup(OutputKind.Application);
        var program = c.BuildMir();
        Assert.NotEmpty(CompilationTestHelper.BindingErrors(c));
        Assert.Empty(program.Findings);
        Assert.DoesNotContain(program.Records, static x => x.Code == DiagnosticCode.InternalInvariant_Kd);
    }

    // A body's Opaque values in order: their destination's Type and root, and the roots their sites name.
    private static (string Type, int Root, int[] Roots)[] Opaques(MirBody body) => [.. body.Statements.ToArray()
        .Where(static x => x.Kind == MirStatementKind.Assign && x.Rvalue.Kind == MirRvalueKind.Opaque)
        .Select(x => (TyCtxt.Name(body.TypeOf(x.Place)!), x.Place.Root, body.OpaqueRoots.Slice(body.OpaqueSites[x.Rvalue.Arg].Start, body.OpaqueSites[x.Rvalue.Arg].Count).ToArray()))];
}
