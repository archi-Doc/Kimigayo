// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Verification;
using Xunit;

namespace XunitTest;

// PLAN G76: the blocking review cases, with expectations from SPEC 10.4, 10.5, 10.7 and 23.1.2.
public partial class ApplicabilityChainRegressionTest
{
    private const string ControlFlowDeclarations =
        "func identity<T>(value: T) -> T => value@move\nfunc keep<T>(value: T) -> T => value@move\nfunc stop() -> Never => $abort(\"stop\")\n" +
        "func take(value: i64) -> () => ()\nfunc choose(value: i32) -> () => ()\nfunc choose(value: i64) -> () => ()\n" +
        "func h(value: i8) -> () => ()\nfunc h(value: i64) -> () => ()\nfunc g(value: (i64, i64)) -> () => ()\nlet c = true\nlet x: i64 = 2\n" +
        "func both<T>(value: (T, T)) -> () => ()\nfunc first<T>(value: (T, i64)) -> () => ()\nfunc tuples(value: (i32, i32)) -> () => ()\n" +
        "func tuples(value: (i64, i64)) -> () => ()\nfunc takeArray(value: Array<i64>) -> () => ()\nfunc takeDict(value: Dictionary<string, (i64, i64)>) -> () => ()\n" +
        "func takeDictI(value: Dictionary<string, i64>) -> () => ()\nfunc takeFill(value: [2 of i64]) -> () => ()\n" +
        "func applyArg<F>(value: (F, i64)) -> ()\n    F is Callable<(i64) -> i64>\n    return ()\n" +
        "func applyOne<F>(value: F) -> ()\n    F is Callable<(i64) -> i64>\n    return ()\n" +
        "func takeF(value: (i64) -> i64) -> () => ()\nfunc takeFn(value: (() -> i64, i64)) -> () => ()\nfunc takeT(value: (i64, i64)) -> () => ()\n" +
        "func process<length N>(values: [N of i64]) -> () => ()\nlet r = x@ref\n" +
        "func takeD<T>(value: Dictionary<string, T>) -> () => ()\nfunc takeA<T>(value: Array<T>) -> () => ()\nfunc takeCI(value: (char, i64)) -> () => ()\n";

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void ResultOnlyItemOriginsFitFixedEvidence(bool callable, bool reversed, bool stored)
    {
        var parameters = callable ? "f: ref/F" : "f: () -> T";
        parameters = reversed ? "other: T, " + parameters : parameters + ", other: T";
        var declaration = callable
            ? $"func make<T, F>({parameters}) -> T\n    F is Callable<() -> T>\n    return other@move\n"
            : $"func make<T>({parameters}) -> T => other@move\n";
        var item = stored ? "c" : "constant";
        var arguments = reversed ? $"local@ref, {item}" : $"{item}, local@ref";
        var source = "func constant() -> ref/i32 during s => $abort(\"never\")\n" + declaration +
            "let local: i32 = 7\n" + (stored ? "let c = constant\n" : string.Empty) +
            $"let r = make({arguments})\nrequire r@follow == 7 else => $abort(\"fixed result\")";
        ScalarEmissionTest.EmitFixture($"ApplicabilityChainResult{callable}{reversed}{stored}", source, string.Empty);
    }

    [Theory]
    [InlineData("Forward", "func callIt(p: ref/i32, f: () -> ref/i32 during p) -> i32 => 2\nfunc g(p: ref/i32) -> i32 => callIt(p, constant)\n")]
    [InlineData("Reverse", "func callIt(f: () -> ref/i32 during p, p: ref/i32) -> i32 => 2\nfunc g(p: ref/i32) -> i32 => callIt(constant, p)\n")]
    [InlineData("Enclosing", "func make<T>(f: () -> T, other: T) -> T => other@move\nfunc g(p: ref/i32) -> i32 => make(constant, p)@follow\n")]
    [InlineData("Static", "func callIt(f: () -> ref/i32 during static) -> i32 => 2\nfunc g(p: ref/i32) -> i32 => callIt(constant)\n")]
    public void ResultOnlyOriginsFitPublishedParameters(string name, string declarations)
        => ScalarEmissionTest.EmitFixture("ApplicabilityChainPublished" + name, "func constant() -> ref/i32 during s => $abort(\"never\")\n" + declarations + "let local: i32 = 2\nrequire g(local@ref) == 2 else => $abort(\"published result\")", string.Empty);

    [Theory]
    [InlineData("Direct", "func keep(o: Option<i32>) -> i32 => 1\nrequire keep(make()) == 1 else => $abort(\"nested\")")]
    [InlineData("Local", "func keep(o: Option<i32>) -> i32 => 1\nlet k = keep(make())\nrequire k == 1 else => $abort(\"nested\")")]
    [InlineData("Parenthesized", "func keep(o: Option<i32>) -> i32 => 1\nrequire keep((make())) == 1 else => $abort(\"nested\")")]
    [InlineData("Ranked", "func keep(o: Option<i32>, tag: i32) -> i32 => 1\nfunc keep(o: Option<i64>, tag: i64) -> i32 => 2\nrequire keep(make(), 1@i32) == 1 else => $abort(\"nested\")")]
    [InlineData("Named", "func keep(tag: i32, o: Option<i32>) -> i32 => 1\nrequire keep(o: make(), tag: 1) == 1 else => $abort(\"nested\")")]
    [InlineData("Constructor", "struct K\n    public let o: Option<i32>\n    public init(o: Option<i32>) => self.o = o\nlet k = K.init(make())\nmatch k.o\n    .None => ()\n    .Some(let n) => $abort(\"nested\")")]
    [InlineData("Generic", "func keep<T>(o: Option<T>, value: T) -> T => value@move\nrequire keep(make(), 3@i64) == 3 else => $abort(\"nested\")")]
    [InlineData("Deep", "func also<T>(o: Option<T>) -> Option<T> => o@move\nfunc keep(o: Option<i64>) -> i32 => 1\nrequire keep(also(make())) == 1 else => $abort(\"nested\")")]
    [InlineData("Value", "func keep(o: Option<i64>) -> i32 => 1\nlet k: (Option<i64>) -> i32 = keep\nrequire k(make()) == 1 else => $abort(\"nested\")")]
    [InlineData("LiteralInner", "func identity<T>(value: T) -> T => value@move\nfunc take(value: i64) -> i32 => 1\nrequire take(identity(1)) == 1 else => $abort(\"nested\")")]
    [InlineData("LiteralExpected", "func identity<T>(value: T) -> T => value@move\nlet x: i64 = identity(identity(1))\nrequire x == 1 else => $abort(\"nested\")")]
    [InlineData("LiteralOperands", "func identity<T>(value: T) -> T => value@move\nrequire identity(1) + identity(2) == 3 else => $abort(\"operands\")")]
    [InlineData("OverloadedInner", "func inner(x: i32) -> Option<i32> => .Some(x)\nfunc inner(x: i64) -> Option<i64> => .Some(x)\nfunc keep(o: Option<i64>) -> i32 => 1\nrequire keep(inner(1)) == 1 else => $abort(\"nested\")")]
    public void ADeterminedOuterCandidateSuppliesTheNestedExpectation(string name, string body)
        => ScalarEmissionTest.EmitFixture("ApplicabilityChainNested" + name, "func make<T>() -> Option<T> => .None\n" + body, string.Empty);

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void AFailedCandidateSignatureExplainsTheDependentCall(bool reversed, bool differentTag)
    {
        const string Broken = "func f(x: Option<ref/i32>, y: i32) -> i32 => 1\n";
        var valid = $"func f(x: Option<ref/i32 during static>, y: {(differentTag ? "i64" : "i32")}) -> i32 => 3\n";
        var source = (reversed ? valid + Broken : Broken + valid) +
            "func g(p: ref/i32) -> i32 => f(.Some(p), 1)\npublic func main() -> () => ()\n";
        var result = DiagnosticCorpus.Check(source);
        Assert.False(result.Accepted);
        Assert.Equal(nameof(DiagnosticCode.MissingOriginBinding_Kd), Assert.Single(result.Diagnostics).Code);
        var repaired = source.Replace(Broken, string.Empty, StringComparison.Ordinal).Replace("g(p: ref/i32)", "g(p: ref/i32 during static)", StringComparison.Ordinal);
        Assert.True(DiagnosticCorpus.Check(repaired).Accepted);
    }

