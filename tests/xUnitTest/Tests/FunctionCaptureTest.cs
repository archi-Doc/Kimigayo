// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class FunctionCaptureTest(ITestOutputHelper output)
{
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("Concrete", "let inner: () -> i32 = func () => 7\nlet outer = func [inner@move] () => inner() + 1\nrequire outer() == 8 and outer() == 8 else => $abort(\"nested\")", 0, 0)]
    [InlineData("Erased", "let inner: () -> i32 = func () => 7\nlet outer: () -> i32 = func [inner@move] () => inner() + 1\nrequire outer() == 8 and outer() == 8 else => $abort(\"nested\")", 1, 16)]
    [InlineData("Borrow", "let inner: () -> i32 = func () => 7\nlet outer = func [inner@ref] () => inner() + 1\nrequire outer() == 8 and inner() == 7 else => $abort(\"borrow\")", 0, 0)]
    [InlineData("Return", "func wrap(inner: () -> i32) -> () -> i32 => func [inner@move] () => inner() + 1\nlet outer = wrap(func () => 7)\nrequire outer() == 8 and outer() == 8 else => $abort(\"return\")", 1, 16)]
    public void FunctionValuesCanBeCapturedAndCalledRepeatedly(string name, string source, int count, int bytes)
        => NativeAllocationAudit.WriteFixture("FunctionCapture" + name, source, count, count, bytes, string.Empty);

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void NestedHeapEnvironmentsReleaseTheirPayloadExactlyOnce()
    {
        const string Source = """
            struct Item
                public let id: i32 = 7
                drop => Console.writeLine("drop")
            let owner = Kimi.Intrinsics.makeObj(Item.init())
            let inner: () -> i32 = func [owner@move] () => owner.id
            let outer: () -> i32 = func [inner@move] () => inner() + 1
            require outer() == 8 and outer() == 8 else => $abort("nested")
            """;
        NativeAllocationAudit.WriteFixture("FunctionCaptureNested", Source, 3, 3, 44, "drop\n");
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ReplacingACapturedFunctionDropsTheOldEnvironment()
    {
        const string Source = """
            struct Item
                public let id: i32 = 7
                drop => Console.writeLine("drop")
            let owner = Kimi.Intrinsics.makeObj(Item.init())
            let inner: () -> i32 = func [owner@move] () => owner.id
            var outer = func [var inner@move] () -> i32
                inner = func () => 9
                return inner()
            require outer() == 9 and outer() == 9 else => $abort("replacement")
            Console.writeLine("done")
            """;
        NativeAllocationAudit.WriteFixture("FunctionCaptureReplacement", Source, 2, 2, 28, "drop\ndone\n");
    }

    [Theory]
    [InlineData("let outer = func [inner] () => inner()")]
    [InlineData("let outer = func [inner@move] () => inner()\ninner()")]
    [InlineData("let outer: () -> i32 = func [inner@ref] () => inner()")]
    [InlineData("let outer = func [var inner@move] () -> i32\n    inner = func () => 9\n    return inner()\nouter()")]
    public void FunctionCapturesRetainMoveBorrowAndWriteRequirements(string source)
    {
        var c = MinimalEmissionTest.Analyze("let inner: () -> i32 = func () => 7\n" + source);
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
        Assert.False(c.Emission.Validate(out _));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmNestedFunctionEmissionAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze("let inner: () -> i32 = func () => 7\nlet outer: () -> i32 = func [inner@move] () => inner() + 1\nrequire outer() == 8 else => $abort(\"call\")");
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var issue), MinimalEmissionTest.Describe(c, issue));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _), iterations: 64, warmupIterations: 32));
        Assert.True(valid);
    }

    [Fact]
    public void BareFunctionCaptureExplainsItsNonCopyAcquisition()
    {
        const string Source = "let inner: () -> i32 = func () => 7\nlet outer = func [inner] () => inner()";
        var path = Path.GetFullPath("function-capture.kimi");
        var c = MinimalEmissionTest.Analyze(Source, path);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal("TransferRequired_Kd", error.Code);
        Assert.Equal(Source.IndexOf("[inner]", StringComparison.Ordinal) + 1, error.Span!.Value.Start);
        Assert.Equal(5, error.Span.Value.Length);
        Assert.Contains("() -> i32 is neither proven Copy", error.Note, StringComparison.Ordinal);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains(error.Note!, console.Text, StringComparison.Ordinal);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(error.Display!.Range, sent.Range);
            Assert.Contains(error.Note!, sent.Message, StringComparison.Ordinal);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }
}
