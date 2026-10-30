// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Verification;
using Xunit;

namespace XunitTest;

public class NeverContinuationTest
{
    private const string Stop = "func stop() -> Never => $abort(\"stop\")\n";

    private const string Joins = "struct P\n    public var x: i64 = 5\nfunc g(t: (i64, i64)) => ()\nfunc id<T>(value: T) -> T => value@move\n" +
        "func f(c: bool, d: bool, a: i64, p: P) -> i64\n    ";

    // Owned fixed arrays whose element a diverging key selects, and the start of their function f.
    private const string Elements = "struct S\n    public var n: i32 = 0\n    public init() => ()\n    public func inc(self: uniq/Self) => self.n += 1\n" +
        "struct Counter\n    Self is Addable<Counter>\n    associate Output is Counter\n    public let value: i32\n    public init(value: i32) => self.value = value\n" +
        "    public func added(self: ref/Self, right: ref/Counter) -> Counter => Counter.init(self.value + right.value)\n    drop => ()\n" +
        "func make() -> i32 => 1\nfunc see(v: ref/string) => ()\nfunc bump(s: uniq/string) => ()\nfunc take(s: string) => ()\n" +
        "func make2(k: i32) -> (string, i32) => (\"m\", k)\n" +
        "func f() -> i32\n    var a: [1 of string] = [\"old\"]\n    var b: [1 of S] = [S.init()]\n    var other = \"other\"\n    let n: [1 of [1 of i32]] = [[1]]\n" +
        "    let t: [1 of (i32, bool)] = [(1, true)]\n    var u: [1 of Counter] = [Counter.init(1)]\n    ";

    // The program of JoinedLiteralsRunAtTheirJoinsType.
    private const string JoinedLiterals =
        "struct P\n    public var x: i64 = 5\n" +
        "func tuple(c: bool, a: i64) -> i64\n    let t = if c => (a, a) else => (a, (return 7))\n    return t.0 + t.1\n" +
        "func nested(c: bool, d: bool, a: i64) -> i64\n    let t = if c => (a, a) else => (if d => (a, (return 8)) else => (a, (return 9)))\n    return t.1\n" +
        "func array(c: bool, a: i64) -> i64\n    let x = if c => [(a, a)] else => [(a, a), (a, (return 6))]\n    return x[0].0\n" +
        "func dictionary(c: bool, a: i64) -> i64\n    let x = if c => [\"k\": (a, a)] else => [\"k\": (a, a), \"j\": (a, (return 4))]\n    return x[\"k\"].1\n" +
        "func entries(c: bool, a: i64) -> i64\n    let x = if c => [\"k\": (a, a)] else => [\"k\": (a, (return 3))]\n    return x[\"k\"].1\n" +
        "func values(c: bool, p: P) -> i64\n    let x = if c => [\"k\": p.x] else => [\"k\": 4000000000]\n    return x[\"k\"]\n" +
        "func grouped(c: bool, a: i64) -> i64\n    let t = if c => (a, a) else => ((a, (return 5)))\n    return t.1\n" +
        "func literal(c: bool, p: P) -> i64\n    let t = if c => p.x else => 4000000000\n    return t\n" +
        "func leaf(c: bool, a: i64) -> i64\n    let t = if c => (a, a) else => (a, 3000000000)\n    return t.1\n" +
        "func elements(c: bool, p: P) -> i64\n    let x = if c => [p.x] else => [1, 4000000000]\n    return x[1]\n" +
        "func deep(c: bool, d: bool, p: P) -> i64\n    let t = if c => p.x else => (if d => 4000000000 else => 3 * 1000000000)\n    return t\n" +
        "require tuple(true, 2) == 4 and tuple(false, 2) == 7 else => $abort(\"tuple\")\n" +
        "require nested(true, true, 3) == 3 and nested(false, true, 3) == 8 and nested(false, false, 3) == 9 else => $abort(\"nested\")\n" +
        "require array(true, 2) == 2 and array(false, 2) == 6 and dictionary(true, 2) == 2 and dictionary(false, 2) == 4 else => $abort(\"array\")\n" +
        "require grouped(true, 2) == 2 and grouped(false, 2) == 5 else => $abort(\"grouped\")\n" +
        "require entries(true, 2) == 2 and entries(false, 2) == 3 and values(false, P.init()) == 4000000000 else => $abort(\"entries\")\n" +
        "require literal(true, P.init()) == 5 and literal(false, P.init()) == 4000000000 and leaf(false, 2) == 3000000000 else => $abort(\"literal\")\n" +
        "require elements(false, P.init()) == 4000000000 and deep(false, true, P.init()) == 4000000000 and deep(false, false, P.init()) == 3000000000 else => $abort(\"deep\")\n" +
        "Console.writeLine(\"ok\")";

    [Theory]
    [InlineData("Nested", "var n: i32 = do => do => loop => continue\nConsole.writeLine(\"after\")")]
    [InlineData("Default", "func f(y: i32 = (do => loop => continue)) -> i32 => y\nvar n: i32 = f()\nConsole.writeLine(\"after\")")]
    public void TransparentDivergentScopesHaveNoRuntimeSuccessor(string name, string source)
        => ScalarEmissionTest.EmitFixture(
            "NeverWrapper" + Configuration + name,
            "Console.writeLine(\"begin\")\n" + source,
            "begin\n",
            observationMilliseconds: 200);