    [Fact]
    public void APerCallResultDoesNotMakeTheGenericCandidateBetter()
        => ScalarEmissionTest.EmitFixture("ApplicabilityChainRank", "func make<T>(action: (ref/i32) -> T) -> i32 => 1\nfunc make(action: (ref/i32) -> ref/i32) -> i32 => 2\nfunc pick(n: ref/i32) -> ref/i32 during n => n\nrequire make(pick) == 2 else => $abort(\"rank\")", string.Empty);

    [Fact]
    public void EqualGenericStructuresRemainAmbiguous()
    {
        const string Source = "func make<T>(action: (ref/i32) -> T) -> i32 => 1\nfunc make<U>(action: (ref/i32) -> ref/U) -> i32 => 2\n" +
            "func pick(n: ref/i32) -> ref/i32 during n => n\npublic func main() -> ()\n    let r = make(pick)\n";
        var error = Assert.Single(DiagnosticCorpus.Check(Source).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.AmbiguousBinding_Kd), error.Code);
        Assert.Equal("make(pick)", Source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        Assert.Equal(2, error.Related!.Length);
    }

    // SPEC 10.5, 10.5.1: no Cartesian search, no evidence from a waiting result, and no fallback after the chosen expectation fails; a
    // nested call whose result only a literal default would type, or with another applicable candidate, waits.
    [Theory]
    [InlineData("func make<T>() -> Option<T> => .None\nlet value = make()", nameof(DiagnosticCode.UnboundTypeArgument_Kd), "make()")]
    [InlineData("func make<T>() -> Option<T> => .None\nfunc keep<T>(o: Option<T>) => ()\nkeep(make())", nameof(DiagnosticCode.UnboundTypeArgument_Kd), "keep(make())")]
    [InlineData("func make<T>() -> Option<T> => .None\nfunc keep(o: Option<i32>) => ()\nfunc keep(o: Option<i64>) => ()\nkeep(make())", nameof(DiagnosticCode.AmbiguousBinding_Kd), "keep(make())")]
    [InlineData("func make<T>() -> Option<T>\n    T is PrimitiveInteger\n    return .None\nfunc keep(o: Option<string>, tag: i32) => ()\nfunc keep(o: Option<i32>, tag: i64) => ()\nkeep(make(), 1@i32)", nameof(DiagnosticCode.NoApplicableOverload_Kd), "make()")]
    [InlineData("func make<T>(value: i32) -> Option<T> => .None\nfunc keep(o: Option<i32>) => ()\nkeep(make(missing))", nameof(DiagnosticCode.UnresolvedBinding_Kd), "missing")]
    [InlineData("func identity<T>(value: T) -> T => value@move\nlet x = identity(identity(1))", nameof(DiagnosticCode.UnboundTypeArgument_Kd), "identity(identity(1))")]
    [InlineData("struct Box<T>\n    public let value: T\n    public init(value: T) => self.value = value@move\nfunc makeBox<T>(value: T) -> Box<T> => Box.init(value@move)\nfunc outer<T>(value: Box<T>) => ()\nouter(makeBox(1))", nameof(DiagnosticCode.UnboundTypeArgument_Kd), "outer(makeBox(1))")]
    [InlineData("struct Box<T>\n    public let value: T\n    public init(value: T) => self.value = value@move\nfunc mk() -> Box<i32> => Box.init(1@i32)\nfunc mk(seed: i32 = 0) -> Box<i64> => Box.init(2@i64)\nfunc outer<T>(value: Box<T>) => ()\nouter(mk())", nameof(DiagnosticCode.UnboundTypeArgument_Kd), "outer(mk())")]
    [InlineData("struct Box<T>\n    public let value: T\n    public init(value: T) => self.value = value@move\nlet a = Box.init(Box.init(42))", nameof(DiagnosticCode.UnboundTypeArgument_Kd), "Box.init(Box.init(42))")]
    public void NestedInferenceHasOneBoundary(string source, string code, string at)
    {
        var check = DiagnosticCorpus.Check(source);
        Assert.False(check.Accepted);
        var error = Assert.Single(check.Diagnostics);
        Assert.Equal((code, at), (error.Code, source.Substring(error.Span!.Value.Start, error.Span.Value.Length)));
        if (code == nameof(DiagnosticCode.UnboundTypeArgument_Kd))
        {
            Assert.Equal("Generic parameter 'T' is not bound", error.Label);
            Assert.Contains("No explicit Type argument or evidence binds T", error.Note, StringComparison.Ordinal);
        }
    }

    // SPEC 10.5.1, 14.9.2 (owner decision (1)): a control-flow argument that is no Literal is bound under the argument probe, which
    // reaches its result sources: literals beside only Never or waiting sources, or a waiting source call, make it Waiting, checked
    // against the selected candidate's Type; a typed source makes it independently typable.
    [Theory]
    [InlineData("take(if c => 1 else => stop())")]
    [InlineData("take(if c => identity(1) else => 2)")]
    [InlineData("take(if c => identity(1) else => identity(2))")]
    [InlineData("take(if c => 1 else => x)")]
    [InlineData("let k: i64 = keep(if c => identity(1) else => x)")]
    public void AControlFlowArgumentIsClassifiedByItsResultSources(string use)
    {
        var c = MinimalEmissionTest.Analyze(ControlFlowDeclarations + use);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
    }

