// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class ContextualClosureTest(ITestOutputHelper output)
{
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("String", "let text = \"owned\"\nlet f: () -> bool = func [text@move] () => text == \"owned\"\nrequire f() and f() else => $abort(\"capture\")", 1, 24, "")]
    [InlineData("Object", "struct Item\n    public let id: i32 = 7\n    drop => Console.writeLine(\"drop\")\nlet owner = Kimi.Intrinsics.makeObj(Item.init())\nlet f: () -> i32 = func [owner@move] () => owner.id\nrequire f() == 7 and f() == 7 else => $abort(\"capture\")", 2, 28, "drop\n")]
    [InlineData("Large", "let a: i64 = 1\nlet b: i64 = 2\nlet c: i64 = 3\nlet f: () -> i64 = func [a, b, c] () => a + b + c\nrequire f() == 6 else => $abort(\"capture\")", 1, 24, "")]
    [InlineData("Parameter", "func make(offset: i32) -> (i32) -> i32 => func [offset] (value) => value + offset\nlet f = make(7)\nrequire f(2) == 9 else => $abort(\"parameter\")", 0, 0, "")]
    [InlineData("Argument", "func use(f: () -> bool) -> bool => f()\nlet text = \"owned\"\nrequire use(func [text@move] () => text == \"owned\") else => $abort(\"argument\")", 1, 24, "")]
    public void DirectExpectedFunctionsUseOwnedConcreteEnvironments(string name, string source, int count, int bytes, string stdout)
        => NativeAllocationAudit.WriteFixture("ContextualClosure" + name, source, count, count, bytes, stdout);

    [Theory]
    [InlineData("let text = \"owned\"\nlet f: () -> bool = func [text] () => text == \"owned\"")]
    [InlineData("let n = 7\nlet f: () -> i32 = func [n@ref] () => n + 1")]
    [InlineData("let n = 7\nlet f: () -> i32 = func [var n] () => ++n")]
    [InlineData("let text = \"owned\"\nlet f: () -> string = func [text@move] () => text@move")]
    [InlineData("let text = \"owned\"\nlet f: () -> bool = func [text@move] () => text == \"owned\"\nConsole.writeLine(text)")]
    public void CommonConversionPreservesCaptureAndReceiverRules(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    // SPEC 15.2.3: an Owned failure is a Constraint record; the receiver conditions stay Type mismatches with their Notes.
    [InlineData("let n = 7\nlet f: () -> i32 = func [n@ref] () => n + 1", "Owned environment", "UnsatisfiedConstraint_Kd")]
    [InlineData("let n = 7\nlet f: () -> i32 = func [var n] () => ++n", "Exclusive call")]
    [InlineData("let text = \"owned\"\nlet f: () -> string = func [text@move] () => text@move", "Consuming call")]
    [InlineData("let n = 7\nlet outer: () -> (() -> i32) = func [n] () => func [n@move] () => n", "Consuming call")]
    public void ErasureFailuresExplainTheFailedContract(string source, string cause, string code = "TypeMismatch_Kd")
    {
        var path = Path.GetFullPath("contextual-closure.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal(code, error.Code);
        Assert.StartsWith("func [", source.Substring(error.Span!.Value.Start, error.Span.Value.Length), StringComparison.Ordinal);
        Assert.Contains(cause, error.Note, StringComparison.Ordinal);
        Assert.Null(error.Repairs);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains(cause, console.Text, StringComparison.Ordinal);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(error.Display!.Range, sent.Range);
            Assert.Contains(cause, sent.Message, StringComparison.Ordinal);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }

        c.Bind();
        c.Binding.ReportDiagnostics();
        Assert.Equal(result, c.Diagnostics.Finalize());
    }

    [Fact]
    public void CaptureErrorsKeepTheirCausesBesideIndependentErrors()
    {
        const string Source = "let text = \"owned\"\nlet f: () -> bool = func [text] () => text == \"owned\"\nlet wrong: i32 = true";
        var c = MinimalEmissionTest.Analyze(Source);
        c.Binding.ReportDiagnostics();
        var result = c.Diagnostics.Finalize();
        Assert.Equal(2, result.Diagnostics.Length);
        var capture = Assert.Single(result.Diagnostics, x => x.Code == "TransferRequired_Kd");
        Assert.Equal("text", Source.Substring(capture.Span!.Value.Start, capture.Span.Value.Length));
        Assert.Contains("neither proven Copy", capture.Note, StringComparison.Ordinal);
        Assert.Single(result.Diagnostics, x => x.Code == "TypeMismatch_Kd");
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmContextualBindingAndEmissionReuseTheirPlans()
    {
        var c = MinimalEmissionTest.Analyze("let text = \"owned\"\nlet f: () -> bool = func [text@move] () => text == \"owned\"\nrequire f() else => $abort(\"call\")");
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var issue), MinimalEmissionTest.Describe(c, issue));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete, iterations: 64, warmupIterations: 32));
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _), iterations: 64, warmupIterations: 32));
        Assert.True(valid);
    }

    // SPEC 7.6.4, 23.3.6.5: a closure that fails its conversion at a call argument or a default is shown by its signature, with
    // the conversion Note, exactly as the same closure at a let annotation (N29a; it was "found closure " without a Note). Owned
    // failures are Constraint records (ErasureOwnedDiagnosticTest).
    [Theory]
    [InlineData("func call(action: (i32) -> i32, x: i32) -> i32\n    return action(x)\n\npublic func main() -> ()\n    let c: i32 = 0\n    require call(func [var c] (n) -> i32\n        c += 1\n        return n + c\n    , 1) == 2 else => $abort(\"k2\")\n", "expected (i32) -> i32, found closure (i32) -> i32", "This closure requires an Exclusive call; a common Function value permits Shared calls only")]
    public void AFailedClosureConversionShowsTheClosure(string source, string label, string note)
    {
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.TypeMismatch_Kd), error.Code);
        Assert.Equal(note, error.Note);
        var result = new Kimi.Diagnostics.DiagnosticResult([error], DiagnosticCorpus.Check(source).Sources);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains(label, console.Text, StringComparison.Ordinal);
    }
}
