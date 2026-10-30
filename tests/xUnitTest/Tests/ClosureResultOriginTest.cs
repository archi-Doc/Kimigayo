// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;
using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Verification;
using Xunit;

namespace XunitTest;

public partial class ClosureResultOriginTest
{
    private const string Inspect = "func inspect(value: ref/i32) => ()\n";

    private const string Apply = "func apply(f: () -> i32) -> i32 => f()\n";

    private const string Generic = "func applyG<F>(f: ref/F) -> i32\n    F is Callable<() -> i32>\n    return 0\n";

    private const string Use = "func use()\n";

    private const string ApplyA = "    let a = outer(3)\n    let r = apply(a)\npublic func main() => ()\n";

    private const string GenericA = "    let a = outer(3)\n    let r = applyG(a@ref)\npublic func main() => ()\n";

    private const string Confined = "struct Item\n    public var value: i32 = 7\n    drop => Console.writeLine(\"drop\")\nfunc apply<F>(f: F) -> i32\n" +
        "    F is Callable<owner, () -> i32>\n        effect confined\n    return f@move()\npublic func main() => ()\nfunc entry() -> i32\n";

    private const string ConfinedF = "    let f = outer(3, Item.init())\n    return apply(f@move)\n";

    private const string NestedUniq = "func use() -> i32\n    var n = 1\n    let c = func [var n] ()\n";

    private const string NestedCall = "    c()\n    return 0\npublic func main() => ()\n";

    private const string Captured = "let n = 7\nlet values: [1 of ref/i32] = [n@ref]\nlet f = func [values] () => values[0]\n";

    [Theory]
    [InlineData("Array", Captured + "let first = f()\nlet second = f()\nrequire first == 7 and second == 7 else => $abort(\"result\")")]
    [InlineData("BorrowedClosure", Captured + "let borrowed = f@ref\nlet result = borrowed()\nrequire result == 7 else => $abort(\"result\")")]
    [InlineData("Direct", "let n = 7\nlet view = n@ref\nlet f = func [view] () => view\nlet result = f()\nrequire result == 7 else => $abort(\"result\")")]
    [InlineData("Slot", "let n = 7\nlet f = func [n@ref] () => n\nlet result = f()\nrequire result == 7 else => $abort(\"result\")")]
    [InlineData("Tuple", "let n = 7\nlet view = n@ref\nlet f = func [view] () => (view, 9)\nlet result = f()\nrequire result.0 == 7 and result.1 == 9 else => $abort(\"result\")")]
    [InlineData("Option", "let n = 7\nlet view = n@ref\nlet f = func [view] () => Option.Some(view)\nlet result = f()\nmatch result\n    .Some(let item)\n        require item == 7 else => $abort(\"result\")\n    .None => $abort(\"none\")")]
    [InlineData("Input", "func read(n: ref/i32) -> i32\n    let values: [1 of ref/i32] = [n]\n    let f = func [values] () => values[0]\n    let result = f()\n    return result\nlet n = 7\nrequire read(n@ref) == 7 else => $abort(\"result\")")]
    [InlineData("FreshArgument", "var n = 7\nlet view = n@ref\nlet f = func [view] (other: ref/i32) => view\nvar other = 9\nlet result = f(other@ref)\nother = 11\nrequire result == 7 and other == 11 else => $abort(\"result\")")]
    public void ConcreteResultsKeepFixedExternalOrigins(string name, string source)
        => ScalarEmissionTest.EmitFixture("ClosureResultOrigin" + name, source, string.Empty);