    // SPEC 10.5.1 (the owner, 2026-10-26; DECISIONS BU-43): a control-flow argument is Literal when every result source other than Never
    // is a literal, Never supplying nothing at any position, and its default applies after selection; a join of Tuple or array results
    // that supplies no candidate is classified position by position, as a Tuple argument's elements are: a typed position is evidence at
    // its common Type, a literal one takes its default after selection, one Never in every source is none, and the selected candidate's
    // Type completes it. A join of Dictionary results is typed as a whole, as a Dictionary literal argument is.
    [Theory]
    [InlineData("keep(if c => (x, stop()) else => (stop(), x))", "(i64, i64)")]
    [InlineData("keep(if c => (x, stop()) else => (stop(), 5))", "(i64, i32)")]
    [InlineData("keep(if c => (x, stop()) else => (x, 5))", "(i64, i32)")]
    [InlineData("keep(if c => (5, 6) else => (7, 8))", "(i32, i32)")]
    [InlineData("keep(if c => [1, 2] else => [stop()])", "Array<i32>")]
    [InlineData("let o = Option.Some(if c => (5, 6) else => (7, 8))", "(i32, i32)")]
    [InlineData("g(if c => (x, stop()) else => (stop(), 5))", "(i64, i64)")]
    [InlineData("g(if c => (x, stop()) else => (x, 5))", "(i64, i64)")]
    [InlineData("g(if c => (5, 6) else => (7, 8))", "(i64, i64)")]
    [InlineData("g(if c => (x, stop()) else => (x, stop()))", "(i64, i64)")]
    [InlineData("takeArray(if c => [1, 2] else => [stop()])", "Array<i64>")]
    [InlineData("takeFill(if c => [2 of 5] else => [2 of stop()])", "[2 of i64]")]
    [InlineData("takeT(if c => (r, stop()) else => (x, stop()))", "(i64, i64)")]
    [InlineData("takeFn(if c => (func [x] () => x, stop()) else => (stop(), x))", "(() -> i64, i64)")]
    [InlineData("process(if c => [x, stop()] else => [stop(), x])", "[2 of i64]")]
    [InlineData("both(if c => (x, stop()) else => (stop(), 5))", "(i64, i64)")]
    [InlineData("both(if c => (x, stop()) else => (x, stop()))", "(i64, i64)")]
    [InlineData("first(if c => (5, stop()) else => (6, x))", "(i32, i64)")]
    [InlineData("keep((if c => (5, 6) else => (7, 8), x))", "(i32, i32)")]
    [InlineData("keep(if c => 5 else => stop())", "i32")]
    [InlineData("h(if c => 200 else => stop())", "i64")]
    [InlineData("keep(if c => [\"k\": (x, stop())] else => [\"k\": (stop(), x)])", "Dictionary<string, (i64, i64)>")]
    [InlineData("takeDict(if c => [\"k\": (x, stop())] else => [\"k\": (stop(), x)])", "Dictionary<string, (i64, i64)>")]
    [InlineData("keep(if c => [\"k\": 5] else => [\"k\": stop()])", "Dictionary<string, i32>")]
    public void AControlFlowArgumentsResultsAreClassifiedByPosition(string use, string type)
    {
        var c = MinimalEmissionTest.Analyze(ControlFlowDeclarations + use);
        Assert.Empty(c.Binding.Issues);
        var join = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<IfKoto>().Last();
        Assert.Equal(type, TyCtxt.Name(join.TypeOf()!));
    }

    // SPEC 10.5.1, 10.5.4 (DECISIONS BU-43): an anonymous function among a control-flow argument's results waits for the call, which
    // completes it as it completes a waiting argument at its position: at F it binds F, else it is checked against the selected
    // candidate's Type. A source holding it beside typed parts, also through a nested selection, waits with it and then takes the Type
    // its join forms part by part (SPEC 14.9.2 steps 2-3), a value read included. In a Dictionary literal, which is typed as a whole,
    // it does not wait, as in a Dictionary literal argument (DR3 C1).
    [Theory]
    [InlineData("applyArg(if c => (func [] (v) => v, stop()) else => (stop(), x))")]
    [InlineData("applyOne(if c => func [] (v) => v else => stop())")]
    [InlineData("applyOne(if c => func [] (v: i64) -> i64 => v else => stop())")]
    [InlineData("applyOne(if c => (if c => func [] (v) => v else => stop()) else => stop())")]
    [InlineData("applyOne(if c => (if c => func [] (v: i64) -> i64 => v else => stop()) else => stop())")]
    [InlineData("keep(if c => func [] (v: i64) -> i64 => v else => stop())")]
    [InlineData("takeF(if c => func [] (v) => v else => stop())")]
    [InlineData("takeF(if c => func [] (v) => v else => func [] (w) => w)")]
    [InlineData("takeF(if c => func [] (v: i64) -> i64 => v else => func [] (w: i64) -> i64 => w)")]
    [InlineData("takeFn(if c => (func [x] () => x, x) else => (func [x] () => x + 1, x))")]
    [InlineData("first(if c => (func [] (v: i64) -> i64 => v, x) else => (stop(), stop()))")]
    [InlineData("first(if c => (func [] (v: i64) -> i64 => v, x) else => (stop(), x))")]
    [InlineData("first(if c => (func [x] () -> i64 => x, x) else => (stop(), x))")]
    [InlineData("first(if c => (func [] (v: i64) -> i64 => v, r) else => (stop(), x))")]
    [InlineData("first(if c => (func [] (v: i64) -> i64 => v, if c => x else => stop()) else => (stop(), x))")]
    [InlineData("first(if c => (if c => (func [] (v: i64) -> i64 => v, x) else => (stop(), x)) else => (stop(), x))")]
    [InlineData("applyArg(if c => (func [] (v) => v, x) else => (stop(), x))")]
    [InlineData("applyArg(if c => (func [] (v: i64) -> i64 => v, x) else => (stop(), x))")]
    [InlineData("takeD(if c => [\"k\": func [] (v: i64) -> i64 => v] else => stop())")]
    [InlineData("takeD([\"k\": func [] (v: i64) -> i64 => v])")]
    [InlineData("first(if c => ([\"k\": func [] (v: i64) -> i64 => v], x) else => (stop(), x))")]
    [InlineData("takeA(if c => [func [] (v: i64) -> i64 => v] else => stop())")]
    [InlineData("first(if c => (if c => (func [] (v: i64) -> i64 => v, x) else => (stop(), 5)) else => (stop(), 5))")]
    public void AnAnonymousFunctionAmongAControlFlowArgumentsResultsWaitsForTheCall(string use)
    {
        var c = MinimalEmissionTest.Analyze(ControlFlowDeclarations + use);
        Assert.Empty(c.Binding.Issues);
        Assert.All(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<IfKoto>(), join => Assert.NotNull(join.TypeOf())); // Each join is decided.
    }

