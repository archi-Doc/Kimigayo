// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class GenericDefaultTest
{
    private const string GenericCapture = "func f<T>(x: T, action: () -> T = func [x] () => x) -> T\n    T is Copy and Owned\n    return action()\n";
    private const string GenericCall = "func copy<T>(x: T) -> T\n    T is Copy\n    return x\nfunc f<T>(x: T, y: T = label work: do\n    let copied = copy(x)\n    exit to work copied\n) -> T\n    T is Copy\n    return y\nrequire f(7) == 7 and f(true) else => $abort(\"call\")";

    [Theory]
    [InlineData("CopyScalar", "func f<T>(x: T, y: T = x) -> T\n    T is Copy\n    return y\nrequire f(3) == 3 and f(true) else => $abort(\"copy\")")]
    [InlineData("CopyReference", "func f<T>(x: T, y: T = x) -> T\n    T is Copy\n    return y\nlet n = 7\nlet r = f(n@ref)\nrequire r@follow == 7 else => $abort(\"reference\")")]
    [InlineData("IntegerLiteral", "func f<T>(value: T = 0) -> T\n    T is PrimitiveInteger\n    return value\nrequire f<i32>() == 0 and f<i64>() == 0 else => $abort(\"literal\")")]
    [InlineData("Tuple", "func f<T>(x: T, pair: (T, T) = (x, x)) -> T\n    T is Copy\n    return pair.1\nrequire f(5) == 5 else => $abort(\"tuple\")")]
    [InlineData("FunctionItem", "func identity<T>(x: T) -> T => x@move\nfunc f<T>(x: T, action: (T) -> T = identity) -> T\n    T is Owned\n    return action(x@move)\nrequire f(7) == 7 else => $abort(\"item\")")]
    [InlineData("FunctionLiteral", "func f<T>(x: T, action: (i32) -> i32 = func (n) => n + 1) -> i32\n    T is Owned\n    return action(2)\nrequire f(true) == 3 else => $abort(\"literal\")")]
    [InlineData("FunctionItemTwoTypes", "func identity<T>(x: T) -> T => x@move\nfunc f<T>(x: T, action: (T) -> T = identity) -> T\n    T is Owned\n    return action(x@move)\nrequire f(7) == 7 and f(true) else => $abort(\"types\")")]
    [InlineData("GenericCall", "func copy<T>(x: T) -> T\n    T is Copy\n    return x\nfunc f<T>(x: T, y: T = copy(x)) -> T\n    T is Copy\n    return y\nrequire f(7) == 7 and f(true) else => $abort(\"call\")")]
    [InlineData("Forwarded", "func f<T>(x: T, y: T = x) -> T\n    T is Copy\n    return y\nfunc g<T>(x: T) -> T\n    T is Copy\n    return f(x)\nrequire g(7) == 7 and g(true) else => $abort(\"forward\")")]
    [InlineData("Capture", "func f<T>(x: T, action: () -> T = func [x] () => x) -> T\n    T is Copy and Owned\n    return action()\nrequire f(7) == 7 and f(true) else => $abort(\"capture\")")]
    [InlineData("Array", "func f<T>(x: T, values: [2 of T] = [x, x]) -> T\n    T is Copy\n    return values[1]\nrequire f(7) == 7 and f(true) else => $abort(\"array\")")]
    [InlineData("Local", "func f<T>(x: T, y: T = label work: do\n    let copied = x\n    exit to work copied\n) -> T\n    T is Copy\n    return y\nrequire f(7) == 7 and f(true) else => $abort(\"local\")")]
    [InlineData("AggregateJoin", "func f<T>(x: T, yes: bool, y: (T, T) = if yes => (x, x) else => (x, x)) -> T\n    T is Copy\n    return y.0\nrequire f(7, true) == 7 and f(true, false) else => $abort(\"join\")")]
    [InlineData("LocalBorrow", "func inspect<T>(r: ref/T) -> T\n    T is Copy\n    return r@follow\nfunc f<T>(x: T, y: T = label work: do\n    let copied = x\n    exit to work inspect(copied@ref)\n) -> T\n    T is Copy\n    return y\nrequire f(7) == 7 and f(true) else => $abort(\"local borrow\")")]
    [InlineData("LocalCapture", "func f<T>(x: T, action: () -> T = label work: do\n    let copied = x\n    exit to work func [copied] () => copied\n) -> T\n    T is Copy and Owned\n    return action()\nrequire f(7) == 7 and f(true) else => $abort(\"local capture\")")]
    [InlineData("Match", "func f<T>(x: T, y: T = match x@copy\n    let captured => captured\n) -> T\n    T is Copy\n    return y\nrequire f(7) == 7 and f(true) else => $abort(\"match\")")]
    [InlineData("TupleCapture", GenericCapture + "let pair = f((3, true))\nrequire pair.0 == 3 and pair.1 else => $abort(\"tuple capture\")")]
    [InlineData("BorrowLocal", "func inspect<T>(r: ref/T) -> T\n    T is Copy\n    return r@follow\nfunc f<T>(x: T, y: T = label work: do\n    var copied = x\n    let r = copied@ref\n    exit to work inspect(r)\n) -> T\n    T is Copy\n    return y\nrequire f(7) == 7 and f(true) else => $abort(\"borrow local\")")]
    public void DefaultsAreInstantiatedAfterUniversalChecking(string name, string source)
        => ScalarEmissionTest.EmitFixture("GenericDefault" + name, source, string.Empty);

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void GenericEnvironmentIsAllocatedAndFreedOnce()
        => NativeAllocationAudit.WriteFixture("GenericDefaultEnvironment", GenericCapture + "let value = f((1, 2, 3))\nrequire value.2 == 3 else => $abort(\"environment\")", 1, 1, 12);

    [Fact]
    public void RebindingAndReloadKeepCallContexts()
    {
        var c = MinimalEmissionTest.Analyze(GenericCall);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), MinimalEmissionTest.Describe(c, error));
        Assert.True(c.Bind().IsComplete);
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out error), MinimalEmissionTest.Describe(c, error));
        var restored = CompilationTestHelper.Reload(c);
        Assert.True(restored.Bind().IsComplete);
        restored.Binding.CheckStartup(OutputKind.Application);
        Assert.True(restored.Ownership.Analyze().IsVerified);
        Assert.True(restored.Emission.WriteIr(TextWriter.Null, out error), MinimalEmissionTest.Describe(restored, error));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\nf(7, 1)\nf(true, false)")]
    public void DefaultLocalLoansAreCheckedEvenWithoutEvaluation(string calls)
    {
        const string Declaration = "func inspect<T>(r: ref/T) -> T\n    T is Copy\n    return r@follow\nfunc f<T>(x: T, y: T = label work: do\n    var copied = x\n    let r = copied@ref\n    copied = x\n    exit to work inspect(r)\n) -> T\n    T is Copy\n    return y\n";
        var source = Declaration + (calls.Length == 0 ? "public func main() => ()" : calls);
        var path = Path.GetFullPath("generic-default.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, issue => issue.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.Validate(out _));
        c.Ownership.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.ComparisonLoanConflict_Kd), DiagnosticSeverity.Error, DiagnosticCategory.Language), (record.Code, record.Severity, record.Category));
        Assert.Equal(source.IndexOf("copied = x\n    exit", StringComparison.Ordinal), record.Span!.Value.Start);
        Assert.Contains(record.Related!, related => related.Role == "loan" && related.Span is { } span && source.Substring(span.Start, span.Length) == "let r = copied@ref");
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains(record.Message, console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal((record.Display!.Range, record.Code), (sent.Range, sent.Code));
            Assert.Contains(record.Message, sent.Message, StringComparison.Ordinal);
        }
    }

    [TestClass(DisableParallelization = true)]
    [Trait("Purpose", "Allocation")]
    public class AllocationTests
    {
        [Theory]
        [InlineData(GenericCapture + "require f(7) == 7 and f(true) else => $abort(\"capture\")")]
        [InlineData(GenericCall)]
        public void WarmGenericDefaultsAllocateNothing(string source)
        {
            var c = MinimalEmissionTest.Analyze(source);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), MinimalEmissionTest.Describe(c, error));
            var valid = true;
            Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
            Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
            Assert.True(valid);
        }
    }
}
