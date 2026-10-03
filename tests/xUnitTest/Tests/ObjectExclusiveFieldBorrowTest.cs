// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class ObjectExclusiveFieldBorrowTest(ITestOutputHelper output)
{
    private const string Item = "struct Item\n    public var id: i32 = 7\n    drop => Console.writeLine(\"drop\")\n";

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("Direct", "let field = owner.id@uniq\nfield@follow += 2", "owner.id")]
    [InlineData("Argument", "func edit(field: uniq/i32) => field@follow += 2\nedit(owner.id@uniq)", "owner.id")]
    [InlineData("TwoPhase", "func edit(field: uniq/i32, old: i32) => field@follow = old + 2\nedit(owner.id@uniq, owner.id)", "owner.id")]
    [InlineData("Stored", "var stored = (owner@move, 1)\nlet view = stored.0@objuniq\nlet field = view.id@uniq\nfield@follow += 2", "stored.0.id")]
    [InlineData("StoredArgument", "var stored = (owner@move, 1)\nfunc edit(view: objuniq/Item, old: i32)\n    let field = view.id@uniq\n    field@follow = old + 2\nedit(stored.0@objuniq, stored.0.id)", "stored.0.id")]
    public void MutableOwningPathsLendExclusiveFieldStorage(string name, string edit, string read)
        => NativeAllocationAudit.WriteFixture("ObjectExclusiveField" + name, Item + "var owner = Kimi.Intrinsics.makeObj(Item.init())\n" + edit + "\nrequire " + read + " == 9 else => $abort(\"edit\")", 1, 1, 20, "drop\n");

    [Theory]
    [InlineData("let owner = Kimi.Intrinsics.makeObj(Item.init())\nlet field = owner.id@uniq")]
    [InlineData("var owner = Kimi.Intrinsics.makeObj(Item.init())\nlet view = owner@objref\nlet field = view.id@uniq")]
    public void SharedOrImmutablePathsDoNotGainExclusiveAuthority(string use)
    {
        var c = MinimalEmissionTest.Analyze(Item + use);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Emission.Validate(out _));
    }

    [Fact]
    public void ExclusiveFieldLoansProtectTheOwningHandle()
    {
        var c = MinimalEmissionTest.Analyze(Item + "var owner = Kimi.Intrinsics.makeObj(Item.init())\nlet field = owner.id@uniq\nlet moved = owner@move\nrequire field == 7 else => $abort(\"live\")");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmExclusiveFieldPlansAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Item + "var owner = Kimi.Intrinsics.makeObj(Item.init())\nlet field = owner.id@uniq\nfield@follow += 2\nrequire owner.id == 9 else => $abort(\"edit\")");
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var issue), MinimalEmissionTest.Describe(c, issue));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified, iterations: 64, warmupIterations: 32));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _), iterations: 64, warmupIterations: 32));
        Assert.True(valid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublicOutputsDistinguishLegalBorrowsFromOwnerConflicts(bool conflict)
    {
        var path = Path.GetFullPath("exclusive-object-field.kimi");
        var text = Item + "var owner = Kimi.Intrinsics.makeObj(Item.init())\nlet field = owner.id@uniq\n" +
            (conflict ? "let moved = owner@move\n" : string.Empty) + "require field == 7 else => $abort(\"live\")";
        var c = MinimalEmissionTest.Analyze(text, path);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        c.Binding.ReportDiagnostics();
        c.Ownership.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        if (conflict)
        {
            var error = Assert.Single(result.Diagnostics);
            Assert.Equal("ComparisonLoanConflict_Kd", error.Code);
            Assert.Equal("owner", text.Substring(error.Span!.Value.Start, error.Span.Value.Length));
            Assert.Contains(error.Message, console.Text, StringComparison.Ordinal);
            foreach (var related in new[] { false, true })
            {
                var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
                Assert.Equal(error.Display!.Range, sent.Range);
                Assert.Contains(error.Message, sent.Message, StringComparison.Ordinal);
                output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
            }
        }
        else
        {
            Assert.Empty(result.Diagnostics);
            Assert.Empty(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, true)[identity]);
        }
    }
}