    // SPEC 10.5.1, 10.4: a Literal or waiting control-flow argument is no evidence and ranks equally at every candidate it fits; the
    // Literal fits a candidate only when every source does. Its results classified by position (the owner, 2026-10-26), a join ranks as
    // the Tuple argument of those positions does, a position Never in every source giving no Type at a slot that takes the whole.
    [Theory]
    [InlineData("choose(if c => 1 else => 2)", nameof(DiagnosticCode.AmbiguousBinding_Kd), "choose(if c => 1 else => 2)")]
    [InlineData("h(if c => 100 else => stop())", nameof(DiagnosticCode.AmbiguousBinding_Kd), "h(if c => 100 else => stop())")]
    [InlineData("g((if c => 1 else => 2.0, x))", nameof(DiagnosticCode.NoApplicableOverload_Kd), "g((if c => 1 else => 2.0, x))")]
    [InlineData("tuples(if c => (5, 6) else => (7, 8))", nameof(DiagnosticCode.AmbiguousBinding_Kd), "tuples(if c => (5, 6) else => (7, 8))")]
    [InlineData("tuples(if c => (stop(), 5) else => (6, stop()))", nameof(DiagnosticCode.AmbiguousBinding_Kd), "tuples(if c => (stop(), 5) else => (6, stop()))")]
    [InlineData("keep(if c => (x, stop()) else => (x, stop()))", nameof(DiagnosticCode.UnboundTypeArgument_Kd), "keep(if c => (x, stop()) else => (x, stop()))")]
    [InlineData("keep(if c => (5, stop()) else => (6, stop()))", nameof(DiagnosticCode.UnboundTypeArgument_Kd), "keep(if c => (5, stop()) else => (6, stop()))")]
    [InlineData("takeDictI(if c => [\"k\": 5] else => [\"k\": stop()])", nameof(DiagnosticCode.NoApplicableOverload_Kd), "takeDictI(if c => [\"k\": 5] else => [\"k\": stop()])")]
    [InlineData("applyArg(if c => (func [] (v: bool) => v, stop()) else => (stop(), x))", nameof(DiagnosticCode.NoApplicableOverload_Kd), "applyArg(if c => (func [] (v: bool) => v, stop()) else => (stop(), x))")]
    [InlineData("takeF(if c => func [] (v: bool) => v else => stop())", nameof(DiagnosticCode.NoApplicableOverload_Kd), "takeF(if c => func [] (v: bool) => v else => stop())")]
    [InlineData("applyArg(if c => func [] (v) => v else => stop())", nameof(DiagnosticCode.UnboundTypeArgument_Kd), "applyArg(if c => func [] (v) => v else => stop())")]
    [InlineData("applyArg(func [] (v) => v)", nameof(DiagnosticCode.UnboundTypeArgument_Kd), "applyArg(func [] (v) => v)")]
    [InlineData("takeT(if c => (r, stop()) else => (true, stop()))", nameof(DiagnosticCode.TypeMismatch_Kd), "if c => (r, stop()) else => (true, stop())")]
    [InlineData("process(if c => [x, stop()] else => [stop(), x, x])", nameof(DiagnosticCode.NoApplicableOverload_Kd), "process(if c => [x, stop()] else => [stop(), x, x])")]
    [InlineData("first(if c => (func [] (v: i64) -> i64 => v, x) else => (func [] (w: i64) -> i64 => w, x))", nameof(DiagnosticCode.TypeMismatch_Kd), "if c => (func [] (v: i64) -> i64 => v, x) else => (func [] (w: i64) -> i64 => w, x)")]
    [InlineData("applyArg(if c => (func [] (v) => v, x) else => (func [] (w) => w, x))", nameof(DiagnosticCode.TypeMismatch_Kd), "if c => (func [] (v) => v, x) else => (func [] (w) => w, x)")]
    [InlineData("first(if c => (func [] (v: i64) -> i64 => v, x) else => (stop(), true))", nameof(DiagnosticCode.TypeMismatch_Kd), "if c => (func [] (v: i64) -> i64 => v, x) else => (stop(), true)")]
    [InlineData("first(if c => (func [] (v: i64) -> i64 => v, r) else => (stop(), true))", nameof(DiagnosticCode.TypeMismatch_Kd), "if c => (func [] (v: i64) -> i64 => v, r) else => (stop(), true)")]
    [InlineData("applyArg(if c => (func [] (v: bool) => v, x) else => (stop(), x))", nameof(DiagnosticCode.NoApplicableOverload_Kd), "applyArg(if c => (func [] (v: bool) => v, x) else => (stop(), x))")]
    [InlineData("keep(if c => (func [] (v: i64) -> i64 => v, x) else => (stop(), x))", nameof(DiagnosticCode.UnboundTypeArgument_Kd), "keep(if c => (func [] (v: i64) -> i64 => v, x) else => (stop(), x))")]
    [InlineData("keep((func [] (v: i64) -> i64 => v, x))", nameof(DiagnosticCode.UnboundTypeArgument_Kd), "keep((func [] (v: i64) -> i64 => v, x))")]
    [InlineData("first(if c => (if c => (5, x) else => (stop(), true)) else => (stop(), x))", nameof(DiagnosticCode.TypeMismatch_Kd), "if c => (5, x) else => (stop(), true)")]
    [InlineData("first(if c => (if c => (func [] (v: i64) -> i64 => v, x) else => (stop(), true)) else => (stop(), x))", nameof(DiagnosticCode.TypeMismatch_Kd), "if c => (func [] (v: i64) -> i64 => v, x) else => (stop(), true)")]
    [InlineData("first(if c => (func [] (v: i64) -> i64 => v, 5) else => (func [] (w: i64) -> i64 => w, 2.5))", nameof(DiagnosticCode.TypeMismatch_Kd), "if c => (func [] (v: i64) -> i64 => v, 5) else => (func [] (w: i64) -> i64 => w, 2.5)")]
    [InlineData("first(if c => (if c => (func [] (v: i64) -> i64 => v, 5) else => (func [] (w: i64) -> i64 => w, 6)) else => (stop(), x))", nameof(DiagnosticCode.TypeMismatch_Kd), "if c => (func [] (v: i64) -> i64 => v, 5) else => (func [] (w: i64) -> i64 => w, 6)")]
    [InlineData("first(if c => (if c => (func [] (v: i64) -> i64 => v, x) else => (func [] (w: i64) -> i64 => w, x)) else => (stop(), x))", nameof(DiagnosticCode.TypeMismatch_Kd), "if c => (func [] (v: i64) -> i64 => v, x) else => (func [] (w: i64) -> i64 => w, x)")]
    [InlineData("applyArg(if c => (if c => (func [] (v) => v, x) else => (func [] (w) => w, x)) else => (stop(), x))", nameof(DiagnosticCode.TypeMismatch_Kd), "if c => (func [] (v) => v, x) else => (func [] (w) => w, x)")]
    [InlineData("first(if c => (if c => (func [] (v: i64) -> i64 => v, x) else => (func [] (w: i64) -> i64 => w, stop())) else => (stop(), x))", nameof(DiagnosticCode.TypeMismatch_Kd), "if c => (func [] (v: i64) -> i64 => v, x) else => (func [] (w: i64) -> i64 => w, stop())")]
    [InlineData("first(if c => (if c => (if c => (func [] (v: i64) -> i64 => v, x) else => (func [] (w: i64) -> i64 => w, x)) else => (stop(), x)) else => (stop(), x))", nameof(DiagnosticCode.TypeMismatch_Kd), "if c => (func [] (v: i64) -> i64 => v, x) else => (func [] (w: i64) -> i64 => w, x)")]
    [InlineData("first(if c => ((if c => func [] (v: i64) -> i64 => v else => func [] (w: i64) -> i64 => w), x) else => (stop(), x))", nameof(DiagnosticCode.TypeMismatch_Kd), "if c => func [] (v: i64) -> i64 => v else => func [] (w: i64) -> i64 => w")]
    [InlineData("takeD(if c => (if c => [\"k\": missing] else => [\"k\": stop()]) else => [\"k\": x])", nameof(DiagnosticCode.UnresolvedBinding_Kd), "missing")]
    [InlineData("applyArg(([\"k\": func [] (v) => v], x))", nameof(DiagnosticCode.UnresolvedBinding_Kd), "v")]
    [InlineData("applyArg(if c => ([\"k\": func [] (v: i64) -> i64 => v], x) else => (stop(), x))", nameof(DiagnosticCode.NoApplicableOverload_Kd), "applyArg(if c => ([\"k\": func [] (v: i64) -> i64 => v], x) else => (stop(), x))")]
    public void AControlFlowArgumentRanksAsItsKind(string use, string code, string at)
    {
        var source = ControlFlowDeclarations + use + "\n";
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((code, at), (error.Code, source.Substring(error.Span!.Value.Start, error.Span.Value.Length)));
    }

    // The same ranking for a closure position whose result mismatches (`true`), and, as an independent failure (SPEC 23.1.2; M's Q13
    // call on PLAN G442), A1's record where takeFn receives the join's value: no path reaches the call, and the first arm leaves its
    // Never part unplaced (SPEC 14.10.3, 15.1.3; the owner's O-99 (a)).
    [Fact]
    public void AControlFlowArgumentRanksAsItsKindBesideItsUnplacedPart()
    {
        const string use = "takeFn(if c => (func [x] () => true, stop()) else => (stop(), x))";
        var source = ControlFlowDeclarations + use + "\n";
        Assert.Equal(
            [(nameof(DiagnosticCode.UninitializedPlace_Kd), use), (nameof(DiagnosticCode.TypeMismatch_Kd), "true")],
            DiagnosticCorpus.Check(source).Diagnostics.Select(x => (x.Code, source.Substring(x.Span!.Value.Start, x.Span.Value.Length))));
    }

