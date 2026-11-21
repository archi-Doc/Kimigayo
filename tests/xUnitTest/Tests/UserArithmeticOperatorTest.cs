// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class UserArithmeticOperatorTest
{
    [Theory]
    [InlineData("*", 2)]
    [InlineData("+", 1)]
    public void AnIndependentPointerProhibitionKeepsTheUnresolvedCounterpart(string op, int count)
    {
        var source = $"func f(p: raw/i32) => p {op} missing\npublic func main() => ()";
        var compilation = CompilationTestHelper.ParseSuccess(source);
        Assert.False(compilation.Bind().IsComplete);
        Assert.Equal(count, compilation.Binding.Issues.Count);
        Assert.Single(compilation.Binding.Issues, x => x.Code == DiagnosticCode.UnresolvedBinding_Kd);
        if (count == 2)
        {
            var prohibition = Assert.Single(compilation.Binding.Issues, x => x.Code.ToString() == "OperatorNotDefined_Kd");
            Assert.Equal($"p {op} missing", prohibition.Node.ToString());
        }
    }

    // PLAN G506: a probed operand that bound to nothing and waits for no position or call makes its operator rest at once, so a prefix
    // chain over a failed operand binds each operand once and records only the failure. Fail-before: unary minus bound such an operand
    // again under its expected Type, 2^n binds (dev took about 20 s at 26 operators); `+` and `not` take no probe.
    [Theory]
    [InlineData("- ")]
    [InlineData("+ ")]
    [InlineData("not ")]
    public void APrefixChainOverAFailedOperandBindsEachOperandOnce(string op)
    {
        var source = $"func f(c: i32) -> i32\n    return {string.Concat(Enumerable.Repeat(op, 40))}missing\npublic func main() => ()";
        var compilation = CompilationTestHelper.ParseSuccess(source);
        Assert.False(compilation.Bind().IsComplete);
        Assert.Equal(DiagnosticCode.UnresolvedBinding_Kd, Assert.Single(compilation.Binding.Issues).Code);
    }

    // PLAN G506 (SPEC 12.3, 13.3.4): a unary operand takes the expected Type only when it is literal-only; any other operand is typed
    // once, on its own, and the record is the operand's. Fail-before: dev bound `-make()` and `+make()` under `i64` and accepted them.
    [Theory]
    [InlineData("func make<T>() -> T => $abort(\"x\")\nfunc f() -> i64\n    return -make()", "make()")]
    [InlineData("func make<T>() -> T => $abort(\"x\")\nfunc f() -> i64\n    return +make()", "make()")]
    [InlineData("enum E\n    A\nfunc f() -> E\n    return -.A", ".A")]
    [InlineData("func f() -> Wrapping<u8>\n    return -(1)", null)]
    public void AUnaryOperandTakesTheExpectedTypeOnlyWhenLiteralOnly(string declarations, string? failed)
    {
        var compilation = CompilationTestHelper.ParseSuccess(declarations + "\npublic func main() => ()");
        Assert.Equal(failed is null, compilation.Bind().IsComplete);
        if (failed is not null)
        {
            Assert.Equal(failed, Assert.Single(compilation.Binding.Issues).Node.ToString());
        }
    }

    [Theory]
    [InlineData("bool")]
    [InlineData("()")]
    [InlineData("char")]
    [InlineData("raw/i32")]
    public void ARightOperandCannotDisproveAnUnknownLeftProvider(string type)
    {
        var source = $"func f(value: {type}) => missing + value\npublic func main() => ()";
        var compilation = CompilationTestHelper.ParseSuccess(source);
        Assert.False(compilation.Bind().IsComplete);
        Assert.Equal(DiagnosticCode.UnresolvedBinding_Kd, Assert.Single(compilation.Binding.Issues).Code);
    }

    [Theory]
    [InlineData("bool", "true")]
    [InlineData("()", "()")]
    [InlineData("char", "'a'")]
    public void AnOrdinaryProviderCanTakeANonNumericCounterpart(string type, string value)
    {
        var source = $$"""
            struct Value
                Self is Addable<{{type}}>
                associate Output is i32
                public func added(self: ref/Self, right: ref/{{type}}) -> i32 => 1
            let result = Value.init() + {{value}}
            """;
        var compilation = CompilationTestHelper.ParseSuccess(source);
        Assert.True(compilation.Bind().IsComplete, MinimalEmissionTest.Describe(compilation));
        Assert.Empty(compilation.Binding.Issues);
    }

    [Theory]
    [InlineData("Addable", "added", "+")]
    [InlineData("RemainderProvider", "remainder", "%")]
    public void SelectedProvidersKeepTheirOwnGenericRequirement(string contract, string method, string op)
    {
        var source = $$"""
            struct Value
                Self is {{contract}}<i32>
                associate Output is i32
                public func {{method}}(self: ref/Self, right: ref/i32) -> i32 => 1
            func apply<T>(left: ref/T, right: ref/i32) -> T.({{contract}}<i32>).Output
                T is {{contract}}<i32>
                return left {{op}} right
            let result = Value.init() {{op}} 2
            """;
        var compilation = CompilationTestHelper.ParseSuccess(source);
        Assert.True(compilation.Bind().IsComplete, MinimalEmissionTest.Describe(compilation));
        Assert.Empty(compilation.Binding.Issues);
    }

    private const string Declarations = """
        struct Value
            Self is Addable<i32>
            Self is Subtractable<Value>
            Self is LeftMultipliable<f32>
            Self is Negatable
            associate Addable<i32>.Output is i32
            associate Subtractable<Value>.Output is i32
            associate LeftMultipliable<f32>.Output is f32
            associate Negatable.Output is i32
            public let value: i32 = 40
            public func added(self: ref/Self, right: ref/i32) -> i32 => self.value + right
            public func subtracted(self: ref/Self, right: ref/Value) -> i32 => self.value - right.value
            public func multipliedFrom(left: ref/f32, self: ref/Self) -> f32 => left * self.value@f32
            public func negated(self: ref/Self) -> i32 => -self.value
            drop => ()
        """;

    // SPEC 13.3.3's Vec2, whose Left provider's Output differs from both operands.
    private const string Vector = "\nstruct Vec2\n    Self is LeftMultipliable<f64>\n    associate LeftMultipliable<f64>.Output is Vec2\n    public let x: f64\n    public let y: f64\n" +
        "    public init(x: f64, y: f64)\n        self.x = x\n        self.y = y\n    public func multipliedFrom(left: ref/f64, self: ref/Self) -> Vec2\n        return Vec2.init(left * self.x, left * self.y)\n";

    [Fact]
    public void SharedNonCopyProvidersKeepDistinctInputsAndOutputs()
        => ScalarEmissionTest.EmitFixture("UserArithmeticShared", Declarations + "\nlet value = Value.init()\nrequire value + 2 == 42 and value - value == 0 and -value == -40 else => $abort(\"ordinary\")\nrequire 2.0 * value == 80.0 and (1.0 + 1.0) * value == 80.0 else => $abort(\"left\")\nrequire value.value == 40 else => $abort(\"not consumed\")", string.Empty);

    // SPEC 13.3.4, 14.9.2: a result source routed to a user provider has its conformance's Output, not an operand's Type, so a join
    // (also an anonymous function's single-item body) takes no evidence from its operands; a built-in route's operands (literal-only or
    // ending in a numeric Type) still supply it.
    [Theory]
    [InlineData("let g = func [] (x: Value, y: Value)\n    return x - y\nlet result = g(Value.init(), Value.init())", "i32")]
    [InlineData("func pick(c: bool, x: Value) -> i32\n    let result = if c => x - x else => -x\n    return result", "i32")]
    [InlineData("let k = func [] (s: f64, v: Vec2)\n    return s * v\nlet result = k(2.0, Vec2.init(1.0, 2.0))", "Vec2")]
    [InlineData("let f = func [] (x: Value) => x + 2\nlet result = f(Value.init())", "i32")]
    [InlineData("let g = func [] (x: Value, y: Value) => x - y\nlet result = g(Value.init(), Value.init())", "i32")]
    [InlineData("let h = func [] (x: Value) => -x\nlet result = h(Value.init())", "i32")]
    [InlineData("let k = func [] (s: f64, v: Vec2) => s * v\nlet result = k(2.0, Vec2.init(1.0, 2.0))", "Vec2")]
    [InlineData("func pick(c: bool, a: i64) -> i64\n    let result = if c => a + 1 else => 2\n    return result", "i64")]
    [InlineData("func pick(c: bool, a: i64) -> i64\n    let result = if c => -a else => 5000000000\n    return result", "i64")]
    [InlineData("func pick(c: bool, r: ref/i64) -> i64\n    let result = if c => r * 2 else => 5000000000\n    return result", "i64")]
    [InlineData("func pick(c: bool, a: i64) -> i64\n    let result = if c => 1 + 2 else => a\n    return result", "i64")]
    public void AJoinTakesAnOperatorResultFromItsRoute(string source, string type)
    {
        var c = MinimalEmissionTest.Analyze(Declarations + Vector + source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var result = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<VariableKoto>().Single(x => x.NameKoto.IdentifierName == "result");
        Assert.Equal(type, result.SymbolOf()!.Type!.Name);
    }

    [Fact]
    public void JoinedUserRoutesExecuteTheirOutputs()
    {
        const string Program = "let g = func [] (x: Value, y: Value)\n    return x - y\nfunc pick(c: bool, x: Value) -> i32\n    let result = if c => x - x else => -x\n    return result\n" +
            "let k = func [] (s: f64, v: Vec2)\n    return s * v\nrequire g(Value.init(), Value.init()) == 0 and pick(false, Value.init()) == -40 and pick(true, Value.init()) == 0 else => $abort(\"user\")\n" +
            "require k(2.0, Vec2.init(1.0, 2.0)).y == 4.0 else => $abort(\"left\")\n" +
            "let f = func [] (x: Value) => x + 2\nlet d = func [] (x: Value, y: Value) => x - y\nlet h = func [] (x: Value) => -x\nlet m = func [] (s: f64, v: Vec2) => s * v\n" +
            "require f(Value.init()) == 42 and d(Value.init(), Value.init()) == 0 and h(Value.init()) == -40 and m(2.0, Vec2.init(1.0, 2.0)).y == 4.0 else => $abort(\"expression\")";
        ScalarEmissionTest.EmitFixture("UserArithmeticJoin", Declarations + Vector + Program, string.Empty);
    }

    [Fact]
    public void AnEnumCanProvideAnArithmeticRequirement()
        => ScalarEmissionTest.EmitFixture("UserArithmeticEnum", "enum Marker\n    Self is Negatable\n    associate Output is i32\n    On\n    public func negated(self: ref/Self) -> i32 => -42\nlet value = Marker.On\nrequire -value == -42 else => $abort(\"enum provider\")", string.Empty);

    [Fact]
    public void InheritedArithmeticUsesTheOriginalWitness()
    {
        const string Types = "open struct Base\n    Self is Addable<i32> and LeftSubtractable<i32> and Negatable\n    associate Addable<i32>.Output is i32\n    associate LeftSubtractable<i32>.Output is i32\n    associate Negatable.Output is i32\n    public let value: i32 = 40\n    public init() => ()\n    public func added(self: ref/Self, right: ref/i32) -> i32 => self.value + right\n    public func subtractedFrom(left: ref/i32, self: ref/Self) -> i32 => left - self.value\n    public func negated(self: ref/Self) -> i32 => -self.value\nstruct Derived: Base\n    public init() : base() => ()\nlet value = Derived.init()\n";
        ScalarEmissionTest.EmitFixture("UserArithmeticInherited", Types + "require value.added(2) == 42 and value + 2 == 42 and 82 - value == 42 and -value == -40 else => $abort(\"inherited provider\")", string.Empty);
    }

    [Fact]
    public void PublishedContractsFixGenericOperatorsBeforeInstantiation()
    {
        const string Generic = "\nfunc sum<L, R>(left: ref/L, right: ref/R) -> L.(Addable<R>).Output\n    L is Addable<R>\n    return left + right\nlet value = Value.init()\nrequire sum(value, 2) == 42 and sum(40, 2) == 42 else => $abort(\"generic\")";
        ScalarEmissionTest.EmitFixture("UserArithmeticGeneric", Declarations + Generic, string.Empty);
    }

    [Fact]
    public void KnownReferenceLayersUseTheSameSharedAcquisition()
    {
        const string Program = "\nfunc inspect(value: ref/(ref/Value during b)) -> i32 => value + 2\nlet value = Value.init()\nlet inner = value@ref\nlet outer = inner@ref\nrequire inspect(outer) == 42 else => $abort(\"layers\")";
        ScalarEmissionTest.EmitFixture("UserArithmeticLayers", Declarations + Program, string.Empty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NumericLeftContractBorrowsBeforeEvaluatingTheRight(bool copied)
    {
        var program = "struct Value\n    Self is LeftAddable<i32>\n    associate Output is i32\n    public func addedFrom(left: ref/i32, self: ref/Self) -> i32 => left + 1\nfunc bump(value: uniq/i32) -> Value\n    value@follow += 1\n    return Value.init()\nvar number = 40\nlet result = number" + (copied ? "@copy" : string.Empty) + " + bump(number@uniq)\nrequire result == 41 and number == 41 else => $abort(\"order\")";
        var c = MinimalEmissionTest.Analyze(program);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Equal(copied, c.Analysis.IsVerified);
        if (copied)
        {
            ScalarEmissionTest.EmitFixture("UserArithmeticCopiedLeft", program, string.Empty);
        }
    }

    [Theory]
    [InlineData("2")]
    [InlineData("scale")]
    public void LeftProviderDoesNotInventNumericConversions(string expression)
    {
        var c = MinimalEmissionTest.Analyze(Declarations + "\nlet value = Value.init()\nlet scale = 2.0\nlet result = " + expression + " * value");
        Assert.False(c.Binding.Result.IsComplete);
    }

    [Fact]
    public void PrimitiveIntegerCounterpartCanGuideASharedLiteral()
    {
        const string Program = "func increment<L, R>(left: ref/L) -> L.(Addable<R>).Output\n    L is Addable<R>\n    R is PrimitiveInteger\n    return left + 1\nrequire increment<i32, i32>(41) == 42 else => $abort(\"generic literal\")";
        ScalarEmissionTest.EmitFixture("UserArithmeticGenericLiteral", Program, string.Empty);
    }

    [Fact]
    public void OppositeProviderPremisesConflictWithoutAnOperatorUse()
    {
        const string Program = "func conflict<L, R>(left: ref/L, right: ref/R)\n    L is Addable<R>\n    R is LeftAddable<L>\n    ()\n()";
        var result = DiagnosticCorpus.Check(Program);
        var error = Assert.Single(result.Diagnostics, static x => x.Code == nameof(DiagnosticCode.ArithmeticDirection_Kd));
        Assert.Contains("opposite providers", error.Message, StringComparison.Ordinal);
        Assert.Equal(new[] { "ordinary", "reverse" }, error.Related!.Select(static x => x.Role));
    }

    // x41n's contracts (design-x41n §2, §6; each valid alone). X41: `L is Z` supplies `L.(Z).E is W` (depth 1), W's Addable premise on
    // D (depth 2) and, through `associate C is K1`, K1's Left premise on C.A (depth 3). X41m (design-x41n §6.3, no user generic Contract):
    // the Left premise on the identity's other side, at depth 3. X41i: an identity supplied only at depth 2 (Zi's clause). Y1: the
    // review's two-scope twins; Y3: its y3n family.
    private const string X41 = "contract K1\n    associate A\n    Self.(K1).A is LeftAddable<Self>\ncontract W\n    associate C is K1\n    associate D\n    Self.(W).D is Addable<Self.(W).C.(K1).A>\ncontract Z\n    associate E is W\n";
    private const string X41m = "contract K1\n    associate A\n    Self.(K1).A is LeftAddable<Self>\ncontract Wm\n    associate C is K1\n    associate H\n    Self.(Wm).C is Addable<Self.(Wm).H>\ncontract Zm\n    associate E is Wm\n";
    private const string X41i = "contract Z0\n    associate A\n    associate B\n    associate C\ncontract Zi: Z0\n    Self.(Z0).B is Self.(Z0).C\ncontract G0\n    associate G is Z0\ncontract Gi: G0\n    Self.(G0).G is Zi\n";
    private const string Y1 = "contract Z0\n    associate A\n    associate B\ncontract Zq: Z0\n    Self.(Z0).A is LeftAddable<Self.(Z0).B>\ncontract G0\n    associate G is Z0\ncontract Gq: G0\n    Self.(G0).G is Zq\n";
    private const string Y3 = "contract Z0\n    associate A\n    associate B\ncontract Za: Z0\n    Self.(Z0).B is Addable<Self.(Z0).A>\ncontract Zb: Z0\n    Self.(Z0).A is LeftAddable<Self.(Z0).B>\ncontract G0\n    associate G is Z0\ncontract Ga: G0\n    Self.(G0).G is Za\ncontract Gb: G0\n    Self.(G0).G is Zb\n";
    private const string MainLine = "public func main() => ()\n";

    // PLAN G882 (SPEC 13.3.4 under 8.2.3 and 8.3.2): the coverage table's programs (labels from SPEC first; design-g882 §3) and the
    // independent review's twins (t*). One operand pair is one up to the view's identity, on either side and in either direction, and
    // its opposite premise may be written or supplied by a Contract premise at any depth; the contradiction is recorded once, at the
    // declaration where its premises first meet (`at`: f, Box, or the member g whose identity joins Box's premises). The valid controls
    // are accepted. e0n and e2n keep their independent 8.2.3 conflict beside the contradiction, and x22n, x22s and x22r their 8.3.2
    // contradictory evidence (`T is Copy`, `T is not Copy`), each at the declaration (SPEC 23.1.2); x40n's two premises are both supplied
    // by `L is W`, one level deep, its identity written; k2n's contradiction is in Contract K's own environment, whose `Self is
    // ObjectPayload` reads K itself: no record at K beside the contradiction (a self-read is no failed input). Pinned as they are: k2n's
    // four ICs at its premises' projections (5:5, 5:27, 6:5, 6:31: later checks reading K's failed environment, dev's records). a6n, a6k
    // and a6v have 8.3.2's closed-world record, dev's own (an identity premise makes `Option<i32> is LeftAddable<L>`, `Option<i32> is
    // Addable<R>` in a6k, `Option<u8> is LeftAddable<L>` in a6v, directly available evidence that the closed world refutes: contradictory
    // evidence at f, SPEC 8.3.1, 8.3.2, 8.2.3): a6n and a6k beside their contradiction (SPEC 23.1.2), a6v (invalid by 8.3.2 alone) with
    // no direction record. x41n (design-x41n §2, §6; labels from SPEC: x41v, x41u, x41x, x41mv, x41iv, x41sv, y1v, y3v valid, the others invalid): a pair's
    // premises are the rows of the parameters and projections occurring in the chain's premises, so a pair two or three levels below a
    // written Contract premise (x41n, x41o, x41t), on either side of the identity (x41m, x41md; d2n, whose identity names the projection),
    // joined by decomposition (y4n), by congruence (x41p) or by a premise of the member (x41s, y1n, y1m,
    // y1d) is found where the premises first meet (x41s2 at Box, g stating nothing); x41e and x41f keep their independent 8.2.3 conflict
    // and 8.3.2 contradictory evidence. The ICs at the identity's projections rest on the failed environment (a3n's pattern). Pinned accepted, Closure(view)'s limit (PLAN dm1's class 1 row): x41i, an identity supplied only at depth; y3n and
    // y3s, both premises below the premises' Types.
    [Theory]
    [InlineData("a0n", "func f<L, R>(left: ref/L, right: ref/R)\n    L is Addable<R>\n    R is LeftAddable<L>\n    ()\npublic func main() => ()\n", "1*ArithmeticDirection_Kd", "f<")]
    [InlineData("a0r", "func f<L, R>(left: ref/L, right: ref/R)\n    R is LeftAddable<L>\n    L is Addable<R>\n    ()\npublic func main() => ()\n", "1*ArithmeticDirection_Kd", "f<")]
    [InlineData("a0v", "func f<L, R, M>(left: ref/L, right: ref/R)\n    L is Addable<R>\n    R is LeftAddable<M>\n    ()\npublic func main() => ()\n", "clean", null)]
    [InlineData("a1n", "func f<L, R, S>(left: ref/L, right: ref/R)\n    L is Addable<R>\n    S is LeftAddable<L>\n    R is S\n    ()\npublic func main() => ()\n", "1*ArithmeticDirection_Kd", "f<")]
    [InlineData("a1o", "func f<L, R, S>(left: ref/L, right: ref/R)\n    L is Addable<R>\n    S is LeftAddable<L>\n    S is R\n    ()\npublic func main() => ()\n", "1*ArithmeticDirection_Kd", "f<")]
    [InlineData("a1r", "func f<L, R, S>(left: ref/L, right: ref/R)\n    S is LeftAddable<L>\n    L is Addable<R>\n    R is S\n    ()\npublic func main() => ()\n", "1*ArithmeticDirection_Kd", "f<")]
    [InlineData("a1v", "func f<L, R, S>(left: ref/L, right: ref/R)\n    L is Addable<R>\n    S is LeftAddable<L>\n    ()\npublic func main() => ()\n", "clean", null)]
    [InlineData("a1w", "func f<L, R, S, M>(left: ref/L, right: ref/R)\n    L is Addable<R>\n    S is LeftAddable<M>\n    R is S\n    ()\npublic func main() => ()\n", "clean", null)]
    [InlineData("a2n", "func f<L, R, M>(left: ref/L, right: ref/R)\n    L is Addable<R>\n    R is LeftAddable<M>\n    M is L\n    ()\npublic func main() => ()\n", "1*ArithmeticDirection_Kd", "f<")]
    [InlineData("a9n", "func f<L, R, S, M>(left: ref/L, right: ref/R)\n    L is Addable<R>\n    S is LeftAddable<M>\n    R is S\n    M is L\n    ()\npublic func main() => ()\n", "1*ArithmeticDirection_Kd", "f<")]
    [InlineData("a4n", "func f<L, R>(left: ref/L, right: ref/R)\n    L is Addable<R>\n    L is LeftAddable<R>\n    L is R\n    ()\npublic func main() => ()\n", "1*ArithmeticDirection_Kd", "f<")]
    [InlineData("a4v", "func f<L, R>(left: ref/L, right: ref/R)\n    L is Addable<R>\n    L is LeftAddable<R>\n    ()\npublic func main() => ()\n", "clean", null)]
    [InlineData("a5n", "func f<L, R, S, W>(left: ref/L, right: ref/R)\n    L is Addable<R>\n    S is LeftAddable<L>\n    W is Option<R>\n    W is Option<S>\n    ()\npublic func main() => ()\n", "1*ArithmeticDirection_Kd", "f<")]
    [InlineData("a5v", "func f<L, R, S, W, V>(left: ref/L, right: ref/R)\n    L is Addable<R>\n    S is LeftAddable<L>\n    W is Option<R>\n    V is Option<S>\n    ()\npublic func main() => ()\n", "clean", null)]
    [InlineData("a7n", "contract Q\n    associate A\n    associate B\n    Self.(Q).A is Self.(Q).B\nfunc f<L, T>(left: ref/L)\n    T is Q\n    L is Addable<T.(Q).A>\n    T.(Q).B is LeftAddable<L>\n    ()\npublic func main() => ()\n", "1*ArithmeticDirection_Kd 2*InvalidConstraint_Kd", "f<")]
    [InlineData("a7v", "contract Q\n    associate A\n    associate B\nfunc f<L, T>(left: ref/L)\n    T is Q\n    L is Addable<T.(Q).A>\n    T.(Q).B is LeftAddable<L>\n    ()\npublic func main() => ()\n", "clean", null)]
    [InlineData("a3n", "contract P\n    associate A\n    Self.(P).A is LeftAddable<Self>\nfunc f<L>(left: ref/L)\n    L is P\n    L is Addable<L.(P).A>\n    ()\npublic func main() => ()\n", "1*ArithmeticDirection_Kd 1*InvalidConstraint_Kd", "f<")]
    [InlineData("a3v", "contract P\n    associate A\n    Self.(P).A is LeftAddable<i32>\nfunc f<L>(left: ref/L)\n    L is P\n    L is Addable<L.(P).A>\n    ()\npublic func main() => ()\n", "clean", null)]
    [InlineData("a3i", "contract P\n    associate A\n    Self.(P).A is LeftAddable<Self>\nfunc f<L, R>(left: ref/L, right: ref/R)\n    L is P\n    L is Addable<R>\n    R is L.(P).A\n    ()\npublic func main() => ()\n", "1*ArithmeticDirection_Kd 1*InvalidConstraint_Kd", "f<")]
    [InlineData("a3iv", "contract P\n    associate A\n    Self.(P).A is LeftAddable<i32>\nfunc f<L, R>(left: ref/L, right: ref/R)\n    L is P\n    L is Addable<R>\n    R is L.(P).A\n    ()\npublic func main() => ()\n", "clean", null)]
    [InlineData("d2r", "contract P\n    associate A\n    Self.(P).A is LeftAddable<Self>\ncontract P2\n    associate B is P\nfunc f<L>(left: ref/L)\n    L is P2\n    L.(P2).B is Addable<L.(P2).B.(P).A>\n    ()\npublic func main() => ()\n", "1*ArithmeticDirection_Kd 3*InvalidConstraint_Kd", "f<")]
    [InlineData("d2n", "contract P\n    associate A\n    Self.(P).A is LeftAddable<Self>\ncontract P2\n    associate B is P\nfunc f<L, R>(left: ref/L, right: ref/R)\n    L is P2\n    L.(P2).B is Addable<R>\n    R is L.(P2).B.(P).A\n    ()\npublic func main() => ()\n", "1*ArithmeticDirection_Kd 3*InvalidConstraint_Kd", "f<")]
    [InlineData("a6n", "func f<L, R>(left: ref/L, right: ref/R)\n    L is Addable<Option<i32>>\n    R is LeftAddable<L>\n    R is Option<i32>\n    ()\npublic func main() => ()\n", "1*ArithmeticDirection_Kd 1*InvalidConstraint_Kd", "f<")]
    [InlineData("a6k", "func f<L, R, K>(left: ref/L, right: ref/R)\n    L is Addable<R>\n    R is LeftAddable<K>\n    L is Option<i32>\n    K is Option<i32>\n    ()\npublic func main() => ()\n", "1*ArithmeticDirection_Kd 1*InvalidConstraint_Kd", "f<")]
    [InlineData("a6v", "func f<L, R>(left: ref/L, right: ref/R)\n    L is Addable<Option<i32>>\n    R is LeftAddable<L>\n    R is Option<u8>\n    ()\npublic func main() => ()\n", "1*InvalidConstraint_Kd", null)]
    [InlineData("n1n", "struct Box<L, R, S>\n    L is Addable<R>\n    S is LeftAddable<L>\n    public func g(self)\n        R is S\n        ()\npublic func main() => ()\n", "1*ArithmeticDirection_Kd", "g(self)")]
    [InlineData("n1v", "struct Box<L, R, S>\n    L is Addable<R>\n    S is LeftAddable<L>\n    public func g(self)\n        ()\npublic func main() => ()\n", "clean", null)]
    [InlineData("n2n", "struct Box<L, R, S>\n    L is Addable<R>\n    public func g(self)\n        S is LeftAddable<L>\n        R is S\n        ()\npublic func main() => ()\n", "1*ArithmeticDirection_Kd", "g(self)")]
    [InlineData("n2v", "struct Box<L, R, S>\n    L is Addable<R>\n    public func g(self)\n        S is LeftAddable<L>\n        ()\npublic func main() => ()\n", "clean", null)]
    [InlineData("o2v", "func f<L, R, S>(left: ref/L, right: ref/R)\n    L is Addable<R>\n    S is LeftAddable<L>\n    R is S or i32\n    ()\npublic func main() => ()\n", "clean", null)]
    [InlineData("e1n", "func f<L, R, S>(left: ref/L, right: ref/R)\n    L is Addable<R>\n    S is LeftAddable<L>\n    R is S\n    S is Option<R>\n    ()\npublic func main() => ()\n", "1*InvalidConstraint_Kd", null)]
    [InlineData("e0n", "func f<L, R, T, U>(left: ref/L, right: ref/R)\n    L is Addable<R>\n    R is LeftAddable<L>\n    T is Option<U>\n    T is Array<U>\n    ()\npublic func main() => ()\n", "1*ArithmeticDirection_Kd 1*InvalidConstraint_Kd", "f<")]
    [InlineData("e2n", "func f<L, R, S, T, U>(left: ref/L, right: ref/R)\n    L is Addable<R>\n    S is LeftAddable<L>\n    R is S\n    T is Option<U>\n    T is Array<U>\n    ()\npublic func main() => ()\n", "1*ArithmeticDirection_Kd 1*InvalidConstraint_Kd", "f<")]
    [InlineData("x22n", "func f<L, R, S, T>(left: ref/L, right: ref/R)\n    T is Copy\n    T is not Copy\n    L is Addable<R>\n    S is LeftAddable<L>\n    R is S\n    ()\npublic func main() => ()\n", "1*ArithmeticDirection_Kd 1*InvalidConstraint_Kd", "f<")]
    [InlineData("x22s", "struct Box<L, R, S, T>\n    T is Copy\n    T is not Copy\n    L is Addable<R>\n    S is LeftAddable<L>\n    R is S\npublic func main() => ()\n", "1*ArithmeticDirection_Kd 1*InvalidConstraint_Kd", "struct Box")]
    [InlineData("x22r", "func f<L, R, T>(left: ref/L, right: ref/R)\n    T is Copy\n    T is not Copy\n    L is Addable<R>\n    R is LeftAddable<L>\n    ()\npublic func main() => ()\n", "1*ArithmeticDirection_Kd 1*InvalidConstraint_Kd", "f<")]
    [InlineData("x40n", "contract K1\n    associate A\n    Self.(K1).A is LeftAddable<Self>\ncontract W\n    associate C is K1\n    associate D\n    Self.(W).D is Addable<Self.(W).C.(K1).A>\nfunc f<L>(left: ref/L)\n    L is W\n    L.(W).D is L.(W).C\n    ()\npublic func main() => ()\n", "1*ArithmeticDirection_Kd 2*InvalidConstraint_Kd", "f<")]
    [InlineData("k2n", "contract K\n    associate A\n    associate B\n    Self is ObjectPayload\n    Self.(K).A is Addable<Self.(K).B>\n    Self.(K).B is LeftAddable<Self.(K).A>\npublic func main() => ()\n", "1*ArithmeticDirection_Kd 4*InvalidConstraint_Kd", "contract K")]
    [InlineData("k2v", "contract K\n    associate A\n    associate B\n    Self is ObjectPayload\n    Self.(K).A is Addable<Self.(K).B>\npublic func main() => ()\n", "clean", null)]
    [InlineData("t1i", "contract P\n    associate A\n    Self.(P).A is Addable<Self>\nfunc f<L, M>(left: ref/L)\n    L is P\n    L is LeftAddable<M>\n    M is L.(P).A\n    ()\npublic func main() => ()\n", "1*ArithmeticDirection_Kd 1*InvalidConstraint_Kd", "f<")]
    [InlineData("t1v", "contract P\n    associate A\n    Self.(P).A is Addable<i32>\nfunc f<L>(left: ref/L)\n    L is P\n    L is LeftAddable<L.(P).A>\n    ()\npublic func main() => ()\n", "clean", null)]
    [InlineData("t2n", "contract Q\n    associate A\nfunc f<L, R, T>(left: ref/L)\n    T is Q\n    L is Addable<T.(Q).A>\n    R is LeftAddable<L>\n    R is T.(Q).A\n    ()\npublic func main() => ()\n", "1*ArithmeticDirection_Kd 2*InvalidConstraint_Kd", "f<")]
    [InlineData("t2d", "contract Q\n    associate A\nfunc f<L, R, T, W>(left: ref/L)\n    T is Q\n    L is Addable<T.(Q).A>\n    R is LeftAddable<L>\n    W is Option<T.(Q).A>\n    W is Option<R>\n    ()\npublic func main() => ()\n", "1*ArithmeticDirection_Kd 2*InvalidConstraint_Kd", "f<")]
    [InlineData("t2v", "contract Q\n    associate A\nfunc f<L, R, T, W, V>(left: ref/L)\n    T is Q\n    L is Addable<T.(Q).A>\n    R is LeftAddable<L>\n    W is Option<T.(Q).A>\n    V is Option<R>\n    ()\npublic func main() => ()\n", "clean", null)]
    [InlineData("t3m", "struct Box<L, R, S>\n    S is LeftAddable<L>\n    public func g(self)\n        L is Addable<R>\n        R is S\n        ()\npublic func main() => ()\n", "1*ArithmeticDirection_Kd", "g(self)")]
    [InlineData("t3x", "struct Box<L, R, S>\n    R is S\n    L is Addable<R>\n    public func g(self)\n        S is LeftAddable<L>\n        ()\npublic func main() => ()\n", "1*ArithmeticDirection_Kd", "g(self)")]
    [InlineData("t3c", "struct Box<L, R, S>\n    L is Addable<R>\n    S is LeftAddable<L>\n    R is S\n    public func g(self)\n        R is S\n        ()\npublic func main() => ()\n", "1*ArithmeticDirection_Kd 1*InvalidConstraint_Kd", "struct Box")]
    [InlineData("t7n", "contract P\n    associate A\n    Self.(P).A is Addable<Self>\ncontract P2\n    associate B is P\nfunc f<L>(left: ref/L)\n    L is P2\n    L.(P2).B is LeftAddable<L.(P2).B.(P).A>\n    ()\npublic func main() => ()\n", "1*ArithmeticDirection_Kd 3*InvalidConstraint_Kd", "f<")]
    [InlineData("t5a", "func f<L, R>(left: ref/L, right: ref/R)\n    L is Addable<R>\n    R is LeftSubtractable<L>\n    ()\npublic func main() => ()\n", "clean", null)]
    [InlineData("t5b", "func f<L, R>(left: ref/L, right: ref/R)\n    L is Addable<R>\n    R is LeftAddable<Option<L>>\n    ()\npublic func main() => ()\n", "clean", null)]
    [InlineData("t5c", "func f<L, R>(left: ref/L, right: ref/R)\n    L is Addable<R>\n    ()\nfunc g<L, R>(left: ref/L, right: ref/R)\n    R is LeftAddable<L>\n    ()\npublic func main() => ()\n", "clean", null)]
    [InlineData("t5d", "contract K<X>\nfunc f<L, R, S, T>(left: ref/L, right: ref/R)\n    L is Addable<R>\n    S is LeftAddable<L>\n    T is K<R>\n    T is K<S>\n    ()\npublic func main() => ()\n", "clean", null)]
    [InlineData("t5e", "struct Box<L, R>\n    L is Addable<R>\nstruct Box2<L, R>\n    R is LeftAddable<L>\npublic func main() => ()\n", "clean", null)]
    [InlineData("t5f", "func f<L, R, S, W>(left: ref/L, right: ref/R)\n    L is Addable<R>\n    S is LeftAddable<L>\n    W is Option<R>\n    S is Option<W>\n    ()\npublic func main() => ()\n", "clean", null)]
    [InlineData("t5g", "func f<L, R>(left: ref/L, right: ref/R)\n    L is Addable<R>\n    R is LeftAddable<L> or Copy\n    ()\npublic func main() => ()\n", "clean", null)]
    [InlineData("x41n", X41 + "func f<L>(left: ref/L)\n    L is Z\n    L.(Z).E.(W).D is L.(Z).E.(W).C\n    ()\n" + MainLine, "1*ArithmeticDirection_Kd 4*InvalidConstraint_Kd", "f<")]
    [InlineData("x41o", "contract Z\n    associate E is W\ncontract W\n    associate C is K1\n    associate D\n    Self.(W).D is Addable<Self.(W).C.(K1).A>\ncontract K1\n    associate A\n    Self.(K1).A is LeftAddable<Self>\nfunc f<L>(left: ref/L)\n    L.(Z).E.(W).C is L.(Z).E.(W).D\n    L is Z\n    ()\n" + MainLine, "1*ArithmeticDirection_Kd 4*InvalidConstraint_Kd", "f<")]
    [InlineData("x41t", X41 + "contract Y\n    associate G is Z\nfunc f<L>(left: ref/L)\n    L is Y\n    L.(Y).G.(Z).E.(W).D is L.(Y).G.(Z).E.(W).C\n    ()\n" + MainLine, "1*ArithmeticDirection_Kd 6*InvalidConstraint_Kd", "f<")]
    [InlineData("x41m", X41m + "func f<L>(left: ref/L)\n    L is Zm\n    L.(Zm).E.(Wm).H is L.(Zm).E.(Wm).C.(K1).A\n    ()\n" + MainLine, "1*ArithmeticDirection_Kd 5*InvalidConstraint_Kd", "f<")]
    [InlineData("x41md", X41m + "func f<L>(left: ref/L)\n    L is Zm\n    L.(Zm).E.(Wm).C.(K1).A is L.(Zm).E.(Wm).H\n    ()\n" + MainLine, "1*ArithmeticDirection_Kd 5*InvalidConstraint_Kd", "f<")]
    [InlineData("x41mv", X41m + "func f<L>(left: ref/L)\n    L is Zm\n    L.(Zm).E.(Wm).H is L.(Zm).E.(Wm).C\n    ()\n" + MainLine, "clean", null)]
    [InlineData("x41p", X41 + "func f<L, M>(left: ref/L, right: ref/M)\n    L is Z\n    M is Z\n    L.(Z).E is M.(Z).E\n    L.(Z).E.(W).D is M.(Z).E.(W).C\n    ()\n" + MainLine, "1*ArithmeticDirection_Kd 6*InvalidConstraint_Kd", "f<")]
    [InlineData("x41s", X41 + "struct Box<L>\n    L is Z\n    public func g(self)\n        L.(Z).E.(W).D is L.(Z).E.(W).C\n        ()\n" + MainLine, "1*ArithmeticDirection_Kd 4*InvalidConstraint_Kd", "g(self)")]
    [InlineData("x41s2", X41 + "struct Box<L>\n    L is Z\n    L.(Z).E.(W).D is L.(Z).E.(W).C\n    public func g(self)\n        ()\n" + MainLine, "1*ArithmeticDirection_Kd 4*InvalidConstraint_Kd", "struct Box")]
    [InlineData("x41sv", X41 + "struct Box<L>\n    L is Z\n    public func g(self)\n        L.(Z).E.(W).D is Copy\n        ()\n" + MainLine, "clean", null)]
    [InlineData("x41v", X41 + "func f<L>(left: ref/L)\n    L is Z\n    ()\n" + MainLine, "clean", null)]
    [InlineData("x41u", "contract K1\n    associate A\n    Self.(K1).A is LeftMultipliable<Self>\ncontract W\n    associate C is K1\n    associate D\n    Self.(W).D is Addable<Self.(W).C.(K1).A>\ncontract Z\n    associate E is W\nfunc f<L>(left: ref/L)\n    L is Z\n    L.(Z).E.(W).D is L.(Z).E.(W).C\n    ()\n" + MainLine, "clean", null)]
    [InlineData("x41x", "contract K1\n    associate A\n    Self.(K1).A is LeftAddable<Self>\ncontract W\n    associate C is K1\n    associate D\n    Self.(W).D is Addable<Self.(W).C>\ncontract Z\n    associate E is W\nfunc f<L>(left: ref/L)\n    L is Z\n    L.(Z).E.(W).D is L.(Z).E.(W).C\n    ()\n" + MainLine, "clean", null)]
    [InlineData("x41e", X41 + "func f<L, T, U>(left: ref/L)\n    L is Z\n    L.(Z).E.(W).D is L.(Z).E.(W).C\n    T is Option<U>\n    T is Array<U>\n    ()\n" + MainLine, "1*ArithmeticDirection_Kd 5*InvalidConstraint_Kd", "f<")]
    [InlineData("x41f", X41 + "func f<L, T>(left: ref/L)\n    T is Copy\n    T is not Copy\n    L is Z\n    L.(Z).E.(W).D is L.(Z).E.(W).C\n    ()\n" + MainLine, "1*ArithmeticDirection_Kd 5*InvalidConstraint_Kd", "f<")]
    [InlineData("x41i", X41i + "func f<M>(value: ref/M)\n    M is Gi\n    M.(G0).G.(Z0).B is Addable<M.(G0).G.(Z0).A>\n    M.(G0).G.(Z0).A is LeftAddable<M.(G0).G.(Z0).C>\n    ()\n" + MainLine, "clean", null)]
    [InlineData("x41iv", X41i + "func f<M>(value: ref/M)\n    M is G0\n    M.(G0).G.(Z0).B is Addable<M.(G0).G.(Z0).A>\n    M.(G0).G.(Z0).A is LeftAddable<M.(G0).G.(Z0).C>\n    ()\n" + MainLine, "clean", null)]
    [InlineData("y4n", X41 + "func f<L, T>(left: ref/L, value: T)\n    L is Z\n    T is Option<L.(Z).E.(W).D>\n    T is Option<L.(Z).E.(W).C>\n    ()\n" + MainLine, "1*ArithmeticDirection_Kd 4*InvalidConstraint_Kd", "f<")]
    [InlineData("y1n", Y1 + "struct Box<M>\n    M is G0\n    M.(G0).G.(Z0).B is Addable<M.(G0).G.(Z0).A>\n    public func g(self)\n        M is Gq\n        ()\n" + MainLine, "1*ArithmeticDirection_Kd", "g(self)")]
    [InlineData("y1m", Y1 + "struct Box<M>\n    M is Gq\n    public func g(self)\n        M.(G0).G.(Z0).B is Addable<M.(G0).G.(Z0).A>\n        ()\n" + MainLine, "1*ArithmeticDirection_Kd 4*InvalidConstraint_Kd", "g(self)")]
    [InlineData("y1f", Y1 + "func f<M>(value: ref/M)\n    M is G0\n    M.(G0).G.(Z0).B is Addable<M.(G0).G.(Z0).A>\n    M is Gq\n    ()\n" + MainLine, "1*ArithmeticDirection_Kd 4*InvalidConstraint_Kd", "f<")]
    [InlineData("y1v", "contract Z0\n    associate A\n    associate B\ncontract Zq: Z0\n    Self.(Z0).A is LeftMultipliable<Self.(Z0).B>\ncontract G0\n    associate G is Z0\ncontract Gq: G0\n    Self.(G0).G is Zq\nstruct Box<M>\n    M is G0\n    M.(G0).G.(Z0).B is Addable<M.(G0).G.(Z0).A>\n    public func g(self)\n        M is Gq\n        ()\n" + MainLine, "clean", null)]
    [InlineData("y1d", Y1 + "struct Box<M>\n    M is Z0\n    M.(Z0).B is Addable<M.(Z0).A>\n    public func g(self)\n        M is Zq\n        ()\n" + MainLine, "1*ArithmeticDirection_Kd", "g(self)")]
    [InlineData("y3n", Y3 + "func f<M>(value: ref/M)\n    M is Ga\n    M is Gb\n    ()\n" + MainLine, "clean", null)]
    [InlineData("y3s", Y3 + "struct Box<M>\n    M is Ga\n    public func g(self)\n        M is Gb\n        ()\n" + MainLine, "clean", null)]
    [InlineData("y3d", Y3 + "func f<M>(value: ref/M)\n    M is Za\n    M is Zb\n    ()\n" + MainLine, "1*ArithmeticDirection_Kd", "f<")]
    [InlineData("y3v", "contract Z0\n    associate A\n    associate B\ncontract Za: Z0\n    Self.(Z0).B is Addable<Self.(Z0).A>\ncontract Zb: Z0\n    Self.(Z0).A is LeftMultipliable<Self.(Z0).B>\ncontract G0\n    associate G is Z0\ncontract Ga: G0\n    Self.(G0).G is Za\ncontract Gb: G0\n    Self.(G0).G is Zb\nfunc f<M>(value: ref/M)\n    M is Ga\n    M is Gb\n    ()\n" + MainLine, "clean", null)]
    public void ADirectionContradictionIsOnePairUpToIdentity(string name, string program, string records, string? at)
    {
        _ = name;
        var result = DiagnosticCorpus.Check(program);
        Assert.Equal(records, DiagnosticCorpus.RecordProfile(result));
        int[] expected = at is null ? [] : [program.IndexOf(at, StringComparison.Ordinal)];
        Assert.Equal(expected, result.Diagnostics.Where(static x => x.Code == nameof(DiagnosticCode.ArithmeticDirection_Kd)).Select(static x => x.Span!.Value.Start));
    }

    // PLAN G882's scaling row: the direction check asks the rows once per written arithmetic premise of a view (OppositeRow), so the
    // pairs Closure(view) indexes grow linearly in one declaration's premises and in the declarations. Valid (SPEC 13.3.4, 8.2.3): each
    // identity joins S{i} to R{i}, whose pairs (L{i}, R{i}) and (M{i}, R{i}) differ (a1w).
    [Theory]
    [InlineData("premises")]
    [InlineData("declarations")]
    public void DirectionChecksAreLinearInTheArithmeticPremises(string grown)
    {
        var (w8, w16, w32) = (Work(grown, 8), Work(grown, 16), Work(grown, 32));
        Assert.True(w8 > 0 && w16 <= 2 * w8 && w32 <= 2 * w16, $"{grown}: {w8} at 8, {w16} at 16, {w32} at 32");

        static int Work(string grown, int n)
        {
            var (declarations, premises) = grown == "declarations" ? (n, 4) : (1, n);
            var parameters = string.Join(", ", Enumerable.Range(0, premises).Select(static i => $"L{i}, R{i}, S{i}, M{i}"));
            var clauses = string.Concat(Enumerable.Range(0, premises).Select(static i => $"    L{i} is Addable<R{i}>\n    S{i} is LeftAddable<M{i}>\n    R{i} is S{i}\n"));
            return Closure(string.Concat(Enumerable.Range(0, declarations).Select(j => $"func f{j}<{parameters}>(left: ref/L0)\n{clauses}    ()\n"))) - Closure(string.Empty);
        }

        static int Closure(string source)
        {
            var c = MinimalEmissionTest.Analyze(source + "public func main() -> ()\n    return\n");
            Assert.True(CompilationTestHelper.PublishedErrors(c).Length == 0, MinimalEmissionTest.Describe(c));
            return c.Binding.ClosureIndexWork;
        }
    }

    // x41n's scaling row: a scope with premises of its own asks the rows of each seed of its chain once (each parameter or projection
    // occurring in the chain's premises, once per check) and a scope with none asks nothing, so the seeds grow linearly in the members
    // with premises (members x (own + enclosing seeds)), in the enclosing premises and in a written projection's depth, and premise-less
    // members add none. The work is Closure(view)'s pairs (ClosureIndexWork, G882's measure): a view whose class keys are final keeps its
    // entry, formed once, so it is linear too; a view with a derived identity fact (K's `Self.(K).A is Self.(K).B`, `-derived`) forms its
    // entry again for each request (ContractSolver's RowsRequest keeps only final keys), so a scope pays its chain seeds x its view's
    // pairs: with n enclosing premises about (3n seeds) x (2n facts) per scope, quadratic in n (`enclosing-derived`, the bound asserted:
    // at most 4x per doubling; RP8's unit-specific kernel for x41n). `depth`'s link walks (a request per prefix, each walking up to n
    // links) have no counter here. Measured pairs at 8, 16, 32 (the ceilings pass a linear result too, so these are the evidence):
    // members 52, 100, 196; members-derived 5,674, 11,146, 22,090 (linear, about 690 per member); enclosing 48, 88, 168;
    // enclosing-derived 7,906, 25,138, 88,786 (x3.2, x3.5: about 87 n^2); depth 18, 34, 66. Valid (SPEC 13.3.4, 8.3.2, 8.2.3): no
    // arithmetic premise.
    [Theory]
    [InlineData("members")]
    [InlineData("members-derived")]
    [InlineData("enclosing")]
    [InlineData("enclosing-derived")]
    [InlineData("depth")]
    public void DirectionCheckCostIsItsSeedsTimesTheirViewsEntries(string grown)
    {
        var (s8, w8) = Measure(Source(grown, 8, 0));
        var (s16, w16) = Measure(Source(grown, 16, 0));
        var (s32, w32) = Measure(Source(grown, 32, 0));
        var growth = grown == "enclosing-derived" ? 4 : 2;
        Assert.True(s8 > 0 && s16 <= 2 * s8 && s32 <= 2 * s16, $"{grown} seeds: {s8} at 8, {s16} at 16, {s32} at 32");
        Assert.True(w8 > 0 && w16 <= growth * w8 && w32 <= growth * w16, $"{grown} pairs indexed: {w8} at 8, {w16} at 16, {w32} at 32");
        Assert.Equal(s8, Measure(Source(grown, 8, 8)).Seeds);

        // `members`: n members with two premises each under four enclosing premises; `enclosing`: four members under n; both with
        // `premiseless` members stating nothing. `depth`: one written projection n links deep.
        static string Source(string grown, int n, int premiseless)
        {
            if (grown == "depth")
            {
                var contracts = string.Concat(Enumerable.Range(0, n).Select(static i => $"contract C{i}\n    associate N is C{i + 1}\n")) + $"contract C{n}\n";
                var projection = "L" + string.Concat(Enumerable.Range(0, n).Select(static i => $".(C{i}).N"));
                return contracts + $"func f<L>(left: ref/L)\n    L is C0\n    {projection} is Copy\n    ()\n" + string.Concat(Enumerable.Range(0, premiseless).Select(static j => $"func h{j}<T>(value: ref/T) => ()\n"));
            }

            var members = grown.StartsWith("members", StringComparison.Ordinal) ? n : 4;
            var enclosing = grown.StartsWith("members", StringComparison.Ordinal) ? 4 : n;
            var contract = grown.EndsWith("-derived", StringComparison.Ordinal) ? "contract K\n    associate A\n    associate B\n    Self.(K).A is Self.(K).B\n" : "contract K\n";
            var parameters = string.Join(", ", Enumerable.Range(0, enclosing).Select(static i => $"L{i}").Concat(Enumerable.Range(0, 2 * members).Select(static i => $"U{i}")));
            return contract + $"struct Box<{parameters}>\n" + string.Concat(Enumerable.Range(0, enclosing).Select(static i => $"    L{i} is K\n")) +
                string.Concat(Enumerable.Range(0, members).Select(static j => $"    public func g{j}(self)\n        U{2 * j} is K\n        U{(2 * j) + 1} is K\n        ()\n")) +
                string.Concat(Enumerable.Range(0, premiseless).Select(static j => $"    public func h{j}(self)\n        ()\n"));
        }

        // The seeds the direction checks asked for and the pairs Closure(view) indexed, beyond those of the library alone.
        static (int Seeds, int Work) Measure(string source)
        {
            const string Main = "public func main() -> ()\n    return\n";
            var c = MinimalEmissionTest.Analyze(source + Main);
            Assert.True(CompilationTestHelper.PublishedErrors(c).Length == 0, MinimalEmissionTest.Describe(c));
            var library = MinimalEmissionTest.Analyze(Main).Binding;
            return (c.Binding.DirectionSeeds - library.DirectionSeeds, c.Binding.ClosureIndexWork - library.ClosureIndexWork);
        }
    }

    [Fact]
    public void LiteralAmbiguityRetainsBothConformancesInPublicRecords()
    {
        var source = Declarations.Replace("    Self is Negatable", "    Self is LeftMultipliable<f64>\n    Self is Negatable", StringComparison.Ordinal).Replace("    associate Negatable.Output", "    associate LeftMultipliable<f64>.Output is f64\n    associate Negatable.Output", StringComparison.Ordinal).Replace("    public func negated", "    public func multipliedFrom(left: ref/f64, self: ref/Self) -> f64 => left * self.value@f64\n    public func negated", StringComparison.Ordinal) + "\nlet value = Value.init()\nlet result: f32 = 2.0 * value";
        var path = Path.GetFullPath("arithmetic-selection.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.ArithmeticSelection_Kd), error.Code);
        Assert.Equal("2.0 * value", source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        Assert.Equal(new[] { "operator", "contract", "provider", "counterpart", "cause" }, error.Reason!.Select(static x => x.Name));
        Assert.Equal(new DiagnosticValue("cause", DiagnosticValueKind.Enumeration, "Ambiguous"), error.Reason![4]);
        Assert.Equal("provider: Value; counterpart: not independently typed", error.Label);
        Assert.Equal("Multiple proven conformances fit the counterpart structure", error.Note);
        Assert.Equal(2, error.Related!.Length);
        Assert.All(error.Related!, static x => Assert.Equal("conformance", x.Role));
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("LeftMultipliable<f32>", console.Text, StringComparison.Ordinal);
        Assert.Contains("LeftMultipliable<f64>", console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(error.Code, sent.Code);
            Assert.Equal(error.Display!.Range, sent.Range);
            Assert.Contains("Multiple proven", sent.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AFixedProviderGuidesOneNestedCall()
    {
        var source = Declarations + "\nfunc make<T>() -> T\n    T is PrimitiveInteger\n    return 2@T\nlet value = Value.init()\nrequire value + make() == 42 else => $abort(\"expected counterpart\")";
        ScalarEmissionTest.EmitFixture("UserArithmeticNested", source, string.Empty);
    }

    [Fact]
    public void LeftPremiseKeepsItsDirectionAndNoncommutativeArgumentOrder()
    {
        const string Program = "struct Difference\n    Self is LeftSubtractable<i32>\n    associate Output is i32\n    public func subtractedFrom(left: ref/i32, self: ref/Self) -> i32 => left - 2\nfunc subtract<L, R>(left: ref/L, right: ref/R) -> R.(LeftSubtractable<L>).Output\n    R is LeftSubtractable<L>\n    return left - right\nlet value = Difference.init()\nrequire subtract(44, value) == 42 else => $abort(\"left direction\")";
        ScalarEmissionTest.EmitFixture("UserArithmeticLeftGeneric", Program, string.Empty);
    }

    [Fact]
    public void ResultsMayKeepExistingExternalDependencies()
    {
        const string Program = "struct View {a}\n    Self is Addable<()>\n    associate Output is ref/i32 during a\n    let value: ref/i32 during a\n    public init(value: ref/i32 during a) => self.value = value\n    public func added(self: ref/Self, right: ref/()) -> ref/i32 during a => self.value\nlet n = 42\nlet view = View.init(n@ref)\nlet result = view + ()\nrequire result == 42 else => $abort(\"external dependency\")";
        ScalarEmissionTest.EmitFixture("UserArithmeticExternal", Program, string.Empty);
    }

    [Fact]
    public void OriginsCannotChooseBetweenStructurallyEqualCounterparts()
    {
        const string Program = "struct View {source}\n    let value: ref/i32 during source\nfunc select<L>(left: ref/L, right: ref/(View during a), marker: ref/i32 during b) -> i32\n    L is Addable<View during a> and Addable<View during b>\n    return left + right\n()";
        var result = DiagnosticCorpus.Check(Program);
        var error = Assert.Single(result.Diagnostics, static x => x.Code == nameof(DiagnosticCode.ArithmeticSelection_Kd));
        Assert.Equal(new DiagnosticValue("cause", DiagnosticValueKind.Enumeration, "Ambiguous"), error.Reason![4]);
        Assert.StartsWith("provider: L; counterpart: View", error.Label, StringComparison.Ordinal);
        Assert.Equal("Multiple proven conformances fit the counterpart structure", error.Note);
        Assert.Equal(2, error.Related!.Length);
    }

    [Fact]
    public void UnknownConditionalCandidatesCannotEstablishUniqueness()
    {
        const string Program = "struct Box<T>\n    Self is Addable<i32>\n    Self is Addable<i64> when T is Copy\n    associate Addable<i32>.Output is i32\n    associate Addable<i64>.Output is i64\n    public func added(self: ref/Self, right: ref/i32) -> i32 => right\n    public func added(self: ref/Self, right: ref/i64) -> i64 => right\nfunc use<T>(value: ref/Box<T>) -> i32 => value + 1\n()";
        var result = DiagnosticCorpus.Check(Program);
        var error = Assert.Single(result.Diagnostics, static x => x.Code == nameof(DiagnosticCode.ArithmeticSelection_Kd));
        Assert.Equal(new DiagnosticValue("cause", DiagnosticValueKind.Enumeration, "Unproven"), error.Reason![4]);
        Assert.Equal("provider: Box<T>; counterpart: not independently typed", error.Label);
        Assert.Equal("A structurally fitting conditional conformance remains unproven", error.Note);
        Assert.Equal(2, error.Related!.Length);
    }

    // SPEC 13.3.4: a right provider under a non-numeric left operand, and no fitting conformance on the ordinary route (a user
    // provider's candidates, related) or without one (a Type parameter); each cause is explained by the Note alone (DECISIONS BU-111).
    [Theory]
    [InlineData(Declarations + "\nlet value = Value.init()\nlet result = true + value", "LeftAddable", "NonNumericLeft", "A right provider requires a built-in numeric left operand", "provider: Value; counterpart: bool", 0)]
    [InlineData(Declarations + "\nlet value = Value.init()\nlet result = value + true", "Addable", "NoFit", "No proven conformance fits the counterpart structure", "provider: Value; counterpart: bool", 1)]
    [InlineData("func add<T>(a: T, b: T) -> T => a + b\npublic func main() => ()", "Addable", "NoFit", "No proven conformance fits the counterpart structure", "provider: T; counterpart: T", 0)]
    public void ASelectionFailureNamesItsCause(string source, string contract, string cause, string note, string label, int related)
    {
        var result = DiagnosticCorpus.Check(source);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.ArithmeticSelection_Kd), error.Code);
        Assert.Equal(new[] { "operator", "contract", "provider", "counterpart", "cause" }, error.Reason!.Select(static x => x.Name));
        Assert.Equal(contract, error.Reason![1].Value);
        Assert.Equal(new DiagnosticValue("cause", DiagnosticValueKind.Enumeration, cause), error.Reason[4]);
        Assert.Equal(label, error.Label);
        Assert.Equal(note, error.Note);
        Assert.Equal(related, error.Related?.Length ?? 0);
    }

    [Theory]
    [InlineData("0..1")]
    [InlineData("Option<i32>.Some(1)")]
    public void OrdinaryLibraryTypesUseTheOrdinaryProviderRule(string value)
    {
        var result = DiagnosticCorpus.Check("let value = " + value + "\nlet result = value + ()");
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.ArithmeticSelection_Kd), error.Code);
        Assert.Contains("Addable", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("value + missing")]
    [InlineData("missing * value")]
    public void AnInvalidOperandDoesNotInventAConformanceFailure(string expression)
    {
        var source = Declarations + "\nlet value = Value.init()\nlet result = " + expression;
        var result = DiagnosticCorpus.Check(source);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.UnresolvedBinding_Kd), error.Code);
        Assert.Equal("missing", source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        Assert.DoesNotContain(result.Diagnostics, static x => x.Code == nameof(DiagnosticCode.ArithmeticSelection_Kd));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GenericOperatorEffectsUseOnlyPublishedBounds(bool bounded)
    {
        var source = "contract ConfinedAdd<T>: Addable<T>\n    effect (Addable<T>).added confined\ncontract Operation<T>\n    func run(self: ref/Self, left: ref/T, right: ref/T) -> i32\n        effect confined\nstruct Forward<T>\n    T is " + (bounded ? "ConfinedAdd<T>" : "Addable<T>") + "\n    T.(Addable<T>).Output is i32\n    Self is Operation<T>\n    public func run(self: ref/Self, left: ref/T, right: ref/T) -> i32 => left + right\npublic func main() => ()";
        EffectBoundImplementationTest.AssertBound(MinimalEmissionTest.Analyze(source), bounded, "Self is Operation<T>"); // A4's record (R6-check H3b T40)
    }

    [Fact]
    public void AnEditedProviderCannotReuseItsPreviousOperatorSelection()
    {
        var c = MinimalEmissionTest.Analyze(Declarations + "\nlet value = Value.init()\nlet result = value + 2");
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
        var original = Assert.Single(c.Kotonoha.RootKoto.NestedContainers.OfType<StructKoto>());
        var donor = MinimalEmissionTest.Analyze("struct Value\n    public let value: i32 = 40\nlet value = Value.init()");
        var changed = Assert.Single(donor.Kotonoha.RootKoto.NestedContainers.OfType<StructKoto>());
        ParseTestHelper.Detach(changed, new GroupKoto(changed.CodeContext, default, default));
        Assert.True(KotoHelper.Replace(original.Parent!, original, changed));
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, static x => x.Code == DiagnosticCode.ArithmeticSelection_Kd);
        Assert.False(c.Generate(TextWriter.Null));
    }

    [Fact]
    public void AnImplementationCannotReturnItsFreshInspectionLoan()
    {
        const string Source = "struct View {a}\n    Self is Addable<()>\n    associate Output is ref/i32 during a\n    let value: i32 = 42\n    public func added(self: ref/Self, right: ref/()) -> ref/i32 during a => self.value@ref\n()";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.False(c.Binding.Result.IsComplete && c.Analysis.IsVerified); // R7-O1 H: the analysis rejects it (A5's record; Binding judges no body relation, F-H1-5).
        var error = Assert.Single(CompilationTestHelper.PublishedErrors(c));
        Assert.Equal(nameof(DiagnosticCode.UnprovenOriginRelation_Kd), error.Code);
        Assert.Equal("self.value@ref", error.Text);
        Assert.DoesNotContain(c.Problems, static x => x.Code is DiagnosticCode.Unsupported_Kd or DiagnosticCode.InternalInvariant_Kd);
    }

    [Fact]
    public void UserAbortKeepsItsImplementationLocationAndSkipsWriteback()
    {
        const string Source = "struct Value\n    Self is Addable<i32>\n    associate Output is Self\n    public func added(self: ref/Self, right: ref/i32) -> Self => $abort(\"operation\")\n    drop => Console.writeLine(\"unexpected cleanup\")\nvar value = Value.init()\nvalue += 1\nConsole.writeLine(\"unexpected continuation\")";
        ScalarEmissionTest.EmitFixture("UserArithmeticAbort", Source, string.Empty, 1, "Hello.kimi:4:66: abort KIMI_E_ABORT: operation\n");
    }

    [Fact]
    public void ResultOutputPropagatesOnlyThroughExplicitTry()
    {
        const string Source = "struct Value\n    Self is Dividable<i32>\n    associate Output is Result<i32, i32>\n    public func divided(self: ref/Self, right: ref/i32) -> Result<i32, i32>\n        require right != 0 else => return .Err(7)\n        return .Ok(84 / right)\nfunc divide(right: i32) -> Result<i32, i32>\n    let value = Value.init()\n    let result = try (value / right)\n    return .Ok(result)\nmatch divide(2)\n    .Ok(let value) => require value == 42 else => $abort(\"value\")\n    .Err(_) => $abort(\"unexpected failure\")\nmatch divide(0)\n    .Ok(_) => $abort(\"unexpected success\")\n    .Err(let value) => require value == 7 else => $abort(\"error\")";
        ScalarEmissionTest.EmitFixture("UserArithmeticResult", Source, string.Empty);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmOperatorPlansReuseTheirStorage()
    {
        var c = MinimalEmissionTest.Analyze(Declarations + "\nlet value = Value.init()\nlet added = value + 2\nlet scaled = 2.0 * value\nlet negative = -value");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Generate(TextWriter.Null)));
        Assert.True(valid);
    }
}
