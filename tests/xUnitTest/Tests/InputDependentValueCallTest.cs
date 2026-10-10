// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Verification;
using Xunit;

namespace XunitTest;

// SPEC 10.7, 15.6.4: a value call whose result depends on its per-call inputs takes the arguments' Origins into that result,
// as an ordinary call does; the result retains the argument Loans and an exclusive result reborrows its argument.
public class InputDependentValueCallTest
{
    private const string Box = "struct Box<T>\n    var item: T\n\n    public init(item: T)\n        self.item = item@move\n\n" +
        "    public func get(self) -> ref/T during self => self.item@ref\n";

    private const string Pick = "func pick(a: ref/i32, b: ref/i32) -> ref/i32 => a\n";

    private const string Bump = "func bump(value: uniq/i32) -> uniq/i32 => value\n";

    [Theory]
    [InlineData("Item", Box + "let box = Box<i32>.init(item: 5)\nlet get = Box<i32>.get\nlet r = get(box@ref)\nrequire r == 5 else => $abort(\"item\")")]
    [InlineData("Closure", "let f = func (n: ref/i32) => n\nlet n = 7\nlet r = f(n@ref)\nrequire r == 7 else => $abort(\"closure\")")]
    [InlineData("TwoInputs", Pick + "let p = pick\nlet x = 1\nlet y = 2\nlet r = p(x@ref, y@ref)\nrequire r == 1 else => $abort(\"pick\")")]
    [InlineData("ErasedShared", Box + "let box = Box<i32>.init(item: 8)\nlet g: (ref/Box<i32>) -> ref/i32 = Box<i32>.get\nlet r = g(box@ref)\nrequire r == 8 else => $abort(\"erased\")")]
    [InlineData("ErasedClosure", "let f = func (n: ref/i32) => n\nlet g: (ref/i32) -> ref/i32 = f\nlet n = 7\nrequire g(n@ref) == 7 else => $abort(\"erased closure\")")]
    [InlineData("ErasedTwoInputs", Pick + "let p: (ref/i32, ref/i32) -> ref/i32 = pick\nlet x = 3\nlet y = 4\nrequire p(x@ref, y@ref) == 3 else => $abort(\"erased pick\")")]
    [InlineData("ExclusiveChain", Bump + "var k: i32 = 1\nlet b = bump\nlet d = b(k@uniq)\nlet e = b(d)\ne@follow = 4\nd@follow = 5\nrequire k == 5 else => $abort(\"chain\")")]
    [InlineData("ErasedExclusive", Bump + "var k: i32 = 1\nlet b: (uniq/i32) -> uniq/i32 = bump\nlet d = b(k@uniq)\nd@follow = 6\nrequire k == 6 else => $abort(\"erased exclusive\")")]
    [InlineData("AfterLastUse", Box + "var box = Box<i32>.init(item: 5)\nlet get = Box<i32>.get\nlet r = get(box@ref)\nrequire r == 5 else => $abort(\"read\")\nbox = Box<i32>.init(item: 6)\nrequire box.get() == 6 else => $abort(\"replaced\")")]
    public void ResultsTakeTheArgumentOrigins(string name, string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        ScalarEmissionTest.EmitFixture("InputDependentCall" + name, source, string.Empty);
    }

