// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;
using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Verification;
using Xunit;

namespace XunitTest;

public class ClosureResultOriginTest
{
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
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ReturningAnExternalReferenceDoesNotRetainTheEnvironment()
        => NativeAllocationAudit.WriteFixture("ClosureResultOriginCleanup", "struct Item\n    public let id: i32 = 7\n    drop => Console.writeLine(\"drop\")\nlet owner = Kimi.Intrinsics.makeObj(Item.init())\nlet n = 7\nlet values: [1 of ref/i32] = [n@ref]\nlet f = func [values, owner@move] () => values[0]\nlet result = f@move()\nrequire result == 7 else => $abort(\"result\")", 1, 1, 20, "drop\n");

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void StoredObjectResultsKeepHeaderIdentityAndTheOwner()
        => NativeAllocationAudit.WriteFixture("ClosureResultOriginObject", "struct Item\n    public let id: i32 = 7\n    drop => Console.writeLine(\"drop\")\nlet owner = Kimi.Intrinsics.makeObj(Item.init())\nlet values: [1 of objref/Item] = [owner@objref]\nlet f = func [values] () => values[0]\nlet result = f()\nrequire result.id == 7 else => $abort(\"result\")", 1, 1, 20, "drop\n");

    // SPEC 15.8.2, 15.6.4, 7.6.3: a call's result over the closure's environment depends on the call receiver, which needs region
    // inference, so each such call is one Unsupported_Kd: a borrow of an environment binding, through a reference receiver, a Reborrow
    // through a captured exclusive reference, a result beside a per-call input, a temporary closure, a Consuming call that moves a
    // captured reference out and a returned closure that borrows the environment (compiler reduction R1; they ran since G65).
    [Theory]
    [InlineData("let n = 7\nlet f = func [n] () => n@ref\nlet r = f()", "f()")]
    [InlineData("let n = 7\nlet f = func [n] () => n@ref\nlet g = f@ref\nlet r = g()", "g()")]
    [InlineData("var n = 7\nlet view = n@uniq\nlet f = func [view] () => view@follow@ref\nlet r = f()", "f()")]
    [InlineData("func pick(a: ref/i32, b: ref/i32) -> ref/i32 => a\nlet n = 7\nlet f = func [n] (x: ref/i32) => pick(n@ref, x)\nlet m = 8\nlet r = f(m@ref)", "f(m@ref)")]
    [InlineData("let n = 7\nlet r = (func [n] () => n@ref)()", "(func [n] () => n@ref)()")]
    [InlineData("var n = 7\nlet view = n@uniq\nvar f = func [view] () => view\nlet r = f()", "f()")]
    [InlineData("var n = 7\nlet view = n@uniq\nlet take = func [view@move] () => view@move\nlet r = take@move()", "take@move()")]
    [InlineData("let n = 7\nlet outer = func [n] () => func [n@ref] () => n\nlet inner = outer()", "outer()")]
    public void AResultOverTheEnvironmentIsUnsupported(string source, string call)
    {
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.Unsupported_Kd), call), (error.Code, source.Substring(error.Span!.Value.Start, error.Span.Value.Length)));
    }

    [Fact]
    public void CommonFunctionErasureCannotHideAnExternalOrigin()
    {
        var c = MinimalEmissionTest.Analyze(Captured + "let erased: () -> ref/i32 = f\nerased()");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Emission.Validate(out _));
    }

    // SPEC 7.6.4, 15.6.1, 23.3.6.5: a common Function value cannot return a borrow of its hidden environment receiver, so the erasure of
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
        Assert.Equal("A common Function value cannot return a borrow of its hidden environment receiver (SPEC 7.6.4)", error.Note);
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
        Assert.False(c.Emission.Validate(out _));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmBindingAnalysisAndEmissionReuseFixedContracts()
    {
        var c = MinimalEmissionTest.Analyze(Captured + "let result = f()\nrequire result == 7 else => $abort(\"result\")");
        var expected = CompilationTestHelper.WriteIr(c);
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete, iterations: 64, warmupIterations: 32));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified, iterations: 64, warmupIterations: 32));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _), iterations: 64, warmupIterations: 32));
        Assert.True(valid);
        Assert.Equal(expected, CompilationTestHelper.WriteIr(c));
    }
}
