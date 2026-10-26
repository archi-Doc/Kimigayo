// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;
using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

public class CallableOriginForwardingTest
{
    [Theory]
    [InlineData("i32", "42", "value")]
    [InlineData("(i32, i32)", "(20, 22)", "value.0 + value.1")]
    public void EquivalentPerCallContractsForward(string type, string value, string read)
    {
        var source = "func invoke(f: (ref/" + type + ") -> i32, value: ref/" + type + ") -> i32 => f(value)\n" +
            "func forward(f: (ref/" + type + ") -> i32, value: ref/" + type + ") -> i32 => invoke(f@move, value)\n" +
            "let callback = func (value: ref/" + type + ") -> i32 => " + read + "\n" +
            "let f: (ref/" + type + ") -> i32 = callback\nlet value = " + value + "\n" +
            "require forward(f@move, value@ref) == 42 else => $abort(\"forward\")";
        ScalarEmissionTest.EmitFixture("CallableOriginForward" + (type == "i32" ? "Scalar" : "Tuple"), source, string.Empty);
    }

    [Fact]
    public void GenericForwardingKeepsThePerCallBinderLocal()
    {
        const string Source = "func invoke<T>(f: (ref/T) -> i32, value: ref/T) -> i32 => f(value)\n" +
            "func forward<T>(f: (ref/T) -> i32, value: ref/T) -> i32 => invoke(f@move, value)\n" +
            "let callback = func (value: ref/(i32, i32)) -> i32 => value.0 + value.1\n" +
            "let f: (ref/(i32, i32)) -> i32 = callback\nlet value = (20, 22)\n" +
            "require forward(f@move, value@ref) == 42 else => $abort(\"generic\")";
        ScalarEmissionTest.EmitFixture("CallableOriginForwardGeneric", Source, string.Empty);
    }

    [Fact]
    public void ExclusiveInputsKeepTheirAccessRequirement()
    {
        const string Source = "func invoke(f: (uniq/(i32, i32)) -> i32, value: uniq/(i32, i32)) -> i32 => f(value)\n" +
            "func forward(f: (uniq/(i32, i32)) -> i32, value: uniq/(i32, i32)) -> i32 => invoke(f@move, value)\n" +
            "let callback = func (value: uniq/(i32, i32)) -> i32\n    value.0 += 1\n    return value.0 + value.1\n" +
            "let f: (uniq/(i32, i32)) -> i32 = callback\nvar value = (20, 21)\n" +
            "require forward(f@move, value@uniq) == 42 and value.0 == 21 else => $abort(\"exclusive\")";
        ScalarEmissionTest.EmitFixture("CallableOriginForwardExclusive", Source, string.Empty);
    }