    [Theory]
    [InlineData(Box + "var box = Box<i32>.init(item: 5)\nlet get = Box<i32>.get\nlet r = get(box@ref)\nbox = Box<i32>.init(item: 6)\nrequire r == 5 else => $abort(\"read\")")]
    [InlineData("let f = func (n: ref/i32) => n\nvar n = 7\nlet m = f(n@ref)\nn = 8\nrequire m == 7 else => $abort(\"read\")")]
    [InlineData(Pick + "let p = pick\nlet x = 1\nvar y = 2\nlet r = p(x@ref, y@ref)\ny = 3\nrequire r == 1 else => $abort(\"read\")")]
    [InlineData(Box + "var box = Box<i32>.init(item: 8)\nlet g: (ref/Box<i32>) -> ref/i32 = Box<i32>.get\nlet r = g(box@ref)\nbox = Box<i32>.init(item: 9)\nrequire r == 8 else => $abort(\"read\")")]
    [InlineData(Bump + "var k: i32 = 1\nlet b = bump\nlet c = b(k@uniq)\nlet read = k\nc@follow = 2")]
    [InlineData(Bump + "var k: i32 = 1\nlet b = bump\nlet d = b(k@uniq)\nlet e = b(d)\nd@follow = 5\ne@follow = 4")]
    public void TheResultRetainsTheArgumentLoans(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    private const string None = "func none() -> Option<ref/i32> => .None\nfunc noneOf(x: i32) -> Option<ref/i32> => .None\n";

    private const string IsNone = "\nmatch r\n    .Some(let item) => $abort(\"some\")\n    .None => ()";

    // SPEC 15.4.3, 15.6.4: a result whose Origins are all static substitutes nothing, whatever the per-call inputs.
    [Theory]
    [InlineData("Item", None + "let e = none\nlet r = e()" + IsNone)]
    [InlineData("Erased", None + "let e: () -> Option<ref/i32> = none\nlet r = e()" + IsNone)]
    [InlineData("ErasedInput", None + "let e: (i32) -> Option<ref/i32> = noneOf\nlet r = e(1)" + IsNone)]
    [InlineData("Callable", None + "func apply<F>(f: ref/F) -> Option<ref/i32>\n    F is Callable<() -> Option<ref/i32>>\n    return f()\nlet r = apply(none)" + IsNone)]
    public void StaticResultsNeedNoSubstitution(string name, string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        ScalarEmissionTest.EmitFixture("InputDependentCallStatic" + name, source, string.Empty);
    }

    [Theory]
    [InlineData("func apply<F>(f: ref/F) -> i32\n    F is Callable<() -> ref/i32>\n    let r = f()\n    return r\nlet n = 7\nlet view = n@ref\nlet f = func [view] () => view\nlet v = apply(f@ref)")]
    [InlineData("let n = 7\nlet view = n@ref\nlet f = func [view] () => view\nlet g: () -> ref/i32 = f")]
    public void ALocalResultIsNoStaticResult(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Analyze().IsVerified && c.Emission.Validate(out _));
    }