    // SPEC 23.1.2 (DECISIONS BU-79, rule W; the owner, 2026-11-02): a failed part is a wildcard and no evidence, so beside the
    // closure's own record (`v`) the call reports F unbound, a dependent record SPEC 23.1.2 allows. The Dictionary held in the join's
    // Tuple no longer rests F on its failed part (U1.6h-3' fix (b), superseded).
    [Fact]
    public void AFailedPartIsAWildcardAndTheCallsOwnRecordIsReported()
    {
        var source = ControlFlowDeclarations + "applyArg(if c => ([\"k\": func [] (v) => v], x) else => (stop(), x))\n";
        var shown = DiagnosticCorpus.Check(source).Diagnostics.Select(x => x.Code + " " + source.Substring(x.Span!.Value.Start, x.Span.Value.Length)).Order();
        Assert.Equal(["UnboundTypeArgument_Kd applyArg(if c => ([\"k\": func [] (v) => v], x) else => (stop(), x))", "UnresolvedBinding_Kd v"], shown);
    }

    // SPEC 23.1.2, 10.4 (U1.6h-3'): a join of Tuple results fits `take(i64)` whatever its failed part is, so the call's
    // NoApplicableOverload_Kd is independent and stands beside `missing`'s record (formerly `missing`'s alone, a dependent suppression);
    // its twin with `x` for `missing` gives the call's record alone.
    [Theory]
    [InlineData("take(if c => ((if c => missing else => x), stop()) else => (stop(), x))", new[] { nameof(DiagnosticCode.NoApplicableOverload_Kd), nameof(DiagnosticCode.UnresolvedBinding_Kd) })]
    [InlineData("take(if c => ((if c => x else => x), stop()) else => (stop(), x))", new[] { nameof(DiagnosticCode.NoApplicableOverload_Kd) })]
    public void ATupleJoinFitsNoScalarParameterWhateverItsFailedPartIs(string use, string[] codes)
    {
        var source = ControlFlowDeclarations + use + "\n";
        Assert.Equal(codes, DiagnosticCorpus.Check(source).Diagnostics.Select(static x => x.Code).Order());
    }

    // SPEC 23.1.2 (DECISIONS BU-43): an unreadable character literal in a call's join is bound as `char`, so the call keeps its own record
    // beside the lexer's, as its Tuple twin does.
    [Theory]
    [InlineData("takeCI(if c => ('ab', stop()) else => (stop(), true))")]
    [InlineData("takeCI(('ab', true))")]
    public void AnUnreadableLiteralInAJoinKeepsItsCallsRecord(string use)
    {
        var source = ControlFlowDeclarations + use + "\n";
        Assert.Contains(DiagnosticCorpus.Check(source).Diagnostics, static x => x.Code == nameof(DiagnosticCode.NoApplicableOverload_Kd));
    }

    // SPEC 23.1.2 (DECISIONS BU-43): in a call's join, a defaults conflict of a nested join is reported beside an independent failure of
    // the enclosing join, as outside a call.
    [Fact]
    public void AWaitingJoinsOwnConflictInACallIsReportedBesideAnotherFailure()
    {
        var source = ControlFlowDeclarations + "takeT(if c => (if c => (5, x) else => (2.5, stop())) else => (missing, x))\n";
        var codes = DiagnosticCorpus.Check(source).Diagnostics.Select(static x => x.Code).Order();
        Assert.Equal([nameof(DiagnosticCode.TypeMismatch_Kd), nameof(DiagnosticCode.UnresolvedBinding_Kd)], codes);
    }

    // SPEC 23.1.2 (DECISIONS BU-43, BU-79): at a call that selects none, a held join's own Conflict is its one record, and the enclosing
    // join rests on it (its own verdict over a failed child). Under rule W (the owner, 2026-11-02) a waited source of the failed join is
    // linked to nothing: U1.6h-3' fix (a)'s links are superseded. A source bound before the join decides, `["k": stop()]`, rests on
    // nothing.
    [Fact]
    public void AHeldJoinsOwnConflictIsItsCallsOneRecord()
    {
        var c = CompilationTestHelper.ParseSuccess(ControlFlowDeclarations +
            "first(if c => (if c => (func [] (v: i64) -> i64 => v, x) else => (func [] (w: i64) -> i64 => w, x)) else => (stop(), x))\n");
        c.Binding.Bind(BindingMode.Final);
        var joins = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<IfKoto>().ToArray();
        Assert.Same(joins[1], Assert.Single(c.Binding.Issues).Node);
        var source = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<TupleLiteralKoto>().First(x => x.Elements[0] is FunctionKoto).Parent!; // The expression body.
        Assert.DoesNotContain(c.Binding.FailureCauses(source) ?? [], x => ReferenceEquals(x.Subject, joins[1]));
        Assert.Same(joins[1], Assert.Single(c.Binding.FailureCauses(joins[0])!).Subject);
        var dictionaries = MinimalEmissionTest.Analyze(ControlFlowDeclarations +
            "takeD(if c => (if c => [\"k\": missing] else => [\"k\": stop()]) else => [\"k\": x])\n");
        var dictionary = KotoTree.Walk(dictionaries.Kotonoha.RootKoto).OfType<DictionaryLiteralKoto>().Single(x => x.ToString() == "[\"k\": stop()]");
        Assert.Null(dictionaries.Binding.FailureCauses(dictionary));
    }

    // SPEC 23.1.2, 10.1: a call that selects no candidate, or whose selected candidate fails, still binds its Literal and waiting
    // control-flow parts on their own, so their own problems are reported beside the call's record, once; nothing is selected again.
    [Theory]
    [InlineData("choose(if ready => 1 else => 0)", nameof(DiagnosticCode.AmbiguousBinding_Kd), "choose(if ready => 1 else => 0)")]
    [InlineData("missingCall(if ready => 1 else => 0)", nameof(DiagnosticCode.UnresolvedBinding_Kd), "missingCall")]
    [InlineData("choose(identity(if ready => 1 else => 0))", nameof(DiagnosticCode.AmbiguousBinding_Kd), "choose(identity(if ready => 1 else => 0))")]
    [InlineData("pair((if ready => 1 else => 2, 3))", nameof(DiagnosticCode.AmbiguousBinding_Kd), "pair((if ready => 1 else => 2, 3))")]
    public void AFailedCallStillReportsItsPartsOwnProblems(string use, string code, string at)
    {
        var source = ControlFlowDeclarations + "func pair(value: (i32, i32)) -> () => ()\nfunc pair(value: (i64, i64)) -> () => ()\n" + use + "\n";
        var found = DiagnosticCorpus.Check(source).Diagnostics.Select(x => (x.Code, source.Substring(x.Span!.Value.Start, x.Span.Value.Length)));
        Assert.Equal([(code, at), (nameof(DiagnosticCode.UnresolvedBinding_Kd), "ready")], found);
    }

