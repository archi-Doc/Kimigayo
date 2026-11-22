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
    // A default reading its function's length slot, omitted by a closed and by a generic caller (ALengthSlotsNameIsItsValue).
    private const string Count = "func count<length N>(values: [N of i32], n: isize = N) -> isize => n\n" +
        "func outer<length M>(values: [M of i32]) -> isize => count(values)\nlet c = count([1, 2])\n";

    // Plan 6.2: a form without a building rule is one record at its location, however many times a body meets it (a defer body, built at
    // each way out of its scope), and an Opaque value naming the locals its syntax touches. A node the lexer or Binding rejected is a derived
    // Opaque without a record (SPEC 23.1.2): Binding's Error (a rejected declaration's kept initializer its own, DECISIONS Z16) and a use
    // of a local whose rejected declaration has no Type (one with a Type is its local's, ARejectedDeclarationsLocalIsItsNamesOneStorage).
    // Building goes on after each. A Field Binding failed whose own Type is valid (a struct with a misplaced `length` slot) has its
    // initializer published (PLAN G1071's gate 1, F's C2 ruling 261 (3)), so each constructor builds it, with no Opaque, record or finding
    // (it was an Opaque in each constructor while only a Resolved definition's parts were published). The unbuilt form is the tuple
    // default the call's substitution cannot instantiate (Unbuilt); its Opaque names the roots of the slots before the default.
    [Fact]
    public void UnsupportedFormsAreOpaqueAtOneRecord()
    {
        var c = CompilationTestHelper.Parse(
            "struct Grid<length N>\n    public var cells: [N of i32] = [N of 7]\n    public init() => ()\n    public init(y: i32) => ()\n" +
            Unbuilt.Declarations + $"func f(a: i32, x: i32) -> i32\n    let c = {Unbuilt.Callee}(x)\n    return a\n" +
            $"func g() -> i32 => missing\nfunc h(a: i32, c: bool, u: i32) -> i32\n    defer\n" +
            $"        let d = {Unbuilt.Callee}(u)\n    if c => return 1\n    return a\nfunc k() -> i32\n    let x = missing\n    return x\n");
        Assert.Equal(nameof(DiagnosticCode.MisplacedSyntax_Kd), Assert.Single(TestDiagnostics.Of(c)).Code);
        c.Bind();
        _ = c.Binding.CheckStartup(OutputKind.Application);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var bodies = program.Bodies.ToArray().Where(static x => x.Key.Decl?.Name is "f" or "g" or "h" or "k").ToDictionary(static x => x.Key.Decl!.Name);
        var unbuilt = bodies["f"];
        Assert.True(unbuilt.HasOpaque);
        Assert.Equal(new[] { 4 }, unbuilt.OpaqueUses.ToArray().Select(static x => x.Root)); // take's argument slot before the default.
        Assert.Contains(unbuilt.Blocks.ToArray(), static x => x.Terminator.Kind == MirTerminatorKind.Return);
        var records = program.Records(MirRecordClass.Construction).Where(static x => x.Subject.Module == 0).Select(x => c.SyntaxOf(x.Subject).ToString()).Order(StringComparer.Ordinal);
        Assert.Equal([$"{Unbuilt.Callee}(u)", $"{Unbuilt.Callee}(x)"], records);
        Assert.Equal(2, bodies["h"].OpaqueSites.ToArray().Count(x => c.SyntaxOf(x.Source).ToString() == $"{Unbuilt.Callee}(u)"));

        Assert.Equal(0, program.Bodies.ToArray().Count(x => x.OpaqueSites.ToArray().Any(y => c.SyntaxOf(y.Source).ToString() == "[N of 7]")));
        Assert.Equal(HirOp.Error, HirTables.Op(c.SyntaxOf(bodies["g"].OpaqueSites[0].Source)));
        Assert.Equal(["missing", "let x = missing", "x"], bodies["k"].OpaqueSites.ToArray().Select(x => c.SyntaxOf(x.Source).ToString()));
    }

    // DECISIONS Z17: an Opaque site names only what it does not decide, and a part its consumer reads is read at its own node, as the dump
    // prints them: a derived write's located right side its consumer acquires is read by a FakeRead of its Place (p.n, _1.1) and is no use,
    // a failed call's kept part stays undecided at its Place, a failed jump's operand is located and read (x at each exit its defer body is
    // built at), a constant operand gives no read and no use (N8), a part of no Type, a failed slot's, is undecided, and a right side that
    // is a Temporary (the failed slot's name, an Error's derived value) is its built value, no use. Each site's uses, in order, then the
    // reads.
    [Theory]
    [InlineData("func f()\n    var p = P.init(\"s\", 1)\n    missing = p.n\n", " / FakeRead(_1.1)")]
    [InlineData("func f()\n    var p = P.init(\"s\", 1)\n    missing(p.n)\n", "_1.1 / ")]
    [InlineData("func f() -> i32\n    var x: i32\n    defer => return x\n    return 1\n", " |  / FakeRead(_1) FakeRead(_1)")]
    [InlineData("func f()\n    defer => return 1\n", " / ")]
    [InlineData("func f()\n    var q = missing2\n    missing = q\n", " | _2 |  |  / ")]
    public void AnOpaqueSiteNamesOnlyWhatItDoesNotDecide(string source, string expected)
    {
        var program = Compile("struct P\n    public var s: string\n    public var n: i32\n    public init(s: string, n: i32)\n        self.s = s@move\n" +
            "        self.n = n\n" + source).BuildMir();
        Assert.Empty(program.Findings);
        var text = new StringBuilder();
        MirDump.Write(program, 0, text);
        var dump = text.ToString()[text.ToString().IndexOf("fn f @", StringComparison.Ordinal)..];
        var sites = Regex.Matches(dump, @"(?m)^  opaque \d+(?: @\d+)?:(.*)$").Select(static x => x.Groups[1].Value.Trim());
        var reads = Regex.Matches(dump, @"FakeRead\([^)]*\)").Select(static x => x.Value);
        Assert.Equal(expected, $"{string.Join(" | ", sites)} / {string.Join(" ", reads)}");
    }

    // SPEC 23.1.2 (DECISIONS R4-M70): where Binding or control flow published that it formed no value, the MIR forms none from facts
    // that do not exist and records nothing, so each body validates: a Field Binding formed no stored Type for (one naming a missing Type,
    // or inferred from a call it rejected) has no Place, so a constructor places no initializer there, nor a base subobject whose base
    // clause Binding formed no Type for, so its base call, written, omitted or with arguments, is not placed (PLAN G320, fail-before: a
    // Projection finding at the call). A body falling through a non-Unit result, which control flow rejects (an empty body, one ending
    // in a statement, one with a return on another path), leaves one derived Opaque value there, without a record or a root
    // (MirBuilder.Leave): the result temp is then never empty where the runtime edges deliver it, which MirInit's invariant requires
    // (fail-before: no value, and with the whole-temp carry published for every destination InternalInvariant_Kd at the function of
    // the last row). (A raw read of a C-layout struct Binding rejected was the derived Opaque's row until DECISIONS R4-M134 Q8 made
    // such a declaration no Binding error.)
    [Theory]
    [InlineData("struct S\n    var item: Missing = 7\n    var other: i32 = 1\n", "UnresolvedBinding_Kd", 0)]
    [InlineData("open struct B<T>\n    public init() => ()\nstruct D : B<Missing>\n    public init() : base() => ()\n", "UnresolvedBinding_Kd", 0)]
    [InlineData("open struct B<T>\n    public init() => ()\nstruct D : B<Missing>\n    public init() => ()\n", "UnresolvedBinding_Kd", 0)]
    [InlineData("struct D : Missing\n    public init(k: i32) : base(k + 1) => ()\n", "UnresolvedBinding_Kd", 0)]
    [InlineData("func f<T>() -> T\n    T is Copy\n", "FunctionFallthrough_Kd", 1)]
    [InlineData("func f(a: i32) -> i32\n    let z = 3\n", "FunctionFallthrough_Kd", 1)] // ft1
    [InlineData("func f(a: i32) -> i32\n    if a == 1 => return 2\n    let z = 3\n", "FunctionFallthrough_Kd", 1)] // ft2
    public void ValuesBindingDidNotFormAreDerived(string source, string code, int derived)
    {
        var c = Compile(source);
        Assert.Contains(CompilationTestHelper.BindingErrors(c), x => x.Code == code);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.DoesNotContain(program.Records(MirRecordClass.Construction), static x => x.Subject.Module == 0);
        Assert.DoesNotContain(program.Problems.All.ToArray(), static x => x.Code == DiagnosticCode.InternalInvariant_Kd);
        var sites = program.Bodies.ToArray().Where(static x => x.Source.Module == 0).SelectMany(static x => x.OpaqueSites.ToArray()).ToArray();
        Assert.Equal(derived, sites.Length);
        Assert.All(sites, static x => Assert.Equal(0, x.Count));
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
        Assert.DoesNotContain(program.Records(MirRecordClass.Construction), static x => x.Subject.Module == 0);
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
        Assert.Empty(program.Records(MirRecordClass.Construction));
        var body = program.Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn } && x.Key.Decl?.Name == "f");
        Assert.False(body.HasOpaque);
        var text = new StringBuilder();
        MirDump.Write(body, text, program.Drops);
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

    // SPEC 4.4.1, 7.2.3, 7.7.2 (R8-W2 c2a): a length slot's Name is its value, one constant form per length (condition (a)): a Length
    // constant where the body leaves the slot open (a function's body, a Closure's, a default's template, and a default's copy whose call
    // binds the slot to the caller's own), an Integer where the copy's call closes it; no record, finding or Opaque value (fail-before: an
    // Unsupported record at each Name).
    [Theory]
    [InlineData("func size<length N>(values: [N of i32]) -> isize => N\nlet n = size([1, 2, 3])\n", "Fn", "size", "const length N")]
    [InlineData("func f<length N>(a: [(N + 1) of u8])\n    let k: isize = N + 1\n    let b: [k of u8] = a\nf<1>([2 of 0])\n", "Fn", "f", "const length N")]
    [InlineData("func size<length N>(values: [N of i32]) -> isize\n    let read = func () -> isize => N\n    return read()\n", "Closure", null, "const length N")]
    [InlineData(Count, "DefaultTemplate", "count", "const length N")]
    [InlineData(Count, "Fn", "outer", "const length M")]
    [InlineData(Count, "Fn", null, "const 2_isize")]
    public void ALengthSlotsNameIsItsValue(string source, string bodyKind, string? name, string constant)
    {
        var kind = Enum.Parse<MirBodyKind>(bodyKind);
        var c = Compile(source);
        Assert.Empty(c.Binding.Issues);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.Empty(program.Records(MirRecordClass.Construction));
        var body = program.Bodies.ToArray().Single(x => x.Key.Kind == kind && x.Key.Decl?.Name == (kind == MirBodyKind.Closure ? x.Key.Decl?.Name : name) &&
            x.Source.Module == 0);
        Assert.False(body.HasOpaque);
        var text = MirDumpTest.Unsited(program, body);
        Assert.Contains(constant, text, StringComparison.Ordinal);
        Assert.Equal(constant.StartsWith("const length", StringComparison.Ordinal), text.Contains("const length", StringComparison.Ordinal));
    }

    // SPEC 4.3.1, 4.9.5, 12.2, 12.4.1 (DECISIONS R5-M129): an Array literal is one Array aggregate of its elements in source order, `[]` one
    // of none, an inner literal its own, and an element before the last that builds code secured where it is evaluated (A1 moves each
    // operand at the one Assign); no call, record or Opaque value (fail-before: the empty Array from a FixedArray aggregate, then reserve and
    // one append per element). A non-Copy element is written `s@move` (SPEC 3.5 row 3), built through its temporary (`_5 = move _2`); building
    // an explicit Move operand in place is the MIR builder's open item (R6-check H1).
    [Theory]
    [InlineData("Array<i32>", "[x, 2]", "_3 = Array(copy _1, const 2_i32): Array<i32>")]
    [InlineData("Array<i32>", "[]", "_3 = Array(): Array<i32>")]
    [InlineData("Array<i32>", "[x, g(), 3]", "_3 = Array(move _4, move _5, const 3_i32): Array<i32>")]
    [InlineData("Array<Array<i32>>", "[[x], []]", "_4 = Array(copy _1): Array<i32>|_5 = Array(): Array<i32>|_3 = Array(move _4, move _5): Array<Array<i32>>")]
    [InlineData("Array<(string, i32)>", "[(s@move, x)]", "_4 = Tuple(move _5, copy _1): (string, i32)|_3 = Array(move _4): Array<(string, i32)>")]
    public void AnArrayLiteralIsOneAggregateOfItsElements(string type, string literal, string aggregates)
    {
        var c = Compile($"func g() -> i32 => 1\nfunc f(x: i32, s: string) -> i32\n    let a: {type} = {literal}\n    return 0\n");
        Assert.Empty(c.Binding.Issues);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.Empty(program.Records(MirRecordClass.Construction));
        var body = program.Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn } && x.Key.Decl?.Name == "f");
        Assert.False(body.HasOpaque);
        var text = new StringBuilder();
        MirDump.Write(body, text, program.Drops);
        var built = Regex.Matches(text.ToString(), @"(_\d+ = (?:Array|Tuple|FixedArray)\(.*?\): .*?) @\d+").Select(static x => x.Groups[1].Value);
        Assert.Equal(aggregates.Split('|'), built);
        Assert.DoesNotContain(body.Blocks.ToArray(), x => x.Terminator.Kind == MirTerminatorKind.Call && x.Terminator.Callee.Symbol?.Name != "g");
    }

    // SPEC 3.5.1, 8.14 (PLAN G262, DECISIONS BU-45): an exclusive path and the acquiring binder's own layer keep their bare acquisition: a
    // Reborrow, a PairAcquire. A bare acquisition planned without a Reborrow through a shared layer is SPEC 3.5's error, Binding's record
    // since R6-check H1 (TransferJudgmentTest's shared-layer rows). Each row: the built statement, and the form no body may contain.
    [Theory]
    [InlineData("func f<s/T>(x: s/(s/T during a) during b)\n    s is ref or uniq\n    let y = x@follow\n", "_2 = acquire b0 (_1@b0)", "move (_1@b0)")]
    [InlineData("func f(r: uniq/(uniq/i32 during a) during b)\n    let y = r@follow\n", "_2 = &uniq (*(*_1))", "move (*(*_1))")]
    public void ABareAcquisitionThroughItsOwnLayersKeepsItsPlan(string source, string built, string absent)
    {
        var c = Compile(source);
        Assert.Empty(c.Binding.Issues);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.Empty(program.Records(MirRecordClass.Construction));
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
        Assert.Equal(type, TyCtxt.Name(HirTables.TypeOf(node)!));
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.DoesNotContain(program.Records(MirRecordClass.Construction), static x => x.Subject.Module == 0);
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
        Assert.DoesNotContain(program.Records(MirRecordClass.Construction), static x => x.Subject.Module == 0);
        var f = program.Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn, Decl.Name: "f" });
        var temps = f.Locals.ToArray().Where(x => x.Kind == MirLocalKind.Temp && c.SyntaxOf(x.Source.Syntax).ToString() == target).Select(static x => TyCtxt.Name(x.Type!));
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
            .Single(static x => x.Key is { Kind: MirBodyKind.Fn, Decl.Name: "drop" } && x.Locals[1].Type?.Symbol?.Name == "FixedOwnedRemainder");
        Assert.DoesNotContain(drop.Blocks.ToArray(), static x => x.Terminator.Kind == MirTerminatorKind.Drop && x.Terminator.Place.Root == 1);
    }

    // SPEC 5.2.3 (R8-A1d): ArrayOwningIterator's constructor takes the Array's component from a held Array, which nothing destroys, so the
    // constructor ends without a Drop of its InlineStorage local.
    [Fact]
    public void AHeldArrayIsNeverDestroyed()
    {
        var program = Compile("public func main()\n    let values: Array<string> = [\"a\"]\n    for value in values@move => ()\n").BuildMir();
        Assert.Empty(program.Findings);
        var init = program.Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn, Decl.Name: "init" } &&
            x.Locals.ToArray().Any(static y => y.Type?.Symbol?.Name == "ArrayOwningIterator") && x.Locals.ToArray().Any(static y => y.Type?.Symbol?.Name == "InlineStorage"));
        Assert.DoesNotContain(init.Blocks.ToArray(), x => x.Terminator.Kind == MirTerminatorKind.Drop && init.TypeOf(x.Terminator.Place)?.Symbol?.Name == "InlineStorage");
    }

    // DECISIONS R4-M61: the publisher gives a node an Op only with the facts its building rule reads, so a node Binding resolved without
    // them (a call's, an indexer's or a view selection's plan, a local's symbol, a closure's definition, a case adaptation's plan, a loop's
    // iterator symbol, a write of an interpolation, a tuple operator's conforming Type, R5-M75; since PLAN G396 (3) Z1 a plain call's
    // operations covering its parameters, a member access's metadata form, an array literal's array Type) is published Error and recorded
    // as InternalInvariant_Kd at it, with an Opaque value in its place, which the validator accepts, never a fault. A node Binding rejected
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
            "func text(x: i32) -> string => \"a\\(x)\"\nfunc order(a: (i32, f64), b: (i32, f64)) -> bool => a < b\n" +
            "func two(a: i32, b: i32) -> i32 => a + b\nfunc paired() -> i32 => two(1, 2)\nstruct Q\n    public var x: i32 = 1\n    public init() => ()\n" +
            "func field(q: ref/Q) -> i32 => q.x\nfunc shaped() -> isize\n    let values = [1, 2]\n    return values.length\n");
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
        HirTables.PlanFor(call, written: false).Plan = null; // After Binding the facts go missing; CheckBound publishes them as they are.
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
        var tuple = CallPlan.Of(c.Binding, order)!;
        Assert.True(tuple.TupleOperator);
        var (mapping, types, operations) = (tuple.ArgumentToParameter.ToArray(), tuple.TypeArguments.ToArray(), tuple.ArgumentOperations.ToArray());
        tuple.Set(c.Binding, tuple.Target, tuple.ReturnType, tuple.Receiver, mapping, types, operations: operations);
        tuple.TupleOperator = true; // Its conforming Type is gone.
        var paired = nodes.OfType<InvocationKoto>().Single(static x => x.ToString() == "two(1, 2)");
        var two = CallPlan.Of(c.Binding, paired)!;
        var (pairs, arguments, inputs) = (two.ArgumentToParameter[..1].ToArray(), two.TypeArguments.ToArray(), two.ArgumentOperations[..1].ToArray());
        two.Set(c.Binding, two.Target, two.ReturnType, two.Receiver, pairs, arguments, operations: inputs); // One input for two parameters.
        var member = nodes.OfType<MemberAccessKoto>().Single(static x => x.ToString() == "q.x");
        HirTables.Of(member).Symbols[member.SyntaxId] = null; // A resolved member access without a symbol: a metadata form, which `x` is not.
        var literal = nodes.OfType<ArrayLiteralKoto>().Single();
        HirTables.Of(literal).Meanings[literal.SyntaxId] = BoundType.Primitives["i32"];
        c.Binding.CheckBound(); // A Final pass publishes in its Check (DECISIONS BU-81), so the end of the pass runs again over the gaps.
        var program = c.BuildMir();
        Koto[] broken = [call, indexes[0], indexes[1], local, closure, adaptation, loop, text, order, paired, member, literal];
        Assert.Empty(program.Findings);
        Assert.All(broken.Append(calls[1]), static x => Assert.Equal(HirOp.Error, HirTables.Op(x)));
        Assert.Equal(broken, HirTables.Of(call).Broken!); // The publisher's list: the resolved nodes without their facts, not the rejected one.
        // Each is recorded once; a use of a broken node's result (the name of the broken `let x`) is not this contract's subject.
        var internals = program.Records(MirRecordClass.Construction).Where(static x => x.Code == DiagnosticCode.InternalInvariant_Kd).Select(static x => x.Subject).ToArray();
        Assert.All(broken, x => Assert.Single(internals, y => y == SourceRef.Of(x)));
        Assert.DoesNotContain(SourceRef.Of(calls[1]), program.Records(MirRecordClass.Construction).Select(static x => x.Subject));
    }

    // SPEC 23.1.2, DECISIONS R4-M61's slot rows: a slot of no Type is the failed slot only where Binding recorded a cause at its
    // TypeSource, so a node declaring one without it (a function's result, a parameter, also of a definition Binding rejected, a let's
    // local, a match's or a for loop's binding, a Closure's parameter or result) is published Error and recorded as InternalInvariant_Kd at it, never a fault; a default
    // template is built only beside its definition's Body (a twin keeps its own). A read of a broken slot is a broken contract too, at the
    // read (PLAN G396 (3) Z1, F's C2 ruling 310): the use of the broken `let x`, whose local no build binds.
    [Fact]
    public void ASlotOfNoTypeWithoutItsFailedFactIsABrokenContract()
    {
        var c = Compile(
            "func result() -> i32 => 1\nfunc param(x: i32, n: i32 = 1) -> i32 => x\nfunc kept(x: i32, n: i32 = 1) -> i32 => x\n" +
            "group G\n    protected func guarded(x: i32) -> i32 => x\nfunc local() -> i32\n    let x = 1\n    return x\n" +
            "func pick(o: Option<i32>) -> i32\n    match o\n        .Some(let v) => return v\n        _ => return 0\n" +
            "func sum(values: ref/Array<i32>) -> i32\n    var total = 0\n    for value in values\n        total += value\n    return total\n" +
            "func closures() -> i32\n    let p = func [] (v: i32) -> i32 => v\n    let r = func [] () -> i32 => 1\n    return p(1) + r()\n");
        var nodes = KotoTree.Walk(c.Kotonoha.RootKoto).ToArray();
        var literals = nodes.OfType<FunctionKoto>().Where(static x => x.IsAnonymous).ToArray();
        var functions = nodes.OfType<FunctionKoto>().Where(static x => !x.IsAnonymous).ToDictionary(static x => x.Name);
        var local = nodes.OfType<VariableKoto>().Single(static x => x.ToString().StartsWith("let x", StringComparison.Ordinal));
        var match = nodes.OfType<MatchKoto>().Single();
        var loop = nodes.OfType<ForKoto>().Single();
        var read = nodes.OfType<IdentifierNameKoto>().Single(x => x.Parent is ReturnKoto && ReferenceEquals(HirTables.Symbol(x), HirTables.Symbol(local)));
        HirTables.Symbol(functions["result"])!.Type = null; // After Binding the Types go with no cause recorded; CheckBound publishes them.
        c.Binding.ParameterSymbol(functions["param"], 0).Type = null;
        c.Binding.ParameterSymbol(functions["guarded"], 0).Type = null; // Rejected by Binding (a protected function in a group, SPEC 9.4).
        HirTables.Symbol(local)!.Type = null;
        BoundMatch.Of(c.Binding, match)!.Positions.Single(static x => x.BodySymbol is not null).BodySymbol!.Type = null;
        BoundMatch.Of(c.Binding, loop)!.Positions.Single(static x => x.BodySymbol is not null).BodySymbol!.Type = null;
        c.Binding.ParameterSymbol(literals[0], 0).Type = null; // A Closure's parameter and result slots (PLAN G1110).
        HirTables.Symbol(literals[1])!.Type = null;
        c.Binding.CheckBound();
        var program = c.BuildMir();
        Koto[] broken = [functions["result"], functions["param"], functions["guarded"], local, match, loop, literals[0], literals[1]];
        Assert.Empty(program.Findings);
        Assert.All(broken, static x => Assert.Equal(HirOp.Error, HirTables.Op(x)));
        Assert.Equal(
            broken.Append(read).Select(SourceRef.Of).OrderBy(static x => x.Id),
            program.Records(MirRecordClass.Construction).Where(static x => x.Code == DiagnosticCode.InternalInvariant_Kd).Select(static x => x.Subject).OrderBy(static x => x.Id));
        var templates = program.Bodies.ToArray().Where(static x => x.Key.Kind == MirBodyKind.DefaultTemplate).Select(static x => x.Key.Decl!.Name);
        Assert.Equal(["kept"], templates);
    }

    // PLAN G1073 (SPEC 23.1.2; F's C2 ruling 308 (4)): an accessor whose written receiver's Type failed executes with no self input, and
    // its body's `storage` reads its self, the failed slot: the write is derived from the slot's record (an Opaque, no construction
    // record; fail-before: InternalInvariant_Kd at `storage`, Z1's no-receiver arm). The twin with a valid self writes its self's Field.
    [Theory]
    [InlineData("uniq/Missing")]
    [InlineData("uniq/Self")]
    public void AStorageWriteThroughAFailedSelfIsDerived(string receiver)
    {
        var c = Compile($"struct HW\n    public var x: i32\n        set(self: {receiver}, value: i32) -> ()\n            storage = 1\n    public init(x: i32) => self.x = x\n");
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.Empty(program.Records(MirRecordClass.Construction));
        var body = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "x.Set");
        var writes = body.Statements.ToArray().Count(x => x.Kind == MirStatementKind.Assign && x.Place.Projection != 0 && body.Projections[x.Place.Projection].Kind == MirProjectionKind.Field);
        Assert.Equal(receiver == "uniq/Self" ? 1 : 0, writes);
        Assert.Equal(receiver != "uniq/Self", body.HasOpaque);
    }

    // PLAN G396 (3) Z1 (DECISIONS R4-M61; consumers trust validated IR): an arm of a closed HIR vocabulary, or of a fact a published Op
    // guarantees, that no published form takes is a broken contract, InternalInvariant_Kd at the node with an Opaque value in its place,
    // which the validator accepts. Each row publishes one node so by hand after the publish (fail-before: Unsupported_Kd at the same node):
    // a call whose operations do not cover its parameters (B2), a PrimitiveOp of no operator form (B4), a member access without a metadata
    // form (B5), a Downcast Place outside a Pattern (B6), a local's name its body does not bind (B7), an instance Property's storage without a
    // receiver (B8), an Op that is no value (B12), a storage projection taken as an operand, and a base borrow or an erasure whose consumer
    // gives no Type (B13; `t.0` an operand of `+`, SPEC 13.4), an array literal of no array Type (B14), a capture whose source no binding
    // holds (B15), and a target that is no Place without a setter (B16).
    [Theory]
    [InlineData("B2")]
    [InlineData("B4")]
    [InlineData("B5")]
    [InlineData("B6")]
    [InlineData("B7")]
    [InlineData("B8")]
    [InlineData("B12")]
    [InlineData("B13")]
    [InlineData("B13 BaseBorrow")]
    [InlineData("B13 Erasure")]
    [InlineData("B14")]
    [InlineData("B15")]
    [InlineData("B16")]
    public void AnArmNoPublishedFormTakesIsABrokenContract(string site)
    {
        var c = Compile(
            "struct P\n    public var x: i32 = 1\n    public init() => ()\nfunc two(a: i32, b: i32) -> i32 => a + b\nfunc other() -> i32\n    let far = 3\n" +
            "    return far\nfunc f(p: uniq/P, q: ref/i32) -> i32\n    let t = (1, 2)\n    let a = [1, 2]\n    let k = 4\n    let c = func [k] () -> i32 => k\n" +
            "    p.x = 5\n    let r = p.x\n    let s = two(1, 2)\n    return t.0 + a[0] + c() + s + r + q@follow\n");
        var nodes = KotoTree.Walk(c.Kotonoha.RootKoto).ToArray();
        Koto Initializer(string name) => nodes.OfType<VariableKoto>().Single(x => x.ToString().StartsWith($"let {name} =", StringComparison.Ordinal)).InitializerKoto!;
        var far = HirTables.Symbol(nodes.OfType<VariableKoto>().Single(static x => x.ToString().StartsWith("let far", StringComparison.Ordinal)))!;
        var call = (InvocationKoto)Initializer("s");
        var element = nodes.OfType<MemberAccessKoto>().Single(static x => x.ToString() == "t.0");
        var name = element.Left;
        var assignment = nodes.OfType<BinaryKoto>().Single(static x => x.ToString() == "p.x = 5");
        Koto node = site switch
        {
            "B2" or "B13" => site == "B2" ? call : call.ArgumentNodes[0],
            "B4" or "B12" => Initializer("t"),
            "B5" => Initializer("r"),
            "B6" => nodes.OfType<ConversionKoto>().Single(static x => x.ToString() == "q@follow").Left,
            "B7" or "B8" => name,
            "B13 BaseBorrow" or "B13 Erasure" => element,
            "B14" => Initializer("a"),
            "B15" => Initializer("c"),
            _ => assignment,
        };
        ref var hir = ref HirTables.Of(node);
        switch (site)
        {
            case "B2":
                var plan = CallPlan.Of(c.Binding, call)!;
                var (pairs, arguments, inputs) = (plan.ArgumentToParameter[..1].ToArray(), plan.TypeArguments.ToArray(), plan.ArgumentOperations[..1].ToArray());
                plan.Set(c.Binding, plan.Target, plan.ReturnType, plan.Receiver, pairs, arguments, operations: inputs);
                break;
            case "B4" or "B5" or "B12":
                hir.Ops![node.SyntaxId] = site == "B12" ? HirOp.Let : HirOp.PrimitiveOp;
                hir.Adjusts![node.SyntaxId] = new(HirAcquire.Temporary, HirAdaptation.None); // Built where its consumer takes it (Into).
                break;
            case "B6":
                hir.Places![node.SyntaxId] = new(HirPlaceKind.Downcast, 0);
                break;
            case "B7" or "B8":
                hir.Symbols[node.SyntaxId] = site == "B7" ? far : HirTables.Symbol(assignment.Left);
                break;
            case "B13" or "B13 BaseBorrow" or "B13 Erasure":
                var adaptation = site == "B13" ? HirAdaptation.StorageProjection : site == "B13 BaseBorrow" ? HirAdaptation.BaseBorrow : HirAdaptation.Erasure;
                hir.Adjusts![node.SyntaxId] = new(HirAcquire.Copy, adaptation);
                break;
            case "B14":
                hir.Meanings[node.SyntaxId] = BoundType.Primitives["i32"];
                break;
            case "B15": // The captured local's declaration names another symbol, so the capture's source has no binding in the body.
                var declaration = nodes.OfType<VariableKoto>().Single(static x => x.ToString().StartsWith("let k", StringComparison.Ordinal));
                HirTables.Of(declaration).Symbols[declaration.SyntaxId] = far;
                break;
            default:
                HirTables.Of(assignment.Left).Places![assignment.Left.SyntaxId] = new(HirPlaceKind.Value, 0);
                break;
        }

        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.Equal(
            [(DiagnosticCode.InternalInvariant_Kd, SourceRef.Of(node))],
            program.Records(MirRecordClass.Construction).Where(static x => x.Subject.Module == 0).Select(static x => (x.Code, x.Subject)));
    }

    // SPEC 15.6.1, 15.6.5, 13.7.2, 23.1.2 (R7-O1 b1, superseding DECISIONS R4-M109 for call positions): an input whose call-position
    // relation the premises do not prove is no rejected input (the relation is A5's row), so the MIR builds it as a valid input: `field`
    // borrows `c.n` for the call (one Ref, no Opaque), and `update` evaluates `chosen(...)` once, its getter and setter each taking the
    // receiver by its own Ref, with no Opaque (fail-before: an Opaque of each consumer's Type in place of each input).
    [Fact]
    public void ACallPositionsInputIsBuiltAsAValidOne()
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
        Assert.DoesNotContain(c.Binding.Issues, static x => x.Failure == BindingFailure.OriginRelation);
        var field = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "field");
        Assert.Single(field.Statements.ToArray(), static x => x.Rvalue.Kind == MirRvalueKind.Ref);
        Assert.Empty(Opaques(field));
        var update = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "update");
        Assert.Empty(Opaques(update));
        Assert.Equal(3, update.Statements.ToArray().Count(static x => x.Rvalue.Kind == MirRvalueKind.Ref));
        Assert.Equal(
            ["chosen", "item.Get", "item.Set"],
            update.Blocks.ToArray().Where(static x => x.Terminator is { Kind: MirTerminatorKind.Call }).Select(static x => x.Terminator.Callee.Symbol?.Name));
    }

    // SPEC 23.1.2, 13.7.2 (DECISIONS R4-M109; superseded for call positions only, ACallPositionsInputIsBuiltAsAValidOne): an input Binding
    // rejects after selection, here a static Place's Borrow (the located limit until P42, DECISIONS BU-25), is its consumer's derived
    // value, an Opaque of the Type the consumer takes, moved into it, with no Ref of a temp of the node's own Type; an update locates an
    // Error receiver once (the index call it keeps evaluated once), and its setter's reservation, made there (SPEC 13.7.2), and its getter
    // each take their own derived value, whose site names no use: the located receiver holds the Error's derived value, a Temporary part
    // (DECISIONS Z17; before Z17, the located root); the setter takes that reservation.
    [Fact]
    public void ARejectedInputIsItsConsumersDerivedValue()
    {
        const string Source = """
            struct Tally
                public var n: i32 = 0
                public var item: i32 = 0
                    get(self: ref/Self) -> i32 => storage
                    set(self: uniq/Self, value: i32) -> () => storage = value
                public init() => ()
            group State
                public var tally: Tally = Tally.init()
                public var tallies: [2 of Tally] = [Tally.init(), Tally.init()]
            func peek(x: ref/i32) => ()
            func at() -> isize => 0
            func field() => peek(State.tally.n)
            func update() => State.tallies[at()].item += 1
            """;
        var c = Compile(Source);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.Equal(["Unsupported_Kd State.tallies[at()]", "Unsupported_Kd State.tally.n"], c.Binding.Issues.Select(static x => $"{x.Code} {x.Node}").Order(StringComparer.Ordinal));
        var field = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "field");
        Assert.DoesNotContain(field.Statements.ToArray(), static x => x.Rvalue.Kind == MirRvalueKind.Ref);
        Assert.Equal(["ref/i32"], Opaques(field).Select(static x => x.Type));
        var update = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "update");
        var opaques = Opaques(update);
        Assert.Equal(["Tally", "uniq/Tally", "ref/Tally"], opaques.Select(static x => x.Type));
        var located = opaques[0].Root;
        Assert.All(opaques.Skip(1), static x => Assert.Empty(x.Roots));
        Assert.DoesNotContain(update.Statements.ToArray(), x => x.Rvalue.Kind == MirRvalueKind.Ref && x.Rvalue.Place.Root == located);
        Assert.Single(update.Blocks.ToArray(), x => x.Terminator is { Kind: MirTerminatorKind.Call, Callee.Symbol.Name: "item.Set" } &&
            update.Operands[x.Terminator.Start] == MirOperand.Move(new MirPlace(opaques[1].Root)));
        Assert.Single(update.Blocks.ToArray(), static x => x.Terminator is { Kind: MirTerminatorKind.Call, Callee.Symbol.Name: "at" });
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
        Assert.DoesNotContain(program.Records(MirRecordClass.Construction), static x => x.Subject.Module == 0);
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
    // its statement's temporary: a pair layer by case, a Copy, a stored exclusive reference Reborrowed in its own mode, and the explicit
    // Move of a Non-Copy Place (`title@move`; SPEC 3.5 row 3 rejects a bare one: TransferJudgmentTest, R6-check H1).
    [Fact]
    public void ADiscardedPlaceIsAcquiredByItsBareAcquisition()
    {
        var c = Compile("func f<s/T>(value: s/T, count: i32, r: uniq/i32, title: string)\n    s is owner or uniq\n    T is Copy\n    value\n    count\n    r\n" +
            "    title@move\npublic func main() => ()\n");
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var body = program.Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn } && x.Key.Decl?.Name == "f");
        var acquired = body.Statements.ToArray()
            .Where(x => x.Kind == MirStatementKind.Assign && body.Locals[x.Place.Root].Kind == MirLocalKind.Temp && x.Rvalue.Kind is MirRvalueKind.Use or MirRvalueKind.Ref)
            .Select(x => x.Rvalue.Kind == MirRvalueKind.Ref ? $"Ref {(MirRefKind)x.Rvalue.Op} {Steps(body, x.Rvalue.Place)}"
                : $"{body.Operands[x.Rvalue.Start].Kind} {Steps(body, body.Operands[x.Rvalue.Start].Place)}");
        Assert.Equal(["PairAcquire _1", "Copy _2", "Ref Uniq _3*", "Move _4"], acquired);
    }

    // SPEC 14.2.2, 14.2.4, 15.6.3 (DECISIONS BU-98, PLAN G233): a discarded stored exclusive reference is held in a temporary of its
    // Reborrow's Type, Binding's name for it (the meet of the layers reaching it; BU-27), whether the discard is explicit, bare or
    // parenthesized, never of the Place's stored Type.
    [Fact]
    public void ADiscardedReborrowIsHeldAtBindingsName()
    {
        var c = Compile("struct Holder2 {a}\n    public var link: uniq/i32 during a\nfunc f(h: uniq/(Holder2 during a) during s) -> ()\n" +
            "    _ = h.link\n    h.link\n    (h.link)\n    _ = (h.link)\npublic func main() => ()\n");
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var body = program.Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn } && x.Key.Decl?.Name == "f");
        var held = body.Statements.ToArray().Where(static x => x.Kind == MirStatementKind.Assign && x.Rvalue.Kind == MirRvalueKind.Ref)
            .Select(x => (Binding.HoverTypeName(body.Locals[x.Place.Root].Type!), Binding.HoverTypeName(x.Rvalue.Type!)));
        Assert.Equal(Enumerable.Repeat(("uniq/i32 during (s and a)", "uniq/i32 during (s and a)"), 4), held);
    }

    // PLAN G1073 (SPEC 23.1.2; the design's R1-10): a parameter whose Type Binding failed is a local of no Type, in its function's body and,
    // as the prepared input of a later default's template, never a reference over no Type; both bodies validate, with no record (g's
    // call omits the default into a failed signature: an Opaque, no copy).
    [Fact]
    public void AFailedParameterIsALocalOfNoTypeInItsBodyAndALaterDefaultsTemplate()
    {
        var c = Compile("func g<T>(t: T, u: Missing, v: i32 = 1) -> i32\n    return v\nfunc h() -> i32\n    return g(1, 2)\n");
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.DoesNotContain(program.Records(MirRecordClass.Construction), static x => x.Code == DiagnosticCode.InternalInvariant_Kd);
        var bodies = program.Bodies.ToArray().Where(static x => x.Key.Decl?.Name == "g").ToDictionary(static x => x.Key.Kind);
        Assert.Null(bodies[MirBodyKind.Fn].Locals.ToArray().Single(static x => x.Name == "u").Type);
        Assert.Null(bodies[MirBodyKind.DefaultTemplate].Locals.ToArray().Single(static x => x.Name == "u").Type);
    }

    // SPEC 23.1.2 (DECISIONS R4-M145, BU-103): a Place whose bare acquisition failed, a Place without a Type (its parameter's Type
    // unresolved), is a derived Opaque with no record, discarded or as an item no consumer took (its Adjust unset, which the container
    // reads as a discard), its plan row Failed, as any consumed child's Failed Adjust is; a complete compilation has no such row
    // (HirFormsTest.EveryDiscardedPlaceHasItsAcquisition). The discarded value is of no Type, the statement's Type Binding failed (PLAN
    // G1073: MirBuilder.Held). g's fallthrough into i32 rests on that Error (no FunctionFallthrough_Kd), so its result is a second derived
    // Opaque, without a root (MirBuilder.Leave).
    [Fact]
    public void AFailedDiscardRowIsDerivedFromABindingError()
    {
        var c = Compile("func f(b: Missing, n: i32)\n    b\n    n\nfunc g(b: Missing) -> i32\n    b\npublic func main() => ()\n");
        Assert.Equal([DiagnosticCode.UnresolvedBinding_Kd, DiagnosticCode.UnresolvedBinding_Kd], c.Binding.Issues.Select(static x => x.Code));
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.DoesNotContain(program.Records(MirRecordClass.Construction), static x => x.Subject.Module == 0);
        foreach (var name in new[] { "f", "g" })
        {
            var body = program.Bodies.ToArray().Single(x => x.Key is { Kind: MirBodyKind.Fn } && x.Key.Decl?.Name == name);
            var b = Array.FindIndex(body.Locals.ToArray(), static x => x.Name == "b");
            var opaques = Opaques(body);
            Assert.Equal([b], opaques[0].Roots);
            Assert.Equal(name == "g" ? [null, "i32"] : [null], opaques.Select(static x => x.Type));
            Assert.Equal(name == "g" ? [0] : [], opaques[1..].Select(static x => x.Root + x.Roots.Length));
        }
    }

    // SPEC 23.1.2 (DECISIONS BU-103): a consumed Place whose acquisition failed (no Type, or a Move resting on an Error Copy proof; an
    // Error node's folds to (Temporary, None), R4-M109) gives its consumer a derived value, which writes the destination: a body's result
    // (`_0`) and each branch of a Var's initializer, also through a group or a typeless alias (fail-before: `_0` and the else path's `y`
    // were never written, so A1 reported UninitializedPlace_Kd at `y`). No record is added.
    [Fact]
    public void AFailedAcquisitionWritesItsDestination()
    {
        var c = Compile("struct Bad\n    Self is Copy\n    public var s: string = \"\"\n    public init() => ()\nfunc a(b: Bad) -> Bad => b\n" +
            "func f(t: (i32, i32)) -> i32 => t.5\nfunc k(c: bool) -> i32\n    let x = missing\n    let y: i32 = if c => 1 else => x\n    return y\n" +
            "func e(c: bool) -> i32\n    let x = missing\n    let y: i32 = if c => 1 else => (x)\n    return y\n" +
            "func g(c: bool) -> i32\n    let x = missing\n    let z = x\n    let y: i32 = if c => 1 else => z\n    return y\npublic func main() => ()\n");
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.DoesNotContain(program.Records(MirRecordClass.Construction), static x => x.Subject.Module == 0);
        foreach (var (name, local) in new[] { ("a", (string?)null), ("f", null), ("k", "y"), ("e", "y"), ("g", "y") })
        {
            var body = program.Bodies.ToArray().Single(x => x.Key is { Kind: MirBodyKind.Fn } && x.Key.Decl?.Name == name);
            var root = local is null ? 0 : Array.FindIndex(body.Locals.ToArray(), x => x.Name == local);
            var writes = body.Statements.ToArray().Count(x => x.Kind == MirStatementKind.Assign && x.Place == new MirPlace(root));
            Assert.True(writes == (local is null ? 1 : 2), $"{name}: {writes} writes of _{root}");
        }
    }

    // SPEC 15.1.6, 14.6.2, 3.5.2, 23.1.2 (DECISIONS BU-110): a for item is acquired by the match's rule, so one whose Copy proof is Error is
    // published Failed with no record of its own (the declaration's is reported) and its local takes a value derived from that failure,
    // while the loop and its body are built: the body's independent MovedPlace_Kd stays. Fail-before: the item was moved out of the
    // Subject's payload.
    [Fact]
    public void AForItemWhoseCopyProofIsErrorIsDerived()
    {
        var c = Compile("struct Bad\n    Self is Copy\n    public var s: string = \"\"\n    public init() => ()\nfunc g(xs: Array<Bad>)\n" +
            "    for x in xs@move\n        let s: string = \"a\"\n        let t = s@move\n        let u = s@move\npublic func main() => ()\n");
        Assert.Equal(["UnsatisfiedConstraint_Kd Self is Copy"], c.Binding.Issues.Select(static x => $"{x.Code} {x.Node}"));
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.Equal(
            ["MovedPlace_Kd s"],
            program.Problems.All.ToArray().Where(static x => !x.IsRecord && x.Subject.Module == 0).Select(x => $"{x.Code} {c.SyntaxOf(x.Subject)}"));
        var body = program.Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn } && x.Key.Decl?.Name == "g");
        var statements = body.Statements.ToArray();
        var item = Array.FindIndex(body.Locals.ToArray(), static x => x.Name == "x");
        var write = Assert.Single(statements, x => x is { Kind: MirStatementKind.Assign, Rvalue.Kind: MirRvalueKind.Use } && x.Place == new MirPlace(item));
        var source = body.Operands[write.Rvalue.Start].Place;
        Assert.Contains(statements, x => x is { Kind: MirStatementKind.Assign, Rvalue.Kind: MirRvalueKind.Opaque } && x.Place == new MirPlace(source.Root));
    }

    // SPEC 15.1.6, 14.8, 23.1.2 (DECISIONS R4-M179): a match binding whose Copy proof is Error is Failed with no record of its own and takes
    // a derived value, while its match is built as over the Copy twin: an arm's independent MovedPlace_Kd stays, in either arm and either
    // match of a nesting, and a use of the match's value adds none; an arm reading the binding by value fails at bind time and adds no
    // MIR record. Fail-before: the match was built Opaque.
    [Theory]
    [InlineData("    let s: string = \"a\"\n    match o@move\n        .Some(let v) => pair(s@move, s@move)\n        .None => ()\n", "MovedPlace_Kd s")]
    [InlineData("    let t: string = \"b\"\n    match o@move\n        .Some(let v) => ()\n        .None => pair(t@move, t@move)\n", "MovedPlace_Kd t")]
    [InlineData("    let s: string = \"a\"\n    match o@move\n        .Some(let v) => match n\n            .Some(let k) => pair(s@move, s@move)\n            .None => ()\n        .None => ()\n", "MovedPlace_Kd s")]
    [InlineData("    let s: string = \"a\"\n    match n\n        .Some(let k) => match o@move\n            .Some(let v) => pair(s@move, s@move)\n            .None => ()\n        .None => ()\n", "MovedPlace_Kd s")]
    [InlineData("    match o@move\n        .Some(let v) => take(v, v)\n        .None => ()\n", "")]
    [InlineData("    let r: i32 = match o@move\n        .Some(let v) => 1\n        .None => 2\n    use(r)\n", "")]
    public void AMatchBindingWhoseCopyProofIsErrorIsDerived(string body, string expected)
    {
        foreach (var (type, field) in new[] { ("Bad", "s: string = \"\""), ("Good", "n: i32 = 0") })
        {
            var c = Compile($"struct {type}\n    Self is Copy\n    public var {field}\n    public init() => ()\nfunc pair(a: string, b: string) => ()\n" +
                $"func take(a: {type}, b: {type}) => ()\nfunc use(r: i32) => ()\nfunc g(o: Option<{type}>, n: Option<i32>)\n{body}public func main() => ()\n");
            var program = c.BuildMir();
            Assert.Empty(program.Findings);
            Assert.Equal(
                expected.Length == 0 ? [] : [expected],
                program.Problems.All.ToArray().Where(static x => !x.IsRecord && x.Subject.Module == 0).Select(x => $"{x.Code} {c.SyntaxOf(x.Subject)}"));
            var g = program.Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn } && x.Key.Decl?.Name == "g");
            var statements = g.Statements.ToArray();
            var v = Array.FindIndex(g.Locals.ToArray(), static x => x.Name == "v");
            if (v < 0)
            {
                // `take(v, v)` over Bad fails its arm at bind time (Binding's dependent TransferRequired_Kd), so that match is an Error
                // node, replaced (PLAN G121).
                Assert.Equal("Bad", type);
                Assert.Contains(c.Binding.Issues, static x => x.Code == DiagnosticCode.TransferRequired_Kd);
                continue;
            }

            var write = Assert.Single(statements, x => x is { Kind: MirStatementKind.Assign, Rvalue.Kind: MirRvalueKind.Use } && x.Place == new MirPlace(v));
            var source = g.Operands[write.Rvalue.Start].Place;
            Assert.Equal(type == "Bad", statements.Any(x => x is { Kind: MirStatementKind.Assign, Rvalue.Kind: MirRvalueKind.Opaque } && x.Place == new MirPlace(source.Root)));
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
        Assert.DoesNotContain(program.Records(MirRecordClass.Construction), static x => x.Subject.Module == 0);
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
        Assert.DoesNotContain(program.Records(MirRecordClass.Construction), static x => x.Code == DiagnosticCode.InternalInvariant_Kd);
    }

    // DIAGNOSTICS 4.3, DECISIONS R5-M186: a require's row names the nodes Binding guessed its failure body's completion rests on: a body
    // the parser supplied for a missing one, the failure body or one inside it, and an unresolved call; a failure body written in source,
    // with a written branch or a resolved call, names none. MirAnalysisTest's A3 row reads the same programs. Fail-before: no row named
    // a site.
    public static TheoryData<string, string> GuessedFailureBodies => new()
    {
        { "let a = 1\nrequire a == 1 else\nlet b = 2\n", "body" },
        { "let a = 1\nrequire a == 1 else\n    if a == 2\n    else => $abort(\"x\")\n", "CodeBlockKoto" },
        { "let a = 1\nrequire a == 1 else => missing()\n", "body" },
        { "let a = 1\nrequire a == 1 else => ()\n", string.Empty },
        { "let a = 1\nrequire a == 1 else\n    if a == 2 => ()\n    else => $abort(\"x\")\n", string.Empty },
        { "let a = 1\nrequire a == 1 else => found()\nfunc found() => ()\n", string.Empty },
    };

    [Theory]
    [MemberData(nameof(GuessedFailureBodies))]
    public void ARequireRowNamesItsGuessedSites(string source, string sites)
    {
        var c = MinimalEmissionTest.Analyze(source);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var (body, row) = Assert.Single(program.Bodies.ToArray().Where(static x => x.Source.Module == 0)
            .SelectMany(static x => x.MustNotComplete.ToArray().Select(row => (x, row))));
        Assert.Equal(sites, Sites(c, row.Subject, body.GuessedSites.Slice(row.Start, row.Count).ToArray()));
    }

    // The sites a require's row or problem names: "body" for its failure body, else a site's syntax kind and first line.
    internal static string Sites(Compilation c, SourceRef subject, IEnumerable<SourceRef> sites)
        => string.Join(", ", sites.Select(x => x == subject ? "body" : $"{c.SyntaxOf(x).GetType().Name} {c.SyntaxOf(x).ToString().Split('\n')[0].Trim()}".Trim()));

    // A body's Opaque values in order: their destination's Type and root, and the roots their sites name.
    private static (string? Type, int Root, int[] Roots)[] Opaques(MirBody body) => [.. body.Statements.ToArray()
        .Where(static x => x.Kind == MirStatementKind.Assign && x.Rvalue.Kind == MirRvalueKind.Opaque)
        .Select(x => (body.TypeOf(x.Place) is { } type ? TyCtxt.Name(type) : null, x.Place.Root, body.OpaqueUses.Slice(body.OpaqueSites[x.Rvalue.Arg].Start, body.OpaqueSites[x.Rvalue.Arg].Count).ToArray().Select(static y => y.Root).ToArray()))];
}