    [Fact]
    public void TheCallResultIsOverTheArgumentPlace()
    {
        var c = MinimalEmissionTest.Analyze(Box + "let box = Box<i32>.init(item: 5)\nlet get = Box<i32>.get\nlet r = get(box@ref)");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<Kimi.Compiler.Parsing.InvocationKoto>(), x => x.ValueCallOf() is not null);
        var origin = call.TypeOf()!.Origin!;
        Assert.Equal(OriginKind.Projection, origin.Kind);
    }

    private const string Swap = "func pass(x: ref/i32) -> ref/i32 during x\n    let start: ref/i32 = x\n    var c = func [var start] (n: ref/i32 during x) -> ref/i32 during x\n" +
        "        let old = start\n        start = n\n        return old\n";

    // SPEC 8.6, 15.6.4: an input or result written over a fixed Origin of the calling body, here an input of the enclosing function,
    // is no per-call input of the callee: the argument is fitted to it as written and the result keeps it, whatever slot it occupies.
    [Theory]
    [InlineData("TupleInput", "func use(x: ref/i32) -> i32\n    let c = func (pair: (ref/i32 during x, i32)) => pair.0@follow + pair.1\n    return c((x, 1))\nlet n: i32 = 2\nrequire use(n@ref) == 3 else => $abort(\"tuple\")")]
    [InlineData("OptionInput", "func use(x: ref/i32) -> i32\n    let d = func (o: Option<ref/i32 during x>) -> i32\n        match o\n            .Some(let r) => return r@follow\n            .None => return 0\n    return d(.Some(x))\nlet n: i32 = 2\nrequire use(n@ref) == 2 else => $abort(\"option\")")]
    [InlineData("Coincident", "func use(x: ref/i32) -> i32\n    let c = func (n: ref/i32 during x) => n@follow\n    return c(x)\nlet a: i32 = 3\nrequire use(a@ref) == 3 else => $abort(\"coincident\")")]
    [InlineData("FixedResult", Swap + "    let r = c(x)\n    return c(r)\nlet a: i32 = 3\nrequire pass(a@ref)@follow == 3 else => $abort(\"fixed result\")")]
    [InlineData("CopyCaptureResult", "func use(x: ref/i32) -> i32\n    let b = 5\n    let rb = b@ref\n    let f = func [rb] (n: ref/i32 during x) => rb\n    let r = f(x)\n    return r@follow + x@follow\nlet n: i32 = 2\nrequire use(n@ref) == 7 else => $abort(\"capture\")")]
    [InlineData("FunctionValueHoldsNoLoan", "func bump(n: uniq/i32) -> uniq/i32 => n\nfunc apply(x: uniq/i32, f: (uniq/i32 during x) -> uniq/i32 during x) -> i32\n    let r = f(x)\n    r@follow = 4\n    return x@follow\nvar p: i32 = 1\nrequire apply(p@uniq, bump) == 4 else => $abort(\"apply\")")]
    [InlineData("NestedFunction", "func use(x: ref/i32) -> i32\n    func helper(b: ref/i32 during x, h: (ref/i32 during x, i32)) -> ref/i32 during x => b\n    let r = helper(x, (x, 1))\n    return r@follow\nlet n: i32 = 2\nrequire use(n@ref) == 2 else => $abort(\"nested\")")]
    public void FixedInputsAreOrdinaryArguments(string name, string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        ScalarEmissionTest.EmitFixture("FixedInputCall" + name, source, string.Empty);
    }

    // A fixed input is fitted as written; a finite local cannot satisfy an enclosing fixed Origin.
    [Theory]
    [InlineData(Swap + "    if x@follow > 0\n        let local: i32 = 5\n        c(local@ref)\n    return c(x)\n", nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "local@ref")]
    [InlineData("func use(y: i32, x: ref/i32) -> ref/i32 during x\n    let start: ref/i32 = x\n    var c = func [var start] (n: ref/i32 during x) -> ref/i32 during x\n        let old = start\n        start = n\n        return old\n    if x@follow > y\n        let local: i32 = 5\n        c(local@ref)\n    return c(x)\n", nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "local@ref")]
    [InlineData("func use(x: ref/i32) -> i32\n    let c = func (n: ref/i32 during x) => n@follow\n    let local: i32 = 5\n    return c(local@ref)\n", nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "local@ref")]
    [InlineData("func use() -> i32\n    let x: i32 = 4\n    let r = x@ref\n    let start = r\n    var c = func [var start] (n: ref/i32 during r) -> ref/i32 during r\n        let old = start\n        start = n\n        return old\n    if x > 0\n        let y: i32 = 5\n        c(y@ref)\n    let z = c(r)\n    return z@follow\n", nameof(DiagnosticCode.ComparisonLoanConflict_Kd), "let y: i32 = 5\n        c(y@ref)")]
    public void AFixedInputIsNoPerCallInput(string body, string code, string text)
    {
        var source = body + "public func main() => ()\n";
        var errors = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.NotEmpty(errors);
        Assert.All(errors, x => Assert.Equal(code, x.Code));
        Assert.Equal(text, errors[0].Span is { } span ? source.Substring(span.Start, span.Length) : string.Empty);
    }

    // G65: the original fixed-input reproductions now use ordinary retention and conflict diagnostics.
    [Theory]
    [InlineData("func use(x: ref/i32, z: uniq/i32) -> i32\n    origin z outlives x\n    let c = func (b: uniq/i32 during x) -> uniq/i32 during x => b\n    let r = c(z)\n    r@follow = 1\n    return z@follow\n", true)]
    [InlineData("func use(z: uniq/i32, x: ref/i32) -> i32\n    origin z outlives x\n    var holder: ref/i32 during x = x\n    var c = func [holder@uniq] (b: ref/i32 during x) -> i32\n        holder@follow = b\n        return 0\n    c(z@follow@ref)\n    return holder@follow\n", true)]
    [InlineData("func use(x: ref/i32, z: uniq/i32) -> i32\n    origin z outlives x\n    func helper(b: uniq/i32 during x) -> uniq/i32 during x => b\n    let r = helper(z)\n    let view = z@follow@ref\n    r@follow = view@follow + 40\n    return view@follow\n", false)]
    [InlineData("func use(z: uniq/i32, x: ref/i32) -> i32\n    origin z outlives x\n    func helper(h: uniq/(ref/i32 during x), b: ref/i32 during x) -> i32\n        h@follow = b\n        return 0\n    var holder: ref/i32 during x = x\n    helper(holder@uniq, z@follow@ref)\n    z@follow = 41\n    return holder@follow\n", false)]
    public void FixedInputsPreserveTheirActualLoans(string body, bool valid)
    {
        var source = body + "\npublic func main() => ()";
        var errors = DiagnosticCorpus.Check(source).Diagnostics;
        if (valid)
        {
            Assert.Empty(errors);
        }
        else
        {
            var error = Assert.Single(errors);
            Assert.Equal(nameof(DiagnosticCode.ComparisonLoanConflict_Kd), error.Code);
            Assert.Equal("z", source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        }
    }

    // SPEC 15.6.4: a value call through a Function Type whose result is written over an input of the enclosing function binds as an
    // ordinary call with that fixed result, and a result returned through it keeps the caller's Loan.
    [Fact]
    public void AFixedResultKeepsItsLoans()
    {
        var c = MinimalEmissionTest.Analyze("func apply(x: ref/i32, f: ref/((i32) -> ref/i32 during x)) -> ref/i32 during x\n    return f(1)\n");
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        c = MinimalEmissionTest.Analyze(Swap + "    return c(x)\nvar a: i32 = 3\nlet r = pass(a@ref)\na = 4\nrequire r@follow == 3 else => $abort(\"read\")");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmFixedInputValueCallsAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(VerificationWorkloads.FixedInputValueCall);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmInputDependentValueCallsAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(VerificationWorkloads.InputDependentValueCall);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }
}