    // SPEC 23.1.2, 10.1 (DECISIONS U4b, PLAN G421): a call or Case construction that stops before some of its parts (a value call's
    // failed callee, explicit Type arguments, arity or label, an earlier failed argument; a construction's earlier failed payload) binds
    // each of them on its own, a nested call that defers there included, outside its consultation: its own problems are reported, and
    // the call's own record (a payload callee's located Unsupported_Kd) rests on none of them, nor on a part its completion already
    // reached (a contextual Case's payload, a control-flow part, a Tuple element). A named or method call prepares every argument
    // first. Each row: Binding's errors.
    [Theory]
    [InlineData("func call(r: ref/(rc/((ref/i32) -> ()))) -> () => r@follow(undefinedName)\n", "Unsupported_Kd r@follow; UnresolvedBinding_Kd undefinedName")]
    [InlineData("func call(r: ref/(rc/((ref/i32) -> ()))) -> () => r@follow(1 + true)\n", "Unsupported_Kd r@follow; NonNumericOperand_Kd 1 + true")]
    [InlineData("func call(r: ref/(rc/((ref/i32) -> ())), p: ref/i32) -> () => r@follow(p)\n", "Unsupported_Kd r@follow")]
    [InlineData("func call(r: ref/(rc/((Option<i32>) -> ()))) -> () => r@follow(.Some(undefinedName))\n", "Unsupported_Kd r@follow; UnresolvedBinding_Kd undefinedName")]
    [InlineData("func call(c: bool, r: ref/(rc/((i32) -> ()))) -> () => r@follow(if c => undefinedName else => 1)\n", "Unsupported_Kd r@follow; UnresolvedBinding_Kd undefinedName")]
    [InlineData("func call(r: ref/(rc/((Option<i32>) -> i32))) -> i32\n    let v = r@follow(.Some(undefinedName))\n    return v + 1\n", "Unsupported_Kd r@follow; UnresolvedBinding_Kd undefinedName")]
    [InlineData("func call(r: ref/(rc/(((i32, i32)) -> ()))) -> () => r@follow((undefinedName, 1))\n", "Unsupported_Kd r@follow; UnresolvedBinding_Kd undefinedName")]
    [InlineData("func f() -> ()\n    let t: string = \"a\"\n    let c = func [t@move] (w: ref/i32) -> ()\n        let u = t@move\n        return\n    c(undefinedName)\n", "TransferRequired_Kd c; UnresolvedBinding_Kd undefinedName")]
    [InlineData("func f(g: (i32, i32) -> ()) -> ()\n    g(undefinedName)\n", "NoApplicableOverload_Kd g(undefinedName); UnresolvedBinding_Kd undefinedName")]
    [InlineData("func f(g: (i32, i32) -> ()) -> ()\n    g(undefinedName, 1 + true)\n", "UnresolvedBinding_Kd undefinedName; NonNumericOperand_Kd 1 + true")]
    [InlineData("func f(g: (i32) -> ()) -> ()\n    g(v: 1 + true)\n", "NoApplicableOverload_Kd g(v: 1 + true); NonNumericOperand_Kd 1 + true")]
    [InlineData("func f(g: (i32) -> ()) -> ()\n    g<i32>(1 + true)\n", "NoApplicableOverload_Kd g<i32>(1 + true); NonNumericOperand_Kd 1 + true")]
    [InlineData("func f(g: (i32, i32) -> ()) -> ()\n    g(1 + true, undefinedName)\n", "NonNumericOperand_Kd 1 + true; UnresolvedBinding_Kd undefinedName")]
    [InlineData("enum E\n    A(i32, i32)\nfunc f() -> E\n    return .A(1 + true, undefinedName)\n", "NonNumericOperand_Kd 1 + true; UnresolvedBinding_Kd undefinedName")]
    [InlineData("enum E\n    A(i32, i32)\nfunc f() -> E\n    return E.A(true, undefinedName)\n", "TypeMismatch_Kd E.A(true, undefinedName); UnresolvedBinding_Kd undefinedName")]
    [InlineData("enum E\n    A(i32)\nfunc f() -> E\n    return E.A<i32>(1 + true)\n", "NotCallable_Kd E.A<i32>(1 + true); NonNumericOperand_Kd 1 + true")]
    [InlineData("func wrap<T>(v: Option<i32>) -> T => $abort(\"x\")\nfunc f(g: (i32, i32) -> ()) -> ()\n    g(undefinedName, wrap(.Some(undefinedName2)))\n", "UnresolvedBinding_Kd undefinedName; UnresolvedBinding_Kd undefinedName2")]
    [InlineData("func wrap<T>(v: Option<i32>) -> T => $abort(\"x\")\nfunc take(v: i32, w: i32) -> () => ()\nfunc f() -> ()\n    take(undefinedName, wrap(.Some(undefinedName2)))\n", "UnresolvedBinding_Kd undefinedName; UnresolvedBinding_Kd undefinedName2")]
    [InlineData("func take(v: i32, w: i32) -> () => ()\nfunc f() -> ()\n    take(undefinedName, 1 + true)\n", "UnresolvedBinding_Kd undefinedName; NonNumericOperand_Kd 1 + true")]
    [InlineData("struct H\n    public var n: i32\n    public init(n: i32) => self.n = n\n    public func m(self: ref/Self, v: i32, w: i32) -> () => ()\nfunc f(h: ref/H) -> ()\n    h.m(undefinedName, 1 + true)\n", "UnresolvedBinding_Kd undefinedName; NonNumericOperand_Kd 1 + true")]
    public void AStoppedCallBindsTheRestOfItsPartsOnTheirOwn(string source, string errors)
    {
        var c = CompilationTestHelper.ParseSuccess(source);
        c.Bind();
        Assert.Equal(errors, string.Join("; ", CompilationTestHelper.BindingErrors(c).Select(x => $"{x.Code} {source.Substring(x.Span.Start, x.Span.Length)}")));
    }