    [Theory]
    [InlineData("var n = 7\nlet values: [1 of ref/i32] = [n@ref]\nlet f = func [values] () => values[0]\nlet result = f()\nn = 9\nrequire result == 7 else => $abort(\"result\")")]
    [InlineData("var n = 7\nlet f = func [n@ref] () => n\nlet result = f()\nn = 9\nrequire result == 7 else => $abort(\"result\")")]
    public void TheReturnedViewProtectsTheOwnerAfterTheLastClosureUse(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Problems, x => x.Code.IsLoanConflict());
        Assert.DoesNotContain(c.Problems, x => x.Code == DiagnosticCode.Unsupported_Kd);
        Assert.False(c.Validate(out _));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ReturningAnExternalReferenceDoesNotRetainTheEnvironment()
        => NativeAllocationAudit.WriteFixture("ClosureResultOriginCleanup", "struct Item\n    public let id: i32 = 7\n    drop => Console.writeLine(\"drop\")\nlet owner = Kimi.Intrinsics.makeObj(Item.init())\nlet n = 7\nlet values: [1 of ref/i32] = [n@ref]\nlet f = func [values, owner@move] () => values[0]\nlet result = f@move()\nrequire result == 7 else => $abort(\"result\")", 1, 1, 20, "drop\n");

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void StoredObjectResultsKeepHeaderIdentityAndTheOwner()
        => NativeAllocationAudit.WriteFixture("ClosureResultOriginObject", "struct Item\n    public let id: i32 = 7\n    drop => Console.writeLine(\"drop\")\nlet owner = Kimi.Intrinsics.makeObj(Item.init())\nlet values: [1 of objref/Item] = [owner@objref]\nlet f = func [values] () => values[0]\nlet result = f()\nrequire result.id == 7 else => $abort(\"result\")", 1, 1, 20, "drop\n");

