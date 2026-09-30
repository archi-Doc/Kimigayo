// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 13.5.5.2, 15.6.3: a child reference retains its actual parent's authority.</summary>
public class StoredReferenceLoanTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData("", "value@follow")]
    [InlineData(" during a", "value@follow")]
    [InlineData(" during a", "value")]
    public void ExclusiveChildrenSuspendParentValueReads(string origin, string read)
    {
        var source = "func run(value: uniq/i32" + origin + ")\n    let child = value@follow@uniq\n    let snapshot: i32 = " + read + "\n    child@follow = 99";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.DoesNotContain(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("Shared", "ref", "value@follow")]
    [InlineData("Child", "uniq", "child@follow")]
    public void CompatibleReadsRemainAllowed(string name, string mode, string read)
    {
        var source = "func run(value: uniq/i32 during a)\n    let child = value@follow@" + mode + "\n    let snapshot: i32 = " + read + "\n    require child == snapshot else => $abort(\"value\")\nvar value = 42\nrun(value@uniq)";
        ScalarEmissionTest.EmitFixture("StoredReferenceLoanRead" + name, source, string.Empty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ParentReadDiagnosticsIdentifyEachReadAndTheExclusiveChild(bool independent)
    {
        var source = "func run(value: uniq/i32 during a)\n    let child = value@follow@uniq\n    let first: i32 = value\n" +
            (independent ? "    let second: i32 = value\n" : string.Empty) + "    child@follow = 99";
        var path = Path.GetFullPath("parent-read.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        Assert.True(c.Binding.Result.IsComplete);
        c.Ownership.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        Assert.Equal(independent ? 2 : 1, result.Diagnostics.Length);
        foreach (var error in result.Diagnostics)
        {
            Assert.Equal("ComparisonLoanConflict_Kd", error.Code);
            Assert.Equal(DiagnosticSeverity.Error, error.Severity);
            Assert.Equal("value", source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
            Assert.Equal("This operation conflicts with an active loan", error.Message);
            var retained = Assert.Single(error.Related!);
            Assert.Equal("loan", retained.Role);
            Assert.Contains("child", source.Substring(retained.Span!.Value.Start, retained.Span.Value.Length), StringComparison.Ordinal);
        }

        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("value retaining the conflicting loan", console.Text, StringComparison.Ordinal);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity];
            Assert.Equal(result.Diagnostics.Length, sent.Length);
            for (var i = 0; i < sent.Length; i++)
            {
                Assert.Equal(result.Diagnostics[i].Display!.Range, sent[i].Range);
                if (related)
                {
                    Assert.Equal(result.Diagnostics[i].Related![0].Range, Assert.Single(sent[i].RelatedInformation!).Location.Range);
                }
                else
                {
                    Assert.Contains("value retaining the conflicting loan", sent[i].Message, StringComparison.Ordinal);
                }
            }
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" during a")]
    public void ASharedChildSuspendsExclusiveParentAccess(string origin)
    {
        var source = "func change(value: uniq/i32) => value@follow = 99\nfunc run(value: uniq/i32" + origin + ")\n    let found = value@follow@ref\n    change(value)\n    require found == 42 else => $abort(\"value\")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.Contains(c.Ownership.Issues, static x => x.Code == Kimi.DiagnosticCode.CallActivationConflict_Kd);
        Assert.DoesNotContain(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" during a")]
    public void ParentAccessResumesAfterTheChildsLastUse(string origin)
    {
        var source = "func change(value: uniq/i32) => value@follow = 99\nfunc run(value: uniq/i32" + origin + ")\n    let found = value@follow@ref\n    require found == 42 else => $abort(\"value\")\n    change(value)\nvar value = 42\nrun(value@uniq)\nrequire value == 99 else => $abort(\"change\")";
        ScalarEmissionTest.EmitFixture("StoredReferenceLoanResumed" + (origin.Length == 0 ? "Implicit" : "Named"), source, string.Empty);
    }

    [Fact]
    public void EqualOriginNamesDoNotMergeIndependentInputCapabilities()
    {
        const string source = "func change(value: uniq/i32) => value@follow = 99\nfunc run(left: uniq/i32 during a, right: uniq/i32 during a)\n    let found = left@follow@ref\n    change(right)\n    require found == 42 else => $abort(\"value\")\nvar left = 42\nvar right = 0\nrun(left@uniq, right@uniq)\nrequire right == 99 else => $abort(\"change\")";
        ScalarEmissionTest.EmitFixture("StoredReferenceLoanIndependent", source, string.Empty);
    }

    [Theory]
    [InlineData("let found = value@follow@ref", "value@follow = 99")]
    [InlineData("let found = value@follow@uniq", "let other = value@follow@ref\n    require other == 42 else => $abort(\"other\")")]
    [InlineData("let first = value@follow@ref\n    let found = first", "change(value)")]
    [InlineData("let first = value@follow@uniq\n    let found = first@follow@ref", "change(first)")]
    [InlineData("let found = value@follow@ref\n    let moved = value@move", "change(moved)")]
    [InlineData("let found = value@follow@ref\n    let moved = value@move", "moved@follow = 99")]
    public void ParentRestrictionsFollowAcquisitionAndCopies(string acquisition, string mutation)
    {
        var source = "func change(value: uniq/i32) => value@follow = 99\nfunc run(value: uniq/i32 during a)\n    " + acquisition + "\n    " + mutation + "\n    require found == 42 else => $abort(\"value\")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.DoesNotContain(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Fact]
    public void AChildRetainsItsInputAfterTransferringTheParentReference()
    {
        const string source = "func change(value: uniq/i32) => value@follow = 99\nfunc run(value: uniq/i32 during a)\n    let found = value@follow@ref\n    let moved = value@move\n    require found == 42 else => $abort(\"value\")\n    change(moved)\nvar value = 42\nrun(value@uniq)\nrequire value == 99 else => $abort(\"changed\")";
        ScalarEmissionTest.EmitFixture("StoredReferenceLoanTransferred", source, string.Empty);
    }

    [Fact]
    public void SplitIteratorValuesStillProtectTheirOwnChildren()
    {
        const string source = "var entries = [1: 42]\nvar iterator = entries.iterateUniq()\nmatch iterator.next()\n    .Some((let key, let value))\n        let child = value@follow@ref\n        value@follow = 99\n        require child == 42 and key == 1 else => $abort(\"child\")\n    .None => $abort(\"missing\")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.DoesNotContain(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublicDiagnosticsIdentifyTheConflictAndRetainedReference(bool independent)
    {
        var source = "func change(value: uniq/i32) => value@follow = 99\nfunc run(value: uniq/i32 during a)\n    let found = value@follow@ref\n    change(value)\n" +
            (independent ? "    change(value)\n" : string.Empty) + "    require found == 42 else => $abort(\"value\")";
        var path = Path.GetFullPath("retained-loan.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        c.Ownership.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        Assert.Equal(independent ? 2 : 1, result.Diagnostics.Length);
        foreach (var error in result.Diagnostics)
        {
            Assert.Equal("CallActivationConflict_Kd", error.Code);
            Assert.Equal(DiagnosticSeverity.Error, error.Severity);
            Assert.Equal("change(value)", source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
            Assert.Contains("cannot activate", error.Message, StringComparison.Ordinal);
            var retained = Assert.Single(error.Related!);
            Assert.Equal("loan", retained.Role);
            Assert.Contains("found", source.Substring(retained.Span!.Value.Start, retained.Span.Value.Length), StringComparison.Ordinal);
            Assert.Equal("value retaining the conflicting loan", retained.Label);
        }

        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("value retaining the conflicting loan", console.Text, StringComparison.Ordinal);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity];
            Assert.Equal(result.Diagnostics.Length, sent.Length);
            for (var i = 0; i < sent.Length; i++)
            {
                Assert.Equal(result.Diagnostics[i].Display!.Range, sent[i].Range);
                Assert.Contains(result.Diagnostics[i].Message, sent[i].Message, StringComparison.Ordinal);
                if (related)
                {
                    Assert.Equal(result.Diagnostics[i].Related![0].Range, Assert.Single(sent[i].RelatedInformation!).Location.Range);
                }
                else
                {
                    Assert.Contains("value retaining the conflicting loan", sent[i].Message, StringComparison.Ordinal);
                }
            }
        }

        Assert.False(c.Emission.Validate(out _));
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("ref")]
    [InlineData("uniq")]
    public void NamedInputBorrowAnalysisReusesItsStorage(string mode)
    {
        var c = MinimalEmissionTest.Analyze("func run(value: uniq/i32 during a)\n    let found = value@follow@" + mode + "\n    require found == 42 else => $abort(\"value\")\n    value@follow = 99");
        for (var i = 0; i < 8; i++)
        {
            Assert.True(c.Ownership.Analyze().IsVerified);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Ownership.Analyze()));
    }
}
