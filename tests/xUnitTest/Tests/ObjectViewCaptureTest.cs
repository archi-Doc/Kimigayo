// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class ObjectViewCaptureTest(ITestOutputHelper output)
{
    private const string Cell = "struct Cell\n    public var value: i32 = 7\n    drop => Console.writeLine(\"drop\")\n";
    private const string Shared = "func read(input: objref/Cell) -> i32\n    let operation = func[input]() -> i32 => input.value\n    return operation() + operation()\nlet owner = Cell.init()@obj\nrequire read(owner@objref) == 14 else => $abort(\"shared\")";

    [Theory]
    [InlineData("obj")]
    [InlineData("rc")]
    [InlineData("arc")]
    public void SharedObjectViewsCanBeCopiedIntoConcreteEnvironments(string ownership)
        => NativeAllocationAudit.WriteFixture("ObjectViewCaptureShared" + ownership, Cell + Shared.Replace("Cell.init()@obj", "Cell.init()@" + ownership, StringComparison.Ordinal), 1, 1, 20, "drop\n");

    [Fact]
    public void ExclusiveObjectViewsReborrowIntoMutableEnvironments()
    {
        const string Source = "func increment(input: objuniq/Cell) -> i32\n    var operation = func[input]() -> i32\n        input.value += 1\n        return input.value\n    return operation() + operation()\nvar owner = Cell.init()@obj\nrequire increment(owner@objuniq) == 17 and owner.value == 9 else => $abort(\"exclusive\")";
        NativeAllocationAudit.WriteFixture("ObjectViewCaptureExclusive", Cell + Source, 1, 1, 20, "drop\n");
    }

    [Fact]
    public void MovingAnExclusiveViewTransfersTheExistingLoan()
    {
        const string Source = "func increment(input: objuniq/Cell) -> i32\n    var operation = func[input@move]() -> i32\n        input.value += 1\n        return input.value\n    return operation()\nvar owner = Cell.init()@obj\nrequire increment(owner@objuniq) == 8 and owner.value == 8 else => $abort(\"move\")";
        NativeAllocationAudit.WriteFixture("ObjectViewCaptureMove", Cell + Source, 1, 1, 20, "drop\n");
    }

    [Theory]
    [InlineData("var owner = Cell.init()@obj\nlet view = owner@objref\nlet operation = func[view]() -> i32 => view.value\nowner = Cell.init()@obj\nlet result = operation()", "ComparisonLoanConflict_Kd")]
    [InlineData("func use(input: objuniq/Cell) -> i32\n    var operation = func[input]() -> i32 => input.value\n    input.value += 1\n    return operation()\n()", "ComparisonLoanConflict_Kd")]
    [InlineData("func use(input: objuniq/Cell) -> i32\n    let operation = func[input@move]() -> i32 => input.value\n    let invalid = input.value\n    return operation()\n()", "MovedPlace_Kd")]
    [InlineData("func leak(input: objref/Cell) -> () -> i32 => func[input]() -> i32 => input.value\n()", "UnprovenConstraint_Kd")]
    public void CapturesPreserveLoansTransfersAndErasureRequirements(string body, string code)
    {
        var result = DiagnosticCorpus.Check(Cell + body);
        Assert.Contains(result.Diagnostics, x => x.Code == code);
        Assert.DoesNotContain(result.Diagnostics, x => x.Code is "UnsupportedBinding_Kd" or "UnsupportedOwnership_Kd");
    }

    [Fact]
    public void TheOriginalExclusiveViewIsAvailableAfterTheLastClosureUse()
    {
        const string Source = "func increment(input: objuniq/Cell) -> i32\n    let operation = func[input]() -> i32 => input.value\n    let before = operation()\n    input.value += 1\n    return before + input.value\nvar owner = Cell.init()@obj\nrequire increment(owner@objuniq) == 15 else => $abort(\"last use\")";
        NativeAllocationAudit.WriteFixture("ObjectViewCaptureLastUse", Cell + Source, 1, 1, 20, "drop\n");
    }

    [Fact]
    public void AConflictingOwnerReplacementKeepsItsLoanFactsInCliAndLsp()
    {
        var source = Cell + "var owner = Cell.init()@obj\nlet view = owner@objref\nlet operation = func[view]() -> i32 => view.value\nowner = Cell.init()@obj\nlet result = operation()";
        var path = Path.GetFullPath("object-view-capture.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        c.Binding.ReportDiagnostics();
        c.Ownership.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var record = Assert.Single(result.Diagnostics, x => x.Code == "ComparisonLoanConflict_Kd");
        Assert.Equal(DiagnosticCategory.Language, record.Category);
        Assert.Equal("owner = Cell.init()@obj", source.Substring(record.Span!.Value.Start, record.Span.Value.Length));
        Assert.NotEmpty(record.Related!);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("active loan", console.Text, StringComparison.Ordinal);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity], x => x.Code == record.Code);
            Assert.Equal(record.Display!.Range, sent.Range);
            Assert.Contains("active loan", sent.Message, StringComparison.Ordinal);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmObjectViewCapturesReuseBindingOwnershipAndEmissionPlans()
    {
        var c = MinimalEmissionTest.Analyze(Cell + Shared);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var issue), MinimalEmissionTest.Describe(c, issue));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete, iterations: 64, warmupIterations: 32));
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified, iterations: 64, warmupIterations: 32));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _), iterations: 64, warmupIterations: 32));
        Assert.True(valid);
    }
}