    // SPEC 15.8.2, 15.6.4, 7.7.3: a call's result over the closure's environment depends on the call receiver. Binding binds it with the
    // receiver's Origin (DECISIONS R4-M152), and the old analysis, which cannot judge it, keeps R1's verdict through its gate: each such
    // call is one located Unsupported_Kd in a program Binding accepts, its code and span R1's, until R6-check (the MIR judges them, MirBorrowsTest):
    // a borrow of an environment binding, through a reference receiver, a Reborrow through a captured exclusive reference, a result
    // beside a per-call input, a temporary closure, a Consuming call that moves a captured reference out, a returned closure that borrows
    // the environment, a call inside a closure argument (bound under a consultation, gated by its final binding: F4) and a nested
    // Closure's call in its maker's body.
    [Theory]
    [InlineData("let n = 7\nlet f = func [n] () => n@ref\nlet r = f()", "f()")]
    [InlineData("let n = 7\nlet f = func [n] () => n@ref\nlet g = f@ref\nlet r = g()", "g()")]
    [InlineData("var n = 7\nlet view = n@uniq\nlet f = func [view] () => view@follow@ref\nlet r = f()", "f()")]
    [InlineData("func pick(a: ref/i32, b: ref/i32) -> ref/i32 => a\nlet n = 7\nlet f = func [n] (x: ref/i32) => pick(n@ref, x)\nlet m = 8\nlet r = f(m@ref)", "f(m@ref)")]
    [InlineData("let n = 7\nlet r = (func [n] () => n@ref)()", "(func [n] () => n@ref)()")]
    [InlineData("var n = 7\nlet view = n@uniq\nvar f = func [view] () => view\nlet r = f()", "f()")]
    [InlineData("var n = 7\nlet view = n@uniq\nlet take = func [view@move] () => view@move\nlet r = take@move()", "take@move()")]
    [InlineData("let n = 7\nlet outer = func [n] () => func [n@ref] () => n\nlet inner = outer()", "outer()")]
    [InlineData("func apply(f: () -> i32) -> i32 => f()\nlet n: i32 = 4\nlet get = func [n] () => n@ref\nlet r = apply(func [get] () => get()@follow)", "get()")]
    [InlineData("var x: i32 = 1\nlet u = x@uniq\nvar outer = func [u] () -> i32\n    let i = func [u] () => u@move\n    let p = i@move()\n    p@follow = 2\n    return 0\n_ = outer()", "i@move()")]
    public void AResultOverTheEnvironmentIsUnsupported(string source, string call)
    {
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.Unsupported_Kd), call), (error.Code, source.Substring(error.Span!.Value.Start, error.Span.Value.Length)));
    }

    // Kept apart, not counted for G273 (PLAN G280; R4's G273 problem check K3). SPEC 15.8.2, 23.1.2 (DECISIONS R4-M152 P8, R5-M63): a
    // returned Closure literal that P8 fails, a maker's expression body at any nesting, keeps its definition: its record is at the literal,
    // as the block form's `return` places it, and the returned value stays callable, with no derived NotCallable_Kd or UnresolvedBinding_Kd
    // (review 2's B1: v2, a08, f03 = G273's p8_param_bad, f04, f07; each with its block-form twin). A reader reaching a closure through its
    // Type reads its definition (BoundClosure.Of), whatever its literal's creation outcome. The call's result rests on that record (P9's
    // image of the call result atom is an Unbound end), so the record is the program's one; dev adds F1's located Unsupported_Kd at the
    // call. Question: does G280 (or G206 (6)) give an Unbound end its cause, so that the call records nothing?
    [Theory]
    [InlineData("let outer = func (x: i32) => func [x@ref] () => x\nlet a = outer(3)\nlet r = a()", "func [x@ref] () => x")]
    [InlineData("let outer = func (x: i32)\n    return func [x@ref] () => x\nlet a = outer(3)\nlet r = a()", "func [x@ref] () => x")]
    [InlineData(Inspect + "let outer = func (x: i32) => func [x@ref] () => x\nlet a = outer(3)\ninspect(a())", "func [x@ref] () => x")]
    [InlineData(Inspect + "let outer = func (x: i32)\n    return func [x@ref] () => x\nlet a = outer(3)\ninspect(a())", "func [x@ref] () => x")]
    [InlineData("let outer = func (x: i32) => func (y: i32) => func [y@ref] () => y\nlet m = outer(3)\nlet a = m(4)\nlet r = a()", "func [y@ref] () => y")]
    [InlineData("let outer = func (x: i32)\n    return func (y: i32) => func [y@ref] () => y\nlet m = outer(3)\nlet a = m(4)\nlet r = a()", "func [y@ref] () => y")]
    [InlineData(
        "let outer = func (x: i32)\n    return func (y: i32)\n        return func [y@ref] () => y\nlet m = outer(3)\nlet a = m(4)\nlet r = a()",
        "func [y@ref] () => y")]
    [InlineData("let outer = func (x: i32) => func [x@ref] () => x\nlet r = outer(3)()", "func [x@ref] () => x")]
    [InlineData("let outer = func (x: i32)\n    return func [x@ref] () => x\nlet r = outer(3)()", "func [x@ref] () => x")]
    public void AFailedReturnedLiteralKeepsItsDefinitionPendingG280(string source, string literal)
        => Assert.Equal($"{nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd)} {literal}", Records(source));

    // DECISIONS R4-M152 (round 3, BU's re-check 2): a returned value P8 failed whose result is `ref/i32`, passed to a generic
    // `F is Callable<() -> i32>`, keeps beside the record at the literal an independent NoApplicableOverload_Kd, since a valid Closure of
    // that signature gets it too (dev had a derived NotCallable_Kd).
    [Fact]
    public void AFailedReturnedLiteralsCallableUseKeepsItsIndependentRecord()
        => Assert.Equal(
            "UnsatisfiedOriginRelation_Kd func [x@ref] () => x; NoApplicableOverload_Kd apply(a@ref)",
            Records("func apply<F>(f: ref/F) -> i32\n    F is Callable<() -> i32>\n    return 0\nlet outer = func (x: i32)\n    return func [x@ref] () => x\n" +
                "let a = outer(3)\nlet r = apply(a@ref)"));

    // SPEC 7.7.4, 8.11, 23.1.2 (DECISIONS R4-M152, R5-M63 (1)): a use that reaches a returned literal P8 failed through its Type reads the
    // closure's definition, so the bare expression body gives its parenthesized and block twins' records: no TransferRequired_Kd with an
    // inapplicable Repair at a `() -> i32` parameter (review 3's h1, BU's d3), no InvalidConstraint_Kd from the failed-context walk, where
    // the literal keeps its signature (h4, d4, and eff1, whose MIR keeps its independent UnsatisfiedEffectBound_Kd). The Owned proof's
    // UnprovenConstraint_Kd at `a`, in every twin alike, is a cascade of the P8 record: the environment's Borrow has P9's image of the
    // call result atom, an Unbound end resting on that record (PLAN G280).
    [Theory]
    [InlineData(Apply + Use + "    let outer = func (x: i32) => func [x@ref] () -> i32 => x@follow\n" + ApplyA, "func [x@ref] () -> i32 => x@follow", "a", null)]
    [InlineData(Apply + Use + "    let outer = func (x: i32) => (func [x@ref] () -> i32 => x@follow)\n" + ApplyA, "(func [x@ref] () -> i32 => x@follow)", "a", null)]
    [InlineData(Apply + Use + "    let outer = func (x: i32)\n        return func [x@ref] () -> i32 => x@follow\n" + ApplyA, "func [x@ref] () -> i32 => x@follow", "a", null)]
    [InlineData(Apply + Use + "    let outer = func (x: i32) => func [x@ref] () => x@follow\n" + ApplyA, "func [x@ref] () => x@follow", "a", null)]
    [InlineData(Apply + Use + "    let outer = func (x: i32)\n        return func [x@ref] () => x@follow\n" + ApplyA, "func [x@ref] () => x@follow", "a", null)]
    [InlineData(Generic + Use + "    let outer = func (x: i32) => func [x@ref] () -> i32 => x@follow\n" + GenericA, "func [x@ref] () -> i32 => x@follow", null, null)]
    [InlineData(Generic + Use + "    let outer = func (x: i32) => (func [x@ref] () -> i32 => x@follow)\n" + GenericA, "(func [x@ref] () -> i32 => x@follow)", null, null)]
    [InlineData(Generic + Use + "    let outer = func (x: i32)\n        return func [x@ref] () -> i32 => x@follow\n" + GenericA, "func [x@ref] () -> i32 => x@follow", null, null)]
    [InlineData(Generic + Use + "    let outer = func (x: i32) => func [x@ref] () => x@follow\n" + GenericA, "func [x@ref] () => x@follow", null, null)]
    [InlineData(Generic + Use + "    let outer = func (x: i32)\n        return func [x@ref] () => x@follow\n" + GenericA, "func [x@ref] () => x@follow", null, null)]
    [InlineData(
        Confined + "    let outer = func (x: i32, item: Item) => func [item@move, x@ref] () -> i32 => item.value\n" + ConfinedF,
        "func [item@move, x@ref] () -> i32 => item.value",
        null,
        "f@move")]
    [InlineData(
        Confined + "    let outer = func (x: i32, item: Item)\n        return func [item@move, x@ref] () -> i32 => item.value\n" + ConfinedF,
        "func [item@move, x@ref] () -> i32 => item.value",
        null,
        "f@move")]
    public void AUseOfAFailedReturnedLiteralReadsItsDefinition(string source, string literal, string? owned, string? effect)
    {
        var expected = $"{nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd)} {literal}" +
            (owned is null ? string.Empty : $"; {nameof(DiagnosticCode.UnprovenConstraint_Kd)} {owned}");
        Assert.Equal(expected, Records(source));

        var c = VerifiedSpecExampleTest.Bind(source);
        c.Analyze();
        var effects = MirDifferenceLedger.Sides(c, c.BuildMir()).Mir.Where(static x => x.Code == DiagnosticCode.UnsatisfiedEffectBound_Kd);
        Assert.Equal(effect is null ? [] : [effect], effects.Select(x => source.Substring(x.Node.Span.Start, x.Node.Span.Length)));
    }

    // SPEC 7.7.3, 23.1.2 (DECISIONS R4-M152): the receiver classification reads a nested Closure's definition, so a nested literal whose
    // erasure fails (its creation failed) still makes its maker Exclusive through its `n@uniq` capture: `c()` through a `let` is
    // InvalidAssignment_Kd at `c`, the independent record its created twin has (BU's zz3_n2 and zz3_n2ok).
    [Theory]
    [InlineData(
        "func run(f: () -> ()) => f()\n" + NestedUniq + "        run(func [n@uniq] () => ())\n" + NestedCall,
        "UnprovenConstraint_Kd func [n@uniq] () => (); InvalidAssignment_Kd c")]
    [InlineData(NestedUniq + "        let k = func [n@uniq] () => ()\n        k()\n" + NestedCall, "InvalidAssignment_Kd c")]
    public void ANestedClosureWhoseCreationFailedStillClassifiesItsMaker(string source, string records)
    {
        Assert.Equal(records, Records(source));
    }

    // SPEC 15.8.2, 13.5.6 (DECISIONS R4-M152): a Closure literal's own temporary is its ClosureValueSlot atom, the receiver Origin ρ of a
    // call on the literal, so a Borrow of it kept past its statement is the Loan conflict at its storage end on the default pipeline too
    // (its slot 0 read as the literal's parameter, which the old analysis rooted nowhere); used within its statement it is valid.
    [Theory]
    [InlineData(Inspect + "let n: i32 = 1\nlet r = (func [n] () => n)@ref\ninspect(r()@ref)", "ComparisonLoanConflict_Kd let r = (func [n] () => n)@ref")]
    [InlineData(Inspect + "let n: i32 = 1\ninspect((func [n] () => n)@ref()@ref)", "")]
    public void AClosureLiteralsTemporaryIsItsReceiverAtom(string source, string records)
    {
        var found = DiagnosticCorpus.Check(source).Diagnostics.Select(x => $"{x.Code} {source.Substring(x.Span!.Value.Start, x.Span.Value.Length)}");
        Assert.Equal(records, string.Join("; ", found));
    }

    [Fact]
    public void CommonFunctionErasureCannotHideAnExternalOrigin()
    {
        var c = MinimalEmissionTest.Analyze(Captured + "let erased: () -> ref/i32 = f\nerased()");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Validate(out _));
    }

    // SPEC 7.7.4, 15.6.1, 23.1: a common Function value cannot return a borrow of its hidden environment receiver, so the erasure of
    // a closure whose result borrows its environment is one UnprovenOriginContract_Kd at the converted value, whose longer end is the
    // closure display "call receiver", related at the anonymous function's header. It was displayed as a borrow of the closure's text.
    [Theory]
    [InlineData("    let n = 7\n    let f = func [n] () => n@ref\n    let e: () -> ref/i32 = f\n", "f")]
    [InlineData("    let text = \"abc\"\n    let f = func [text@move] () => text@ref\n    let e: () -> ref/string = f@move\n", "f@move")]
    public void AnErasedResultCannotBorrowTheCallReceiver(string body, string text)
    {
        var source = "public func main() -> ()\n" + body;
        var output = DiagnosticCorpus.Check(source);
        var error = Assert.Single(output.Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnprovenOriginContract_Kd), text), (error.Code, error.Span is { } span ? source.Substring(span.Start, span.Length) : string.Empty));
        var header = Assert.Single(error.Related!, static x => x.Role == "origin").Span!.Value;
        Assert.StartsWith("func [", source.Substring(header.Start, header.Length), StringComparison.Ordinal);
        Assert.EndsWith("] ()", source.Substring(header.Start, header.Length), StringComparison.Ordinal);
        Assert.Equal("A common Function value cannot return a borrow of its hidden environment receiver (SPEC 7.7.4)", error.Note);
        var result = new DiagnosticResult(output.Diagnostics, output.Sources);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("the result requires the closure's call receiver outlives static, which is not proven", console.Text, StringComparison.Ordinal);
        var json = JsonSerializer.Serialize(result, DiagnosticJsonContext.Default.DiagnosticResult);
        Assert.Contains("{\"name\":\"longer\",\"kind\":\"Origin\",\"value\":\"call receiver\",\"elided\":false,\"origin\":\"closure\"}", json, StringComparison.Ordinal);
    }

    [Fact]
    public void AReturnedViewCannotOutliveItsExternalOwner()
    {
        var c = MinimalEmissionTest.Analyze("func bad() -> ref/i32 during static\n    let n = 7\n    let values: [1 of ref/i32] = [n@ref]\n    let f = func [values] () => values[0]\n    return f()\nbad()");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.DoesNotContain(c.Binding.Issues, x => x.Failure == BindingFailure.Unsupported);
        Assert.False(c.Validate(out _));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmBindingAnalysisAndEmissionReuseFixedContracts()
    {
        var c = MinimalEmissionTest.Analyze(Captured + "let result = f()\nrequire result == 7 else => $abort(\"result\")");
        var expected = CompilationTestHelper.WriteIr(c);
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid);
        Assert.Equal(expected, CompilationTestHelper.WriteIr(c));
    }

    // A program's published records in source order, each as its code and its span's text.
    private static string Records(string source)
        => string.Join("; ", ContextualCaptureTest.PublishedRecords(source).OrderBy(static x => x.Span.Start)
            .Select(x => $"{x.Code} {source.Substring(x.Span.Start, x.Span.Length)}"));
}