    [Theory]
    [InlineData("ref/i32 during static")]
    [InlineData("uniq/i32")]
    [InlineData("ref/bool")]
    [InlineData("ref/i32, ref/i32")]
    public void ForwardingCannotEraseFixedOriginsModesOrParameterShapes(string input)
    {
        var c = MinimalEmissionTest.Analyze("func accept(f: (ref/i32) -> i32) => ()\nfunc forward(f: (" + input + ") -> i32) => accept(f@move)");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, static x => x.Failure == BindingFailure.NoApplicableCandidate);
        Assert.False(c.Validate(out _));
    }

    private const string Main = "public func main() => ()\n";

    // G216 (a) revision 2: a Function value whose result relates q to r converted to one over q alone, and its uses.
    private const string Source = "func k(q: ref/i32, r: ref/i32, f: () -> Option<ref/(ref/i32 during q) during r>) -> ";

    private const string Weaker = "() -> Option<ref/(ref/i32 during q) during q>";

    private const string Use1 = "func use1(q: ref/i32, g: () -> Option<ref/(ref/i32 during q) during q>) -> i32\n    let o = g()\n    return 0\n";

    // An independent Loan conflict, and an unrelated Binding failure (SPEC 23.1.2).
    private const string Conflict = "    var x = 1\n    let w = x@ref\n    x = 2\n    let u = w@follow\n";

    private const string Unrelated = "func m() -> i32\n    let b: bool = 3\n    return 0\n";

    // A Closure's input conversion, proven on dev already.
    private const string AOutlivesB = "func k(a: ref/i32, b: ref/i32) -> i32\n    origin a outlives b\n";

    private const string ClosureOverB = "func [] (p: ref/(ref/i32 during b)) -> ref/i32 => p@follow";

    // A result whose omitted inner Origin is the meet of two inputs (`b` and the second's).
    private const string TwoInputs = "(ref/i32 during b, ref/i32) -> (Option<ref/(ref/i32) during b>, ref/i32 during b)";

    // A target whose result gives its input outlives static, and one writing static, against an Item and a Closure with that premise.
    private const string StaticResult = "(ref/i32) -> Option<ref/(ref/i32) during static>";

    private const string StaticWeaker = "(ref/i32) -> Option<ref/(ref/i32 during static) during static>";

    private const string StaticItem = "func leak1(q: ref/i32) -> Option<ref/(ref/i32 during q) during static>\n    return .None\n";

    private const string StaticClosure = "func [] (q: ref/i32) -> Option<ref/(ref/i32 during q) during static> => .None";

    // The same premise written as a clause, which the target's Types give too (R4-M144's known limit, held out by the owner), and its
    // selected calls.
    private const string ClauseItem = "func pin1(q: ref/i32) -> Option<ref/(ref/i32 during q) during static>\n    origin q outlives static\n    return .None\n";

    private const string FixedFunctionValue = "func use(x: ref/i32, f: (ref/i32 during x) -> ref/i32 during x) -> i32\n    let g: (ref/i32) -> ref/i32 = f@move\n" +
        "    let local: i32 = 5\n    return g(local@ref)@follow\n";

    private const string StoringCallable = "func callIt<F>(f: uniq/F, x: ref/i32) -> i32\n    F is Callable<uniq, (ref/i32) -> ref/i32>\n    if x@follow > 0\n" +
        "        let local: i32 = 5\n        f(local@ref)\n    let r = f(x)\n    return r@follow\nfunc use(x: ref/i32) -> i32\n    let start: ref/i32 = x\n" +
        "    var c = func [var start] (n: ref/i32 during x) -> ref/i32 during x\n        let old = start\n        start = n\n        return old\n    return callIt(c@uniq, x)\n";

    private const string ReadingCallable = "func callIt<F>(f: ref/F, x: ref/i32) -> i32\n    F is Callable<(ref/i32) -> i32>\n    let local: i32 = 5\n    return f(local@ref)\n" +
        "func use(x: ref/i32) -> i32\n    let c = func (n: ref/i32 during x) => n@follow\n    return callIt(c@ref, x)\n";

    // F1 (PLAN G65): an implementation input written over a fixed Origin of the enclosing body is no per-call input, even at its own
    // slot (SPEC 8.10, 10.7), so it never fits a required per-call input. A Callable proof (whose Unknown Origin part is the Constraint
    // record at the argument, SPEC 8.3, 15.6.1), a closure erasure and a Function value conversion each reject it; the closure that
    // stored its argument let a dead stack slot be read.
    [Theory]
    [InlineData(StoringCallable, nameof(DiagnosticCode.UnprovenConstraint_Kd), "c@uniq")]
    [InlineData(ReadingCallable, nameof(DiagnosticCode.UnprovenConstraint_Kd), "c@ref")]
    [InlineData("func use(x: ref/i32) -> i32\n    let c = func (n: ref/i32 during x) => n@follow\n    let g: (ref/i32) -> i32 = c\n    let local: i32 = 5\n    return g(local@ref)\n", nameof(DiagnosticCode.UnprovenOriginContract_Kd), "c")]
    [InlineData(FixedFunctionValue, nameof(DiagnosticCode.UnprovenOriginContract_Kd), "f@move")]
    public void AFixedImplementationNeverFitsAPerCallRequirement(string body, string code, string text)
    {
        var source = body + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((code, text), (error.Code, error.Span is { } span ? source.Substring(span.Start, span.Length) : string.Empty));
    }

    // SPEC 23.1: a per-call Origin of the required Function Type has no name; it is displayed as omitted at its input Type occurrence,
    // which is related.
    [Fact]
    public void AnUnnamedPerCallInputIsDisplayedAsOmitted()
    {
        var output = DiagnosticCorpus.Check(FixedFunctionValue + Main);
        var record = Assert.Single(output.Diagnostics);
        var result = new DiagnosticResult(output.Diagnostics, output.Sources);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("the 1st parameter requires the omitted Origin of ref/i32 outlives x, which is not proven", console.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("self", console.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{name}", console.Text, StringComparison.Ordinal);
        Assert.Equal("ref/i32", (FixedFunctionValue + Main).Substring(record.Related![0].Span!.Value.Start, record.Related[0].Span!.Value.Length));
        var json = JsonSerializer.Serialize(result, DiagnosticJsonContext.Default.DiagnosticResult);
        Assert.Contains("{\"name\":\"longer\",\"kind\":\"Origin\",\"value\":\"ref/i32\",\"elided\":false,\"origin\":\"omitted\"}", json, StringComparison.Ordinal);
    }

    // SPEC 10.7, 15.6.1, 23.1: a common Function conversion whose signatures match structurally and whose Origin contract is not
    // proven is one UnprovenOriginContract_Kd at the converted value. Its member is the failing parameter or the result, and its ends
    // are rigid symbols of the comparison, displayed as the required contract writes them, never a call-time Origin of the implementation.
    [Theory]
    [InlineData("func use(x: ref/i32) -> i32\n    let c = func (n: ref/i32 during x) => n@follow\n    let g: (ref/i32) -> i32 = c\n    return g(x)\n", "c", "the 1st parameter requires the omitted Origin of ref/i32 outlives x, which is not proven")]
    [InlineData("func use(x: ref/i32, f: (ref/i32 during x, ref/i32) -> i32) -> i32\n    let g: (ref/i32, ref/i32) -> i32 = f@move\n    let local: i32 = 5\n    return g(local@ref, local@ref)\n", "f@move", "the 1st parameter requires the omitted Origin of ref/i32 outlives x, which is not proven")]
    [InlineData("func id(x: ref/i32) -> ref/i32 => x\nfunc use() -> ()\n    let f: (ref/i32) -> ref/i32 during static = id\n", "id", "the result requires the omitted Origin of ref/i32 outlives static, which is not proven")]
    [InlineData("func id(n: ref/i32) -> ref/i32 => n\nfunc use(x: ref/i32) -> ref/i32 during x\n    let g: (ref/i32) -> ref/i32 during x = id\n    let local: i32 = 5\n    return g(local@ref)\n", "id", "the result requires the omitted Origin of ref/i32 outlives x, which is not proven")]
    public void AConversionReportsItsOriginContract(string body, string text, string label)
    {
        var source = body + "public func main() => ()\n";
        var output = DiagnosticCorpus.Check(source);
        var error = Assert.Single(output.Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnprovenOriginContract_Kd), DiagnosticCategory.Proof, text), (error.Code, error.Category, error.Span is { } span ? source.Substring(span.Start, span.Length) : string.Empty));
        var result = new DiagnosticResult(output.Diagnostics, output.Sources);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains(label, console.Text, StringComparison.Ordinal);
        Assert.Contains(" = origin: ", console.Text, StringComparison.Ordinal);
        var json = JsonSerializer.Serialize(result, DiagnosticJsonContext.Default.DiagnosticResult);
        Assert.Contains("{\"name\":\"comparison\",\"kind\":\"Text\",\"value\":\"conversion\",\"elided\":false}", json, StringComparison.Ordinal);
    }

    // The same conversions with a fitting contract are accepted: the required input written over the fixed Origin, or a static result.
    [Theory]
    [InlineData("func use(x: ref/i32) -> i32\n    let c = func (n: ref/i32 during x) => n@follow\n    let g: (ref/i32 during x) -> i32 = c\n    return g(x)\n")]
    [InlineData("func id(n: ref/i32) -> ref/i32 => n\nfunc use(x: ref/i32) -> ref/i32 during x\n    let g: (ref/i32 during x) -> ref/i32 during x = id\n    return g(x)\n")]
    public void AFittingContractConverts(string body)
        => Assert.Empty(DiagnosticCorpus.Check(body + "public func main() => ()\n").Diagnostics);

    // SPEC 10.7, 15.3.7, 15.6.1 (DECISIONS R4-M144): a conversion of a Function value (a Function Type value or a Closure) or of a Function
    // Item to another Function Type proves the source's signature Types, which its implementation assumes (an anonymous function's result
    // included), from the target's own Types, which each call through the target proves (owner, 2026-10-27, for an Item: 「(a)
    // 無名関数と同じ規則 (推奨)」): at a value, a returned value, a call or construction argument, each operand of an elided meet on its own.
    // Known limit (R4-M144; owner, 2026-10-27: 「(1) 切り離して記録 (推奨)」, a separate unit): an Item's declared clause that the
    // target's Types give is a bound of the comparison's solve, proven from the use's premises alone, so pin1 keeps dev's record (valid
    // under the rule) where its implied-bound twin leak1 converts; its weaker twin and its selected calls are as on dev.
    // A target that weakens the source's result relation is UnprovenOriginContract_Kd at the converted value, as a Closure's input conversion
    // is; it fails binding, so an independent Loan conflict or the same value's transfer is lost beside it, as on dev (PLAN G56's limit), and
    // a value call reports its argument's NoApplicableOverload_Kd instead (PLAN open issue: a value call's arguments are judged as a selected
    // call's).
    [Theory]
    [InlineData(Source + "i32\n    origin r outlives q\n    let g: " + Weaker + " = f@move\n    return 0\n", "UnprovenOriginContract_Kd f@move")]
    [InlineData(Source + "i32\n    origin r outlives q\n    origin q outlives r\n    let g: " + Weaker + " = f@move\n    return 0\n", "")]
    [InlineData(Source + "i32\n    origin r outlives q\n    let g: " + Weaker + " = f@move\n    return 0\n" + Unrelated, "UnprovenOriginContract_Kd f@move; TypeMismatch_Kd 3")]
    [InlineData(Source + "i32\n    origin r outlives q\n" + Conflict + "    let g: " + Weaker + " = f@move\n    return 0\n", "UnprovenOriginContract_Kd f@move")]
    [InlineData(Source + "i32\n    origin r outlives q\n    let g: " + Weaker + " = f\n    return 0\n", "UnprovenOriginContract_Kd f")]
    [InlineData(Source + Weaker + "\n    origin r outlives q\n    return f@move\n", "UnprovenOriginContract_Kd f@move")]
    [InlineData(Use1 + Source + "i32\n    origin r outlives q\n    return use1(q, f@move)\n", "UnprovenOriginContract_Kd f@move")]
    [InlineData(Use1 + Source + "i32\n    origin q outlives r\n    return use1(q, f@move)\n", "")]
    [InlineData(Use1 + Source + "i32\n    origin r outlives q\n" + Conflict + "    return use1(q, f@move)\n", "UnprovenOriginContract_Kd f@move")]
    [InlineData("struct H {q}\n    var g: " + Weaker + "\n    public init(g: " + Weaker + ") => self.g = g@move\n" + Source + "i32\n    origin r outlives q\n    let h = H.init(g: f@move)\n    return 0\n", "UnprovenOriginContract_Kd f@move")]
    [InlineData(Source + "i32\n    origin r outlives q\n    let inner = func [] (n: Option<ref/(ref/i32 during q) during r>, g: " + Weaker + ") -> i32 => 0\n    return inner(Option.None, f@move)\n", "NoApplicableOverload_Kd inner(Option.None, f@move)")]
    [InlineData(AOutlivesB + "    let f: (ref/(ref/i32 during a)) -> ref/i32 = " + ClosureOverB + "\n    return 0\n", "UnprovenOriginContract_Kd " + ClosureOverB)]
    [InlineData(AOutlivesB + "    let f: (ref/(ref/i32 during a)) -> ref/i32 = func [] (p: ref/(ref/i32 during a)) -> ref/i32 => p@follow\n    return 0\n", "")]
    [InlineData(AOutlivesB + Conflict + "    let f: (ref/(ref/i32 during a)) -> ref/i32 = " + ClosureOverB + "\n    return 0\n", "UnprovenOriginContract_Kd " + ClosureOverB)]
    [InlineData("func k(b: ref/i32, f: " + TwoInputs + ") -> i32\n    let g: " + TwoInputs + " = f@move\n    return 0\n", "")]
    [InlineData("func mk() -> i32\n    let g: " + StaticResult + " = " + StaticClosure + "\n    return 0\n", "")]
    [InlineData("func mk() -> i32\n    let g: " + StaticWeaker + " = " + StaticClosure + "\n    return 0\n", "UnprovenOriginContract_Kd " + StaticClosure)]
    [InlineData("func use(g: " + StaticResult + ") -> i32 => 0\nfunc mk() -> i32\n    return use(" + StaticClosure + ")\n", "")]
    [InlineData(StaticItem + "func mk() -> i32\n    let g: " + StaticResult + " = leak1\n    return 0\n", "")]
    [InlineData(StaticItem + "func mk() -> i32\n    let g: " + StaticWeaker + " = leak1\n    return 0\n", "UnprovenOriginContract_Kd leak1")]
    [InlineData(StaticItem + "func mk() -> i32\n    let fr = leak1\n    let g: " + StaticResult + " = fr\n    return 0\n", "")]
    [InlineData(StaticItem + "func use(g: " + StaticResult + ") -> i32 => 0\nfunc mk() -> i32\n    return use(leak1)\n", "")]
    [InlineData(StaticItem + "func mk() -> " + StaticResult + "\n    return leak1\n", "")]
    [InlineData(ClauseItem + "func mk() -> i32\n    let g: " + StaticResult + " = pin1\n    return 0\n", "UnprovenOriginContract_Kd pin1")]
    [InlineData(ClauseItem + "func mk() -> i32\n    let g: " + StaticWeaker + " = pin1\n    return 0\n", "UnprovenOriginContract_Kd pin1")]
    [InlineData(ClauseItem + "func mk() -> i32\n    let local = 1\n    let o = pin1(local@ref)\n    return 0\n", "UnsatisfiedOriginRelation_Kd local@ref")]
    [InlineData(ClauseItem + "func mk(x: ref/i32) -> i32\n    let o = pin1(x)\n    return 0\n", "UnprovenOriginRelation_Kd x")]
    public void AConversionProvesTheSourcesTypesFromTheTargets(string body, string records)
    {
        var source = body + "public func main() => ()\n";
        Assert.Equal(records, string.Join("; ", DiagnosticCorpus.Check(source).Diagnostics.Select(x => $"{x.Code} {(x.Span is { } span ? source.Substring(span.Start, span.Length) : string.Empty).Split('\n')[0]}")));
    }

    // SPEC 23.1 (DECISIONS R4-M144): the record names the source's signature and the relation that the target's Types do not give.
    [Fact]
    public void AConversionNamesTheSourcesSignature()
    {
        var source = Source + "i32\n    origin r outlives q\n    let g: " + Weaker + " = f@move\n    let o = g()\n    return 0\n" + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnprovenOriginContract_Kd), DiagnosticCategory.Proof), (error.Code, error.Category));
        Assert.Equal("the source's signature requires q outlives r, which is not proven", error.Label);
        Assert.Equal(["conversion", "the source's signature", "outlives", "q", "r"], error.Reason!.Select(static x => x.Value));
    }

    private const string Add = "func add(a: ref/i32, b: ref/i32) -> i32 => a@follow + b@follow\n";

    // SPEC 10.7, 15.3.7: each input that the implementation binds per call is instantiated at the required input in the same position,
    // per call or fixed, and every other input is compared as written; a result is compared under that instantiation. Signatures that
    // mix fixed and per-call inputs therefore fit position by position (a 9f8022d8 regression, found by review).
    [Theory]
    [InlineData("FunctionValue", Add + "func use(x: ref/i32) -> i32\n    let f: (ref/i32 during x, ref/i32) -> i32 = add\n    let g: (ref/i32 during x, ref/i32) -> i32 = f@move\n    let local: i32 = 5\n    return g(x, local@ref)\nlet p: i32 = 1\nrequire use(p@ref) == 6 else => $abort(\"value\")")]
    [InlineData("Forwarded", Add + "func invoke(x: ref/i32, f: (ref/i32 during x, ref/i32) -> i32) -> i32\n    let local: i32 = 5\n    return f(x, local@ref)\nfunc use(x: ref/i32) -> i32\n    let f: (ref/i32 during x, ref/i32) -> i32 = add\n    return invoke(x, f@move)\nlet p: i32 = 1\nrequire use(p@ref) == 6 else => $abort(\"forward\")")]
    [InlineData("CallableSlot", "func callIt<T, F>(v: T, f: ref/F) -> i32\n    F is Callable<(T, ref/i32) -> i32>\n    let local: i32 = 5\n    return f(v@move, local@ref)\nfunc use(x: ref/i32) -> i32\n    let c = func (n: ref/i32 during x, m: ref/i32) => n@follow + m@follow\n    return callIt(x, c@ref)\nlet p: i32 = 1\nrequire use(p@ref) == 6 else => $abort(\"callable\")")]
    [InlineData("WrittenHeader", "func use(x: ref/i32, y: ref/i32) -> i32\n    let g: (ref/i32, ref/i32 during y) -> i32 = func (a: ref/i32, b: ref/i32 during y) => a@follow + b@follow\n    let local: i32 = 5\n    return g(local@ref, y)\nlet p: i32 = 1\nlet q: i32 = 2\nrequire use(p@ref, q@ref) == 7 else => $abort(\"written\")")]
    [InlineData("ContextualHeader", "func use(x: ref/i32, y: ref/i32) -> i32\n    let g: (ref/i32, ref/i32 during y) -> i32 = func (a, b) => a@follow + b@follow\n    let local: i32 = 5\n    return g(local@ref, y)\nlet p: i32 = 1\nlet q: i32 = 2\nrequire use(p@ref, q@ref) == 7 else => $abort(\"contextual\")")]
    [InlineData("FixedHeader", "func use(x: ref/i32) -> i32\n    let g: (ref/i32 during x) -> i32 = func (n: ref/i32 during x) => n@follow\n    return g(x)\nlet a: i32 = 4\nrequire use(a@ref) == 4 else => $abort(\"fixed\")")]
    [InlineData("FixedResult", "func use(x: ref/i32, y: ref/i32) -> i32\n    origin x outlives y\n    let g: (ref/i32 during x, i32) -> ref/i32 during y = func (n, k) => n\n    return g(x, 5)@follow\nlet a: i32 = 4\nrequire use(a@ref, a@ref) == 4 else => $abort(\"result\")")]
    public void MixedFixedAndPerCallInputsFit(string name, string source)
        => ScalarEmissionTest.EmitFixture("CallableInstance" + name, source, string.Empty);

    // SPEC 10.7: a per-call implementation fits a required input written over a fixed Origin by instantiating its call-time Origin.
    [Fact]
    public void APerCallImplementationFitsAFixedInput()
        => ScalarEmissionTest.EmitFixture("CallableInstanceFixedInput", "func use(x: ref/i32) -> i32\n    let c = func (n: ref/i32) => n@follow\n    let g: (ref/i32 during x) -> i32 = c\n    return g(x)\nlet a: i32 = 3\nrequire use(a@ref) == 3 else => $abort(\"fixed input\")", string.Empty);

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmForwardingDoesNotAllocate()
    {
        const string Source = "func invoke(f: (ref/i32) -> i32, value: ref/i32) -> i32 => f(value)\n" +
            "let callback = func (value: ref/i32) -> i32 => value\n" +
            "let f: (ref/i32) -> i32 = callback\nlet value = 42\ninvoke(f@move, value@ref)";
        var c = MinimalEmissionTest.Analyze(Source);
        var valid = true;
        var allocated = AllocationMeasurement.Measure(() => valid &= c.WriteIr(TextWriter.Null, out _));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(0, allocated);
    }
}
