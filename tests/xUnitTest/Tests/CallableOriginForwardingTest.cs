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
        Assert.False(c.Emission.Validate(out _));
    }

    private const string Main = "public func main() => ()\n";

    private const string FixedFunctionValue = "func use(x: ref/i32, f: (ref/i32 during x) -> ref/i32 during x) -> i32\n    let g: (ref/i32) -> ref/i32 = f@move\n" +
        "    let local: i32 = 5\n    return g(local@ref)@follow\n";

    private const string StoringCallable = "func callIt<F>(f: uniq/F, x: ref/i32) -> i32\n    F is Callable<uniq, (ref/i32) -> ref/i32>\n    if x@follow > 0\n" +
        "        let local: i32 = 5\n        f(local@ref)\n    let r = f(x)\n    return r@follow\nfunc use(x: ref/i32) -> i32\n    let start: ref/i32 = x\n" +
        "    var c = func [var start] (n: ref/i32 during x) -> ref/i32 during x\n        let old = start\n        start = n\n        return old\n    return callIt(c@uniq, x)\n";

    private const string ReadingCallable = "func callIt<F>(f: ref/F, x: ref/i32) -> i32\n    F is Callable<(ref/i32) -> i32>\n    let local: i32 = 5\n    return f(local@ref)\n" +
        "func use(x: ref/i32) -> i32\n    let c = func (n: ref/i32 during x) => n@follow\n    return callIt(c@ref, x)\n";

    // F1 (PLAN G65): an implementation input written over a fixed Origin of the enclosing body is no per-call input, even at its own
    // slot (SPEC 8.6, 10.7), so it never fits a required per-call input. A Callable proof, a closure erasure and a Function value
    // conversion each reject it; the closure that stored its argument let a dead stack slot be read.
    [Theory]
    [InlineData(StoringCallable, nameof(DiagnosticCode.NoApplicableOverload_Kd), "callIt(c@uniq, x)")]
    [InlineData(ReadingCallable, nameof(DiagnosticCode.NoApplicableOverload_Kd), "callIt(c@ref, x)")]
    [InlineData("func use(x: ref/i32) -> i32\n    let c = func (n: ref/i32 during x) => n@follow\n    let g: (ref/i32) -> i32 = c\n    let local: i32 = 5\n    return g(local@ref)\n", nameof(DiagnosticCode.UnprovenOriginContract_Kd), "c")]
    [InlineData(FixedFunctionValue, nameof(DiagnosticCode.UnprovenOriginContract_Kd), "f@move")]
    public void AFixedImplementationNeverFitsAPerCallRequirement(string body, string code, string text)
    {
        var source = body + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((code, text), (error.Code, error.Span is { } span ? source.Substring(span.Start, span.Length) : string.Empty));
    }

    // SPEC 23.3.6.5: a per-call Origin of the required Function Type has no name; it is displayed as omitted at its input Type occurrence,
    // which is related, and Advice never offers an Origin set on that borrowed input.
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

    // SPEC 10.7, 15.6.1, 23.3.6.5: a common Function conversion whose signatures match structurally and whose Origin contract is not
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
        var allocated = AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(0, allocated);
    }
}