    // SPEC 10.1, 10.1.1, 7.4.4, 23.1.2 (DECISIONS BU-79's G455 amendment, PLAN G500): beside an unprepared argument (an unbound failed
    // part) a named, method, constructor or constraint-based value call runs only the checks SPEC 10.1 makes before inference, which
    // read no argument's Type: the acquisition-shape contracts (7.4.4), the explicit Type arguments' own records, and NoApplicable when
    // no candidate passes step 1 (the explicit Type arguments' count and kinds) and step 2 (argument matching, 10.1.1's `!` boundary
    // included). Every other outcome rests on the part, in either argument order (the rows after `pair<3, Undefined>(5)`); the records
    // lost so are PLAN G500's. Step 1 judges every kind before a failed Type argument rests (`pair<Undefined, 3>(5)`, no unprepared
    // argument). A plain value call judges MapValueCallArguments before any part (PLAN G494 unit 2), so `g(undefinedName, v: 1)`
    // reports its NoApplicable as `g(v: 1, undefinedName)` does. Each row: Binding's errors.
    [Theory]
    [InlineData("func take(v: i32, w: i32) -> () => ()\nfunc f() -> ()\n    take(undefinedName)\n", "NoApplicableOverload_Kd take(undefinedName); UnresolvedBinding_Kd undefinedName")]
    [InlineData("func take1(v: i32) -> () => ()\nfunc f() -> ()\n    take1(undefinedName, 2)\n", "NoApplicableOverload_Kd take1(undefinedName, 2); UnresolvedBinding_Kd undefinedName")]
    [InlineData("func take1(v: i32) -> () => ()\nfunc f() -> ()\n    take1(1, undefinedName)\n", "NoApplicableOverload_Kd take1(1, undefinedName); UnresolvedBinding_Kd undefinedName")]
    [InlineData("func take(v: i32, w: i32) -> () => ()\nfunc f() -> ()\n    take(w: undefinedName)\n", "NoApplicableOverload_Kd take(w: undefinedName); UnresolvedBinding_Kd undefinedName")]
    [InlineData("func takeD(v: i32, w: i32, z: i32 = 0) -> () => ()\nfunc f() -> ()\n    takeD(undefinedName)\n", "NoApplicableOverload_Kd takeD(undefinedName); UnresolvedBinding_Kd undefinedName")]
    [InlineData("func takeG<T>(v: T, w: T) -> () => ()\nfunc f() -> ()\n    takeG(undefinedName)\n", "NoApplicableOverload_Kd takeG(undefinedName); UnresolvedBinding_Kd undefinedName")]
    [InlineData("struct H\n    public var n: i32\n    public init(n: i32) => self.n = n\n    public func m(self: ref/Self, v: i32, w: i32) -> () => ()\nfunc f(h: ref/H) -> ()\n    h.m(undefinedName)\n", "NoApplicableOverload_Kd h.m(undefinedName); UnresolvedBinding_Kd undefinedName")]
    [InlineData("func take(v: i32, w: i32) -> () => ()\nfunc take(v: i64, w: i64, x: i64) -> () => ()\nfunc f() -> ()\n    take(undefinedName)\n", "NoApplicableOverload_Kd take(undefinedName); UnresolvedBinding_Kd undefinedName")]
    [InlineData("func other(v: i32) -> i32 => v\nfunc take(v: i32, w: i32) -> () => ()\nfunc f() -> ()\n    take(other(true))\n", "NoApplicableOverload_Kd take(other(true)); NoApplicableOverload_Kd other(true)")]
    [InlineData("func take(v: (i32, i32), w: i32) -> () => ()\nfunc f() -> ()\n    take((undefinedName, 1))\n", "NoApplicableOverload_Kd take((undefinedName, 1)); UnresolvedBinding_Kd undefinedName")]
    [InlineData("func both<F>(action: ref/F) -> i32\n    F is Callable<(i32, i32) -> i32>\n    F is Callable<(i64, i64) -> i32>\n    return action(undefinedName)\n", "NoApplicableOverload_Kd action(undefinedName); UnresolvedBinding_Kd undefinedName")]
    [InlineData("struct P\n    public var a: i32\n    public var b: i32\n    public init(a: i32, b: i32)\n        self.a = a\n        self.b = b\nfunc k() -> P\n    return P.init(undefinedName)\n", "NoApplicableOverload_Kd P.init(undefinedName); UnresolvedBinding_Kd undefinedName")]
    [InlineData("struct C<T>\n    public init(value: (T, i32), code: i32) => ()\n    public init(value: ref/(i64, T), name: string) => ()\nfunc k() -> ()\n    let c = C.init(undefinedName, 0)\n", "UnprovenAcquisitionCorrelation_Kd C.init(undefinedName, 0); UnresolvedBinding_Kd undefinedName")]
    [InlineData("struct C<T>\n    public init(value: (T, i32), code: i32) => ()\n    public init(value: ref/(i64, T), name: string) => ()\nfunc k() -> ()\n    let c = C.init(undefinedName)\n", "UnprovenAcquisitionCorrelation_Kd C.init(undefinedName); UnresolvedBinding_Kd undefinedName")]
    [InlineData("func takeG<T>(v: T, w: T) -> () => ()\nfunc f() -> ()\n    takeG<Undefined>(undefinedName)\n", "UnresolvedBinding_Kd Undefined; UnresolvedBinding_Kd undefinedName")]
    [InlineData("func takeG<T>(v: T, w: T) -> () => ()\nfunc f() -> ()\n    takeG<i32, i32>(undefinedName, 1)\n", "NoApplicableOverload_Kd takeG<i32, i32>(undefinedName, 1); UnresolvedBinding_Kd undefinedName")]
    [InlineData("func takeG<T>(v: T, w: T) -> () => ()\nfunc f() -> ()\n    takeG<3>(undefinedName, 1)\n", "NoApplicableOverload_Kd takeG<3>(undefinedName, 1); UnresolvedBinding_Kd undefinedName")]
    [InlineData("func pair<T, U>(v: T) -> () => ()\nfunc f() -> ()\n    pair<Undefined, 3>(5)\n", "NoApplicableOverload_Kd pair<Undefined, 3>(5); UnresolvedBinding_Kd Undefined")]
    [InlineData("struct Item\n    public var value: i32\n    public init(value: i32) => self.value = value\nvar owner = Kimi.Intrinsics.makeRc(Item.init(1))\nvar other = Item.init(2)\nKimi.Intrinsics.exchange(owner@follow@uniq, other@uniq)\n", "NoApplicableOverload_Kd Kimi.Intrinsics.exchange(owner@follow@uniq, other@uniq); SharedPathAccess_Kd owner@follow@uniq")]
    [InlineData("func pair<T, U>(v: T) -> () => ()\nfunc f() -> ()\n    pair<3, Undefined>(5)\n", "NoApplicableOverload_Kd pair<3, Undefined>(5); UnresolvedBinding_Kd Undefined")]
    [InlineData("func takeD2(v: i32, w: i32 = 0) -> () => ()\nfunc f() -> ()\n    takeD2(undefinedName)\n", "UnresolvedBinding_Kd undefinedName")]
    [InlineData("func take(v: i32, w: i32) -> () => ()\nfunc take(v: bool) -> () => ()\nfunc f() -> ()\n    take(undefinedName)\n", "UnresolvedBinding_Kd undefinedName")]
    [InlineData("func both<F>(action: ref/F) -> i32\n    F is Callable<(i32) -> i32>\n    F is Callable<(bool, bool) -> i32>\n    return action(undefinedName)\n", "UnresolvedBinding_Kd undefinedName")]
    [InlineData("func take(v: i32) -> () => ()\nfunc take(v: i64) -> () => ()\nfunc f() -> ()\n    take(undefinedName)\n", "UnresolvedBinding_Kd undefinedName")]
    [InlineData("func identity<T>(v: T) -> T => v@move\nfunc f() -> ()\n    _ = identity(undefinedName)\n", "UnresolvedBinding_Kd undefinedName")]
    [InlineData("func both<F>(action: ref/F) -> i32\n    F is Callable<(i32, i32) -> i32>\n    F is Callable<(i64, i64) -> i32>\n    return action(true, undefinedName)\n", "UnresolvedBinding_Kd undefinedName")]
    [InlineData("func both<F>(action: ref/F) -> i32\n    F is Callable<(i32, i32) -> i32>\n    F is Callable<(i64, i64) -> i32>\n    return action(undefinedName, true)\n", "UnresolvedBinding_Kd undefinedName")]
    [InlineData("func take(v: i32, w: i32) -> () => ()\nfunc f() -> ()\n    take(true, undefinedName)\n", "UnresolvedBinding_Kd undefinedName")]
    [InlineData("func f(g: (i32, i32) -> ()) -> ()\n    g(undefinedName, v: 1)\n", "NoApplicableOverload_Kd g(undefinedName, v: 1); UnresolvedBinding_Kd undefinedName")]
    public void ACallBesideAFailedPartReportsWhatReadsNoArgumentType(string source, string errors)
    {
        var c = CompilationTestHelper.ParseSuccess(source);
        c.Bind();
        Assert.Equal(errors, string.Join("; ", CompilationTestHelper.BindingErrors(c).Select(x => $"{x.Code} {source.Substring(x.Span.Start, x.Span.Length)}")));
    }

    // SPEC 23.1.2 (PLAN G421): as above, a part with a syntax error that the completion reaches is no prerequisite of the stopped call,
    // so the call's own record stays direct beside the syntax error. Each row: the published errors.
    [Theory]
    [InlineData("func call(r: ref/(rc/((Option<i32>) -> ()))) -> () => r@follow(.Some(1 +))\n", "Unsupported_Kd r@follow; ExpectedSyntax_Kd )")]
    [InlineData("func call(c: bool, r: ref/(rc/((i32) -> ()))) -> () => r@follow(if c => 1 + else => 1)\n", "Unsupported_Kd r@follow; ExpectedSyntax_Kd else")]
    [InlineData("func f(g: (i32, i32) -> ()) -> ()\n    g(v: 1, undefinedName)\n", "NoApplicableOverload_Kd g(v: 1, undefinedName); MisplacedSyntax_Kd undefinedName")]
    public void AStoppedCallsRecordRestsOnNoRecoveredPart(string source, string errors)
    {
        var c = CompilationTestHelper.Parse(source);
        c.Bind();
        Assert.Equal(errors, string.Join("; ", CompilationTestHelper.BindingErrors(c).Select(x => $"{x.Code} {source.Substring(x.Span.Start, x.Span.Length)}")));
    }