    [Theory]
    [InlineData("stop() + 1", "i32")]
    [InlineData("stop() * wide", "i64")]
    [InlineData("stop() == 1", "bool")]
    [InlineData("stop() < wide", "bool")]
    public void ANonCompletingLeftOperandFitsTheOtherOperand(string expression, string type)
    {
        // SPEC 3.1.3, 13.3.1, 13.4: Never fits the other operand's Type; it imposes no expected Never on that operand.
        var c = MinimalEmissionTest.Analyze(Stop + "let wide: i64 = 2\nlet n: " + type + " = " + expression);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
    }

    // SPEC 3.8 and 12.3.1: Never supplies no numeric Type; a legitimate counterpart or literal default supplies it.
    [Theory]
    [InlineData("let n = stop() & 1")]
    [InlineData("let n = stop() % 1")]
    [InlineData("let n = 1 & stop()")]
    [InlineData("let n = 1 % stop()")]
    [InlineData("let wide: i64 = 2\nlet n = wide & stop()")]
    [InlineData("let wide: i64 = 2\nlet n = wide % stop()")]
    [InlineData("let n = 1.5 + stop()")]
    [InlineData("let n = (1 + 2) & stop()")]
    [InlineData("let n = stop() < stop()")]
    [InlineData("let n = stop() and true")]
    [InlineData("let n = true and stop()")]
    [InlineData("let n = 1 << stop()")]
    [InlineData("let p: raw/i32 = null\nunsafe\n    let n = p + stop()")]
    public void TheOperatorTypeComesFromTheCompletingOperand(string source)
    {
        var c = MinimalEmissionTest.Analyze(Stop + source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Empty(c.Binding.Issues);
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
    }

    [Theory]
    [InlineData("func f() -> i32\n    +(return 1)", DiagnosticCode.OperatorNotDefined_Kd)]
    [InlineData("func f() -> Never => +stop()", DiagnosticCode.OperatorNotDefined_Kd)]
    [InlineData("func f() -> i32\n    -(return 1)", DiagnosticCode.OperatorNotDefined_Kd)]
    [InlineData("func f() -> i32\n    (return 1) << 1", DiagnosticCode.NonIntegerOperand_Kd)]
    [InlineData("func f() -> i32\n    (return 1) >> 1", DiagnosticCode.NonIntegerOperand_Kd)]
    [InlineData("func f(x: Never)\n    var y = x\n    y++", DiagnosticCode.NonIntegerOperand_Kd)]
    [InlineData("func f(x: Never)\n    var y = x\n    ++y", DiagnosticCode.NonIntegerOperand_Kd)]
    [InlineData("func f(x: Never)\n    var y = x\n    y--", DiagnosticCode.NonIntegerOperand_Kd)]
    [InlineData("func f(x: Never)\n    var y = x\n    --y", DiagnosticCode.NonIntegerOperand_Kd)]
    [InlineData("func f(x: Never)\n    var y = x\n    y <<= 1", DiagnosticCode.NonIntegerOperand_Kd)]
    [InlineData("func f(x: Never)\n    var y = x\n    y &= 1", DiagnosticCode.NonIntegerOperand_Kd)]
    [InlineData("func f() -> i32\n    (return 42)[0] = 1\n    return 0", DiagnosticCode.InvalidAssignment_Kd)] // A Never receiver has no element.
    [InlineData("func f() -> i32\n    let ia: [1 of i32] = [1]\n    let x: bool = ia[(return 42)]\n    return 0", DiagnosticCode.TypeMismatch_Kd)] // R5-M159.
    [InlineData("let n = stop() & stop()", DiagnosticCode.NonIntegerOperand_Kd)]
    [InlineData("let n = stop() + stop()", DiagnosticCode.ArithmeticSelection_Kd)]
    [InlineData("let n = stop() & true", DiagnosticCode.NonNumericOperand_Kd)]
    [InlineData("let n = stop() & missing", DiagnosticCode.UnresolvedBinding_Kd)]
    [InlineData("let n = 2147483648 & stop()", DiagnosticCode.InvalidNumericLiteral_Kd)]
    [InlineData("let n = 1.5 & stop()", DiagnosticCode.NonIntegerOperand_Kd)]
    [InlineData("let p: raw/i32 = null\nunsafe\n    let n = stop() + p", DiagnosticCode.TypeMismatch_Kd)]
    public void NeverDoesNotProveAnOtherwiseInvalidOperator(string source, DiagnosticCode code)
    {
        var c = MinimalEmissionTest.Analyze(Stop + source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Equal(code, Assert.Single(c.Binding.Issues).Code);
        using var output = new StringWriter();
        Assert.False(c.Generate(output));
        Assert.Empty(output.ToString());
    }

    // SPEC 3.8: a Never element of a Tuple literal fits its expected element Type, which the literal's Type takes, so the literal fits its
    // expected Type, also as an enum Case's payload.
    [Theory]
    [InlineData("func pair(x: (i32, i32)) => ()\nfunc f() -> i32\n    pair((1, (return 1)))\n    return 0\n")]
    [InlineData("func opt(x: Option<(i32, i32)>) => ()\nfunc f() -> i32\n    opt(.Some((1, (return 1))))\n    return 0\n")]
    [InlineData("func f() -> i32\n    let t: (i32, i32) = (1, (return 1))\n    return 0\n")]
    [InlineData("func f() -> i32\n    let o: Option<(i32, i32)> = .Some((1, (return 1)))\n    return 0\n")]
    public void ANeverElementTakesItsExpectedType(string source)
    {
        var c = MirBuildTest.Compile(source);
        Assert.Empty(c.Binding.Issues);
        Assert.Equal("(i32, i32)", TyCtxt.Name((BoundType)HirTables.Meaning(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<TupleLiteralKoto>().Single())!));
    }

    // SPEC 3.1.3, 4.7.1 (DECISIONS R5-M159): an element access is no Never form, so an owned fixed-array element whose key diverges has its
    // element Type, as an Array's element has: its Field is written and updated, it is updated by a user operator, it is borrowed for a
    // call, shared or exclusively, it is a `uniq/Self` receiver and an intrinsic's exclusive input (fail-before: UnresolvedBinding_Kd,
    // ArithmeticSelection_Kd, NoApplicableOverload_Kd). Binding accepts `swap`'s, whose later input is the old analysis's first
    // unreachable local use, its located Unsupported_Kd until R6-check (STATUS Unreachable code).
    [Theory]
    [InlineData("b[(return 42)].n = 1")]
    [InlineData("b[(return 42)].n += make()")]
    [InlineData("u[(return 42)] += Counter.init(2)")]
    [InlineData("see(a[(return 42)]@ref)")]
    [InlineData("bump(a[(return 42)]@uniq)")]
    [InlineData("b[(return 42)].inc()")]
    [InlineData("Kimi.Intrinsics.replace(a[(return 42)]@uniq, with: \"new\")")]
    [InlineData("let old = Kimi.Intrinsics.exchange(a[(return 42)]@uniq, with: \"new\")")]
    [InlineData("Kimi.Intrinsics.swap(a[(return 42)]@uniq, other@uniq)", "other")]
    public void AnElementBelowADivergingKeyIsUsedAtItsElementType(string statement, string? unsupported = null)
    {
        var c = MinimalEmissionTest.Analyze(Elements + statement + "\n    return 0\n");
        Assert.True(c.Binding.Result.IsComplete, string.Join('\n', c.Binding.Issues));
        c.ReportAnalysis();
        (string Code, string? Text)[] expected = unsupported is null ? [] : [(nameof(DiagnosticCode.Unsupported_Kd), unsupported)];
        Assert.Equal(expected, TestDiagnostics.Of(c).Where(static x => x.Severity == DiagnosticSeverity.Error).Select(static x => (x.Code, x.Text)));
    }

    // SPEC 14.10.3, 15.1.3, 3.5 (DECISIONS R5-M159): an element consumed where its access cannot complete normally is checked on the
    // assumed path by its reachable twin's Move rules, with the twin's codes at the element: an explicit Move into a local or into a
    // call has no static Move Path, a part of a temporary neither, and a bare consume is TransferRequired_Kd beside the element path's
    // Unsupported_Kd (fail-before: each accepted, the old analysis checking nothing there). A Copy element, a Tuple element's or an
    // element's own, is read as its twin is, with no record.
    [Theory]
    [InlineData("let s = a[(return 42)]@move", "a[(return 42)]", "StaticMovePathRequired_Kd")]
    [InlineData("take(a[(return 42)]@move)", "a[(return 42)]", "StaticMovePathRequired_Kd")]
    [InlineData("take(make2((return 42)).0@move)", "make2((return 42)).0", "StaticMovePathRequired_Kd")]
    [InlineData("let x = a[(return 42)]", "a[(return 42)]", "TransferRequired_Kd Unsupported_Kd")]
    [InlineData("let k = t[(return 42)].1", "t[(return 42)].1", "")]
    [InlineData("let c = n[0][(return 42)]", "n[0][(return 42)]", "")]
    public void AMoveBelowADivergingKeyIsCheckedAsItsReachableTwin(string statement, string element, string codes)
    {
        var c = MinimalEmissionTest.Analyze(Elements + statement + "\n    return 0\n");
        Assert.True(c.Binding.Result.IsComplete, string.Join('\n', c.Binding.Issues));
        c.ReportAnalysis();
        Assert.Equal(
            codes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => (x, (string?)element)),
            TestDiagnostics.Of(c).Where(static x => x.Severity == DiagnosticSeverity.Error).Select(static x => (x.Code, x.Text)));
    }

    // SPEC 3.1.3, 4.7.1 (DECISIONS R5-M159): the HIR publishes that element Type for the element, for an element and a Tuple element
    // below it and for the local it initializes, which MIR builds with no record (fail-before: Never, and a Type finding at the local).
    [Theory]
    [InlineData("let x = b[(return 42)].n", "b[(return 42)]", "S")]
    [InlineData("let x = n[0][(return 42)]", "n[0][(return 42)]", "i32")]
    [InlineData("let x = n[(return 42)][0]", "n[(return 42)][0]", "i32")]
    [InlineData("let x = t[(return 42)].0", "t[(return 42)].0", "i32")]
    public void AnElementBelowADivergingKeyHasItsElementType(string statement, string element, string type)
    {
        var c = MirBuildTest.Compile(Elements + statement + "\n    return 0\n");
        Assert.Empty(c.Binding.Issues);
        Assert.Equal(type, TyCtxt.Name((BoundType)HirTables.Meaning(KotoTree.Walk(c.Kotonoha.RootKoto).Single(x => x.ToString() == element))!));
        var f = c.BuildMir().Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn, Decl.Name: "f" });
        Assert.Equal("i32", TyCtxt.Name(f.Locals.ToArray().Single(static x => x.Name == "x").Type));
        BuildsWithoutRecords(c);
    }

    // SPEC 14.9.2 step 3, 3.8: a literal whose element is Never awaits its position: beside a source that supplies a candidate it supplies
    // none, and it takes the Target Result Type once the join selects it, nested, in parentheses, as an array element, a dictionary value,
    // a part of a Tuple, array or dictionary literal source and a call argument too, which its published Type shows; where no source
    // supplies a candidate, the Type is formed part by part over the sources other than Never (step 2; DECISIONS R4-M134, BU-43): Tuples
    // by element, arrays as Array<T>, fills at their length and Dictionaries by key and value, through nested selections, `do` and `loop`;
    // MIR builds it at that Type with no record and no finding.
    [Theory]
    [InlineData("let t = if c => (a, (return 1)) else => ((return 2), a)", "((return 2), a)")]
    [InlineData("let t = match c\n        true => (a, (return 1))\n        false => ((return 2), a)", "((return 2), a)")]
    [InlineData("let t = if c => (if d => (a, (return 1)) else => ((return 2), a)) else => (a, a)", "((return 2), a)")]
    [InlineData("let t = if c => (if d => (a, (return 1)) else => ((return 2), a)) else => ((return 3), a)", "((return 3), a)")]
    [InlineData("let t = if c => (a, (return 1)) else if d => ((return 2), a) else => ((return 3), (return 4))", "((return 2), a)")]
    [InlineData("let t = if c => (do => (a, (return 1))) else => ((return 2), a)", "(a, (return 1))")]
    [InlineData("let t = loop\n        if c => exit (a, (return 1))\n        exit ((return 2), a)", "((return 2), a)")]
    [InlineData("let g = func [a] (e: bool) => if e => (a, $abort(\"x\")) else => ($abort(\"y\"), a)", "($abort(\"y\"), a)")]
    [InlineData("let t = if c => ((a, (return 1)), a) else => (((return 2), a), a)", "((return 2), a)")]
    [InlineData("let t = if c => [(a, (return 1))] else => [((return 2), a)]", "[((return 2), a)]", "Array<(i64, i64)>")]
    [InlineData("let t = if c => [2 of (a, (return 1))] else => [2 of ((return 2), a)]", "[2 of ((return 2), a)]", "[2 of (i64, i64)]")]
    [InlineData("let t = if c => [\"k\": (a, (return 1))] else => [\"k\": ((return 2), a)]", "[\"k\": ((return 2), a)]", "Dictionary<string, (i64, i64)>")]
    [InlineData("let t = if c => (a, (return 1)) else => (a, (return 2))", "(a, (return 2))", "(i64, Never)")]
    [InlineData("let r = a@ref\n    let t = if c => (r, (return 1)) else => (a, (return 2))", "(r, (return 1))", "(i64, Never)")]
    [InlineData("let t: (() -> i64, i64) = if c => (func [a] () => a, (return 1)) else => ((return 2), a)", "((return 2), a)", "(() -> i64, i64)")]
    [InlineData("let t = if c => (a, a) else => (a, (return 1))", "(a, (return 1))")]
    [InlineData("let t = if c => (a, (return 1)) else => (a, a)", "(a, (return 1))")]
    [InlineData("let t = match c\n        true => (a, a)\n        false => (a, (return 1))", "(a, (return 1))")]
    [InlineData("let t = if c => (a, a) else => (if d => (a, (return 1)) else => (a, (return 2)))", "(a, (return 2))")]
    [InlineData("let t = loop\n        if c => exit (a, (return 1))\n        exit (a, a)", "(a, (return 1))")]
    [InlineData("let t = if c => (a, a) else => ((a, (return 1)))", "(a, (return 1))")]
    [InlineData("let t = if c => (p.x, p.x) else => (p.x, (return 1))", "(p.x, (return 1))")]
    [InlineData("let x = [(a, a), (a, (return 1))]", "(a, (return 1))")]
    [InlineData("let x = [\"k\": (a, a), \"j\": (a, (return 1))]", "(a, (return 1))")]
    [InlineData("g(if c => (a, (return 1)) else => (a, (return 2)))", "(a, (return 2))")]
    [InlineData("let r: (i64, i64) = id(if c => (a, (return 1)) else => (a, (return 2)))", "(a, (return 1))")]
    [InlineData("let t = if c => ((a, a), a) else => ((a, (return 1)), a)", "(a, (return 1))")]
    [InlineData("let t = if c => [(a, a)] else => [(a, (return 1))]", "(a, (return 1))")]
    [InlineData("let t = if c => [\"k\": (a, a)] else => [\"k\": (a, (return 1))]", "(a, (return 1))")]
    [InlineData("let t = if c => [\"k\": a] else => [\"k\": (return 1)]", "[\"k\": (return 1)]", "Dictionary<string, i64>")]
    [InlineData("let t = if c => (a, [\"k\": a]) else => (a, [\"k\": (return 1)])", "[\"k\": (return 1)]", "Dictionary<string, i64>")]
    [InlineData("let t = if c => [1, 2] else => [(return 1)]", "[(return 1)]", "Array<i32>")]
    public void ANeverElementLiteralTakesItsJoinsType(string statement, string literal, string type = "(i64, i64)")
    {
        var c = MirBuildTest.Compile(Joins + statement + "\n    return 0\n");
        Assert.Empty(c.Binding.Issues);
        var node = KotoTree.Walk(c.Kotonoha.RootKoto).Single(x => x is TupleLiteralKoto or ArrayLiteralKoto or DictionaryLiteralKoto && x.ToString() == literal);
        Assert.Equal(type, TyCtxt.Name((BoundType)HirTables.Meaning(node)!));
        BuildsWithoutRecords(c);
    }

    // SPEC 14.9.2 step 3: the literals a join types late run at its Type: a Never element diverges where it is evaluated, and a literal
    // source or leaf, nested or an array element, holds a value only its join's Type has.
    [Fact]
    public void JoinedLiteralsRunAtTheirJoinsType()
        => ScalarEmissionTest.EmitFixture("NeverContinuation" + Configuration + "Joins", JoinedLiterals, "ok\n");

    // The counterparts: such a literal takes the Target Result Type only when its parts fit it, an array literal's elements included
    // (SPEC 4.3); otherwise the join is a Type mismatch. An @move operand takes no Type from its position, so it keeps its own (SPEC
    // 12.3.1, 13.5.2; the owner, 2026-10-19).
    [Theory]
    [InlineData("let t = if c => (d, d) else => (a, (return 1))")]
    [InlineData("let t = if c => [(a, a)] else => [(a, (return 1)), (a, true)]")]
    [InlineData("let t = if c => [\"k\": (a, a)] else => [\"k\": (a, (return 1)), \"j\": (a, true)]")]
    [InlineData("let t = if c => (a, a) else => (a, (return 1))@move")]
    [InlineData("let t: (i64, i64) = if c => (a, a) else => (a, (return 1))@move")]
    [InlineData("let t = if c => (a, (return 1)) else => (c, a)")]
    [InlineData("let t = if c => (a, (return 1)) else => ((return 2), a, a)")]
    [InlineData("let t = if c => (a, (return 1)) else => [a, (return 2)]")]
    [InlineData("let t = if c => (if d => (a, (return 1)) else => ((return 2), c)) else => (a, a)")]
    [InlineData("let t = if c => [2 of (a, (return 1))] else => [3 of ((return 2), a)]")]
    [InlineData("let t = if c => [2 of 5] else => [3 of a]")]
    [InlineData("let t = if c => [2 of (a, (return 1))] else => [((return 2), a), ((return 3), a)]")]
    [InlineData("let t = if c => [\"k\": (a, (return 1))] else => [\"k\": (c, (return 2))]")]
    [InlineData("let t = if c => (1, (return 1)) else => (2.5, a)")]
    public void ANeverElementLiteralThatDoesNotFitItsJoinIsAMismatch(string statement)
    {
        var c = MirBuildTest.Compile(Joins + statement + "\n    return 0\n");
        Assert.Contains(c.Binding.Issues, static x => x.Node is IfKoto && x.Failure == BindingFailure.TypeMismatch);
    }

    // SPEC 23.1.2: a conflict in a join formed part by part is one record, at the join whose sources conflict, also when a later use
    // binds its declaration again (the survey of a bound source that awaits supplies no candidate, DECISIONS BU-43), and when a nested
    // join holding a waiting literal conflicts within itself: it fails at once, and its outer join rests on that part's record.
    [Theory]
    [InlineData("let t = if c => (if d => (a, (return 1)) else => ((return 2), c)) else => (a, a)")]
    [InlineData("let t = if c => (if d => (a, (return 1)) else => ((return 2), a)) else => (c, (return 3))")]
    [InlineData("let t = if c => (if d => (a, (return 1)) else => (c, (return 2))) else => (a, a)")]
    [InlineData("let t = if c => (if d => (5, a) else => ((return 1), c)) else => ((return 2), a)")]
    public void AConflictInAJoinFormedPartByPartIsOneRecord(string statement)
    {
        var c = MirBuildTest.Compile(Joins + statement + "\n    return t.0\n");
        var issue = Assert.Single(c.Binding.Issues);
        Assert.Equal(BindingFailure.TypeMismatch, issue.Failure);
    }

    // SPEC 14.9.2 step 3 ("a literal result is not fixed before outer constraints"), 23.1.2 (DECISIONS BU-43): a conflict among literal
    // defaults is the root's, the outermost join of the tree, judged over every position of the flattened tree: one record at the root,
    // also for two inner conflicts (dev: two, one at each inner join) and beside the root's own typed conflict (dev: one, at the inner
    // join); every nested join rests on it.
    [Theory]
    [InlineData("let t = if c => (if d => 5 else => 2.5) else => 2.5")]
    [InlineData("let t = if c => (if d => (if c => 5 else => 2.5) else => 2.5) else => 2.5")]
    [InlineData("let t = if c => (if d => (5, (return 1)) else => (2.5, (return 2))) else => ((return 3), a)")]
    [InlineData("let t = if c => (if d => 5 else => 2.5) else => (return 3)")]
    [InlineData("let t = if c => ((if d => 5 else => 2.5), (if d => 6 else => 3.5)) else => ((return 1), (return 2))")]
    [InlineData("let t = if c => ((if d => 5 else => 2.5), a, (return 1)) else => ((return 2), true, (return 3))")]
    public void ADefaultsConflictIsRecordedOnceAtTheOutermostJoin(string statement)
    {
        var c = MirBuildTest.Compile(Joins + statement + "\n    return 0\n");
        var issue = Assert.Single(c.Binding.Issues);
        Assert.Equal(BindingFailure.TypeMismatch, issue.Failure);
        Assert.Same(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<IfKoto>().First(), issue.Node);
    }

    // SPEC 23.1.2, 14.9.2 step 3 (DECISIONS BU-43): a failed part stops no formation, so a defaults conflict among the other parts of
    // the tree is still found and is the root's record, beside the other failure's own record (dev: at the inner join), also when the
    // other failure is a sibling join's own conflict.
    [Theory]
    [InlineData("let t = if c => (if d => 5 else => 2.5) else => nope")]
    [InlineData("let t = if c => (if d => 5 else => 2.5) else => missing()")]
    [InlineData("let t = if c => (if d => 5 else => 2.5) else => id(nope)")]
    [InlineData("let t = if c => nope else => (if d => 5 else => 2.5)")]
    [InlineData("let t = if c => ((if d => 5 else => 2.5), (return 1)) else => (nope, (return 2))")]
    [InlineData("let t = if c => (if d => (5, (return 1)) else => (2.5, (return 2))) else => (if d => (a, a) else => (true, a))")]
    [InlineData("let t = if c => [(if d => 5 else => 2.5)] else => [nope]")]
    [InlineData("let t = if c => [\"k\": (if d => 5 else => 2.5)] else => [\"k\": nope]")]
    [InlineData("let t = if c => (if d => (if c => 5 else => 2.5) else => 2.5) else => nope")]
    public void ADefaultsConflictBesideAnotherFailureIsTheRootsRecord(string statement)
    {
        var c = MirBuildTest.Compile(Joins + statement + "\n    return 0\n");
        var root = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<IfKoto>().First();
        Assert.Equal(2, c.Binding.Issues.Count);
        Assert.Single(c.Binding.Issues, x => x.Failure == BindingFailure.TypeMismatch && ReferenceEquals(x.Node, root));
    }

    // SPEC 23.1.2 (DECISIONS BU-43): an unreadable character literal is bound as `char`, its lexer record standing for it, so a join
    // forms its Type over it and a later use keeps its own record.
    [Fact]
    public void AnUnreadableLiteralIsATypedPartOfItsJoin()
    {
        const string Source = "func f(c: bool, a: i64) -> i64\n    let t = if c => ('ab', (return 1)) else => ((return 2), 'x')\n    let z: i64 = t.0\n    return 0\n" +
            "public func main() => ()\n";
        var codes = DiagnosticCorpus.Check(Source).Diagnostics.Select(static x => x.Code).Order();
        Assert.Equal([nameof(DiagnosticCode.InvalidCharLiteral_Kd), nameof(DiagnosticCode.TypeMismatch_Kd)], codes);
    }

    // DECISIONS BU-9 r1, BU-43: a failed part is no waiting part, so no Type is formed over it and no literal is bound twice; its join
    // rests on it, and so does each source of that join that holds no literal: the program keeps the failed part's one record.
    [Theory]
    [InlineData("let t = if c => ([], (return 1)) else => ((return 2), [a])")]
    [InlineData("let t = if c => [] else => [(return 1)]")]
    public void AFailedPartIsNoWaitingPart(string statement)
    {
        var source = "func f(c: bool, a: i64) -> i64\n    " + statement + "\n    return 0\npublic func main() => ()\n";
        var diagnostic = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.Unsupported_Kd), diagnostic.Code);
    }

    // SPEC 3.8: Never is a subtype of no other Type, at any depth: a Type holding Never relates only by identity, a Function Type's result
    // and input included (the section's own example), and through a Type argument in either direction; Never fitting is the judgment of
    // a Never expression alone.
    [Theory]
    [InlineData("struct Sink<T>\n    public var f: (T) -> ()\nfunc g(s: Sink<i32>)\n    let t: Sink<Never> = s@move\n")]
    [InlineData("struct Sink<T>\n    public var f: (T) -> ()\nfunc g(s: Option<Sink<i32>>)\n    let t: Option<Sink<Never>> = s@move\n")]
    [InlineData("func f(handler: (i32) -> Never) -> ()\n    let g: (i32) -> i32 = handler@move\n")]
    [InlineData("func f(handler: (i32) -> ()) -> ()\n    let g: (Never) -> () = handler@move\n")]
    [InlineData("func f(a: i64) -> i64\n    let u = (a, (return 1))\n    let t: (i64, i64) = u\n    return 0\n")]
    [InlineData("func take(t: (i64, i64)) => ()\nfunc f(a: i64, u: (i64, Never))\n    take(u)\n")]
    public void ATypeHoldingNeverRelatesOnlyByIdentity(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Contains(c.Binding.Issues, static x => x.Failure is BindingFailure.TypeMismatch or BindingFailure.NoApplicableCandidate);
    }

    // SPEC 3.8, 10.7: Never fitting at a value position, and 10.7's result compatibility (R <: Q or R is Never) at an implementation's
    // result: a Function Item and a contract implementation whose result is Never.
    [Theory]
    [InlineData("func stop() -> Never => $abort(\"stop\")\nfunc f()\n    let x: i64 = stop()\n    let t: (i64, i64) = (1, stop())\n")]
    [InlineData("func h(x: i32) -> Never => $abort(\"x\")\nlet g: (i32) -> i32 = h\n")]
    [InlineData("contract Empty\n    func empty() -> Self\nstruct Impossible\n    Self is Empty\n    public func empty() -> Never => $abort(\"no value\")\n")]
    public void NeverFittingIsAValueAndResultJudgment(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
    }

    // SPEC 10.4 step 2: parameter Types rank no candidates, Never's included: `f(x: Never)` and `f(x: i32)` both take `stop()`, by
    // Never fitting, and are incomparable.
    [Fact]
    public void ANeverParameterRanksNoCandidate()
    {
        var c = MinimalEmissionTest.Analyze(Stop + "func f(x: Never) -> i32 => 1\nfunc f(x: i32) -> i32 => 2\nlet r = f(stop())\n");
        Assert.Contains(c.Binding.Issues, static x => x.Failure == BindingFailure.Ambiguous);
    }

    // SPEC 14.9.2 step 3: a literal holding Never that a join types late fits a Target Result Type fixed by an outer expectation through
    // its late Type, not by Never fitting, also in parentheses and when a nested join waited for it.
    [Theory]
    [InlineData("let t: (i64, i64) = if c => (a, a) else => ((a, (return 1)))", "(a, (return 1))")]
    [InlineData("let t: (i64, i64) = if c => (a, a) else => (if d => (a, (return 1)) else => (a, 4000000000))", "(a, (return 1))")]
    [InlineData("let t = if c => (a, a) else => (if d => (a, (return 1)) else => (a, 4000000000))", "(a, (return 1))")]
    [InlineData("let t: (i64, i64) = if c => (a, a) else => (a, (return 1))", "(a, (return 1))")]
    public void ANeverElementLiteralFitsAnExpectedJoinByItsLateType(string statement, string literal)
    {
        var c = MirBuildTest.Compile(Joins + statement + "\n    return 0\n");
        Assert.Empty(c.Binding.Issues);
        var tuple = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<TupleLiteralKoto>().Single(x => x.ToString() == literal);
        Assert.Equal("(i64, i64)", TyCtxt.Name((BoundType)HirTables.Meaning(tuple)!));
        BuildsWithoutRecords(c);
    }

    // SPEC 3.1.3: Never is Copy and Owned, so a Type storing it is Copy and Owned by its structure, and a bare read of a Place of such
    // a Type is a Copy. The old path's Unsupported_Kd for every Place of a Type holding Never stays its only record until R6-check.
    [Theory]
    [InlineData("func f()\n    var t = (1, stop())\n", "t", 0)]
    [InlineData("func f()\n    let a: Option<Never> = .None\n", "a", 0)]
    [InlineData("func f(a: Option<Never>) => ()\n", "a", 0)]
    [InlineData("func f()\n    let a: Option<Never> = .None\n    let b = a\n    let c = a\n", "a", 2)]
    [InlineData("func g(a: Never, b: Never) => ()\nfunc f(x: Never) => g(x, x)\n", "x", 2)]
    public void APlaceHoldingNeverIsReadByCopy(string source, string place, int copies)
    {
        var c = MinimalEmissionTest.Analyze(Stop + source);
        Assert.Empty(c.Binding.Issues);
        Assert.All(LegacyRecords.Of(c).Select(static x => x.Code).Concat(c.Problems.Select(static x => x.Code)), static x => Assert.Equal(DiagnosticCode.Unsupported_Kd, x));
        HirTables.Publish(c);
        var reads = KotoTree.Walk(c.Kotonoha.RootKoto).Where(x => x.ToString() == place && HirTables.AdjustOf(x).Acquire != HirAcquire.Unset).ToArray();
        Assert.Equal(copies, reads.Length);
        Assert.All(reads, static x => Assert.Equal(HirAcquire.Copy, HirTables.AdjustOf(x).Acquire));
    }

    // SPEC 14.9.3, 14.10.3, 13.5.3: an explicit acquisition of a Place of Type Never supplies no normal completion and keeps its effect:
    // `x@move` Moves x, and the later bare read is a Copy checked on the assumed path; `x@copy` and `x@Never` stay an Identity.
    [Theory]
    [InlineData("x@move", true)]
    [InlineData("x@copy", false)]
    [InlineData("x@Never", false)]
    public void AnExplicitAcquisitionOfANeverPlaceKeepsItsOperation(string acquisition, bool moves)
    {
        var operand = moves ? HirAcquire.Move : HirAcquire.Copy;
        var c = MinimalEmissionTest.Analyze("func pass(x: Never, y: Never) => ()\nfunc absurd(x: Never) => pass(" + acquisition + ", x)\n");
        Assert.Empty(c.Binding.Issues);
        Assert.Equal(moves, LegacyRecords.Of(c).Any(static x => x.Code == DiagnosticCode.MovedPlace_Kd));
        HirTables.Publish(c);
        var conversion = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<ConversionKoto>().Single();
        Assert.Equal((HirOp.Group, HirCompletion.Diverges), (HirTables.Op(conversion), HirTables.CompletionOf(conversion)));
        Assert.Equal(operand, HirTables.AdjustOf(conversion.Left).Acquire);
        var read = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto), x => x.ToString() == "x" && !ReferenceEquals(x, conversion.Left) && HirTables.AdjustOf(x).Acquire != HirAcquire.Unset);
        Assert.Equal(HirAcquire.Copy, HirTables.AdjustOf(read).Acquire);
    }

    // SPEC 14.9.3, 14.10.3, 13.5.6: an explicit borrow of a Place of Type Never forms its Loan like any explicit borrow and supplies no
    // normal completion, so a later Move of the Place is a Loan conflict on the assumed path.
    [Theory]
    [InlineData("func pass(a: ref/Never, b: Never) => ()\nfunc absurd(x: Never) => pass(x@(ref/Never), x@move)\n", "x@(ref/Never)", true)]
    [InlineData("func pass(a: ref/Never, b: Never) => ()\nfunc absurd(x: Never) => pass(x@ref, x@move)\n", "x@ref", true)]
    [InlineData("func pass(a: uniq/Never) => ()\nfunc absurd(x: Never)\n    var y = x\n    pass(y@(uniq/Never))\n", "y@(uniq/Never)", false)]
    [InlineData("func pass(a: uniq/Never) => ()\nfunc absurd(x: Never)\n    var y = x\n    pass(y@uniq)\n", "y@uniq", false)]
    public void AnExplicitBorrowOfANeverPlaceFormsItsLoan(string source, string borrow, bool conflict)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Empty(c.Binding.Issues);
        Assert.Equal(conflict, LegacyRecords.Of(c).Any(static x => x.Code.IsLoanConflict()));
        HirTables.Publish(c);
        var conversion = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<ConversionKoto>().Single(x => x.ToString() == borrow);
        Assert.Equal((HirOp.Borrow, HirCompletion.Diverges), (HirTables.Op(conversion), HirTables.CompletionOf(conversion)));
        var type = conversion.TypeOf()!;
        Assert.Equal((borrow.Contains("uniq", StringComparison.Ordinal) ? SemanticsKind.Uniq : SemanticsKind.Ref, BoundType.Never), (type.Semantics, type.Components[0]));
    }

    // SPEC 14.9.3: a borrow of a Never value, which is no Place, only diverges.
    [Fact]
    public void ABorrowOfANeverValueOnlyDiverges()
    {
        var c = MinimalEmissionTest.Analyze(Stop + "func pass(a: ref/Never) => ()\nfunc f() => pass(stop()@ref)\nfunc g() => pass(stop()@(ref/Never))\n");
        Assert.Empty(c.Binding.Issues);
        HirTables.Publish(c);
        Assert.All(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<ConversionKoto>(), static x => Assert.Equal(HirOp.Convert, HirTables.Op(x)));
    }

    // The counterpart: a Type storing a Non-Copy part beside Never is Non-Copy, so a bare read of its Place is a Move that needs @move
    // (SPEC 3.5).
    [Fact]
    public void APlaceHoldingNeverAndANonCopyPartIsNotCopied()
    {
        var c = MinimalEmissionTest.Analyze("func f(a: Option<(string, Never)>)\n    let b = a\n    let c = a\n");
        Assert.Contains(LegacyRecords.Of(c), static x => x.Code == DiagnosticCode.TransferRequired_Kd);
        HirTables.Publish(c);
        var reads = KotoTree.Walk(c.Kotonoha.RootKoto).Where(static x => x.ToString() == "a" && HirTables.AdjustOf(x).Acquire != HirAcquire.Unset).ToArray();
        Assert.Equal(2, reads.Length);
        Assert.All(reads, static x => Assert.Equal(HirAcquire.Move, HirTables.AdjustOf(x).Acquire));
    }

    [Fact]
    public void ANonCompletingLeftOperandSkipsTheOperation()
        => ScalarEmissionTest.EmitFixture("NeverOperand" + Configuration + "Left", Stop + "Console.writeLine(\"begin\")\nlet n: i32 = stop() + 1\nConsole.writeLine(\"after\")", "begin\n", 1, "Hello.kimi:1:25: abort KIMI_E_ABORT: stop\n");

    [Theory]
    [InlineData("BitwiseRight", "1 & stop()")]
    [InlineData("RemainderRight", "1 % stop()")]
    public void ANonCompletingRightOperandSkipsTheOperation(string name, string expression)
        => ScalarEmissionTest.EmitFixture(
            "NeverOperand" + Configuration + name,
            Stop + "Console.writeLine(\"begin\")\nlet n = " + expression + "\nConsole.writeLine(\"after\")",
            "begin\n",
            1,
            "Hello.kimi:1:25: abort KIMI_E_ABORT: stop\n");

    [Fact]
    public void DivergentOperatorSkipsCalleeAndLaterOutput()
        => ScalarEmissionTest.EmitFixture(
            "NeverOperand" + Configuration + "Divergence",
            "Console.writeLine(\"begin\")\nvar n: i32 = 1 << (loop => continue)\nConsole.writeLine(\"after\")",
            "begin\n",
            observationMilliseconds: 200);

    [Fact]
    public void NoncompletingFirstArgumentSkipsLaterAcquisitionsAndCallee()
        => ScalarEmissionTest.EmitFixture(
            "NeverContinuation" + Configuration + "FirstArgument",
            "func spin() -> Never => loop => ()\nfunc f(a: i32, b: i32) => Console.writeLine(\"bad\")\nConsole.writeLine(\"begin\")\nf(spin(), 2)\nConsole.writeLine(\"bad\")",
            "begin\n",
            observationMilliseconds: 200);

#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif

    // MIR builds the program with no record and no validator finding.
    private static void BuildsWithoutRecords(Compilation c)
    {
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.DoesNotContain(program.Records, static x => x.Subject.Module == 0);
    }
}