    // SPEC 10.5.1, 10.2.1 (the owner, 2026-10-26): a literal position of a join argument takes its first literal's default after
    // selection, which each of its literals must fit: the misfit is the literal's own record, as for a scalar control-flow argument.
    [Theory]
    [InlineData("keep(if c => 1 else => 2.5)")]
    [InlineData("keep(if c => (1, stop()) else => (2.5, x))")]
    public void ALiteralPositionsDefaultIsCheckedAtEachLiteral(string use)
    {
        var source = ControlFlowDeclarations + use + "\n";
        var errors = DiagnosticCorpus.Check(source).Diagnostics.Where(static x => x.Code != nameof(DiagnosticCode.PrerequisiteUnavailable_Kd));
        var error = Assert.Single(errors);
        Assert.Equal((nameof(DiagnosticCode.TypeMismatch_Kd), "2.5"), (error.Code, source.Substring(error.Span!.Value.Start, error.Span.Value.Length)));
    }

    [Fact]
    public void AResultOnlyOriginKeepsItsInvariantOccurrencesCorrelated()
    {
        const string Source = "func constant() -> (uniq/(ref/i32 during s) during a, uniq/(ref/i32 during s) during b) => $abort(\"never\")\n" +
            "func reject(x: ref/i32, y: ref/i32) -> ()\n    let c = constant\n    let f: () -> (uniq/(ref/i32 during x) during x, uniq/(ref/i32 during y) during y) = c\npublic func main() -> () => ()";
        var rejected = DiagnosticCorpus.Check(Source);
        Assert.False(rejected.Accepted);
        var error = Assert.Single(rejected.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.UnprovenOriginContract_Kd), error.Code);
        Assert.Equal("the result requires x == y, which is not proven", error.Label);
        Assert.True(DiagnosticCorpus.Check(Source.Replace("during y", "during x", StringComparison.Ordinal)).Accepted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ANamedUniversalIsInstantiatedAtTheRequiredInput(bool callable)
    {
        var declaration = callable ? "func run<T, F>(value: T, f: ref/F) -> T\n    F is Callable<(T) -> T>\n    return f(value@move)\n"
            : "func run<T>(value: T, f: (T) -> T) -> T => f(value@move)\n";
        ScalarEmissionTest.EmitFixture("ApplicabilityChainNamed" + callable, "func pick(n: ref/i32 during a) -> ref/i32 during a => n\n" + declaration + "let local: i32 = 7\nrequire run(local@ref, pick)@follow == 7 else => $abort(\"named\")", string.Empty);
    }

    [Fact]
    public void ANestedExpectationCanBindTheWholeResultSlot()
        => Assert.True(DiagnosticCorpus.Check("func make<T>() -> T => $abort(\"never\")\nfunc keep(n: i64) -> i64 => n\nfunc caller() -> i64 => keep(make())\npublic func main() -> () => ()").Accepted);

    [Fact]
    public void TheFailedDeclarationRecordReachesEveryOutputAndKeepsIndependentErrors()
    {
        const string Source = "func f(x: Option<ref/i32>, y: i32) -> i32 => 1\nfunc f(x: Option<ref/i32 during static>, y: i32) -> i32 => 3\n" +
            "func g(p: ref/i32) -> i32 => f(.Some(p), 1)\npublic func main() -> ()\n    let x = missing";
        var check = DiagnosticCorpus.Check(Source);
        Assert.Equal([nameof(DiagnosticCode.MissingOriginBinding_Kd), nameof(DiagnosticCode.UnresolvedBinding_Kd)], check.Diagnostics.Select(x => x.Code));
        var declaration = check.Diagnostics[0];
        Assert.Equal("ref/i32", Source.Substring(declaration.Span!.Value.Start, declaration.Span.Value.Length));
        var result = new DiagnosticResult(check.Diagnostics, check.Sources);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("MissingOriginBinding_Kd", console.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("UnprovenConstraint_Kd", console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(check.Sources[declaration.Source].Path);
        foreach (var related in new[] { false, true })
        {
            var sent = WorkspaceCheck.Place(check, [identity], identity, related)[identity];
            Assert.Equal(check.Diagnostics.Select(x => (x.Code, x.Display!.Range!.Value)), sent.Select(x => (x.Code, x.Range)));
        }

        var json = System.Text.Json.JsonSerializer.Serialize(result, DiagnosticJsonContext.Default.DiagnosticResult);
        Assert.Equal(result, System.Text.Json.JsonSerializer.Deserialize(json, DiagnosticJsonContext.Default.DiagnosticResult));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmNestedInferenceAndUniversalErasureAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(VerificationWorkloads.NestedInferenceAndUniversalErasure);
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Generate(TextWriter.Null)));
        Assert.True(valid, MinimalEmissionTest.Describe(c));
    }

    // PLAN G765: a failed selection's rejected candidates are rows reused between passes, so a warm rebind of the rejected call allocates
    // nothing: no applicable candidate (one, two), an ambiguous call (SPEC 10.4), an unfixed and a mismatched function reference (SPEC
    // 10.5), and a call whose recovered argument list rejects every candidate without a record of its own (SPEC 23.1.2).
    [Theory]
    [Trait("Purpose", "Allocation")]
    [InlineData("func f(x: i32) -> i32 => x\npublic func main() -> ()\n    let y = f(true)\n", "NoApplicableOverload_Kd")]
    [InlineData("func f(x: i32) -> i32 => x\nfunc f(x: i64) -> i64 => x\npublic func main() -> ()\n    let y = f(true)\n", "NoApplicableOverload_Kd")]
    [InlineData("func choose(value: i32) -> () => ()\nfunc choose(value: i64) -> () => ()\npublic func main() -> ()\n    choose(1)\n", "AmbiguousBinding_Kd")]
    [InlineData("func f(x: i32) -> i32 => x\nfunc f(x: i64) -> i64 => x\npublic func main() -> ()\n    let g = f\n", "AmbiguousBinding_Kd")]
    [InlineData("func f(x: i32) -> i32 => x\nfunc f(x: i64) -> i64 => x\npublic func main() -> ()\n    let g: (bool) -> i32 = f\n", "NoApplicableOverload_Kd")]
    [InlineData("func f(a: i32, b: i32) -> i32 => a\npublic func main()\n    var y = f(1,, 2)\n", null)]
    public void WarmRejectedSelectionsReuseTheirCandidateFacts(string source, string? code)
    {
        var c = MinimalEmissionTest.Analyze(source);
        var rejected = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => rejected &= !c.Bind().IsComplete));
        Assert.True(rejected);
        Assert.Equal(code is null ? [] : [code], c.Binding.Issues.Select(static x => x.Code.ToString()));
    }

    // PLAN G729: a joined position of two references at `ref/i32 during a` (two Origins, one region covering both) allocates nothing in a
    // warm rebind (the adapted parts' scratch, SlotSolution.InferAdaptedParts).
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmJoinedBorrowPositionAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze(JoinPositions + "    let q = pick(if c => (n, 1) else => (m, 2))\n    return q\n()");
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(valid, MinimalEmissionTest.Describe(c));
    }
}
